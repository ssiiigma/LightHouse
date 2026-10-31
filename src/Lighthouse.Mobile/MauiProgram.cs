using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;

namespace Lighthouse.Mobile;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        }).UseMauiCommunityToolkit();
        
        builder.Services.AddSingleton<Services.Auth.ITokenStore, Services.Auth.SecureTokenStore>();
        builder.Services.AddHttpClient<Services.Api.ApiClient>(client =>
        {
            client.BaseAddress = new Uri(Services.Api.ApiOptions.BaseUrl);
        });
        builder.Services.AddTransient<ViewModels.WelcomeViewModel>();
        builder.Services.AddTransient<ViewModels.LoginViewModel>();
        builder.Services.AddTransient<WelcomePage>();
        builder.Services.AddTransient<Views.LoginPage>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        
        return builder.Build();
    }
}