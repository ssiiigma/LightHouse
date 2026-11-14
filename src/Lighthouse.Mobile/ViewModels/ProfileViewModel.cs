using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Auth;
using Lighthouse.Mobile.Services.Sync;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class ProfileViewModel(IAuth auth, ISyncSvc sync, LocalizationService loc) : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _initial = "?";

    [ObservableProperty]
    private bool _isUkrainian = true;

    [ObservableProperty]
    private bool _isEnglish;

    public async Task LoadAsync()
    {
        var profile = await auth.GetProfileAsync();
        Name = profile.Name;
        Email = profile.Email;
        Initial = Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
        RefreshLanguageFlags();
    }

    [RelayCommand]
    private void SetUkrainian()
    {
        loc.SetLanguage(LocalizationService.Ukrainian);
        RefreshLanguageFlags();
    }

    [RelayCommand]
    private void SetEnglish()
    {
        loc.SetLanguage(LocalizationService.English);
        RefreshLanguageFlags();
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        var pending = await sync.GetPendingCountAsync();
        if (pending > 0)
        {
            var confirmed = await Shell.Current.DisplayAlert(
                loc["SignOut_Title"], loc.Get("SignOut_Pending", pending),
                loc["Btn_SignOutConfirm"], loc["Btn_Cancel"]);
            if (!confirmed) return;
        }

        await auth.LogoutAsync();
        await Shell.Current.GoToAsync("//welcome");
    }

    private void RefreshLanguageFlags()
    {
        IsUkrainian = loc.Language == LocalizationService.Ukrainian;
        IsEnglish = !IsUkrainian;
    }
}