using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Forms;
using Honua.Mobile.FieldCollection.Services.Sync;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class NoCloudFieldDayAcceptanceHarnessTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _databasePath;
    private readonly string _artifactRoot;
    private readonly string _mediaRoot;

    public NoCloudFieldDayAcceptanceHarnessTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-field-day-{Guid.NewGuid():N}.gpkg");
        _artifactRoot = Path.Combine(Path.GetTempPath(), $"honua-field-day-artifacts-{Guid.NewGuid():N}");
        _mediaRoot = Path.Combine(Path.GetTempPath(), $"honua-field-day-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_artifactRoot);
        Directory.CreateDirectory(_mediaRoot);
    }

    [Fact]
    public async Task FieldDayHarness_RunsNoCloudWorkflowAndWritesEvidence()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var layer = CreateLayer();
        await storage.CreateLayerAsync(layer);
        await storage.UpsertProjectCatalogEntryAsync(new FieldProjectCatalogEntry
        {
            ProjectId = "field-day",
            ServiceId = "field-day",
            PackageId = "field-day-local-package",
            Version = "2026.05.23",
            Name = "Field Day Local Package",
            Description = "No-cloud acceptance fixture",
            State = FieldProjectCatalogState.Installed,
            ValidationStatus = FieldProjectValidationStatus.Valid,
            LayerCount = 1,
            ImportedAtUtc = DateTime.UtcNow.AddHours(-1),
            PackageDigest = "sha256:field-day-fixture"
        });
        await storage.MarkProjectCatalogEntryOpenedAsync("field-day");

        var form = layer.Form!;
        var formData = CreateCapturedFormData(form);
        var formValid = await new FormService().ValidateFormAsync(formData, form);
        Assert.True(formValid, string.Join(" | ", formData.ValidationErrors.Select(error => $"{error.Key}: {error.Value}")));
        Assert.Empty(formData.ValidationErrors);

        var capturedFeature = new Feature
        {
            Id = formData.FeatureId!,
            LayerId = layer.Id,
            Version = 1,
            Geometry = new Point(21.3, -157.8),
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            ModifiedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Attributes = new Dictionary<string, object?>(formData.Values, StringComparer.Ordinal)
        };
        await storage.StoreFeatureAsync(capturedFeature);

        var localPhotoPath = Path.Combine(_mediaRoot, "capture", "field-day-photo.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(localPhotoPath)!);
        await File.WriteAllTextAsync(localPhotoPath, "field day photo");
        await storage.StoreAttachmentMetadataAsync(new AttachmentInfo
        {
            Id = "field-day-photo-1",
            LayerId = layer.Id,
            FeatureId = capturedFeature.Id,
            FileName = "field-day-photo.jpg",
            ContentType = "image/jpeg",
            PayloadKind = AttachmentPayloadKind.Photo,
            SizeBytes = 15,
            LocalPath = localPhotoPath,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10),
            SyncStatus = AttachmentSyncStatus.PendingUpload
        });

        var conflictEvidenceRoot = Path.Combine(_artifactRoot, "conflicts");
        var conflictPlan = new LocalFieldConflictReplayPlan
        {
            RunId = "field-day-conflict",
            Layer = layer,
            FeatureId = "field-day-conflict",
            LocalVersion = 2,
            ServerVersion = 1,
            LocalAttributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["asset_id"] = "asset-conflict",
                ["status"] = "field-updated"
            },
            ServerAttributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["asset_id"] = "asset-conflict",
                ["status"] = "office-updated"
            },
            Resolution = ConflictResolution.AcceptServer
        };
        var peer = new LocalReplayFieldSyncPeer([LocalFieldConflictReplayHarness.CreateServerUpdate(conflictPlan)]);
        using var sync = new GeoPackageSyncService(
            storage,
            new TestAuthenticationService(),
            new TestConnectivityService(),
            peer,
            peer,
            peer);
        var conflictResult = await new LocalFieldConflictReplayHarness(
            storage,
            sync,
            conflictEvidenceRoot).RunAsync(conflictPlan);

        var exportRoot = Path.Combine(_artifactRoot, "exports");
        var exportResult = await new LocalRecordExportService(storage, exportRoot).ExportLayerAsync(layer);
        var catalogEntry = await storage.GetProjectCatalogEntryAsync("field-day");
        var diagnostics = await storage.GetOfflineCacheDiagnosticsAsync();
        var evidencePath = Path.Combine(_artifactRoot, "field-day.evidence.json");
        var evidence = new FieldDayEvidence(
            "honua.mobile.no-cloud-field-day.evidence.v1",
            NoCloud: true,
            CloudUploadIncluded: false,
            GeneratedAtUtc: DateTime.UtcNow,
            Steps:
            [
                Step("package-catalog", "passed", "field_project_catalog row installed and opened", "field-day"),
                Step("assignment-packet", "follow-up", "portable task packet adapters remain in honua-mobile#252", null),
                Step("record-collection", "passed", "form values validated and stored in GeoPackage", capturedFeature.Id),
                Step("media-attachment", "passed", "local photo metadata stored and exported from device path", "field-day-photo-1"),
                Step("lifecycle-transition", "follow-up", "formal lifecycle runtime remains in honua-mobile#251", null),
                Step("conflict-replay", conflictResult.ResolutionApplied ? "passed" : "failed", "local peer replayed conflict and selected resolution", Path.GetFileName(conflictResult.EvidencePath)),
                Step("export", File.Exists(exportResult.EvidenceManifestPath) ? "passed" : "failed", "local export package written", Path.GetFileName(exportResult.EvidenceManifestPath))
            ],
            Artifacts: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conflictEvidence"] = Path.GetFileName(conflictResult.EvidencePath),
                ["exportDirectory"] = Path.GetFileName(exportResult.ExportDirectory),
                ["exportEvidence"] = Path.GetFileName(exportResult.EvidenceManifestPath),
                ["recordsCsv"] = Path.GetFileName(exportResult.CsvPath),
                ["attachmentsManifest"] = Path.GetFileName(exportResult.AttachmentManifestPath)
            },
            Counts: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["records"] = exportResult.RecordCount,
                ["attachments"] = exportResult.AttachmentCount,
                ["copiedMediaFiles"] = exportResult.MediaFileCount,
                ["pendingOperations"] = diagnostics.Operations.PendingCount,
                ["conflicts"] = diagnostics.Operations.ConflictCount
            });
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(evidence, JsonOptions));

        Assert.NotNull(catalogEntry?.LastOpenedAtUtc);
        Assert.NotNull(catalogEntry.LastExportAtUtc);
        Assert.True(conflictResult.ResolutionApplied);
        Assert.Equal(2, exportResult.RecordCount);
        Assert.Equal(1, exportResult.AttachmentCount);
        Assert.Equal(1, exportResult.MediaFileCount);
        Assert.True(File.Exists(exportResult.EvidenceManifestPath));
        Assert.True(File.Exists(evidencePath));

        var evidenceJson = await File.ReadAllTextAsync(evidencePath);
        using var parsed = JsonDocument.Parse(evidenceJson);
        Assert.Equal(
            "honua.mobile.no-cloud-field-day.evidence.v1",
            parsed.RootElement.GetProperty("schemaVersion").GetString());
        Assert.True(parsed.RootElement.GetProperty("noCloud").GetBoolean());
        Assert.False(parsed.RootElement.GetProperty("cloudUploadIncluded").GetBoolean());
        Assert.Equal(7, parsed.RootElement.GetProperty("steps").GetArrayLength());
        Assert.Contains("\"status\": \"follow-up\"", evidenceJson);
        Assert.DoesNotContain(_artifactRoot, evidenceJson);
        Assert.DoesNotContain(_mediaRoot, evidenceJson);
        Assert.DoesNotContain(localPhotoPath, evidenceJson);
    }

    public void Dispose()
    {
        DeleteIfExists(_databasePath);
        DeleteIfExists($"{_databasePath}-wal");
        DeleteIfExists($"{_databasePath}-shm");

        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }

        if (Directory.Exists(_mediaRoot))
        {
            Directory.Delete(_mediaRoot, recursive: true);
        }
    }

    private static LayerInfo CreateLayer()
    {
        return new LayerInfo
        {
            Id = 1,
            ServiceId = "field-day",
            SourceId = "field-day/FeatureServer/1",
            Name = "Field Day Assets",
            GeometryType = GeometryType.Point,
            IsEditable = true,
            Form = CreateForm()
        };
    }

    private static FormDefinition CreateForm()
    {
        return new FormDefinition
        {
            FormId = "field_day_assets",
            Name = "Field Day Assets",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["default:status"] = "open"
            },
            Sections =
            [
                new FormSection
                {
                    SectionId = "main",
                    Label = "Main",
                    Fields =
                    [
                        Field("asset_id", "Asset ID", FormFieldType.Text, required: true),
                        ChoiceField("status", "Status", ["open", "closed"]),
                        new FormField
                        {
                            FieldId = "display_name",
                            Label = "Display name",
                            Type = FormFieldType.Calculated,
                            CalculatedExpression = "concat($asset_id,'-', $status)"
                        },
                        Field("location", "Location", FormFieldType.Location, required: true),
                        new FormField
                        {
                            FieldId = "photos",
                            Label = "Photos",
                            Type = FormFieldType.Photo,
                            Required = true,
                            Validation = new FieldValidationRule { MinMediaCount = 1 }
                        }
                    ]
                }
            ]
        };
    }

    private static FormData CreateCapturedFormData(FormDefinition form)
    {
        var values = MobileFormRuleRuntime.ApplyDefaultValues(form, new Dictionary<string, object?>
        {
            ["asset_id"] = "asset-field-day-1",
            ["location"] = new FieldGeoPoint(21.3, -157.8, 3)
        });
        values = MobileFormRuleRuntime.ApplyCalculatedValues(form, values);

        return new FormData
        {
            LayerId = 1,
            FeatureId = "asset-field-day-1",
            Values = values,
            Location = new FieldGeoPoint(21.3, -157.8, 3),
            Media =
            [
                new FieldMediaAttachment
                {
                    AttachmentId = "field-day-photo-1",
                    FieldId = "photos",
                    FileName = "field-day-photo.jpg",
                    ContentType = "image/jpeg",
                    MediaType = FieldMediaType.Photo,
                    SizeBytes = 15,
                    CaptureLocation = new FieldGeoPoint(21.3, -157.8, 3)
                }
            ]
        };
    }

    private static FormField Field(
        string fieldId,
        string label,
        FormFieldType type,
        bool required = false)
        => new()
        {
            FieldId = fieldId,
            Label = label,
            Type = type,
            Required = required
        };

    private static FormField ChoiceField(
        string fieldId,
        string label,
        IReadOnlyList<string> choices)
        => new()
        {
            FieldId = fieldId,
            Label = label,
            Type = FormFieldType.SingleChoice,
            Required = true,
            Choices = choices.Select(choice => new FieldChoice { Value = choice, Label = choice }).ToList()
        };

    private static FieldDayStep Step(string name, string status, string detail, string? artifact)
        => new(name, status, detail, artifact);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record FieldDayEvidence(
        string SchemaVersion,
        bool NoCloud,
        bool CloudUploadIncluded,
        DateTime GeneratedAtUtc,
        IReadOnlyList<FieldDayStep> Steps,
        IReadOnlyDictionary<string, string?> Artifacts,
        IReadOnlyDictionary<string, int> Counts);

    private sealed record FieldDayStep(
        string Name,
        string Status,
        string Detail,
        string? Artifact);
}
