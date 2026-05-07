using System.Diagnostics;
using System.Text.Json;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Features;
using Honua.Sdk.Abstractions.Features;
using Microsoft.Maui.Devices;

namespace Honua.Mobile.PlatformSmoke;

internal sealed class PlatformSmokeRunner
{
    public const string ConfigFileName = "honua-mobile-platform-smoke-config.json";
    public const string ResultFileName = "honua-mobile-platform-smoke-result.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<PlatformSmokeResult> RunAsync()
    {
        PlatformSmokeConfiguration? configuration = null;

        try
        {
            configuration = PlatformSmokeConfiguration.Load();
            var result = await QueryFeatureLayerAsync(configuration).ConfigureAwait(false);
            await WriteResultAsync(result, configuration.ResultDirectory).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            var result = PlatformSmokeResult.Failed(
                platform: DeviceInfo.Platform.ToString(),
                elapsedMilliseconds: 0,
                errorMessage: ex.Message,
                errorType: ex.GetType().FullName ?? ex.GetType().Name);

            await WriteResultAsync(result, configuration?.ResultDirectory ?? PlatformSmokeConfiguration.DefaultResultDirectory)
                .ConfigureAwait(false);
            return result;
        }
    }

    private static async Task<PlatformSmokeResult> QueryFeatureLayerAsync(PlatformSmokeConfiguration configuration)
    {
        using var http = new HttpClient();
        using var client = new HonuaMobileClient(http, new HonuaMobileClientOptions
        {
            BaseUri = configuration.BaseUri,
            ApiKey = configuration.ApiKey,
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
            Timeout = TimeSpan.FromSeconds(5),
        });

        IHonuaFeatureQueryClient featureClient = new HonuaMobileSdkFeatureClient(client);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var stopwatch = Stopwatch.StartNew();
        FeatureQueryResult query;
        try
        {
            query = await featureClient.QueryAsync(new FeatureQueryRequest
            {
                Source = new FeatureSource
                {
                    ServiceId = configuration.ServiceId,
                    LayerId = configuration.LayerId,
                },
                Limit = 1,
            }, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            stopwatch.Stop();
            return PlatformSmokeResult.Failed(
                platform: DeviceInfo.Platform.ToString(),
                elapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                errorMessage: $"Live feature query exceeded the 1 second smoke budget: {stopwatch.Elapsed}.",
                errorType: "SmokeBudgetExceeded");
        }

        stopwatch.Stop();

        if (stopwatch.Elapsed >= TimeSpan.FromSeconds(1))
        {
            return PlatformSmokeResult.Failed(
                platform: DeviceInfo.Platform.ToString(),
                elapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                errorMessage: $"Live feature query exceeded the 1 second smoke budget: {stopwatch.Elapsed}.",
                errorType: "SmokeBudgetExceeded");
        }

        return PlatformSmokeResult.Passed(
            platform: DeviceInfo.Platform.ToString(),
            elapsedMilliseconds: stopwatch.ElapsedMilliseconds,
            featureCount: query.Features.Count,
            providerName: featureClient.ProviderName);
    }

    private static async Task WriteResultAsync(PlatformSmokeResult result, string directory)
    {
        Directory.CreateDirectory(directory);
        var resultPath = Path.Combine(directory, ResultFileName);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        await File.WriteAllTextAsync(resultPath, json).ConfigureAwait(false);
    }
}
