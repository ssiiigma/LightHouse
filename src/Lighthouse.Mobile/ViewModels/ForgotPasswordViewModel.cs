using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Auth;

namespace Lighthouse.Mobile.ViewModels;

/// <summary>Відновлення пароля: крок 1 — запит листа, крок 2 — код з листа й новий пароль.</summary>
public sealed partial class ForgotPasswordViewModel(IAuth auth, LocalizationService loc) : ObservableObject
{
    private const int MinPasswordLength = 8; // збігається з валідацією сервера

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStepOne))]
    private bool _isStepTwo;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _token = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string? _infoMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    public bool IsStepOne => !IsStepTwo;

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsBusy) return;

        ErrorMessage = null;
        InfoMessage = null;
        if (!Email.Contains('@') || Email.Trim().Length < 5)
        {
            ErrorMessage = loc["Err_InvalidEmail"];
            return;
        }

        IsBusy = true;
        try
        {
            await auth.RequestPasswordResetAsync(Email.Trim());
            InfoMessage = loc["Forgot_Sent"];
            IsStepTwo = true;
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
    private void HaveCode()
    {
        ErrorMessage = null;
        InfoMessage = null;
        IsStepTwo = true;
    }

    [RelayCommand]
    private void BackToStepOne()
    {
        ErrorMessage = null;
        InfoMessage = null;
        IsStepTwo = false;
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        if (IsBusy) return;

        ErrorMessage = null;
        InfoMessage = null;
        if (string.IsNullOrWhiteSpace(Token)) { ErrorMessage = loc["Err_EnterCode"]; return; }
        if (NewPassword.Length < MinPasswordLength) { ErrorMessage = loc.Get("Err_PasswordShort", MinPasswordLength); return; }
        if (NewPassword != ConfirmPassword) { ErrorMessage = loc["Err_PasswordMismatch"]; return; }

        IsBusy = true;
        try
        {
            await auth.ResetPasswordAsync(Token.Trim(), NewPassword);
            await Shell.Current.DisplayAlertAsync(loc["Forgot_DoneTitle"], loc["Forgot_Done"], loc["Btn_Ok"]);
            await Shell.Current.GoToAsync(".."); // назад до екрана входу
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            ErrorMessage = loc["Err_ResetInvalid"];
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