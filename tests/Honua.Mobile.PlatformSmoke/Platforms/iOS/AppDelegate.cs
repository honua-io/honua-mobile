using Foundation;

namespace Honua.Mobile.PlatformSmoke;

[Register("AppDelegate")]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();
}
