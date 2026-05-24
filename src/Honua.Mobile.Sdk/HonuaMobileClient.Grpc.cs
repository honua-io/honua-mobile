using Honua.Mobile.Sdk.Auth;
using Honua.Sdk.Grpc;

namespace Honua.Mobile.Sdk;

public sealed partial class HonuaMobileClient
{
    internal HonuaGrpcClient GetGrpcClient()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(HonuaMobileClient));
        }

        return _grpcClient.Value;
    }

    internal HonuaGrpcClientOptions BuildGrpcClientOptions()
    {
        var address = _options.GrpcEndpoint ?? _options.BaseUri;
        if (HasConfiguredGrpcAuthentication)
        {
            EnsureSecureTransport(address);
        }

        return new HonuaGrpcClientOptions
        {
            BaseAddress = address,
            ApiKey = _options.ApiKey,
            ApiKeyProvider = BuildGrpcApiKeyProvider(),
            BearerToken = _options.BearerToken,
            BearerTokenProvider = BuildGrpcBearerTokenProvider(),
            Timeout = _options.Timeout,
        };
    }

    private Func<CancellationToken, Task<string?>>? BuildGrpcApiKeyProvider()
    {
        if (_authTokenProvider is null)
        {
            return null;
        }

        return async ct =>
        {
            var token = await _authTokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
            return token is { Scheme: HonuaAuthScheme.ApiKey }
                ? token.AccessToken
                : _options.ApiKey;
        };
    }

    private Func<CancellationToken, Task<string?>>? BuildGrpcBearerTokenProvider()
    {
        if (_authTokenProvider is not null)
        {
            return async ct =>
            {
                var token = await _authTokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
                if (token is { Scheme: HonuaAuthScheme.Bearer })
                {
                    return token.AccessToken;
                }

                return token is null
                    ? await ResolveBearerTokenAsync(ct).ConfigureAwait(false)
                    : _options.BearerToken;
            };
        }

        return _options.AccessTokenProvider is null
            ? null
            : async ct => await _options.AccessTokenProvider(ct).ConfigureAwait(false) ?? _options.BearerToken;
    }

    private bool HasConfiguredGrpcAuthentication =>
        !string.IsNullOrWhiteSpace(_options.ApiKey) ||
        !string.IsNullOrWhiteSpace(_options.BearerToken) ||
        _options.AccessTokenProvider is not null ||
        _authTokenProvider is not null;
}
