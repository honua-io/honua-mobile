using System.Net;
using System.Security.Cryptography;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Packages;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Field.Projects;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class LocalFieldProjectPackageDownloadServiceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly string _downloadRoot;
    private readonly string _installRoot;

    public LocalFieldProjectPackageDownloadServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-package-download-{Guid.NewGuid():N}.gpkg");
        _downloadRoot = Path.Combine(Path.GetTempPath(), $"honua-package-download-cache-{Guid.NewGuid():N}");
        _installRoot = Path.Combine(Path.GetTempPath(), $"honua-package-download-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_downloadRoot);
        Directory.CreateDirectory(_installRoot);
    }

    [Fact]
    public async Task DownloadAndImportAsync_WithValidManifest_DownloadsArtifactsAndCreatesCatalog()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var artifactBytes = "offline feature data"u8.ToArray();
        var package = PackageWithArtifact(artifactBytes);
        var requestedPaths = new List<string>();
        using var httpClient = CreateHttpClient(request =>
        {
            requestedPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            return request.RequestUri?.AbsolutePath switch
            {
                "/packages/day-1/field-project-package.json" => JsonResponse(package.ToJson()),
                "/packages/day-1/data/assets.gpkg" => BytesResponse(artifactBytes),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        var result = await CreateService(storage, httpClient).DownloadAndImportAsync(new LocalFieldProjectPackageDownloadRequest
        {
            ManifestUri = new Uri("https://packages.honua.test/packages/day-1/field-project-package.json"),
            DownloadRootDirectory = _downloadRoot,
            DestinationRootDirectory = _installRoot,
            OverwriteExisting = true
        });

        Assert.True(result.Downloaded, string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.Imported, string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(
            new[] { "/packages/day-1/field-project-package.json", "/packages/day-1/data/assets.gpkg" },
            requestedPaths);
        Assert.Equal("local-inspection-demo", result.ProjectId);
        Assert.True(File.Exists(result.DownloadedManifestPath));
        Assert.True(File.Exists(Path.Combine(result.DownloadDirectory!, "data", "assets.gpkg")));
        var downloadedFile = Assert.Single(result.DownloadedFiles);
        Assert.Equal("pkg-assets", downloadedFile.PackageId);
        Assert.Equal("data/assets.gpkg", downloadedFile.RelativePath);
        Assert.Equal(artifactBytes.Length, downloadedFile.SizeBytes);
        Assert.True(result.DownloadedBytes > artifactBytes.Length);

        var catalogEntry = await storage.GetProjectCatalogEntryAsync("local-inspection-demo");
        Assert.NotNull(catalogEntry);
        Assert.Equal(FieldProjectCatalogState.Installed, catalogEntry.State);
        Assert.Equal("https://packages.honua.test/packages/day-1/field-project-package.json", catalogEntry.ImportSource);
    }

    [Fact]
    public async Task DownloadAndImportAsync_WithArtifactHttpError_ReturnsDownloadDiagnosticWithoutImporting()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var artifactBytes = "offline feature data"u8.ToArray();
        var package = PackageWithArtifact(artifactBytes);
        using var httpClient = CreateHttpClient(request => request.RequestUri?.AbsolutePath switch
        {
            "/packages/day-1/field-project-package.json" => JsonResponse(package.ToJson()),
            "/packages/day-1/data/assets.gpkg" => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                ReasonPhrase = "Not Found"
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await CreateService(storage, httpClient).DownloadAndImportAsync(new LocalFieldProjectPackageDownloadRequest
        {
            ManifestUri = new Uri("https://packages.honua.test/packages/day-1/field-project-package.json"),
            DownloadRootDirectory = _downloadRoot,
            DestinationRootDirectory = _installRoot,
            OverwriteExisting = true
        });

        Assert.False(result.Downloaded);
        Assert.False(result.Imported);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "download-http-error");
        Assert.Null(await storage.GetProjectCatalogEntryAsync("local-inspection-demo"));
    }

    [Fact]
    public async Task DownloadAndImportAsync_WithUnsafeArtifactPath_DoesNotRequestArtifact()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var package = PackageWithArtifact("offline feature data"u8.ToArray()) with
        {
            OfflinePackages =
            [
                PackageWithArtifact("offline feature data"u8.ToArray()).OfflinePackages[0] with
                {
                    RelativePath = "../escape.gpkg"
                }
            ]
        };
        var artifactRequested = false;
        using var httpClient = CreateHttpClient(request =>
        {
            if (request.RequestUri?.AbsolutePath != "/packages/day-1/field-project-package.json")
            {
                artifactRequested = true;
            }

            return request.RequestUri?.AbsolutePath == "/packages/day-1/field-project-package.json"
                ? JsonResponse(package.ToJson())
                : BytesResponse("unexpected"u8.ToArray());
        });

        var result = await CreateService(storage, httpClient).DownloadAndImportAsync(new LocalFieldProjectPackageDownloadRequest
        {
            ManifestUri = new Uri("https://packages.honua.test/packages/day-1/field-project-package.json"),
            DownloadRootDirectory = _downloadRoot,
            DestinationRootDirectory = _installRoot,
            OverwriteExisting = true
        });

        Assert.False(result.Downloaded);
        Assert.False(result.Imported);
        Assert.False(artifactRequested);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "unsafe-offline-artifact-path");
    }

    [Fact]
    public async Task DownloadAndImportAsync_WithChecksumMismatch_DownloadsThenReturnsImportDiagnostics()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var artifactBytes = "offline feature data"u8.ToArray();
        var package = PackageWithArtifact(artifactBytes) with
        {
            OfflinePackages =
            [
                PackageWithArtifact(artifactBytes).OfflinePackages[0] with
                {
                    Sha256 = new string('0', 64)
                }
            ]
        };
        using var httpClient = CreateHttpClient(request => request.RequestUri?.AbsolutePath switch
        {
            "/packages/day-1/field-project-package.json" => JsonResponse(package.ToJson()),
            "/packages/day-1/data/assets.gpkg" => BytesResponse(artifactBytes),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await CreateService(storage, httpClient).DownloadAndImportAsync(new LocalFieldProjectPackageDownloadRequest
        {
            ManifestUri = new Uri("https://packages.honua.test/packages/day-1/field-project-package.json"),
            DownloadRootDirectory = _downloadRoot,
            DestinationRootDirectory = _installRoot,
            OverwriteExisting = true
        });

        Assert.True(result.Downloaded);
        Assert.False(result.Imported);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "offline-artifact-sha256-mismatch");
        var catalogEntry = await storage.GetProjectCatalogEntryAsync("local-inspection-demo");
        Assert.NotNull(catalogEntry);
        Assert.Equal(FieldProjectCatalogState.Invalid, catalogEntry.State);
    }

    [Fact]
    public async Task DownloadAndImportAsync_WithAuthCustomizer_AttachesApiKeyToSameOriginRequests()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var artifactBytes = "offline feature data"u8.ToArray();
        var package = PackageWithArtifact(artifactBytes);
        var apiKeys = new List<string?>();
        using var httpClient = CreateHttpClient(request =>
        {
            apiKeys.Add(request.Headers.TryGetValues("X-API-Key", out var values) ? values.Single() : null);
            return request.RequestUri?.AbsolutePath switch
            {
                "/packages/day-1/field-project-package.json" => JsonResponse(package.ToJson()),
                "/packages/day-1/data/assets.gpkg" => BytesResponse(artifactBytes),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var auth = new TestAuthenticationService
        {
            ServerUrl = "https://packages.honua.test",
            ApiKey = "package-api-key"
        };

        var result = await CreateService(
            storage,
            httpClient,
            new FieldProjectPackageDownloadAuthHeader(auth)).DownloadAndImportAsync(new LocalFieldProjectPackageDownloadRequest
            {
                ManifestUri = new Uri("https://packages.honua.test/packages/day-1/field-project-package.json"),
                DownloadRootDirectory = _downloadRoot,
                DestinationRootDirectory = _installRoot,
                OverwriteExisting = true
            });

        Assert.True(result.Imported, string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(new[] { "package-api-key", "package-api-key" }, apiKeys);
        Assert.Equal(2, auth.EnsureValidSessionCalls);
    }

    private static FieldProjectPackage PackageWithArtifact(byte[] artifactBytes)
    {
        var package = LocalFieldProjectPackageImportServiceTests.CreatePackage();
        return package with
        {
            OfflinePackages =
            [
                package.OfflinePackages[0] with
                {
                    SizeBytes = artifactBytes.Length,
                    Sha256 = ComputeSha256(artifactBytes)
                }
            ]
        };
    }

    private static LocalFieldProjectPackageDownloadService CreateService(
        GeoPackageStorageService storage,
        HttpClient httpClient,
        params IFieldProjectPackageDownloadRequestCustomizer[] customizers)
    {
        return new LocalFieldProjectPackageDownloadService(
            httpClient,
            new LocalFieldProjectPackageImportService(storage),
            customizers);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => new(new StubHttpMessageHandler(handler));

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage BytesResponse(byte[] bytes)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose()
    {
        DeleteIfExists(_databasePath);
        DeleteIfExists($"{_databasePath}-wal");
        DeleteIfExists($"{_databasePath}-shm");
        DeleteDirectoryIfExists(_downloadRoot);
        DeleteDirectoryIfExists(_installRoot);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
