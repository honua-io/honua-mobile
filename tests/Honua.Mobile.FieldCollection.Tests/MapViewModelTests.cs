using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.ViewModels;
using Microsoft.Maui.Devices.Sensors;
using FieldPoint = Honua.Mobile.FieldCollection.Models.Point;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class MapViewModelTests
{
    [Fact]
    public async Task LoadMetadataCommand_RendersLocalLayerGeometriesAndZoomsToBounds()
    {
        var layer = EditableLayer(7, GeometryType.Point);
        var featureService = new RecordingFeatureService(
            layer,
            [
                Feature("asset-1", layer.Id, new FieldPoint(21.3, -157.8), ("name", "Pump")),
                Feature("line-1", layer.Id, new LineString
                {
                    Coordinates = [new FieldPoint(21.31, -157.81), new FieldPoint(21.32, -157.82)]
                }),
                Feature("poly-1", layer.Id, new Polygon
                {
                    Coordinates =
                    [
                        [
                            new FieldPoint(21.30, -157.82),
                            new FieldPoint(21.30, -157.80),
                            new FieldPoint(21.32, -157.80),
                            new FieldPoint(21.30, -157.82),
                        ],
                    ]
                }),
                Feature("no-geometry", layer.Id, null),
            ]);
        var viewModel = CreateViewModel(featureService, new StubLocationService(), new StubMetadataService(layer));

        await viewModel.LoadMetadataCommand.ExecuteAsync(null);
        await viewModel.ZoomToFeaturesCommand.ExecuteAsync(null);

        Assert.Equal(layer, viewModel.SelectedLayer);
        Assert.Equal(4, viewModel.MapFeatures.Count);
        Assert.Equal(3, viewModel.VisibleMapFeatures.Count);
        Assert.Contains(viewModel.VisibleMapFeatures, item => item.IsPoint);
        Assert.Contains(viewModel.VisibleMapFeatures, item => item.IsLine);
        Assert.Contains(viewModel.VisibleMapFeatures, item => item.IsPolygon);
        Assert.Equal("features", viewModel.LastViewportRequest?.Reason);
        Assert.True(viewModel.LastViewportRequest?.Bounds?.MinLatitude <= 21.30);
        Assert.True(viewModel.LastViewportRequest?.Bounds?.MaxLongitude >= -157.80);
    }

    [Fact]
    public async Task IdentifyAtLocationCommand_SelectsFeatureAndOpensDetail()
    {
        var layer = EditableLayer(7, GeometryType.Point);
        var feature = Feature("asset-1", layer.Id, new FieldPoint(21.3, -157.8), ("name", "Pump"));
        var featureService = new RecordingFeatureService(layer, [feature]);
        var navigation = new RecordingNavigationService();
        var viewModel = CreateViewModel(featureService, new StubLocationService(), new StubMetadataService(layer), navigation);

        await viewModel.LoadMetadataCommand.ExecuteAsync(null);
        await viewModel.IdentifyAtLocationCommand.ExecuteAsync(new FieldPoint(21.3001, -157.8001));

        Assert.Equal(feature.Id, viewModel.SelectedFeature?.Id);
        Assert.Equal("map/feature-detail", navigation.LastRoute);
        Assert.Equal(feature.Id, navigation.LastParameters["featureId"]);
        Assert.Equal(layer.Id, navigation.LastParameters["layerId"]);
    }

    [Fact]
    public async Task AddFeatureCommands_OpenCreateWorkflowWithMapAndGpsCaptureMetadata()
    {
        var layer = EditableLayer(7, GeometryType.Point);
        var capturedAt = new DateTimeOffset(2026, 5, 23, 8, 30, 0, TimeSpan.Zero);
        var navigation = new RecordingNavigationService();
        var location = new Location(21.31, -157.81, capturedAt)
        {
            Accuracy = 4.5,
            VerticalAccuracy = 6.5
        };
        var locationService = new StubLocationService
        {
            CurrentFix = FieldLocationMetadataMapper.FromMauiLocation(
                location,
                new FieldLocationCaptureMetadata
                {
                    SourceKind = FieldLocationSourceKind.ExternalGnss,
                    Provider = "bluetooth-nmea",
                    Receiver = new FieldLocationReceiverMetadata
                    {
                        Name = "Trimble R12",
                        IsExternal = true
                    }
                })
        };
        var viewModel = CreateViewModel(
            new RecordingFeatureService(layer, []),
            locationService,
            new StubMetadataService(layer),
            navigation);

        await viewModel.LoadMetadataCommand.ExecuteAsync(null);
        await viewModel.StartAddingFeatureCommand.ExecuteAsync(null);
        await viewModel.IdentifyAtLocationCommand.ExecuteAsync(new FieldPoint(21.3, -157.8));

        Assert.Equal("record-create", navigation.LastRoute);
        Assert.Equal(MobileMapCaptureSource.MapTap.ToString(), navigation.LastParameters["captureSource"]);
        Assert.Equal(21.3, Assert.IsType<FieldPoint>(navigation.LastParameters["location"]).Latitude);

        await viewModel.AddFeatureFromCurrentLocationCommand.ExecuteAsync(null);

        Assert.Equal("record-create", navigation.LastRoute);
        Assert.Equal(MobileMapCaptureSource.CurrentGps.ToString(), navigation.LastParameters["captureSource"]);
        Assert.Equal(4.5, navigation.LastParameters["gpsAccuracyMeters"]);
        Assert.Equal(FieldLocationSourceKind.ExternalGnss.ToString(), navigation.LastParameters["gpsSource"]);
        Assert.Equal(capturedAt.UtcDateTime, navigation.LastParameters["capturedAtUtc"]);
        var evidence = Assert.IsType<FieldLocationCaptureEvidence>(navigation.LastParameters["locationEvidence"]);
        Assert.Equal(FieldLocationSourceKind.ExternalGnss, evidence.SourceKind);
        Assert.Equal("Trimble R12", evidence.Receiver?.Name);
        Assert.Contains("External GNSS", viewModel.CurrentLocationMetadata, StringComparison.Ordinal);
        Assert.Contains("accuracy 4.5 m", viewModel.CurrentLocationMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueueGeometryEditCommand_UpdatesFeatureThroughOfflineFeatureService()
    {
        var layer = EditableLayer(7, GeometryType.Polyline);
        var feature = Feature("line-1", layer.Id, new LineString
        {
            Coordinates = [new FieldPoint(21.3, -157.8), new FieldPoint(21.31, -157.81)]
        });
        var featureService = new RecordingFeatureService(layer, [feature]);
        var viewModel = CreateViewModel(featureService, new StubLocationService(), new StubMetadataService(layer));
        var editedGeometry = new LineString
        {
            Coordinates =
            [
                new FieldPoint(21.3, -157.8),
                new FieldPoint(21.32, -157.82),
                new FieldPoint(21.33, -157.83),
            ],
        };

        await viewModel.LoadMetadataCommand.ExecuteAsync(null);
        await viewModel.QueueGeometryEditCommand.ExecuteAsync(new MobileMapGeometryEditRequest(
            feature,
            editedGeometry,
            new DateTimeOffset(2026, 5, 23, 9, 0, 0, TimeSpan.Zero)));

        Assert.Same(editedGeometry, featureService.LastUpdatedFeature?.Geometry);
        Assert.True(featureService.LastUpdatedFeature?.IsPendingSync);
        Assert.Equal("geometry-edit", viewModel.LastViewportRequest?.Reason);
        Assert.Equal(feature.Id, viewModel.SelectedFeature?.Id);
        Assert.Single(viewModel.VisibleMapFeatures);
    }

    private static MapViewModel CreateViewModel(
        RecordingFeatureService featureService,
        StubLocationService locationService,
        StubMetadataService metadataService,
        RecordingNavigationService? navigation = null)
    {
        return new MapViewModel(
            navigation ?? new RecordingNavigationService(),
            locationService,
            featureService,
            new StubFormService(),
            metadataService);
    }

    private static LayerInfo EditableLayer(int id, GeometryType geometryType)
    {
        return new LayerInfo
        {
            Id = id,
            Name = $"Layer {id}",
            GeometryType = geometryType,
            IsEditable = true,
            IsVisible = true
        };
    }

    private static Feature Feature(
        string id,
        int layerId,
        Geometry? geometry,
        params (string Key, object? Value)[] attributes)
    {
        return new Feature
        {
            Id = id,
            LayerId = layerId,
            Geometry = geometry,
            Attributes = attributes.ToDictionary(attribute => attribute.Key, attribute => attribute.Value),
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public string LastRoute { get; private set; } = string.Empty;
        public Dictionary<string, object> LastParameters { get; private set; } = [];

        public Task NavigateToAsync(string route)
        {
            LastRoute = route;
            LastParameters = [];
            return Task.CompletedTask;
        }

        public Task NavigateToAsync(string route, IDictionary<string, object> parameters)
        {
            LastRoute = route;
            LastParameters = new Dictionary<string, object>(parameters);
            return Task.CompletedTask;
        }

        public Task GoBackAsync() => Task.CompletedTask;

        public Task PopToRootAsync() => Task.CompletedTask;

        public Task DisplayAlert(string title, string message, string cancel) => Task.CompletedTask;

        public Task<bool> DisplayAlert(string title, string message, string accept, string cancel) => Task.FromResult(true);

        public Task<string> DisplayActionSheet(string title, string cancel, string destruction, params string[] buttons) =>
            Task.FromResult(buttons.FirstOrDefault() ?? cancel);

        public Task<string> DisplayPromptAsync(
            string title,
            string message,
            string accept = "OK",
            string cancel = "Cancel",
            string placeholder = "",
            int maxLength = -1,
            string initialValue = "") => Task.FromResult(initialValue);
    }

    private sealed class StubLocationService : ILocationService
    {
        public Location? CurrentLocation { get; init; }
        public FieldLocationFix? CurrentFix { get; init; }

        public bool IsLocationEnabled => true;

        public Task<Location?> GetCurrentLocationAsync() => Task.FromResult(CurrentFix?.Location ?? CurrentLocation);

        public Task<FieldLocationFix?> GetCurrentLocationFixAsync(CancellationToken cancellationToken = default)
        {
            var fix = CurrentFix ?? (CurrentLocation is null
                ? null
                : FieldLocationMetadataMapper.FromMauiLocation(CurrentLocation));
            return Task.FromResult(fix);
        }

        public Task<Location?> GetLastKnownLocationAsync() => Task.FromResult(CurrentFix?.Location ?? CurrentLocation);

        public Task<FieldLocationFix?> GetLastKnownLocationFixAsync(CancellationToken cancellationToken = default)
            => GetCurrentLocationFixAsync(cancellationToken);

        public Task StartLocationTracking() => Task.CompletedTask;

        public Task StopLocationTracking() => Task.CompletedTask;
    }

    private sealed class RecordingFeatureService : IFeatureService
    {
        private readonly LayerInfo _layer;
        private readonly List<Feature> _features;

        public RecordingFeatureService(LayerInfo layer, IReadOnlyList<Feature> features)
        {
            _layer = layer;
            _features = features.ToList();
        }

        public Feature? LastUpdatedFeature { get; private set; }

        public Task<IReadOnlyList<LayerInfo>> GetLayersAsync() => Task.FromResult<IReadOnlyList<LayerInfo>>([_layer]);

        public Task<IEnumerable<Feature>> GetFeaturesAsync(int layerId, Polygon? spatialFilter = null) =>
            Task.FromResult<IEnumerable<Feature>>(_features.Where(feature => feature.LayerId == layerId).ToList());

        public Task<Feature?> GetFeatureAsync(int layerId, string featureId) =>
            Task.FromResult(_features.FirstOrDefault(feature => feature.LayerId == layerId && feature.Id == featureId));

        public Task<Feature> CreateFeatureAsync(int layerId, Feature feature)
        {
            feature.LayerId = layerId;
            feature.IsPendingSync = true;
            _features.Add(feature);
            return Task.FromResult(feature);
        }

        public Task<Feature> UpdateFeatureAsync(int layerId, Feature feature)
        {
            feature.LayerId = layerId;
            feature.IsPendingSync = true;
            LastUpdatedFeature = feature;
            var index = _features.FindIndex(existing => existing.LayerId == layerId && existing.Id == feature.Id);
            if (index >= 0)
            {
                _features[index] = feature;
            }
            else
            {
                _features.Add(feature);
            }

            return Task.FromResult(feature);
        }

        public Task DeleteFeatureAsync(int layerId, string featureId)
        {
            _features.RemoveAll(feature => feature.LayerId == layerId && feature.Id == featureId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubMetadataService : IFieldCollectionMetadataService
    {
        private readonly LayerInfo _layer;

        public StubMetadataService(LayerInfo layer)
        {
            _layer = layer;
        }

        public Task<IReadOnlyList<FieldProjectInfo>> GetProjectsAsync(bool refresh = false, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<FieldProjectInfo> projects =
            [
                new FieldProjectInfo
                {
                    ServiceId = "project-1",
                    Name = "Project 1",
                    LayerCount = 1,
                    IsAvailableOffline = true,
                    Layers = [_layer]
                },
            ];
            return Task.FromResult(projects);
        }

        public Task<FieldProjectInfo?> GetSelectedProjectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<FieldProjectInfo?>(null);

        public Task SelectProjectAsync(string serviceId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<LayerInfo>> GetLayersAsync(bool refresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LayerInfo>>([_layer]);
    }

    private sealed class StubFormService : IFormService
    {
        public Task<FormDefinition?> GetFormDefinitionAsync(int layerId) => Task.FromResult<FormDefinition?>(null);

        public Task<bool> ValidateFormAsync(FormData formData, FormDefinition definition) => Task.FromResult(true);

        public Task<FormData> CreateEmptyFormAsync(int layerId) =>
            Task.FromResult(new FormData { LayerId = layerId });
    }
}
