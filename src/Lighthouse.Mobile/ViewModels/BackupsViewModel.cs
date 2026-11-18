using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Backups;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Mobile.ViewModels;

public sealed class BackupItem
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public DateTime CreatedLocal { get; init; }
}

public sealed partial class BackupsViewModel(
    IBackup backup, ISyncSvc sync, IConnectivity connectivity, LocalizationService loc) : ObservableObject
{
    private const int MaxImportBytes = 5 * 1024 * 1024;

    private static readonly FilePickerFileType ImportTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = new[]
        {
            "application/json", "text/csv", "text/comma-separated-values", "text/plain", "application/octet-stream"
        }
    });

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string? _infoMessage;
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<BackupItem> Items { get; } = [];

    public Task LoadAsync() => GuardAsync(RefreshListAsync);

    [RelayCommand]
    private Task CreateAsync() => GuardAsync(async () =>
    {
        using var cts = Timeout();
        await backup.CreateAsync(cts.Token);
        await RefreshListAsync();
        InfoMessage = loc["Backup_Created"];
    });

    public async Task RestoreAsync(BackupItem item)
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            loc["Backup_RestoreTitle"], loc.Get("Backup_RestoreConfirm", item.CreatedLocal.ToString("g")),
            loc["Backup_Restore"], loc["Btn_Cancel"]);
        if (!confirmed)
        {
            return;
        }

        await GuardAsync(async () =>
        {
            // Спершу надсилаємо власні зміни, щоб відновлення не знищило ненадіслане.
            await sync.SyncNowAsync();
            if (await sync.GetPendingCountAsync() > 0)
            {
                ErrorMessage = loc["Backup_PendingChanges"];
                return;
            }

            using var cts = Timeout();
            var result = await backup.RestoreAsync(item.Id, cts.Token);
            await sync.SyncNowAsync(); // отримуємо відновлені записи й «надгробки»
            await RefreshListAsync();
            InfoMessage = loc.Get("Backup_Restored", result.Tasks, result.Habits, result.Categories);
        });
    }

    public async Task DeleteAsync(BackupItem item)
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            loc["Backup_DeleteTitle"], loc.Get("Backup_DeleteConfirm", item.CreatedLocal.ToString("g")),
            loc["Btn_Delete"], loc["Btn_Cancel"]);
        if (!confirmed)
        {
            return;
        }

        await GuardAsync(async () =>
        {
            using var cts = Timeout();
            await backup.DeleteAsync(item.Id, cts.Token);
            await RefreshListAsync();
        });
    }

    [RelayCommand]
    private Task ExportAsync(string format) => GuardAsync(async () =>
    {
        using var cts = Timeout();
        var path = await backup.ExportAsync(format, cts.Token);
        await Share.Default.RequestAsync(new ShareFileRequest(loc["Backup_Title"], new ShareFile(path)));
    });

    [RelayCommand]
    private async Task ImportAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = loc["Import_Pick"], FileTypes = ImportTypes
        });
        if (file is null)
        {
            return;
        }

        await GuardAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length > MaxImportBytes)
            {
                ErrorMessage = loc["Import_TooBig"];
                return;
            }

            var bytes = buffer.ToArray();
            using var cts = Timeout();
            var result = await backup.ImportAsync(DetectFormat(file.FileName, bytes), bytes, cts.Token);
            await sync.SyncNowAsync();

            var message = loc.Get("Import_Done", result.Tasks, result.Habits, result.Categories, result.Skipped);
            InfoMessage = result.Errors.Count > 0 ? message + " " + string.Join(" ", result.Errors.Take(3)) : message;
        });
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    private async Task RefreshListAsync()
    {
        using var cts = Timeout();
        var list = await backup.ListAsync(cts.Token);

        Items.Clear();
        foreach (var info in list)
        {
            var created = DateTime.SpecifyKind(info.CreatedAtUtc, DateTimeKind.Utc).ToLocalTime();
            Items.Add(new BackupItem
            {
                Id = info.Id,
                CreatedLocal = created,
                Title = created.ToString("g"),
                Detail = $"{(info.Kind == BackupKind.Auto ? loc["Backup_Auto"] : loc["Backup_Manual"])} · {Math.Max(1, info.SizeBytes / 1024)} KB"
            });
        }

        IsEmpty = Items.Count == 0;
    }

    /// <summary>Спільна обгортка: перевірка мережі, стан зайнятості й зрозумілі повідомлення про помилки.</summary>
    private async Task GuardAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        InfoMessage = null;
        if (connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            ErrorMessage = loc["Backup_NoNetwork"];
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            ErrorMessage = loc["Backup_Conflict"];
        }
        catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
        {
            ErrorMessage = loc["Backup_BadFile"];
        }
        catch (ApiException ex) when (ex.StatusCode == (HttpStatusCode)429)
        {
            ErrorMessage = loc["Err_TooManyRequests"];
        }
        catch (Exception)
        {
            ErrorMessage = loc["Err_NoConnection"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static CancellationTokenSource Timeout() => new(TimeSpan.FromSeconds(30));

    private static string DetectFormat(string fileName, byte[] bytes)
    {
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return "csv";
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "json";

        var first = bytes.SkipWhile(b => b is 0xEF or 0xBB or 0xBF or (byte)' ' or (byte)'\r' or (byte)'\n')
            .FirstOrDefault();
        return first == (byte)'{' ? "json" : "csv";
    }
}