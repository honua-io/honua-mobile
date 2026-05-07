using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Honua.Mobile.PlatformSmoke;

public sealed class App : Application
{
    private readonly PlatformSmokeRunner _runner;
    private Label? _statusLabel;

    public App(PlatformSmokeRunner runner)
    {
        _runner = runner;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _statusLabel = new Label
        {
            Text = "Running Honua platform smoke...",
            FontSize = 16,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var page = new ContentPage
        {
            Content = new Grid
            {
                Padding = 24,
                Children = { _statusLabel },
            },
        };

        _ = RunSmokeAsync();
        return new Window(page);
    }

    private async Task RunSmokeAsync()
    {
        var result = await _runner.RunAsync().ConfigureAwait(false);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_statusLabel is not null)
            {
                _statusLabel.Text = result.Success
                    ? $"Honua platform smoke passed in {result.ElapsedMilliseconds} ms."
                    : $"Honua platform smoke failed: {result.ErrorMessage}";
            }
        });

        if (!result.Success)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            Environment.Exit(1);
        }
    }
}
