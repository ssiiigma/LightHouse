using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Calendar;
using Lighthouse.Mobile.Services.Sync;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class CalendarViewModel(ICalendar calendar, IConnectivity connectivity, 
    LocalizationService loc, ISyncSvc sync)
    : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private bool _isConnected;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private string? _infoMessage;
    [ObservableProperty] private string? _errorMessage;

    public bool IsDisconnected => !IsConnected;

    /// <summary>Оновлює стан підключення (при відкритті екрана й після повернення з браузера).</summary>
    public Task RefreshAsync() => GuardAsync(RefreshStatusAsync, keepMessages: true);

    [RelayCommand]
    private Task ConnectAsync() => GuardAsync(async () =>
    {
        using var cts = Timeout();
        var url = await calendar.BeginConnectAsync(cts.Token);
        await Browser.Default.OpenAsync(new Uri(url), BrowserLaunchMode.SystemPreferred);
        InfoMessage = loc["Calendar_Finish"];
    });

    [RelayCommand]
    private Task SyncNowAsync() => GuardAsync(async () =>
    {
        // Спершу надсилаємо на сервер зміни пристрою: календар синхронізується з тим, що вже є на сервері.
        await sync.SyncNowAsync();
        if (await sync.GetPendingCountAsync() > 0)
        {
            ErrorMessage = loc["Calendar_PendingChanges"];
            return;
        }

        using var cts = Timeout();
        var result = await calendar.SyncNowAsync(cts.Token);
        await RefreshStatusAsync();
        var message = loc.Get("Calendar_SyncResult", result.Created, result.Updated, result.Deleted);
        InfoMessage = result.Remaining > 0 ? message + loc.Get("Calendar_SyncRemaining", result.Remaining) : message;
    });

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            loc["Calendar_DisconnectTitle"], loc["Calendar_DisconnectConfirm"],
            loc["Calendar_Disconnect"], loc["Btn_Cancel"]);
        if (!confirmed)
        {
            return;
        }

        await GuardAsync(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // видалення подій може тривати
            await calendar.DisconnectAsync(cts.Token);
            await RefreshStatusAsync();
        });
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    private async Task RefreshStatusAsync()
    {
        using var cts = Timeout();
        var status = await calendar.GetStatusAsync(cts.Token);
        IsConnected = status.Connected;
        StatusText = status.Connected ? loc["Calendar_Connected"] : loc["Calendar_NotConnected"];
        DetailText = status.Connected ? loc.Get("Calendar_Events", status.EventCount) : string.Empty;
    }

    private async Task GuardAsync(Func<Task> action, bool keepMessages = false)
    {
        if (IsBusy)
        {
            return;
        }

        if (!keepMessages)
        {
            InfoMessage = null;
        }

        ErrorMessage = null;
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
            ErrorMessage = loc["Calendar_NotConnectedError"];
        }
        catch (ApiException ex) when (ex.StatusCode == (HttpStatusCode)429)
        {
            ErrorMessage = loc["Err_TooManyRequests"];
        }
        catch (ApiException ex) when ((int)ex.StatusCode >= 500)
        {
            ErrorMessage = loc["Calendar_SyncFailed"];
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
}