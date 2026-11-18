using Lighthouse.Mobile.Services.Notifications;

namespace Lighthouse.Mobile;

public partial class App : Application
{
    public App(INotif notif, INotifSettingsSync settingsSync)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark; // застосунок лише в темній темі: системні діалоги теж темні
        notif.Start();
        settingsSync.Start();
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}