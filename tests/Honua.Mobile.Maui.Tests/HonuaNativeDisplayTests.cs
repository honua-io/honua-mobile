using System.Runtime.CompilerServices;
using Honua.Mobile.Maui;
using Honua.Mobile.Maui.Annotations;
using Honua.Mobile.Maui.Display;
using Honua.Sdk.Abstractions.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.Maui.Tests;

public sealed class HonuaNativeDisplayTests
{
    [Fact]
    public async Task RefreshAsync_QueriesVisibleFeatureLayersAndRendersAnnotations()
    {
        var adapter = new RecordingNativeMapAdapter();
        var featureClient = new RecordingFeatureQueryClient();
        var controller = new HonuaNativeMapDisplayController(adapter, featureClient);
        var featureLayer = new HonuaNativeMapLayer
        {
            Id = "parks",
            Source = CreateSource("parks"),
            Filter = "status = 'open'",
            FilterLanguage = FeatureFilterLanguage.SqlWhere,
            OutFields = ["objectid", "name"],
            ZIndex = 10,
        };
        var hiddenLayer = featureLayer with
        {
            Id = "hidden",
            Source = CreateSource("hidden"),
            IsVisible = false,
        };
        var tileLayer = featureLayer with
        {
            Id = "tiles",
            Source = CreateSource("tiles"),
            Kind = HonuaNativeMapLayerKind.VectorTile,
        };
        var annotation = new HonuaAnnotationLayer()
            .DrawPoint(new HonuaMapCoordinate(21.3069, -157.8583), id: "device");
        var scene = new HonuaNativeMapScene
        {
            Layers = [hiddenLayer, tileLayer, featureLayer],
            Annotations = [annotation],
        };
        var view = CreateView();

        await controller.RefreshAsync(scene, view);

        var request = Assert.Single(featureClient.Requests);
        Assert.Equal("parks", request.Source.CollectionId);
        Assert.Equal("status = 'open'", request.Filter);
        Assert.Equal(FeatureFilterLanguage.SqlWhere, request.FilterLanguage);
        Assert.Equal(["objectid", "name"], request.OutFields);
        Assert.True(request.ReturnGeometry);
        Assert.Same(view.Extent, request.Bbox);
        Assert.Equal(HonuaNativeMapProjection.WebMercator, request.OutputCrs);
        Assert.Same(scene, adapter.Scene);
        Assert.Same(view, adapter.View);
        Assert.Equal("parks", Assert.Single(adapter.FeatureRenders).Layer.Id);
        Assert.Equal("device", Assert.Single(adapter.Annotations).Id);
    }

    [Fact]
    public async Task RefreshAsync_RejectsDuplicateLayerIds()
    {
        var controller = new HonuaNativeMapDisplayController(
            new RecordingNativeMapAdapter(),
            new RecordingFeatureQueryClient());
        var layer = new HonuaNativeMapLayer
        {
            Id = "duplicate",
            Source = CreateSource("parks"),
        };
        var scene = new HonuaNativeMapScene { Layers = [layer, layer] };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.RefreshAsync(scene, CreateView()));

        Assert.Contains("defined more than once", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(double.NaN, 21, -157, 22)]
    [InlineData(-158, double.PositiveInfinity, -157, 22)]
    [InlineData(-158, 21, double.NegativeInfinity, 22)]
    [InlineData(-158, 21, -157, double.NaN)]
    public async Task RefreshAsync_RejectsNonFiniteViewExtent(
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        var adapter = new RecordingNativeMapAdapter();
        var featureClient = new RecordingFeatureQueryClient();
        var controller = new HonuaNativeMapDisplayController(adapter, featureClient);
        var layer = new HonuaNativeMapLayer
        {
            Id = "parks",
            Source = CreateSource("parks"),
        };
        var view = new HonuaNativeMapViewState
        {
            Extent = new FeatureBoundingBox
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY,
                Crs = HonuaNativeMapProjection.Wgs84,
            },
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.RefreshAsync(new HonuaNativeMapScene { Layers = [layer] }, view));

        Assert.Contains("finite", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(featureClient.Requests);
    }

    [Fact]
    public void AddHonuaNativeDisplay_RegistersController()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IHonuaNativeMapAdapter, RecordingNativeMapAdapter>()
            .AddSingleton<IHonuaFeatureQueryClient, RecordingFeatureQueryClient>()
            .AddHonuaNativeDisplay()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<HonuaNativeMapDisplayController>());
    }

    private static SourceDescriptor CreateSource(string id)
        => new()
        {
            Id = id,
            Protocol = FeatureProtocolIds.OgcFeatures,
            Locator = new SourceLocator { CollectionId = id },
        };

    private static HonuaNativeMapViewState CreateView()
        => new()
        {
            Extent = new FeatureBoundingBox
            {
                MinX = -158,
                MinY = 21,
                MaxX = -157,
                MaxY = 22,
                Crs = HonuaNativeMapProjection.Wgs84,
            },
            DisplayCrs = HonuaNativeMapProjection.WebMercator,
        };

    private sealed class RecordingNativeMapAdapter : IHonuaNativeMapAdapter
    {
        public HonuaNativeMapScene? Scene { get; private set; }

        public HonuaNativeMapViewState? View { get; private set; }

        public List<(HonuaNativeMapLayer Layer, FeatureQueryResult Features)> FeatureRenders { get; } = [];

        public IReadOnlyList<HonuaAnnotation> Annotations { get; private set; } = [];

        public Task ApplySceneAsync(HonuaNativeMapScene scene, CancellationToken ct = default)
        {
            Scene = scene;
            return Task.CompletedTask;
        }

        public Task SetViewAsync(HonuaNativeMapViewState view, CancellationToken ct = default)
        {
            View = view;
            return Task.CompletedTask;
        }

        public Task RenderFeaturesAsync(
            HonuaNativeMapLayer layer,
            FeatureQueryResult features,
            CancellationToken ct = default)
        {
            FeatureRenders.Add((layer, features));
            return Task.CompletedTask;
        }

        public Task RenderAnnotationsAsync(
            IReadOnlyList<HonuaAnnotation> annotations,
            CancellationToken ct = default)
        {
            Annotations = annotations;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingFeatureQueryClient : IHonuaFeatureQueryClient
    {
        public string ProviderName => "recording";

        public List<FeatureQueryRequest> Requests { get; } = [];

        public Task<FeatureQueryResult> QueryAsync(
            FeatureQueryRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new FeatureQueryResult
            {
                ProviderName = ProviderName,
                NumberReturned = 0,
                Features = [],
            });
        }

        public async IAsyncEnumerable<FeatureQueryResult> QueryPagesAsync(
            FeatureQueryRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return await QueryAsync(request, ct);
        }
    }
}
