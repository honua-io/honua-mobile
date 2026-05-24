using System.Globalization;
using System.Net;
using System.Text.Json;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.GeoServices.FeatureServer.Exceptions;

namespace Honua.Mobile.Sdk;

public sealed partial class HonuaMobileClient
{
    /// <summary>
    /// Lists FeatureServer attachments using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment list request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Attachment metadata for the requested feature.</returns>
    public async Task<IReadOnlyList<FeatureAttachmentInfo>> ListAttachmentsAsync(
        FeatureAttachmentListRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.ListAttachmentsAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await ListAttachmentsRestCompatAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    /// <summary>
    /// Downloads one FeatureServer attachment using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment download request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Downloaded attachment content. Dispose the returned content stream when finished.</returns>
    public async Task<FeatureAttachmentContent> DownloadAttachmentAsync(
        FeatureAttachmentDownloadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.DownloadAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    /// <summary>
    /// Adds one FeatureServer attachment using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment add request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Attachment edit result.</returns>
    public async Task<FeatureAttachmentResult> AddAttachmentAsync(
        FeatureAttachmentAddRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.AddAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    /// <summary>
    /// Updates one FeatureServer attachment using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Attachment edit result.</returns>
    public async Task<FeatureAttachmentResult> UpdateAttachmentAsync(
        FeatureAttachmentUpdateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.UpdateAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    /// <summary>
    /// Deletes one FeatureServer attachment using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment delete request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Attachment edit result.</returns>
    public async Task<FeatureAttachmentResult> DeleteAttachmentAsync(
        FeatureAttachmentDeleteRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.DeleteAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    private async Task<IReadOnlyList<FeatureAttachmentInfo>> ListAttachmentsRestCompatAsync(
        FeatureAttachmentListRequest request,
        CancellationToken ct)
    {
        var serviceId = request.Source.ServiceId
            ?? throw new InvalidOperationException("FeatureServer attachments require a service ID.");
        var layerId = request.Source.LayerId
            ?? throw new InvalidOperationException("FeatureServer attachments require a layer ID.");
        var path = $"/rest/services/{EscapePathSegments(serviceId)}/FeatureServer/{layerId}/queryAttachments";

        using var response = await SendJsonAsync(
            HttpMethod.Get,
            path,
            new Dictionary<string, string?>
            {
                ["f"] = "json",
                ["objectIds"] = request.ObjectId.ToString(CultureInfo.InvariantCulture),
                ["returnUrl"] = "true",
            },
            content: null,
            ct).ConfigureAwait(false);

        return ToFeatureAttachmentInfos(response.RootElement, request.Source, request.ObjectId);
    }

    private static IReadOnlyList<FeatureAttachmentInfo> ToFeatureAttachmentInfos(
        JsonElement root,
        FeatureSource source,
        long parentObjectId)
    {
        var attachments = new List<FeatureAttachmentInfo>();
        if (TryGetJsonProperty(root, out var groups, "attachmentGroups") && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var groupParentObjectId = TryGetInt64(group, out var parsedParentObjectId, "parentObjectId", "objectId", "objectid")
                    ? parsedParentObjectId
                    : parentObjectId;
                AddAttachmentInfos(attachments, group, source, groupParentObjectId);
            }
        }

        AddAttachmentInfos(attachments, root, source, parentObjectId);
        return attachments;
    }

    private static void AddAttachmentInfos(
        List<FeatureAttachmentInfo> attachments,
        JsonElement element,
        FeatureSource source,
        long parentObjectId)
    {
        if (!TryGetJsonProperty(element, out var infos, "attachmentInfos") || infos.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var info in infos.EnumerateArray())
        {
            if (info.ValueKind == JsonValueKind.Object)
            {
                attachments.Add(ToFeatureAttachmentInfo(info, source, parentObjectId));
            }
        }
    }

    private static FeatureAttachmentInfo ToFeatureAttachmentInfo(JsonElement element, FeatureSource source, long parentObjectId)
    {
        var parsedParentObjectId = TryGetInt64(element, out var attachmentParentObjectId, "parentObjectId", "objectId", "objectid")
            ? attachmentParentObjectId
            : parentObjectId;
        var url = TryGetString(element, out var rawUrl, "url") && !string.IsNullOrWhiteSpace(rawUrl)
            ? new Uri(rawUrl, UriKind.RelativeOrAbsolute)
            : null;

        return new FeatureAttachmentInfo
        {
            Source = source,
            ParentObjectId = parsedParentObjectId,
            AttachmentId = TryGetInt64(element, out var attachmentId, "id", "attachmentId")
                ? attachmentId
                : 0,
            GlobalId = TryGetString(element, out var globalId, "globalId", "globalid")
                ? globalId
                : null,
            Name = TryGetString(element, out var name, "name")
                ? name
                : string.Empty,
            ContentType = TryGetString(element, out var contentType, "contentType")
                ? contentType
                : "application/octet-stream",
            Size = TryGetInt64(element, out var size, "size")
                ? size
                : 0,
            Keywords = TryGetString(element, out var keywords, "keywords")
                ? keywords
                : null,
            Url = url,
        };
    }
}
