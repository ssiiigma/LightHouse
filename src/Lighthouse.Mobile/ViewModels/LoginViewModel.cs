using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Auth;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class LoginViewModel(ApiClient api, ITokenStore tokenStore) : ObservableObject
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
        IsBusy = true;
        try
        {
            var auth = await api.LoginAsync(Email.Trim(), Password, CancellationToken.None);
            await tokenStore.SaveAsync(auth.AccessToken, auth.RefreshToken);
            ErrorMessage = $"Успішний вхід: {auth.User.Name}"; // тимчасово, замінимо на навігацію пізніше
        }
        catch (ApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized)
        {
            ErrorMessage = "Невірний email або пароль.";
        }
        catch (ApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Locked)
        {
            ErrorMessage = "Забагато спроб входу. Спробуйте пізніше.";
        }
        catch (Exception)
        {
            ErrorMessage = "Немає з'єднання з сервером. Перевірте інтернет і спробуйте ще раз.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");
}