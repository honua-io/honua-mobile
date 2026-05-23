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

public sealed class FormDraftSnapshot
{
    public int LayerId { get; set; }
    public string FeatureId { get; set; } = string.Empty;
    public string? FormId { get; set; }
    public Dictionary<string, object?> Values { get; set; } = new(StringComparer.Ordinal);
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
