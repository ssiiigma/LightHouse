using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Auth;
using Lighthouse.Mobile.Services.Notifications;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Shared.Notifications;

namespace Lighthouse.Mobile.ViewModels;

/// <summary>Профіль і налаштування: ім'я, пошта, пояс, мова, сповіщення й тихий режим, синхронізація, вихід.</summary>
public sealed partial class ProfileViewModel(
    IAuth auth, ISyncSvc sync, LocalizationService loc,
    INotifSettings notifSettings, INotif notif, ISystemSettings system) : ObservableObject
{
    private bool _loadingPrefs;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _initial = "?";
    [ObservableProperty] private string _timeZoneText = string.Empty;
    [ObservableProperty] private bool _isUkrainian = true;
    [ObservableProperty] private bool _isEnglish;

    [ObservableProperty] private bool _taskRemindersOn = true;
    [ObservableProperty] private bool _habitRemindersOn = true;
    [ObservableProperty] private bool _quietOn;
    [ObservableProperty] private TimeSpan? _quietStart = new TimeSpan(22, 0, 0);
    [ObservableProperty] private TimeSpan? _quietEnd = new TimeSpan(7, 0, 0);
    [ObservableProperty] private bool _notificationsBlocked;
    [ObservableProperty] private bool _exactBlocked;
    [ObservableProperty] private bool _batteryRestricted;

    [ObservableProperty] private string _syncSummary = string.Empty;
    [ObservableProperty] private string _syncDetail = string.Empty;
    [ObservableProperty] private bool _hasParked;

    partial void OnTaskRemindersOnChanged(bool value) => SavePrefs();
    partial void OnHabitRemindersOnChanged(bool value) => SavePrefs();
    partial void OnQuietOnChanged(bool value) => SavePrefs();
    partial void OnQuietStartChanged(TimeSpan? value) => SavePrefs();
    partial void OnQuietEndChanged(TimeSpan? value) => SavePrefs();

    public async Task LoadAsync()
    {
        var profile = await auth.GetProfileAsync();
        Name = profile.Name;
        Email = profile.Email;
        Initial = Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        RefreshTexts();

        _loadingPrefs = true;
        var prefs = notifSettings.Load();
        TaskRemindersOn = prefs.TaskReminders;
        HabitRemindersOn = prefs.HabitReminders;
        QuietOn = prefs.QuietEnabled;
        QuietStart = prefs.QuietStart;
        QuietEnd = prefs.QuietEnd;
        _loadingPrefs = false;

        await RefreshNotifStatusAsync();
        await RefreshSyncAsync();
    }

    private void SavePrefs()
    {
        if (_loadingPrefs)
        {
            return;
        }

        notifSettings.Save(new NotificationPrefs(
            TaskRemindersOn, HabitRemindersOn, QuietOn,
            QuietStart ?? new TimeSpan(22, 0, 0), QuietEnd ?? new TimeSpan(7, 0, 0)));
    }

    [RelayCommand]
    private void SetUkrainian()
    {
        loc.SetLanguage(LocalizationService.Ukrainian);
        RefreshTexts();
        _ = RefreshSyncAsync();
    }

    [RelayCommand]
    private void SetEnglish()
    {
        loc.SetLanguage(LocalizationService.English);
        RefreshTexts();
        _ = RefreshSyncAsync();
    }

    [RelayCommand]
    private Task OpenCategoriesAsync() => Shell.Current.GoToAsync(nameof(Views.CategoriesPage));

    [RelayCommand]
    private async Task GrantNotificationsAsync()
    {
        var granted = await notif.RequestPermissionAsync();
        if (!granted)
        {
            system.OpenAppNotificationSettings(); // повторний запит системою блокується — користувач вмикає вручну
        }

        await RefreshNotifStatusAsync();
    }

    [RelayCommand]
    private void GrantExact() => system.OpenExactAlarmSettings();

    [RelayCommand]
    private void OpenBattery() => system.OpenBatteryOptimizationSettings();

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        await sync.SyncNowAsync();
        await RefreshSyncAsync();
    }

    [RelayCommand]
    private async Task FullResyncAsync()
    {
        await sync.FullResyncAsync();
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

    private async Task RefreshNotifStatusAsync()
    {
        var status = await notif.GetStatusAsync();
        NotificationsBlocked = !status.Enabled;
        ExactBlocked = status.Enabled && !status.Exact;
        BatteryRestricted = !status.BatteryUnrestricted;
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

    private void RefreshTexts()
    {
        IsUkrainian = loc.Language == LocalizationService.Ukrainian;
        IsEnglish = !IsUkrainian;
        TimeZoneText = loc.Get("Profile_TimeZone", TimeZoneInfo.Local.Id);
    }
    
    [RelayCommand]
    private Task OpenBackupsAsync() => Shell.Current.GoToAsync(nameof(Views.BackupsPage));
}