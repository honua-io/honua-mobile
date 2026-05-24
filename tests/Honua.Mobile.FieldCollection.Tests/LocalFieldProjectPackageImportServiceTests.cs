using System.Security.Cryptography;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Assignments;
using Honua.Mobile.FieldCollection.Services.Packages;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Projects;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class LocalFieldProjectPackageImportServiceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly string _packageRoot;
    private readonly string _installRoot;

    public LocalFieldProjectPackageImportServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-package-import-{Guid.NewGuid():N}.gpkg");
        _packageRoot = Path.Combine(Path.GetTempPath(), $"honua-package-source-{Guid.NewGuid():N}");
        _installRoot = Path.Combine(Path.GetTempPath(), $"honua-package-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_packageRoot);
        Directory.CreateDirectory(_installRoot);
    }

    [Fact]
    public async Task ImportAsync_WithValidPackage_CopiesArtifactsCreatesCatalogLayersAndAssignments()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var manifestPath = await WritePackageAsync(CreatePackage());
        var result = await new LocalFieldProjectPackageImportService(storage).ImportAsync(new LocalFieldProjectPackageImportRequest
        {
            ManifestPath = manifestPath,
            DestinationRootDirectory = _installRoot,
            ImportSource = "usb",
            OverwriteExisting = true
        });

        Assert.True(result.Imported, string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == LocalFieldProjectPackageDiagnosticSeverity.Error);
        Assert.Equal("local-inspection-demo", result.ProjectId);
        Assert.True(File.Exists(result.InstalledManifestPath));
        var importedFile = Assert.Single(result.ImportedFiles);
        Assert.Equal("data/assets.gpkg", importedFile.RelativePath);
        Assert.True(File.Exists(importedFile.DestinationPath));

        var catalogEntry = await storage.GetProjectCatalogEntryAsync("local-inspection-demo");
        Assert.NotNull(catalogEntry);
        Assert.Equal(FieldProjectCatalogState.Installed, catalogEntry.State);
        Assert.Equal(FieldProjectValidationStatus.Valid, catalogEntry.ValidationStatus);
        Assert.Equal("pkg-assets", catalogEntry.PackageId);
        Assert.Equal(2, catalogEntry.LayerCount);
        Assert.Equal("usb", catalogEntry.ImportSource);

        var layers = await storage.GetLayersAsync();
        Assert.Equal(2, layers.Count);
        Assert.All(layers, layer => Assert.Equal("local-inspection-demo", layer.ServiceId));
        Assert.Contains(layers, layer => layer.Form?.FormId == "asset-inspection");
        Assert.Contains(layers, layer => layer.Form?.Metadata["honua:bindingId"] == "incident-assets");

        var assignmentService = new LocalFieldAssignmentService(storage);
        var fieldUserAssignments = await assignmentService.GetAssignmentsAsync(new LocalFieldAssignmentFilter
        {
            AssigneeUserId = "field-user-1",
            Status = FieldAssignmentStatus.NotStarted,
            MinimumPriority = FieldAssignmentPriority.High,
            DueBeforeUtc = new DateTimeOffset(2026, 5, 24, 4, 0, 0, TimeSpan.Zero),
            SourceId = "assets",
            IntersectsExtent = new FeatureBoundingBox
            {
                MinX = -158,
                MinY = 21,
                MaxX = -157,
                MaxY = 22,
                Crs = "EPSG:4326"
            }
        });
        var assignment = Assert.Single(fieldUserAssignments);
        Assert.Equal("task-asset-100", assignment.AssignmentId);
        Assert.Equal("asset-100", Assert.Single(assignment.RecordIds));

        Assert.True(await assignmentService.UpdateStatusAsync("task-asset-100", FieldAssignmentStatus.Complete));
        var completed = Assert.Single(await assignmentService.GetAssignmentsAsync(new LocalFieldAssignmentFilter
        {
            Status = FieldAssignmentStatus.Complete
        }));
        Assert.Equal("task-asset-100", completed.AssignmentId);
        Assert.NotNull(completed.CompletedAtUtc);

        using var restartedStorage = new GeoPackageStorageService(_databasePath);
        var restartedAssignments = await restartedStorage.GetFieldAssignmentsAsync(new LocalFieldAssignmentFilter
        {
            Status = FieldAssignmentStatus.Complete
        });
        Assert.Single(restartedAssignments);
    }

    [Fact]
    public async Task ImportAsync_WithInvalidPackages_ReturnsDeterministicDiagnostics()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var validPackage = CreatePackage();
        var cases = new[]
        {
            (
                Name: "unsupported schema",
                Package: validPackage with { SchemaVersion = "honua.field-project-package.v0" },
                Code: FieldProjectPackageValidationCodes.UnsupportedSchemaVersion),
            (
                Name: "missing form",
                Package: validPackage with
                {
                    Bindings =
                    [
                        validPackage.Bindings[0] with { FormId = "missing-form" }
                    ]
                },
                Code: FieldProjectPackageValidationCodes.MissingReference),
            (
                Name: "missing layer binding",
                Package: validPackage with { Bindings = [] },
                Code: "missing-layer-binding"),
            (
                Name: "invalid media policy",
                Package: validPackage with
                {
                    MediaPolicy = new FieldProjectMediaPolicy
                    {
                        Requirements =
                        [
                            new FieldMediaRequirement
                            {
                                FormId = "asset-inspection",
                                FieldId = "photos",
                                MediaType = FieldMediaType.Photo,
                                MinCount = 3,
                                MaxCount = 1
                            }
                        ]
                    }
                },
                Code: FieldProjectPackageValidationCodes.InvalidValue)
        };

        foreach (var testCase in cases)
        {
            var manifestPath = await WritePackageAsync(testCase.Package, testCase.Name.Replace(' ', '-'));
            var result = await new LocalFieldProjectPackageImportService(storage).ImportAsync(new LocalFieldProjectPackageImportRequest
            {
                ManifestPath = manifestPath,
                DestinationRootDirectory = _installRoot,
                OverwriteExisting = true
            });

            Assert.False(result.Imported);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == testCase.Code);
        }

        var catalogEntry = await storage.GetProjectCatalogEntryAsync("local-inspection-demo");
        Assert.NotNull(catalogEntry);
        Assert.Equal(FieldProjectCatalogState.Invalid, catalogEntry.State);
        Assert.Equal(FieldProjectValidationStatus.Error, catalogEntry.ValidationStatus);
    }

    public void Dispose()
    {
        DeleteIfExists(_databasePath);
        DeleteIfExists($"{_databasePath}-wal");
        DeleteIfExists($"{_databasePath}-shm");

        if (Directory.Exists(_packageRoot))
        {
            Directory.Delete(_packageRoot, recursive: true);
        }

        if (Directory.Exists(_installRoot))
        {
            Directory.Delete(_installRoot, recursive: true);
        }
    }

    private async Task<string> WritePackageAsync(FieldProjectPackage package, string directoryName = "valid")
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

    internal static FieldProjectPackage CreatePackage()
        => new()
        {
            SchemaVersion = FieldProjectPackage.CurrentSchemaVersion,
            ProjectId = "local-inspection-demo",
            Name = "Local Inspection Demo",
            Version = "2026.05",
            Description = "No-cloud local package fixture.",
            Sources =
            [
                new SourceDescriptor
                {
                    Id = "assets",
                    Protocol = "FeatureServer",
                    Locator = new SourceLocator
                    {
                        ServiceId = "mobile_offline_demo",
                        LayerId = 68910
                    },
                    Capabilities = ["query", "edit", "attachments"],
                    Schema = new SourceSchema
                    {
                        PrimaryKey = "globalid",
                        ObjectIdField = "objectid",
                        GlobalIdField = "globalid",
                        GeometryType = FeatureSpatialGeometryType.Point,
                        SpatialReference = "EPSG:4326"
                    }
                }
            ],
            Forms =
            [
                CreateInspectionForm(),
                CreateIncidentForm()
            ],
            Bindings =
            [
                new FieldProjectBinding
                {
                    BindingId = "asset-inspection-assets",
                    FormId = "asset-inspection",
                    SourceId = "assets",
                    OfflinePackageId = "pkg-assets",
                    Editable = true,
                    DisplayFieldId = "asset_id",
                    SourceQuery = new SourceQuery
                    {
                        Where = "1=1",
                        ReturnGeometry = true,
                        Bbox = new FeatureBoundingBox
                        {
                            MinX = -158,
                            MinY = 21,
                            MaxX = -157,
                            MaxY = 22,
                            Crs = "EPSG:4326"
                        }
                    }
                },
                new FieldProjectBinding
                {
                    BindingId = "incident-assets",
                    FormId = "incident-report",
                    SourceId = "assets",
                    OfflinePackageId = "pkg-assets",
                    Editable = true,
                    DisplayFieldId = "summary"
                }
            ],
            OfflinePackages =
            [
                new FieldOfflinePackageReference
                {
                    PackageId = "pkg-assets",
                    Kind = FieldOfflinePackageKind.FeatureData,
                    RelativePath = "data/assets.gpkg",
                    ContentType = "application/geopackage+sqlite3",
                    SourceIds = ["assets"]
                }
            ],
            MediaPolicy = new FieldProjectMediaPolicy
            {
                AllowedContentTypes = ["image/jpeg", "image/png", "audio/mp4"],
                MaxAttachmentBytes = 52_428_800,
                RequiresFaceBlurByDefault = true,
                Requirements =
                [
                    new FieldMediaRequirement
                    {
                        FormId = "asset-inspection",
                        FieldId = "photos",
                        MediaType = FieldMediaType.Photo,
                        MinCount = 1,
                        MaxCount = 12,
                        AllowedContentTypes = ["image/jpeg", "image/png"]
                    }
                ]
            },
            LifecyclePolicy = FieldRecordLifecyclePolicy.Default,
            TaskPackets =
            [
                new FieldTaskPacket
                {
                    TaskPacketId = "crew-a-day-1",
                    Name = "Crew A day 1",
                    Assignments =
                    [
                        new FieldAssignment
                        {
                            AssignmentId = "task-asset-100",
                            BindingId = "asset-inspection-assets",
                            AssigneeUserId = "field-user-1",
                            Priority = FieldAssignmentPriority.High,
                            Status = FieldAssignmentStatus.NotStarted,
                            DueAtUtc = new DateTimeOffset(2026, 5, 24, 3, 0, 0, TimeSpan.Zero),
                            RecordIds = ["asset-100"],
                            WorkQuery = new SourceQuery
                            {
                                ReturnGeometry = true,
                                Bbox = new FeatureBoundingBox
                                {
                                    MinX = -158,
                                    MinY = 21,
                                    MaxX = -157,
                                    MaxY = 22,
                                    Crs = "EPSG:4326"
                                }
                            },
                            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["route"] = "north"
                            }
                        },
                        new FieldAssignment
                        {
                            AssignmentId = "task-incident-sweep",
                            BindingId = "incident-assets",
                            CrewId = "crew-a",
                            Priority = FieldAssignmentPriority.Normal,
                            Status = FieldAssignmentStatus.NotStarted,
                            WorkQuery = new SourceQuery
                            {
                                Where = "priority IN ('normal','urgent')",
                                ReturnGeometry = true
                            }
                        }
                    ]
                }
            ]
        };

    private static FormDefinition CreateInspectionForm()
        => new()
        {
            FormId = "asset-inspection",
            Name = "Asset Inspection",
            Version = "2026.05",
            Target = new FormTarget
            {
                SourceId = "assets",
                ServiceId = "mobile_offline_demo",
                LayerId = 68910
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
                        new FormField
                        {
                            FieldId = "condition",
                            Label = "Condition",
                            Type = FormFieldType.SingleChoice,
                            Required = true,
                            ChoiceSetId = "asset-condition",
                            Choices =
                            [
                                new FieldChoice { Value = "good", Label = "Good" },
                                new FieldChoice { Value = "needsRepair", Label = "Needs repair" }
                            ]
                        },
                        new FormField
                        {
                            FieldId = "photos",
                            Label = "Photos",
                            Type = FormFieldType.Photo,
                            Required = true,
                            Validation = new FieldValidationRule { MinMediaCount = 1 },
                            MediaPolicy = new FieldMediaCapturePolicy
                            {
                                AllowedContentTypes = ["image/jpeg", "image/png"],
                                CaptureLocation = true
                            }
                        }
                    ]
                }
            ]
        };

    private static FormDefinition CreateIncidentForm()
        => new()
        {
            FormId = "incident-report",
            Name = "Incident Report",
            Version = "2026.05",
            Target = new FormTarget
            {
                SourceId = "assets",
                ServiceId = "mobile_offline_demo",
                LayerId = 68910
            },
            Sections =
            [
                new FormSection
                {
                    SectionId = "main",
                    Label = "Main",
                    Fields =
                    [
                        Field("summary", "Summary", FormFieldType.Text, required: true),
                        new FormField
                        {
                            FieldId = "linked_asset",
                            Label = "Linked asset",
                            Type = FormFieldType.RecordLink,
                            ReferencedFormId = "asset-inspection"
                        }
                    ]
                }
            ]
        };

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

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
