using System.Text.Json;
using Honua.Mobile.Field.Capture;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Records;
using Honua.Sdk.Offline.Abstractions;

namespace Honua.Mobile.Offline.Tests;

public sealed class FieldWorkflowOfflineAdapterTests : IDisposable
{
    private readonly string _databasePath;

    public FieldWorkflowOfflineAdapterTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-field-offline-{Guid.NewGuid():N}.gpkg");
    }

    [Fact]
    public async Task ValidatedSdkFieldRecord_CanFlowIntoOfflineJournalMetadata()
    {
        var workflow = new MobileFieldCaptureWorkflow(new DuplicateDetector());
        var form = new FormDefinition
        {
            FormId = "inspection",
            Name = "Inspection",
            Sections =
            [
                new FormSection
                {
                    SectionId = "main",
                    Label = "Main",
                    Fields =
                    [
                        new FormField { FieldId = "asset_id", Label = "Asset ID", Type = FormFieldType.Text, Required = true },
                    ],
                },
            ],
        };
        var fieldRecord = new FieldRecord
        {
            RecordId = "field-1",
            FormId = "inspection",
            Values =
            {
                ["asset_id"] = "A-100",
            },
        };

        var validation = workflow.Validate(form, fieldRecord);
        Assert.True(validation.IsValid);

        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        var adapter = new GeoPackageSdkOfflineStoreAdapter(store);
        await adapter.EnqueueAsync(new OfflineChangeJournalEntry
        {
            OperationId = "op-field-1",
            PackageId = "area-1",
            SourceId = "assets",
            Source = new FeatureSource { CollectionId = "assets" },
            OperationKind = OfflineEditOperationKind.Add,
            Feature = new FeatureEditFeature
            {
                Id = fieldRecord.RecordId,
                Attributes = new Dictionary<string, JsonElement>
                {
                    ["asset_id"] = JsonSerializer.SerializeToElement(fieldRecord.Values["asset_id"]),
                },
            },
            Metadata = new Dictionary<string, string>
            {
                ["formId"] = fieldRecord.FormId,
                ["fieldRecordId"] = fieldRecord.RecordId,
            },
        });

        var pending = await adapter.GetPendingAsync("area-1", 10);

        var entry = Assert.Single(pending);
        Assert.Equal("inspection", entry.Metadata["formId"]);
        Assert.Equal("field-1", entry.Metadata["fieldRecordId"]);
        Assert.Equal("A-100", entry.Feature?.Attributes["asset_id"].GetString());
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
