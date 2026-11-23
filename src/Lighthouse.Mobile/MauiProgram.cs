using CommunityToolkit.Maui;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Auth;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Mobile.Services.Tasks;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification; 
using Lighthouse.Mobile.Services.Notifications;

namespace Lighthouse.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("DMSans-Regular.ttf", "DMSans");
            fonts.AddFont("DMSans-Medium.ttf", "DMSansMedium");
            fonts.AddFont("DMSans-SemiBold.ttf", "DMSansSemibold");
            fonts.AddFont("DMSans-Bold.ttf", "DMSansBold");
            fonts.AddFont("CormorantGaramond-Light.ttf", "CormorantLight");
        }).UseLocalNotification().UseMauiCommunityToolkit();

        // Інфраструктура: екрани → сервіси → інфраструктура, без обходів.
        builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);
        builder.Services.AddSingleton<ISecureStore, SecureStorageService>();
        builder.Services.AddSingleton<ILocalDB, LocalDB>();
        builder.Services.AddSingleton(LocalizationService.Instance);
        builder.Services.AddSingleton<IApiClient>(sp => new ApiClient(
            new HttpClient
            {
                BaseAddress = new Uri(ApiOptions.BaseUrl),
                Timeout = TimeSpan.FromSeconds(15)
            },
            sp.GetRequiredService<ISecureStore>()));

        // Сервіси.
        builder.Services.AddSingleton<IEntitySyncHandler, CategorySyncHandler>();
        builder.Services.AddSingleton<IEntitySyncHandler, TaskSyncHandler>();
        builder.Services.AddSingleton<ISyncSvc, SyncService>();
        builder.Services.AddSingleton<IAuth, AuthManager>();
        builder.Services.AddSingleton<ITaskSvc, TaskService>();
        builder.Services.AddSingleton<IEntitySyncHandler, HabitSyncHandler>();
        builder.Services.AddSingleton<IEntitySyncHandler, HabitLogSyncHandler>();
        builder.Services.AddSingleton<Services.Habits.IHabitSvc, Services.Habits.HabitService>();
        builder.Services.AddSingleton<Services.Analytics.IAnalyticsSvc, Services.Analytics.AnalyticsService>();
        builder.Services.AddSingleton<IEntitySyncHandler, FeedbackSyncHandler>();
        builder.Services.AddSingleton<Services.AI.IAIClient, Services.AI.AIClientService>();
        builder.Services.AddSingleton<ILocalNotif, LocalNotifier>();
        builder.Services.AddSingleton<ISystemSettings, AndroidSystemSettings>();
        builder.Services.AddSingleton<INotifSettings, NotificationSettingsStore>();
        builder.Services.AddSingleton<INotif, NotifManager>();
        builder.Services.AddSingleton<Services.Backups.IBackup, Services.Backups.BackupService>();
        builder.Services.AddSingleton<INotifSettingsSync, NotifSettingsSync>();
        builder.Services.AddSingleton<IGoogleAuth, GoogleAuthService>();
        builder.Services.AddSingleton<Services.Calendar.ICalendar, Services.Calendar.CalendarService>();
        builder.Services.AddSingleton<Services.Push.IPushTokenSource, AndroidPushTokenSource>();
        builder.Services.AddSingleton<Services.Push.IPushSvc, Services.Push.PushService>();

        // Екрани.
        builder.Services.AddTransient<ViewModels.WelcomeViewModel>();
        builder.Services.AddTransient<ViewModels.LoginViewModel>();
        builder.Services.AddTransient<ViewModels.RegisterViewModel>();
        builder.Services.AddTransient<ViewModels.TasksViewModel>();
        builder.Services.AddTransient<ViewModels.ProfileViewModel>();
        builder.Services.AddTransient<WelcomePage>();
        builder.Services.AddTransient<Views.LoginPage>();
        builder.Services.AddTransient<Views.RegisterPage>();
        builder.Services.AddTransient<Views.TasksPage>();
        builder.Services.AddTransient<Views.ProfilePage>();
        builder.Services.AddTransient<ViewModels.TaskEditViewModel>();
        builder.Services.AddTransient<Views.TaskEditPage>();
        builder.Services.AddTransient<ViewModels.HabitsViewModel>();
        builder.Services.AddTransient<ViewModels.HabitEditViewModel>();
        builder.Services.AddTransient<Views.HabitsPage>();
        builder.Services.AddTransient<Views.HabitEditPage>();
        builder.Services.AddTransient<ViewModels.AnalyticsViewModel>();
        builder.Services.AddTransient<Views.AnalyticsPage>();
        builder.Services.AddTransient<ViewModels.AdviceBannerViewModel>();
        builder.Services.AddTransient<ViewModels.CategoriesViewModel>();
        builder.Services.AddTransient<Views.CategoriesPage>();
        builder.Services.AddTransient<ViewModels.ForgotPasswordViewModel>();
        builder.Services.AddTransient<Views.ForgotPasswordPage>();
        builder.Services.AddTransient<ViewModels.BackupsViewModel>();
        builder.Services.AddTransient<Views.BackupsPage>();
        builder.Services.AddTransient<ViewModels.CalendarViewModel>();
        builder.Services.AddTransient<Views.CalendarPage>();
        
#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}