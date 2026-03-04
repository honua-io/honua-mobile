using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Mobile.Sdk.Models;

namespace Honua.Mobile.Sdk;

public sealed class HonuaMobileClient
{
    private readonly HttpClient _http;
    private readonly HonuaMobileClientOptions _options;

    public HonuaMobileClient(HttpClient httpClient, HonuaMobileClientOptions options)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http.BaseAddress = options.BaseUri;
        _http.Timeout = options.Timeout;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(options.UserAgent);
    }

    public Task<JsonDocument> QueryFeaturesAsync(QueryFeaturesRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["where"] = request.Where,
            ["outFields"] = request.OutFields is { Count: > 0 } ? string.Join(',', request.OutFields) : "*",
            ["resultRecordCount"] = request.ResultRecordCount?.ToString(),
        };

        var path = $"/rest/services/{Uri.EscapeDataString(request.ServiceId)}/FeatureServer/{request.LayerId}/query";
        return SendJsonAsync(HttpMethod.Get, path, query, content: null, ct);
    }

    public Task<JsonDocument> ApplyEditsAsync(ApplyEditsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/rest/services/{Uri.EscapeDataString(request.ServiceId)}/FeatureServer/{request.LayerId}/applyEdits";
        var body = new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["adds"] = request.AddsJson,
            ["updates"] = request.UpdatesJson,
            ["deletes"] = request.DeletesCsv,
            ["rollbackOnFailure"] = request.RollbackOnFailure ? "true" : "false",
        }
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
        .ToDictionary(pair => pair.Key, pair => pair.Value!);

        return SendJsonAsync(HttpMethod.Post, path, query: null, new FormUrlEncodedContent(body), ct);
    }

    public Task<JsonDocument> GetOgcCollectionsAsync(CancellationToken ct = default)
        => SendJsonAsync(HttpMethod.Get, "/ogc/features/collections", new Dictionary<string, string?> { ["f"] = "json" }, null, ct);

    public Task<JsonDocument> GetOgcItemsAsync(OgcItemsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["limit"] = request.Limit?.ToString(),
            ["offset"] = request.Offset?.ToString(),
            ["properties"] = request.PropertyNames is { Count: > 0 } ? string.Join(',', request.PropertyNames) : null,
            ["filter"] = request.CqlFilter,
        };

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items";
        return SendJsonAsync(HttpMethod.Get, path, query, null, ct);
    }

    public Task<JsonDocument> CreateOgcItemAsync(OgcCreateItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items";
        var payload = JsonSerializer.Serialize(request.Feature);
        return SendJsonAsync(HttpMethod.Post, path, null, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        IReadOnlyDictionary<string, string?>? query,
        HttpContent? content,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BuildUri(relativePath, query));
        request.Content = content;
        await ApplyAuthenticationAsync(request, ct).ConfigureAwait(false);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HonuaMobileApiException(
                response.StatusCode,
                $"Honua mobile request failed with status {(int)response.StatusCode} {response.ReasonPhrase}",
                raw);
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
    }

    private async ValueTask ApplyAuthenticationAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
        }

        var token = _options.BearerToken;
        if (_options.AccessTokenProvider is not null)
        {
            token = await _options.AccessTokenProvider(ct).ConfigureAwait(false) ?? token;
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private Uri BuildUri(string relativePath, IReadOnlyDictionary<string, string?>? query)
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
}
