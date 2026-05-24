using System.Security.Cryptography;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Assignments;
using Honua.Mobile.FieldCollection.Services.Packages;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.FieldCollection.ViewModels;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Projects;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class FieldOperationsViewModelTests : IDisposable
{
    private readonly string _databasePath;
    private readonly string _packageRoot;
    private readonly string _downloadRoot;
    private readonly string _installRoot;
    private readonly string _exportRoot;

    public FieldOperationsViewModelTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-workspace-{Guid.NewGuid():N}.gpkg");
        _packageRoot = Path.Combine(Path.GetTempPath(), $"honua-workspace-package-{Guid.NewGuid():N}");
        _downloadRoot = Path.Combine(Path.GetTempPath(), $"honua-workspace-download-{Guid.NewGuid():N}");
        _installRoot = Path.Combine(Path.GetTempPath(), $"honua-workspace-install-{Guid.NewGuid():N}");
        _exportRoot = Path.Combine(Path.GetTempPath(), $"honua-workspace-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_packageRoot);
        Directory.CreateDirectory(_downloadRoot);
        Directory.CreateDirectory(_installRoot);
        Directory.CreateDirectory(_exportRoot);
    }

    [Fact]
    public async Task WorkCommands_ImportPackageManageAssignmentsOpenRecordAndShareExport()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var navigation = new RecordingNavigationService();
        var share = new RecordingExportShareService();
        var viewModel = CreateViewModel(storage, navigation, share);
        viewModel.PackageManifestPath = await WritePackageAsync(LocalFieldProjectPackageImportServiceTests.CreatePackage());
        viewModel.PackageDestinationRoot = _installRoot;

        await viewModel.ImportPackageCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Packages);
        Assert.Equal("local-inspection-demo", viewModel.SelectedPackage?.ProjectId);
        Assert.Equal(2, viewModel.AvailableLayers.Count);
        Assert.Equal(2, viewModel.Assignments.Count);
        Assert.False(viewModel.HasImportDiagnostics);
        Assert.Contains("Imported local-inspection-demo", viewModel.LastImportSummary, StringComparison.Ordinal);

        var assignment = viewModel.Assignments.Single(item => item.AssignmentId == "task-asset-100");
        await viewModel.StartAssignmentCommand.ExecuteAsync(assignment);
        Assert.Equal(FieldAssignmentStatus.InProgress, viewModel.Assignments.Single(item => item.AssignmentId == assignment.AssignmentId).Status);

        await viewModel.CompleteAssignmentCommand.ExecuteAsync(viewModel.Assignments.Single(item => item.AssignmentId == assignment.AssignmentId));
        Assert.Equal(FieldAssignmentStatus.Complete, viewModel.Assignments.Single(item => item.AssignmentId == assignment.AssignmentId).Status);

        await viewModel.ReopenAssignmentCommand.ExecuteAsync(viewModel.Assignments.Single(item => item.AssignmentId == assignment.AssignmentId));
        Assert.Equal(FieldAssignmentStatus.NotStarted, viewModel.Assignments.Single(item => item.AssignmentId == assignment.AssignmentId).Status);

        await viewModel.OpenAssignmentRecordCommand.ExecuteAsync(viewModel.Assignments.Single(item => item.AssignmentId == assignment.AssignmentId));
        Assert.Equal("record-detail", navigation.LastRoute);
        Assert.Equal("asset-100", navigation.LastParameters["featureId"]);
        Assert.Equal(viewModel.AvailableLayers.Single(layer =>
            layer.Form?.Metadata.TryGetValue("honua:bindingId", out var bindingId) == true &&
            bindingId == "asset-inspection-assets").Id, navigation.LastParameters["layerId"]);

        viewModel.SelectedLayer = viewModel.AvailableLayers.Single(layer =>
            layer.Form?.Metadata.TryGetValue("honua:bindingId", out var bindingId) == true &&
            bindingId == "asset-inspection-assets");
        await storage.StoreFeatureAsync(new Feature
        {
            Id = "asset-100",
            LayerId = viewModel.SelectedLayer.Id,
            Geometry = new Point(21.3, -157.8),
            CreatedAt = new DateTime(2026, 5, 24, 8, 0, 0, DateTimeKind.Utc),
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["asset_id"] = "asset-100"
            }
        });

        await viewModel.ExportSelectedLayerCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasLastExport);
        Assert.Contains("Exported 1 record", viewModel.LastExportSummary, StringComparison.Ordinal);
        Assert.All(viewModel.ExportArtifacts, path => Assert.True(File.Exists(path), path));

        await viewModel.ShareLastExportCommand.ExecuteAsync(null);
        Assert.NotNull(share.SharedExport);
        Assert.Equal(viewModel.SelectedLayer.Id, share.SharedExport.LayerId);
    }

    [Fact]
    public async Task DownloadPackageCommand_WithManifestUrl_DownloadsImportsAndSelectsPackage()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var artifactBytes = "offline feature data"u8.ToArray();
        var basePackage = LocalFieldProjectPackageImportServiceTests.CreatePackage();
        var package = basePackage with
        {
            OfflinePackages =
            [
                basePackage.OfflinePackages[0] with
                {
                    SizeBytes = artifactBytes.Length,
                    Sha256 = ComputeSha256(artifactBytes)
                }
            ]
        };

        var requestedPaths = new List<string>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            return request.RequestUri?.AbsolutePath switch
            {
                "/packages/day-1/field-project-package.json" => JsonResponse(package.ToJson()),
                "/packages/day-1/data/assets.gpkg" => BytesResponse(artifactBytes),
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            };
        }));
        var importService = new LocalFieldProjectPackageImportService(storage);
        var downloadService = new LocalFieldProjectPackageDownloadService(httpClient, importService);
        var viewModel = CreateViewModel(storage, packageDownloadService: downloadService);
        viewModel.PackageManifestUrl = "https://packages.honua.test/packages/day-1/field-project-package.json";
        viewModel.PackageDownloadRoot = _downloadRoot;
        viewModel.PackageDestinationRoot = _installRoot;

        await viewModel.DownloadPackageCommand.ExecuteAsync(null);

        Assert.Equal(
            new[] { "/packages/day-1/field-project-package.json", "/packages/day-1/data/assets.gpkg" },
            requestedPaths);
        Assert.Single(viewModel.Packages);
        Assert.Equal("local-inspection-demo", viewModel.SelectedPackage?.ProjectId);
        Assert.Equal(2, viewModel.Assignments.Count);
        Assert.False(viewModel.HasImportDiagnostics);
        Assert.Contains("Downloaded and imported local-inspection-demo", viewModel.LastImportSummary, StringComparison.Ordinal);
        Assert.True(File.Exists(viewModel.PackageManifestPath));
    }

    [Fact]
    public async Task ImportPackageCommand_WithInvalidPackage_RendersDeterministicDiagnostics()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var viewModel = CreateViewModel(storage);
        var invalidPackage = LocalFieldProjectPackageImportServiceTests.CreatePackage() with
        {
            SchemaVersion = "honua.field-project-package.v0"
        };
        viewModel.PackageManifestPath = await WritePackageAsync(invalidPackage);
        viewModel.PackageDestinationRoot = _installRoot;

        await viewModel.ImportPackageCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasImportDiagnostics);
        Assert.Contains(viewModel.ImportDiagnostics, diagnostic => diagnostic.Code == FieldProjectPackageValidationCodes.UnsupportedSchemaVersion);
        Assert.Contains("failed", viewModel.LastImportSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FieldProjectCatalogState.Invalid, viewModel.Packages.Single().State);
    }

    private FieldOperationsViewModel CreateViewModel(
        GeoPackageStorageService storage,
        RecordingNavigationService? navigation = null,
        ILocalRecordExportShareService? share = null,
        LocalFieldProjectPackageDownloadService? packageDownloadService = null)
    {
        var importService = new LocalFieldProjectPackageImportService(storage);
        return new FieldOperationsViewModel(
            navigation ?? new RecordingNavigationService(),
            storage,
            new LocalFieldAssignmentService(storage),
            importService,
            packageDownloadService ?? new LocalFieldProjectPackageDownloadService(
                new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound))),
                importService),
            new LocalRecordExportService(storage, _exportRoot),
            share ?? new RecordingExportShareService());
    }

    private async Task<string> WritePackageAsync(FieldProjectPackage package, string directoryName = "package")
    {
        var packageDirectory = Path.Combine(_packageRoot, directoryName);
        Directory.CreateDirectory(Path.Combine(packageDirectory, "data"));
        var artifactPath = Path.Combine(packageDirectory, "data", "assets.gpkg");
        await File.WriteAllTextAsync(artifactPath, "offline feature data");
        package = package with
        {
            OfflinePackages =
            [
                package.OfflinePackages[0] with
                {
                    SizeBytes = new FileInfo(artifactPath).Length,
                    Sha256 = ComputeSha256(artifactPath)
                }
            ]
        };

        var manifestPath = Path.Combine(packageDirectory, "field-project-package.json");
        await File.WriteAllTextAsync(manifestPath, package.ToJson());
        return manifestPath;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage JsonResponse(string json)
        => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage BytesResponse(byte[] bytes)
        => new(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };

    public void Dispose()
    {
        DeleteFile(_databasePath);
        DeleteDirectory(_packageRoot);
        DeleteDirectory(_downloadRoot);
        DeleteDirectory(_installRoot);
        DeleteDirectory(_exportRoot);
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public string LastRoute { get; private set; } = string.Empty;
        public Dictionary<string, object> LastParameters { get; private set; } = [];

        public Task NavigateToAsync(string route)
        {
            LastRoute = route;
            LastParameters = [];
            return Task.CompletedTask;
        }

        public Task NavigateToAsync(string route, IDictionary<string, object> parameters)
        {
            LastRoute = route;
            LastParameters = new Dictionary<string, object>(parameters);
            return Task.CompletedTask;
        }

        public Task GoBackAsync() => Task.CompletedTask;

        public Task PopToRootAsync() => Task.CompletedTask;

        public Task DisplayAlert(string title, string message, string cancel) => Task.CompletedTask;

        public Task<bool> DisplayAlert(string title, string message, string accept, string cancel) => Task.FromResult(true);

        public Task<string> DisplayActionSheet(string title, string cancel, string destruction, params string[] buttons) =>
            Task.FromResult(buttons.FirstOrDefault() ?? cancel);

        public Task<string> DisplayPromptAsync(
            string title,
            string message,
            string accept = "OK",
            string cancel = "Cancel",
            string placeholder = "",
            int maxLength = -1,
            string initialValue = "") => Task.FromResult(initialValue);
    }

    private sealed class RecordingExportShareService : ILocalRecordExportShareService
    {
        public LocalRecordExportResult? SharedExport { get; private set; }

        public Task ShareExportAsync(LocalRecordExportResult export, CancellationToken cancellationToken = default)
        {
            SharedExport = export;
            return Task.CompletedTask;
        }
    }
}
