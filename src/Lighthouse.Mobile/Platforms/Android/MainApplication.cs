using Android.App;
using Android.Runtime;

namespace Lighthouse.Mobile;

#if DEBUG
// Незашифрований HTTP дозволено лише в Debug (емулятор → http://10.0.2.2:5080).
[Application(UsesCleartextTraffic = true)]
#else
[Application]
#endif
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}