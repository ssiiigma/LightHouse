using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Auth;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class RegisterViewModel(IAuth auth, LocalizationService loc) : ObservableObject
{
    private const int MinPasswordLength = 8; // збігається з валідацією сервера

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy) return;

        ErrorMessage = Validate();
        if (ErrorMessage is not null) return;

        IsBusy = true;
        try
        {
            await auth.RegisterAsync(Name.Trim(), Email.Trim(), Password);
            await Shell.Current.GoToAsync("//tasks");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            ErrorMessage = loc["Err_EmailTaken"];
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            ErrorMessage = loc["Err_CheckData"];
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

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return loc["Err_EnterName"];
        if (!Email.Contains('@') || Email.Trim().Length < 5) return loc["Err_InvalidEmail"];
        if (Password.Length < MinPasswordLength) return loc.Get("Err_PasswordShort", MinPasswordLength);
        if (Password != ConfirmPassword) return loc["Err_PasswordMismatch"];
        return null;
    }
}