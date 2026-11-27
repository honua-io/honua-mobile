namespace Honua.Mobile.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Resolve the page after the application resources used by its XAML are loaded.
        return new Window(_services.GetRequiredService<MainPage>());
    }
}
