namespace Honua.Mobile.FieldCollection;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell())
        {
            Title = "Honua Field Collection"
        };

#if WINDOWS || MACCATALYST
        window.MinimumHeight = 600;
        window.MinimumWidth = 800;
#endif

        return window;
    }

    protected override void OnStart()
    {
        // App started
    }

    protected override void OnSleep()
    {
        // App went to sleep
        // Save any pending data
    }

    protected override void OnResume()
    {
        // App resumed from sleep
        // Check for pending sync operations
    }
}
