using System.Text.Json;
using System.Xml.Linq;
using Honua.Mobile.Sdk.Models;
using Honua.Sdk.GeoServices.FeatureServer.Models;
using GrpcModels = Honua.Sdk.Grpc.Models;

namespace Honua.Mobile.Sdk.Tests;

public sealed class SdkGrpcTransportMappingsTests
{
    [Fact]
    public void Project_ConsumesSdkGrpcPackageAndDoesNotCompileLocalProto()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "Honua.Mobile.Sdk", "Honua.Mobile.Sdk.csproj"));
        var packageIds = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.Contains("Honua.Sdk.Grpc", packageIds);
        Assert.DoesNotContain("Google.Protobuf", packageIds);
        Assert.DoesNotContain("Grpc.Tools", packageIds);
        Assert.Empty(project.Descendants("Protobuf"));
        Assert.False(File.Exists(Path.Combine(root, "proto", "honua", "v1", "feature_service.proto")));
    }

    [Fact]
    public void ToGrpcApplyEditsRequest_MapsLegacyJsonPayloadsToSdkGrpcModels()
    {
        var request = new ApplyEditsRequest
        {
            ServiceId = "default",
            LayerId = 0,
            AddsJson = """
            [
              {
                "attributes": { "asset_id": "A-1", "priority": 3 },
                "geometry": { "x": -157.8583, "y": 21.3069 }
              }
            ]
            """,
            UpdatesJson = """
            [
              {
                "id": 11,
                "attributes": { "asset_id": "A-1", "priority": 4 }
              }
            ]
            """,
            DeletesCsv = "9,10",
            RollbackOnFailure = true,
            ForceWrite = true,
        };

        var grpc = SdkGrpcTransportMappings.ToGrpcApplyEditsRequest(request);

        Assert.Equal("default", grpc.ServiceId);
        Assert.Equal(0, grpc.LayerId);
        Assert.Single(grpc.Adds!);
        Assert.Single(grpc.Updates!);
        Assert.Equal([9L, 10L], grpc.Deletes);
        Assert.True(grpc.RollbackOnFailure);
        Assert.True(grpc.ForceWrite);
        Assert.Equal("A-1", grpc.Adds![0].Attributes["asset_id"]);
        Assert.Equal(3L, grpc.Adds[0].Attributes["priority"]);
        Assert.Equal(-157.8583, grpc.Adds[0].Geometry!["x"]);
        Assert.Equal(21.3069, grpc.Adds[0].Geometry!["y"]);
        Assert.Equal(11L, grpc.Updates![0].Id);
        Assert.Equal(4L, grpc.Updates[0].Attributes["priority"]);
    }

    [Fact]
    public void ToGrpcApplyEditsRequest_MapsSdkFeatureServerModels()
    {
        var request = new ApplyEditsRequest
        {
            ServiceId = "default",
            LayerId = 0,
            Adds =
            [
                new FeatureServerFeature
                {
                    Attributes = new Dictionary<string, JsonElement>
                    {
                        ["asset_id"] = JsonSerializer.SerializeToElement("A-1"),
                        ["priority"] = JsonSerializer.SerializeToElement(3),
                    },
                    Geometry = JsonSerializer.SerializeToElement(new { x = -157.8583, y = 21.3069 }),
                },
            ],
            Deletes = [9, 10],
        };

        var grpc = SdkGrpcTransportMappings.ToGrpcApplyEditsRequest(request);

        Assert.Single(grpc.Adds!);
        Assert.Equal([9L, 10L], grpc.Deletes);
        Assert.Equal("A-1", grpc.Adds![0].Attributes["asset_id"]);
        Assert.Equal(3L, grpc.Adds[0].Attributes["priority"]);
        Assert.Equal(-157.8583, grpc.Adds[0].Geometry!["x"]);
        Assert.Equal(21.3069, grpc.Adds[0].Geometry!["y"]);
    }

    [Fact]
    public void ToJsonDocument_QueryFeaturesResponse_ProducesExistingJsonEnvelope()
    {
        var response = new GrpcModels.QueryFeaturesResponse
        {
            ObjectIdFieldName = "objectid",
            GeometryType = GrpcModels.GeometryType.Point,
            SpatialReference = new GrpcModels.SpatialReference { Wkid = 4326 },
            Fields =
            [
                new GrpcModels.FieldDefinition
                {
                    Name = "name",
                    FieldType = GrpcModels.FieldType.String,
                    Length = 64,
                    Nullable = true,
                },
            ],
            Features =
            [
                new GrpcModels.Feature
                {
                    Id = 99,
                    Attributes = new Dictionary<string, object?>
                    {
                        ["name"] = "Test",
                        ["count"] = 5L,
                    },
                    Geometry = new Dictionary<string, object?>
                    {
                        ["x"] = -157.8583,
                        ["y"] = 21.3069,
                    },
                },
            ],
            Count = 1,
        };

        using var json = SdkGrpcTransportMappings.ToJsonDocument(response);
        var root = json.RootElement;
        var feature = root.GetProperty("features")[0];

        Assert.Equal("objectid", root.GetProperty("objectIdFieldName").GetString());
        Assert.Equal("Point", root.GetProperty("geometryType").GetString());
        Assert.Equal(4326, root.GetProperty("spatialReference").GetProperty("wkid").GetInt32());
        Assert.Equal("name", root.GetProperty("fields")[0].GetProperty("name").GetString());
        Assert.Equal(99L, feature.GetProperty("id").GetInt64());
        Assert.Equal("Test", feature.GetProperty("attributes").GetProperty("name").GetString());
        Assert.Equal(5L, feature.GetProperty("attributes").GetProperty("count").GetInt64());
        Assert.Equal(-157.8583, feature.GetProperty("geometry").GetProperty("x").GetDouble(), 4);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Honua.Mobile.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Honua.Mobile.sln from the test output directory.");
    }
}
