using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Forms;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class MobileFormRuntimeTests
{
    [Fact]
    public void ControlSelector_SelectsPilotFieldTypeControls()
    {
        var cases = new (FormField Field, MobileFormControlKind Expected)[]
        {
            (Field("text", FormFieldType.Text), MobileFormControlKind.SingleLineText),
            (Field("notes", FormFieldType.Text, maxLength: 1024), MobileFormControlKind.MultilineText),
            (Field("number", FormFieldType.Numeric), MobileFormControlKind.Numeric),
            (Field("date", FormFieldType.Date), MobileFormControlKind.Date),
            (Field("datetime", FormFieldType.DateTime), MobileFormControlKind.DateTime),
            (Field("yesno", FormFieldType.YesNo), MobileFormControlKind.YesNo),
            (Field("single", FormFieldType.SingleChoice, choiceCount: 3), MobileFormControlKind.SingleChoice),
            (Field("dropdown", FormFieldType.SingleChoice, choiceCount: 8), MobileFormControlKind.Dropdown),
            (Field("multi", FormFieldType.MultipleChoice, choiceCount: 3), MobileFormControlKind.MultipleChoice),
            (Field("gps", FormFieldType.Location), MobileFormControlKind.Location),
            (Field("photo", FormFieldType.Photo), MobileFormControlKind.Photo),
            (Field("file", FormFieldType.File), MobileFormControlKind.File),
            (Field("signature", FormFieldType.Signature), MobileFormControlKind.Signature),
            (Field("barcode", FormFieldType.Barcode), MobileFormControlKind.Barcode),
        };

        foreach (var (field, expected) in cases)
        {
            Assert.Equal(expected, MobileFormControlSelector.Select(field));
        }
    }

    [Fact]
    public void ValueConverter_NormalizesPilotFieldValues()
    {
        Assert.Equal("inspection", MobileFormValueConverter.NormalizeValue(Field("text", FormFieldType.Text), "inspection"));
        Assert.Equal(42L, MobileFormValueConverter.NormalizeValue(Field("integer", FormFieldType.Numeric), "42"));
        Assert.Equal(3.5m, MobileFormValueConverter.NormalizeValue(Field("decimal", FormFieldType.Numeric), "3.5"));
        Assert.Equal(new DateOnly(2026, 5, 23), MobileFormValueConverter.NormalizeValue(Field("date", FormFieldType.Date), "2026-05-23"));
        Assert.Equal(
            new DateTime(2026, 5, 23, 8, 30, 0, DateTimeKind.Utc),
            MobileFormValueConverter.NormalizeValue(Field("datetime", FormFieldType.DateTime), "2026-05-23T08:30:00Z"));
        Assert.True((bool)MobileFormValueConverter.NormalizeValue(Field("yesno", FormFieldType.YesNo), "yes")!);
        Assert.Equal("open", MobileFormValueConverter.NormalizeValue(Field("single", FormFieldType.SingleChoice), "open"));
        var tags = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            MobileFormValueConverter.NormalizeValue(Field("multi", FormFieldType.MultipleChoice), "urgent, wet"));
        Assert.Equal(["urgent", "wet"], tags);

        var location = Assert.IsType<FieldGeoPoint>(
            MobileFormValueConverter.NormalizeValue(Field("gps", FormFieldType.Location), "21.3,-157.8,5"));
        Assert.Equal(21.3, location.Latitude);
        Assert.Equal(-157.8, location.Longitude);
        Assert.Equal(5, location.AccuracyMeters);

        var photo = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            MobileFormValueConverter.NormalizeValue(Field("photo", FormFieldType.Photo), "photo-1"));
        Assert.Equal(["photo-1"], photo);
        var file = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            MobileFormValueConverter.NormalizeValue(Field("file", FormFieldType.File), "file-1"));
        Assert.Equal(["file-1"], file);
        var signature = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            MobileFormValueConverter.NormalizeValue(Field("signature", FormFieldType.Signature), "signature-1"));
        Assert.Equal(["signature-1"], signature);
        Assert.Equal("QR-123", MobileFormValueConverter.NormalizeValue(Field("barcode", FormFieldType.Barcode), "QR-123"));
    }

    [Fact]
    public async Task FormData_ToSdkRecord_IncludesMediaAndLocationForSdkValidation()
    {
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
                        Field("gps", FormFieldType.Location, required: true),
                        new FormField
                        {
                            FieldId = "photos",
                            Label = "Photos",
                            Type = FormFieldType.Photo,
                            Required = true,
                            Validation = new FieldValidationRule { MinMediaCount = 1 },
                        },
                    ],
                },
            ],
        };
        var formData = new FormData
        {
            LayerId = 7,
            FeatureId = "asset-1",
            Values =
            {
                ["gps"] = new FieldGeoPoint(21.3, -157.8, 4),
                ["photos"] = new[] { "photo-1" },
            },
            Location = new FieldGeoPoint(21.3, -157.8, 4),
            Media =
            [
                new FieldMediaAttachment
                {
                    AttachmentId = "photo-1",
                    FieldId = "photos",
                    FileName = "site.jpg",
                    MediaType = FieldMediaType.Photo,
                },
            ],
        };

        var valid = await new FormService().ValidateFormAsync(formData, form);

        Assert.True(valid);
        Assert.Empty(formData.ValidationErrors);
        var record = formData.ToSdkFieldRecord(form);
        Assert.Single(record.Media);
        Assert.Equal(21.3, record.Location?.Latitude);
    }

    [Fact]
    public async Task DraftService_PreservesTypedValuesAcrossServiceRestart()
    {
        var settings = new InMemorySettingsService();
        var service = new SettingsFormDraftService(settings);
        await service.SaveDraftAsync(new FormDraftSnapshot
        {
            LayerId = 4,
            FeatureId = "draft-1",
            FormId = "inspection",
            Values =
            {
                ["name"] = "Pump Station",
                ["score"] = 3.5m,
                ["tags"] = new[] { "urgent", "wet" },
                ["captured_at"] = new DateTime(2026, 5, 23, 8, 30, 0, DateTimeKind.Utc),
                ["gps"] = new FieldGeoPoint(21.3, -157.8, 4),
            },
        });

        var restarted = new SettingsFormDraftService(settings);
        var loaded = await restarted.GetDraftAsync(4, "draft-1");

        Assert.NotNull(loaded);
        Assert.Equal("Pump Station", MobileFormValueConverter.NormalizeValue(Field("name", FormFieldType.Text), loaded.Values["name"]));
        Assert.Equal(3.5m, MobileFormValueConverter.NormalizeValue(Field("score", FormFieldType.Numeric), loaded.Values["score"]));
        var loadedTags = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            MobileFormValueConverter.NormalizeValue(Field("tags", FormFieldType.MultipleChoice), loaded.Values["tags"]));
        Assert.Equal(["urgent", "wet"], loadedTags);
        Assert.True(MobileFormValueConverter.TryGetDateTime(loaded.Values["captured_at"], out _));
        Assert.True(MobileFormValueConverter.TryGetLocation(loaded.Values["gps"], out _));
    }

    private static FormField Field(
        string fieldId,
        FormFieldType type,
        bool required = false,
        int? maxLength = null,
        int choiceCount = 0)
    {
        return new FormField
        {
            FieldId = fieldId,
            Label = fieldId,
            Type = type,
            Required = required,
            Validation = maxLength.HasValue
                ? new FieldValidationRule { MaxLength = maxLength.Value }
                : new FieldValidationRule(),
            Choices = Enumerable.Range(1, choiceCount)
                .Select(index => new FieldChoice { Value = $"choice-{index}", Label = $"Choice {index}" })
                .ToList(),
        };
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
        {
            return Task.FromResult(_values.TryGetValue(key, out var value) && value is T typed
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
