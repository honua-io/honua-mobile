namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class LiveHonuaServerFixtureOptionsTests
{
    [Fact]
    public void FromEnvironment_uses_explicit_grpc_endpoint_override()
    {
        var options = LiveHonuaServerOptions.FromEnvironment(name => name switch
        {
            "HONUA_MOBILE_LIVE_SERVER_TESTS" => "1",
            "HONUA_MOBILE_LIVE_SERVER_BASE_URL" => "http://127.0.0.1:18080",
            "HONUA_MOBILE_LIVE_SERVER_GRPC_URL" => "http://127.0.0.1:18081",
            _ => null,
        });

        Assert.True(options.Enabled);
        Assert.Equal(new Uri("http://127.0.0.1:18080/"), options.BaseUri);
        Assert.Equal(new Uri("http://127.0.0.1:18081/"), options.GrpcEndpoint);
    }

    [Fact]
    public void FromEnvironment_with_prestarted_base_uri_and_no_grpc_override_leaves_grpc_endpoint_unset()
    {
        var options = LiveHonuaServerOptions.FromEnvironment(name => name switch
        {
            "HONUA_MOBILE_LIVE_SERVER_TESTS" => "1",
            "HONUA_MOBILE_LIVE_SERVER_BASE_URL" => "http://127.0.0.1:18080",
            _ => null,
        });

        Assert.True(options.Enabled);
        Assert.Equal(new Uri("http://127.0.0.1:18080/"), options.BaseUri);
        Assert.Null(options.GrpcEndpoint);
    }

    [Fact]
    public void WithTestcontainersGrpcEndpoint_sets_derived_grpc_endpoint_when_no_override_exists()
    {
        var options = new LiveHonuaServerOptions();
        var grpcEndpoint = new Uri("http://127.0.0.1:18081/");

        var derived = options.WithTestcontainersGrpcEndpoint(grpcEndpoint);

        Assert.Equal(grpcEndpoint, derived.GrpcEndpoint);
    }

    [Fact]
    public void WithTestcontainersGrpcEndpoint_keeps_explicit_grpc_endpoint_override()
    {
        var explicitEndpoint = new Uri("http://127.0.0.1:19081/");
        var options = new LiveHonuaServerOptions
        {
            GrpcEndpoint = explicitEndpoint,
        };

        var derived = options.WithTestcontainersGrpcEndpoint(new Uri("http://127.0.0.1:18081/"));

        Assert.Equal(explicitEndpoint, derived.GrpcEndpoint);
    }
}
