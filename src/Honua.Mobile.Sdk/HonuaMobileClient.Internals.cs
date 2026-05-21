using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Honua.Mobile.Sdk.Auth;

namespace Honua.Mobile.Sdk;

public sealed partial class HonuaMobileClient
{
    internal async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        IReadOnlyDictionary<string, string?>? query,
        HttpContent? content,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BuildAbsoluteUri(relativePath, query));
        request.Content = content;
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(_userAgent);
        await ApplyHttpAuthenticationAsync(request, ct).ConfigureAwait(false);

        // Apply per-request timeout via a linked cancellation token so the caller's
        // HttpClient (potentially owned by IHttpClientFactory) is not mutated.
        using var timeoutCts = new CancellationTokenSource(_requestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TaskCanceledException("Honua mobile request timed out.");
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                var raw = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                throw new HonuaMobileApiException(
                    response.StatusCode,
                    $"Honua mobile request failed with status {(int)response.StatusCode} {response.ReasonPhrase}",
                    raw);
            }

            // 204 No Content (and other success-without-body responses) carry no JSON
            // payload — surface them as an empty object so callers can pattern-match
            // on JsonValueKind.Object without parsing failures.
            if (response.StatusCode == HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
            {
                return JsonDocument.Parse("{}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                return await JsonDocument.ParseAsync(stream, default, linkedCts.Token).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                // Preserve the prior contract: whitespace-only / missing bodies parse as
                // an empty object (some servers omit Content-Length on empty payloads),
                // anything else surfaces as a HonuaMobileApiException with the invalid payload.
                if (ex.LineNumber == 0 && ex.BytePositionInLine == 0)
                {
                    return JsonDocument.Parse("{}");
                }

                throw new HonuaMobileApiException("Honua mobile request returned invalid JSON.", ex);
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    private async ValueTask ApplyHttpAuthenticationAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var apiKey = _options.ApiKey;
        var token = _options.BearerToken;
        var providerToken = await ResolveProviderTokenAsync(ct).ConfigureAwait(false);
        if (providerToken is { Scheme: HonuaAuthScheme.ApiKey })
        {
            apiKey = providerToken.AccessToken;
        }
        else if (providerToken is { Scheme: HonuaAuthScheme.Bearer })
        {
            token = providerToken.AccessToken;
        }
        else
        {
            token = await ResolveBearerTokenAsync(ct).ConfigureAwait(false);
        }

        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
        var hasBearerToken = !string.IsNullOrWhiteSpace(token);

        if (hasApiKey || hasBearerToken)
        {
            EnsureSecureTransport(ResolveAbsoluteRequestUri(request));
        }

        if (hasApiKey)
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async ValueTask<string?> ResolveBearerTokenAsync(CancellationToken ct)
    {
        var token = _options.BearerToken;
        if (_options.AccessTokenProvider is not null)
        {
            token = await _options.AccessTokenProvider(ct).ConfigureAwait(false) ?? token;
        }

        return token;
    }

    private async ValueTask<HonuaAuthToken?> ResolveProviderTokenAsync(CancellationToken ct)
        => _authTokenProvider is null
            ? null
            : await _authTokenProvider.GetTokenAsync(ct).ConfigureAwait(false);

    private Uri ResolveAbsoluteRequestUri(HttpRequestMessage request)
    {
        if (request.RequestUri is null)
        {
            throw new InvalidOperationException("Request URI cannot be null.");
        }

        if (request.RequestUri.IsAbsoluteUri)
        {
            return request.RequestUri;
        }

        return new Uri(_baseUri, request.RequestUri);
    }

    private void EnsureSecureTransport(Uri targetUri)
    {
        if (_options.AllowInsecureTransportForDevelopment)
        {
            return;
        }

        if (!string.Equals(targetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to send authentication over non-HTTPS transport. " +
                "Set AllowInsecureTransportForDevelopment=true only for local development.");
        }
    }

    private static Uri BuildUri(string relativePath, IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
        {
            return new Uri(relativePath, UriKind.Relative);
        }

        var queryText = string.Join(
            '&',
            query.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

        if (string.IsNullOrWhiteSpace(queryText))
        {
            return new Uri(relativePath, UriKind.Relative);
        }

        return new Uri($"{relativePath}?{queryText}", UriKind.Relative);
    }

    private Uri BuildAbsoluteUri(string relativePath, IReadOnlyDictionary<string, string?>? query)
    {
        var relative = BuildUri(relativePath, query);
        return relative.IsAbsoluteUri ? relative : new Uri(_baseUri, relative);
    }

    private static bool TryGetJsonProperty(JsonElement element, out JsonElement property, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out property))
            {
                return true;
            }
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (propertyNames.Any(name => string.Equals(name, candidate.Name, StringComparison.OrdinalIgnoreCase)))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, out string? value, params string[] propertyNames)
    {
        if (TryGetJsonProperty(element, out var property, propertyNames))
        {
            if (property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString();
                return !string.IsNullOrWhiteSpace(value);
            }

            if (property.ValueKind == JsonValueKind.Number)
            {
                value = property.GetRawText();
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetInt32(JsonElement element, out int value, params string[] propertyNames)
    {
        if (TryGetInt64(element, out var parsed, propertyNames) &&
            parsed >= int.MinValue &&
            parsed <= int.MaxValue)
        {
            value = (int)parsed;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetInt64(JsonElement element, out long value, params string[] propertyNames)
    {
        if (TryGetJsonProperty(element, out var property, propertyNames))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.String &&
                long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetBool(JsonElement element, out bool value, params string[] propertyNames)
    {
        if (TryGetJsonProperty(element, out var property, propertyNames))
        {
            if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }

            if (property.ValueKind == JsonValueKind.String &&
                bool.TryParse(property.GetString(), out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }
}
