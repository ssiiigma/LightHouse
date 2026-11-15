using Lighthouse.Mobile.Localization;

namespace Lighthouse.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(Views.LoginPage), typeof(Views.LoginPage));
        Routing.RegisterRoute(nameof(Views.RegisterPage), typeof(Views.RegisterPage));
        Routing.RegisterRoute(nameof(Views.TaskEditPage), typeof(Views.TaskEditPage));
        Routing.RegisterRoute(nameof(Views.HabitEditPage), typeof(Views.HabitEditPage));

        ApplyTitles();
        LocalizationService.Instance.PropertyChanged += (_, _) => ApplyTitles();
    }

    private void ApplyTitles()
    {
        var loc = LocalizationService.Instance;
        TasksTab.Title = loc["Tab_Tasks"];
        ProfileTab.Title = loc["Tab_Profile"];
        AnalyticsTab.Title = loc["Tab_Analytics"];
        HabitsTab.Title = loc["Tab_Habits"];
    }
}