using Honua.Mobile.Maui.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using SdkGeofenceDefinition = Honua.Sdk.Geometry.HonuaGeofenceDefinition;
using SdkGeofenceStatus = Honua.Sdk.Geometry.HonuaGeofenceStatus;
using SdkGeofenceTransition = Honua.Sdk.Geometry.HonuaGeofenceTransition;

namespace Honua.Mobile.Maui.Location;

/// <summary>
/// SDK-backed geofence workflow request translated into mobile-owned platform monitoring.
/// </summary>
public sealed record HonuaSdkGeofenceWorkflowRequest
{
    public IReadOnlyList<SdkGeofenceDefinition> Definitions { get; init; } = [];

    public HonuaBackgroundLocationOptions? BackgroundUpdates { get; init; }

    public HonuaLocationAccess RequiredAccess { get; init; } = HonuaLocationAccess.Background;

    public bool ReplaceExisting { get; init; } = true;

    public bool AllowBatterySaverDeferral { get; init; } = true;

    public TimeSpan MinimumBackgroundInterval { get; init; } = TimeSpan.FromMinutes(5);

    public double MinimumNativeRadiusMeters { get; init; } = 25;

    public string Purpose { get; init; } = "Honua geofence workflow";

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(Purpose);

        if (Definitions.Count == 0)
        {
            throw new ArgumentException("At least one SDK geofence definition is required.", nameof(Definitions));
        }

        if (MinimumBackgroundInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumBackgroundInterval),
                "Minimum background interval must be positive.");
        }

        if (MinimumNativeRadiusMeters <= 0 ||
            double.IsNaN(MinimumNativeRadiusMeters) ||
            double.IsInfinity(MinimumNativeRadiusMeters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumNativeRadiusMeters),
                "Minimum native radius must be finite and positive.");
        }

        BackgroundUpdates?.Validate();

        var geofenceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.GeofenceId);

            if (!geofenceIds.Add(definition.GeofenceId))
            {
                throw new InvalidOperationException($"SDK geofence '{definition.GeofenceId}' is defined more than once.");
            }

            if (definition.Geometry is null || definition.Geometry.IsEmpty)
            {
                throw new ArgumentException(
                    $"SDK geofence '{definition.GeofenceId}' does not include a geometry.",
                    nameof(Definitions));
            }

            if (definition.BufferDistance is < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Definitions),
                    $"SDK geofence '{definition.GeofenceId}' has a negative buffer distance.");
            }

            if (definition.ProximityDistance is < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Definitions),
                    $"SDK geofence '{definition.GeofenceId}' has a negative proximity distance.");
            }
        }
    }
}

/// <summary>
/// Native region binding for an SDK geofence definition.
/// </summary>
public sealed record HonuaSdkGeofenceRegionBinding
{
    public required string RegionId { get; init; }

    public required string GeofenceId { get; init; }

    public required SdkGeofenceDefinition Definition { get; init; }

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}

/// <summary>
/// Native geofence monitoring plan generated from SDK-owned geofence definitions.
/// </summary>
public sealed record HonuaSdkGeofenceMonitoringPlan
{
    public required HonuaGeofenceMonitoringRequest NativeRequest { get; init; }

    public required IReadOnlyDictionary<string, HonuaSdkGeofenceRegionBinding> RegionBindings { get; init; }
}

/// <summary>
/// Mobile workflow event emitted after a native geofence transition maps back to an SDK geofence definition.
/// </summary>
public sealed record HonuaGeofenceWorkflowEvent
{
    public required string GeofenceId { get; init; }

    public required string RegionId { get; init; }

    public required HonuaGeofenceTransitionKind NativeTransition { get; init; }

    public required SdkGeofenceTransition SdkTransition { get; init; }

    public required SdkGeofenceStatus SdkStatus { get; init; }

    public HonuaDeviceLocation? Location { get; init; }

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}

/// <summary>
/// Receives mobile geofence workflow events for field UX, local persistence, or sync queue integration.
/// </summary>
public interface IHonuaGeofenceWorkflowEventSink
{
    ValueTask EnqueueAsync(HonuaGeofenceWorkflowEvent workflowEvent, CancellationToken ct = default);
}

/// <summary>
/// Bridges SDK geofence definitions to mobile-owned native geofence lifecycle and workflow events.
/// </summary>
public sealed class HonuaSdkGeofenceWorkflowController : IDisposable, IAsyncDisposable
{
    private const double MetersPerDegreeLatitude = 111_320d;

    private readonly HonuaDeviceLocationCoordinator _locations;
    private readonly HonuaBackgroundLocationLifecycleController _lifecycle;
    private readonly IReadOnlyList<IHonuaGeofenceWorkflowEventSink> _eventSinks;
    private readonly ILogger<HonuaSdkGeofenceWorkflowController> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<string, HonuaSdkGeofenceRegionBinding> _regionBindings =
        new Dictionary<string, HonuaSdkGeofenceRegionBinding>(StringComparer.Ordinal);
    private bool _disposed;

    public HonuaSdkGeofenceWorkflowController(
        HonuaDeviceLocationCoordinator locations,
        HonuaBackgroundLocationLifecycleController lifecycle,
        IEnumerable<IHonuaGeofenceWorkflowEventSink> eventSinks,
        ILogger<HonuaSdkGeofenceWorkflowController>? logger = null)
    {
        _locations = locations ?? throw new ArgumentNullException(nameof(locations));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _eventSinks = eventSinks?.ToArray() ?? [];
        _logger = logger ?? NullLogger<HonuaSdkGeofenceWorkflowController>.Instance;

        _locations.GeofenceTransitioned += OnGeofenceTransitioned;
    }

    public event EventHandler<HonuaGeofenceWorkflowEvent>? WorkflowEventEmitted;

    public HonuaBackgroundLocationRuntimeState State => _lifecycle.State;

    public async ValueTask StartAsync(
        HonuaSdkGeofenceWorkflowRequest request,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var plan = CreateMonitoringPlan(request);
        var lifecycleRequest = new HonuaBackgroundLocationLifecycleRequest
        {
            BackgroundUpdates = CreateBackgroundOptions(request),
            Geofences = plan.NativeRequest,
            AllowBatterySaverDeferral = request.AllowBatterySaverDeferral,
            Metadata = request.Metadata,
        };

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _regionBindings = plan.RegionBindings;
            try
            {
                await _lifecycle.StartAsync(lifecycleRequest, ct).ConfigureAwait(false);
            }
            catch
            {
                _regionBindings = new Dictionary<string, HonuaSdkGeofenceRegionBinding>(StringComparer.Ordinal);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask StopAsync(
        HonuaBackgroundLocationStopReason reason = HonuaBackgroundLocationStopReason.UserStopped,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _lifecycle.StopAsync(reason, ct).ConfigureAwait(false);
            _regionBindings = new Dictionary<string, HonuaSdkGeofenceRegionBinding>(StringComparer.Ordinal);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask HandleLifecycleEventAsync(
        HonuaLocationLifecycleEvent lifecycleEvent,
        CancellationToken ct = default)
    {
        await _lifecycle.HandleLifecycleEventAsync(lifecycleEvent, ct).ConfigureAwait(false);

        if (lifecycleEvent is HonuaLocationLifecycleEvent.Suspended or HonuaLocationLifecycleEvent.Shutdown)
        {
            _regionBindings = new Dictionary<string, HonuaSdkGeofenceRegionBinding>(StringComparer.Ordinal);
        }
    }

    public static HonuaSdkGeofenceMonitoringPlan CreateMonitoringPlan(HonuaSdkGeofenceWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var regions = new List<HonuaGeofenceRegion>(request.Definitions.Count);
        var bindings = new Dictionary<string, HonuaSdkGeofenceRegionBinding>(StringComparer.Ordinal);

        foreach (var definition in request.Definitions)
        {
            var regionId = definition.GeofenceId;
            var center = GetEnvelopeCenter(definition.Geometry);
            var radius = EstimateNativeRadiusMeters(definition, center, request.MinimumNativeRadiusMeters);
            var metadata = BuildRegionMetadata(request, definition, regionId);

            regions.Add(new HonuaGeofenceRegion
            {
                Id = regionId,
                Center = center,
                RadiusMeters = radius,
                NotifyOnEntry = true,
                NotifyOnExit = true,
                Metadata = metadata,
            });

            bindings[regionId] = new HonuaSdkGeofenceRegionBinding
            {
                RegionId = regionId,
                GeofenceId = definition.GeofenceId,
                Definition = definition,
                Metadata = metadata,
            };
        }

        return new HonuaSdkGeofenceMonitoringPlan
        {
            NativeRequest = new HonuaGeofenceMonitoringRequest
            {
                Regions = regions,
                RequiredAccess = request.RequiredAccess,
                ReplaceExisting = request.ReplaceExisting,
            },
            RegionBindings = bindings,
        };
    }

    public async ValueTask PublishTransitionAsync(
        HonuaGeofenceTransition transition,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        transition.Validate();

        if (!_regionBindings.TryGetValue(transition.RegionId, out var binding))
        {
            _logger.LogDebug("Ignoring native geofence transition for unknown region {RegionId}.", transition.RegionId);
            return;
        }

        var workflowEvent = new HonuaGeofenceWorkflowEvent
        {
            GeofenceId = binding.GeofenceId,
            RegionId = transition.RegionId,
            NativeTransition = transition.Kind,
            SdkTransition = ToSdkTransition(transition.Kind),
            SdkStatus = ToSdkStatus(transition.Kind),
            Location = transition.Location,
            OccurredAt = transition.OccurredAt,
            Metadata = BuildEventMetadata(binding, transition),
        };

        WorkflowEventEmitted?.Invoke(this, workflowEvent);

        foreach (var sink in _eventSinks)
        {
            await sink.EnqueueAsync(workflowEvent, ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _locations.GeofenceTransitioned -= OnGeofenceTransitioned;
        await StopAsync(HonuaBackgroundLocationStopReason.Shutdown).ConfigureAwait(false);
        _gate.Dispose();
        _disposed = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _locations.GeofenceTransitioned -= OnGeofenceTransitioned;
        StopAsync(HonuaBackgroundLocationStopReason.Shutdown).AsTask().GetAwaiter().GetResult();
        _gate.Dispose();
        _disposed = true;
    }

    private void OnGeofenceTransitioned(object? sender, HonuaGeofenceTransition transition)
    {
        try
        {
            PublishTransitionAsync(transition).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish geofence workflow transition for region {RegionId}.", transition.RegionId);
        }
    }

    private static HonuaBackgroundLocationOptions? CreateBackgroundOptions(HonuaSdkGeofenceWorkflowRequest request)
    {
        if (request.BackgroundUpdates is null)
        {
            return null;
        }

        var options = request.BackgroundUpdates;
        return options with
        {
            MinimumInterval = options.MinimumInterval < request.MinimumBackgroundInterval
                ? request.MinimumBackgroundInterval
                : options.MinimumInterval,
            AllowBatterySaverDeferral = options.AllowBatterySaverDeferral && request.AllowBatterySaverDeferral,
            Purpose = string.IsNullOrWhiteSpace(options.Purpose) ? request.Purpose : options.Purpose,
        };
    }

    private static HonuaMapCoordinate GetEnvelopeCenter(Geometry geometry)
    {
        var envelope = geometry.EnvelopeInternal;
        if (envelope.IsNull)
        {
            throw new ArgumentException("SDK geofence geometry envelope cannot be empty.", nameof(geometry));
        }

        return new HonuaMapCoordinate(
            (envelope.MinY + envelope.MaxY) / 2,
            (envelope.MinX + envelope.MaxX) / 2);
    }

    private static double EstimateNativeRadiusMeters(
        SdkGeofenceDefinition definition,
        HonuaMapCoordinate center,
        double minimumNativeRadiusMeters)
    {
        var envelope = definition.Geometry.EnvelopeInternal;
        var envelopeRadius = Math.Max(
            EstimateDistanceMeters(center, envelope.MinY, envelope.MinX),
            Math.Max(
                EstimateDistanceMeters(center, envelope.MinY, envelope.MaxX),
                Math.Max(
                    EstimateDistanceMeters(center, envelope.MaxY, envelope.MinX),
                    EstimateDistanceMeters(center, envelope.MaxY, envelope.MaxX))));
        var sdkPadding = Math.Max(
            NormalizeNonNegative(definition.BufferDistance),
            NormalizeNonNegative(definition.ProximityDistance));

        return Math.Max(minimumNativeRadiusMeters, envelopeRadius + sdkPadding);
    }

    private static double EstimateDistanceMeters(HonuaMapCoordinate center, double latitude, double longitude)
    {
        var latitudeMeters = (latitude - center.Latitude) * MetersPerDegreeLatitude;
        var longitudeMeters = (longitude - center.Longitude) *
            MetersPerDegreeLatitude *
            Math.Cos(center.Latitude * Math.PI / 180);
        return Math.Sqrt(latitudeMeters * latitudeMeters + longitudeMeters * longitudeMeters);
    }

    private static double NormalizeNonNegative(double? value)
        => value.HasValue && double.IsFinite(value.Value) && value.Value > 0 ? value.Value : 0;

    private static IReadOnlyDictionary<string, object?> BuildRegionMetadata(
        HonuaSdkGeofenceWorkflowRequest request,
        SdkGeofenceDefinition definition,
        string regionId)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        MergeMetadata(metadata, request.Metadata);

        metadata["honua.sdk.geofence_id"] = definition.GeofenceId;
        metadata["honua.native.region_id"] = regionId;

        AddMetadata(metadata, "honua.sdk.source_id", definition.Source?.Id);
        AddMetadata(metadata, "honua.sdk.source_protocol", definition.Source?.Protocol);
        AddMetadata(metadata, "honua.sdk.source_query_where", definition.SourceQuery?.Where);
        MergeMetadata(metadata, definition.Metadata);

        return metadata;
    }

    private static IReadOnlyDictionary<string, object?> BuildEventMetadata(
        HonuaSdkGeofenceRegionBinding binding,
        HonuaGeofenceTransition transition)
    {
        var metadata = new Dictionary<string, object?>(binding.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["honua.native.transition"] = transition.Kind.ToString(),
            ["honua.sdk.transition"] = ToSdkTransition(transition.Kind).ToString(),
            ["honua.sdk.status"] = ToSdkStatus(transition.Kind).ToString(),
        };

        if (transition.Location?.Provider is { Length: > 0 } provider)
        {
            metadata["honua.location.provider"] = provider;
        }

        if (transition.Location?.AccuracyMeters is { } accuracy)
        {
            metadata["honua.location.accuracy_m"] = accuracy;
        }

        return metadata;
    }

    private static void MergeMetadata<TValue>(
        IDictionary<string, object?> metadata,
        IEnumerable<KeyValuePair<string, TValue>> source)
    {
        foreach (var item in source)
        {
            if (!string.IsNullOrWhiteSpace(item.Key))
            {
                metadata[item.Key] = item.Value;
            }
        }
    }

    private static void AddMetadata(IDictionary<string, object?> metadata, string key, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        metadata[key] = value;
    }

    private static SdkGeofenceTransition ToSdkTransition(HonuaGeofenceTransitionKind kind)
    {
        return kind switch
        {
            HonuaGeofenceTransitionKind.Enter => SdkGeofenceTransition.Entered,
            HonuaGeofenceTransitionKind.Exit => SdkGeofenceTransition.Exited,
            HonuaGeofenceTransitionKind.Proximity => SdkGeofenceTransition.Approached,
            HonuaGeofenceTransitionKind.Dwell => SdkGeofenceTransition.None,
            _ => SdkGeofenceTransition.None,
        };
    }

    private static SdkGeofenceStatus ToSdkStatus(HonuaGeofenceTransitionKind kind)
    {
        return kind switch
        {
            HonuaGeofenceTransitionKind.Enter or HonuaGeofenceTransitionKind.Dwell => SdkGeofenceStatus.Inside,
            HonuaGeofenceTransitionKind.Proximity => SdkGeofenceStatus.Proximity,
            HonuaGeofenceTransitionKind.Exit => SdkGeofenceStatus.Outside,
            _ => SdkGeofenceStatus.Outside,
        };
    }
}
