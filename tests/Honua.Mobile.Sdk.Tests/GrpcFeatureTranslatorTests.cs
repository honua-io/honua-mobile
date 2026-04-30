using System.Text.Json;
using Honua.Mobile.Sdk.Grpc;
using Honua.Mobile.Sdk.Models;
using Honua.Sdk.GeoServices.FeatureServer.Models;
using Proto = Honua.Server.Features.Grpc.Proto;

namespace Honua.Mobile.Sdk.Tests;

public sealed class GrpcFeatureTranslatorTests
{
    [Fact]
    public void ToProtoQueryRequest_MapsQueryFields()
    {
        var request = new QueryFeaturesRequest
        {
            ServiceId = "default",
            LayerId = 7,
            Where = "status = 'open'",
            ObjectIds = [1, 2, 3],
            OutFields = ["objectid", "status"],
            ReturnGeometry = false,
            ResultOffset = 50,
            ResultRecordCount = 100,
            OrderBy = "status ASC",
            ReturnDistinct = true,
            ReturnCountOnly = false,
            ReturnIdsOnly = true,
            ReturnExtentOnly = false,
        };

        var proto = GrpcFeatureTranslator.ToProtoQueryRequest(request);

        Assert.Equal("default", proto.ServiceId);
        Assert.Equal(7, proto.LayerId);
        Assert.Equal("status = 'open'", proto.Where);
        Assert.Equal([1L, 2L, 3L], proto.ObjectIds);
        Assert.Equal(["objectid", "status"], proto.OutFields);
        Assert.False(proto.ReturnGeometry);
        Assert.Equal(50, proto.ResultOffset);
        Assert.Equal(100, proto.ResultRecordCount);
        Assert.Equal("status ASC", proto.OrderBy);
        Assert.True(proto.ReturnDistinct);
        Assert.True(proto.ReturnIdsOnly);
    }

    [Fact]
    public void ToProtoApplyEditsRequest_MapsAddsUpdatesDeletes()
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

        var proto = GrpcFeatureTranslator.ToProtoApplyEditsRequest(request);

        Assert.Equal("default", proto.ServiceId);
        Assert.Equal(0, proto.LayerId);
        Assert.Single(proto.Adds);
        Assert.Single(proto.Updates);
        Assert.Equal([9L, 10L], proto.Deletes);
        Assert.True(proto.RollbackOnFailure);
        Assert.True(proto.ForceWrite);

        Assert.Equal("A-1", proto.Adds[0].Attributes["asset_id"].StringValue);
        Assert.Equal(3L, proto.Adds[0].Attributes["priority"].Int64Value);
        Assert.Equal(-157.8583, proto.Adds[0].Geometry.Point.X, 4);
        Assert.Equal(21.3069, proto.Adds[0].Geometry.Point.Y, 4);

        Assert.Equal(11L, proto.Updates[0].Id);
        Assert.Equal(4L, proto.Updates[0].Attributes["priority"].Int64Value);
    }

    [Fact]
    public void ToProtoApplyEditsRequest_MapsSdkFeatureServerModels()
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

        var proto = GrpcFeatureTranslator.ToProtoApplyEditsRequest(request);

        Assert.Single(proto.Adds);
        Assert.Equal([9L, 10L], proto.Deletes);
        Assert.Equal("A-1", proto.Adds[0].Attributes["asset_id"].StringValue);
        Assert.Equal(3L, proto.Adds[0].Attributes["priority"].Int64Value);
        Assert.Equal(-157.8583, proto.Adds[0].Geometry.Point.X, 4);
        Assert.Equal(21.3069, proto.Adds[0].Geometry.Point.Y, 4);
    }

    [Fact]
    public void ToJsonDocument_ApplyEditsResponse_ProducesEsriLikeEnvelope()
    {
        var response = new Proto.ApplyEditsResponse();
        response.AddResults.Add(new Proto.EditResult { ObjectId = 1, Success = true });
        response.UpdateResults.Add(new Proto.EditResult
        {
            ObjectId = 2,
            Success = false,
            Error = new Proto.EditError { Code = 409, Message = "conflict" },
        });

        using var json = GrpcFeatureTranslator.ToJsonDocument(response);
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("addResults", out var addResults));
        Assert.True(root.TryGetProperty("updateResults", out var updateResults));
        Assert.Equal(1, addResults.GetArrayLength());
        Assert.Equal(1, updateResults.GetArrayLength());

        Assert.True(addResults[0].GetProperty("success").GetBoolean());
        Assert.False(updateResults[0].GetProperty("success").GetBoolean());
        Assert.Equal(409, updateResults[0].GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void ToJsonDocument_QueryFeaturesResponse_MapsAttributesAndGeometry()
    {
        var response = new Proto.QueryFeaturesResponse
        {
            ObjectIdFieldName = "objectid",
            GeometryType = Proto.GeometryType.Point,
            SpatialReference = new Proto.SpatialReference { Wkid = 4326 },
        };

        response.Features.Add(new Proto.Feature
        {
            Id = 99,
            Geometry = new Proto.Geometry { Point = new Proto.PointGeometry { X = -157.8583, Y = 21.3069 } },
            Attributes =
            {
                ["name"] = new Proto.AttributeValue { StringValue = "Test" },
                ["count"] = new Proto.AttributeValue { Int64Value = 5 },
                ["active"] = new Proto.AttributeValue { BoolValue = true },
            },
        });

        using var json = GrpcFeatureTranslator.ToJsonDocument(response);
        var feature = json.RootElement.GetProperty("features")[0];

        Assert.Equal(99L, feature.GetProperty("id").GetInt64());
        Assert.Equal("Test", feature.GetProperty("attributes").GetProperty("name").GetString());
        Assert.Equal(5L, feature.GetProperty("attributes").GetProperty("count").GetInt64());
        Assert.True(feature.GetProperty("attributes").GetProperty("active").GetBoolean());
        Assert.Equal(-157.8583, feature.GetProperty("geometry").GetProperty("x").GetDouble(), 4);
    }

    [Fact]
    public void ToProtoApplyEditsRequest_MapsMOnlyCoordinatesWhenHasMTrue()
    {
        var request = new ApplyEditsRequest
        {
            ServiceId = "default",
            LayerId = 0,
            AddsJson = """
            [
              {
                "attributes": { "asset_id": "A-1" },
                "geometry":
                {
                  "hasM": true,
                  "paths": [
                    [
                      [1.0, 2.0, 9.5],
                      [3.0, 4.0, 8.5]
                    ]
                  ]
                }
              }
            ]
            """,
        };

        var proto = GrpcFeatureTranslator.ToProtoApplyEditsRequest(request);
        var firstCoord = proto.Adds[0].Geometry.Polyline.Paths[0].Coords[0];

        Assert.Equal(1.0, firstCoord.X);
        Assert.Equal(2.0, firstCoord.Y);
        Assert.Equal(9.5, firstCoord.M);
        Assert.Equal(0.0, firstCoord.Z);
    }
}
