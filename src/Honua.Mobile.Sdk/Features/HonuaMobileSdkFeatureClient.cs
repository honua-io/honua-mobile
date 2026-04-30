using System.Runtime.CompilerServices;
using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.Sdk.Features;

/// <summary>
/// Adapts <see cref="HonuaMobileClient"/> to the SDK feature query and edit abstractions.
/// </summary>
public sealed class HonuaMobileSdkFeatureClient :
    IHonuaFeatureQueryClient,
    IHonuaFeatureEditClient,
    IHonuaFeatureAttachmentClient
{
    private readonly HonuaMobileClient _client;

    /// <summary>
    /// Initializes a new <see cref="HonuaMobileSdkFeatureClient"/>.
    /// </summary>
    /// <param name="client">Mobile client used for server calls.</param>
    public HonuaMobileSdkFeatureClient(HonuaMobileClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public string ProviderName => "honua-mobile";

    /// <inheritdoc />
    public FeatureEditCapabilities EditCapabilities { get; } = new()
    {
        SupportsAdds = true,
        SupportsUpdates = true,
        SupportsDeletes = true,
        SupportsRollbackOnFailure = true,
        NativeSurface = "HonuaMobileClient",
    };

    /// <inheritdoc />
    public FeatureAttachmentCapabilities AttachmentCapabilities { get; } = new()
    {
        SupportsList = true,
        SupportsDownload = true,
        SupportsAdd = true,
        SupportsUpdate = true,
        SupportsDelete = true,
        NativeSurface = "HonuaMobileClient FeatureServer attachments",
    };

    /// <inheritdoc />
    public async Task<FeatureQueryResult> QueryAsync(FeatureQueryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await _client.QueryAsync(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FeatureQueryResult> QueryPagesAsync(
        FeatureQueryRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await foreach (var page in _client.QueryPagesAsync(request, ct).ConfigureAwait(false))
        {
            yield return page;
        }
    }

    /// <inheritdoc />
    public async Task<FeatureEditResponse> ApplyEditsAsync(FeatureEditRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _client.ApplyEditsAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaMobileApiException ex)
        {
            return new FeatureEditResponse
            {
                ProviderName = ProviderName,
                Error = new FeatureEditError { Code = (int)ex.StatusCode, Message = ex.Message },
            };
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FeatureAttachmentInfo>> ListAttachmentsAsync(
        FeatureAttachmentListRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureFeatureServerAttachmentSource(request.Source);
        return _client.ListAttachmentsAsync(request, ct);
    }

    /// <inheritdoc />
    public Task<FeatureAttachmentContent> DownloadAttachmentAsync(
        FeatureAttachmentDownloadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureFeatureServerAttachmentSource(request.Source);
        return _client.DownloadAttachmentAsync(request, ct);
    }

    /// <inheritdoc />
    public async Task<FeatureAttachmentResult> AddAttachmentAsync(
        FeatureAttachmentAddRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureFeatureServerAttachmentSource(request.Source);

        try
        {
            return await _client.AddAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaMobileApiException ex)
        {
            return ToAttachmentErrorResult(ex);
        }
    }

    /// <inheritdoc />
    public async Task<FeatureAttachmentResult> UpdateAttachmentAsync(
        FeatureAttachmentUpdateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureFeatureServerAttachmentSource(request.Source);

        try
        {
            return await _client.UpdateAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaMobileApiException ex)
        {
            return ToAttachmentErrorResult(ex);
        }
    }

    /// <inheritdoc />
    public async Task<FeatureAttachmentResult> DeleteAttachmentAsync(
        FeatureAttachmentDeleteRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureFeatureServerAttachmentSource(request.Source);

        try
        {
            return await _client.DeleteAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaMobileApiException ex)
        {
            return ToAttachmentErrorResult(ex);
        }
    }

    private static void EnsureFeatureServerAttachmentSource(FeatureSource source)
    {
        if (string.IsNullOrWhiteSpace(source.ServiceId) || !source.LayerId.HasValue)
        {
            throw new InvalidOperationException(
                "Mobile attachment operations currently require FeatureServer service and layer identifiers.");
        }
    }

    private static FeatureAttachmentResult ToAttachmentErrorResult(HonuaMobileApiException exception)
        => new()
        {
            Succeeded = false,
            Error = new FeatureEditError
            {
                Code = (int)exception.StatusCode,
                Message = exception.Message,
            },
        };
}
