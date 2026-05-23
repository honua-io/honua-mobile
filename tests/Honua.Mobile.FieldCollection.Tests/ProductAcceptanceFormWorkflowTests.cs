using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Forms;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class ProductAcceptanceFormWorkflowTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _artifactDirectory;

    public ProductAcceptanceFormWorkflowTests()
    {
        _artifactDirectory = Path.Combine(Path.GetTempPath(), $"honua-form-acceptance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_artifactDirectory);
    }

    [Fact]
    public async Task FormRulesRepeatSectionsScenario_ValidatesMediaAndWritesQueueArtifact()
    {
        var form = CreateInspectionFormDefinition();
        var repeatSection = form.Sections.Single(section => section.SectionId == "visits");
        var conditionField = repeatSection.Fields.Single(field => field.FieldId == "condition");
        var repairNotesField = repeatSection.Fields.Single(field => field.FieldId == "repair_notes");
        var photoField = repeatSection.Fields.Single(field => field.FieldId == "photos");
        var values = MobileFormRuleRuntime.ApplyDefaultValues(
            form,
            new Dictionary<string, object?>
            {
                ["asset"] = "pump-101",
                [MobileFormRepeatKey.ForField(repeatSection, 0, conditionField)] = "damaged",
                [MobileFormRepeatKey.ForField(repeatSection, 0, repairNotesField)] = "replace gasket",
                [MobileFormRepeatKey.ForField(repeatSection, 0, photoField)] = "photo-acceptance-1",
            },
            initialRepeatCount: 1);
        var formData = new FormData
        {
            LayerId = 0,
            FeatureId = "asset-form-101",
            Values = values,
            Media =
            [
                new FieldMediaAttachment
                {
                    AttachmentId = "photo-acceptance-1",
                    FieldId = MobileFormRepeatKey.ForField(repeatSection, 0, photoField),
                    FileName = "repeat-photo.jpg",
                    ContentType = "image/jpeg",
                    MediaType = FieldMediaType.Photo,
                    SizeBytes = 128,
                },
            ],
        };

        var valid = await new FormService().ValidateFormAsync(formData, form);
        var bindings = MobileFormRuleRuntime.BuildFieldBindings(form, formData.Values, initialRepeatCount: 1);
        var record = formData.ToSdkFieldRecord(form);
        var artifact = ProductAcceptanceFormArtifact.From(
            form,
            record,
            repeatSection.SectionId,
            repeatEntryCount: 1,
            bindings.Count);
        var artifactPath = Path.Combine(_artifactDirectory, "form-rules-repeat-sections.evidence.json");
        await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(artifact, JsonOptions));

        Assert.True(valid, string.Join(" | ", formData.ValidationErrors.Select(error => $"{error.Key}: {error.Value}")));
        Assert.Empty(formData.ValidationErrors);
        Assert.Equal("open", record.Values["status"]);
        Assert.Equal("pump-101-open", record.Values["display"]);
        Assert.Contains(bindings, binding => binding.ValueKey == "visits[0].condition");
        Assert.Single(record.Media);
        Assert.Equal("photo-acceptance-1", record.Media[0].AttachmentId);
        Assert.True(File.Exists(artifactPath));

        var artifactJson = await File.ReadAllTextAsync(artifactPath);
        Assert.Contains("\"schemaVersion\": \"honua.mobile.form-repeat-acceptance.evidence.v1\"", artifactJson);
        Assert.Contains("\"scenarioId\": \"form-rules-repeat-sections\"", artifactJson);
        Assert.Contains("\"attachmentRoundTripCount\": 1", artifactJson);
        Assert.Contains("\"repeatEntryCount\": 1", artifactJson);
    }

    public void Dispose()
    {
        if (Directory.Exists(_artifactDirectory))
        {
            Directory.Delete(_artifactDirectory, recursive: true);
        }
    }

    private static FormDefinition CreateInspectionFormDefinition()
    {
        var visits = new FormSection
        {
            SectionId = "visits",
            Label = "Visits",
            Repeatable = true,
            Fields =
            [
                CreateFormField("condition", FormFieldType.SingleChoice, required: true, choices: ["ok", "damaged"]),
                new FormField
                {
                    FieldId = "repair_notes",
                    Label = "Repair notes",
                    Type = FormFieldType.Text,
                    Required = true,
                    VisibilityRule = new FieldVisibilityRule
                    {
                        DependsOnFieldId = "condition",
                        Operator = ComparisonOperator.Equals,
                        MatchValue = "damaged",
                    },
                },
                new FormField
                {
                    FieldId = "photos",
                    Label = "Photos",
                    Type = FormFieldType.Photo,
                    Required = true,
                    Validation = new FieldValidationRule { MinMediaCount = 1 },
                },
            ],
        };

        return new FormDefinition
        {
            FormId = "inspection",
            Name = "Inspection",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["default:status"] = "open",
            },
            Sections =
            [
                new FormSection
                {
                    SectionId = "main",
                    Label = "Main",
                    Fields =
                    [
                        CreateFormField("asset", FormFieldType.Text, required: true),
                        CreateFormField("status", FormFieldType.SingleChoice, choices: ["open", "closed"]),
                        new FormField
                        {
                            FieldId = "display",
                            Label = "Display",
                            Type = FormFieldType.Calculated,
                            CalculatedExpression = "concat($asset,'-', $status)",
                        },
                    ],
                },
                visits,
            ],
        };
    }

    private static FormField CreateFormField(
        string fieldId,
        FormFieldType type,
        bool required = false,
        IReadOnlyList<string>? choices = null)
        => new()
        {
            FieldId = fieldId,
            Label = fieldId,
            Type = type,
            Required = required,
            Choices = choices is null
                ? []
                : choices.Select(choice => new FieldChoice { Value = choice, Label = choice }).ToList(),
        };

    private sealed record ProductAcceptanceFormArtifact(
        string SchemaVersion,
        string ScenarioId,
        string FormId,
        string RecordId,
        string RepeatSectionId,
        int RepeatEntryCount,
        int BindingCount,
        int AttachmentRoundTripCount,
        IReadOnlyDictionary<string, object?> Values,
        IReadOnlyList<string> AttachmentIds)
    {
        public static ProductAcceptanceFormArtifact From(
            FormDefinition form,
            FieldRecord record,
            string repeatSectionId,
            int repeatEntryCount,
            int bindingCount)
            => new(
                "honua.mobile.form-repeat-acceptance.evidence.v1",
                "form-rules-repeat-sections",
                form.FormId,
                record.RecordId,
                repeatSectionId,
                repeatEntryCount,
                bindingCount,
                record.Media.Count,
                new Dictionary<string, object?>(record.Values, StringComparer.Ordinal),
                record.Media.Select(media => media.AttachmentId).ToArray());
    }
}
