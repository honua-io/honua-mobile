namespace Honua.Mobile.Maui.SceneAnchoring;

/// <summary>
/// Coordinates mobile-owned AR scene anchoring policy with a platform-native AR adapter.
/// </summary>
public sealed class HonuaNativeArSceneAnchoringController
{
    private readonly IHonuaNativeArSceneAnchorAdapter _adapter;
    private readonly HonuaNativeArSessionOptions _options;
    private HonuaNativeArSceneAnchorRequest? _activeRequest;
    private bool _resumeAfterLifecyclePause;

    public HonuaNativeArSceneAnchoringController(
        IHonuaNativeArSceneAnchorAdapter adapter,
        HonuaNativeArSessionOptions? options = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _options = options ?? new HonuaNativeArSessionOptions();
        _options.Validate();
    }

    /// <summary>
    /// Checks platform support, starts the native AR session, and returns the UI readiness gate.
    /// </summary>
    public async ValueTask<HonuaNativeArReadiness> StartAsync(
        HonuaNativeArSceneAnchorRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var support = await _adapter.CheckSupportAsync(ct).ConfigureAwait(false);
        support.Validate();

        var supportBlockers = BuildSupportBlockers(support);
        if (supportBlockers.Count > 0)
        {
            return CreateReadiness(HonuaNativeArReadinessLevel.Unsupported, supportBlockers, []);
        }

        var status = await _adapter.StartSessionAsync(request, ct).ConfigureAwait(false);
        _activeRequest = request;
        _resumeAfterLifecyclePause = false;

        return EvaluateReadiness(status, request, _options);
    }

    /// <summary>
    /// Reads current native AR status and re-evaluates the UI readiness gate.
    /// </summary>
    public async ValueTask<HonuaNativeArReadiness> RefreshReadinessAsync(CancellationToken ct = default)
    {
        var request = _activeRequest
            ?? throw new InvalidOperationException("No native AR scene anchoring session is active.");

        var status = await _adapter.GetStatusAsync(ct).ConfigureAwait(false);
        return EvaluateReadiness(status, request, _options);
    }

    /// <summary>
    /// Pauses the active native AR session.
    /// </summary>
    public async ValueTask<HonuaNativeArReadiness> PauseAsync(CancellationToken ct = default)
    {
        var request = _activeRequest
            ?? throw new InvalidOperationException("No native AR scene anchoring session is active.");

        var status = await _adapter.PauseSessionAsync(ct).ConfigureAwait(false);
        return EvaluateReadiness(status, request, _options);
    }

    /// <summary>
    /// Resumes the active native AR session with the original scene anchoring request.
    /// </summary>
    public async ValueTask<HonuaNativeArReadiness> ResumeAsync(CancellationToken ct = default)
    {
        var request = _activeRequest
            ?? throw new InvalidOperationException("No native AR scene anchoring session is active.");

        var status = await _adapter.ResumeSessionAsync(request, ct).ConfigureAwait(false);
        _resumeAfterLifecyclePause = false;
        return EvaluateReadiness(status, request, _options);
    }

    /// <summary>
    /// Stops the active native AR session and clears lifecycle resume state.
    /// </summary>
    public async ValueTask StopAsync(CancellationToken ct = default)
    {
        if (_activeRequest is null)
        {
            return;
        }

        await _adapter.StopSessionAsync(ct).ConfigureAwait(false);
        _activeRequest = null;
        _resumeAfterLifecyclePause = false;
    }

    /// <summary>
    /// Applies app lifecycle transitions to camera/sensor-heavy native AR sessions.
    /// </summary>
    public async ValueTask HandleAppLifecycleAsync(
        HonuaNativeArAppLifecycleEvent lifecycleEvent,
        CancellationToken ct = default)
    {
        if (_activeRequest is null)
        {
            return;
        }

        switch (lifecycleEvent)
        {
            case HonuaNativeArAppLifecycleEvent.EnteringBackground:
                var status = await _adapter.GetStatusAsync(ct).ConfigureAwait(false);
                status.Validate();
                if (status.State is HonuaNativeArSessionState.Running or HonuaNativeArSessionState.Starting)
                {
                    _resumeAfterLifecyclePause = true;
                    await _adapter.PauseSessionAsync(ct).ConfigureAwait(false);
                }

                break;
            case HonuaNativeArAppLifecycleEvent.ResumingForeground:
                if (_resumeAfterLifecyclePause)
                {
                    await _adapter.ResumeSessionAsync(_activeRequest, ct).ConfigureAwait(false);
                    _resumeAfterLifecyclePause = false;
                }

                break;
            case HonuaNativeArAppLifecycleEvent.Stopping:
                await StopAsync(ct).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lifecycleEvent), lifecycleEvent, "Unsupported AR lifecycle event.");
        }
    }

    /// <summary>
    /// Evaluates whether the current native AR session can render, capture, or enable precision tools.
    /// </summary>
    public static HonuaNativeArReadiness EvaluateReadiness(
        HonuaNativeArSessionStatus status,
        HonuaNativeArSceneAnchorRequest request,
        HonuaNativeArSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(request);

        options ??= new HonuaNativeArSessionOptions();
        options.Validate();
        request.Validate();
        status.Validate();

        var blockers = new List<string>();
        var warnings = new List<string>(status.Warnings);

        AddSessionBlockers(status, blockers);
        AddSceneBlockers(status, request, blockers);
        AddOfflineBlockers(status, request, blockers);

        var horizontalAccuracy = status.Accuracy.HorizontalAccuracyMeters;
        if (horizontalAccuracy is null)
        {
            blockers.Add("Horizontal accuracy is not available.");
        }
        else if (horizontalAccuracy.Value > options.CoarsePreviewHorizontalAccuracyMeters)
        {
            blockers.Add(
                $"Horizontal accuracy {horizontalAccuracy.Value:0.##} m is above the coarse preview threshold.");
        }

        if (blockers.Count > 0)
        {
            return CreateReadiness(HonuaNativeArReadinessLevel.Blocked, blockers, warnings);
        }

        var level = HonuaNativeArReadinessLevel.CoarsePreview;

        if (status.TrackingState == HonuaNativeArTrackingState.Limited)
        {
            warnings.Add("AR tracking is limited; keep overlays in coarse preview.");
        }

        if (status.ActiveAnchoringMode == HonuaNativeArAnchoringMode.GpsPreview)
        {
            warnings.Add("GPS-only anchoring is coarse preview only.");
        }

        if (CanEnterSiteReview(status, options, warnings))
        {
            level = HonuaNativeArReadinessLevel.SiteReview;
        }

        if (level == HonuaNativeArReadinessLevel.SiteReview
            && CanEnterPrecisionInspection(status, request, options, warnings))
        {
            level = HonuaNativeArReadinessLevel.PrecisionInspection;
        }

        return CreateReadiness(level, blockers, warnings);
    }

    private static List<string> BuildSupportBlockers(HonuaNativeArCapabilityStatus support)
    {
        var blockers = new List<string>(support.MissingRequirements);

        if (!support.IsSupported)
        {
            blockers.Add("Native AR is not supported on this device.");
        }

        if (!support.CameraPermissionGranted)
        {
            blockers.Add("Camera permission is required for native AR anchoring.");
        }

        if (!support.LocationPermissionGranted)
        {
            blockers.Add("Location permission is required for scene anchoring.");
        }

        if (!support.MotionTrackingAvailable)
        {
            blockers.Add("Visual-inertial motion tracking is required for native AR anchoring.");
        }

        return blockers
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void AddSessionBlockers(HonuaNativeArSessionStatus status, List<string> blockers)
    {
        if (status.State != HonuaNativeArSessionState.Running)
        {
            blockers.Add($"AR session is {status.State}.");
        }

        if (status.TrackingState is HonuaNativeArTrackingState.Unsupported or HonuaNativeArTrackingState.Failed)
        {
            blockers.Add("AR tracking is unavailable.");
        }
    }

    private static void AddSceneBlockers(
        HonuaNativeArSessionStatus status,
        HonuaNativeArSceneAnchorRequest request,
        List<string> blockers)
    {
        if (!string.IsNullOrWhiteSpace(status.SceneId)
            && !string.Equals(status.SceneId, request.SceneId, StringComparison.Ordinal))
        {
            blockers.Add("AR session is not rendering the requested scene.");
        }

        if (!string.IsNullOrWhiteSpace(status.SceneRevision)
            && !string.IsNullOrWhiteSpace(request.SceneRevision)
            && !string.Equals(status.SceneRevision, request.SceneRevision, StringComparison.Ordinal))
        {
            blockers.Add("AR session is not using the requested scene revision.");
        }
    }

    private static void AddOfflineBlockers(
        HonuaNativeArSessionStatus status,
        HonuaNativeArSceneAnchorRequest request,
        List<string> blockers)
    {
        if (!request.IsOffline)
        {
            return;
        }

        if (status.PackageState != HonuaNativeArScenePackageState.Valid)
        {
            blockers.Add($"Offline scene package is {status.PackageState}.");
        }

        if (!string.IsNullOrWhiteSpace(status.PackageId)
            && !string.Equals(status.PackageId, request.PackageId, StringComparison.Ordinal))
        {
            blockers.Add("AR session is not using the requested offline scene package.");
        }
    }

    private static bool CanEnterSiteReview(
        HonuaNativeArSessionStatus status,
        HonuaNativeArSessionOptions options,
        List<string> warnings)
    {
        if (status.TrackingState != HonuaNativeArTrackingState.Tracking)
        {
            return false;
        }

        if (status.ActiveAnchoringMode == HonuaNativeArAnchoringMode.GpsPreview
            || status.ActiveAnchoringMode == HonuaNativeArAnchoringMode.VisualInertial
            || status.ActiveAnchoringMode == HonuaNativeArAnchoringMode.Unknown)
        {
            return false;
        }

        if (status.Accuracy.HorizontalAccuracyMeters > options.SiteReviewHorizontalAccuracyMeters)
        {
            warnings.Add("Horizontal accuracy is not sufficient for site review.");
            return false;
        }

        var yawAccuracy = status.Accuracy.YawAccuracyDegrees;
        if (yawAccuracy is null)
        {
            warnings.Add("Yaw accuracy is not available for site review.");
            return false;
        }

        if (yawAccuracy.Value > options.SiteReviewYawAccuracyDegrees)
        {
            warnings.Add("Yaw accuracy is not sufficient for site review.");
            return false;
        }

        if (status.ActiveAnchoringMode == HonuaNativeArAnchoringMode.ControlPointCalibration
            && status.ConfirmedControlPointCount < options.SiteReviewControlPointCount)
        {
            warnings.Add("At least one confirmed control point is required for site review.");
            return false;
        }

        return true;
    }

    private static bool CanEnterPrecisionInspection(
        HonuaNativeArSessionStatus status,
        HonuaNativeArSceneAnchorRequest request,
        HonuaNativeArSessionOptions options,
        List<string> warnings)
    {
        if (status.ActiveAnchoringMode == HonuaNativeArAnchoringMode.GpsPreview
            || status.ActiveAnchoringMode == HonuaNativeArAnchoringMode.VisualInertial)
        {
            return false;
        }

        if (!request.SourceDeclaresSurveyQuality)
        {
            warnings.Add("Precision tools require source data that declares survey quality.");
            return false;
        }

        var requiredControlPoints = request.RequiresVerticalPlacement
            ? options.PrecisionVerticalControlPointCount
            : options.PrecisionHorizontalControlPointCount;

        if (status.ConfirmedControlPointCount < requiredControlPoints)
        {
            warnings.Add("Precision tools require additional confirmed control points.");
            return false;
        }

        if (status.Accuracy.CalibrationResidualMeters is null)
        {
            warnings.Add("Precision tools require a calibration residual.");
            return false;
        }

        if (status.Accuracy.CalibrationResidualMeters.Value > options.PrecisionCalibrationResidualMeters)
        {
            warnings.Add("Calibration residual is above the precision threshold.");
            return false;
        }

        return true;
    }

    private static HonuaNativeArReadiness CreateReadiness(
        HonuaNativeArReadinessLevel level,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings)
    {
        return new HonuaNativeArReadiness
        {
            Level = level,
            Blockers = blockers,
            Warnings = warnings
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        };
    }
}
