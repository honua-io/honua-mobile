using System.Net;
using System.Text.Json;

namespace Honua.Mobile.Sdk.Auth;

internal static class HonuaAuthProblemDetails
{
    private const int MaxProblemTextLength = 512;

    public static HonuaMobileAuthException CreateRefreshException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string? responseBody)
    {
        var problem = TryReadProblemSummary(responseBody);
        var statusText = string.IsNullOrWhiteSpace(reasonPhrase)
            ? $"status {(int)statusCode}"
            : $"status {(int)statusCode} {reasonPhrase}";
        var message = problem is null
            ? $"Honua auth token refresh failed with {statusText}."
            : $"Honua auth token refresh failed with {statusText}: {problem}";

        return new HonuaMobileAuthException(message, statusCode);
    }

    private static string? TryReadProblemSummary(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var title = ReadString(root, "title");
            var detail = ReadString(root, "detail");

            if (string.IsNullOrWhiteSpace(title))
            {
                return TrimProblemText(detail);
            }

            if (string.IsNullOrWhiteSpace(detail) ||
                string.Equals(title, detail, StringComparison.OrdinalIgnoreCase))
            {
                return TrimProblemText(title);
            }

            return TrimProblemText($"{title}: {detail}");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? TrimProblemText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxProblemTextLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, MaxProblemTextLength), "...");
    }
}
