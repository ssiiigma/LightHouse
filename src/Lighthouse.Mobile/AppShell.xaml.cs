namespace Lighthouse.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(Views.LoginPage), typeof(Views.LoginPage));
    }
}