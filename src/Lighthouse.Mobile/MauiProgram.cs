using CommunityToolkit.Maui;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Auth;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Mobile.Services.Tasks;
using Microsoft.Extensions.Logging;

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
        }).UseMauiCommunityToolkit();

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
#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}