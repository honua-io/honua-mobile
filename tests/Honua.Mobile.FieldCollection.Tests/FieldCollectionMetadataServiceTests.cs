using System.Net;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Metadata;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Forms;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class FieldCollectionMetadataServiceTests
{
    public FieldCollectionMetadataServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public async Task GetProjectsAsync_WithServerCatalog_ListsFeatureServerProjects()
    {
        var requests = new List<HttpRequestMessage>();
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add(request);
            Assert.Equal("/rest/services?f=json", request.RequestUri!.PathAndQuery);

            return JsonResponse("""
                {
                  "services": [
                    { "name": "assets", "type": "FeatureServer" },
                    { "name": "Routing", "type": "NAServer" }
                  ]
                }
                """);
        }));

        var databasePath = Path.Combine(Path.GetTempPath(), $"honua-field-metadata-projects-{Guid.NewGuid():N}.gpkg");
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var service = CreateService(storage, http);

        var projects = await service.GetProjectsAsync(refresh: true);

        var project = Assert.Single(projects);
        Assert.Equal("assets", project.ServiceId);
        Assert.False(project.IsAvailableOffline);
        Assert.True(requests.Single().Headers.TryGetValues("X-API-Key", out var values));
        Assert.Equal("test-api-key", Assert.Single(values));
    }

    [Fact]
    public async Task GetLayersAsync_WithRemoteMetadata_CachesLayerSchemaAndForm()
    {
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            return request.RequestUri!.PathAndQuery switch
            {
                "/rest/services/assets/FeatureServer?f=json" => JsonResponse("""
                    {
                      "serviceDescription": "Asset inspections",
                      "capabilities": "Query",
                      "layers": [
                        { "id": 7, "name": "Inspection Sites" }
                      ]
                    }
                    """),
                "/rest/services/assets/FeatureServer/7?f=json" => JsonResponse("""
                    {
                      "id": 7,
                      "name": "Inspection Sites",
                      "description": "Pilot inspection layer",
                      "geometryType": "esriGeometryPoint",
                      "capabilities": "Query,Create,Update,Delete",
                      "fields": [
                        { "name": "OBJECTID", "type": "esriFieldTypeOID", "alias": "Object ID", "nullable": false, "editable": false },
                        { "name": "site_name", "type": "esriFieldTypeString", "alias": "Site name", "nullable": false, "editable": true, "length": 80 },
                        {
                          "name": "status",
                          "type": "esriFieldTypeString",
                          "alias": "Status",
                          "nullable": true,
                          "editable": true,
                          "domain": {
                            "type": "codedValue",
                            "codedValues": [
                              { "name": "Open", "code": "open" },
                              { "name": "Closed", "code": "closed" }
                            ]
                          }
                        }
                      ]
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }));

        var databasePath = Path.Combine(Path.GetTempPath(), $"honua-field-metadata-layers-{Guid.NewGuid():N}.gpkg");
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var service = CreateService(storage, http);

        await service.SelectProjectAsync("assets");
        var layers = await service.GetLayersAsync(refresh: true);

        var layer = Assert.Single(layers);
        Assert.Equal(7, layer.Id);
        Assert.Equal("assets", layer.ServiceId);
        Assert.Equal("assets/FeatureServer/7", layer.SourceId);
        Assert.Equal(FeatureSpatialGeometryType.Point, layer.GeometryType);
        Assert.True(layer.IsEditable);

        Assert.Equal(2, layer.Schema.Count);
        var siteName = layer.Schema.Single(field => field.FieldId == "site_name");
        Assert.Equal(FormFieldType.Text, siteName.Type);
        Assert.True(siteName.Required);
        Assert.Equal(80, siteName.Validation.MaxLength);

        var status = layer.Schema.Single(field => field.FieldId == "status");
        Assert.Equal(FormFieldType.SingleChoice, status.Type);
        Assert.Equal(["open", "closed"], status.Choices.Select(choice => choice.Value).ToArray());

        Assert.NotNull(layer.Form);
        Assert.Equal("assets", layer.Form.Target?.ServiceId);
        Assert.Equal(7, layer.Form.Target?.LayerId);

        var cachedLayer = Assert.Single(await storage.GetLayersAsync());
        Assert.Equal("assets", cachedLayer.ServiceId);
        Assert.Equal("assets", cachedLayer.Form?.Target?.ServiceId);
        Assert.Equal(2, cachedLayer.Schema.Count);
    }

    [Fact]
    public async Task GetLayersAsync_WhenOffline_ReturnsCachedLayerMetadata()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"honua-field-metadata-offline-{Guid.NewGuid():N}.gpkg");
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.CreateLayerAsync(new LayerInfo
        {
            Id = 42,
            ServiceId = "assets",
            SourceId = "assets/FeatureServer/42",
            Name = "Cached Assets",
            Description = "Previously loaded metadata",
            GeometryType = FeatureSpatialGeometryType.Polygon,
            IsEditable = false,
            Schema =
            [
                new FormField
                {
                    FieldId = "asset_id",
                    SourceFieldName = "asset_id",
                    Label = "Asset ID",
                    Type = FormFieldType.Text,
                    Required = true
                }
            ]
        });

        using var http = new HttpClient(new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Offline service must not call the network.")));
        var service = CreateService(
            storage,
            http,
            new TestAuthenticationService
            {
                IsAuthenticated = false,
                ServerUrl = null,
                ApiKey = null
            });

        var layers = await service.GetLayersAsync();

        var layer = Assert.Single(layers);
        Assert.Equal("Cached Assets", layer.Name);
        Assert.Equal("assets", layer.ServiceId);
        Assert.Equal(FeatureSpatialGeometryType.Polygon, layer.GeometryType);
        Assert.NotNull(layer.Form);
        Assert.Equal("assets/FeatureServer/42", layer.Form.Target?.SourceId);

        var formService = new FormService(service);
        var cachedForm = await formService.GetFormDefinitionAsync(42);

        Assert.NotNull(cachedForm);
        Assert.Equal("assets/FeatureServer/42", cachedForm.Target?.SourceId);
    }

    private static FieldCollectionMetadataService CreateService(
        GeoPackageStorageService storage,
        HttpClient http,
        IAuthenticationService? authenticationService = null)
    {
        return new FieldCollectionMetadataService(
            authenticationService ?? new TestAuthenticationService { ServerUrl = "https://api.honua.test" },
            new InMemorySettingsService(),
            storage,
            http);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private sealed class DatabaseCleanup : IAsyncDisposable
    {
        private readonly string _databasePath;

        public DatabaseCleanup(string databasePath)
        {
            _databasePath = databasePath;
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> _settings = new(StringComparer.Ordinal);

        public Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
        {
            return Task.FromResult(_settings.TryGetValue(key, out var value) && value is T typedValue
                ? typedValue
                : defaultValue);
        }

        public Task SetSettingAsync<T>(string key, T value)
        {
            _settings[key] = value;
            return Task.CompletedTask;
        }

        public Task<bool> HasSettingAsync(string key)
        {
            return Task.FromResult(_settings.ContainsKey(key));
        }
    }
}
