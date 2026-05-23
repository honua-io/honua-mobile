using System.Globalization;
using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Services.Forms;

public enum MobileFormControlKind
{
    SingleLineText,
    MultilineText,
    Numeric,
    Date,
    DateTime,
    YesNo,
    SingleChoice,
    Dropdown,
    MultipleChoice,
    Location,
    Photo,
    File,
    Signature,
    Barcode,
    Unsupported
}

public static class MobileFormControlSelector
{
    public static MobileFormControlKind Select(FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Type switch
        {
            FormFieldType.Text => IsLikelyMultiline(field)
                ? MobileFormControlKind.MultilineText
                : MobileFormControlKind.SingleLineText,
            FormFieldType.Numeric => MobileFormControlKind.Numeric,
            FormFieldType.Date => MobileFormControlKind.Date,
            FormFieldType.DateTime => MobileFormControlKind.DateTime,
            FormFieldType.YesNo => MobileFormControlKind.YesNo,
            FormFieldType.SingleChoice => field.Choices.Count > 5
                ? MobileFormControlKind.Dropdown
                : MobileFormControlKind.SingleChoice,
            FormFieldType.MultipleChoice => MobileFormControlKind.MultipleChoice,
            FormFieldType.Location => MobileFormControlKind.Location,
            FormFieldType.Photo => MobileFormControlKind.Photo,
            FormFieldType.File => MobileFormControlKind.File,
            FormFieldType.Signature => MobileFormControlKind.Signature,
            FormFieldType.Barcode => MobileFormControlKind.Barcode,
            FormFieldType.Hyperlink or
            FormFieldType.Address or
            FormFieldType.Classification or
            FormFieldType.RecordLink or
            FormFieldType.Calculated => MobileFormControlKind.SingleLineText,
            _ => MobileFormControlKind.Unsupported
        };
    }

    private static bool IsLikelyMultiline(FormField field)
    {
        if (field.Validation?.MaxLength > 255)
        {
            return true;
        }

        var name = $"{field.FieldId} {field.Label}".ToLowerInvariant();
        return name.Contains("note", StringComparison.Ordinal) ||
            name.Contains("comment", StringComparison.Ordinal) ||
            name.Contains("description", StringComparison.Ordinal);
    }
}

public static class MobileFormValueConverter
{
    public static object? NormalizeValue(FormField field, object? value)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (IsBlank(value))
        {
            return null;
        }

        return field.Type switch
        {
            FormFieldType.Numeric => NormalizeNumeric(value),
            FormFieldType.Date => NormalizeDate(value),
            FormFieldType.DateTime => NormalizeDateTime(value),
            FormFieldType.Time => NormalizeTime(value),
            FormFieldType.YesNo => NormalizeBoolean(value),
            FormFieldType.SingleChoice => ToScalarText(value),
            FormFieldType.MultipleChoice => ToChoiceValues(value),
            FormFieldType.Location => NormalizeLocation(value),
            FormFieldType.Photo or
            FormFieldType.File or
            FormFieldType.Signature or
            FormFieldType.Video or
            FormFieldType.Audio or
            FormFieldType.Sketch => ToChoiceValues(value),
            _ => ToScalarText(value)
        };
    }

    public static object? FromText(FormField field, string? text)
        => NormalizeValue(field, text);

    public static object? FromBoolean(FormField field, bool value)
        => field.Type == FormFieldType.YesNo ? value : NormalizeValue(field, value);

    public static object? FromDate(FormField field, DateTime date)
    {
        if (field.Type == FormFieldType.Date)
        {
            return DateOnly.FromDateTime(date);
        }

        return NormalizeValue(field, date);
    }

    public static object? FromDateTime(FormField field, DateTime date, TimeSpan time)
    {
        var value = date.Date.Add(time);
        return field.Type == FormFieldType.DateTime
            ? value
            : NormalizeValue(field, value);
    }

    public static object? FromChoiceValues(FormField field, IEnumerable<string> values)
    {
        var selected = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return field.Type == FormFieldType.MultipleChoice ? selected : selected.FirstOrDefault();
    }

    public static object FromLocation(double latitude, double longitude, double? accuracyMeters = null)
        => new FieldGeoPoint(latitude, longitude, accuracyMeters);

    public static string ToDisplayText(FormField field, object? value)
    {
        var normalized = NormalizeValue(field, value);
        return normalized switch
        {
            null => string.Empty,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("u", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("u", CultureInfo.InvariantCulture),
            TimeSpan time => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            FieldGeoPoint point => FormatLocation(point),
            string[] values => string.Join(", ", values),
            IEnumerable<string> values => string.Join(", ", values),
            bool boolean => boolean ? "Yes" : "No",
            _ => Convert.ToString(normalized, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    public static IReadOnlyList<string> ToChoiceValues(object? value)
    {
        if (IsBlank(value))
        {
            return [];
        }

        if (value is JsonElement json)
        {
            return JsonToStrings(json);
        }

        if (value is string text)
        {
            return text
                .Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
        }

        if (value is IEnumerable<string> strings)
        {
            return strings.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            return enumerable
                .Cast<object?>()
                .Select(ToScalarText)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
        }

        var scalar = ToScalarText(value);
        return string.IsNullOrWhiteSpace(scalar) ? [] : [scalar];
    }

    public static bool TryGetBoolean(object? value, out bool boolean)
    {
        var normalized = NormalizeBoolean(value);
        if (normalized is bool typed)
        {
            boolean = typed;
            return true;
        }

        boolean = false;
        return false;
    }

    public static bool TryGetDate(object? value, out DateTime date)
    {
        var normalized = NormalizeDate(value);
        if (normalized is DateOnly dateOnly)
        {
            date = dateOnly.ToDateTime(TimeOnly.MinValue);
            return true;
        }

        if (normalized is DateTime dateTime)
        {
            date = dateTime.Date;
            return true;
        }

        date = DateTime.Today;
        return false;
    }

    public static bool TryGetDateTime(object? value, out DateTime dateTime)
    {
        var normalized = NormalizeDateTime(value);
        if (normalized is DateTime typed)
        {
            dateTime = typed;
            return true;
        }

        if (normalized is DateTimeOffset offset)
        {
            dateTime = offset.DateTime;
            return true;
        }

        dateTime = DateTime.Now;
        return false;
    }

    public static bool TryGetLocation(object? value, out FieldGeoPoint point)
    {
        var normalized = NormalizeLocation(value);
        if (normalized is FieldGeoPoint typed)
        {
            point = typed;
            return true;
        }

        point = default!;
        return false;
    }

    public static IReadOnlyList<FieldMediaAttachment> BuildMediaAttachments(
        IEnumerable<FormField> fields,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, AttachmentInfo> attachmentsById)
    {
        var media = new List<FieldMediaAttachment>();
        foreach (var field in fields.Where(IsMediaField))
        {
            if (!values.TryGetValue(field.FieldId, out var rawValue))
            {
                continue;
            }

            foreach (var attachmentId in ToChoiceValues(rawValue))
            {
                if (!attachmentsById.TryGetValue(attachmentId, out var attachment))
                {
                    continue;
                }

                media.Add(new FieldMediaAttachment
                {
                    AttachmentId = attachment.Id,
                    FieldId = field.FieldId,
                    FileName = attachment.FileName,
                    ContentType = attachment.ContentType,
                    SizeBytes = attachment.SizeBytes,
                    CapturedAtUtc = new DateTimeOffset(attachment.CreatedAt == default ? DateTime.UtcNow : attachment.CreatedAt),
                    CaptureLocation = attachment.CaptureLocation?.ToFieldGeoPoint(),
                    MediaType = ToSdkMediaType(field.Type, attachment.PayloadKind)
                });
            }
        }

        return media;
    }

    public static bool IsMediaField(FormField field)
        => field.Type is FormFieldType.Photo or
            FormFieldType.Video or
            FormFieldType.Audio or
            FormFieldType.Signature or
            FormFieldType.Sketch or
            FormFieldType.File;

    public static FieldMediaType ToSdkMediaType(FormFieldType fieldType, AttachmentPayloadKind payloadKind)
    {
        return fieldType switch
        {
            FormFieldType.Photo => FieldMediaType.Photo,
            FormFieldType.Video => FieldMediaType.Video,
            FormFieldType.Audio => FieldMediaType.Audio,
            FormFieldType.Signature => FieldMediaType.Signature,
            FormFieldType.Sketch => FieldMediaType.Sketch,
            _ => payloadKind switch
            {
                AttachmentPayloadKind.Photo => FieldMediaType.Photo,
                AttachmentPayloadKind.Signature => FieldMediaType.Signature,
                _ => FieldMediaType.File
            }
        };
    }

    private static object? NormalizeNumeric(object? value)
    {
        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Number)
            {
                if (json.TryGetInt64(out var integer))
                {
                    return integer;
                }

                if (json.TryGetDouble(out var number))
                {
                    return number;
                }
            }

            if (json.ValueKind == JsonValueKind.String)
            {
                value = json.GetString();
            }
        }

        if (value is byte or short or int or long or float or double or decimal)
        {
            return value;
        }

        var text = ToScalarText(value);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
        {
            return integerValue;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return decimalValue;
        }

        return text;
    }

    private static object? NormalizeDate(object? value)
    {
        if (value is DateOnly or DateTime)
        {
            return value;
        }

        if (value is DateTimeOffset offset)
        {
            return DateOnly.FromDateTime(offset.DateTime);
        }

        var text = ToJsonAwareText(value);
        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return dateOnly;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }

        return text;
    }

    private static object? NormalizeDateTime(object? value)
    {
        if (value is DateTime or DateTimeOffset)
        {
            return value;
        }

        var text = ToJsonAwareText(value);
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
        {
            return dateTime;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var offset))
        {
            return offset;
        }

        return text;
    }

    private static object? NormalizeTime(object? value)
    {
        if (value is TimeSpan)
        {
            return value;
        }

        var text = ToJsonAwareText(value);
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var time)
            ? time
            : text;
    }

    private static object? NormalizeBoolean(object? value)
    {
        if (value is JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when json.TryGetInt32(out var number) => number != 0,
                JsonValueKind.String => NormalizeBoolean(json.GetString()),
                _ => null
            };
        }

        if (value is bool)
        {
            return value;
        }

        var text = ToScalarText(value);
        if (bool.TryParse(text, out var boolean))
        {
            return boolean;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer != 0;
        }

        return text?.Trim().ToLowerInvariant() switch
        {
            "yes" or "y" => true,
            "no" or "n" => false,
            _ => null
        };
    }

    private static object? NormalizeLocation(object? value)
    {
        if (value is FieldGeoPoint)
        {
            return value;
        }

        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object &&
                TryReadDouble(json, "latitude", out var latitude) &&
                TryReadDouble(json, "longitude", out var longitude))
            {
                return new FieldGeoPoint(
                    latitude,
                    longitude,
                    TryReadDouble(json, "accuracyMeters", out var accuracy) ? accuracy : null);
            }

            if (json.ValueKind == JsonValueKind.String)
            {
                value = json.GetString();
            }
        }

        var text = ToScalarText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split([','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLatitude) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLongitude))
        {
            double? accuracy = null;
            if (parts.Length >= 3 &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedAccuracy))
            {
                accuracy = parsedAccuracy;
            }

            return new FieldGeoPoint(parsedLatitude, parsedLongitude, accuracy);
        }

        return text;
    }

    private static bool TryReadDouble(JsonElement json, string propertyName, out double value)
    {
        if (json.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static IReadOnlyList<string> JsonToStrings(JsonElement json)
    {
        return json.ValueKind switch
        {
            JsonValueKind.Array => json.EnumerateArray()
                .Select(item => ToJsonAwareText(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray()!,
            JsonValueKind.String => ToChoiceValues(json.GetString()),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => [json.GetRawText()],
            _ => []
        };
    }

    private static string? ToScalarText(object? value)
    {
        if (value is JsonElement json)
        {
            return ToJsonAwareText(json);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string? ToJsonAwareText(object? value)
    {
        if (value is not JsonElement json)
        {
            return ToScalarText(value);
        }

        return json.ValueKind switch
        {
            JsonValueKind.String => json.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => json.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => json.GetRawText()
        };
    }

    private static bool IsBlank(object? value)
    {
        return value is null ||
            value is string text && string.IsNullOrWhiteSpace(text) ||
            value is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined };
    }

    private static string FormatLocation(FieldGeoPoint point)
    {
        var accuracy = point.AccuracyMeters.HasValue
            ? $", +/- {point.AccuracyMeters.Value:F0} m"
            : string.Empty;
        return $"{point.Latitude:F6}, {point.Longitude:F6}{accuracy}";
    }
}

public sealed record MobileFormFieldBinding(
    FormSection Section,
    FormField Field,
    string ValueKey,
    int? RepeatIndex);

public static class MobileFormRepeatKey
{
    public static string ForField(FormSection section, int repeatIndex, FormField field)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(field);

        if (repeatIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(repeatIndex), repeatIndex, "Repeat index must be non-negative.");
        }

        return $"{section.SectionId}[{repeatIndex}].{field.FieldId}";
    }

    public static bool TryParse(string? key, out string sectionId, out int repeatIndex, out string fieldId)
    {
        sectionId = string.Empty;
        repeatIndex = -1;
        fieldId = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var open = key.IndexOf('[', StringComparison.Ordinal);
        var close = key.IndexOf(']', StringComparison.Ordinal);
        if (open <= 0 || close <= open + 1 || close + 1 >= key.Length || key[close + 1] != '.')
        {
            return false;
        }

        if (!int.TryParse(key.AsSpan(open + 1, close - open - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out repeatIndex) ||
            repeatIndex < 0)
        {
            return false;
        }

        sectionId = key[..open];
        fieldId = key[(close + 2)..];
        return !string.IsNullOrWhiteSpace(sectionId) && !string.IsNullOrWhiteSpace(fieldId);
    }

    public static IReadOnlyList<int> GetRepeatIndices(
        FormSection section,
        IReadOnlyDictionary<string, object?> values,
        int defaultCount = 0)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(values);

        var fieldIds = section.Fields
            .Select(field => field.FieldId)
            .Where(fieldId => !string.IsNullOrWhiteSpace(fieldId))
            .ToHashSet(StringComparer.Ordinal);
        var indices = values.Keys
            .Select(key => TryParse(key, out var sectionId, out var repeatIndex, out var fieldId) &&
                string.Equals(sectionId, section.SectionId, StringComparison.Ordinal) &&
                fieldIds.Contains(fieldId)
                    ? repeatIndex
                    : -1)
            .Where(index => index >= 0)
            .Distinct()
            .Order()
            .ToList();

        if (indices.Count > 0 || defaultCount <= 0)
        {
            return indices;
        }

        return Enumerable.Range(0, defaultCount).ToArray();
    }

    public static int GetRepeatCount(
        FormSection section,
        IReadOnlyDictionary<string, object?> values,
        int defaultCount = 0)
    {
        var indices = GetRepeatIndices(section, values, defaultCount);
        return indices.Count == 0 ? 0 : indices.Max() + 1;
    }
}

public static class MobileFormRuleRuntime
{
    public static IReadOnlyList<MobileFormFieldBinding> BuildFieldBindings(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values,
        int initialRepeatCount = 1)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        var bindings = new List<MobileFormFieldBinding>();
        foreach (var section in definition.Sections)
        {
            if (!section.Repeatable)
            {
                bindings.AddRange(section.Fields.Select(field =>
                    new MobileFormFieldBinding(section, field, field.FieldId, null)));
                continue;
            }

            foreach (var repeatIndex in MobileFormRepeatKey.GetRepeatIndices(section, values, initialRepeatCount))
            {
                bindings.AddRange(section.Fields.Select(field =>
                    new MobileFormFieldBinding(section, field, MobileFormRepeatKey.ForField(section, repeatIndex, field), repeatIndex)));
            }
        }

        return bindings;
    }

    public static Dictionary<string, object?> ApplyDefaultValues(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values,
        int initialRepeatCount = 1)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        var result = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var binding in BuildFieldBindings(definition, result, initialRepeatCount))
        {
            if (result.TryGetValue(binding.ValueKey, out var existing) && !IsBlank(existing))
            {
                continue;
            }

            if (TryGetDefaultValue(definition, binding.Section, binding.Field, out var defaultValue))
            {
                result[binding.ValueKey] = MobileFormValueConverter.NormalizeValue(binding.Field, defaultValue);
            }
        }

        return result;
    }

    public static Dictionary<string, object?> ApplyCalculatedValues(
        FormDefinition definition,
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        var result = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        ApplyNonRepeatCalculations(definition, result);
        ApplyRepeatCalculations(definition, result);
        return result;
    }

    public static bool IsFieldVisible(
        FormDefinition definition,
        FormSection section,
        FormField field,
        IReadOnlyDictionary<string, object?> values,
        int? repeatIndex = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(values);

        if (field.VisibilityRule == null)
        {
            return true;
        }

        var probeField = new FormField
        {
            FieldId = field.FieldId,
            Label = field.Label,
            Type = field.Type,
            SourceFieldName = field.SourceFieldName,
            Required = true,
            Choices = field.Choices.ToList(),
            Validation = field.Validation ?? new FieldValidationRule(),
            VisibilityRule = field.VisibilityRule,
            CalculatedExpression = field.CalculatedExpression,
            HelpText = field.HelpText
        };

        var probeDefinition = new FormDefinition
        {
            FormId = definition.FormId,
            Name = definition.Name,
            Sections =
            [
                new FormSection
                {
                    SectionId = section.SectionId,
                    Label = section.Label,
                    Fields = [probeField]
                }
            ]
        };
        var recordValues = BuildRecordValuesForBinding(section, values, repeatIndex);
        recordValues[field.FieldId] = null;
        var result = FormValidator.Validate(probeDefinition, new FieldRecord
        {
            RecordId = "visibility-probe",
            FormId = definition.FormId,
            Values = recordValues
        });

        return result.Errors.Any(error => string.Equals(error.FieldId, field.FieldId, StringComparison.Ordinal));
    }

    public static FormField CloneField(FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return new FormField
        {
            FieldId = field.FieldId,
            Label = field.Label,
            Type = field.Type,
            SourceFieldName = field.SourceFieldName,
            Required = field.Required,
            Choices = field.Choices.ToList(),
            Validation = field.Validation ?? new FieldValidationRule(),
            VisibilityRule = field.VisibilityRule,
            CalculatedExpression = field.CalculatedExpression,
            HelpText = field.HelpText
        };
    }

    public static FormSection CloneSection(FormSection section, bool repeatable)
    {
        ArgumentNullException.ThrowIfNull(section);

        return new FormSection
        {
            SectionId = section.SectionId,
            Label = section.Label,
            Description = section.Description,
            Repeatable = repeatable,
            Collapsible = section.Collapsible,
            InitiallyCollapsed = section.InitiallyCollapsed,
            Fields = section.Fields.Select(CloneField).ToList()
        };
    }

    public static Dictionary<string, object?> BuildRecordValuesForBinding(
        FormSection section,
        IReadOnlyDictionary<string, object?> values,
        int? repeatIndex)
    {
        var recordValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (MobileFormRepeatKey.TryParse(value.Key, out var repeatSectionId, out var parsedRepeatIndex, out var fieldId))
            {
                if (repeatIndex.HasValue &&
                    parsedRepeatIndex == repeatIndex.Value &&
                    string.Equals(repeatSectionId, section.SectionId, StringComparison.Ordinal))
                {
                    recordValues[fieldId] = value.Value;
                }

                continue;
            }

            recordValues[value.Key] = value.Value;
        }

        return recordValues;
    }

    private static void ApplyNonRepeatCalculations(FormDefinition definition, Dictionary<string, object?> values)
    {
        var sections = definition.Sections
            .Where(section => !section.Repeatable)
            .Select(section => CloneSection(section, repeatable: false))
            .ToList();
        if (sections.Count == 0)
        {
            return;
        }

        var form = CloneDefinition(definition, sections);
        var record = new FieldRecord
        {
            RecordId = "calculation",
            FormId = definition.FormId,
            Values = values
                .Where(value => !MobileFormRepeatKey.TryParse(value.Key, out _, out _, out _))
                .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase)
        };
        CalculatedFieldEvaluator.ApplyCalculatedFields(form, record);

        foreach (var field in sections.SelectMany(section => section.Fields).Where(IsCalculatedField))
        {
            if (record.Values.TryGetValue(field.FieldId, out var calculated))
            {
                values[field.FieldId] = calculated;
            }
        }
    }

    private static void ApplyRepeatCalculations(FormDefinition definition, Dictionary<string, object?> values)
    {
        foreach (var section in definition.Sections.Where(section => section.Repeatable))
        {
            var repeatForm = CloneDefinition(definition, [CloneSection(section, repeatable: false)]);
            foreach (var repeatIndex in MobileFormRepeatKey.GetRepeatIndices(section, values))
            {
                var record = new FieldRecord
                {
                    RecordId = $"calculation:{section.SectionId}:{repeatIndex}",
                    FormId = definition.FormId,
                    Values = BuildRecordValuesForBinding(section, values, repeatIndex)
                };
                CalculatedFieldEvaluator.ApplyCalculatedFields(repeatForm, record);

                foreach (var field in section.Fields.Where(IsCalculatedField))
                {
                    if (record.Values.TryGetValue(field.FieldId, out var calculated))
                    {
                        values[MobileFormRepeatKey.ForField(section, repeatIndex, field)] = calculated;
                    }
                }
            }
        }
    }

    private static FormDefinition CloneDefinition(FormDefinition definition, List<FormSection> sections)
    {
        return new FormDefinition
        {
            FormId = definition.FormId,
            Name = definition.Name,
            Version = definition.Version,
            Description = definition.Description,
            Target = definition.Target,
            Sections = sections,
            Metadata = new Dictionary<string, string>(definition.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool IsCalculatedField(FormField field)
        => field.Type == FormFieldType.Calculated || !string.IsNullOrWhiteSpace(field.CalculatedExpression);

    private static bool TryGetDefaultValue(
        FormDefinition definition,
        FormSection section,
        FormField field,
        out string defaultValue)
    {
        var keys = new[]
        {
            $"default:{section.SectionId}.{field.FieldId}",
            $"default:{field.FieldId}",
            $"defaults.{section.SectionId}.{field.FieldId}",
            $"defaults.{field.FieldId}"
        };

        foreach (var key in keys)
        {
            if (definition.Metadata.TryGetValue(key, out defaultValue!) &&
                !string.IsNullOrWhiteSpace(defaultValue))
            {
                return true;
            }
        }

        defaultValue = string.Empty;
        return false;
    }

    private static bool IsBlank(object? value)
    {
        return value is null ||
            value is string text && string.IsNullOrWhiteSpace(text) ||
            value is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined };
    }
}

public sealed class FormDraftSnapshot
{
    public int LayerId { get; set; }
    public string FeatureId { get; set; } = string.Empty;
    public string? FormId { get; set; }
    public Dictionary<string, object?> Values { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ValidationErrors { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> RepeatCounts { get; set; } = new(StringComparer.Ordinal);
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public interface IFormDraftService
{
    Task<FormDraftSnapshot?> GetDraftAsync(int layerId, string featureId, CancellationToken cancellationToken = default);
    Task SaveDraftAsync(FormDraftSnapshot draft, CancellationToken cancellationToken = default);
    Task DeleteDraftAsync(int layerId, string featureId, CancellationToken cancellationToken = default);
}

public sealed class SettingsFormDraftService : IFormDraftService
{
    private readonly ISettingsService _settingsService;

    public SettingsFormDraftService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public Task<FormDraftSnapshot?> GetDraftAsync(
        int layerId,
        string featureId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _settingsService.GetSettingAsync<FormDraftSnapshot?>(
            BuildKey(layerId, featureId),
            default);
    }

    public Task SaveDraftAsync(FormDraftSnapshot draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        draft.UpdatedAtUtc = DateTime.UtcNow;
        return _settingsService.SetSettingAsync(BuildKey(draft.LayerId, draft.FeatureId), draft);
    }

    public Task DeleteDraftAsync(
        int layerId,
        string featureId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _settingsService.RemoveSettingAsync(BuildKey(layerId, featureId));
    }

    private static string BuildKey(int layerId, string featureId)
        => $"field-collection:draft:{layerId}:{featureId}";
}
