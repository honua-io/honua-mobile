using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Mobile.Sdk.Auth;

namespace Honua.Mobile.Maui.Auth;

/// <summary>
/// Minimal secure key-value storage contract used by <see cref="MauiSecureAuthTokenStore"/>.
/// </summary>
public interface IMauiSecureStorage
{
    /// <summary>
    /// Gets a stored secure value.
    /// </summary>
    /// <param name="key">Storage key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored value, or <see langword="null"/> when absent.</returns>
    ValueTask<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Sets a secure value.
    /// </summary>
    /// <param name="key">Storage key.</param>
    /// <param name="value">Value to store.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SetAsync(string key, string value, CancellationToken ct = default);

    /// <summary>
    /// Removes a secure value.
    /// </summary>
    /// <param name="key">Storage key.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Auth token store backed by MAUI secure storage. On iOS this maps to Keychain, and on Android it maps to
/// encrypted SharedPreferences protected by the Android Keystore provider.
/// </summary>
public sealed class MauiSecureAuthTokenStore : IAuthTokenStore
{
    private const string DefaultStorageKey = "honua.mobile.auth.token";

    private readonly IMauiSecureStorage _storage;
    private readonly string _storageKey;

    /// <summary>
    /// Initializes a new <see cref="MauiSecureAuthTokenStore"/>.
    /// </summary>
    /// <param name="storage">Platform secure storage adapter.</param>
    /// <param name="storageKey">Storage key. Defaults to a Honua SDK key.</param>
    public MauiSecureAuthTokenStore(IMauiSecureStorage storage, string storageKey = DefaultStorageKey)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _storageKey = string.IsNullOrWhiteSpace(storageKey) ? DefaultStorageKey : storageKey;
    }

    /// <inheritdoc />
    public async ValueTask<HonuaAuthToken?> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            var payload = await _storage.GetAsync(_storageKey, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(payload)
                ? null
                : JsonSerializer.Deserialize(payload, HonuaMobileMauiAuthJsonContext.Default.HonuaAuthToken);
        }
        catch (Exception ex)
        {
            throw new HonuaMobileAuthException("Honua auth token secure-storage read failed.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(HonuaAuthToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        try
        {
            var payload = JsonSerializer.Serialize(token, HonuaMobileMauiAuthJsonContext.Default.HonuaAuthToken);
            await _storage.SetAsync(_storageKey, payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new HonuaMobileAuthException("Honua auth token secure-storage write failed.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask ClearAsync(CancellationToken ct = default)
    {
        try
        {
            await _storage.RemoveAsync(_storageKey, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new HonuaMobileAuthException("Honua auth token secure-storage clear failed.", ex);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HonuaAuthToken))]
internal sealed partial class HonuaMobileMauiAuthJsonContext : JsonSerializerContext;
