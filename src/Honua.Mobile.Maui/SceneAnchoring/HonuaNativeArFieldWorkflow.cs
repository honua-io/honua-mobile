using System.Globalization;
using Honua.Mobile.Maui.Annotations;

namespace Honua.Mobile.Maui.SceneAnchoring;

/// <summary>
/// User-visible degraded state for a native AR field workflow.
/// </summary>
public enum HonuaNativeArFieldWorkflowDegradedMode
{
    None,
    UnsupportedDevice,
    Blocked,
    OfflinePackageUnavailable,
    CoarsePreviewOnly,
    PrecisionUnavailable,
}

/// <summary>
/// Field record context owned by the mobile workflow surface.
/// </summary>
public sealed record HonuaNativeArFieldContext
{
    public required string WorkflowId { get; init; }

    public string? FormId { get; init; }

    public string? RecordId { get; init; }

    public string? LayerId { get; init; }

    public string? FeatureId { get; init; }

    public string Purpose { get; init; } = "field-ar-scene-review";

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Purpose);
        ArgumentNullException.ThrowIfNull(Metadata);

        foreach (var key in Metadata.Keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
        }
    }
}

/// <summary>
/// Request that starts an AR scene workflow for a field record or report.
/// </summary>
public sealed record HonuaNativeArFieldWorkflowRequest
{
    public required HonuaNativeArSceneAnchorRequest AnchorRequest { get; init; }

    public required HonuaNativeArFieldContext FieldContext { get; init; }

    public bool RequiresPrecisionEvidence { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(AnchorRequest);
        ArgumentNullException.ThrowIfNull(FieldContext);
        AnchorRequest.Validate();
        FieldContext.Validate();
    }
}

/// <summary>
/// Current user-facing state for an AR field workflow.
/// </summary>
public sealed record HonuaNativeArFieldWorkflowState
{
    public required HonuaNativeArFieldContext FieldContext { get; init; }

    public required HonuaNativeArSceneAnchorRequest AnchorRequest { get; init; }

    public required HonuaNativeArReadiness Readiness { get; init; }

    public HonuaNativeArFieldWorkflowDegradedMode DegradedMode { get; init; }

    public required string UserVisibleStatus { get; init; }

    public required string OfflineBehavior { get; init; }

    public bool CanRenderOverlay => Readiness.CanRenderOverlay;

    public bool CanCaptureFieldEvidence => Readiness.CanCaptureEvidence;

    public bool CanUsePrecisionTools => Readiness.CanUsePrecisionTools;
}

/// <summary>
/// Evidence context attached to photos, annotations, or report metadata.
/// </summary>
public sealed record HonuaNativeArFieldEvidence
{
    public required HonuaNativeArFieldContext FieldContext { get; init; }

    public required HonuaNativeArEvidenceContext ArContext { get; init; }

    public required HonuaNativeArFieldWorkflowState WorkflowState { get; init; }

    public IReadOnlyDictionary<string, object?> ToMetadata()
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = "honua.native-ar-field-evidence.v1",
            ["workflowId"] = FieldContext.WorkflowId,
            ["purpose"] = FieldContext.Purpose,
            ["formId"] = FieldContext.FormId,
            ["recordId"] = FieldContext.RecordId,
            ["layerId"] = FieldContext.LayerId,
            ["featureId"] = FieldContext.FeatureId,
            ["sceneId"] = ArContext.SceneId,
            ["sceneRevision"] = ArContext.SceneRevision,
            ["packageId"] = ArContext.PackageId,
            ["isOffline"] = ArContext.IsOffline,
            ["isOnline"] = ArContext.IsOnline,
            ["runtime"] = ArContext.Runtime.ToString(),
            ["deviceModel"] = ArContext.DeviceModel,
            ["readinessLevel"] = ArContext.ReadinessLevel.ToString(),
            ["anchoringMode"] = ArContext.ActiveAnchoringMode.ToString(),
            ["packageState"] = ArContext.PackageState.ToString(),
            ["horizontalAccuracyMeters"] = ArContext.HorizontalAccuracyMeters,
            ["verticalAccuracyMeters"] = ArContext.VerticalAccuracyMeters,
            ["yawAccuracyDegrees"] = ArContext.YawAccuracyDegrees,
            ["calibrationResidualMeters"] = ArContext.CalibrationResidualMeters,
            ["confirmedControlPointCount"] = ArContext.ConfirmedControlPointCount,
            ["controlPointIds"] = ArContext.ControlPointIds.ToArray(),
            ["blockers"] = ArContext.Blockers.ToArray(),
            ["warnings"] = ArContext.Warnings.ToArray(),
            ["canAttachToFieldEvidence"] = ArContext.CanAttachToFieldEvidence,
            ["canAttachToPrecisionEvidence"] = ArContext.CanAttachToPrecisionEvidence,
            ["capturedAtUtc"] = ArContext.CapturedAt.ToString("O", CultureInfo.InvariantCulture),
            ["degradedMode"] = WorkflowState.DegradedMode.ToString(),
            ["userVisibleStatus"] = WorkflowState.UserVisibleStatus,
            ["offlineBehavior"] = WorkflowState.OfflineBehavior,
        };

        foreach (var item in FieldContext.Metadata)
        {
            metadata[$"field.{item.Key}"] = item.Value;
        }

        return metadata;
    }
}

/// <summary>
/// Coordinates the first mobile-owned AR field workflow over scene anchoring,
/// cached scene package state, and field capture context.
/// </summary>
public sealed class HonuaNativeArFieldWorkflow
{
    public const string EvidenceMetadataKey = "honua.arEvidence";

    private readonly HonuaNativeArSceneAnchoringController _anchoringController;
    private HonuaNativeArFieldWorkflowRequest? _activeRequest;
    private HonuaNativeArFieldWorkflowState? _activeState;

    public HonuaNativeArFieldWorkflow(HonuaNativeArSceneAnchoringController anchoringController)
    {
        _anchoringController = anchoringController ?? throw new ArgumentNullException(nameof(anchoringController));
    }

    public async ValueTask<HonuaNativeArFieldWorkflowState> StartAsync(
        HonuaNativeArFieldWorkflowRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var readiness = await _anchoringController.StartAsync(request.AnchorRequest, ct).ConfigureAwait(false);
        var state = CreateState(request, readiness);
        _activeRequest = request;
        _activeState = state;
        return state;
    }

    public async ValueTask<HonuaNativeArFieldWorkflowState> RefreshAsync(CancellationToken ct = default)
    {
        var request = _activeRequest
            ?? throw new InvalidOperationException("No native AR field workflow is active.");

        var readiness = await _anchoringController.RefreshReadinessAsync(ct).ConfigureAwait(false);
        var state = CreateState(request, readiness);
        _activeState = state;
        return state;
    }

    public async ValueTask<HonuaNativeArFieldEvidence> CreateEvidenceAsync(CancellationToken ct = default)
    {
        var request = _activeRequest
            ?? throw new InvalidOperationException("No native AR field workflow is active.");

        var state = _activeState
            ?? throw new InvalidOperationException("No native AR field workflow state is active.");

        var arContext = await _anchoringController.CreateEvidenceContextAsync(ct).ConfigureAwait(false);
        return new HonuaNativeArFieldEvidence
        {
            FieldContext = request.FieldContext,
            ArContext = arContext,
            WorkflowState = state,
        };
    }

    public HonuaAnnotation AttachEvidence(
        HonuaAnnotation annotation,
        HonuaNativeArFieldEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(evidence);

        var metadata = new Dictionary<string, object?>(annotation.Metadata, StringComparer.Ordinal)
        {
            [EvidenceMetadataKey] = evidence.ToMetadata(),
        };

        return annotation with
        {
            Metadata = metadata,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public IReadOnlyDictionary<string, object?> CreateReportMetadata(HonuaNativeArFieldEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new Dictionary<string, object?>
        {
            [EvidenceMetadataKey] = evidence.ToMetadata(),
        };
    }

    private static HonuaNativeArFieldWorkflowState CreateState(
        HonuaNativeArFieldWorkflowRequest request,
        HonuaNativeArReadiness readiness)
    {
        return new HonuaNativeArFieldWorkflowState
        {
            FieldContext = request.FieldContext,
            AnchorRequest = request.AnchorRequest,
            Readiness = readiness,
            DegradedMode = DetermineDegradedMode(request, readiness),
            UserVisibleStatus = BuildUserVisibleStatus(request, readiness),
            OfflineBehavior = BuildOfflineBehavior(request.AnchorRequest, readiness),
        };
    }

    private static HonuaNativeArFieldWorkflowDegradedMode DetermineDegradedMode(
        HonuaNativeArFieldWorkflowRequest request,
        HonuaNativeArReadiness readiness)
    {
        if (readiness.Level == HonuaNativeArReadinessLevel.Unsupported)
        {
            return HonuaNativeArFieldWorkflowDegradedMode.UnsupportedDevice;
        }

        if (readiness.Level == HonuaNativeArReadinessLevel.Blocked)
        {
            return request.AnchorRequest.IsOffline
                && readiness.Blockers.Any(blocker => blocker.Contains("Offline scene package", StringComparison.Ordinal))
                    ? HonuaNativeArFieldWorkflowDegradedMode.OfflinePackageUnavailable
                    : HonuaNativeArFieldWorkflowDegradedMode.Blocked;
        }

        if (readiness.Level == HonuaNativeArReadinessLevel.CoarsePreview)
        {
            return HonuaNativeArFieldWorkflowDegradedMode.CoarsePreviewOnly;
        }

        if (request.RequiresPrecisionEvidence
            && readiness.Level != HonuaNativeArReadinessLevel.PrecisionInspection)
        {
            return HonuaNativeArFieldWorkflowDegradedMode.PrecisionUnavailable;
        }

        return HonuaNativeArFieldWorkflowDegradedMode.None;
    }

    private static string BuildUserVisibleStatus(
        HonuaNativeArFieldWorkflowRequest request,
        HonuaNativeArReadiness readiness)
    {
        return readiness.Level switch
        {
            HonuaNativeArReadinessLevel.Unsupported =>
                "AR is unavailable on this device. Continue with the 2D field workflow.",
            HonuaNativeArReadinessLevel.Blocked =>
                $"AR overlay is blocked: {FirstReason(readiness.Blockers)}",
            HonuaNativeArReadinessLevel.CoarsePreview =>
                "Coarse AR preview is available. Evidence capture and precision tools are disabled.",
            HonuaNativeArReadinessLevel.SiteReview when request.RequiresPrecisionEvidence =>
                "Site review is available. Precision evidence is disabled until survey-quality calibration is complete.",
            HonuaNativeArReadinessLevel.SiteReview =>
                "Site review is available. Captures will include AR scene, accuracy, and package metadata.",
            HonuaNativeArReadinessLevel.PrecisionInspection =>
                "Precision inspection is available. Captures will include calibration residual and control-point evidence.",
            _ => "AR workflow state is unknown.",
        };
    }

    private static string BuildOfflineBehavior(
        HonuaNativeArSceneAnchorRequest request,
        HonuaNativeArReadiness readiness)
    {
        if (!request.IsOffline)
        {
            return "Online scene metadata is active; evidence records online runtime state.";
        }

        if (readiness.Blockers.Any(blocker => blocker.Contains("Offline scene package", StringComparison.Ordinal)))
        {
            return "Offline scene package is not valid for AR rendering; use the 2D field workflow or refresh the package.";
        }

        return $"Using validated offline scene package '{request.PackageId}' for cached scene assets.";
    }

    private static string FirstReason(IReadOnlyList<string> reasons)
        => reasons.Count == 0 ? "unknown reason" : reasons[0];
}
