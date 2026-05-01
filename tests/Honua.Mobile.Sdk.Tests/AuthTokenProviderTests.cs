using System.Net;
using System.Text;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Auth;
using Honua.Mobile.Sdk.Models;

namespace Honua.Mobile.Sdk.Tests;

public sealed class AuthTokenProviderTests
{
    [Fact]
    public async Task QueryFeaturesAsync_WithAuthTokenProviderApiKey_SendsApiKeyHeader()
    {
        string? capturedApiKey = null;
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(HonuaAuthScheme.ApiKey, "provider-api-key"));

        var client = CreateClient(request =>
        {
            if (request.Headers.TryGetValues("X-API-Key", out var values))
            {
                capturedApiKey = values.FirstOrDefault();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"features":[]}""", Encoding.UTF8, "application/json"),
            };
        }, new RefreshingAuthTokenProvider(store, new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))));

        using var result = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
        });

        Assert.Equal("provider-api-key", capturedApiKey);
    }

    [Fact]
    public async Task QueryFeaturesAsync_WithAuthTokenProviderAndThrowingLegacyProvider_UsesProviderToken()
    {
        var legacyProviderCalls = 0;
        string? capturedApiKey = null;
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(HonuaAuthScheme.ApiKey, "provider-api-key"));

        var client = CreateClient(request =>
        {
            if (request.Headers.TryGetValues("X-API-Key", out var values))
            {
                capturedApiKey = values.FirstOrDefault();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"features":[]}""", Encoding.UTF8, "application/json"),
            };
        }, new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))),
        new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            AccessTokenProvider = _ =>
            {
                legacyProviderCalls++;
                throw new InvalidOperationException("Legacy provider should not be invoked when an auth token provider resolves a token.");
            },
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        });

        using var result = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
        });

        Assert.Equal("provider-api-key", capturedApiKey);
        Assert.Equal(0, legacyProviderCalls);
    }

    [Fact]
    public async Task BuildGrpcClientOptions_WithAuthTokenProviderApiKey_DoesNotInvokeLegacyBearerProvider()
    {
        var legacyProviderCalls = 0;
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(HonuaAuthScheme.ApiKey, "provider-api-key"));
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK), new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))),
        new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            AccessTokenProvider = _ =>
            {
                legacyProviderCalls++;
                throw new InvalidOperationException("Legacy provider should not be invoked when an auth token provider resolves a token.");
            },
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        });

        var options = client.BuildGrpcClientOptions();

        Assert.NotNull(options.ApiKeyProvider);
        Assert.Equal("provider-api-key", await options.ApiKeyProvider!(CancellationToken.None));
        Assert.NotNull(options.BearerTokenProvider);
        Assert.Null(await options.BearerTokenProvider!(CancellationToken.None));
        Assert.Equal(0, legacyProviderCalls);
    }

    [Fact]
    public async Task QueryFeaturesAsync_WithExpiredBearerToken_RefreshesBeforeSending()
    {
        string? capturedAuthHeader = null;
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(-5)));

        var refreshHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                    "accessToken": "fresh-token",
                    "refreshToken": "next-refresh-token",
                    "expiresIn": 3600
                }
                """,
                Encoding.UTF8,
                "application/json"),
        });
        var provider = new RefreshingAuthTokenProvider(
            store,
            new HttpClient(refreshHandler),
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri("https://auth.honua.test/token/refresh"),
            });
        var client = CreateClient(request =>
        {
            capturedAuthHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"features":[]}""", Encoding.UTF8, "application/json"),
            };
        }, provider);

        using var result = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
        });

        var stored = await store.ReadAsync();
        Assert.Equal("Bearer fresh-token", capturedAuthHeader);
        Assert.Equal("next-refresh-token", stored?.RefreshToken);
    }

    [Fact]
    public async Task GetTokenAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = new RefreshingAuthTokenProvider(
            new InMemoryAuthTokenStore(),
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.GetTokenAsync(cts.Token));
    }

    private static HonuaMobileClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        IAuthTokenProvider provider,
        HonuaMobileClientOptions? options = null)
    {
        options ??= new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        };

        return new HonuaMobileClient(
            new HttpClient(new StubHttpMessageHandler(handler))
            {
                BaseAddress = options.BaseUri,
            },
            options,
            provider);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
