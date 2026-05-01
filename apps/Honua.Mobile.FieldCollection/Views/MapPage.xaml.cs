using Honua.Mobile.FieldCollection.ViewModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using FieldPoint = Honua.Mobile.FieldCollection.Models.Point;

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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Load current location and features when page appears
        await _viewModel.LoadCurrentLocationCommand.ExecuteAsync(null);
        await _viewModel.LoadMapFeaturesCommand.ExecuteAsync(null);

        // Center map on current location if available
        if (_viewModel.CurrentLocation != null)
        {
            var location = new Location(_viewModel.CurrentLocation.Latitude, _viewModel.CurrentLocation.Longitude);
            MapView.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromKilometers(1)));
        }
    }

    private async void OnMapClicked(object? sender, MapClickedEventArgs e)
    {
        // Handle map clicks for adding new features
        if (_viewModel.IsAddingFeature)
        {
            var point = new FieldPoint
            {
                Latitude = e.Location.Latitude,
                Longitude = e.Location.Longitude
            };

            await _viewModel.AddFeatureAtLocationCommand.ExecuteAsync(point);
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
