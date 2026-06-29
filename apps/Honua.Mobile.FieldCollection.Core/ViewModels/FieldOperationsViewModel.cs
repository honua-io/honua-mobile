using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Assignments;
using Honua.Mobile.FieldCollection.Services.Packages;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Field.Projects;

namespace Honua.Mobile.FieldCollection.ViewModels;

public partial class FieldOperationsViewModel : BaseViewModel
{
    private readonly IGeoPackageStorageService _storage;
    private readonly ILocalFieldAssignmentService _assignmentService;
    private readonly LocalFieldProjectPackageImportService _packageImportService;
    private readonly LocalFieldProjectPackageDownloadService _packageDownloadService;
    private readonly ILocalRecordExportService _recordExportService;
    private readonly ILocalRecordExportShareService _exportShareService;

    [ObservableProperty]
    private FieldProjectCatalogEntry? selectedPackage;

    [ObservableProperty]
    private LocalFieldAssignmentInfo? selectedAssignment;

    [ObservableProperty]
    private LayerInfo? selectedLayer;

    [ObservableProperty]
    private string packageManifestPath = string.Empty;

    [ObservableProperty]
    private string packageManifestUrl = string.Empty;

    [ObservableProperty]
    private string packageDownloadRoot = string.Empty;

    [ObservableProperty]
    private string packageDestinationRoot = string.Empty;

    [ObservableProperty]
    private bool includeArchivedPackages;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string lastImportSummary = string.Empty;

    [ObservableProperty]
    private string lastExportSummary = string.Empty;

    [ObservableProperty]
    private string lastExportDirectory = string.Empty;

    [ObservableProperty]
    private bool hasImportDiagnostics;

    [ObservableProperty]
    private bool hasLastExport;

    private LocalRecordExportResult? _lastExport;

    public ObservableCollection<FieldProjectCatalogEntry> Packages { get; } = [];
    public ObservableCollection<LocalFieldAssignmentInfo> Assignments { get; } = [];
    public ObservableCollection<LayerInfo> AvailableLayers { get; } = [];
    public ObservableCollection<LocalFieldProjectPackageDiagnostic> ImportDiagnostics { get; } = [];
    public ObservableCollection<string> ExportArtifacts { get; } = [];

    public FieldOperationsViewModel(
        INavigationService navigationService,
        IGeoPackageStorageService storage,
        ILocalFieldAssignmentService assignmentService,
        LocalFieldProjectPackageImportService packageImportService,
        LocalFieldProjectPackageDownloadService packageDownloadService,
        ILocalRecordExportService recordExportService,
        ILocalRecordExportShareService exportShareService)
        : base(navigationService)
    {
        _storage = storage;
        _assignmentService = assignmentService;
        _packageImportService = packageImportService;
        _packageDownloadService = packageDownloadService;
        _recordExportService = recordExportService;
        _exportShareService = exportShareService;
        Title = "Work";
    }

    protected override Task OnRefresh() => LoadWorkspace();

    partial void OnSelectedPackageChanged(FieldProjectCatalogEntry? value)
    {
        _ = LoadAssignments();
    }

    partial void OnIncludeArchivedPackagesChanged(bool value)
    {
        _ = LoadWorkspace();
    }

    [RelayCommand]
    private async Task LoadWorkspace()
    {
        await ExecuteAsync(RefreshWorkspaceCoreAsync);
    }

    [RelayCommand]
    private async Task LoadAssignments()
    {
        await ExecuteAsync(LoadAssignmentsCoreAsync);
    }

    [RelayCommand]
    private async Task ImportPackage()
    {
        if (string.IsNullOrWhiteSpace(PackageManifestPath))
        {
            await ShowError("Package Required", "Enter a field-project-package.json path before importing.");
            return;
        }

        var manifestPath = PackageManifestPath.Trim();
        var destinationRoot = string.IsNullOrWhiteSpace(PackageDestinationRoot)
            ? Path.Combine(Path.GetDirectoryName(manifestPath) ?? Environment.CurrentDirectory, "installed")
            : PackageDestinationRoot.Trim();

        await ExecuteAsync(async () =>
        {
            var result = await _packageImportService.ImportAsync(new LocalFieldProjectPackageImportRequest
            {
                ManifestPath = manifestPath,
                DestinationRootDirectory = destinationRoot,
                ImportSource = manifestPath,
                OverwriteExisting = true
            });

            ImportDiagnostics.Clear();
            foreach (var diagnostic in result.Diagnostics)
            {
                ImportDiagnostics.Add(diagnostic);
            }

            HasImportDiagnostics = ImportDiagnostics.Count > 0;
            LastImportSummary = result.Imported
                ? $"Imported {result.ProjectId} with {result.ImportedFiles.Count} artifact(s), {result.CopiedBytes} byte(s)."
                : $"Package import failed for {result.ProjectId ?? Path.GetFileName(manifestPath)}.";
            StatusMessage = LastImportSummary;

            await RefreshWorkspaceCoreAsync();
        });
    }

    [RelayCommand]
    private async Task DownloadPackage()
    {
        if (string.IsNullOrWhiteSpace(PackageManifestUrl))
        {
            await ShowError("Package Required", "Enter a field project package manifest URL before downloading.");
            return;
        }

        if (!Uri.TryCreate(PackageManifestUrl.Trim(), UriKind.Absolute, out var manifestUri))
        {
            await ShowError("Package URL Invalid", "Enter an absolute field project package manifest URL.");
            return;
        }

        var downloadRoot = string.IsNullOrWhiteSpace(PackageDownloadRoot)
            ? Path.Combine(GetLocalAppDataDirectory(), "Honua", "field-package-downloads")
            : PackageDownloadRoot.Trim();
        var destinationRoot = string.IsNullOrWhiteSpace(PackageDestinationRoot)
            ? Path.Combine(GetLocalAppDataDirectory(), "Honua", "field-packages")
            : PackageDestinationRoot.Trim();

        await ExecuteAsync(async () =>
        {
            var result = await _packageDownloadService.DownloadAndImportAsync(new LocalFieldProjectPackageDownloadRequest
            {
                ManifestUri = manifestUri,
                DownloadRootDirectory = downloadRoot,
                DestinationRootDirectory = destinationRoot,
                OverwriteExisting = true
            });

            ImportDiagnostics.Clear();
            foreach (var diagnostic in result.Diagnostics)
            {
                ImportDiagnostics.Add(diagnostic);
            }

            HasImportDiagnostics = ImportDiagnostics.Count > 0;
            PackageManifestPath = result.DownloadedManifestPath ?? PackageManifestPath;
            LastImportSummary = result.Imported
                ? $"Downloaded and imported {result.ProjectId} with {result.DownloadedFiles.Count} artifact(s), {result.DownloadedBytes} byte(s)."
                : result.Downloaded
                    ? $"Package downloaded but import failed for {result.ProjectId ?? manifestUri.Host}."
                    : $"Package download failed for {result.ProjectId ?? manifestUri.Host}.";
            StatusMessage = LastImportSummary;

            await RefreshWorkspaceCoreAsync();
        });
    }

    [RelayCommand]
    private async Task MarkPackageOpened()
    {
        if (SelectedPackage is null)
        {
            return;
        }

        var projectId = SelectedPackage.ProjectId;
        await ExecuteAsync(async () =>
        {
            await _storage.MarkProjectCatalogEntryOpenedAsync(projectId);
            StatusMessage = $"Opened {SelectedPackage.Name}.";
            await RefreshWorkspaceCoreAsync();
        });
    }

    [RelayCommand]
    private async Task ArchivePackage()
    {
        if (SelectedPackage is null)
        {
            return;
        }

        var projectId = SelectedPackage.ProjectId;
        await ExecuteAsync(async () =>
        {
            await _storage.UpdateProjectCatalogStateAsync(projectId, FieldProjectCatalogState.Archived);
            StatusMessage = $"Archived {SelectedPackage.Name}.";
            await RefreshWorkspaceCoreAsync();
        });
    }

    [RelayCommand]
    private async Task StartAssignment(LocalFieldAssignmentInfo assignment)
    {
        await UpdateAssignmentStatusAsync(assignment, FieldAssignmentStatus.InProgress);
    }

    [RelayCommand]
    private async Task CompleteAssignment(LocalFieldAssignmentInfo assignment)
    {
        await UpdateAssignmentStatusAsync(assignment, FieldAssignmentStatus.Complete);
    }

    [RelayCommand]
    private async Task ReopenAssignment(LocalFieldAssignmentInfo assignment)
    {
        await UpdateAssignmentStatusAsync(assignment, FieldAssignmentStatus.NotStarted);
    }

    [RelayCommand]
    private async Task OpenAssignmentRecord(LocalFieldAssignmentInfo assignment)
    {
        if (assignment.RecordIds.Count == 0)
        {
            StatusMessage = "Assignment has no bound record.";
            return;
        }

        var layer = FindLayerForAssignment(assignment);
        if (layer is null)
        {
            StatusMessage = $"No local layer found for assignment {assignment.AssignmentId}.";
            return;
        }

        await NavigationService.NavigateToAsync(
            "record-detail",
            new Dictionary<string, object>
            {
                ["layerId"] = layer.Id,
                ["featureId"] = assignment.RecordIds[0]
            });
    }

    [RelayCommand]
    private async Task ExportSelectedLayer()
    {
        if (SelectedLayer is null)
        {
            await ShowError("Layer Required", "Select a local layer before exporting.");
            return;
        }

        var layer = SelectedLayer;
        await ExecuteAsync(async () =>
        {
            _lastExport = await _recordExportService.ExportLayerAsync(layer);
            HasLastExport = true;
            LastExportDirectory = _lastExport.ExportDirectory;
            LastExportSummary = $"Exported {_lastExport.RecordCount} record(s), {_lastExport.AttachmentCount} attachment(s).";

            ExportArtifacts.Clear();
            ExportArtifacts.Add(_lastExport.CsvPath);
            ExportArtifacts.Add(_lastExport.GeoJsonPath);
            ExportArtifacts.Add(_lastExport.AttachmentManifestPath);
            ExportArtifacts.Add(_lastExport.EvidenceManifestPath);

            StatusMessage = LastExportSummary;
            await RefreshWorkspaceCoreAsync();
        });
    }

    [RelayCommand]
    private async Task ShareLastExport()
    {
        if (_lastExport is null)
        {
            await ShowError("Export Required", "Export a layer before sharing.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            await _exportShareService.ShareExportAsync(_lastExport);
            StatusMessage = $"Shared export for {_lastExport.LayerName}.";
        });
    }

    private async Task UpdateAssignmentStatusAsync(LocalFieldAssignmentInfo? assignment, FieldAssignmentStatus status)
    {
        if (assignment is null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (!await _assignmentService.UpdateStatusAsync(assignment.AssignmentId, status))
            {
                StatusMessage = $"Assignment {assignment.AssignmentId} was not found.";
                return;
            }

            StatusMessage = $"Assignment {assignment.AssignmentId} is {status}.";
            await LoadAssignmentsCoreAsync();
        });
    }

    private async Task RefreshWorkspaceCoreAsync()
    {
        var previousPackageId = SelectedPackage?.ProjectId;
        var previousLayerId = SelectedLayer?.Id;
        var packages = await _storage.GetProjectCatalogEntriesAsync(IncludeArchivedPackages);
        var layers = await _storage.GetLayersAsync();

        Packages.Clear();
        foreach (var package in packages)
        {
            Packages.Add(package);
        }

        AvailableLayers.Clear();
        foreach (var layer in layers.OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase))
        {
            AvailableLayers.Add(layer);
        }

        SelectedPackage = Packages.FirstOrDefault(package =>
                string.Equals(package.ProjectId, previousPackageId, StringComparison.OrdinalIgnoreCase)) ??
            Packages.FirstOrDefault();
        SelectedLayer = AvailableLayers.FirstOrDefault(layer => layer.Id == previousLayerId) ??
            AvailableLayers.FirstOrDefault();

        await LoadAssignmentsCoreAsync();
        StatusMessage = Packages.Count == 0
            ? "No local field packages installed."
            : $"{Packages.Count} package(s), {Assignments.Count} assignment(s).";
    }

    private async Task LoadAssignmentsCoreAsync()
    {
        var projectId = SelectedPackage?.ProjectId;
        var assignments = await _assignmentService.GetAssignmentsAsync(
            string.IsNullOrWhiteSpace(projectId)
                ? null
                : new LocalFieldAssignmentFilter { ProjectId = projectId });

        var selectedAssignmentId = SelectedAssignment?.AssignmentId;
        Assignments.Clear();
        foreach (var assignment in assignments
            .OrderBy(assignment => assignment.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ThenByDescending(assignment => assignment.Priority)
            .ThenBy(assignment => assignment.AssignmentId, StringComparer.OrdinalIgnoreCase))
        {
            Assignments.Add(assignment);
        }

        SelectedAssignment = Assignments.FirstOrDefault(assignment =>
                string.Equals(assignment.AssignmentId, selectedAssignmentId, StringComparison.OrdinalIgnoreCase)) ??
            Assignments.FirstOrDefault();
    }

    private LayerInfo? FindLayerForAssignment(LocalFieldAssignmentInfo assignment)
    {
        return AvailableLayers.FirstOrDefault(layer =>
                string.Equals(layer.ServiceId, assignment.ProjectId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ReadBindingId(layer), assignment.BindingId, StringComparison.OrdinalIgnoreCase)) ??
            AvailableLayers.FirstOrDefault(layer =>
                string.Equals(layer.ServiceId, assignment.ProjectId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(layer.SourceId, $"{assignment.ProjectId}/{assignment.BindingId}", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadBindingId(LayerInfo layer)
    {
        return layer.Form?.Metadata.TryGetValue("honua:bindingId", out var bindingId) == true
            ? bindingId
            : null;
    }

    private static string GetLocalAppDataDirectory()
    {
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(directory)
            ? Environment.CurrentDirectory
            : directory;
    }
}
