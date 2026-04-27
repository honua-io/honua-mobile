using System.ComponentModel;
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
    private string? _currentUserId;
    private string? _currentUserName;
    private string? _apiKey;
    private string? _serverUrl;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAuthenticated => !string.IsNullOrEmpty(CurrentUserId) && !string.IsNullOrEmpty(ApiKey);

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
            // Validate connection first
            var isValid = await ValidateConnectionAsync(serverUrl, apiKey);
            if (!isValid)
            {
                return AuthenticationResult.Failure("Unable to connect to server or invalid API key");
            }

            // Store credentials securely
            await SecureStorage.SetAsync("server_url", serverUrl);
            await SecureStorage.SetAsync("api_key", apiKey);
            await SecureStorage.SetAsync("user_id", "demo_user"); // TODO: Get from server
            await SecureStorage.SetAsync("user_name", "Demo User"); // TODO: Get from server

            // Update properties
            ServerUrl = serverUrl;
            ApiKey = apiKey;
            CurrentUserId = "demo_user";
            CurrentUserName = "Demo User";

            return AuthenticationResult.Success(CurrentUserId, CurrentUserName, apiKey);
        }
        catch (Exception ex)
        {
            return AuthenticationResult.Failure($"Authentication failed: {ex.Message}");
        }
    }

    public async Task<AuthenticationResult> AuthenticateWithCredentialsAsync(string serverUrl, string username, string password)
    {
        try
        {
            // TODO: Implement OAuth/credential-based authentication
            // For now, simulate with API key approach
            var demoApiKey = $"demo_key_{username}";
            return await AuthenticateAsync(serverUrl, demoApiKey);
        }
        catch (Exception ex)
        {
            return AuthenticationResult.Failure($"Credential authentication failed: {ex.Message}");
        }
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
        catch
        {
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
            // TODO: Implement actual server connection validation
            // For now, simulate with basic validation
            if (string.IsNullOrEmpty(serverUrl))
                return false;

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
                return false;

            await Task.Delay(500); // Simulate network call
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
