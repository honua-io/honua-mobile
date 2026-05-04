using System.ComponentModel;
using Honua.Mobile.FieldCollection.Services;
using Microsoft.Maui.Networking;

namespace Honua.Mobile.FieldCollection.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}

internal sealed class TestAuthenticationService : IAuthenticationService
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAuthenticated { get; init; } = true;
    public string? CurrentUserId { get; init; } = "test-user";
    public string? CurrentUserName { get; init; } = "Test User";
    public string? ApiKey { get; init; } = "test-api-key";
    public string? ServerUrl { get; init; } = "https://api.honua.test";

    public Task<AuthenticationResult> AuthenticateAsync(string serverUrl, string apiKey)
    {
        return Task.FromResult(AuthenticationResult.Success("test-user", "Test User", apiKey));
    }

    public Task<AuthenticationResult> AuthenticateWithCredentialsAsync(
        string serverUrl,
        string username,
        string password)
    {
        return Task.FromResult(AuthenticationResult.Failure("Not configured."));
    }

    public Task<bool> RefreshTokenAsync()
    {
        return Task.FromResult(true);
    }

    public Task LogoutAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateConnectionAsync(string serverUrl, string? apiKey = null)
    {
        return Task.FromResult(true);
    }

    public void RaiseChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class TestConnectivityService : IConnectivityService
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<bool>? ConnectivityChanged;

    public bool IsConnected { get; init; } = true;
    public NetworkAccess NetworkAccess => IsConnected ? NetworkAccess.Internet : NetworkAccess.None;
    public ConnectionProfile ConnectionProfile => ConnectionProfile.WiFi;

    public void RaiseChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
        ConnectivityChanged?.Invoke(this, IsConnected);
    }
}
