using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Forms;
using System.Text.Json;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class FieldCollectionSdkContractMigrationTests
{
    public FieldCollectionSdkContractMigrationTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public void Feature_ToSdkFeatureRecord_UsesSdkFeatureContractWithGeoJsonGeometry()
    {
        var feature = new Feature
        {
            Id = "asset-1",
            LayerId = 7,
            Geometry = new Point(21.3, -157.8, 12),
            Attributes =
            {
                ["name"] = "Pump Station",
                ["priority"] = 3,
                ["active"] = true
            }
        };

        var sdkFeature = feature.ToSdkFeatureRecord();

        Assert.Equal("asset-1", sdkFeature.Id);
        Assert.Equal("Pump Station", sdkFeature.Attributes["name"].GetString());
        Assert.Equal(3, sdkFeature.Attributes["priority"].GetInt32());
        Assert.True(sdkFeature.Attributes["active"].GetBoolean());
        Assert.True(sdkFeature.Geometry.HasValue);
        Assert.Equal("Point", sdkFeature.Geometry.Value.GetProperty("type").GetString());
        Assert.Equal(-157.8, sdkFeature.Geometry.Value.GetProperty("coordinates")[0].GetDouble());
        Assert.Equal(21.3, sdkFeature.Geometry.Value.GetProperty("coordinates")[1].GetDouble());

        var mobileFeature = Feature.FromSdkFeatureRecord(sdkFeature, layerId: 7);

        Assert.Equal(7, mobileFeature.LayerId);
        Assert.Equal("asset-1", mobileFeature.Id);
        var point = Assert.IsType<Point>(mobileFeature.Geometry);
        Assert.Equal(21.3, point.Latitude);
        Assert.Equal(-157.8, point.Longitude);
        Assert.Equal(12, point.Altitude);
        Assert.Equal("Pump Station", mobileFeature.Attributes["name"]);
    }

    [Fact]
    public void Feature_FromSdkFeatureRecord_PreservesSdkNullAttributes()
    {
        var sdkFeature = new FeatureRecord
        {
            Id = "asset-null",
            Attributes = new Dictionary<string, JsonElement>
            {
                ["nullable"] = JsonSerializer.SerializeToElement<string?>(null),
                ["name"] = JsonSerializer.SerializeToElement("Null Test")
            }
        };

        var mobileFeature = Feature.FromSdkFeatureRecord(sdkFeature, layerId: 7);

        Assert.True(mobileFeature.Attributes.ContainsKey("nullable"));
        Assert.Null(mobileFeature.Attributes["nullable"]);
        Assert.Equal("Null Test", mobileFeature.Attributes["name"]);
    }

    [Fact]
    public void Feature_FromSdkFeatureRecord_ReadsFeatureServerGeometry()
    {
        var sdkFeature = new FeatureRecord
        {
            Id = "asset-feature-server",
            Geometry = JsonSerializer.SerializeToElement(new
            {
                x = -157.8,
                y = 21.3,
                z = 12.0,
                spatialReference = new { wkid = 3857 }
            })
        };

        var mobileFeature = Feature.FromSdkFeatureRecord(sdkFeature, layerId: 7);

        var point = Assert.IsType<Point>(mobileFeature.Geometry);
        Assert.Equal(21.3, point.Latitude);
        Assert.Equal(-157.8, point.Longitude);
        Assert.Equal(12, point.Altitude);
        Assert.Equal(3857, point.SRID);
    }

    [Fact]
    public async Task FormService_ValidateFormAsync_UsesSdkFormValidator()
    {
        var definition = new FormDefinition
        {
            FormId = "inspection",
            Name = "Inspection",
            Sections =
            [
                new FormSection
                {
                    SectionId = "main",
                    Label = "Main",
                    Fields =
                    [
                        new FieldDefinition
                        {
                            FieldId = "site_name",
                            Label = "Site name",
                            Type = FormFieldType.Text,
                            Required = true
                        }
                    ]
                }
            ]
        };
        var formData = new FormData { LayerId = 4 };
        var service = new FormService();

        var missingRequired = await service.ValidateFormAsync(formData, definition);

        Assert.False(missingRequired);
        Assert.Contains("site_name", formData.ValidationErrors.Keys);

        formData.Values["site_name"] = "Honua Yard";
        var valid = await service.ValidateFormAsync(formData, definition);

        Assert.True(valid);
        Assert.Empty(formData.ValidationErrors);
    }

    [Fact]
    public async Task GeoPackageStorageService_CreateLayerAsync_StoresSdkFieldSchema()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"honua-field-sdk-contracts-{Guid.NewGuid():N}.gpkg");
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);

        await storage.CreateLayerAsync(new LayerInfo
        {
            Id = 12,
            Name = "Routes",
            Description = "Inspection routes",
            GeometryType = GeometryType.Polyline,
            Schema =
            [
                new FieldDefinition
                {
                    FieldId = "route_status",
                    SourceFieldName = "route_status",
                    Label = "Route status",
                    Type = FormFieldType.SingleChoice,
                    Required = true,
                    Choices =
                    [
                        new FieldChoice { Value = "open", Label = "Open" },
                        new FieldChoice { Value = "closed", Label = "Closed" }
                    ]
                }
            ]
        });

        var layer = Assert.Single(await storage.GetLayersAsync());

        Assert.Equal(GeometryType.Polyline, layer.GeometryType);
        var field = Assert.Single(layer.Schema);
        Assert.Equal("route_status", field.FieldId);
        Assert.Equal(FormFieldType.SingleChoice, field.Type);
        Assert.True(field.Required);
        Assert.Equal(["open", "closed"], field.Choices.Select(choice => choice.Value).ToArray());
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
}
