namespace Honua.Mobile.Sdk.Auth;

/// <summary>
/// Resolves and refreshes mobile authentication tokens used by Honua clients.
/// </summary>
public interface IAuthTokenProvider
{
    /// <summary>
    /// Returns the current token, refreshing it first when the provider supports refresh and the token is near expiry.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current token, or <see langword="null"/> when no token is available.</returns>
    ValueTask<HonuaAuthToken?> GetTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Attempts to refresh the current token.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The refreshed token, the existing token when refresh is not possible, or <see langword="null"/> when no token exists.</returns>
    ValueTask<HonuaAuthToken?> RefreshTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores new authentication material.
    /// </summary>
    /// <param name="token">Token to store.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask StoreTokenAsync(HonuaAuthToken token, CancellationToken ct = default);

    /// <summary>
    /// Clears stored authentication material.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ClearTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// Secure storage abstraction used by auth token providers.
/// </summary>
public interface IAuthTokenStore
{
    /// <summary>
    /// Reads stored authentication material.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored token, or <see langword="null"/> when no token is stored.</returns>
    ValueTask<HonuaAuthToken?> ReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes authentication material to storage.
    /// </summary>
    /// <param name="token">Token to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask WriteAsync(HonuaAuthToken token, CancellationToken ct = default);

    /// <summary>
    /// Removes authentication material from storage.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ClearAsync(CancellationToken ct = default);
}
