using System.Net.Http.Headers;

namespace Honua.Mobile.Sdk;

public sealed class HonuaMobileClientOptions
{
    public Uri BaseUri { get; init; } = new("https://api.honua.io");

    public string? ApiKey { get; init; }

    public string? BearerToken { get; init; }

    public Func<CancellationToken, ValueTask<string?>>? AccessTokenProvider { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public ProductInfoHeaderValue UserAgent { get; init; } = new("honua-mobile-sdk", "0.1.0");
}
