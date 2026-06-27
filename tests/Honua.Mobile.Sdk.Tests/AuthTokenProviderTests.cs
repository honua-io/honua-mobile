using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Auth;
using Honua.Sdk.Abstractions.Features;

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
    public async Task GetTokenAsync_WithExpiresIn_UsesConfiguredClockForRefreshedExpiration()
    {
        var now = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-token",
            "refresh-token",
            now.AddMinutes(-5)));

        var provider = new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "access_token": "fresh-token",
                        "refresh_token": "next-refresh-token",
                        "expires_in": 120
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            })),
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri("https://auth.honua.test/token/refresh"),
                TimeProvider = new FixedTimeProvider(now),
            });

        var token = await provider.GetTokenAsync();

        Assert.NotNull(token);
        Assert.Equal(now.AddSeconds(120), token.ExpiresAtUtc);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithApiKeyToken_SkipsRefreshEndpoint()
    {
        var refreshCalls = 0;
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(HonuaAuthScheme.ApiKey, "provider-api-key"));
        var provider = new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(_ =>
            {
                refreshCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            })),
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri("https://auth.honua.test/token/refresh"),
            });

        var token = await provider.RefreshTokenAsync();

        Assert.NotNull(token);
        Assert.Equal(HonuaAuthScheme.ApiKey, token.Scheme);
        Assert.Equal("provider-api-key", token.AccessToken);
        Assert.Equal(0, refreshCalls);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithHttpRefreshEndpoint_ThrowsAndDoesNotSendRequest()
    {
        var refreshCalls = 0;
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(-5)));
        var provider = new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(_ =>
            {
                refreshCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            })),
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri("http://auth.honua.test/token/refresh"),
            });

        var exception = await Assert.ThrowsAsync<HonuaMobileAuthException>(async () => await provider.RefreshTokenAsync());

        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("must use HTTPS", inner.Message);
        Assert.Equal(0, refreshCalls);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithRelativeHttpRefreshEndpoint_ThrowsAndDoesNotSendRequest()
    {
        var refreshCalls = 0;
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(-5)));
        var http = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            refreshCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }))
        {
            BaseAddress = new Uri("http://auth.honua.test"),
        };
        var provider = new RefreshingAuthTokenProvider(
            store,
            http,
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri("/token/refresh", UriKind.Relative),
            });

        var exception = await Assert.ThrowsAsync<HonuaMobileAuthException>(async () => await provider.RefreshTokenAsync());

        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("must use HTTPS", inner.Message);
        Assert.Equal(0, refreshCalls);
    }

    [Theory]
    [InlineData("http://localhost:5000/token/refresh")]
    [InlineData("http://127.0.0.1:5000/token/refresh")]
    public async Task RefreshTokenAsync_WithLoopbackHttpRefreshEndpoint_SendsRequest(string refreshEndpoint)
    {
        var refreshCalls = 0;
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(-5)));
        var provider = new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(request =>
            {
                refreshCalls++;
                Assert.Equal(new Uri(refreshEndpoint), request.RequestUri);
                return new HttpResponseMessage(HttpStatusCode.OK)
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
                };
            })),
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri(refreshEndpoint),
            });

        var token = await provider.RefreshTokenAsync();

        Assert.Equal("fresh-token", token?.AccessToken);
        Assert.Equal(1, refreshCalls);
    }

    [Fact]
    public async Task GetTokenAsync_WhenRefreshReturnsProblemDetails_ThrowsMappedAuthException()
    {
        var now = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-token",
            "refresh-token",
            now.AddMinutes(-5)));
        var provider = new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    """
                    {
                        "type": "https://honua.test/problems/auth/expired-refresh-token",
                        "title": "Refresh token expired",
                        "detail": "Sign in again before syncing."
                    }
                    """,
                    Encoding.UTF8,
                    "application/problem+json"),
            })),
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri("https://auth.honua.test/token/refresh"),
                TimeProvider = new FixedTimeProvider(now),
            });

        var exception = await Assert.ThrowsAsync<HonuaMobileAuthException>(async () => await provider.GetTokenAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("Refresh token expired", exception.Message);
        Assert.Contains("Sign in again before syncing.", exception.Message);
    }

    [Fact]
    public async Task RefreshTokenAsync_EmitsActivityAndRefreshCounterResult()
    {
        var now = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
        var measurements = new List<string>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MobileAuthTelemetry.MeterName &&
                instrument.Name == "mobile_auth_token_refreshes_total")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            var result = tags.ToArray().FirstOrDefault(tag => tag.Key == "result").Value?.ToString();
            if (measurement == 1 && result is not null)
            {
                measurements.Add(result);
            }
        });
        meterListener.Start();

        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MobileAuthTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(activityListener);

        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-token",
            "refresh-token",
            now.AddMinutes(-5)));
        var provider = new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "accessToken": "fresh-token",
                        "refreshToken": "next-refresh-token",
                        "expiresIn": 120
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            })),
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri("https://auth.honua.test/token/refresh"),
                TimeProvider = new FixedTimeProvider(now),
            });

        await provider.RefreshTokenAsync();

        Assert.Contains("success", measurements);
        Assert.Contains(activities, activity =>
            activity.OperationName == "honua.mobile.auth.refresh" &&
            string.Equals(activity.GetTagItem("auth.refresh.result")?.ToString(), "success", StringComparison.Ordinal));
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

    [Fact]
    public async Task GetTokenAsync_ConcurrentExpiredCallers_CoalesceToSingleRefresh()
    {
        var now = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
        var refreshCalls = 0;
        var store = new CountingAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-token",
            "refresh-token",
            now.AddMinutes(-5)));

        var provider = new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(_ =>
            {
                Interlocked.Increment(ref refreshCalls);
                return new HttpResponseMessage(HttpStatusCode.OK)
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
                };
            })),
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri("https://auth.honua.test/token/refresh"),
                TimeProvider = new FixedTimeProvider(now),
            });

        var tokens = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => provider.GetTokenAsync().AsTask()));

        Assert.Equal(1, refreshCalls);
        Assert.All(tokens, token => Assert.Equal("fresh-token", token?.AccessToken));
        // The rotating refresh token was persisted exactly once.
        var stored = await store.ReadAsync();
        Assert.Equal("next-refresh-token", stored?.RefreshToken);
    }

    [Fact]
    public async Task GetTokenAsync_WithValidCachedToken_ReadsSecureStoreOnce()
    {
        var now = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
        var store = new CountingAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "valid-token",
            "refresh-token",
            now.AddHours(1)));

        var provider = new RefreshingAuthTokenProvider(
            store,
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = new Uri("https://auth.honua.test/token/refresh"),
                TimeProvider = new FixedTimeProvider(now),
            });

        var readsBefore = store.ReadCount;
        for (var i = 0; i < 5; i++)
        {
            var token = await provider.GetTokenAsync();
            Assert.Equal("valid-token", token?.AccessToken);
        }

        // The keystore is read once on first use; subsequent requests are served
        // from the in-memory cache.
        Assert.Equal(readsBefore + 1, store.ReadCount);
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

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class CountingAuthTokenStore : IAuthTokenStore
    {
        private readonly InMemoryAuthTokenStore _inner = new();
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public ValueTask<HonuaAuthToken?> ReadAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _readCount);
            return _inner.ReadAsync(ct);
        }

        public ValueTask WriteAsync(HonuaAuthToken token, CancellationToken ct = default)
            => _inner.WriteAsync(token, ct);

        public ValueTask ClearAsync(CancellationToken ct = default)
            => _inner.ClearAsync(ct);
    }
}
