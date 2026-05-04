using System.ComponentModel;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Honua.Mobile.FieldCollection.Services;

public interface IAuthenticationService : INotifyPropertyChanged
{
    bool IsAuthenticated { get; }
    string? CurrentUserId { get; }
    string? CurrentUserName { get; }
    string? ApiKey { get; }
    string? ServerUrl { get; }

    Task<AuthenticationResult> AuthenticateAsync(string serverUrl, string apiKey);
    Task<AuthenticationResult> AuthenticateWithCredentialsAsync(string serverUrl, string username, string password);
    Task<bool> RefreshTokenAsync();
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

    public static AuthenticationResult Success(string userId, string userName, string token, DateTime? expiresAt = null) =>
        new() { IsSuccess = true, UserId = userId, UserName = userName, Token = token, ExpiresAt = expiresAt };

    public static AuthenticationResult Failure(string errorMessage) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage };
}

public class AuthenticationService : IAuthenticationService
{
    private static readonly string[] ConnectionValidationPaths =
    {
        "/health",
        "/api/health"
    };

    private static readonly string[] AuthenticatedValidationPaths =
    {
        "/api/scenes?f=json"
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthenticationService>? _logger;
    private string? _currentUserId;
    private string? _currentUserName;
    private string? _apiKey;
    private string? _serverUrl;

    public AuthenticationService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
    {
    }

    public AuthenticationService(HttpClient httpClient, ILogger<AuthenticationService>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;

        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout > TimeSpan.FromSeconds(10))
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAuthenticated => !string.IsNullOrEmpty(ServerUrl) && !string.IsNullOrEmpty(ApiKey);

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

            var isValid = await ValidateConnectionAsync(serverUrl, apiKey);
            if (!isValid)
            {
                return AuthenticationResult.Failure("Unable to connect to server or invalid API key");
            }

            var normalizedServerUrl = normalizedUri.ToString().TrimEnd('/');
            var userId = normalizedUri.Host;
            var userName = $"API key ({normalizedUri.Host})";

            await SecureStorage.SetAsync("api_key", apiKey);
            await SecureStorage.SetAsync("server_url", normalizedServerUrl);
            await SecureStorage.SetAsync("user_id", userId);
            await SecureStorage.SetAsync("user_name", userName);

            ServerUrl = normalizedServerUrl;
            ApiKey = apiKey;
            CurrentUserId = userId;
            CurrentUserName = userName;

            return AuthenticationResult.Success(CurrentUserId, CurrentUserName, apiKey);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "API-key authentication failed");
            return AuthenticationResult.Failure("Authentication failed. Check the server URL and API key.");
        }
    }

    public Task<AuthenticationResult> AuthenticateWithCredentialsAsync(string serverUrl, string username, string password)
    {
        return Task.FromResult(AuthenticationResult.Failure(
            "Username/password authentication is not configured for this app. Use an API key."));
    }

    public async Task<bool> RefreshTokenAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(ServerUrl) || string.IsNullOrEmpty(ApiKey))
                return false;

            // TODO: Implement token refresh logic
            return await ValidateConnectionAsync(ServerUrl, ApiKey);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Token refresh validation failed");
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        // Clear secure storage
        SecureStorage.Remove("server_url");
        SecureStorage.Remove("api_key");
        SecureStorage.Remove("user_id");
        SecureStorage.Remove("user_name");

        // Clear properties
        ServerUrl = null;
        ApiKey = null;
        CurrentUserId = null;
        CurrentUserName = null;

        await Task.CompletedTask;
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
                    HttpCompletionOption.ResponseHeadersRead);

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
}
