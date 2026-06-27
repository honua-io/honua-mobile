using System.Text.Json;
using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.Sdk.Tests;

public sealed class LegacyFeatureQueryEnvelopeTests
{
    [Fact]
    public void ToLegacyFeatureQueryJsonDocument_NormalQuery_OmitsTopLevelCount()
    {
        var result = new FeatureQueryResult
        {
            NumberMatched = 42,
            Features =
            [
                new FeatureRecord { Id = "1" },
            ],
        };

        using var document = HonuaMobileClient.ToLegacyFeatureQueryJsonDocument(result, returnCountOnly: false);

        // Esri reserves the top-level "count" for returnCountOnly responses; a normal feature
        // query must not emit it alongside features.
        Assert.False(document.RootElement.TryGetProperty("count", out _));
        Assert.True(document.RootElement.TryGetProperty("features", out var features));
        Assert.Equal(1, features.GetArrayLength());
    }

    [Fact]
    public void ToLegacyFeatureQueryJsonDocument_ReturnCountOnly_EmitsTopLevelCount()
    {
        var result = new FeatureQueryResult
        {
            NumberMatched = 42,
            Features = [],
        };

        using var document = HonuaMobileClient.ToLegacyFeatureQueryJsonDocument(result, returnCountOnly: true);

        Assert.True(document.RootElement.TryGetProperty("count", out var count));
        Assert.Equal(42, count.GetInt64());
    }
}

public sealed class GrpcEditFallbackOptionTests
{
    [Fact]
    public void AllowRestFallbackOnGrpcEditFailure_DefaultsToOff()
    {
        // Queries are safe to retry over REST, but edits are not idempotent, so the edit-specific
        // fallback must default off to avoid double-applying an edit that already reached the server.
        var options = new HonuaMobileClientOptions();

        Assert.True(options.AllowRestFallbackOnGrpcFailure);
        Assert.False(options.AllowRestFallbackOnGrpcEditFailure);
    }
}
