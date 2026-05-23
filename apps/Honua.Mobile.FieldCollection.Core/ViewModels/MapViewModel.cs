using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Microsoft.Maui.Devices.Sensors;
using FieldPoint = Honua.Mobile.FieldCollection.Models.Point;

namespace Honua.Mobile.FieldCollection.ViewModels;

public enum MobileMapCaptureSource
{
    MapTap,
    CurrentGps
}

public sealed record MobileMapBounds(
    double MinLatitude,
    double MinLongitude,
    double MaxLatitude,
    double MaxLongitude)
{
    public FieldPoint Center => new(
        (MinLatitude + MaxLatitude) / 2,
        (MinLongitude + MaxLongitude) / 2);

    public double LatitudeDelta => Math.Max(0.001, MaxLatitude - MinLatitude);

    public double LongitudeDelta => Math.Max(0.001, MaxLongitude - MinLongitude);

    public static MobileMapBounds FromPoint(FieldPoint point)
    {
        return new MobileMapBounds(point.Latitude, point.Longitude, point.Latitude, point.Longitude);
    }

    public MobileMapBounds Include(FieldPoint point)
    {
        return new MobileMapBounds(
            Math.Min(MinLatitude, point.Latitude),
            Math.Min(MinLongitude, point.Longitude),
            Math.Max(MaxLatitude, point.Latitude),
            Math.Max(MaxLongitude, point.Longitude));
    }

    public MobileMapBounds Include(MobileMapBounds bounds)
    {
        return new MobileMapBounds(
            Math.Min(MinLatitude, bounds.MinLatitude),
            Math.Min(MinLongitude, bounds.MinLongitude),
            Math.Max(MaxLatitude, bounds.MaxLatitude),
            Math.Max(MaxLongitude, bounds.MaxLongitude));
    }

    public MobileMapBounds Expand(double degrees)
    {
        return new MobileMapBounds(
            MinLatitude - degrees,
            MinLongitude - degrees,
            MaxLatitude + degrees,
            MaxLongitude + degrees);
    }

    public bool Contains(FieldPoint point)
    {
        return point.Latitude >= MinLatitude &&
            point.Latitude <= MaxLatitude &&
            point.Longitude >= MinLongitude &&
            point.Longitude <= MaxLongitude;
    }
}

public sealed record MobileMapViewportRequest(
    MobileMapBounds? Bounds,
    FieldPoint? Center,
    double RadiusKilometers,
    string Reason);

public sealed record MobileMapGeometryEditRequest(
    Feature Feature,
    Geometry Geometry,
    DateTimeOffset EditedAtUtc);

public sealed partial class MapFeatureItem : ObservableObject
{
    public MapFeatureItem(Feature feature, LayerInfo layer)
    {
        Feature = feature;
        Layer = layer;
        Bounds = TryGetBounds(feature.Geometry);
    }

    public Feature Feature { get; }

    public LayerInfo Layer { get; }

    public string FeatureId => Feature.Id;

    public int LayerId => Feature.LayerId;

    public Geometry? Geometry => Feature.Geometry;

    public MobileMapBounds? Bounds { get; }

    public string Title => Feature.DisplayTitle;

    public string Summary => Feature.AttributeSummary;

    public bool IsPendingSync => Feature.IsPendingSync;

    public bool IsPoint => Geometry is FieldPoint;

    public bool IsLine => Geometry is LineString;

    public bool IsPolygon => Geometry is Polygon;

    public bool CanEditGeometry => Layer.IsEditable && Geometry is LineString or Polygon;

    [ObservableProperty]
    private bool isSelected;

    public static MobileMapBounds? TryGetBounds(Geometry? geometry)
    {
        return EnumeratePoints(geometry)
            .Aggregate<FieldPoint, MobileMapBounds?>(
                null,
                (bounds, point) => bounds == null ? MobileMapBounds.FromPoint(point) : bounds.Include(point));
    }

    public static IEnumerable<FieldPoint> EnumeratePoints(Geometry? geometry)
    {
        return geometry switch
        {
            FieldPoint point => [point],
            LineString line => line.Coordinates,
            Polygon polygon => polygon.Coordinates.SelectMany(ring => ring),
            _ => []
        };
    }
}

public partial class MapViewModel : BaseViewModel
{
    private const double IdentifyToleranceDegrees = 0.0005;

    private readonly ILocationService _locationService;
    private readonly IFeatureService _featureService;
    private readonly IFormService _formService;
    private readonly IFieldCollectionMetadataService _metadataService;
    private bool _updatingSelection;

    [ObservableProperty]
    private Location? currentLocation;

    [ObservableProperty]
    private DateTimeOffset? currentLocationCapturedAtUtc;

    [ObservableProperty]
    private string currentLocationMetadata = "GPS not captured";

    [ObservableProperty]
    private bool isLocationEnabled;

    [ObservableProperty]
    private LayerInfo? selectedLayer;

    [ObservableProperty]
    private FieldProjectInfo? selectedProject;

    [ObservableProperty]
    private Feature? selectedFeature;

    [ObservableProperty]
    private MapFeatureItem? selectedFeatureItem;

    [ObservableProperty]
    private bool isAddingFeature;

    [ObservableProperty]
    private FieldPoint? newFeatureLocation;

    [ObservableProperty]
    private MobileMapViewportRequest? lastViewportRequest;

    public ObservableCollection<FieldProjectInfo> AvailableProjects { get; } = [];
    public ObservableCollection<LayerInfo> AvailableLayers { get; } = [];
    public ObservableCollection<Feature> MapFeatures { get; } = [];
    public ObservableCollection<MapFeatureItem> VisibleMapFeatures { get; } = [];

    public event EventHandler<MobileMapViewportRequest>? ViewportRequested;

    public MapViewModel(
        INavigationService navigationService,
        ILocationService locationService,
        IFeatureService featureService,
        IFormService formService,
        IFieldCollectionMetadataService metadataService)
        : base(navigationService)
    {
        _locationService = locationService;
        _featureService = featureService;
        _formService = formService;
        _metadataService = metadataService;

        Title = "Map";
        IsLocationEnabled = _locationService.IsLocationEnabled;
    }

    partial void OnSelectedLayerChanged(LayerInfo? value)
    {
        if (_updatingSelection || value == null)
        {
            return;
        }

        _ = LoadMapFeatures();
    }

    partial void OnSelectedProjectChanged(FieldProjectInfo? value)
    {
        if (_updatingSelection || value == null)
        {
            return;
        }

        _ = SelectProject(value);
    }

    protected override async Task OnRefresh()
    {
        await LoadMetadataAsync(refresh: true);
        await LoadCurrentLocation();
    }

    [RelayCommand]
    private Task LoadMetadata()
    {
        return LoadMetadataAsync();
    }

    private async Task LoadMetadataAsync(bool refresh = false)
    {
        await ExecuteAsync(async () =>
        {
            var projects = await _metadataService.GetProjectsAsync(refresh);
            var selectedProject = await _metadataService.GetSelectedProjectAsync();
            var layers = await _metadataService.GetLayersAsync(refresh);

            ApplyMetadata(projects, selectedProject, layers);
        });

        await LoadMapFeatures();
    }

    private void ApplyMetadata(
        IReadOnlyList<FieldProjectInfo> projects,
        FieldProjectInfo? selectedProject,
        IReadOnlyList<LayerInfo> layers)
    {
        var selectedLayerId = SelectedLayer?.Id;

        _updatingSelection = true;
        try
        {
            AvailableProjects.Clear();
            foreach (var project in projects)
            {
                AvailableProjects.Add(project);
            }

            SelectedProject = selectedProject is null
                ? AvailableProjects.FirstOrDefault()
                : AvailableProjects.FirstOrDefault(project =>
                    string.Equals(project.ServiceId, selectedProject.ServiceId, StringComparison.OrdinalIgnoreCase)) ?? selectedProject;

            AvailableLayers.Clear();
            foreach (var layer in layers)
            {
                AvailableLayers.Add(layer);
            }

            SelectedLayer = AvailableLayers.FirstOrDefault(layer => layer.Id == selectedLayerId) ??
                AvailableLayers.FirstOrDefault();
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    [RelayCommand]
    private async Task LoadCurrentLocation()
    {
        await ExecuteAsync(async () =>
        {
            CurrentLocation = await _locationService.GetCurrentLocationAsync();
            CurrentLocationCapturedAtUtc = CurrentLocation?.Timestamp == default
                ? DateTimeOffset.UtcNow
                : CurrentLocation?.Timestamp;
            CurrentLocationMetadata = FormatLocationMetadata(CurrentLocation, CurrentLocationCapturedAtUtc);
        });
    }

    [RelayCommand]
    private async Task LoadMapFeatures()
    {
        await ExecuteAsync(LoadMapFeaturesCoreAsync);
    }

    [RelayCommand]
    private async Task SelectLayer(LayerInfo layer)
    {
        if (SelectedLayer == layer)
        {
            return;
        }

        SelectedLayer = layer;
        await LoadMapFeatures();
    }

    [RelayCommand]
    private async Task SelectProject(FieldProjectInfo project)
    {
        if (SelectedProject != project)
        {
            SelectedProject = project;
        }

        await _metadataService.SelectProjectAsync(project.ServiceId);
        await LoadMetadataAsync(refresh: true);
    }

    [RelayCommand]
    private Task ToggleLayerVisibility(LayerInfo layer)
    {
        layer.IsVisible = !layer.IsVisible;
        RefreshVisibleMapFeatures();
        return Task.CompletedTask;
    }

    public void SetLayerVisibility(LayerInfo? layer, bool isVisible)
    {
        if (layer == null)
        {
            return;
        }

        layer.IsVisible = isVisible;
        RefreshVisibleMapFeatures();
    }

    [RelayCommand]
    private Task IdentifyAtLocation(FieldPoint location)
    {
        if (IsAddingFeature)
        {
            return AddFeatureAtLocation(location);
        }

        var item = FindFeatureAtLocation(location);
        return item == null
            ? Task.CompletedTask
            : SelectFeatureItem(item);
    }

    [RelayCommand]
    private Task SelectFeatureItem(MapFeatureItem item)
    {
        return SelectFeature(item.Feature);
    }

    [RelayCommand]
    private async Task SelectFeature(Feature feature)
    {
        SelectFeatureInState(feature);

        var parameters = new Dictionary<string, object>
        {
            ["featureId"] = feature.Id,
            ["layerId"] = feature.LayerId
        };

        await NavigationService.NavigateToAsync("map/feature-detail", parameters);
    }

    [RelayCommand]
    private async Task StartAddingFeature()
    {
        if (SelectedLayer == null || !SelectedLayer.IsEditable)
        {
            await ShowError("Cannot Add Feature", "Please select an editable layer first.");
            return;
        }

        if (!SupportsPointCapture(SelectedLayer.GeometryType))
        {
            await ShowError("Cannot Add Point", "Map point capture is only available for point layers.");
            return;
        }

        IsAddingFeature = true;
        await ShowMessage("Add Feature", $"Tap on the map to add a new {SelectedLayer.Name} feature.");
    }

    [RelayCommand]
    private Task CancelAddingFeature()
    {
        IsAddingFeature = false;
        NewFeatureLocation = null;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task AddFeatureFromCurrentLocation()
    {
        if (SelectedLayer == null || !SelectedLayer.IsEditable)
        {
            await ShowError("Cannot Add Feature", "Please select an editable layer first.");
            return;
        }

        if (!SupportsPointCapture(SelectedLayer.GeometryType))
        {
            await ShowError("Cannot Add Point", "GPS point capture is only available for point layers.");
            return;
        }

        if (CurrentLocation == null)
        {
            await LoadCurrentLocation();
        }

        if (CurrentLocation == null)
        {
            await ShowError("Location Unavailable", "Unable to determine current location.");
            return;
        }

        var point = new FieldPoint(CurrentLocation.Latitude, CurrentLocation.Longitude, CurrentLocation.Altitude);
        await BeginRecordCreateAtLocation(
            point,
            MobileMapCaptureSource.CurrentGps,
            CurrentLocation.Accuracy,
            CurrentLocationCapturedAtUtc ?? DateTimeOffset.UtcNow);
    }

    [RelayCommand]
    private async Task AddFeatureAtLocation(FieldPoint location)
    {
        if (!IsAddingFeature || SelectedLayer == null)
        {
            return;
        }

        await BeginRecordCreateAtLocation(location, MobileMapCaptureSource.MapTap, null, DateTimeOffset.UtcNow);
    }

    [RelayCommand]
    private async Task QueueGeometryEdit(MobileMapGeometryEditRequest request)
    {
        if (SelectedLayer?.Id != request.Feature.LayerId)
        {
            await ShowError("Cannot Edit Geometry", "The feature is not in the selected layer.");
            return;
        }

        if (SelectedLayer is not { IsEditable: true })
        {
            await ShowError("Cannot Edit Geometry", "The selected layer is not editable.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var updatedFeature = CloneFeature(request.Feature);
            updatedFeature.Geometry = request.Geometry;
            updatedFeature.Attributes["geometry_edited_at_utc"] = request.EditedAtUtc.UtcDateTime;

            var saved = await _featureService.UpdateFeatureAsync(updatedFeature.LayerId, updatedFeature);
            await LoadMapFeaturesCoreAsync();
            SelectFeatureInState(saved);
            RequestViewport(BuildFeatureViewport(saved, "geometry-edit"));
        });
    }

    [RelayCommand]
    private async Task ZoomToCurrentLocation()
    {
        if (CurrentLocation == null)
        {
            await LoadCurrentLocation();
        }

        if (CurrentLocation != null)
        {
            RequestViewport(new MobileMapViewportRequest(
                Bounds: null,
                Center: new FieldPoint(CurrentLocation.Latitude, CurrentLocation.Longitude, CurrentLocation.Altitude),
                RadiusKilometers: 1,
                Reason: "current-location"));
        }
        else
        {
            await ShowError("Location Unavailable", "Unable to determine current location.");
        }
    }

    [RelayCommand]
    private async Task ZoomToFeatures()
    {
        var bounds = GetVisibleFeatureBounds();
        if (bounds == null)
        {
            await ShowMessage("No Features", "No visible features to display on the current layer.");
            return;
        }

        RequestViewport(new MobileMapViewportRequest(bounds, null, 1, "features"));
    }

    [RelayCommand]
    private async Task ZoomToSelectedFeature()
    {
        if (SelectedFeature == null)
        {
            await ShowMessage("No Feature", "Select a feature first.");
            return;
        }

        RequestViewport(BuildFeatureViewport(SelectedFeature, "selected-feature"));
    }

    [RelayCommand]
    private async Task OpenLayerSettings()
    {
        if (SelectedLayer == null)
        {
            return;
        }

        var parameters = new Dictionary<string, object>
        {
            ["layerId"] = SelectedLayer.Id
        };

        await NavigationService.NavigateToAsync("map/layer-settings", parameters);
    }

    private async Task BeginRecordCreateAtLocation(
        FieldPoint location,
        MobileMapCaptureSource source,
        double? accuracyMeters,
        DateTimeOffset capturedAtUtc)
    {
        if (SelectedLayer == null)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            _ = await _formService.CreateEmptyFormAsync(SelectedLayer.Id);
            NewFeatureLocation = location;

            var parameters = new Dictionary<string, object>
            {
                ["layerId"] = SelectedLayer.Id,
                ["location"] = location,
                ["isNew"] = true,
                ["captureSource"] = source.ToString(),
                ["capturedAtUtc"] = capturedAtUtc.UtcDateTime
            };
            if (accuracyMeters.HasValue)
            {
                parameters["gpsAccuracyMeters"] = accuracyMeters.Value;
            }

            await NavigationService.NavigateToAsync("record-create", parameters);
        });

        IsAddingFeature = false;
        NewFeatureLocation = null;
    }

    private void RefreshVisibleMapFeatures()
    {
        var selectedFeatureId = SelectedFeature?.Id;
        VisibleMapFeatures.Clear();
        if (SelectedLayer is not { IsVisible: true })
        {
            return;
        }

        foreach (var feature in MapFeatures.Where(feature => feature.Geometry != null))
        {
            var item = new MapFeatureItem(feature, SelectedLayer)
            {
                IsSelected = string.Equals(feature.Id, selectedFeatureId, StringComparison.Ordinal)
            };
            VisibleMapFeatures.Add(item);
        }
    }

    private async Task LoadMapFeaturesCoreAsync()
    {
        if (SelectedLayer == null)
        {
            MapFeatures.Clear();
            VisibleMapFeatures.Clear();
            SelectedFeature = null;
            SelectedFeatureItem = null;
            return;
        }

        var features = await _featureService.GetFeaturesAsync(SelectedLayer.Id);

        MapFeatures.Clear();
        foreach (var feature in features)
        {
            MapFeatures.Add(feature);
        }

        RefreshVisibleMapFeatures();
    }

    private void SelectFeatureInState(Feature feature)
    {
        SelectedFeature = feature;
        foreach (var item in VisibleMapFeatures)
        {
            item.IsSelected = string.Equals(item.FeatureId, feature.Id, StringComparison.Ordinal);
        }

        SelectedFeatureItem = VisibleMapFeatures.FirstOrDefault(item => item.IsSelected);
    }

    private MapFeatureItem? FindFeatureAtLocation(FieldPoint location)
    {
        return VisibleMapFeatures
            .Where(item => item.Bounds != null)
            .Select(item => new
            {
                Item = item,
                Distance = EstimateHitDistanceDegrees(item, location)
            })
            .Where(match => match.Distance <= IdentifyToleranceDegrees)
            .OrderBy(match => match.Distance)
            .Select(match => match.Item)
            .FirstOrDefault();
    }

    private static double EstimateHitDistanceDegrees(MapFeatureItem item, FieldPoint location)
    {
        return item.Geometry switch
        {
            FieldPoint point => Math.Max(Math.Abs(point.Latitude - location.Latitude), Math.Abs(point.Longitude - location.Longitude)),
            _ when item.Bounds != null && item.Bounds.Expand(IdentifyToleranceDegrees).Contains(location) => 0,
            _ when item.Bounds != null => DistanceToBoundsDegrees(item.Bounds, location),
            _ => double.MaxValue
        };
    }

    private static double DistanceToBoundsDegrees(MobileMapBounds bounds, FieldPoint location)
    {
        var latitudeDelta = Math.Max(Math.Max(bounds.MinLatitude - location.Latitude, 0), location.Latitude - bounds.MaxLatitude);
        var longitudeDelta = Math.Max(Math.Max(bounds.MinLongitude - location.Longitude, 0), location.Longitude - bounds.MaxLongitude);
        return Math.Max(latitudeDelta, longitudeDelta);
    }

    private MobileMapBounds? GetVisibleFeatureBounds()
    {
        return VisibleMapFeatures
            .Where(item => item.Bounds != null)
            .Select(item => item.Bounds!)
            .Aggregate<MobileMapBounds, MobileMapBounds?>(
                null,
                (bounds, itemBounds) => bounds == null ? itemBounds : bounds.Include(itemBounds));
    }

    private static MobileMapViewportRequest BuildFeatureViewport(Feature feature, string reason)
    {
        var bounds = MapFeatureItem.TryGetBounds(feature.Geometry);
        return bounds == null
            ? new MobileMapViewportRequest(null, null, 1, reason)
            : new MobileMapViewportRequest(bounds.Expand(IdentifyToleranceDegrees), bounds.Center, 1, reason);
    }

    private void RequestViewport(MobileMapViewportRequest request)
    {
        LastViewportRequest = request;
        ViewportRequested?.Invoke(this, request);
    }

    private static bool SupportsPointCapture(GeometryType geometryType)
    {
        return geometryType is GeometryType.Unspecified or GeometryType.Point;
    }

    private static string FormatLocationMetadata(Location? location, DateTimeOffset? capturedAtUtc)
    {
        if (location == null)
        {
            return "GPS not captured";
        }

        var accuracy = location.Accuracy.HasValue
            ? $"accuracy {location.Accuracy.Value:F1} m"
            : "accuracy unknown";
        var captured = capturedAtUtc.HasValue
            ? capturedAtUtc.Value.UtcDateTime.ToString("u")
            : "time unknown";
        return $"{accuracy}; captured {captured}";
    }

    private static Feature CloneFeature(Feature feature)
    {
        return new Feature
        {
            Id = feature.Id,
            LayerId = feature.LayerId,
            Geometry = feature.Geometry,
            Attributes = new Dictionary<string, object?>(feature.Attributes, StringComparer.OrdinalIgnoreCase),
            CreatedAt = feature.CreatedAt,
            ModifiedAt = feature.ModifiedAt,
            UpdatedAt = feature.UpdatedAt,
            Version = feature.Version,
            CreatedBy = feature.CreatedBy,
            UpdatedBy = feature.UpdatedBy,
            IsPendingSync = feature.IsPendingSync,
            Attachments = feature.Attachments.ToList()
        };
    }
}
