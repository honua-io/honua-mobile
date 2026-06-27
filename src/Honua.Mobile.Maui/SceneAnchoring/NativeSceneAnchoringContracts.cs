// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

namespace Honua.Mobile.Maui.SceneAnchoring;

/// <summary>
/// Native AR runtime family owned by the app host.
/// </summary>
public enum HonuaNativeArRuntime
{
    Unknown,
    AndroidArCore,
    IosArKit,
}

/// <summary>
/// Anchor source currently used by a native AR session.
/// </summary>
public enum HonuaNativeArAnchoringMode
{
    Unknown,
    GpsPreview,
    VisualInertial,
    PlatformGeospatial,
    ControlPointCalibration,
    AssetAnchor,
}

/// <summary>
/// Native AR session lifecycle state reported by the platform adapter.
/// </summary>
public enum HonuaNativeArSessionState
{
    NotStarted,
    Starting,
    Running,
    Paused,
    Stopped,
    Failed,
}

/// <summary>
/// Native camera-pose tracking quality reported by the platform adapter.
/// </summary>
public enum HonuaNativeArTrackingState
{
    Unknown,
    Unsupported,
    Initializing,
    Limited,
    Tracking,
    Paused,
    Failed,
}

/// <summary>
/// Local package quality state for AR overlays that render disconnected scene content.
/// </summary>
public enum HonuaNativeArScenePackageState
{
    NotRequired,
    Unknown,
    Valid,
    Stale,
    Expired,
    Partial,
    Revoked,
}

/// <summary>
/// Readiness level the mobile UI can use to gate AR overlay actions.
/// </summary>
public enum HonuaNativeArReadinessLevel
{
    Unsupported,
    Blocked,
    CoarsePreview,
    SiteReview,
    PrecisionInspection,
}

/// <summary>
/// Mobile app lifecycle event relevant to native camera and sensor sessions.
/// </summary>
public enum HonuaNativeArAppLifecycleEvent
{
    EnteringBackground,
    ResumingForeground,
    Stopping,
}

/// <summary>
/// Workflow thresholds used by the mobile AR readiness policy.
/// </summary>
public sealed record HonuaNativeArSessionOptions
{
    public double CoarsePreviewHorizontalAccuracyMeters { get; init; } = 10;

    public double SiteReviewHorizontalAccuracyMeters { get; init; } = 2;

    public double SiteReviewYawAccuracyDegrees { get; init; } = 15;

    public double PrecisionCalibrationResidualMeters { get; init; } = 0.5;

    public int SiteReviewControlPointCount { get; init; } = 1;

    public int PrecisionHorizontalControlPointCount { get; init; } = 2;

    public int PrecisionVerticalControlPointCount { get; init; } = 3;

    public void Validate()
    {
        ValidatePositive(CoarsePreviewHorizontalAccuracyMeters, nameof(CoarsePreviewHorizontalAccuracyMeters));
        ValidatePositive(SiteReviewHorizontalAccuracyMeters, nameof(SiteReviewHorizontalAccuracyMeters));
        ValidatePositive(SiteReviewYawAccuracyDegrees, nameof(SiteReviewYawAccuracyDegrees));
        ValidatePositive(PrecisionCalibrationResidualMeters, nameof(PrecisionCalibrationResidualMeters));

        if (SiteReviewControlPointCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SiteReviewControlPointCount),
                "Site review control point count must be non-negative.");
        }

        if (PrecisionHorizontalControlPointCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PrecisionHorizontalControlPointCount),
                "Precision horizontal control point count must be positive.");
        }

        if (PrecisionVerticalControlPointCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PrecisionVerticalControlPointCount),
                "Precision vertical control point count must be positive.");
        }
    }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Thresholds must be finite and positive.");
        }
    }
}

/// <summary>
/// Scene/runtime request handed to a native AR platform adapter.
/// </summary>
public sealed record HonuaNativeArSceneAnchorRequest
{
    public required string SceneId { get; init; }

    public string? SceneRevision { get; init; }

    public string? PackageId { get; init; }

    public bool IsOffline { get; init; }

    public bool RequiresVerticalPlacement { get; init; }

    public bool SourceDeclaresSurveyQuality { get; init; }

    public HonuaNativeArAnchoringMode PreferredAnchoringMode { get; init; } =
        HonuaNativeArAnchoringMode.PlatformGeospatial;

    public IReadOnlyList<string> ControlPointIds { get; init; } = [];

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } =
        new Dictionary<string, object?>();

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SceneId);
        ArgumentNullException.ThrowIfNull(ControlPointIds);
        ArgumentNullException.ThrowIfNull(Metadata);

        if (IsOffline)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(PackageId);
        }

        var controlPointIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ControlPointIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (!controlPointIds.Add(id))
            {
                throw new InvalidOperationException($"Control point '{id}' is defined more than once.");
            }
        }
    }
}

/// <summary>
/// Accuracy values reported by ARKit, ARCore, device location, or a calibration flow.
/// </summary>
public sealed record HonuaNativeArAccuracySample
{
    public double? HorizontalAccuracyMeters { get; init; }

    public double? VerticalAccuracyMeters { get; init; }

    public double? YawAccuracyDegrees { get; init; }

    public double? CalibrationResidualMeters { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public void Validate()
    {
        ValidateNonNegative(HorizontalAccuracyMeters, nameof(HorizontalAccuracyMeters));
        ValidateNonNegative(VerticalAccuracyMeters, nameof(VerticalAccuracyMeters));
        ValidateNonNegative(YawAccuracyDegrees, nameof(YawAccuracyDegrees));
        ValidateNonNegative(CalibrationResidualMeters, nameof(CalibrationResidualMeters));
    }

    private static void ValidateNonNegative(double? value, string parameterName)
    {
        if (value is null)
        {
            return;
        }

        if (!double.IsFinite(value.Value) || value.Value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Accuracy values must be finite and non-negative.");
        }
    }
}

/// <summary>
/// Device and permission support state reported before starting a native AR session.
/// </summary>
public sealed record HonuaNativeArCapabilityStatus
{
    public HonuaNativeArRuntime Runtime { get; init; } = HonuaNativeArRuntime.Unknown;

    public bool IsSupported { get; init; }

    public bool CameraPermissionGranted { get; init; }

    public bool LocationPermissionGranted { get; init; }

    public bool PreciseLocationGranted { get; init; }

    public bool MotionTrackingAvailable { get; init; }

    public bool PlatformGeospatialAvailable { get; init; }

    public bool DepthAvailable { get; init; }

    public IReadOnlyList<string> MissingRequirements { get; init; } = [];

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(MissingRequirements);
        foreach (var requirement in MissingRequirements)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(requirement);
        }
    }
}

/// <summary>
/// Native AR session status after the platform adapter has started or updated the session.
/// </summary>
public sealed record HonuaNativeArSessionStatus
{
    public HonuaNativeArRuntime Runtime { get; init; } = HonuaNativeArRuntime.Unknown;

    public HonuaNativeArSessionState State { get; init; } = HonuaNativeArSessionState.NotStarted;

    public HonuaNativeArTrackingState TrackingState { get; init; } = HonuaNativeArTrackingState.Unknown;

    public HonuaNativeArAnchoringMode ActiveAnchoringMode { get; init; } = HonuaNativeArAnchoringMode.Unknown;

    public HonuaNativeArAccuracySample Accuracy { get; init; } = new();

    public HonuaNativeArScenePackageState PackageState { get; init; } = HonuaNativeArScenePackageState.NotRequired;

    public int ConfirmedControlPointCount { get; init; }

    public bool IsOnline { get; init; } = true;

    public string? SceneId { get; init; }

    public string? SceneRevision { get; init; }

    public string? PackageId { get; init; }

    public string? DeviceModel { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Accuracy);
        Accuracy.Validate();

        ArgumentNullException.ThrowIfNull(Warnings);
        foreach (var warning in Warnings)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(warning);
        }

        if (DeviceModel is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(DeviceModel);
        }

        if (ConfirmedControlPointCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConfirmedControlPointCount),
                "Confirmed control point count must be non-negative.");
        }
    }
}

/// <summary>
/// Mobile UI gate derived from native AR status and workflow thresholds.
/// </summary>
public sealed record HonuaNativeArReadiness
{
    public HonuaNativeArReadinessLevel Level { get; init; }

    public IReadOnlyList<string> Blockers { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool CanRenderOverlay => Level >= HonuaNativeArReadinessLevel.CoarsePreview;

    public bool CanCaptureEvidence => Level >= HonuaNativeArReadinessLevel.SiteReview;

    public bool CanUsePrecisionTools => Level == HonuaNativeArReadinessLevel.PrecisionInspection;
}

/// <summary>
/// Mobile-owned AR evidence metadata attached to field photos, annotations, or reports.
/// </summary>
public sealed record HonuaNativeArEvidenceContext
{
    public required string SceneId { get; init; }

    public string? SceneRevision { get; init; }

    public string? PackageId { get; init; }

    public bool IsOffline { get; init; }

    public bool IsOnline { get; init; }

    public HonuaNativeArRuntime Runtime { get; init; } = HonuaNativeArRuntime.Unknown;

    public string? DeviceModel { get; init; }

    public HonuaNativeArReadinessLevel ReadinessLevel { get; init; }

    public HonuaNativeArAnchoringMode ActiveAnchoringMode { get; init; } = HonuaNativeArAnchoringMode.Unknown;

    public HonuaNativeArScenePackageState PackageState { get; init; } = HonuaNativeArScenePackageState.NotRequired;

    public double? HorizontalAccuracyMeters { get; init; }

    public double? VerticalAccuracyMeters { get; init; }

    public double? YawAccuracyDegrees { get; init; }

    public double? CalibrationResidualMeters { get; init; }

    public int ConfirmedControlPointCount { get; init; }

    public IReadOnlyList<string> ControlPointIds { get; init; } = [];

    public IReadOnlyList<string> Blockers { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool CanAttachToFieldEvidence => ReadinessLevel >= HonuaNativeArReadinessLevel.SiteReview;

    public bool CanAttachToPrecisionEvidence => ReadinessLevel == HonuaNativeArReadinessLevel.PrecisionInspection;

    public static HonuaNativeArEvidenceContext Create(
        HonuaNativeArSceneAnchorRequest request,
        HonuaNativeArSessionStatus status,
        HonuaNativeArReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(readiness);
        request.Validate();
        status.Validate();

        ArgumentNullException.ThrowIfNull(readiness.Blockers);
        ArgumentNullException.ThrowIfNull(readiness.Warnings);

        return new HonuaNativeArEvidenceContext
        {
            SceneId = status.SceneId ?? request.SceneId,
            SceneRevision = status.SceneRevision ?? request.SceneRevision,
            PackageId = status.PackageId ?? request.PackageId,
            IsOffline = request.IsOffline,
            IsOnline = status.IsOnline,
            Runtime = status.Runtime,
            DeviceModel = status.DeviceModel,
            ReadinessLevel = readiness.Level,
            ActiveAnchoringMode = status.ActiveAnchoringMode,
            PackageState = status.PackageState,
            HorizontalAccuracyMeters = status.Accuracy.HorizontalAccuracyMeters,
            VerticalAccuracyMeters = status.Accuracy.VerticalAccuracyMeters,
            YawAccuracyDegrees = status.Accuracy.YawAccuracyDegrees,
            CalibrationResidualMeters = status.Accuracy.CalibrationResidualMeters,
            ConfirmedControlPointCount = status.ConfirmedControlPointCount,
            ControlPointIds = request.ControlPointIds.ToArray(),
            Blockers = readiness.Blockers.ToArray(),
            Warnings = readiness.Warnings.ToArray(),
            CapturedAt = status.UpdatedAt,
        };
    }
}

/// <summary>
/// Platform adapter for native ARKit/ARCore scene anchoring.
/// </summary>
public interface IHonuaNativeArSceneAnchorAdapter
{
    HonuaNativeArRuntime Runtime { get; }

    ValueTask<HonuaNativeArCapabilityStatus> CheckSupportAsync(CancellationToken ct = default);

    ValueTask<HonuaNativeArSessionStatus> StartSessionAsync(
        HonuaNativeArSceneAnchorRequest request,
        CancellationToken ct = default);

    ValueTask<HonuaNativeArSessionStatus> GetStatusAsync(CancellationToken ct = default);

    ValueTask<HonuaNativeArSessionStatus> PauseSessionAsync(CancellationToken ct = default);

    ValueTask<HonuaNativeArSessionStatus> ResumeSessionAsync(
        HonuaNativeArSceneAnchorRequest request,
        CancellationToken ct = default);

    ValueTask StopSessionAsync(CancellationToken ct = default);
}
