using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Mobile.Sdk.Auth;

/// <summary>
/// Options for <see cref="RefreshingAuthTokenProvider"/>.
/// </summary>
public sealed class RefreshingAuthTokenProviderOptions
{
    /// <summary>
    /// Token refresh endpoint. When unset, refresh attempts return the current stored token.
    /// </summary>
    public Uri? RefreshEndpoint { get; init; }

    /// <summary>
    /// Clock used for expiration checks. Defaults to <see cref="TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Time before expiration when a bearer token should be refreshed. Defaults to 2 minutes.
    /// </summary>
    public TimeSpan RefreshSkew { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Token provider that stores tokens locally and can refresh bearer tokens through a mockable HTTP endpoint.
/// </summary>
public sealed class RefreshingAuthTokenProvider : IAuthTokenProvider
{
    private readonly IAuthTokenStore _store;
    private readonly HttpClient _http;
    private readonly RefreshingAuthTokenProviderOptions _options;

    /// <summary>
    /// Initializes a new <see cref="RefreshingAuthTokenProvider"/>.
    /// </summary>
    /// <param name="store">Secure token store.</param>
    /// <param name="http">HTTP client used for refresh requests.</param>
    /// <param name="options">Provider options.</param>
    public RefreshingAuthTokenProvider(
        IAuthTokenStore store,
        HttpClient http,
        RefreshingAuthTokenProviderOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? new RefreshingAuthTokenProviderOptions();
    }

    /// <inheritdoc />
    public async ValueTask<HonuaAuthToken?> GetTokenAsync(CancellationToken ct = default)
    {
        try
        {
            var token = await _store.ReadAsync(ct).ConfigureAwait(false);
            if (token is null)
            {
                return null;
            }

            var now = _options.TimeProvider.GetUtcNow();
            if (!token.ShouldRefresh(now, _options.RefreshSkew))
            {
                return token;
            }

            return await RefreshTokenAsync(ct).ConfigureAwait(false);
        }
        catch (HonuaMobileAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new HonuaMobileAuthException("Honua auth token resolution failed.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask<HonuaAuthToken?> RefreshTokenAsync(CancellationToken ct = default)
    {
        using var activity = MobileAuthTelemetry.ActivitySource.StartActivity("honua.mobile.auth.refresh", ActivityKind.Client);
        activity?.SetTag("auth.scheme", "bearer");

        try
        {
            var current = await _store.ReadAsync(ct).ConfigureAwait(false);
            if (current is null || string.IsNullOrWhiteSpace(current.RefreshToken) || _options.RefreshEndpoint is null)
            {
                MobileAuthTelemetry.RecordTokenRefresh("skipped");
                activity?.SetTag("auth.refresh.result", "skipped");
                return current;
            }

            using var content = JsonContent.Create(
                new RefreshTokenRequest(current.RefreshToken),
                HonuaMobileAuthJsonContext.Default.RefreshTokenRequest);
            using var response = await _http.PostAsync(_options.RefreshEndpoint, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                MobileAuthTelemetry.RecordTokenRefresh("failure");
                activity?.SetStatus(ActivityStatusCode.Error);
                throw new HonuaMobileAuthException("Honua auth token refresh failed.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(
                stream,
                HonuaMobileAuthJsonContext.Default.JsonElement,
                ct).ConfigureAwait(false);
            var refreshed = ParseRefreshResponse(payload, current);
            await _store.WriteAsync(refreshed, ct).ConfigureAwait(false);

            MobileAuthTelemetry.RecordTokenRefresh("success");
            activity?.SetTag("auth.refresh.result", "success");
            return refreshed;
        }
        catch (HonuaMobileAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            MobileAuthTelemetry.RecordTokenRefresh("failure");
            activity?.SetStatus(ActivityStatusCode.Error);
            throw new HonuaMobileAuthException("Honua auth token refresh failed.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask StoreTokenAsync(HonuaAuthToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        try
        {
            await _store.WriteAsync(token, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new HonuaMobileAuthException("Honua auth token persistence failed.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask ClearTokenAsync(CancellationToken ct = default)
    {
        try
        {
            await _store.ClearAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new HonuaMobileAuthException("Honua auth token clearing failed.", ex);
        }
    }

    private static HonuaAuthToken ParseRefreshResponse(JsonElement payload, HonuaAuthToken current)
    {
        var accessToken = ReadString(payload, "accessToken", "access_token")
            ?? throw new HonuaMobileAuthException("Honua auth token refresh returned no access token.");
        var refreshToken = ReadString(payload, "refreshToken", "refresh_token") ?? current.RefreshToken;
        var tokenType = ReadString(payload, "tokenType", "token_type");
        var scheme = string.Equals(tokenType, "api_key", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tokenType, "apiKey", StringComparison.OrdinalIgnoreCase)
                ? HonuaAuthScheme.ApiKey
                : HonuaAuthScheme.Bearer;
        var expiresAtUtc = ReadExpiresAt(payload);

        return new HonuaAuthToken(scheme, accessToken, refreshToken, expiresAtUtc);
    }

    private static string? ReadString(JsonElement payload, params string[] names)
    {
        foreach (var name in names)
        {
            if (payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadExpiresAt(JsonElement payload)
    {
        var expiresAt = ReadString(payload, "expiresAtUtc", "expires_at_utc", "expiresAt", "expires_at");
        if (DateTimeOffset.TryParse(expiresAt, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        if (payload.TryGetProperty("expiresIn", out var expiresIn) || payload.TryGetProperty("expires_in", out expiresIn))
        {
            if (expiresIn.TryGetInt64(out var seconds))
            {
                return DateTimeOffset.UtcNow.AddSeconds(seconds);
            }
        }

        return null;
    }
}

internal sealed record RefreshTokenRequest(
    [property: JsonPropertyName("refreshToken")] string RefreshToken);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RefreshTokenRequest))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class HonuaMobileAuthJsonContext : JsonSerializerContext
{
}
