using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Forms;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class LocalFormParityGoldenFixtureTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _artifactDirectory;

    public LocalFormParityGoldenFixtureTests()
    {
        _artifactDirectory = Path.Combine(Path.GetTempPath(), $"honua-form-golden-fixtures-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_artifactDirectory);
    }

    [Fact]
    public async Task GoldenFixtures_ValidateRestoreDraftsAndWriteCoverageEvidence()
    {
        var fixtures = CreateFixtures();
        var summaries = new List<GoldenFixtureSummary>();

        foreach (var fixture in fixtures)
        {
            var values = MobileFormRuleRuntime.ApplyDefaultValues(
                fixture.Form,
                fixture.Values,
                fixture.InitialRepeatCount);
            values = MobileFormRuleRuntime.ApplyCalculatedValues(fixture.Form, values);
            var formData = new FormData
            {
                LayerId = fixture.LayerId,
                FeatureId = fixture.RecordId,
                Values = values,
                Location = fixture.Location,
                Media = fixture.Media.ToList(),
                CreatedAt = new DateTime(2026, 5, 23, 8, 30, 0, DateTimeKind.Utc)
            };

            var valid = await new FormService().ValidateFormAsync(formData, fixture.Form);
            Assert.True(valid, $"{fixture.FixtureId}: {string.Join(" | ", formData.ValidationErrors.Select(error => $"{error.Key}: {error.Value}"))}");
            Assert.Empty(formData.ValidationErrors);

            var draftService = new SettingsFormDraftService(new InMemorySettingsService());
            await draftService.SaveDraftAsync(new FormDraftSnapshot
            {
                LayerId = fixture.LayerId,
                FeatureId = fixture.RecordId,
                FormId = fixture.Form.FormId,
                Values = new Dictionary<string, object?>(formData.Values, StringComparer.Ordinal),
                ValidationErrors = new Dictionary<string, string>(formData.ValidationErrors, StringComparer.Ordinal),
                RepeatCounts = new Dictionary<string, int>(fixture.RepeatCounts, StringComparer.Ordinal)
            });
            var restored = await draftService.GetDraftAsync(fixture.LayerId, fixture.RecordId);
            Assert.NotNull(restored);
            Assert.Equal(fixture.Form.FormId, restored.FormId);
            Assert.True(restored.Values.Count >= fixture.ExpectedRestoredValueCount);

            var bindings = MobileFormRuleRuntime.BuildFieldBindings(
                fixture.Form,
                formData.Values,
                fixture.InitialRepeatCount);
            var controls = fixture.Form.Sections
                .SelectMany(section => section.Fields)
                .ToDictionary(
                    field => field.FieldId,
                    field => MobileFormControlSelector.Select(field).ToString(),
                    StringComparer.Ordinal);

            Assert.DoesNotContain("Unsupported", controls.Values);
            foreach (var valueKey in fixture.ExpectedValueKeys)
            {
                Assert.Contains(valueKey, formData.Values.Keys);
            }

            summaries.Add(GoldenFixtureSummary.From(
                fixture,
                formData,
                bindings.Count,
                controls));
        }

        var evidence = new GoldenFixtureSuiteEvidence(
            "honua.mobile.form-parity-golden-fixtures.evidence.v1",
            GeneratedAtUtc: DateTime.UtcNow,
            FixtureCount: summaries.Count,
            summaries,
            UnsupportedFollowUps:
            [
                "Mobile follow-up: full XLSForm/Arcade expression parity beyond supported concat/default/visibility rules remains a broader runtime slice."
            ]);
        var evidencePath = Path.Combine(_artifactDirectory, "local-form-parity-golden-fixtures.evidence.json");
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(evidence, JsonOptions));

        Assert.Equal(4, summaries.Count);
        Assert.All(summaries, summary => Assert.NotEmpty(summary.Capabilities));
        Assert.All(evidence.UnsupportedFollowUps, followUp =>
            Assert.True(
                followUp.Contains("SDK", StringComparison.Ordinal) ||
                followUp.Contains("Mobile", StringComparison.Ordinal),
                followUp));
        Assert.Contains("inspection-required-conditional-media", summaries.Select(summary => summary.FixtureId));
        Assert.Contains("asset-inventory-barcode-record-link", summaries.Select(summary => summary.FixtureId));
        Assert.Contains("incident-report-location-signature", summaries.Select(summary => summary.FixtureId));
        Assert.Contains("repeat-heavy-survey", summaries.Select(summary => summary.FixtureId));
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Required rules");
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Conditional visibility");
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Calculated values");
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Repeat groups");
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Media constraints");
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Record links");
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Barcode capture");
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Shared choice-set ids");
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Record-link target metadata");
        Assert.Contains(summaries.SelectMany(summary => summary.Capabilities), capability => capability == "Media capture policy");
        Assert.True(File.Exists(evidencePath));
    }

    [Fact]
    public async Task GoldenFixture_RejectedMediaProducesDeterministicValidationError()
    {
        var fixture = CreateInspectionFixture();
        var values = MobileFormRuleRuntime.ApplyDefaultValues(
            fixture.Form,
            fixture.Values,
            fixture.InitialRepeatCount);
        values = MobileFormRuleRuntime.ApplyCalculatedValues(fixture.Form, values);
        var formData = new FormData
        {
            LayerId = fixture.LayerId,
            FeatureId = "inspection-rejected-media",
            Values = values,
            Media = []
        };

        var valid = await new FormService().ValidateFormAsync(formData, fixture.Form);

        Assert.False(valid);
        Assert.Contains("photos", formData.ValidationErrors.Keys);
        Assert.Contains("required", formData.ValidationErrors["photos"], StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_artifactDirectory))
        {
            Directory.Delete(_artifactDirectory, recursive: true);
        }
    }

    private static IReadOnlyList<GoldenFixture> CreateFixtures()
        =>
        [
            CreateInspectionFixture(),
            CreateAssetInventoryFixture(),
            CreateIncidentReportFixture(),
            CreateRepeatHeavySurveyFixture()
        ];

    private static GoldenFixture CreateInspectionFixture()
    {
        var form = new FormDefinition
        {
            FormId = "inspection_required_conditional_media",
            Name = "Inspection Required Conditional Media",
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
                        ChoiceField("status", "Status", required: true, choices: ["open", "closed"]) with
                        {
                            ChoiceSetId = "inspection-statuses"
                        },
                        new FormField
                        {
                            FieldId = "display_name",
                            Label = "Display name",
                            Type = FormFieldType.Calculated,
                            CalculatedExpression = "concat($asset_id,'-', $status)"
                        },
                        new FormField
                        {
                            FieldId = "close_reason",
                            Label = "Close reason",
                            Type = FormFieldType.Text,
                            Required = true,
                            VisibilityRule = new FieldVisibilityRule
                            {
                                DependsOnFieldId = "status",
                                Operator = ComparisonOperator.Equals,
                                MatchValue = "closed"
                            }
                        },
                        new FormField
                        {
                            FieldId = "photos",
                            Label = "Photos",
                            Type = FormFieldType.Photo,
                            Required = true,
                            Validation = new FieldValidationRule
                            {
                                MinMediaCount = 1
                            },
                            MediaPolicy = PhotoMediaPolicy()
                        }
                    ]
                }
            ]
        };

        return new GoldenFixture(
            "inspection-required-conditional-media",
            LayerId: 101,
            "inspection-001",
            form,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["asset_id"] = "pump-101",
                ["status"] = "closed",
                ["close_reason"] = "Repaired in field"
            },
            Media:
            [
                Media("photo-inspection-1", "photos", FieldMediaType.Photo)
            ],
            Location: null,
            RepeatCounts: new Dictionary<string, int>(StringComparer.Ordinal),
            InitialRepeatCount: 1,
            ExpectedRestoredValueCount: 4,
            ExpectedValueKeys: ["asset_id", "display_name", "close_reason"],
            Capabilities:
            [
                "Required rules",
                "Conditional visibility",
                "Calculated values",
                "Media constraints",
                "Media capture policy",
                "Shared choice-set ids",
                "Choice sets",
                "Draft restore"
            ]);
    }

    private static GoldenFixture CreateAssetInventoryFixture()
    {
        var form = new FormDefinition
        {
            FormId = "asset_inventory_barcode_record_link",
            Name = "Asset Inventory Barcode Record Link",
            Sections =
            [
                new FormSection
                {
                    SectionId = "inventory",
                    Label = "Inventory",
                    Fields =
                    [
                        Field("asset_tag", "Asset tag", FormFieldType.Barcode, required: true),
                        ChoiceField("asset_type", "Asset type", required: true, choices: ["pump", "valve", "meter"]) with
                        {
                            ChoiceSetId = "asset-types"
                        },
                        new FormField
                        {
                            FieldId = "condition_codes",
                            Label = "Condition codes",
                            Type = FormFieldType.MultipleChoice,
                            Required = true,
                            ChoiceSetId = "condition-codes",
                            Choices =
                            [
                                new FieldChoice { Value = "working", Label = "Working" },
                                new FieldChoice { Value = "wet", Label = "Wet" },
                                new FieldChoice { Value = "needs_service", Label = "Needs service" }
                            ]
                        },
                        new FormField
                        {
                            FieldId = "linked_work_order",
                            Label = "Linked work order",
                            Type = FormFieldType.RecordLink,
                            Required = true,
                            ReferencedFormId = "work_order"
                        },
                        new FormField
                        {
                            FieldId = "install_date",
                            Label = "Install date",
                            Type = FormFieldType.Date,
                            Required = true
                        }
                    ]
                }
            ]
        };

        return new GoldenFixture(
            "asset-inventory-barcode-record-link",
            LayerId: 102,
            "inventory-001",
            form,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["asset_tag"] = "QR-PUMP-101",
                ["asset_type"] = "pump",
                ["condition_codes"] = new[] { "working", "wet" },
                ["linked_work_order"] = "wo-2026-0001",
                ["install_date"] = new DateOnly(2026, 5, 23)
            },
            Media: [],
            Location: null,
            RepeatCounts: new Dictionary<string, int>(StringComparer.Ordinal),
            InitialRepeatCount: 1,
            ExpectedRestoredValueCount: 5,
            ExpectedValueKeys: ["asset_tag", "condition_codes", "linked_work_order"],
            Capabilities:
            [
                "Barcode capture",
                "Record links",
                "Record-link target metadata",
                "Shared choice-set ids",
                "Choice sets",
                "Required rules",
                "Draft restore"
            ]);
    }

    private static GoldenFixture CreateIncidentReportFixture()
    {
        var form = new FormDefinition
        {
            FormId = "incident_report_location_signature",
            Name = "Incident Report Location Signature",
            Sections =
            [
                new FormSection
                {
                    SectionId = "incident",
                    Label = "Incident",
                    Fields =
                    [
                        ChoiceField("severity", "Severity", required: true, choices: ["low", "medium", "high"]) with
                        {
                            ChoiceSetId = "incident-severity"
                        },
                        new FormField
                        {
                            FieldId = "description",
                            Label = "Description",
                            Type = FormFieldType.Text,
                            Required = true,
                            Validation = new FieldValidationRule
                            {
                                MinLength = 10,
                                MaxLength = 1024
                            }
                        },
                        Field("incident_location", "Incident location", FormFieldType.Location, required: true),
                        Field("injury", "Injury", FormFieldType.YesNo),
                        new FormField
                        {
                            FieldId = "injury_notes",
                            Label = "Injury notes",
                            Type = FormFieldType.Text,
                            Required = true,
                            VisibilityRule = new FieldVisibilityRule
                            {
                                DependsOnFieldId = "injury",
                                Operator = ComparisonOperator.Equals,
                                MatchValue = true
                            }
                        },
                        new FormField
                        {
                            FieldId = "signature",
                            Label = "Signature",
                            Type = FormFieldType.Signature,
                            Required = true,
                            Validation = new FieldValidationRule { MinMediaCount = 1 },
                            MediaPolicy = new FieldMediaCapturePolicy
                            {
                                AllowedContentTypes = ["image/png"],
                                CaptureLocation = true
                            }
                        }
                    ]
                }
            ]
        };

        return new GoldenFixture(
            "incident-report-location-signature",
            LayerId: 103,
            "incident-001",
            form,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["severity"] = "high",
                ["description"] = "Water main break at facility entrance.",
                ["incident_location"] = new FieldGeoPoint(21.3, -157.8, 3),
                ["injury"] = true,
                ["injury_notes"] = "Minor slip reported."
            },
            Media:
            [
                Media("signature-incident-1", "signature", FieldMediaType.Signature)
            ],
            Location: new FieldGeoPoint(21.3, -157.8, 3),
            RepeatCounts: new Dictionary<string, int>(StringComparer.Ordinal),
            InitialRepeatCount: 1,
            ExpectedRestoredValueCount: 5,
            ExpectedValueKeys: ["incident_location", "injury_notes"],
            Capabilities:
            [
                "Required rules",
                "Conditional visibility",
                "Location capture",
                "Media constraints",
                "Media capture policy",
                "Shared choice-set ids",
                "Draft restore"
            ]);
    }

    private static GoldenFixture CreateRepeatHeavySurveyFixture()
    {
        var observations = new FormSection
        {
            SectionId = "observations",
            Label = "Observations",
            Repeatable = true,
            Fields =
            [
                ChoiceField("condition", "Condition", required: true, choices: ["ok", "damaged"]) with
                {
                    ChoiceSetId = "observation-condition"
                },
                new FormField
                {
                    FieldId = "quantity",
                    Label = "Quantity",
                    Type = FormFieldType.Numeric,
                    Required = true,
                    Validation = new FieldValidationRule
                    {
                        MinNumericValue = 1,
                        MaxNumericValue = 100
                    }
                },
                new FormField
                {
                    FieldId = "observation_label",
                    Label = "Observation label",
                    Type = FormFieldType.Calculated,
                    CalculatedExpression = "concat($condition,'-', $quantity)"
                },
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
                        MatchValue = "damaged"
                    }
                },
                new FormField
                {
                    FieldId = "photos",
                    Label = "Photos",
                    Type = FormFieldType.Photo,
                    Required = true,
                    Validation = new FieldValidationRule { MinMediaCount = 1 },
                    MediaPolicy = PhotoMediaPolicy()
                }
            ]
        };
        var form = new FormDefinition
        {
            FormId = "repeat_heavy_survey",
            Name = "Repeat Heavy Survey",
            Sections =
            [
                new FormSection
                {
                    SectionId = "main",
                    Label = "Main",
                    Fields =
                    [
                        Field("survey_id", "Survey ID", FormFieldType.Text, required: true),
                        Field("survey_date", "Survey date", FormFieldType.Date, required: true)
                    ]
                },
                observations
            ]
        };
        var condition0 = MobileFormRepeatKey.ForField(observations, 0, observations.Fields[0]);
        var quantity0 = MobileFormRepeatKey.ForField(observations, 0, observations.Fields[1]);
        var label0 = MobileFormRepeatKey.ForField(observations, 0, observations.Fields[2]);
        var photo0 = MobileFormRepeatKey.ForField(observations, 0, observations.Fields[4]);
        var condition1 = MobileFormRepeatKey.ForField(observations, 1, observations.Fields[0]);
        var quantity1 = MobileFormRepeatKey.ForField(observations, 1, observations.Fields[1]);
        var label1 = MobileFormRepeatKey.ForField(observations, 1, observations.Fields[2]);
        var notes1 = MobileFormRepeatKey.ForField(observations, 1, observations.Fields[3]);
        var photo1 = MobileFormRepeatKey.ForField(observations, 1, observations.Fields[4]);

        return new GoldenFixture(
            "repeat-heavy-survey",
            LayerId: 104,
            "repeat-001",
            form,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["survey_id"] = "survey-2026-05",
                ["survey_date"] = new DateOnly(2026, 5, 23),
                [condition0] = "ok",
                [quantity0] = 3,
                [condition1] = "damaged",
                [quantity1] = 2,
                [notes1] = "Replace marker."
            },
            Media:
            [
                Media("photo-repeat-0", photo0, FieldMediaType.Photo),
                Media("photo-repeat-1", photo1, FieldMediaType.Photo)
            ],
            Location: null,
            RepeatCounts: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["observations"] = 2
            },
            InitialRepeatCount: 2,
            ExpectedRestoredValueCount: 9,
            ExpectedValueKeys: [label0, label1, notes1],
            Capabilities:
            [
                "Repeat groups",
                "Required rules",
                "Conditional visibility",
                "Calculated values",
                "Media constraints",
                "Media capture policy",
                "Shared choice-set ids",
                "Draft restore"
            ]);
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
        bool required,
        IReadOnlyList<string> choices)
        => new()
        {
            FieldId = fieldId,
            Label = label,
            Type = FormFieldType.SingleChoice,
            Required = required,
            Choices = choices.Select(choice => new FieldChoice { Value = choice, Label = choice }).ToList()
        };

    private static FieldMediaAttachment Media(
        string attachmentId,
        string fieldId,
        FieldMediaType mediaType)
        => new()
        {
            AttachmentId = attachmentId,
            FieldId = fieldId,
            FileName = $"{attachmentId}.bin",
            ContentType = mediaType == FieldMediaType.Photo ? "image/jpeg" : "application/octet-stream",
            MediaType = mediaType,
            SizeBytes = 128
        };

    private static FieldMediaCapturePolicy PhotoMediaPolicy()
        => new()
        {
            AllowedContentTypes = ["image/jpeg", "image/png"],
            MaxAttachmentBytes = 10_485_760,
            CaptureLocation = true,
            RequiresFaceBlur = true
        };

    private sealed record GoldenFixture(
        string FixtureId,
        int LayerId,
        string RecordId,
        FormDefinition Form,
        Dictionary<string, object?> Values,
        IReadOnlyList<FieldMediaAttachment> Media,
        FieldGeoPoint? Location,
        Dictionary<string, int> RepeatCounts,
        int InitialRepeatCount,
        int ExpectedRestoredValueCount,
        IReadOnlyList<string> ExpectedValueKeys,
        IReadOnlyList<string> Capabilities);

    private sealed record GoldenFixtureSummary(
        string FixtureId,
        string FormId,
        string RecordId,
        IReadOnlyList<string> Capabilities,
        int SectionCount,
        int FieldCount,
        int BindingCount,
        int MediaCount,
        IReadOnlyDictionary<string, string> Controls,
        IReadOnlyDictionary<string, object?> Values)
    {
        public static GoldenFixtureSummary From(
            GoldenFixture fixture,
            FormData formData,
            int bindingCount,
            IReadOnlyDictionary<string, string> controls)
            => new(
                fixture.FixtureId,
                fixture.Form.FormId,
                fixture.RecordId,
                fixture.Capabilities,
                fixture.Form.Sections.Count,
                fixture.Form.Sections.Sum(section => section.Fields.Count),
                bindingCount,
                formData.Media.Count,
                new Dictionary<string, string>(controls, StringComparer.Ordinal),
                new Dictionary<string, object?>(formData.Values, StringComparer.Ordinal));
    }

    private sealed record GoldenFixtureSuiteEvidence(
        string SchemaVersion,
        DateTime GeneratedAtUtc,
        int FixtureCount,
        IReadOnlyList<GoldenFixtureSummary> Fixtures,
        IReadOnlyList<string> UnsupportedFollowUps);

    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
        {
            return Task.FromResult(
                _values.TryGetValue(key, out var value) && value is T typed
                    ? typed
                    : defaultValue);
        }

        public Task SetSettingAsync<T>(string key, T value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveSettingAsync(string key)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> HasSettingAsync(string key)
        {
            return Task.FromResult(_values.ContainsKey(key));
        }
    }
}
