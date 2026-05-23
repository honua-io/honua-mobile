using Honua.Mobile.FieldCollection.ViewModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Maps;
using FieldPoint = Honua.Mobile.FieldCollection.Models.Point;
using MauiPolygon = Microsoft.Maui.Controls.Maps.Polygon;
using MauiPolyline = Microsoft.Maui.Controls.Maps.Polyline;

namespace Honua.Mobile.FieldCollection.Views;

public partial class MapPage : ContentPage
{
    private readonly MapViewModel _viewModel;

    public MapPage(MapViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to map events
        MapView.MapClicked += OnMapClicked;
        _viewModel.VisibleMapFeatures.CollectionChanged += (_, _) => RenderMapFeatures();
        _viewModel.ViewportRequested += OnViewportRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Load metadata, current location, and features when page appears
        await _viewModel.LoadMetadataCommand.ExecuteAsync(null);
        await _viewModel.LoadCurrentLocationCommand.ExecuteAsync(null);

        RenderMapFeatures();
        if (_viewModel.LastViewportRequest != null)
        {
            ApplyViewportRequest(_viewModel.LastViewportRequest);
        }
    }

    private async void OnMapClicked(object? sender, MapClickedEventArgs e)
    {
        var point = new FieldPoint
        {
            Latitude = e.Location.Latitude,
            Longitude = e.Location.Longitude
        };

        await _viewModel.IdentifyAtLocationCommand.ExecuteAsync(point);
    }

    private async void OnFeaturePinClicked(object? sender, PinClickedEventArgs e)
    {
        e.HideInfoWindow = true;
        if (sender is Pin { BindingContext: MapFeatureItem item })
        {
            await _viewModel.SelectFeatureItemCommand.ExecuteAsync(item);
        }
    }

    private void OnLayerVisibilityChanged(object? sender, CheckedChangedEventArgs e)
    {
        _viewModel.SetLayerVisibility(_viewModel.SelectedLayer, e.Value);
    }

    private void OnViewportRequested(object? sender, MobileMapViewportRequest request)
    {
        ApplyViewportRequest(request);
    }

    private void RenderMapFeatures()
    {
        MapView.Pins.Clear();
        MapView.MapElements.Clear();

        foreach (var item in _viewModel.VisibleMapFeatures)
        {
            switch (item.Geometry)
            {
                case FieldPoint point:
                    AddFeaturePin(item, point);
                    break;
                case Honua.Mobile.FieldCollection.Models.LineString line:
                    AddFeatureLine(item, line);
                    break;
                case Honua.Mobile.FieldCollection.Models.Polygon polygon:
                    AddFeaturePolygon(item, polygon);
                    break;
            }
        }
    }

    private void AddFeaturePin(MapFeatureItem item, FieldPoint point)
    {
        var pin = new Pin
        {
            Label = item.Title,
            Address = item.Summary,
            BindingContext = item,
            Location = new Location(point.Latitude, point.Longitude),
            Type = item.IsPendingSync ? PinType.SavedPin : PinType.Place
        };
        pin.MarkerClicked += OnFeaturePinClicked;
        MapView.Pins.Add(pin);
    }

    private void AddFeatureLine(MapFeatureItem item, Honua.Mobile.FieldCollection.Models.LineString line)
    {
        var polyline = new MauiPolyline
        {
            StrokeColor = ParseColor(item.Layer.Style.StrokeColor, Colors.DeepSkyBlue),
            StrokeWidth = (float)Math.Max(1, item.Layer.Style.StrokeWidth)
        };
        foreach (var point in line.Coordinates)
        {
            polyline.Geopath.Add(new Location(point.Latitude, point.Longitude));
        }

        if (polyline.Geopath.Count > 1)
        {
            MapView.MapElements.Add(polyline);
        }
    }

    private void AddFeaturePolygon(MapFeatureItem item, Honua.Mobile.FieldCollection.Models.Polygon polygon)
    {
        var element = new MauiPolygon
        {
            StrokeColor = ParseColor(item.Layer.Style.StrokeColor, Colors.DeepSkyBlue),
            FillColor = ParseColor(item.Layer.Style.FillColor, Colors.DeepSkyBlue).WithAlpha((float)Math.Clamp(item.Layer.Style.Opacity, 0.05, 0.5)),
            StrokeWidth = (float)Math.Max(1, item.Layer.Style.StrokeWidth)
        };
        foreach (var point in polygon.Coordinates.FirstOrDefault() ?? [])
        {
            element.Geopath.Add(new Location(point.Latitude, point.Longitude));
        }

        if (element.Geopath.Count > 2)
        {
            MapView.MapElements.Add(element);
        }
    }

    private void ApplyViewportRequest(MobileMapViewportRequest request)
    {
        var center = request.Center ?? request.Bounds?.Center;
        if (center == null)
        {
            return;
        }

        var radiusKilometers = request.Bounds == null
            ? request.RadiusKilometers
            : Math.Max(request.RadiusKilometers, EstimateRadiusKilometers(request.Bounds));
        var location = new Location(center.Latitude, center.Longitude);
        MapView.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromKilometers(radiusKilometers)));
    }

    private static double EstimateRadiusKilometers(MobileMapBounds bounds)
    {
        var latitudeKilometers = bounds.LatitudeDelta * 111;
        var longitudeKilometers = bounds.LongitudeDelta * 111;
        return Math.Max(0.25, Math.Max(latitudeKilometers, longitudeKilometers));
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return Color.FromArgb(value);
        }
        catch
        {
            return fallback;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Cancel adding feature mode if user navigates away
        if (_viewModel.IsAddingFeature)
        {
            _viewModel.CancelAddingFeatureCommand.Execute(null);
        }
    }
}
