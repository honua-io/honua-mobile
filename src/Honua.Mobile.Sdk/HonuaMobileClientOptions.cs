using System.Net.Http.Headers;

namespace Honua.Mobile.Sdk;

public sealed class HonuaMobileClientOptions
{
    public Uri BaseUri { get; init; } = new("https://api.honua.io");

    public Uri? GrpcEndpoint { get; init; }

    public bool PreferGrpcForFeatureQueries { get; init; } = true;

    public bool PreferGrpcForFeatureEdits { get; init; } = true;

    public bool AllowRestFallbackOnGrpcFailure { get; init; } = true;

    public string? ApiKey { get; init; }

    public string? BearerToken { get; init; }

    public Func<CancellationToken, ValueTask<string?>>? AccessTokenProvider { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public ProductInfoHeaderValue UserAgent { get; init; } = new("honua-mobile-sdk", "0.1.0");
}
