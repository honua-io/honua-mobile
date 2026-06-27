using System.Net;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.Sdk.Auth;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task ValidateConnection_WithApiKey_RequiresAuthenticatedEndpoint()
    {
        var requestedPaths = new List<string>();
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.PathAndQuery);
            return request.RequestUri!.AbsolutePath == "/api/scenes"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var service = new AuthenticationService(http);

        var isValid = await service.ValidateConnectionAsync("https://api.honua.test", "bad-key");

        Assert.False(isValid);
        Assert.Equal(["/api/scenes?f=json"], requestedPaths);
    }

    [Fact]
    public async Task ValidateConnection_WithApiKey_FallsBackWhenSceneDiscoveryIsMissing()
    {
        var requestedPaths = new List<string>();
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.PathAndQuery);
            return request.RequestUri!.AbsolutePath == "/rest/services"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var service = new AuthenticationService(http);

        var isValid = await service.ValidateConnectionAsync("https://api.honua.test", "api-key");

        Assert.True(isValid);
        Assert.Equal(["/api/scenes?f=json", "/rest/services?f=json"], requestedPaths);
    }

    [Fact]
    public async Task ValidateConnection_WithApiKey_DoesNotFalselySucceedViaHealthEndpoint()
    {
        var requestedPaths = new List<string>();
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);

            // Authenticated endpoints are absent; only the unauthenticated health
            // endpoint answers 200. A bogus key must not validate against it.
            return request.RequestUri!.AbsolutePath == "/health"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var service = new AuthenticationService(http);

        var isValid = await service.ValidateConnectionAsync("https://api.honua.test", "bogus-key");

        Assert.False(isValid);
        Assert.DoesNotContain("/health", requestedPaths);
    }

    [Fact]
    public async Task ValidateConnection_WithoutApiKey_AllowsPublicHealthEndpoint()
    {
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == "/health"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = new AuthenticationService(http);

        var isValid = await service.ValidateConnectionAsync("https://api.honua.test");

        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidateConnection_RejectsPlainHttpExceptLoopback()
    {
        using var http = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new AuthenticationService(http);

        Assert.False(await service.ValidateConnectionAsync("http://api.honua.test", "key"));
        Assert.True(await service.ValidateConnectionAsync("http://localhost:5000", "key"));
    }

    [Fact]
    public async Task AuthenticateWithCredentialsAsync_WhenTokenEndpointSucceeds_StoresBearerSession()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 0, 0, TimeSpan.Zero);
        var requestedPaths = new List<string>();
        var store = new InMemoryAuthenticationSessionStore();
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "access_token": "bearer-access",
                      "refresh_token": "bearer-refresh",
                      "expires_in": 3600,
                      "user_id": "field-user",
                      "user_name": "Field User"
                    }
                    """)
            };
        }));
        var service = new AuthenticationService(
            http,
            sessionStore: store,
            timeProvider: new FixedTimeProvider(now));

        var result = await service.AuthenticateWithCredentialsAsync(
            "https://api.honua.test",
            "field-user",
            "password");
        var token = await service.GetAuthTokenAsync();

        Assert.True(result.IsSuccess);
        Assert.True(service.IsAuthenticated);
        Assert.Equal(HonuaAuthScheme.Bearer, service.AuthScheme);
        Assert.Null(service.ApiKey);
        Assert.Equal("bearer-access", token?.AccessToken);
        Assert.Equal(now.AddHours(1), service.ExpiresAtUtc);
        Assert.Equal(["/oauth/token"], requestedPaths);
    }

    [Fact]
    public async Task EnsureValidSessionAsync_WithExpiredBearerToken_RefreshesBeforeUse()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryAuthenticationSessionStore();
        var tokenStore = new AuthenticationSessionTokenStore(store);
        await store.SetAsync("server_url", "https://api.honua.test");
        await store.SetAsync("user_id", "field-user");
        await store.SetAsync("user_name", "Field User");
        await tokenStore.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-access",
            "refresh-token",
            now.AddMinutes(-10)));
        var provider = new RecordingAuthTokenProvider
        {
            RefreshResult = new HonuaAuthToken(
                HonuaAuthScheme.Bearer,
                "fresh-access",
                "next-refresh",
                now.AddHours(1))
        };
        using var http = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new AuthenticationService(
            http,
            sessionStore: store,
            authTokenProvider: provider,
            timeProvider: new FixedTimeProvider(now));

        var isValid = await service.EnsureValidSessionAsync();
        var token = await service.GetAuthTokenAsync();

        Assert.True(isValid);
        Assert.False(service.RequiresReauthentication);
        Assert.Equal("fresh-access", token?.AccessToken);
        Assert.Equal(now.AddHours(1), service.ExpiresAtUtc);
        Assert.Equal(1, provider.RefreshCalls);
    }

    [Fact]
    public async Task EnsureValidSessionAsync_WhenBearerRefreshFails_RequiresReauthentication()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryAuthenticationSessionStore();
        var tokenStore = new AuthenticationSessionTokenStore(store);
        await store.SetAsync("server_url", "https://api.honua.test");
        await tokenStore.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-access",
            "refresh-token",
            now.AddMinutes(-10)));
        var provider = new RecordingAuthTokenProvider
        {
            RefreshException = new HonuaMobileAuthException("Refresh token expired.")
        };
        using var http = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new AuthenticationService(
            http,
            sessionStore: store,
            authTokenProvider: provider,
            timeProvider: new FixedTimeProvider(now));

        var isValid = await service.EnsureValidSessionAsync();

        Assert.False(isValid);
        Assert.True(service.RequiresReauthentication);
        Assert.False(service.IsAuthenticated);
        Assert.Contains("Sign in again", service.SessionStatusMessage);
        Assert.Equal(1, provider.RefreshCalls);
    }

    [Fact]
    public async Task LogoutAsync_ClearsStoredSecretsAndKeepsOfflineDataPolicyVisible()
    {
        var store = new InMemoryAuthenticationSessionStore();
        var tokenStore = new AuthenticationSessionTokenStore(store);
        var provider = new RecordingAuthTokenProvider();
        await store.SetAsync("server_url", "https://api.honua.test");
        await store.SetAsync("api_key", "api-secret");
        await store.SetAsync("user_id", "field-user");
        await store.SetAsync("user_name", "Field User");
        await tokenStore.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "access-secret",
            "refresh-secret",
            DateTimeOffset.UtcNow.AddHours(1)));
        using var http = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new AuthenticationService(
            http,
            sessionStore: store,
            authTokenProvider: provider);

        await service.LogoutAsync();

        Assert.Null(await tokenStore.ReadAsync());
        Assert.DoesNotContain(store.Values, value =>
            value is "api-secret" or "access-secret" or "refresh-secret");
        Assert.True(provider.ClearCalled);
        Assert.False(service.IsAuthenticated);
        Assert.Contains("Local offline edits remain", service.SessionStatusMessage);
    }

    private sealed class InMemoryAuthenticationSessionStore : IAuthenticationSessionStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Values => _values.Values.ToArray();

        public Task<string?> GetAsync(string key)
        {
            _values.TryGetValue(key, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            _values.Remove(key);
        }
    }

    private sealed class RecordingAuthTokenProvider : IAuthTokenProvider
    {
        public HonuaAuthToken? CurrentToken { get; set; }
        public HonuaAuthToken? RefreshResult { get; set; }
        public Exception? RefreshException { get; set; }
        public int RefreshCalls { get; private set; }
        public bool ClearCalled { get; private set; }

        public ValueTask<HonuaAuthToken?> GetTokenAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CurrentToken);
        }

        public ValueTask<HonuaAuthToken?> RefreshTokenAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            RefreshCalls++;
            if (RefreshException is not null)
            {
                throw RefreshException;
            }

            CurrentToken = RefreshResult ?? CurrentToken;
            return ValueTask.FromResult(CurrentToken);
        }

        public ValueTask StoreTokenAsync(HonuaAuthToken token, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            CurrentToken = token;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearTokenAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ClearCalled = true;
            CurrentToken = null;
            return ValueTask.CompletedTask;
        }
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
}
