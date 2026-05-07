using Microsoft.Extensions.Logging;

namespace Honua.Mobile.PlatformSmoke;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .Services.AddSingleton<PlatformSmokeRunner>();

        builder.Logging.AddDebug();

        return builder.Build();
    }
}
