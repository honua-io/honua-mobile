using System.ComponentModel;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Mobile.Sdk.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Honua.Mobile.FieldCollection.Services;

public interface IAuthenticationService : INotifyPropertyChanged
{
    bool IsAuthenticated { get; }
    bool RequiresReauthentication { get; }
    string? SessionStatusMessage { get; }
    DateTimeOffset? ExpiresAtUtc { get; }
    HonuaAuthScheme? AuthScheme { get; }
    string? CurrentUserId { get; }
    string? CurrentUserName { get; }
    string? ApiKey { get; }
    string? ServerUrl { get; }

    Task<AuthenticationResult> AuthenticateAsync(string serverUrl, string apiKey);
    Task<AuthenticationResult> AuthenticateWithCredentialsAsync(string serverUrl, string username, string password);
    Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default);
    Task<bool> EnsureValidSessionAsync(CancellationToken cancellationToken = default);
    ValueTask<HonuaAuthToken?> GetAuthTokenAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync();
    Task<bool> ValidateConnectionAsync(string serverUrl, string? apiKey = null);
}

public class AuthenticationResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Token { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public static AuthenticationResult Success(
        string userId,
        string userName,
        string token,
        DateTimeOffset? expiresAtUtc = null) =>
        new()
        {
            IsSuccess = true,
            UserId = userId,
            UserName = userName,
            Token = token,
            ExpiresAt = expiresAtUtc?.UtcDateTime,
            ExpiresAtUtc = expiresAtUtc
        };

    public static AuthenticationResult Failure(string errorMessage) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage };
}

internal interface IAuthenticationSessionStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    void Remove(string key);
}

internal sealed class SecureStorageAuthenticationSessionStore : IAuthenticationSessionStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Remove(key);
}

internal sealed class AuthenticationSessionTokenStore : IAuthTokenStore
{
    internal const string AuthSchemeKey = "auth_scheme";
    internal const string AccessTokenKey = "access_token";
    internal const string RefreshTokenKey = "refresh_token";
    internal const string ExpiresAtUtcKey = "expires_at_utc";

    private readonly IAuthenticationSessionStore _store;

    public AuthenticationSessionTokenStore(IAuthenticationSessionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async ValueTask<HonuaAuthToken?> ReadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var schemeText = await _store.GetAsync(AuthSchemeKey).ConfigureAwait(false);
        var accessToken = await _store.GetAsync(AccessTokenKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken) || !TryReadScheme(schemeText, out var scheme))
        {
            return null;
        }

        var refreshToken = await _store.GetAsync(RefreshTokenKey).ConfigureAwait(false);
        var expiresText = await _store.GetAsync(ExpiresAtUtcKey).ConfigureAwait(false);
        var expiresAtUtc = DateTimeOffset.TryParse(expiresText, out var parsed)
            ? parsed.ToUniversalTime()
            : (DateTimeOffset?)null;

        return new HonuaAuthToken(scheme, accessToken, refreshToken, expiresAtUtc);
    }

    public async ValueTask WriteAsync(HonuaAuthToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ct.ThrowIfCancellationRequested();

        await _store.SetAsync(AuthSchemeKey, token.Scheme.ToString()).ConfigureAwait(false);
        await _store.SetAsync(AccessTokenKey, token.AccessToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            _store.Remove(RefreshTokenKey);
        }
        else
        {
            await _store.SetAsync(RefreshTokenKey, token.RefreshToken).ConfigureAwait(false);
        }

        if (token.ExpiresAtUtc.HasValue)
        {
            await _store.SetAsync(ExpiresAtUtcKey, token.ExpiresAtUtc.Value.ToString("O")).ConfigureAwait(false);
        }
        else
        {
            _store.Remove(ExpiresAtUtcKey);
        }
    }

    public ValueTask ClearAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store.Remove(AuthSchemeKey);
        _store.Remove(AccessTokenKey);
        _store.Remove(RefreshTokenKey);
        _store.Remove(ExpiresAtUtcKey);
        return ValueTask.CompletedTask;
    }

    private static bool TryReadScheme(string? value, out HonuaAuthScheme scheme)
    {
        scheme = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, "api_key", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "apiKey", StringComparison.OrdinalIgnoreCase))
        {
            scheme = HonuaAuthScheme.ApiKey;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out scheme);
    }
}

public class AuthenticationService : IAuthenticationService
{
    private const string ServerUrlKey = "server_url";
    private const string ApiKeyKey = "api_key";
    private const string UserIdKey = "user_id";
    private const string UserNameKey = "user_name";
    private const string TokenEndpointPathKey = "token_endpoint_path";
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);

    private static readonly string[] ConnectionValidationPaths =
    {
        "/health",
        "/api/health"
    };

    // API-key validation probes only authenticated endpoints that reject a bad key
    // with 401/403. The unauthenticated health endpoints are intentionally excluded:
    // they return 200 regardless of the key, so treating that as success would let a
    // bogus key falsely validate.
    private static readonly string[] AuthenticatedValidationPaths =
    {
        "/api/scenes?f=json",
        "/rest/services?f=json"
    };

    private static readonly string[] TokenEndpointPaths =
    {
        "/oauth/token",
        "/api/auth/token"
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthenticationService>? _logger;
    private readonly IAuthenticationSessionStore _sessionStore;
    private readonly AuthenticationSessionTokenStore _tokenStore;
    private readonly IAuthTokenProvider? _authTokenProvider;
    private readonly TimeProvider _timeProvider;
    private string? _currentUserId;
    private string? _currentUserName;
    private string? _apiKey;
    private string? _serverUrl;
    private bool _requiresReauthentication;
    private string? _sessionStatusMessage;
    private DateTimeOffset? _expiresAtUtc;
    private HonuaAuthScheme? _authScheme;

    public AuthenticationService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, logger: null)
    {
    }

    public AuthenticationService(HttpClient httpClient, ILogger<AuthenticationService>? logger = null)
        : this(
            httpClient,
            sessionStore: new SecureStorageAuthenticationSessionStore(),
            authTokenProvider: null,
            timeProvider: null,
            logger: logger)
    {
    }

    internal AuthenticationService(
        HttpClient httpClient,
        IAuthenticationSessionStore sessionStore,
        IAuthTokenProvider? authTokenProvider = null,
        TimeProvider? timeProvider = null,
        ILogger<AuthenticationService>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _tokenStore = new AuthenticationSessionTokenStore(_sessionStore);
        _authTokenProvider = authTokenProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout > TimeSpan.FromSeconds(10))
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAuthenticated =>
        !string.IsNullOrEmpty(ServerUrl) &&
        !RequiresReauthentication &&
        ((AuthScheme == HonuaAuthScheme.ApiKey && !string.IsNullOrEmpty(ApiKey)) ||
            AuthScheme == HonuaAuthScheme.Bearer ||
            !string.IsNullOrEmpty(ApiKey));

    public bool RequiresReauthentication
    {
        get => _requiresReauthentication;
        private set
        {
            if (_requiresReauthentication == value)
            {
                return;
            }

            _requiresReauthentication = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAuthenticated));
        }
    }

    public string? SessionStatusMessage
    {
        get => _sessionStatusMessage;
        private set
        {
            if (_sessionStatusMessage == value)
            {
                return;
            }

            _sessionStatusMessage = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset? ExpiresAtUtc
    {
        get => _expiresAtUtc;
        private set
        {
            if (_expiresAtUtc == value)
            {
                return;
            }

            _expiresAtUtc = value;
            OnPropertyChanged();
        }
    }

    public HonuaAuthScheme? AuthScheme
    {
        get => _authScheme;
        private set
        {
            if (_authScheme == value)
            {
                return;
            }

            _authScheme = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAuthenticated));
        }
    }

    public string? CurrentUserId
    {
        get => _currentUserId;
        private set
        {
            _currentUserId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAuthenticated));
        }
    }

    public string? CurrentUserName
    {
        get => _currentUserName;
        private set
        {
            _currentUserName = value;
            OnPropertyChanged();
        }
    }

    public string? ApiKey
    {
        get => _apiKey;
        private set
        {
            _apiKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAuthenticated));
        }
    }

    public string? ServerUrl
    {
        get => _serverUrl;
        private set
        {
            _serverUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAuthenticated));
        }
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string serverUrl, string apiKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return AuthenticationResult.Failure("API key is required");
            }

            if (!TryNormalizeServerUri(serverUrl, out var normalizedUri, out var validationError))
            {
                return AuthenticationResult.Failure(validationError);
            }

            var isValid = await ValidateConnectionAsync(serverUrl, apiKey).ConfigureAwait(false);
            if (!isValid)
            {
                return AuthenticationResult.Failure("Unable to connect to server or invalid API key");
            }

            var normalizedServerUrl = normalizedUri.ToString().TrimEnd('/');
            var userId = normalizedUri.Host;
            var userName = $"API key ({normalizedUri.Host})";
            var token = new HonuaAuthToken(HonuaAuthScheme.ApiKey, apiKey);

            await _sessionStore.SetAsync(ApiKeyKey, apiKey).ConfigureAwait(false);
            await _sessionStore.SetAsync(ServerUrlKey, normalizedServerUrl).ConfigureAwait(false);
            await _sessionStore.SetAsync(UserIdKey, userId).ConfigureAwait(false);
            await _sessionStore.SetAsync(UserNameKey, userName).ConfigureAwait(false);
            _sessionStore.Remove(TokenEndpointPathKey);
            await _tokenStore.WriteAsync(token).ConfigureAwait(false);

            ServerUrl = normalizedServerUrl;
            ApiKey = apiKey;
            CurrentUserId = userId;
            CurrentUserName = userName;
            ApplyTokenToState(token);
            RequiresReauthentication = false;
            SessionStatusMessage = "Signed in";

            return AuthenticationResult.Success(CurrentUserId, CurrentUserName, apiKey);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "API-key authentication failed");
            return AuthenticationResult.Failure("Authentication failed. Check the server URL and API key.");
        }
    }

    public async Task<AuthenticationResult> AuthenticateWithCredentialsAsync(
        string serverUrl,
        string username,
        string password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return AuthenticationResult.Failure("Username and password are required");
            }

            if (!TryNormalizeServerUri(serverUrl, out var normalizedUri, out var validationError))
            {
                return AuthenticationResult.Failure(validationError);
            }

            foreach (var path in TokenEndpointPaths)
            {
                using var response = await RequestCredentialTokenAsync(
                    normalizedUri,
                    path,
                    username,
                    password).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return AuthenticationResult.Failure("Credential authentication failed. Sign in again.");
                }

                var parsed = await ParseTokenResponseAsync(
                    response,
                    _timeProvider.GetUtcNow(),
                    fallbackUserId: username,
                    fallbackUserName: username).ConfigureAwait(false);
                await StoreTokenSessionAsync(normalizedUri, parsed, path).ConfigureAwait(false);

                return AuthenticationResult.Success(
                    parsed.UserId,
                    parsed.UserName,
                    parsed.Token.AccessToken,
                    parsed.Token.ExpiresAtUtc);
            }

            return AuthenticationResult.Failure(
                "Username/password authentication is not configured for this server. Use an API key.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Credential authentication failed");
            return AuthenticationResult.Failure("Authentication failed. Check the server URL and credentials.");
        }
    }

    public async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await LoadStoredSessionAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(ServerUrl))
            {
                MarkReauthenticationRequired("Sign in required before syncing.");
                return false;
            }

            if (AuthScheme == HonuaAuthScheme.ApiKey || !string.IsNullOrEmpty(ApiKey))
            {
                if (string.IsNullOrEmpty(ApiKey))
                {
                    MarkReauthenticationRequired("Authentication is required. Sign in again.");
                    return false;
                }

                var isValid = await ValidateConnectionAsync(ServerUrl, ApiKey).ConfigureAwait(false);
                if (!isValid)
                {
                    MarkReauthenticationRequired("Authentication is invalid. Sign in again.");
                    return false;
                }

                RequiresReauthentication = false;
                SessionStatusMessage = "Session active";
                return true;
            }

            var current = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                MarkReauthenticationRequired("Session expired. Sign in again.");
                return false;
            }

            if (current.Scheme != HonuaAuthScheme.Bearer || string.IsNullOrWhiteSpace(current.RefreshToken))
            {
                if (IsBearerExpired(current))
                {
                    MarkReauthenticationRequired("Session expired. Sign in again.");
                    return false;
                }

                ApplyTokenToState(current);
                RequiresReauthentication = false;
                SessionStatusMessage = "Session active";
                return true;
            }

            var provider = await CreateRefreshProviderAsync().ConfigureAwait(false);
            var refreshed = await provider.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            if (refreshed is null)
            {
                MarkReauthenticationRequired("Session expired. Sign in again.");
                return false;
            }

            await _tokenStore.WriteAsync(refreshed, cancellationToken).ConfigureAwait(false);
            ApplyTokenToState(refreshed);

            if (IsBearerExpired(refreshed))
            {
                MarkReauthenticationRequired("Session expired. Sign in again.");
                return false;
            }

            RequiresReauthentication = false;
            SessionStatusMessage = refreshed.Scheme == HonuaAuthScheme.Bearer
                ? "Session refreshed"
                : "Session active";
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Token refresh failed");
            MarkReauthenticationRequired("Session could not be refreshed. Sign in again.");
            return false;
        }
    }

    public async Task<bool> EnsureValidSessionAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetAuthTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            if (!RequiresReauthentication)
            {
                SessionStatusMessage = "Authentication is required.";
            }

            return false;
        }

        if (IsBearerExpired(token))
        {
            MarkReauthenticationRequired("Session expired. Sign in again.");
            return false;
        }

        RequiresReauthentication = false;
        SessionStatusMessage = token.Scheme == HonuaAuthScheme.Bearer && token.ExpiresAtUtc.HasValue
            ? $"Session active until {token.ExpiresAtUtc.Value.UtcDateTime:u}"
            : "Session active";
        return true;
    }

    public async ValueTask<HonuaAuthToken?> GetAuthTokenAsync(CancellationToken cancellationToken = default)
    {
        await LoadStoredSessionAsync(cancellationToken).ConfigureAwait(false);
        if (RequiresReauthentication)
        {
            return null;
        }

        var token = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (token is null && !string.IsNullOrWhiteSpace(ApiKey))
        {
            token = new HonuaAuthToken(HonuaAuthScheme.ApiKey, ApiKey);
            await _tokenStore.WriteAsync(token, cancellationToken).ConfigureAwait(false);
        }

        if (token is null)
        {
            return null;
        }

        if (token.Scheme == HonuaAuthScheme.Bearer && token.ShouldRefresh(_timeProvider.GetUtcNow(), RefreshSkew))
        {
            var refreshed = await RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!refreshed)
            {
                return null;
            }

            token = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        if (token is null)
        {
            return null;
        }

        if (IsBearerExpired(token))
        {
            MarkReauthenticationRequired("Session expired. Sign in again.");
            return null;
        }

        ApplyTokenToState(token);
        return token;
    }

    public async Task LogoutAsync()
    {
        _sessionStore.Remove(ServerUrlKey);
        _sessionStore.Remove(ApiKeyKey);
        _sessionStore.Remove(UserIdKey);
        _sessionStore.Remove(UserNameKey);
        _sessionStore.Remove(TokenEndpointPathKey);
        await _tokenStore.ClearAsync().ConfigureAwait(false);

        try
        {
            if (_authTokenProvider is not null)
            {
                await _authTokenProvider.ClearTokenAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Auth token provider clearing failed during sign-out");
        }

        ServerUrl = null;
        ApiKey = null;
        CurrentUserId = null;
        CurrentUserName = null;
        AuthScheme = null;
        ExpiresAtUtc = null;
        RequiresReauthentication = false;
        SessionStatusMessage = "Signed out. Local offline edits remain on this device until explicitly removed.";
    }

    public async Task<bool> ValidateConnectionAsync(string serverUrl, string? apiKey = null)
    {
        try
        {
            if (!TryNormalizeServerUri(serverUrl, out var uri, out _))
            {
                return false;
            }

            var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
            var validationPaths = hasApiKey ? AuthenticatedValidationPaths : ConnectionValidationPaths;
            foreach (var path in validationPaths)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, path));
                if (hasApiKey)
                {
                    request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
                }

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                if (IsAcceptedValidationStatus(response.StatusCode, hasApiKey))
                {
                    return true;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return false;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger?.LogDebug(ex, "Server connection validation failed");
            return false;
        }
    }

    private async Task<HttpResponseMessage> RequestCredentialTokenAsync(
        Uri serverUri,
        string tokenEndpointPath,
        string username,
        string password)
    {
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["grantType"] = "password",
            ["username"] = username,
            ["password"] = password
        };

        return await _httpClient.PostAsJsonAsync(
            new Uri(serverUri, tokenEndpointPath),
            payload).ConfigureAwait(false);
    }

    private async Task StoreTokenSessionAsync(Uri serverUri, ParsedAuthToken parsed, string tokenEndpointPath)
    {
        var normalizedServerUrl = serverUri.ToString().TrimEnd('/');
        await _sessionStore.SetAsync(ServerUrlKey, normalizedServerUrl).ConfigureAwait(false);
        await _sessionStore.SetAsync(UserIdKey, parsed.UserId).ConfigureAwait(false);
        await _sessionStore.SetAsync(UserNameKey, parsed.UserName).ConfigureAwait(false);
        await _sessionStore.SetAsync(TokenEndpointPathKey, tokenEndpointPath).ConfigureAwait(false);
        _sessionStore.Remove(ApiKeyKey);
        await _tokenStore.WriteAsync(parsed.Token).ConfigureAwait(false);

        ServerUrl = normalizedServerUrl;
        ApiKey = null;
        CurrentUserId = parsed.UserId;
        CurrentUserName = parsed.UserName;
        ApplyTokenToState(parsed.Token);
        RequiresReauthentication = false;
        SessionStatusMessage = "Signed in";
    }

    private async Task LoadStoredSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(ServerUrl))
        {
            ServerUrl = await _sessionStore.GetAsync(ServerUrlKey).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(ApiKey))
        {
            ApiKey = await _sessionStore.GetAsync(ApiKeyKey).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(CurrentUserId))
        {
            CurrentUserId = await _sessionStore.GetAsync(UserIdKey).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(CurrentUserName))
        {
            CurrentUserName = await _sessionStore.GetAsync(UserNameKey).ConfigureAwait(false);
        }

        var token = await _tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (token is not null)
        {
            ApplyTokenToState(token);
            return;
        }

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            AuthScheme = HonuaAuthScheme.ApiKey;
            ExpiresAtUtc = null;
        }
    }

    private async Task<IAuthTokenProvider> CreateRefreshProviderAsync()
    {
        if (_authTokenProvider is not null)
        {
            return _authTokenProvider;
        }

        if (string.IsNullOrWhiteSpace(ServerUrl) ||
            !Uri.TryCreate(ServerUrl, UriKind.Absolute, out var serverUri))
        {
            return new RefreshingAuthTokenProvider(_tokenStore, _httpClient);
        }

        var path = await _sessionStore.GetAsync(TokenEndpointPathKey).ConfigureAwait(false);
        var refreshEndpoint = Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(serverUri, string.IsNullOrWhiteSpace(path) ? TokenEndpointPaths[0] : path);

        return new RefreshingAuthTokenProvider(
            _tokenStore,
            _httpClient,
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = refreshEndpoint,
                TimeProvider = _timeProvider,
                RefreshSkew = RefreshSkew
            });
    }

    private void ApplyTokenToState(HonuaAuthToken token)
    {
        AuthScheme = token.Scheme;
        ExpiresAtUtc = token.ExpiresAtUtc;
        if (token.Scheme == HonuaAuthScheme.ApiKey && string.IsNullOrWhiteSpace(ApiKey))
        {
            ApiKey = token.AccessToken;
        }
        else if (token.Scheme == HonuaAuthScheme.Bearer)
        {
            ApiKey = null;
        }
    }

    private bool IsBearerExpired(HonuaAuthToken token) =>
        token.Scheme == HonuaAuthScheme.Bearer &&
        token.ExpiresAtUtc.HasValue &&
        token.ExpiresAtUtc.Value <= _timeProvider.GetUtcNow();

    private void MarkReauthenticationRequired(string message)
    {
        RequiresReauthentication = true;
        SessionStatusMessage = message;
    }

    private static async Task<ParsedAuthToken> ParseTokenResponseAsync(
        HttpResponseMessage response,
        DateTimeOffset nowUtc,
        string fallbackUserId,
        string fallbackUserName)
    {
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var payload = document.RootElement;

        var accessToken = ReadString(payload, "accessToken", "access_token")
            ?? throw new InvalidOperationException("Token response did not include an access token.");
        var refreshToken = ReadString(payload, "refreshToken", "refresh_token");
        var tokenType = ReadString(payload, "tokenType", "token_type");
        var scheme = string.Equals(tokenType, "api_key", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tokenType, "apiKey", StringComparison.OrdinalIgnoreCase)
                ? HonuaAuthScheme.ApiKey
                : HonuaAuthScheme.Bearer;
        var expiresAtUtc = ReadExpiresAt(payload, nowUtc);
        var userId = ReadString(payload, "userId", "user_id", "sub", "subject") ?? fallbackUserId;
        var userName = ReadString(payload, "userName", "user_name", "name", "username") ?? fallbackUserName;
        var token = new HonuaAuthToken(scheme, accessToken, refreshToken, expiresAtUtc);

        return new ParsedAuthToken(token, userId, userName);
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

        if (payload.TryGetProperty("expiresIn", out var expiresIn) ||
            payload.TryGetProperty("expires_in", out expiresIn))
        {
            if (expiresIn.TryGetInt64(out var seconds))
            {
                return nowUtc.AddSeconds(seconds);
            }
        }

        return null;
    }

    private static bool TryNormalizeServerUri(string serverUrl, out Uri normalizedUri, out string errorMessage)
    {
        normalizedUri = null!;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            errorMessage = "Server URL is required";
            return false;
        }

        if (!Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var uri))
        {
            errorMessage = "Server URL must be absolute";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            errorMessage = "Server URL must use HTTPS unless it points to localhost";
            return false;
        }

        normalizedUri = uri;
        return true;
    }

    private static bool IsAcceptedValidationStatus(HttpStatusCode statusCode, bool hasApiKey)
    {
        var status = (int)statusCode;
        if (status is >= 200 and < 400)
        {
            return true;
        }

        return !hasApiKey && statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record ParsedAuthToken(HonuaAuthToken Token, string UserId, string UserName);
}
