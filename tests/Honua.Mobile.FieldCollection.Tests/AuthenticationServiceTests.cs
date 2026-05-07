using System.Net;
using Honua.Mobile.FieldCollection.Services;

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
}
