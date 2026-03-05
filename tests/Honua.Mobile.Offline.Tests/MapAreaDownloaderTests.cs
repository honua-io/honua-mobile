using System.Net;
using System.Text;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.MapAreas;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.Tests;

public sealed class MapAreaDownloaderTests : IDisposable
{
    private readonly string _rootDirectory;

    public MapAreaDownloaderTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"honua-maparea-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public async Task DownloadAsync_WritesGeoPackageAndRegistersMapArea()
    {
        var storePath = Path.Combine(_rootDirectory, "sync-store.gpkg");
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = storePath });

        var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            var payload = Encoding.UTF8.GetBytes($"payload:{request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }));

        var downloader = new MapAreaDownloader(httpClient, store);
        var result = await downloader.DownloadAsync(new MapAreaDownloadRequest
        {
            AreaId = "oahu-urban",
            Name = "Oahu Urban",
            BoundingBox = new BoundingBox(-158.30, 21.20, -157.60, 21.60),
            OutputDirectory = Path.Combine(_rootDirectory, "packages"),
            MinZoom = 10,
            MaxZoom = 15,
            Layers =
            [
                new MapLayerDownloadSource
                {
                    LayerKey = "assets",
                    SourceUrl = "https://tiles.honua.test/data?bbox={minLon},{minLat},{maxLon},{maxLat}&z={minZoom}-{maxZoom}",
                    Priority = 1,
                    Required = true,
                },
            ],
        });

        Assert.Equal(1, result.DownloadedLayerCount);
        Assert.True(result.DownloadedBytes > 0);
        Assert.True(File.Exists(result.GeoPackagePath));

        var areas = await store.ListMapAreasAsync();
        Assert.Single(areas);
        Assert.Equal("oahu-urban", areas[0].AreaId);

        await using var packageConnection = new SqliteConnection($"Data Source={result.GeoPackagePath}");
        await packageConnection.OpenAsync();

        await using var cmd = packageConnection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM honua_layer_payloads;";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DownloadAsync_SanitizesAreaId_AndKeepsPackageUnderOutputDirectory()
    {
        var storePath = Path.Combine(_rootDirectory, "sync-store.gpkg");
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = storePath });

        var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            var payload = Encoding.UTF8.GetBytes($"payload:{request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }));

        var outputDirectory = Path.Combine(_rootDirectory, "packages");
        var downloader = new MapAreaDownloader(httpClient, store);
        var result = await downloader.DownloadAsync(new MapAreaDownloadRequest
        {
            AreaId = "../..//../very/../../unsafe",
            Name = "Unsafe Name",
            BoundingBox = new BoundingBox(-158.30, 21.20, -157.60, 21.60),
            OutputDirectory = outputDirectory,
            MinZoom = 10,
            MaxZoom = 15,
            Layers =
            [
                new MapLayerDownloadSource
                {
                    LayerKey = "assets",
                    SourceUrl = "https://tiles.honua.test/data?bbox={minLon},{minLat},{maxLon},{maxLat}&z={minZoom}-{maxZoom}",
                    Priority = 1,
                    Required = true,
                },
            ],
        });

        var packagePath = Path.GetFullPath(result.GeoPackagePath);
        var outputPath = Path.GetFullPath(outputDirectory);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var expectedPrefix = Path.EndsInDirectorySeparator(outputPath)
            ? outputPath
            : outputPath + Path.DirectorySeparatorChar;

        if (pathComparison == StringComparison.OrdinalIgnoreCase)
        {
            Assert.StartsWith(expectedPrefix.ToUpperInvariant(), packagePath.ToUpperInvariant());
        }
        else
        {
            Assert.StartsWith(expectedPrefix, packagePath);
        }
        Assert.DoesNotContain("..", Path.GetFileNameWithoutExtension(packagePath), StringComparison.Ordinal);
        Assert.True(File.Exists(packagePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

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
