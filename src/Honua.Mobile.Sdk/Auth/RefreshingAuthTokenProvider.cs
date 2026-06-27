// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

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

    // Serializes refreshes and cache updates so concurrent callers coalesce onto a
    // single refresh instead of racing the token store (critical with rotating /
    // single-use refresh tokens). Also guards the in-memory cache below.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // In-memory copy of the persisted token so the secure keystore is read once at
    // startup and only on a cache miss or rotation, rather than on every request.
    private HonuaAuthToken? _cached;
    private bool _cacheLoaded;

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
            // Fast path: serve a still-valid cached token without touching the
            // keystore or taking the lock.
            var cached = Volatile.Read(ref _cached);
            if (_cacheLoaded &&
                cached is not null &&
                !cached.ShouldRefresh(_options.TimeProvider.GetUtcNow(), _options.RefreshSkew))
            {
                return cached;
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var token = await LoadCachedTokenLockedAsync(ct).ConfigureAwait(false);
                if (token is null)
                {
                    return null;
                }

                // Re-check under the lock: a concurrent caller may have refreshed
                // while we waited, so we avoid a redundant network round-trip.
                if (!token.ShouldRefresh(_options.TimeProvider.GetUtcNow(), _options.RefreshSkew))
                {
                    return token;
                }

                return await RefreshLockedAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (HonuaMobileAuthException)
        {
            throw;
        }
        catch (OperationCanceledException)
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
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RefreshLockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Performs the refresh while holding <see cref="_gate"/>. Re-reads the current
    // token from the cache/store inside the lock so a coalesced caller always
    // refreshes with the latest (possibly already-rotated) refresh token.
    private async ValueTask<HonuaAuthToken?> RefreshLockedAsync(CancellationToken ct)
    {
        using var activity = MobileAuthTelemetry.ActivitySource.StartActivity("honua.mobile.auth.refresh", ActivityKind.Client);

        try
        {
            var current = await LoadCachedTokenLockedAsync(ct).ConfigureAwait(false);
            activity?.SetTag("auth.scheme", current?.Scheme.ToString().ToLowerInvariant() ?? "none");

            if (current is null ||
                current.Scheme != HonuaAuthScheme.Bearer ||
                string.IsNullOrWhiteSpace(current.RefreshToken) ||
                _options.RefreshEndpoint is null)
            {
                MobileAuthTelemetry.RecordTokenRefresh("skipped");
                activity?.SetTag("auth.refresh.result", "skipped");
                return current;
            }

            var refreshEndpoint = ResolveRefreshEndpoint(_options.RefreshEndpoint);
            EnsureAllowedRefreshEndpoint(refreshEndpoint);

            using var content = JsonContent.Create(
                new RefreshTokenRequest(current.RefreshToken),
                HonuaMobileAuthJsonContext.Default.RefreshTokenRequest);
            using var response = await _http.PostAsync(refreshEndpoint, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw HonuaAuthProblemDetails.CreateRefreshException(response.StatusCode, response.ReasonPhrase, raw);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(
                stream,
                HonuaMobileAuthJsonContext.Default.JsonElement,
                ct).ConfigureAwait(false);
            var refreshed = ParseRefreshResponse(payload, current, _options.TimeProvider.GetUtcNow());
            await _store.WriteAsync(refreshed, ct).ConfigureAwait(false);
            _cached = refreshed;
            _cacheLoaded = true;

            MobileAuthTelemetry.RecordTokenRefresh("success");
            activity?.SetTag("auth.refresh.result", "success");
            return refreshed;
        }
        catch (HonuaMobileAuthException)
        {
            MobileAuthTelemetry.RecordTokenRefresh("failure");
            activity?.SetTag("auth.refresh.result", "failure");
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (OperationCanceledException)
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
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _store.WriteAsync(token, ct).ConfigureAwait(false);
                _cached = token;
                _cacheLoaded = true;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _store.ClearAsync(ct).ConfigureAwait(false);
                _cached = null;
                _cacheLoaded = true;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new HonuaMobileAuthException("Honua auth token clearing failed.", ex);
        }
    }

    // Returns the in-memory token, loading it from the secure store exactly once on
    // first use. Must be called while holding <see cref="_gate"/>.
    private async ValueTask<HonuaAuthToken?> LoadCachedTokenLockedAsync(CancellationToken ct)
    {
        if (_cacheLoaded)
        {
            return _cached;
        }

        _cached = await _store.ReadAsync(ct).ConfigureAwait(false);
        _cacheLoaded = true;
        return _cached;
    }

    private static HonuaAuthToken ParseRefreshResponse(
        JsonElement payload,
        HonuaAuthToken current,
        DateTimeOffset nowUtc)
    {
        var accessToken = ReadString(payload, "accessToken", "access_token")
            ?? throw new HonuaMobileAuthException("Honua auth token refresh returned no access token.");
        var refreshToken = ReadString(payload, "refreshToken", "refresh_token") ?? current.RefreshToken;
        var tokenType = ReadString(payload, "tokenType", "token_type");
        var scheme = string.Equals(tokenType, "api_key", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tokenType, "apiKey", StringComparison.OrdinalIgnoreCase)
                ? HonuaAuthScheme.ApiKey
                : HonuaAuthScheme.Bearer;
        var expiresAtUtc = ReadExpiresAt(payload, nowUtc);

        return new HonuaAuthToken(scheme, accessToken, refreshToken, expiresAtUtc);
    }

    private static string? ReadString(JsonElement payload, params string[] names)
    {
        foreach (var name in names)
        {
            if (payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadExpiresAt(JsonElement payload, DateTimeOffset nowUtc)
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
                return nowUtc.AddSeconds(seconds);
            }
        }

        return null;
    }

    private Uri ResolveRefreshEndpoint(Uri refreshEndpoint)
    {
        if (refreshEndpoint.IsAbsoluteUri)
        {
            return refreshEndpoint;
        }

        if (_http.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "RefreshingAuthTokenProvider requires an absolute refresh endpoint or HttpClient.BaseAddress.");
        }

        return new Uri(_http.BaseAddress, refreshEndpoint);
    }

    private static void EnsureAllowedRefreshEndpoint(Uri refreshEndpoint)
    {
        if (string.Equals(refreshEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(refreshEndpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            refreshEndpoint.IsLoopback)
        {
            return;
        }

        throw new InvalidOperationException(
            "The Honua auth refresh endpoint must use HTTPS unless it points to a loopback HTTP development endpoint.");
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
