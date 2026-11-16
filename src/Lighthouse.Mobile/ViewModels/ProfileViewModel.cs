using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Auth;
using Lighthouse.Mobile.Services.Sync;

namespace Lighthouse.Mobile.ViewModels;

/// <summary>Профіль: ім'я, пошта, мова, стан синхронізації й вихід. Решта налаштувань (сповіщення, календар, резервні копії) — у P4.4–P5.</summary>
public sealed partial class ProfileViewModel(IAuth auth, ISyncSvc sync, LocalizationService loc) : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _initial = "?";
    [ObservableProperty] private bool _isUkrainian = true;
    [ObservableProperty] private bool _isEnglish;
    [ObservableProperty] private string _syncSummary = string.Empty;
    [ObservableProperty] private string _syncDetail = string.Empty;
    [ObservableProperty] private bool _hasParked;

    public async Task LoadAsync()
    {
        var profile = await auth.GetProfileAsync();
        Name = profile.Name;
        Email = profile.Email;
        Initial = Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        RefreshLanguageFlags();
        await RefreshSyncAsync();
    }

    [RelayCommand]
    private void SetUkrainian()
    {
        loc.SetLanguage(LocalizationService.Ukrainian);
        RefreshLanguageFlags();
        _ = RefreshSyncAsync();
    }

    [RelayCommand]
    private void SetEnglish()
    {
        loc.SetLanguage(LocalizationService.English);
        RefreshLanguageFlags();
        _ = RefreshSyncAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        await sync.SyncNowAsync();
        await RefreshSyncAsync();
    }

    [RelayCommand]
    private async Task RetryParkedAsync()
    {
        await sync.RetryParkedAsync();
        await RefreshSyncAsync();
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        var pending = await sync.GetPendingCountAsync();
        if (pending > 0)
        {
            var confirmed = await Shell.Current.DisplayAlertAsync(
                loc["SignOut_Title"], loc.Get("SignOut_Pending", pending),
                loc["Btn_SignOutConfirm"], loc["Btn_Cancel"]);
            if (!confirmed) return;
        }

        await auth.LogoutAsync();
        await Shell.Current.GoToAsync("//welcome");
    }
    
    [RelayCommand]
    private async Task FullResyncAsync()
    {
        await sync.FullResyncAsync();
        await RefreshSyncAsync();
    }

    private async Task RefreshSyncAsync()
    {
        var d = await sync.GetDiagnosticsAsync();
        var last = d.LastSuccessUtc is { } t
            ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : loc["Sync_Never"];

        SyncSummary = d.Pending == 0 && d.Parked == 0
            ? loc.Get("Sync_AllSynced", last)
            : loc.Get("Sync_Waiting", d.Pending + d.Parked, last);

        var detail = d.Parked > 0 ? loc.Get("Sync_Parked", d.Parked) : string.Empty;
        if (d.LastError is { } error)
        {
            detail = (detail + " " + error).Trim();
        }

        SyncDetail = detail;
        HasParked = d.Parked > 0;
    }

    private void RefreshLanguageFlags()
    {
        IsUkrainian = loc.Language == LocalizationService.Ukrainian;
        IsEnglish = !IsUkrainian;
    }
}