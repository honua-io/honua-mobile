namespace Honua.Mobile.FieldCollection;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new AppShell();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        if (window != null)
        {
            // Configure window properties
            window.Title = "Honua Field Collection";

            // Set minimum window size for desktop platforms
#if WINDOWS || MACCATALYST
            window.MinimumHeight = 600;
            window.MinimumWidth = 800;
#endif
        }

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
