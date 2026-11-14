using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Auth;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class LoginViewModel(IAuth auth, LocalizationService loc) : ObservableObject
{
    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;

        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = loc["Err_EnterCredentials"];
            return;
        }

        IsBusy = true;
        try
        {
            await auth.LoginAsync(Email.Trim(), Password);
            await Shell.Current.GoToAsync("//tasks");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            ErrorMessage = loc["Err_InvalidCredentials"];
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Locked)
        {
            ErrorMessage = loc["Err_Locked"];
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

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");
}