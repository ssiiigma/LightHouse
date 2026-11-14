using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Services.Auth;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class WelcomeViewModel(IAuth auth) : ObservableObject
{
    [RelayCommand]
    private Task GoToLoginAsync() => Shell.Current.GoToAsync(nameof(Views.LoginPage));

    [RelayCommand]
    private Task GoToRegisterAsync() => Shell.Current.GoToAsync(nameof(Views.RegisterPage));

    /// <summary>Якщо сеанс збережено, одразу відкриває список завдань.</summary>
    public async Task CheckSessionAsync()
    {
        if (await auth.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync("//tasks");
        }
    }
}