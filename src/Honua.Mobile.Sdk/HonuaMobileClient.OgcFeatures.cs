// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.Sdk;

public sealed partial class HonuaMobileClient
{
    /// <summary>
    /// Retrieves the list of OGC Features API collections available on the server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> describing available collections.</returns>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> GetOgcCollectionsAsync(CancellationToken ct = default)
    {
        return await SendJsonAsync(
            HttpMethod.Get,
            "/ogc/features/collections",
            query: null,
            content: null,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves items from an OGC Features API collection with optional filtering and pagination.
    /// </summary>
    /// <param name="request">Parameters including collection ID, CQL filter, limit, and offset.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the matched items.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> GetOgcItemsAsync(OgcItemsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items";
        return await SendJsonAsync(
            HttpMethod.Get,
            path,
            BuildOgcItemsQueryParameters(request),
            content: null,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new feature item in an OGC Features API collection.
    /// </summary>
    /// <param name="request">The collection ID and GeoJSON feature to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the server response for the created item.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public Task<JsonDocument> CreateOgcItemAsync(OgcCreateItemRequest request, CancellationToken ct = default)
        => CreateOgcItemAsync(request, idempotencyKey: null, ct);

    /// <summary>
    /// Creates a new feature item in an OGC Features API collection, attaching a stable
    /// <c>Idempotency-Key</c> so the server can dedupe a retried create (at-most-once,
    /// honua-server #2250).
    /// </summary>
    /// <remarks>
    /// An OGC create is a server-assigned-id POST and so is not naturally idempotent: a network
    /// failure after the server commits but before the client reads the ack would, on retry,
    /// create a duplicate feature. Supplying a stable key lets the server replay the original
    /// response instead. OGC update (PATCH/PUT by id) and delete (by id) are naturally idempotent
    /// and do not need a key.
    /// </remarks>
    /// <param name="request">The collection ID and GeoJSON feature to create.</param>
    /// <param name="idempotencyKey">Stable client-generated key (≤200 chars, no control characters), or <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the server response for the created item.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> CreateOgcItemAsync(
        OgcCreateItemRequest request,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items";
        return await SendJsonAsync(
            HttpMethod.Post,
            path,
            query: null,
            CreateJsonContent(request.Feature, "application/geo+json"),
            ct,
            idempotencyKey).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces an existing feature item in an OGC Features API collection (HTTP PUT).
    /// </summary>
    /// <param name="request">The collection ID, feature ID, and full replacement feature.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the server response.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> ReplaceOgcItemAsync(OgcReplaceItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items/{Uri.EscapeDataString(request.FeatureId)}";
        return await SendJsonAsync(
            HttpMethod.Put,
            path,
            query: null,
            CreateJsonContent(request.Feature, "application/geo+json"),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Partially updates an existing feature item using JSON Merge Patch (RFC 7396).
    /// </summary>
    /// <param name="request">The collection ID, feature ID, and merge-patch document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the server response.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> PatchOgcItemAsync(OgcPatchItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items/{Uri.EscapeDataString(request.FeatureId)}";
        return await SendJsonAsync(
            HttpMethod.Patch,
            path,
            query: null,
            CreateJsonContent(request.Patch, "application/merge-patch+json"),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a feature item from an OGC Features API collection.
    /// </summary>
    /// <param name="request">The collection ID and feature ID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the server response.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> DeleteOgcItemAsync(OgcDeleteItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items/{Uri.EscapeDataString(request.FeatureId)}";
        return await SendJsonAsync(HttpMethod.Delete, path, null, null, ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string?> BuildOgcItemsQueryParameters(OgcItemsRequest request)
    {
        var query = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (request.Limit is { } limit)
        {
            query["limit"] = limit.ToString(CultureInfo.InvariantCulture);
        }

        if (request.Offset is { } offset)
        {
            query["offset"] = offset.ToString(CultureInfo.InvariantCulture);
        }

        if (request.PropertyNames is { Count: > 0 } propertyNames)
        {
            query["properties"] = string.Join(',', propertyNames);
        }

        if (!string.IsNullOrWhiteSpace(request.CqlFilter))
        {
            query["filter"] = request.CqlFilter;
        }

        if (!string.IsNullOrWhiteSpace(request.ResponseFormat))
        {
            query["f"] = request.ResponseFormat;
        }

        return query;
    }

    private static StringContent CreateJsonContent(JsonElement element, string mediaType)
        => new(element.GetRawText(), Encoding.UTF8, mediaType);
}
