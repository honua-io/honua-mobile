using Honua.Mobile.Maui;
using Honua.Mobile.Maui.SceneAnchoring;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.Maui.Tests;

public sealed class HonuaNativeSceneAnchoringTests
{
    [Fact]
    public async Task StartAsync_WhenPlatformSupported_StartsAdapterAndReturnsSiteReviewReadiness()
    {
        var adapter = new RecordingNativeArSceneAnchorAdapter
        {
            StartStatus = CreateStatus(
                activeAnchoringMode: HonuaNativeArAnchoringMode.PlatformGeospatial,
                horizontalAccuracyMeters: 1.2,
                yawAccuracyDegrees: 8),
        };
        var controller = new HonuaNativeArSceneAnchoringController(adapter);
        var request = CreateRequest();

        var readiness = await controller.StartAsync(request);

        Assert.Equal(HonuaNativeArReadinessLevel.SiteReview, readiness.Level);
        Assert.True(readiness.CanRenderOverlay);
        Assert.True(readiness.CanCaptureEvidence);
        Assert.False(readiness.CanUsePrecisionTools);
        Assert.Same(request, Assert.Single(adapter.StartRequests));
    }

    [Fact]
    public async Task StartAsync_WhenPlatformUnsupported_DoesNotStartAdapter()
    {
        var adapter = new RecordingNativeArSceneAnchorAdapter
        {
            Support = new HonuaNativeArCapabilityStatus
            {
                Runtime = HonuaNativeArRuntime.AndroidArCore,
                IsSupported = false,
                CameraPermissionGranted = true,
                LocationPermissionGranted = true,
                MotionTrackingAvailable = true,
                MissingRequirements = ["ARCore geospatial mode is unavailable"],
            },
        };
        var controller = new HonuaNativeArSceneAnchoringController(adapter);

        var readiness = await controller.StartAsync(CreateRequest());

        Assert.Equal(HonuaNativeArReadinessLevel.Unsupported, readiness.Level);
        Assert.Contains(readiness.Blockers, blocker => blocker.Contains("ARCore geospatial", StringComparison.Ordinal));
        Assert.Empty(adapter.StartRequests);
    }

    [Fact]
    public async Task StartAsync_WhenAdapterStartFails_DoesNotLeaveActiveSession()
    {
        var adapter = new RecordingNativeArSceneAnchorAdapter
        {
            StartException = new ApplicationException("Native AR session failed to start."),
        };
        var controller = new HonuaNativeArSceneAnchoringController(adapter);

        await Assert.ThrowsAsync<ApplicationException>(async () => await controller.StartAsync(CreateRequest()));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await controller.RefreshReadinessAsync());
        Assert.Equal(0, adapter.GetStatusCalls);
    }

    [Fact]
    public void EvaluateReadiness_GpsOnlyAnchoringNeverEnablesPrecisionTools()
    {
        var request = CreateRequest(
            controlPointIds: ["cp-a", "cp-b", "cp-c"],
            sourceDeclaresSurveyQuality: true);
        var status = CreateStatus(
            activeAnchoringMode: HonuaNativeArAnchoringMode.GpsPreview,
            horizontalAccuracyMeters: 0.8,
            yawAccuracyDegrees: 5,
            calibrationResidualMeters: 0.2,
            confirmedControlPointCount: 3);

        var readiness = HonuaNativeArSceneAnchoringController.EvaluateReadiness(status, request);

        Assert.Equal(HonuaNativeArReadinessLevel.CoarsePreview, readiness.Level);
        Assert.True(readiness.CanRenderOverlay);
        Assert.False(readiness.CanCaptureEvidence);
        Assert.False(readiness.CanUsePrecisionTools);
        Assert.Contains(readiness.Warnings, warning => warning.Contains("GPS-only", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateReadiness_OfflinePackageMustBeValidBeforeRendering()
    {
        var request = CreateRequest(isOffline: true, packageId: "pkg-2026-05");
        var status = CreateStatus(
            packageState: HonuaNativeArScenePackageState.Expired,
            packageId: "pkg-2026-05",
            horizontalAccuracyMeters: 1);

        var readiness = HonuaNativeArSceneAnchoringController.EvaluateReadiness(status, request);

        Assert.Equal(HonuaNativeArReadinessLevel.Blocked, readiness.Level);
        Assert.False(readiness.CanRenderOverlay);
        Assert.Contains(readiness.Blockers, blocker => blocker.Contains("Expired", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateReadiness_MissingYawAccuracyDoesNotEnableSiteReview()
    {
        var readiness = HonuaNativeArSceneAnchoringController.EvaluateReadiness(
            CreateStatus(
                activeAnchoringMode: HonuaNativeArAnchoringMode.PlatformGeospatial,
                horizontalAccuracyMeters: 1,
                yawAccuracyDegrees: null),
            CreateRequest());

        Assert.Equal(HonuaNativeArReadinessLevel.CoarsePreview, readiness.Level);
        Assert.False(readiness.CanCaptureEvidence);
        Assert.Contains(readiness.Warnings, warning => warning.Contains("Yaw accuracy", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateReadiness_PrecisionRequiresSurveyQualityControlPointsAndResidual()
    {
        var request = CreateRequest(
            controlPointIds: ["cp-a", "cp-b"],
            sourceDeclaresSurveyQuality: true);
        var status = CreateStatus(
            activeAnchoringMode: HonuaNativeArAnchoringMode.ControlPointCalibration,
            horizontalAccuracyMeters: 0.5,
            yawAccuracyDegrees: 4,
            calibrationResidualMeters: 0.25,
            confirmedControlPointCount: 2);

        var readiness = HonuaNativeArSceneAnchoringController.EvaluateReadiness(status, request);

        Assert.Equal(HonuaNativeArReadinessLevel.PrecisionInspection, readiness.Level);
        Assert.True(readiness.CanUsePrecisionTools);
    }

    [Fact]
    public void EvaluateReadiness_VerticalPlacementRequiresThreeControlPointsForPrecision()
    {
        var request = CreateRequest(
            controlPointIds: ["cp-a", "cp-b"],
            requiresVerticalPlacement: true,
            sourceDeclaresSurveyQuality: true);
        var status = CreateStatus(
            activeAnchoringMode: HonuaNativeArAnchoringMode.ControlPointCalibration,
            horizontalAccuracyMeters: 0.5,
            yawAccuracyDegrees: 4,
            calibrationResidualMeters: 0.25,
            confirmedControlPointCount: 2);

        var readiness = HonuaNativeArSceneAnchoringController.EvaluateReadiness(status, request);

        Assert.Equal(HonuaNativeArReadinessLevel.SiteReview, readiness.Level);
        Assert.False(readiness.CanUsePrecisionTools);
        Assert.Contains(readiness.Warnings, warning => warning.Contains("additional confirmed control points", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAppLifecycleAsync_PausesResumesAndStopsNativeSession()
    {
        var adapter = new RecordingNativeArSceneAnchorAdapter
        {
            Status = CreateStatus(activeAnchoringMode: HonuaNativeArAnchoringMode.PlatformGeospatial),
            StartStatus = CreateStatus(activeAnchoringMode: HonuaNativeArAnchoringMode.PlatformGeospatial),
            PauseStatus = CreateStatus(
                state: HonuaNativeArSessionState.Paused,
                trackingState: HonuaNativeArTrackingState.Paused,
                activeAnchoringMode: HonuaNativeArAnchoringMode.PlatformGeospatial),
            ResumeStatus = CreateStatus(activeAnchoringMode: HonuaNativeArAnchoringMode.PlatformGeospatial),
        };
        var controller = new HonuaNativeArSceneAnchoringController(adapter);
        var request = CreateRequest();

        await controller.StartAsync(request);
        await controller.HandleAppLifecycleAsync(HonuaNativeArAppLifecycleEvent.EnteringBackground);
        await controller.HandleAppLifecycleAsync(HonuaNativeArAppLifecycleEvent.ResumingForeground);
        await controller.HandleAppLifecycleAsync(HonuaNativeArAppLifecycleEvent.Stopping);

        Assert.Equal(1, adapter.GetStatusCalls);
        Assert.Equal(1, adapter.PauseCalls);
        Assert.Same(request, Assert.Single(adapter.ResumeRequests));
        Assert.Equal(1, adapter.StopCalls);
    }

    [Fact]
    public void AddHonuaNativeSceneAnchoring_RegistersControllerAndOptions()
    {
        var options = new HonuaNativeArSessionOptions
        {
            CoarsePreviewHorizontalAccuracyMeters = 12,
        };

        using var provider = new ServiceCollection()
            .AddSingleton<IHonuaNativeArSceneAnchorAdapter, RecordingNativeArSceneAnchorAdapter>()
            .AddHonuaNativeSceneAnchoring(options)
            .BuildServiceProvider();

        Assert.Same(options, provider.GetRequiredService<HonuaNativeArSessionOptions>());
        Assert.NotNull(provider.GetRequiredService<HonuaNativeArSceneAnchoringController>());
    }

    private static HonuaNativeArSceneAnchorRequest CreateRequest(
        bool isOffline = false,
        string? packageId = null,
        bool requiresVerticalPlacement = false,
        bool sourceDeclaresSurveyQuality = false,
        IReadOnlyList<string>? controlPointIds = null)
        => new()
        {
            SceneId = "downtown-honolulu",
            SceneRevision = "scene-rev-42",
            PackageId = packageId,
            IsOffline = isOffline,
            RequiresVerticalPlacement = requiresVerticalPlacement,
            SourceDeclaresSurveyQuality = sourceDeclaresSurveyQuality,
            ControlPointIds = controlPointIds ?? [],
        };

    private static HonuaNativeArSessionStatus CreateStatus(
        HonuaNativeArSessionState state = HonuaNativeArSessionState.Running,
        HonuaNativeArTrackingState trackingState = HonuaNativeArTrackingState.Tracking,
        HonuaNativeArAnchoringMode activeAnchoringMode = HonuaNativeArAnchoringMode.PlatformGeospatial,
        HonuaNativeArScenePackageState packageState = HonuaNativeArScenePackageState.NotRequired,
        string? packageId = null,
        double? horizontalAccuracyMeters = 1,
        double? yawAccuracyDegrees = null,
        double? calibrationResidualMeters = null,
        int confirmedControlPointCount = 0)
        => new()
        {
            Runtime = HonuaNativeArRuntime.AndroidArCore,
            State = state,
            TrackingState = trackingState,
            ActiveAnchoringMode = activeAnchoringMode,
            SceneId = "downtown-honolulu",
            SceneRevision = "scene-rev-42",
            PackageId = packageId,
            PackageState = packageState,
            ConfirmedControlPointCount = confirmedControlPointCount,
            Accuracy = new HonuaNativeArAccuracySample
            {
                HorizontalAccuracyMeters = horizontalAccuracyMeters,
                YawAccuracyDegrees = yawAccuracyDegrees,
                CalibrationResidualMeters = calibrationResidualMeters,
            },
        };

    private sealed class RecordingNativeArSceneAnchorAdapter : IHonuaNativeArSceneAnchorAdapter
    {
        public HonuaNativeArRuntime Runtime => HonuaNativeArRuntime.AndroidArCore;

        public HonuaNativeArCapabilityStatus Support { get; init; } = new()
        {
            Runtime = HonuaNativeArRuntime.AndroidArCore,
            IsSupported = true,
            CameraPermissionGranted = true,
            LocationPermissionGranted = true,
            PreciseLocationGranted = true,
            MotionTrackingAvailable = true,
            PlatformGeospatialAvailable = true,
        };

        public HonuaNativeArSessionStatus Status { get; init; } = CreateStatus();

        public HonuaNativeArSessionStatus StartStatus { get; init; } = CreateStatus();

        public Exception? StartException { get; init; }

        public HonuaNativeArSessionStatus PauseStatus { get; init; } = CreateStatus(
            state: HonuaNativeArSessionState.Paused,
            trackingState: HonuaNativeArTrackingState.Paused);

        public HonuaNativeArSessionStatus ResumeStatus { get; init; } = CreateStatus();

        public List<HonuaNativeArSceneAnchorRequest> StartRequests { get; } = [];

        public List<HonuaNativeArSceneAnchorRequest> ResumeRequests { get; } = [];

        public int GetStatusCalls { get; private set; }

        public int PauseCalls { get; private set; }

        public int StopCalls { get; private set; }

        public ValueTask<HonuaNativeArCapabilityStatus> CheckSupportAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Support);

        public ValueTask<HonuaNativeArSessionStatus> StartSessionAsync(
            HonuaNativeArSceneAnchorRequest request,
            CancellationToken ct = default)
        {
            if (StartException is not null)
            {
                throw StartException;
            }

            StartRequests.Add(request);
            return ValueTask.FromResult(StartStatus);
        }

        public ValueTask<HonuaNativeArSessionStatus> GetStatusAsync(CancellationToken ct = default)
        {
            GetStatusCalls++;
            return ValueTask.FromResult(Status);
        }

        public ValueTask<HonuaNativeArSessionStatus> PauseSessionAsync(CancellationToken ct = default)
        {
            PauseCalls++;
            return ValueTask.FromResult(PauseStatus);
        }

        public ValueTask<HonuaNativeArSessionStatus> ResumeSessionAsync(
            HonuaNativeArSceneAnchorRequest request,
            CancellationToken ct = default)
        {
            ResumeRequests.Add(request);
            return ValueTask.FromResult(ResumeStatus);
        }

        public ValueTask StopSessionAsync(CancellationToken ct = default)
        {
            StopCalls++;
            return ValueTask.CompletedTask;
        }
    }
}
