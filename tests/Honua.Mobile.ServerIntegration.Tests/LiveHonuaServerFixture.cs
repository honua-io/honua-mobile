using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class LiveHonuaServerFixture : IAsyncLifetime
{
    public const string DefaultAdminPassword = "live-image-test-admin-password";

    private const string PostgresAlias = "postgres";
    private const string PostgresDatabase = "honua_dev";
    private const string PostgresUser = "honua_user";
    private const string PostgresPassword = "honua_password";
    private const int HonuaHttpPort = 8080;
    private const int HonuaGrpcPort = 8081;

    private INetwork? _network;
    private IContainer? _postgres;
    private IContainer? _honua;

    public LiveHonuaServerOptions Options { get; private set; } = LiveHonuaServerOptions.Disabled;

    public bool Enabled => Options.Enabled;

    public Uri BaseUri { get; private set; } = new("http://127.0.0.1/");

    public async Task InitializeAsync()
    {
        Options = LiveHonuaServerOptions.FromEnvironment();
        if (!Options.Enabled)
        {
            return;
        }

        if (Options.BaseUri is not null)
        {
            BaseUri = Options.BaseUri;
            CompleteDerivedOptions();
            await WaitForReadyAsync();
            return;
        }

        await StartImageStackAsync();
        CompleteDerivedOptions();
        await WaitForReadyAsync();
    }

    public async Task DisposeAsync()
    {
        if (_honua is not null)
        {
            await _honua.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DisposeAsync();
        }
    }

    public Uri Uri(string relativePath)
    {
        if (System.Uri.TryCreate(relativePath, UriKind.Absolute, out var absoluteUri) &&
            (string.Equals(absoluteUri.Scheme, System.Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(absoluteUri.Scheme, System.Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return absoluteUri;
        }

        return new Uri(BaseUri, relativePath.TrimStart('/'));
    }

    private void CompleteDerivedOptions()
    {
        Options = Options with
        {
            SceneAssetBaseUri = Options.SceneAssetBaseUri
                ?? Uri($"/scenes/{Options.SceneId}/"),
        };
    }

    private async Task StartImageStackAsync()
    {
        _network = new NetworkBuilder()
            .WithName($"honua-mobile-live-{Guid.NewGuid():N}")
            .Build();
        await _network.CreateAsync();

        _postgres = new ContainerBuilder(Options.PostgresImage)
            .WithNetwork(_network)
            .WithNetworkAliases(PostgresAlias)
            .WithEnvironment("POSTGRES_DB", PostgresDatabase)
            .WithEnvironment("POSTGRES_USER", PostgresUser)
            .WithEnvironment("POSTGRES_PASSWORD", PostgresPassword)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilCommandIsCompleted(["pg_isready", "-h", "127.0.0.1", "-U", PostgresUser, "-d", PostgresDatabase]))
            .Build();
        await _postgres.StartAsync();
        await WaitForPostgresReadyAsync();

        _honua = new ContainerBuilder(Options.Image)
            .WithNetwork(_network)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("Kestrel__Endpoints__Http__Url", $"http://+:{HonuaHttpPort.ToString(CultureInfo.InvariantCulture)}")
            .WithEnvironment("Kestrel__Endpoints__Http__Protocols", "Http1")
            .WithEnvironment("Kestrel__Endpoints__Grpc__Url", $"http://+:{HonuaGrpcPort.ToString(CultureInfo.InvariantCulture)}")
            .WithEnvironment("Kestrel__Endpoints__Grpc__Protocols", "Http2")
            .WithEnvironment("ConnectionStrings__DefaultConnection", BuildPostgresConnectionString())
            .WithEnvironment("HONUA_ADMIN_PASSWORD", DefaultAdminPassword)
            .WithEnvironment("Security__ConnectionEncryption__MasterKey", "mobile-live-image-test-master-key-32")
            .WithEnvironment("Security__ConnectionEncryption__Salt", "bW9iaWxlLWxpdmUtaW1hZ2Utc2FsdA==")
            .WithPortBinding(HonuaHttpPort, assignRandomHostPort: true)
            .WithPortBinding(HonuaGrpcPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request
                    .ForPort((ushort)HonuaHttpPort)
                    .ForPath(Options.ReadyPath)))
            .Build();
        await _honua.StartAsync();

        BaseUri = new Uri($"http://127.0.0.1:{_honua.GetMappedPublicPort(HonuaHttpPort).ToString(CultureInfo.InvariantCulture)}/");
        Options = Options.WithTestcontainersGrpcEndpoint(
            new Uri($"http://127.0.0.1:{_honua.GetMappedPublicPort(HonuaGrpcPort).ToString(CultureInfo.InvariantCulture)}/"));
        await ApplyFixtureSeedAsync();
    }

    private async Task WaitForPostgresReadyAsync()
    {
        if (_postgres is null)
        {
            return;
        }

        string? lastError = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var result = await _postgres.ExecAsync(
                [
                    "psql",
                    "-v",
                    "ON_ERROR_STOP=1",
                    "-h",
                    "127.0.0.1",
                    "-p",
                    "5432",
                    "-U",
                    PostgresUser,
                    "-d",
                    PostgresDatabase,
                    "-c",
                    "select 1;",
                ],
                CancellationToken.None);

            if (result.ExitCode == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                return;
            }

            lastError = result.Stderr + result.Stdout;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException($"PostGIS container did not accept SQL before Honua startup: {lastError}");
    }

    private static string BuildPostgresConnectionString()
        => $"Host={PostgresAlias};Database={PostgresDatabase};Username={PostgresUser};Password={PostgresPassword}";

    private async Task ApplyFixtureSeedAsync()
    {
        if (_postgres is null || string.IsNullOrWhiteSpace(Options.FixtureSqlPath))
        {
            return;
        }

        var seedFile = new FileInfo(Options.FixtureSqlPath);
        if (!seedFile.Exists)
        {
            throw new InvalidOperationException(
                $"HONUA_MOBILE_LIVE_SERVER_FIXTURE_SQL points to a missing file: {seedFile.FullName}");
        }

        const string containerSeedDirectory = "/tmp/honua-mobile-live-fixture";
        var containerSeedPath = $"{containerSeedDirectory}/{seedFile.Name}";
        await _postgres.CopyAsync(
            seedFile,
            containerSeedDirectory,
            uid: 0,
            gid: 0,
            UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.GroupRead | UnixFileModes.OtherRead,
            CancellationToken.None);

        var result = await _postgres.ExecAsync(
            [
                "psql",
                "-v",
                "ON_ERROR_STOP=1",
                "-U",
                PostgresUser,
                "-d",
                PostgresDatabase,
                "-f",
                containerSeedPath,
            ],
            CancellationToken.None);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Honua live fixture seed failed with exit code {result.ExitCode}: {result.Stderr}{result.Stdout}");
        }
    }

    private async Task WaitForReadyAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var readyUri = Uri(Options.ReadyPath);
        Exception? lastError = null;

        for (var attempt = 0; attempt < 90; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(readyUri);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                lastError = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new InvalidOperationException(
            $"Honua live server did not become ready at {readyUri}.",
            lastError);
    }
}

public sealed record LiveHonuaServerOptions
{
    public static readonly LiveHonuaServerOptions Disabled = new();

    public bool Enabled { get; init; }

    public Uri? BaseUri { get; init; }

    public string Image { get; init; } = "honuaio/honua-server:latest";

    public string PostgresImage { get; init; } = "postgis/postgis:17-3.5-alpine";

    public string? FixtureSqlPath { get; init; }

    public string ReadyPath { get; init; } = "/healthz/ready";

    public string ServiceId { get; init; } = "mobile_offline_demo";

    public int LayerId { get; init; } = 68910;

    public IReadOnlyList<int> ReplicaLayerIds { get; init; } = [68910, 68920];

    public string OgcCollectionId { get; init; } = "68910";

    public string SceneId { get; init; } = "downtown-honolulu";

    public string RoutingServiceId { get; init; } = "Routing";

    public Uri? GrpcEndpoint { get; init; }

    public string ApiKey { get; init; } = LiveHonuaServerFixture.DefaultAdminPassword;

    public string BearerToken { get; init; } = "live-image-test-bearer-token";

    public string OAuthRefreshPath { get; init; } = "/oauth/token";

    public string ExceptionUploadPath { get; init; } = "/api/mobile/exceptions";

    public Uri? SceneAssetBaseUri { get; init; }

    public static LiveHonuaServerOptions FromEnvironment()
    {
        return FromEnvironment(Environment.GetEnvironmentVariable);
    }

    internal static LiveHonuaServerOptions FromEnvironment(Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);

        var enabled = IsTruthy(readVariable("HONUA_MOBILE_LIVE_SERVER_TESTS"));
        var baseUri = TryUri(readVariable("HONUA_MOBILE_LIVE_SERVER_BASE_URL"));
        var layerId = ReadInt(readVariable, "HONUA_MOBILE_LIVE_SERVER_LAYER_ID", 68910);

        return new LiveHonuaServerOptions
        {
            Enabled = enabled,
            BaseUri = baseUri,
            Image = ReadString(readVariable, "HONUA_MOBILE_LIVE_SERVER_IMAGE", "honuaio/honua-server:latest"),
            PostgresImage = ReadString(readVariable, "HONUA_MOBILE_LIVE_SERVER_POSTGRES_IMAGE", "postgis/postgis:17-3.5-alpine"),
            FixtureSqlPath = EmptyToNull(readVariable("HONUA_MOBILE_LIVE_SERVER_FIXTURE_SQL")),
            ReadyPath = ReadPath(readVariable, "HONUA_MOBILE_LIVE_SERVER_READY_PATH", "/healthz/ready"),
            ServiceId = ReadString(readVariable, "HONUA_MOBILE_LIVE_SERVER_SERVICE_ID", "mobile_offline_demo"),
            LayerId = layerId,
            ReplicaLayerIds = ReadIntList(readVariable, "HONUA_MOBILE_LIVE_SERVER_REPLICA_LAYER_IDS", [layerId, 68920]),
            OgcCollectionId = ReadString(readVariable, "HONUA_MOBILE_LIVE_SERVER_OGC_COLLECTION_ID", layerId.ToString(CultureInfo.InvariantCulture)),
            SceneId = ReadString(readVariable, "HONUA_MOBILE_LIVE_SERVER_SCENE_ID", "downtown-honolulu"),
            RoutingServiceId = ReadString(readVariable, "HONUA_MOBILE_LIVE_SERVER_ROUTING_SERVICE_ID", "Routing"),
            GrpcEndpoint = TryUri(readVariable("HONUA_MOBILE_LIVE_SERVER_GRPC_URL")),
            ApiKey = ReadString(readVariable, "HONUA_MOBILE_LIVE_SERVER_API_KEY", LiveHonuaServerFixture.DefaultAdminPassword),
            BearerToken = ReadString(readVariable, "HONUA_MOBILE_LIVE_SERVER_BEARER_TOKEN", "live-image-test-bearer-token"),
            OAuthRefreshPath = ReadPath(readVariable, "HONUA_MOBILE_LIVE_SERVER_OAUTH_REFRESH_PATH", "/oauth/token"),
            ExceptionUploadPath = ReadPath(readVariable, "HONUA_MOBILE_LIVE_SERVER_EXCEPTION_UPLOAD_PATH", "/api/mobile/exceptions"),
            SceneAssetBaseUri = TryUri(readVariable("HONUA_MOBILE_LIVE_SERVER_SCENE_ASSET_BASE_URL")),
        };
    }

    internal LiveHonuaServerOptions WithTestcontainersGrpcEndpoint(Uri grpcEndpoint)
    {
        ArgumentNullException.ThrowIfNull(grpcEndpoint);

        return GrpcEndpoint is null
            ? this with { GrpcEndpoint = grpcEndpoint }
            : this;
    }

    private static bool IsTruthy(string? value)
        => value is not null &&
           (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase));

    private static Uri? TryUri(string? value)
    {
        return Uri.TryCreate(EmptyToNull(value), UriKind.Absolute, out var uri)
            ? EnsureDirectoryUri(uri)
            : null;
    }

    private static Uri EnsureDirectoryUri(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && !uri.AbsolutePath.EndsWith('/'))
        {
            return new Uri(uri.AbsoluteUri + "/");
        }

        return uri;
    }

    private static string ReadString(Func<string, string?> readVariable, string name, string fallback)
        => EmptyToNull(readVariable(name)) ?? fallback;

    private static string ReadPath(Func<string, string?> readVariable, string name, string fallback)
    {
        var value = ReadString(readVariable, name, fallback);
        return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
    }

    private static int ReadInt(Func<string, string?> readVariable, string name, int fallback)
        => int.TryParse(readVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static IReadOnlyList<int> ReadIntList(Func<string, string?> readVariable, string name, IReadOnlyList<int> fallback)
    {
        var value = readVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
