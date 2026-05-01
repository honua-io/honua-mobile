using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using System.Collections.ObjectModel;
using FieldPoint = Honua.Mobile.FieldCollection.Models.Point;

namespace Honua.Mobile.FieldCollection.ViewModels;

public partial class MapViewModel : BaseViewModel
{
    private readonly ILocationService _locationService;
    private readonly IFeatureService _featureService;
    private readonly IFormService _formService;

    [ObservableProperty]
    private Location? currentLocation;

    [ObservableProperty]
    private bool isLocationEnabled;

    [ObservableProperty]
    private LayerInfo? selectedLayer;

    [ObservableProperty]
    private Feature? selectedFeature;

    [ObservableProperty]
    private bool isAddingFeature;

    [ObservableProperty]
    private FieldPoint? newFeatureLocation;

    public ObservableCollection<LayerInfo> AvailableLayers { get; } = new();
    public ObservableCollection<Feature> MapFeatures { get; } = new();

    public MapViewModel(
        INavigationService navigationService,
        ILocationService locationService,
        IFeatureService featureService,
        IFormService formService)
        : base(navigationService)
    {
        _locationService = locationService;
        _featureService = featureService;
        _formService = formService;

        Title = "Map";
        IsLocationEnabled = _locationService.IsLocationEnabled;

        // Initialize with demo layers
        InitializeLayers();
    }

    private void InitializeLayers()
    {
        AvailableLayers.Add(new LayerInfo
        {
            Id = 1,
            Name = "Points of Interest",
            Description = "Sample point layer for demonstration",
            GeometryType = GeometryType.Point,
            IsVisible = true,
            IsEditable = true,
            Style = new LayerStyle
            {
                FillColor = "#FF0000",
                StrokeColor = "#000000",
                MarkerSize = 12
            }
        });

        AvailableLayers.Add(new LayerInfo
        {
            Id = 2,
            Name = "Inspection Routes",
            Description = "Line features for route planning",
            GeometryType = GeometryType.LineString,
            IsVisible = false,
            IsEditable = true,
            Style = new LayerStyle
            {
                StrokeColor = "#0000FF",
                StrokeWidth = 3
            }
        });

        // Select the first layer by default
        if (AvailableLayers.Count > 0)
        {
            SelectedLayer = AvailableLayers[0];
        }
    }

    protected override async Task OnRefresh()
    {
        await LoadCurrentLocation();
        await LoadMapFeatures();
    }

    [RelayCommand]
    private async Task LoadCurrentLocation()
    {
        await ExecuteAsync(async () =>
        {
            CurrentLocation = await _locationService.GetCurrentLocationAsync();
        });
    }

    [RelayCommand]
    private async Task LoadMapFeatures()
    {
        if (SelectedLayer == null) return;

        await ExecuteAsync(async () =>
        {
            var features = await _featureService.GetFeaturesAsync(SelectedLayer.Id);

            MapFeatures.Clear();
            foreach (var feature in features)
            {
                MapFeatures.Add(feature);
            }
        });
    }

    [RelayCommand]
    private async Task SelectLayer(LayerInfo layer)
    {
        if (SelectedLayer == layer) return;

        SelectedLayer = layer;
        await LoadMapFeatures();
    }

    [RelayCommand]
    private async Task ToggleLayerVisibility(LayerInfo layer)
    {
        layer.IsVisible = !layer.IsVisible;
        // In a real implementation, this would update the map display
    }

    [RelayCommand]
    private async Task SelectFeature(Feature feature)
    {
        SelectedFeature = feature;

        // Navigate to feature detail
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

        IsAddingFeature = true;
        await ShowMessage("Add Feature", $"Tap on the map to add a new {SelectedLayer.Name} feature.");
    }

    [RelayCommand]
    private async Task CancelAddingFeature()
    {
        IsAddingFeature = false;
        NewFeatureLocation = null;
    }

    [RelayCommand]
    private async Task AddFeatureAtLocation(FieldPoint location)
    {
        if (!IsAddingFeature || SelectedLayer == null)
            return;

        await ExecuteAsync(async () =>
        {
            // Create a new feature at the specified location
            var formData = await _formService.CreateEmptyFormAsync(SelectedLayer.Id);

            var parameters = new Dictionary<string, object>
            {
                ["layerId"] = SelectedLayer.Id,
                ["location"] = location,
                ["isNew"] = true
            };

            await NavigationService.NavigateToAsync("record-create", parameters);
        });

        // Reset adding state
        IsAddingFeature = false;
        NewFeatureLocation = null;
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
            // In a real implementation, this would zoom the map to current location
            await ShowMessage("Location", $"Current location: {CurrentLocation.Latitude:F6}, {CurrentLocation.Longitude:F6}");
        }
        else
        {
            await ShowError("Location Unavailable", "Unable to determine current location.");
        }
    }

    [RelayCommand]
    private async Task ZoomToFeatures()
    {
        if (MapFeatures.Count == 0)
        {
            await ShowMessage("No Features", "No features to display on the current layer.");
            return;
        }

        // In a real implementation, this would calculate bounds and zoom to fit all features
        await ShowMessage("Zoom to Features", $"Zooming to {MapFeatures.Count} features");
    }

    [RelayCommand]
    private async Task OpenLayerSettings()
    {
        if (SelectedLayer == null) return;

        var parameters = new Dictionary<string, object>
        {
            ["layerId"] = SelectedLayer.Id
        };

        await NavigationService.NavigateToAsync("map/layer-settings", parameters);
    }
}
