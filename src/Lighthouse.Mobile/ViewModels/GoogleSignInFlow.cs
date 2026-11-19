using System.Net;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Auth;

namespace Lighthouse.Mobile.ViewModels;

/// <summary>Спільний сценарій входу через Google для екранів входу й реєстрації.</summary>
internal static class GoogleSignInFlow
{
    /// <returns>null — успіх або скасування; інакше локалізоване повідомлення про помилку.</returns>
    public static async Task<string?> RunAsync(IAuth auth, LocalizationService loc)
    {
        try
        {
            if (await auth.LoginWithGoogleAsync())
            {
                await Shell.Current.GoToAsync("//tasks");
            }

            return null;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return loc["Err_GoogleConflict"];
        }
        catch (ApiException ex) when (ex.StatusCode == (HttpStatusCode)429)
        {
            return loc["Err_TooManyRequests"];
        }
        catch (Exception)
        {
            return loc["Err_GoogleFailed"];
        }
    }
}