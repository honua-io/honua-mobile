// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace HonuaFieldCollector.Features.Mapping;

/// <summary>
/// ViewModel for the Map screen. Manages GPS tracking, feature selection,
/// layer visibility, and spatial queries.
/// </summary>
public partial class MapViewModel : ObservableObject
{
    private readonly IMapService _mapService;
    private readonly ILocationService _locationService;
    private readonly ISpatialService _spatialService;
    private readonly ILogger<MapViewModel> _logger;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasLocation;
    [ObservableProperty] private string _locationAccuracyText = "No GPS";
    [ObservableProperty] private Color _locationAccuracyColor = Colors.Gray;
    [ObservableProperty] private bool _hasSelectedFeature;
    [ObservableProperty] private string _selectedFeatureTitle = string.Empty;
    [ObservableProperty] private string _selectedFeatureSubtitle = string.Empty;
    [ObservableProperty] private string _selectedFeatureCoordinates = string.Empty;
    [ObservableProperty] private long _selectedFeatureId;

    public MapViewModel(
        IMapService mapService,
        ILocationService locationService,
        ISpatialService spatialService,
        ILogger<MapViewModel> logger)
    {
        _mapService = mapService;
        _locationService = locationService;
        _spatialService = spatialService;
        _logger = logger;
    }

    [RelayCommand]
    private async Task OnAppearingAsync()
    {
        IsLoading = true;
        try
        {
            await _locationService.StartTrackingAsync();
            _locationService.LocationChanged += OnLocationChanged;
            await _mapService.LoadLayersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize map");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OnDisappearing()
    {
        _locationService.LocationChanged -= OnLocationChanged;
        _locationService.StopTracking();
    }

    [RelayCommand]
    private async Task GoToMyLocationAsync()
    {
        var location = await _locationService.GetCurrentLocationAsync();
        if (location is not null)
            await _mapService.PanToAsync(location.Latitude, location.Longitude, zoomLevel: 17);
    }

    [RelayCommand]
    private async Task ToggleLayersAsync()
    {
        await Shell.Current.GoToAsync("layers");
    }

    [RelayCommand]
    private async Task StartSpatialQueryAsync()
    {
        await _mapService.ActivateSpatialQueryAsync();
    }

    [RelayCommand]
    private async Task AddFeatureAtLocationAsync()
    {
        var location = await _locationService.GetCurrentLocationAsync();
        if (location is not null)
            await Shell.Current.GoToAsync($"feature/edit?lat={location.Latitude}&lon={location.Longitude}");
    }

    [RelayCommand]
    private async Task EditSelectedFeatureAsync()
    {
        if (HasSelectedFeature)
            await Shell.Current.GoToAsync($"feature/edit?featureId={SelectedFeatureId}");
    }

    private void OnLocationChanged(object? sender, LocationEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            HasLocation = true;
            var accuracy = e.Accuracy;
            LocationAccuracyText = $"{accuracy:F1}m";
            LocationAccuracyColor = accuracy switch
            {
                <= 5 => Colors.Green,
                <= 15 => Colors.Orange,
                _ => Colors.Red,
            };
        });
    }

    /// <summary>
    /// Called by the map handler when a feature is tapped.
    /// </summary>
    public void OnFeatureSelected(long featureId, string title, string subtitle, double lat, double lon)
    {
        SelectedFeatureId = featureId;
        SelectedFeatureTitle = title;
        SelectedFeatureSubtitle = subtitle;
        SelectedFeatureCoordinates = $"{lat:F6}, {lon:F6}";
        HasSelectedFeature = true;
    }
}
