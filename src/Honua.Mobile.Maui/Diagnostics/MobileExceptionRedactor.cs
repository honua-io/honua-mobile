using System.Globalization;
using System.Text.RegularExpressions;

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Applies mobile privacy redaction before exception details are stored or uploaded.
/// </summary>
public static partial class MobileExceptionRedactor
{
    public const string RedactedValue = "[redacted]";
    public const string PreciseLocationRedactedValue = "[redacted: precise location disabled]";
    public const string FormPayloadRedactedValue = "[redacted: form payload disabled]";
    public const string AttachmentContentRedactedValue = "[redacted: attachment content disabled]";

    public static string? RedactText(string? value, MobileExceptionReportingOptions options, int? maxLength = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = UrlUserInfoRegex().Replace(value, "${scheme}[redacted]@");
        redacted = AuthorizationHeaderRegex().Replace(redacted, "${prefix}${scheme} [redacted]");
        redacted = BearerOrBasicRegex().Replace(redacted, "${scheme} [redacted]");
        redacted = SensitiveKeyValueRegex().Replace(redacted, "${key}=${quote}[redacted]${quote}");
        redacted = SensitiveQueryStringRegex().Replace(redacted, "${prefix}[redacted]");

        if (!options.IncludePreciseLocation)
        {
            redacted = PreciseLocationTextRegex().Replace(redacted, "${key}=[redacted]");
        }

        return Truncate(redacted, maxLength);
    }

    public static IReadOnlyDictionary<string, string?> RedactProperties(
        IReadOnlyDictionary<string, object?>? properties,
        MobileExceptionReportingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (properties is null || properties.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        var redacted = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (LooksLikeSensitiveKey(key))
            {
                redacted[key] = RedactedValue;
                continue;
            }

            if (!options.IncludePreciseLocation && LooksLikePreciseLocationKey(key))
            {
                redacted[key] = PreciseLocationRedactedValue;
                continue;
            }

            if (!options.IncludeFormPayloads && LooksLikeFormPayloadKey(key))
            {
                redacted[key] = FormPayloadRedactedValue;
                continue;
            }

            if (!options.IncludeAttachmentContent && LooksLikeAttachmentContentKey(key))
            {
                redacted[key] = AttachmentContentRedactedValue;
                continue;
            }

            redacted[key] = RedactText(ConvertToString(value), options, options.MaxMessageLength);
        }

        return redacted;
    }

    public static IReadOnlyDictionary<string, string?> RedactMetadata(
        IReadOnlyDictionary<string, string?>? properties,
        MobileExceptionReportingOptions options)
    {
        if (properties is null || properties.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        return RedactProperties(
            properties.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.Ordinal),
            options);
    }

    private static string? ConvertToString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    private static string? Truncate(string? value, int? maxLength)
    {
        if (string.IsNullOrEmpty(value) || maxLength is null || value.Length <= maxLength.Value)
        {
            return value;
        }

        return value[..maxLength.Value];
    }

    private static bool LooksLikeSensitiveKey(string key)
    {
        var normalized = NormalizeKey(key);
        return normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("accesskey", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal) ||
            normalized.Contains("credential", StringComparison.Ordinal) ||
            normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("passwd", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal);
    }

    private static bool LooksLikePreciseLocationKey(string key)
    {
        var normalized = NormalizeKey(key);
        return normalized is "lat" or "latitude" or "lon" or "lng" or "long" or "longitude" ||
            normalized.Contains("gps", StringComparison.Ordinal) ||
            normalized.Contains("coordinate", StringComparison.Ordinal) ||
            normalized.Contains("geolocation", StringComparison.Ordinal) ||
            normalized.Contains("location", StringComparison.Ordinal);
    }

    private static bool LooksLikeFormPayloadKey(string key)
    {
        var normalized = NormalizeKey(key);
        return normalized.Contains("formpayload", StringComparison.Ordinal) ||
            normalized.Contains("formdata", StringComparison.Ordinal) ||
            normalized.Contains("fieldvalues", StringComparison.Ordinal) ||
            normalized.Contains("userinput", StringComparison.Ordinal) ||
            normalized.Contains("surveyresponse", StringComparison.Ordinal) ||
            normalized.Contains("recordattributes", StringComparison.Ordinal);
    }

    private static bool LooksLikeAttachmentContentKey(string key)
    {
        var normalized = NormalizeKey(key);
        return normalized.Contains("attachmentcontent", StringComparison.Ordinal) ||
            normalized.Contains("attachmentbytes", StringComparison.Ordinal) ||
            normalized.Contains("filebytes", StringComparison.Ordinal) ||
            normalized.Contains("imagebytes", StringComparison.Ordinal) ||
            normalized.Contains("photobytes", StringComparison.Ordinal) ||
            normalized.Contains("mediabytes", StringComparison.Ordinal);
    }

    private static string NormalizeKey(string key)
    {
        return new string(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    [GeneratedRegex(@"(?<scheme>https?://)(?<userinfo>[^/?#\s@]+@)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex UrlUserInfoRegex();

    [GeneratedRegex(@"(?<prefix>\bauthorization\s*[:=]\s*)(?<scheme>bearer|basic)?\s*[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"\b(?<scheme>bearer|basic)\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex BearerOrBasicRegex();

    [GeneratedRegex(@"\b(?<key>access[_-]?token|refresh[_-]?token|token|x[_-]?api[_-]?key|api[_-]?key|apikey|access[_-]?key|accesskey|password|passwd|secret|client[_-]?secret|credential)\b\s*[:=]\s*(?<quote>[""']?)[^\s,;&""']+(?<quote2>[""']?)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex SensitiveKeyValueRegex();

    [GeneratedRegex(@"(?<prefix>[?&;](?:access[_-]?token|refresh[_-]?token|token|x[_-]?api[_-]?key|api[_-]?key|apikey|access[_-]?key|accesskey|password|secret|client[_-]?secret|sig|signature|code|key)=)[^&#\s]+", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex SensitiveQueryStringRegex();

    [GeneratedRegex(@"\b(?<key>lat(?:itude)?|lon(?:gitude)?|lng)\s*[:=]\s*-?\d{1,3}(?:\.\d{4,})?\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 250)]
    private static partial Regex PreciseLocationTextRegex();
}
