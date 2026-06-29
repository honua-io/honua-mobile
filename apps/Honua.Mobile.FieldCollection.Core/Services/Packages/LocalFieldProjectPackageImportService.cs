using System.Security.Cryptography;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Projects;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.FieldCollection.Services.Packages;

public sealed class LocalFieldProjectPackageImportService
{
    private const string InstalledManifestFileName = "field-project-package.json";

    private readonly IGeoPackageStorageService _storage;
    private readonly ILogger<LocalFieldProjectPackageImportService>? _logger;

    public LocalFieldProjectPackageImportService(
        IGeoPackageStorageService storage,
        ILogger<LocalFieldProjectPackageImportService>? logger = null)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<LocalFieldProjectPackageImportResult> ImportAsync(
        LocalFieldProjectPackageImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationRootDirectory);

        var manifestPath = Path.GetFullPath(request.ManifestPath);
        if (!File.Exists(manifestPath))
        {
            return Failed(null, Error("missing-manifest", "$.manifestPath", $"Manifest file '{manifestPath}' was not found."));
        }

        var packageRoot = Path.GetDirectoryName(manifestPath) ?? Directory.GetCurrentDirectory();
        FieldProjectPackage package;
        string manifestJson;
        try
        {
            manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            package = FieldProjectPackage.ParseJson(manifestJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            _logger?.LogWarning(ex, "Failed to parse local field project package manifest {ManifestPath}", manifestPath);
            return Failed(null, Error("invalid-manifest", "$", ex.Message));
        }

        var diagnostics = new List<LocalFieldProjectPackageDiagnostic>();
        diagnostics.AddRange(package.Validate().Issues.Select(ToDiagnostic));
        diagnostics.AddRange(ValidateMobileImportShape(package));
        var fileDiagnostics = ValidateOfflinePackageFiles(packageRoot, package, out var offlineFiles);
        diagnostics.AddRange(fileDiagnostics);

        if (diagnostics.Any(IsError))
        {
            await UpsertInvalidCatalogEntryAsync(package, request, diagnostics, cancellationToken).ConfigureAwait(false);
            return Failed(package.ProjectId, diagnostics);
        }

        var destinationDirectory = BuildDestinationDirectory(request.DestinationRootDirectory, package);
        if (Directory.Exists(destinationDirectory))
        {
            if (!request.OverwriteExisting)
            {
                diagnostics.Add(Error(
                    "project-already-installed",
                    "$.projectId",
                    $"Project '{package.ProjectId}' is already installed at the destination."));
                await UpsertInvalidCatalogEntryAsync(package, request, diagnostics, cancellationToken).ConfigureAwait(false);
                return Failed(package.ProjectId, diagnostics);
            }

            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(destinationDirectory);
        var installedManifestPath = Path.Combine(destinationDirectory, InstalledManifestFileName);
        await File.WriteAllTextAsync(installedManifestPath, manifestJson, cancellationToken).ConfigureAwait(false);

        var importedFiles = new List<LocalFieldProjectPackageImportedFile>();
        foreach (var file in offlineFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = ResolveDestinationPath(destinationDirectory, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file.SourcePath, destinationPath, overwrite: true);
            importedFiles.Add(new LocalFieldProjectPackageImportedFile
            {
                PackageId = file.PackageId,
                RelativePath = file.RelativePath,
                DestinationPath = destinationPath,
                SizeBytes = file.SizeBytes,
                Sha256 = file.Sha256
            });
        }

        var layers = BuildLayers(package);
        foreach (var layer in layers)
        {
            await _storage.CreateLayerAsync(layer).ConfigureAwait(false);
        }

        var bindingSourceIds = package.Bindings.ToDictionary(
            binding => binding.BindingId,
            binding => (string?)binding.SourceId,
            StringComparer.Ordinal);
        await _storage.DeleteFieldAssignmentsForProjectAsync(package.ProjectId).ConfigureAwait(false);
        await _storage.UpsertFieldTaskPacketsAsync(package.ProjectId, package.TaskPackets, bindingSourceIds).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var catalogEntry = new FieldProjectCatalogEntry
        {
            ProjectId = package.ProjectId,
            ServiceId = package.ProjectId,
            PackageId = ResolveCatalogPackageId(package),
            Version = package.Version,
            Name = package.Name,
            Description = package.Description ?? string.Empty,
            State = FieldProjectCatalogState.Installed,
            ValidationStatus = diagnostics.Any(diagnostic => diagnostic.Severity == LocalFieldProjectPackageDiagnosticSeverity.Warning)
                ? FieldProjectValidationStatus.Warning
                : FieldProjectValidationStatus.Valid,
            ValidationIssueCount = diagnostics.Count,
            LayerCount = layers.Count,
            PackageSizeBytes = new FileInfo(installedManifestPath).Length + importedFiles.Sum(file => file.SizeBytes),
            MediaSizeBytes = importedFiles
                .Where(file => package.OfflinePackages.Any(offlinePackage =>
                    string.Equals(offlinePackage.PackageId, file.PackageId, StringComparison.Ordinal) &&
                    offlinePackage.Kind == FieldOfflinePackageKind.Media))
                .Sum(file => file.SizeBytes),
            LocalStoragePath = destinationDirectory,
            ManifestPath = installedManifestPath,
            ImportSource = request.ImportSource ?? manifestPath,
            PackageDigest = $"sha256:{ComputeSha256(manifestPath)}",
            ImportedAtUtc = now,
            LastValidationAtUtc = now
        };
        await _storage.UpsertProjectCatalogEntryAsync(catalogEntry).ConfigureAwait(false);

        return new LocalFieldProjectPackageImportResult
        {
            ProjectId = package.ProjectId,
            Imported = true,
            CatalogEntry = catalogEntry,
            Diagnostics = diagnostics,
            ImportedFiles = importedFiles,
            Layers = layers,
            CopiedBytes = importedFiles.Sum(file => file.SizeBytes),
            InstalledManifestPath = installedManifestPath
        };
    }

    private async Task UpsertInvalidCatalogEntryAsync(
        FieldProjectPackage package,
        LocalFieldProjectPackageImportRequest request,
        IReadOnlyList<LocalFieldProjectPackageDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(package.ProjectId))
        {
            return;
        }

        var now = DateTime.UtcNow;
        await _storage.UpsertProjectCatalogEntryAsync(new FieldProjectCatalogEntry
        {
            ProjectId = package.ProjectId,
            ServiceId = package.ProjectId,
            PackageId = ResolveCatalogPackageId(package),
            Version = package.Version,
            Name = string.IsNullOrWhiteSpace(package.Name) ? package.ProjectId : package.Name,
            Description = package.Description ?? string.Empty,
            State = FieldProjectCatalogState.Invalid,
            ValidationStatus = FieldProjectValidationStatus.Error,
            ValidationIssueCount = diagnostics.Count(IsError),
            ImportSource = request.ImportSource ?? request.ManifestPath,
            ImportedAtUtc = now,
            LastValidationAtUtc = now
        }).ConfigureAwait(false);
    }

    private static IReadOnlyList<LayerInfo> BuildLayers(FieldProjectPackage package)
    {
        var formsById = package.Forms.ToDictionary(form => form.FormId, StringComparer.Ordinal);
        var sourcesById = package.Sources.ToDictionary(source => source.Id, StringComparer.Ordinal);
        var usedLayerIds = new HashSet<int>();
        var layers = new List<LayerInfo>(package.Bindings.Count);

        foreach (var binding in package.Bindings)
        {
            if (!formsById.TryGetValue(binding.FormId, out var form) ||
                !sourcesById.TryGetValue(binding.SourceId, out var source))
            {
                continue;
            }

            var layerId = ResolveLayerId(source.Locator.LayerId, usedLayerIds);
            var layerForm = ApplyBindingMetadata(form, binding);
            layers.Add(new LayerInfo
            {
                Id = layerId,
                ServiceId = package.ProjectId,
                SourceId = $"{package.ProjectId}/{binding.BindingId}",
                Name = form.Name,
                Description = package.Description ?? source.Attribution ?? string.Empty,
                GeometryType = source.Schema?.GeometryType ?? Honua.Sdk.Abstractions.Features.FeatureSpatialGeometryType.Unspecified,
                IsEditable = binding.Editable,
                IsVisible = true,
                Form = layerForm,
                Schema = layerForm.Sections.SelectMany(section => section.Fields).ToList()
            });
        }

        return layers;
    }

    private static FormDefinition ApplyBindingMetadata(FormDefinition form, FieldProjectBinding binding)
    {
        var metadata = new Dictionary<string, string>(form.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["honua:bindingId"] = binding.BindingId,
            ["honua:sourceId"] = binding.SourceId
        };

        if (!string.IsNullOrWhiteSpace(binding.OfflinePackageId))
        {
            metadata["honua:offlinePackageId"] = binding.OfflinePackageId;
        }

        if (!string.IsNullOrWhiteSpace(binding.DisplayFieldId))
        {
            metadata["honua:displayFieldId"] = binding.DisplayFieldId;
        }

        if (!string.IsNullOrWhiteSpace(binding.DuplicateKeyFieldId))
        {
            metadata["honua:duplicateKeyFieldId"] = binding.DuplicateKeyFieldId;
        }

        return form with { Metadata = metadata };
    }

    private static int ResolveLayerId(int? preferredLayerId, HashSet<int> usedLayerIds)
    {
        var layerId = preferredLayerId.GetValueOrDefault(usedLayerIds.Count + 1);
        if (layerId <= 0)
        {
            layerId = usedLayerIds.Count + 1;
        }

        while (!usedLayerIds.Add(layerId))
        {
            layerId++;
        }

        return layerId;
    }

    private static IReadOnlyList<LocalFieldProjectPackageDiagnostic> ValidateMobileImportShape(
        FieldProjectPackage package)
    {
        var diagnostics = new List<LocalFieldProjectPackageDiagnostic>();
        if (package.Bindings.Count == 0)
        {
            diagnostics.Add(Error("missing-layer-binding", "$.bindings", "At least one form-to-source binding is required."));
        }

        var boundFormIds = package.Bindings
            .Select(binding => binding.FormId)
            .ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < package.Forms.Count; i++)
        {
            if (!boundFormIds.Contains(package.Forms[i].FormId))
            {
                diagnostics.Add(Error(
                    "missing-layer-binding",
                    $"$.forms[{i}].formId",
                    $"Form '{package.Forms[i].FormId}' is not bound to a source."));
            }
        }

        return diagnostics;
    }

    private static IReadOnlyList<LocalFieldProjectPackageDiagnostic> ValidateOfflinePackageFiles(
        string packageRoot,
        FieldProjectPackage package,
        out IReadOnlyList<OfflinePackageFile> files)
    {
        var diagnostics = new List<LocalFieldProjectPackageDiagnostic>();
        var resolvedFiles = new List<OfflinePackageFile>();

        for (var i = 0; i < package.OfflinePackages.Count; i++)
        {
            var offlinePackage = package.OfflinePackages[i];
            if (string.IsNullOrWhiteSpace(offlinePackage.RelativePath))
            {
                diagnostics.Add(Error(
                    "missing-offline-artifact",
                    $"$.offlinePackages[{i}].relativePath",
                    $"Offline package '{offlinePackage.PackageId}' must include a relativePath for local import."));
                continue;
            }

            if (!TryResolvePackagePath(packageRoot, offlinePackage.RelativePath, out var sourcePath))
            {
                diagnostics.Add(Error(
                    "unsafe-offline-artifact-path",
                    $"$.offlinePackages[{i}].relativePath",
                    $"Offline package '{offlinePackage.PackageId}' has an unsafe relative path."));
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                diagnostics.Add(Error(
                    "missing-offline-artifact",
                    $"$.offlinePackages[{i}].relativePath",
                    $"Offline package artifact '{offlinePackage.RelativePath}' was not found."));
                continue;
            }

            var sizeBytes = new FileInfo(sourcePath).Length;
            if (offlinePackage.SizeBytes.HasValue && offlinePackage.SizeBytes.Value != sizeBytes)
            {
                diagnostics.Add(Error(
                    "offline-artifact-size-mismatch",
                    $"$.offlinePackages[{i}].sizeBytes",
                    $"Offline package '{offlinePackage.PackageId}' declared {offlinePackage.SizeBytes.Value} bytes but found {sizeBytes} bytes."));
            }

            var sha256 = ComputeSha256(sourcePath);
            if (!string.IsNullOrWhiteSpace(offlinePackage.Sha256) &&
                !string.Equals(offlinePackage.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    "offline-artifact-sha256-mismatch",
                    $"$.offlinePackages[{i}].sha256",
                    $"Offline package '{offlinePackage.PackageId}' SHA-256 did not match the manifest."));
            }

            resolvedFiles.Add(new OfflinePackageFile(
                offlinePackage.PackageId,
                offlinePackage.RelativePath,
                sourcePath,
                sizeBytes,
                sha256));
        }

        files = resolvedFiles;
        return diagnostics;
    }

    private static bool TryResolvePackagePath(string packageRoot, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var root = Path.GetFullPath(packageRoot);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : $"{root}{Path.DirectorySeparatorChar}";
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static string ResolveDestinationPath(string destinationDirectory, string relativePath)
    {
        if (!TryResolvePackagePath(destinationDirectory, relativePath, out var fullPath))
        {
            throw new InvalidOperationException($"Invalid package destination path '{relativePath}'.");
        }

        return fullPath;
    }

    private static string BuildDestinationDirectory(
        string destinationRootDirectory,
        FieldProjectPackage package)
    {
        var version = string.IsNullOrWhiteSpace(package.Version) ? "current" : package.Version;
        return Path.Combine(
            Path.GetFullPath(destinationRootDirectory),
            SanitizePathSegment(package.ProjectId),
            SanitizePathSegment(version));
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray()).Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "package" : sanitized;
    }

    private static string ResolveCatalogPackageId(FieldProjectPackage package)
        => package.OfflinePackages.Count == 1 ? package.OfflinePackages[0].PackageId : package.ProjectId;

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static LocalFieldProjectPackageImportResult Failed(
        string? projectId,
        params LocalFieldProjectPackageDiagnostic[] diagnostics)
        => Failed(projectId, diagnostics.AsEnumerable());

    private static LocalFieldProjectPackageImportResult Failed(
        string? projectId,
        IEnumerable<LocalFieldProjectPackageDiagnostic> diagnostics)
        => new()
        {
            ProjectId = projectId,
            Imported = false,
            Diagnostics = diagnostics.ToList()
        };

    private static LocalFieldProjectPackageDiagnostic ToDiagnostic(FieldProjectPackageValidationIssue issue)
        => new()
        {
            Code = issue.Code,
            Path = issue.Path,
            Message = issue.Message,
            Severity = issue.Severity == FieldProjectPackageValidationSeverity.Error
                ? LocalFieldProjectPackageDiagnosticSeverity.Error
                : LocalFieldProjectPackageDiagnosticSeverity.Warning
        };

    private static LocalFieldProjectPackageDiagnostic Error(string code, string path, string message)
        => new()
        {
            Code = code,
            Path = path,
            Message = message,
            Severity = LocalFieldProjectPackageDiagnosticSeverity.Error
        };

    private static bool IsError(LocalFieldProjectPackageDiagnostic diagnostic)
        => diagnostic.Severity == LocalFieldProjectPackageDiagnosticSeverity.Error;

    private sealed record OfflinePackageFile(
        string PackageId,
        string RelativePath,
        string SourcePath,
        long SizeBytes,
        string Sha256);
}
