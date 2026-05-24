using System.Text.Json;
using System.Text.RegularExpressions;

namespace Honua.Mobile.FieldCollection.Services.Diagnostics;

public sealed class OfflineCacheDiagnostics
{
    public string PackageId { get; set; } = string.Empty;
    public string PackageFileName { get; set; } = string.Empty;
    public long PackageSizeBytes { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public long? LocalGeneration { get; set; }
    public long? ServerGeneration { get; set; }
    public MetadataCacheDiagnostics MetadataCache { get; set; } = new();
    public FeatureCacheDiagnostics FeatureCache { get; set; } = new();
    public OfflineOperationDiagnostics Operations { get; set; } = new();
    public IReadOnlyList<OfflineConflictReviewItem> ConflictReview { get; set; } = Array.Empty<OfflineConflictReviewItem>();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string PackageSizeDisplay => FormatBytes(PackageSizeBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}

public sealed class MetadataCacheDiagnostics
{
    public string Status { get; set; } = "Missing";
    public int SourceCount { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
    public IReadOnlyList<OfflineSourceDiagnostics> Sources { get; set; } = Array.Empty<OfflineSourceDiagnostics>();
}

public sealed class FeatureCacheDiagnostics
{
    public string Status { get; set; } = "Empty";
    public int SourceCount { get; set; }
    public int TotalFeatureCount { get; set; }
    public long SizeBytes { get; set; }
    public IReadOnlyList<OfflineSourceDiagnostics> Sources { get; set; } = Array.Empty<OfflineSourceDiagnostics>();
}

public sealed class OfflineSourceDiagnostics
{
    public string SourceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int FeatureCount { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public string? SourceUrl { get; set; }
}

public sealed class OfflineOperationDiagnostics
{
    public int PendingCount { get; set; }
    public int ClaimedCount { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public int RetryCount { get; set; }
    public int ConflictCount { get; set; }
    public int AttachmentPendingCount { get; set; }
    public int AttachmentSucceededCount { get; set; }
    public int AttachmentFailedCount { get; set; }
    public int AttachmentUploadFailedCount { get; set; }
    public int AttachmentDownloadFailedCount { get; set; }
    public int AttachmentDeleteFailedCount { get; set; }
}

public sealed class OfflineConflictReviewItem
{
    public string ConflictId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string FeatureId { get; set; } = string.Empty;
    public string ConflictType { get; set; } = string.Empty;
    public string Status { get; set; } = "Needs review";
    public string Reason { get; set; } = string.Empty;
    public string LocalState { get; set; } = string.Empty;
    public string ServerState { get; set; } = string.Empty;
    public DateTime DetectedAtUtc { get; set; }
    public IReadOnlyList<string> ResolutionActions { get; set; } = Array.Empty<string>();
}

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
            Fragment = string.Empty,
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

    private static bool IsSensitiveName(string name)
    {
        var normalized = string.Concat(name
            .Where(char.IsLetterOrDigit))
            .ToLowerInvariant();

        return normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("accesskey", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal) ||
            normalized.Contains("localpath", StringComparison.Ordinal) ||
            normalized.Contains("rawmedia", StringComparison.Ordinal) ||
            normalized.Contains("mediapayload", StringComparison.Ordinal) ||
            normalized.Contains("photopayload", StringComparison.Ordinal) ||
            normalized.Contains("imagepayload", StringComparison.Ordinal) ||
            normalized.Contains("audiopayload", StringComparison.Ordinal) ||
            normalized.Contains("voicetranscript", StringComparison.Ordinal) ||
            normalized.Contains("biometric", StringComparison.Ordinal) ||
            normalized.Contains("faceembedding", StringComparison.Ordinal);
    }

    [GeneratedRegex("Bearer\\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?i)(x[_-]?api[_-]?key|api[_-]?key|access[_-]?key|token|password|secret|authorization)([\"'\\s:=]+)[^,\"'\\s}]+")]
    private static partial Regex SecretPairRegex();
}
