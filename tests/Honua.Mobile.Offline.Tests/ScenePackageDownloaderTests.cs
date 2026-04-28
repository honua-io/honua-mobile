using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.ScenePackages;
using Honua.Mobile.Sdk.Scenes;

namespace Honua.Mobile.Offline.Tests;

public sealed class ScenePackageDownloaderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);
    private readonly string _rootDirectory;

    public ScenePackageDownloaderTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"honua-scene-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public async Task DownloadAsync_WritesPackageAndRegistersScenePackage()
    {
        var metadata = Encoding.UTF8.GetBytes("""{"sceneId":"downtown-honolulu"}""");
        var tileset = Encoding.UTF8.GetBytes("""{"asset":{"version":"1.1"}}""");
        var manifest = CreateManifest(
            CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", metadata, "\"meta-1\""),
            CreateAsset("buildings-tileset", HonuaScenePackageAssetTypes.ThreeDimensionalTileset, "tilesets/buildings/tileset.json", tileset, "\"tiles-1\""));
        var store = CreateStore();
        var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            var payload = ResolvePayload(request, metadata, tileset);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            };
            response.Headers.ETag = EntityTagHeaderValue.Parse(request.RequestUri!.AbsolutePath.EndsWith("scene.json", StringComparison.Ordinal)
                ? "\"meta-1\""
                : "\"tiles-1\"");
            return Task.FromResult(response);
        }));
        var downloader = new ScenePackageDownloader(httpClient, store);

        var result = await downloader.DownloadAsync(CreateRequest(manifest));

        Assert.Equal(2, result.DownloadedAssetCount);
        Assert.Equal(metadata.Length + tileset.Length, result.DownloadedBytes);
        Assert.True(File.Exists(Path.Combine(result.PackageDirectory, "metadata", "scene.json")));
        Assert.True(File.Exists(Path.Combine(result.PackageDirectory, "tilesets", "buildings", "tileset.json")));
        Assert.True(File.Exists(result.ManifestPath));

        var records = await store.ListScenePackagesAsync();
        var record = Assert.Single(records);
        Assert.Equal("pkg_downtown_honolulu_2026_04", record.PackageId);
        Assert.Equal("downtown-honolulu", record.SceneId);
        Assert.Equal(HonuaScenePackageState.Ready, record.State);
        Assert.Equal(result.PackageDirectory, record.PackageDirectory);
        Assert.Equal(2, record.RequiredAssetCount);
        Assert.Equal(metadata.Length + tileset.Length, record.DownloadedBytes);
        Assert.Empty(record.MissingOptionalAssetKeys);

        await store.DeleteScenePackageAsync(record.PackageId);
        Assert.Empty(await store.ListScenePackagesAsync());
    }

    [Fact]
    public async Task DownloadAsync_ResumesPartialAssetWithRangeRequest()
    {
        var payload = Encoding.UTF8.GetBytes("0123456789abcdef");
        var manifest = CreateManifest(
            CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", payload, "\"meta-2\""));
        var outputDirectory = Path.Combine(_rootDirectory, "packages");
        var partialPath = Path.Combine(outputDirectory, "pkg_downtown_honolulu_2026_04.partial", "metadata", "scene.json.partial");
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, payload[..5]);
        long? requestedRangeStart = null;

        var store = CreateStore();
        var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requestedRangeStart = request.Headers.Range?.Ranges.Single().From;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(payload[5..]),
            };
            response.Headers.ETag = EntityTagHeaderValue.Parse("\"meta-2\"");
            return Task.FromResult(response);
        }));
        var downloader = new ScenePackageDownloader(httpClient, store);

        var result = await downloader.DownloadAsync(CreateRequest(manifest, outputDirectory: outputDirectory));

        Assert.Equal(5, requestedRangeStart);
        Assert.Equal(payload.Length - 5, result.DownloadedBytes);
        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(result.PackageDirectory, "metadata", "scene.json")));
        Assert.Equal(payload.Length, result.CatalogRecord.DownloadedBytes);
    }

    [Fact]
    public async Task DownloadAsync_OptionalAssetFailure_RegistersMissingKey()
    {
        var metadata = Encoding.UTF8.GetBytes("""{"sceneId":"downtown-honolulu"}""");
        var optional = Encoding.UTF8.GetBytes("expected-optional");
        var wrongOptional = Encoding.UTF8.GetBytes("wrong-optional");
        var manifest = CreateManifest(
            CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", metadata),
            CreateAsset(
                "texture-overview",
                HonuaScenePackageAssetTypes.Texture,
                "textures/overview.bin",
                optional,
                required: false));
        var store = CreateStore();
        var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            var payload = request.RequestUri!.AbsolutePath.EndsWith("scene.json", StringComparison.Ordinal)
                ? metadata
                : wrongOptional;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }));
        var downloader = new ScenePackageDownloader(httpClient, store);

        var result = await downloader.DownloadAsync(CreateRequest(manifest));

        Assert.Equal(1, result.DownloadedAssetCount);
        Assert.Equal(HonuaScenePackageState.Ready, result.CatalogRecord.State);
        Assert.Equal(["texture-overview"], result.CatalogRecord.MissingOptionalAssetKeys);
        Assert.False(File.Exists(Path.Combine(result.PackageDirectory, "textures", "overview.bin")));
        var record = Assert.Single(await store.ListScenePackagesAsync());
        Assert.Equal(["texture-overview"], record.MissingOptionalAssetKeys);
    }

    [Fact]
    public async Task DownloadAsync_ChecksumFailure_ThrowsAndCleansPartialPackage()
    {
        var payload = Encoding.UTF8.GetBytes("expected");
        var wrongPayload = Encoding.UTF8.GetBytes("wrongone");
        var manifest = CreateManifest(
            CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", payload));
        var outputDirectory = Path.Combine(_rootDirectory, "packages");
        var store = CreateStore();
        var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(wrongPayload),
            });
        }));
        var downloader = new ScenePackageDownloader(httpClient, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync(CreateRequest(manifest, outputDirectory: outputDirectory)));

        Assert.Contains("SHA-256", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(outputDirectory, "pkg_downtown_honolulu_2026_04.partial")));
        await store.InitializeAsync();
        Assert.Empty(await store.ListScenePackagesAsync());
    }

    [Fact]
    public async Task DownloadAsync_ETagMismatch_ThrowsAndCleansPartialPackage()
    {
        var payload = Encoding.UTF8.GetBytes("metadata");
        var manifest = CreateManifest(
            CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", payload, "\"expected\""));
        var outputDirectory = Path.Combine(_rootDirectory, "packages");
        var store = CreateStore();
        var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            };
            response.Headers.ETag = EntityTagHeaderValue.Parse("\"different\"");
            return Task.FromResult(response);
        }));
        var downloader = new ScenePackageDownloader(httpClient, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync(CreateRequest(manifest, outputDirectory: outputDirectory)));

        Assert.Contains("ETag", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(outputDirectory, "pkg_downtown_honolulu_2026_04.partial")));
    }

    [Fact]
    public async Task DownloadAsync_ExpiredAuth_DoesNotSendRequests()
    {
        var payload = Encoding.UTF8.GetBytes("metadata");
        var manifest = CreateManifest(
            CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", payload),
            authExpiresAtUtc: Now.AddMinutes(-1));
        var requestCount = 0;
        var store = CreateStore();
        var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }));
        var downloader = new ScenePackageDownloader(httpClient, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.DownloadAsync(CreateRequest(manifest)));

        Assert.Contains("credentials have expired", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task DownloadAsync_OverBudgetManifest_DoesNotSendRequests()
    {
        var payload = Encoding.UTF8.GetBytes("metadata");
        var manifest = CreateManifest(
            CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", payload),
            maxPackageBytes: payload.Length - 1,
            declaredBytes: payload.Length);
        var requestCount = 0;
        var store = CreateStore();
        var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }));
        var downloader = new ScenePackageDownloader(httpClient, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.DownloadAsync(CreateRequest(manifest)));

        Assert.Contains(HonuaScenePackageValidationCodes.OverByteBudget, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task DownloadAsync_Cancellation_CleansPartialPackage()
    {
        var payload = Encoding.UTF8.GetBytes("metadata");
        var manifest = CreateManifest(
            CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", payload));
        var outputDirectory = Path.Combine(_rootDirectory, "packages");
        var store = CreateStore();
        var httpClient = new HttpClient(new StubHttpMessageHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }));
        var downloader = new ScenePackageDownloader(httpClient, store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            downloader.DownloadAsync(CreateRequest(manifest, outputDirectory: outputDirectory), cts.Token));

        Assert.False(Directory.Exists(Path.Combine(outputDirectory, "pkg_downtown_honolulu_2026_04.partial")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private GeoPackageSyncStore CreateStore()
        => new(new GeoPackageSyncStoreOptions
        {
            DatabasePath = Path.Combine(_rootDirectory, "sync-store.gpkg"),
        });

    private ScenePackageDownloadRequest CreateRequest(
        HonuaScenePackageManifest manifest,
        string? outputDirectory = null)
        => new()
        {
            Manifest = manifest,
            AssetBaseUri = new Uri("https://cdn.honua.test/packages/pkg_downtown_honolulu_2026_04/"),
            OutputDirectory = outputDirectory ?? Path.Combine(_rootDirectory, "packages"),
            UtcNow = Now,
        };

    private static HonuaScenePackageManifest CreateManifest(
        HonuaScenePackageAsset asset,
        DateTimeOffset? authExpiresAtUtc = null,
        long? maxPackageBytes = null,
        long? declaredBytes = null)
        => CreateManifest([asset], authExpiresAtUtc, maxPackageBytes, declaredBytes);

    private static HonuaScenePackageManifest CreateManifest(
        HonuaScenePackageAsset firstAsset,
        HonuaScenePackageAsset secondAsset)
        => CreateManifest([firstAsset, secondAsset]);

    private static HonuaScenePackageManifest CreateManifest(
        IReadOnlyList<HonuaScenePackageAsset> assets,
        DateTimeOffset? authExpiresAtUtc = null,
        long? maxPackageBytes = null,
        long? declaredBytes = null)
    {
        var assetBytes = assets.Sum(asset => asset.Bytes ?? 0);
        return new HonuaScenePackageManifest
        {
            SchemaVersion = HonuaScenePackageManifest.CurrentSchemaVersion,
            PackageId = "pkg_downtown_honolulu_2026_04",
            SceneId = "downtown-honolulu",
            DisplayName = "Downtown Honolulu 3D",
            EditionGate = HonuaScenePackageEditionGates.Pro,
            ServerRevision = "scene-rev-42",
            CreatedAtUtc = Now.AddHours(-1),
            StaleAfterUtc = Now.AddDays(30),
            OfflineUseExpiresAtUtc = Now.AddDays(60),
            AuthExpiresAtUtc = authExpiresAtUtc ?? Now.AddDays(1),
            Extent = new HonuaSceneBounds
            {
                MinLongitude = -157.872,
                MinLatitude = 21.293,
                MaxLongitude = -157.841,
                MaxLatitude = 21.319,
            },
            Lod = new HonuaScenePackageLod
            {
                MinZoom = 12,
                MaxZoom = 17,
                MaxGeometricErrorMeters = 4.0,
            },
            ByteBudget = new HonuaScenePackageByteBudget
            {
                MaxPackageBytes = maxPackageBytes ?? assetBytes + 1024,
                DeclaredBytes = declaredBytes ?? assetBytes,
            },
            Attribution = ["Honua"],
            Assets = assets,
        };
    }

    private static HonuaScenePackageAsset CreateAsset(
        string key,
        string type,
        string path,
        byte[] payload,
        string? etag = null,
        bool required = true)
        => new()
        {
            Key = key,
            Type = type,
            Role = "metadata",
            Path = path,
            ContentType = "application/octet-stream",
            Bytes = payload.Length,
            Sha256 = Sha256Hex(payload),
            ETag = etag,
            Required = required,
        };

    private static byte[] ResolvePayload(HttpRequestMessage request, byte[] metadata, byte[] tileset)
        => request.RequestUri!.AbsolutePath.EndsWith("scene.json", StringComparison.Ordinal)
            ? metadata
            : tileset;

    private static string Sha256Hex(byte[] payload)
        => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
