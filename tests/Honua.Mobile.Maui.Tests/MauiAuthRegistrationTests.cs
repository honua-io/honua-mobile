using Honua.Mobile.Maui;
using Honua.Mobile.Maui.Auth;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.Maui.Tests;

public sealed class MauiAuthRegistrationTests
{
    [Fact]
    public void AddHonuaMobileAuth_RegistersProviderAndWiresClient()
    {
        var store = new InMemoryAuthTokenStore();

        using var provider = new ServiceCollection()
            .AddHonuaMobileAuth(store)
            .AddHonuaMobileSdk(new HonuaMobileClientOptions
            {
                BaseUri = new Uri("https://api.honua.test"),
                PreferGrpcForFeatureQueries = false,
            })
            .BuildServiceProvider();

        Assert.Same(store, provider.GetRequiredService<IAuthTokenStore>());
        Assert.IsType<RefreshingAuthTokenProvider>(provider.GetRequiredService<IAuthTokenProvider>());
        Assert.NotNull(provider.GetRequiredService<HonuaMobileClient>());
    }

    [Fact]
    public async Task MauiSecureAuthTokenStore_RoundTripsToken()
    {
        var storage = new FakeSecureStorage();
        var store = new MauiSecureAuthTokenStore(storage);

        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "access-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddHours(1)));

        var token = await store.ReadAsync();

        Assert.NotNull(token);
        Assert.Equal(HonuaAuthScheme.Bearer, token.Scheme);
        Assert.Equal("access-token", token.AccessToken);
        Assert.Equal("refresh-token", token.RefreshToken);
    }

    private sealed class FakeSecureStorage : IMauiSecureStorage
    {
        private string? _value;

        public ValueTask<string?> GetAsync(string key, CancellationToken ct = default)
            => ValueTask.FromResult(_value);

        public ValueTask SetAsync(string key, string value, CancellationToken ct = default)
        {
            _value = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
        {
            _value = null;
            return ValueTask.CompletedTask;
        }
    }
}
