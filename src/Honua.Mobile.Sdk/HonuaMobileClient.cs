using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Honua.Mobile.Sdk.Grpc;
using Honua.Mobile.Sdk.Models;
using Proto = Honua.Server.Features.Grpc.Proto;

namespace Honua.Mobile.Sdk;

public sealed class HonuaMobileClient : IDisposable, IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly HonuaMobileClientOptions _options;
    private readonly GrpcChannel? _grpcChannel;
    private readonly Proto.FeatureService.FeatureServiceClient? _grpcClient;

    public HonuaMobileClient(HttpClient httpClient, HonuaMobileClientOptions options)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http.BaseAddress = options.BaseUri;
        _http.Timeout = options.Timeout;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(options.UserAgent);

        var grpcAddress = options.GrpcEndpoint ?? options.BaseUri;
        if (grpcAddress.Scheme is "http" or "https")
        {
            _grpcChannel = GrpcChannel.ForAddress(grpcAddress);
            _grpcClient = new Proto.FeatureService.FeatureServiceClient(_grpcChannel);
        }
    }

    public async Task<JsonDocument> QueryFeaturesAsync(QueryFeaturesRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CanUseGrpcForQueries)
        {
            try
            {
                return await QueryFeaturesGrpcAsync(request, ct).ConfigureAwait(false);
            }
            catch (RpcException) when (_options.AllowRestFallbackOnGrpcFailure)
            {
                // Fall through to REST transport.
            }
        }

        return await QueryFeaturesRestAsync(request, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<JsonDocument> QueryFeaturesStreamAsync(
        QueryFeaturesRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CanUseGrpcForQueries)
        {
            var yieldedGrpcPage = false;
            await using var grpcEnumerator = QueryFeaturesGrpcPagesAsync(request, ct).GetAsyncEnumerator(ct);

            while (true)
            {
                JsonDocument? nextPage = null;
                try
                {
                    if (!await grpcEnumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        if (yieldedGrpcPage)
                        {
                            yield break;
                        }

                        break;
                    }

                    nextPage = grpcEnumerator.Current;
                }
                catch (RpcException) when (_options.AllowRestFallbackOnGrpcFailure && !yieldedGrpcPage)
                {
                    break;
                }

                yieldedGrpcPage = true;
                yield return nextPage!;
            }
        }

        yield return await QueryFeaturesRestAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<JsonDocument> ApplyEditsAsync(ApplyEditsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CanUseGrpcForEdits)
        {
            try
            {
                return await ApplyEditsGrpcAsync(request, ct).ConfigureAwait(false);
            }
            catch (RpcException) when (_options.AllowRestFallbackOnGrpcFailure)
            {
                // Fall through to REST transport.
            }
        }

        return await ApplyEditsRestAsync(request, ct).ConfigureAwait(false);
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

    public Task<JsonDocument> ReplaceOgcItemAsync(OgcReplaceItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items/{Uri.EscapeDataString(request.FeatureId)}";
        var payload = JsonSerializer.Serialize(request.Feature);
        return SendJsonAsync(HttpMethod.Put, path, null, new StringContent(payload, Encoding.UTF8, "application/json"), ct);
    }

    public Task<JsonDocument> PatchOgcItemAsync(OgcPatchItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items/{Uri.EscapeDataString(request.FeatureId)}";
        var payload = JsonSerializer.Serialize(request.Patch);
        return SendJsonAsync(new HttpMethod("PATCH"), path, null, new StringContent(payload, Encoding.UTF8, "application/merge-patch+json"), ct);
    }

    public Task<JsonDocument> DeleteOgcItemAsync(OgcDeleteItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items/{Uri.EscapeDataString(request.FeatureId)}";
        return SendJsonAsync(HttpMethod.Delete, path, null, null, ct);
    }

    public void Dispose()
    {
        _grpcChannel?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private bool CanUseGrpcForQueries => _options.PreferGrpcForFeatureQueries && _grpcClient is not null;

    private bool CanUseGrpcForEdits => _options.PreferGrpcForFeatureEdits && _grpcClient is not null;

    private async Task<JsonDocument> QueryFeaturesGrpcAsync(QueryFeaturesRequest request, CancellationToken ct)
    {
        var protoRequest = GrpcFeatureTranslator.ToProtoQueryRequest(request);
        var metadata = await BuildGrpcMetadataAsync(ct).ConfigureAwait(false);
        var response = await _grpcClient!.QueryFeaturesAsync(protoRequest, metadata, cancellationToken: ct).ConfigureAwait(false);
        return GrpcFeatureTranslator.ToJsonDocument(response);
    }

    private async IAsyncEnumerable<JsonDocument> QueryFeaturesGrpcPagesAsync(
        QueryFeaturesRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var protoRequest = GrpcFeatureTranslator.ToProtoQueryRequest(request);
        var metadata = await BuildGrpcMetadataAsync(ct).ConfigureAwait(false);
        using var call = _grpcClient!.QueryFeaturesStream(protoRequest, metadata, cancellationToken: ct);
        await foreach (var page in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return GrpcFeatureTranslator.ToJsonDocument(page);
        }
    }

    private async Task<JsonDocument> ApplyEditsGrpcAsync(ApplyEditsRequest request, CancellationToken ct)
    {
        var protoRequest = GrpcFeatureTranslator.ToProtoApplyEditsRequest(request);
        var metadata = await BuildGrpcMetadataAsync(ct).ConfigureAwait(false);
        var response = await _grpcClient!.ApplyEditsAsync(protoRequest, metadata, cancellationToken: ct).ConfigureAwait(false);
        return GrpcFeatureTranslator.ToJsonDocument(response);
    }

    private Task<JsonDocument> QueryFeaturesRestAsync(QueryFeaturesRequest request, CancellationToken ct)
    {
        var query = new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["where"] = request.Where,
            ["objectIds"] = request.ObjectIds is { Count: > 0 } ? string.Join(',', request.ObjectIds) : null,
            ["outFields"] = request.OutFields is { Count: > 0 } ? string.Join(',', request.OutFields) : "*",
            ["returnGeometry"] = request.ReturnGeometry ? "true" : "false",
            ["resultOffset"] = request.ResultOffset?.ToString(),
            ["resultRecordCount"] = request.ResultRecordCount?.ToString(),
            ["orderByFields"] = request.OrderBy,
            ["returnDistinctValues"] = request.ReturnDistinct ? "true" : null,
            ["returnCountOnly"] = request.ReturnCountOnly ? "true" : null,
            ["returnIdsOnly"] = request.ReturnIdsOnly ? "true" : null,
            ["returnExtentOnly"] = request.ReturnExtentOnly ? "true" : null,
        };

        var path = $"/rest/services/{Uri.EscapeDataString(request.ServiceId)}/FeatureServer/{request.LayerId}/query";
        return SendJsonAsync(HttpMethod.Get, path, query, content: null, ct);
    }

    private Task<JsonDocument> ApplyEditsRestAsync(ApplyEditsRequest request, CancellationToken ct)
    {
        var path = $"/rest/services/{Uri.EscapeDataString(request.ServiceId)}/FeatureServer/{request.LayerId}/applyEdits";
        var body = new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["adds"] = request.AddsJson,
            ["updates"] = request.UpdatesJson,
            ["deletes"] = request.DeletesCsv,
            ["rollbackOnFailure"] = request.RollbackOnFailure ? "true" : "false",
            ["forceWrite"] = request.ForceWrite ? "true" : null,
        }
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
        .ToDictionary(pair => pair.Key, pair => pair.Value!);

        return SendJsonAsync(HttpMethod.Post, path, query: null, new FormUrlEncodedContent(body), ct);
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
        await ApplyHttpAuthenticationAsync(request, ct).ConfigureAwait(false);

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

    private async ValueTask ApplyHttpAuthenticationAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await ResolveBearerTokenAsync(ct).ConfigureAwait(false);
        var hasApiKey = !string.IsNullOrWhiteSpace(_options.ApiKey);
        var hasBearerToken = !string.IsNullOrWhiteSpace(token);

        if (hasApiKey || hasBearerToken)
        {
            EnsureSecureTransport(ResolveAbsoluteRequestUri(request));
        }

        if (hasApiKey)
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<Metadata> BuildGrpcMetadataAsync(CancellationToken ct)
    {
        var metadata = new Metadata();
        var token = await ResolveBearerTokenAsync(ct).ConfigureAwait(false);
        var hasApiKey = !string.IsNullOrWhiteSpace(_options.ApiKey);
        var hasBearerToken = !string.IsNullOrWhiteSpace(token);

        if (hasApiKey || hasBearerToken)
        {
            EnsureSecureTransport(_options.GrpcEndpoint ?? _options.BaseUri);
        }

        if (hasApiKey)
        {
            metadata.Add("x-api-key", _options.ApiKey!);
        }

        if (hasBearerToken)
        {
            metadata.Add("authorization", $"Bearer {token!}");
        }

        return metadata;
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

        if (_http.BaseAddress is null)
        {
            throw new InvalidOperationException("HonuaMobileClient requires an absolute BaseUri.");
        }

        return new Uri(_http.BaseAddress, request.RequestUri);
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
}
