using System.Text.Json;
using System.Text.RegularExpressions;

namespace Honua.Mobile.FieldCollection.Services.Diagnostics;

public static partial class DiagnosticRedactor
{
    private const string Redacted = "[redacted]";

    public static string RedactPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFileName(path);
    }

    public static string? RedactUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return RedactSensitiveText(value);
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Path);
    }

    public static string RedactSensitiveText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var redacted = BearerTokenRegex().Replace(value, $"Bearer {Redacted}");
        redacted = SecretPairRegex().Replace(redacted, match => $"{match.Groups[1].Value}{Redacted}");
        return redacted;
    }

    public static string RedactJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var redacted = RedactElement(document.RootElement);
            return JsonSerializer.Serialize(redacted, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return RedactSensitiveText(json);
        }
    }

    public static bool IsSensitiveName(string name)
    {
        var normalized = string.Concat(name
            .Where(char.IsLetterOrDigit))
            .ToLowerInvariant();

        return normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("accesskey", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal);
    }

    private static object? RedactElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => RedactObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(RedactElement).ToArray(),
            JsonValueKind.String => RedactSensitiveText(element.GetString()),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Dictionary<string, object?> RedactObject(JsonElement element)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = IsSensitiveName(property.Name)
                ? Redacted
                : RedactElement(property.Value);
        }

        return values;
    }

    [GeneratedRegex("Bearer\\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?i)(x[_-]?api[_-]?key|api[_-]?key|access[_-]?key|token|password|secret|authorization)([\"'\\s:=]+)[^,\"'\\s}]+")]
    private static partial Regex SecretPairRegex();
}
