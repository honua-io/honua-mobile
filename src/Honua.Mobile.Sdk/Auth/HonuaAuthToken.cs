namespace Honua.Mobile.Sdk.Auth;

/// <summary>
/// Authentication scheme represented by a mobile auth token.
/// </summary>
public enum HonuaAuthScheme
{
    /// <summary>
    /// Token is sent through the <c>X-API-Key</c> header.
    /// </summary>
    ApiKey,

    /// <summary>
    /// Token is sent through the HTTP bearer-token authorization convention.
    /// </summary>
    Bearer,
}

/// <summary>
/// Mobile authentication material resolved by an <see cref="IAuthTokenProvider"/>.
/// </summary>
public sealed record HonuaAuthToken
{
    /// <summary>
    /// Initializes a new <see cref="HonuaAuthToken"/>.
    /// </summary>
    /// <param name="scheme">Authentication scheme used for the token.</param>
    /// <param name="accessToken">API key or bearer access token value.</param>
    /// <param name="refreshToken">Optional refresh token used to obtain a new bearer token.</param>
    /// <param name="expiresAtUtc">Optional access token expiration time in UTC.</param>
    public HonuaAuthToken(
        HonuaAuthScheme scheme,
        string accessToken,
        string? refreshToken = null,
        DateTimeOffset? expiresAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        Scheme = scheme;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>
    /// Authentication scheme used for the token.
    /// </summary>
    public HonuaAuthScheme Scheme { get; }

    /// <summary>
    /// API key or bearer access token value.
    /// </summary>
    public string AccessToken { get; }

    /// <summary>
    /// Optional refresh token used to obtain a new bearer token.
    /// </summary>
    public string? RefreshToken { get; }

    /// <summary>
    /// Optional access token expiration time in UTC.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; }

    /// <summary>
    /// Determines whether the token should be refreshed before use.
    /// </summary>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <param name="refreshSkew">Clock skew applied before expiration.</param>
    /// <returns><see langword="true"/> when a refresh should be attempted.</returns>
    public bool ShouldRefresh(DateTimeOffset nowUtc, TimeSpan refreshSkew)
        => Scheme == HonuaAuthScheme.Bearer &&
            !string.IsNullOrWhiteSpace(RefreshToken) &&
            ExpiresAtUtc.HasValue &&
            ExpiresAtUtc.Value <= nowUtc.Add(refreshSkew);
}
