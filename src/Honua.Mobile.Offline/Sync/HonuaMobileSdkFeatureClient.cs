using System.Runtime.CompilerServices;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Features;
using SdkFeatureClient = Honua.Mobile.Sdk.Features.HonuaMobileSdkFeatureClient;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Backward-compatible offline namespace shim for the SDK feature adapter.
/// </summary>
public sealed class HonuaMobileSdkFeatureClient :
    IHonuaFeatureQueryClient,
    IHonuaFeatureEditClient,
    IHonuaFeatureAttachmentClient
{
    private readonly SdkFeatureClient _inner;

    /// <summary>
    /// Initializes a new <see cref="HonuaMobileSdkFeatureClient"/>.
    /// </summary>
    /// <param name="client">Mobile client used for server calls.</param>
    public HonuaMobileSdkFeatureClient(HonuaMobileClient client)
    {
        _inner = new SdkFeatureClient(client);
    }

    /// <inheritdoc />
    public string ProviderName => _inner.ProviderName;

    /// <inheritdoc />
    public FeatureEditCapabilities EditCapabilities => _inner.EditCapabilities;

    /// <inheritdoc />
    public FeatureAttachmentCapabilities AttachmentCapabilities => _inner.AttachmentCapabilities;

    /// <inheritdoc />
    public Task<FeatureQueryResult> QueryAsync(FeatureQueryRequest request, CancellationToken ct = default)
        => _inner.QueryAsync(request, ct);

    /// <inheritdoc />
    public async IAsyncEnumerable<FeatureQueryResult> QueryPagesAsync(
        FeatureQueryRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var page in _inner.QueryPagesAsync(request, ct).ConfigureAwait(false))
        {
            yield return page;
        }
    }

    /// <inheritdoc />
    public Task<FeatureEditResponse> ApplyEditsAsync(FeatureEditRequest request, CancellationToken ct = default)
        => _inner.ApplyEditsAsync(request, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<FeatureAttachmentInfo>> ListAttachmentsAsync(
        FeatureAttachmentListRequest request,
        CancellationToken ct = default)
        => _inner.ListAttachmentsAsync(request, ct);

    /// <inheritdoc />
    public Task<FeatureAttachmentContent> DownloadAttachmentAsync(
        FeatureAttachmentDownloadRequest request,
        CancellationToken ct = default)
        => _inner.DownloadAttachmentAsync(request, ct);

    /// <inheritdoc />
    public Task<FeatureAttachmentResult> AddAttachmentAsync(
        FeatureAttachmentAddRequest request,
        CancellationToken ct = default)
        => _inner.AddAttachmentAsync(request, ct);

    /// <inheritdoc />
    public Task<FeatureAttachmentResult> UpdateAttachmentAsync(
        FeatureAttachmentUpdateRequest request,
        CancellationToken ct = default)
        => _inner.UpdateAttachmentAsync(request, ct);

    /// <inheritdoc />
    public Task<FeatureAttachmentResult> DeleteAttachmentAsync(
        FeatureAttachmentDeleteRequest request,
        CancellationToken ct = default)
        => _inner.DeleteAttachmentAsync(request, ct);
}
