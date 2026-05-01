namespace Honua.Mobile.Sdk.Auth;

/// <summary>
/// In-memory token store intended for tests, local development, and custom provider composition.
/// </summary>
public sealed class InMemoryAuthTokenStore : IAuthTokenStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HonuaAuthToken? _token;

    /// <inheritdoc />
    public async ValueTask<HonuaAuthToken?> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(HonuaAuthToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _token = token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _token = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
