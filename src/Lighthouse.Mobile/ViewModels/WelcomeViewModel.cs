using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class WelcomeViewModel : ObservableObject
{
    [RelayCommand]
    private Task GoToLoginAsync() => Shell.Current.GoToAsync(nameof(Views.LoginPage));
}