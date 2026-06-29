using System.Text;
using System.Text.Json;
using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.Sdk.Tests;

public sealed class LegacyFeatureQueryEnvelopeTests
{
    [Fact]
    public void ToLegacyFeatureQueryJsonDocument_NormalQuery_OmitsTopLevelCount()
    {
        var result = new FeatureQueryResult
        {
            NumberMatched = 42,
            Features =
            [
                new FeatureRecord { Id = "1" },
            ],
        };

        using var document = HonuaMobileClient.ToLegacyFeatureQueryJsonDocument(result, returnCountOnly: false);

        // Esri reserves the top-level "count" for returnCountOnly responses; a normal feature
        // query must not emit it alongside features.
        Assert.False(document.RootElement.TryGetProperty("count", out _));
        Assert.True(document.RootElement.TryGetProperty("features", out var features));
        Assert.Equal(1, features.GetArrayLength());
    }

    [Fact]
    public void ToLegacyFeatureQueryJsonDocument_ReturnCountOnly_EmitsTopLevelCount()
    {
        var result = new FeatureQueryResult
        {
            NumberMatched = 42,
            Features = [],
        };

        using var document = HonuaMobileClient.ToLegacyFeatureQueryJsonDocument(result, returnCountOnly: true);

        Assert.True(document.RootElement.TryGetProperty("count", out var count));
        Assert.Equal(42, count.GetInt64());
    }

    // --- REST-vs-gRPC envelope parity (honua-mobile#314) -------------------------------------
    //
    // The public QueryFeaturesAsync surface serves the same logical result over either gRPC
    // (projected through ToLegacyFeatureQueryJsonDocument) or REST (the server's raw Esri-JSON
    // passthrough). These tests pin the conformant, transport-invariant envelope: for the keys
    // both transports can produce, the gRPC projection must match the REST passthrough byte-for-
    // byte. The one documented divergence is that the gRPC FeatureQueryResult contract does not
    // carry spatialReference/geometryType/fields, so the gRPC envelope omits them and callers
    // read the spatial reference from layer metadata.

    /// <summary>
    /// The canonical Esri-JSON envelope a conformant FeatureServer returns over REST for a normal
    /// (non-count) query. The gRPC projection is expected to reproduce every key here except the
    /// documented spatialReference/geometryType/fields gap.
    /// </summary>
    private const string RestPassthroughEnvelope = """
    {
        "objectIdFieldName": "objectid",
        "geometryType": "esriGeometryPoint",
        "spatialReference": { "wkid": 4326, "latestWkid": 4326 },
        "fields": [
            { "name": "objectid", "type": "esriFieldTypeOID", "alias": "OBJECTID" },
            { "name": "name", "type": "esriFieldTypeString", "alias": "Name" }
        ],
        "exceededTransferLimit": true,
        "features": [
            { "attributes": { "objectid": 1, "name": "Pump Station" }, "geometry": { "x": -157.8, "y": 21.3 } },
            { "attributes": { "objectid": 2, "name": "Reservoir" }, "geometry": { "x": -157.9, "y": 21.4 } }
        ]
    }
    """;

    [Fact]
    public void GrpcProjection_MatchesRestPassthrough_ForSharedEnvelopeKeys()
    {
        using var rest = JsonDocument.Parse(RestPassthroughEnvelope);

        // The provider-neutral FeatureQueryResult is what the gRPC transport yields for the same
        // logical data the REST passthrough above describes.
        var grpcResult = new FeatureQueryResult
        {
            ObjectIdFieldName = "objectid",
            HasMoreResults = true,
            NumberMatched = 2,
            Features =
            [
                Feature("""{ "objectid": 1, "name": "Pump Station" }""", """{ "x": -157.8, "y": 21.3 }"""),
                Feature("""{ "objectid": 2, "name": "Reservoir" }""", """{ "x": -157.9, "y": 21.4 }"""),
            ],
        };

        using var grpc = HonuaMobileClient.ToLegacyFeatureQueryJsonDocument(grpcResult, returnCountOnly: false);

        // objectIdFieldName is transport-invariant.
        Assert.Equal(
            rest.RootElement.GetProperty("objectIdFieldName").GetString(),
            grpc.RootElement.GetProperty("objectIdFieldName").GetString());

        // exceededTransferLimit is transport-invariant.
        Assert.Equal(
            rest.RootElement.GetProperty("exceededTransferLimit").GetBoolean(),
            grpc.RootElement.GetProperty("exceededTransferLimit").GetBoolean());

        // The features array (attributes + geometry) must be identical across transports.
        var restFeatures = rest.RootElement.GetProperty("features");
        var grpcFeatures = grpc.RootElement.GetProperty("features");
        Assert.Equal(restFeatures.GetArrayLength(), grpcFeatures.GetArrayLength());
        for (var i = 0; i < restFeatures.GetArrayLength(); i++)
        {
            Assert.Equal(
                Canonical(restFeatures[i].GetProperty("attributes")),
                Canonical(grpcFeatures[i].GetProperty("attributes")));
            Assert.Equal(
                Canonical(restFeatures[i].GetProperty("geometry")),
                Canonical(grpcFeatures[i].GetProperty("geometry")));
        }
    }

    [Fact]
    public void GrpcProjection_NormalQuery_EmitsOnlyConformantEsriEnvelopeKeys()
    {
        var grpcResult = new FeatureQueryResult
        {
            ObjectIdFieldName = "objectid",
            HasMoreResults = true,
            NumberMatched = 1,
            Features = [Feature("""{ "objectid": 1 }""", """{ "x": 0, "y": 0 }""")],
        };

        using var grpc = HonuaMobileClient.ToLegacyFeatureQueryJsonDocument(grpcResult, returnCountOnly: false);

        // Every top-level key the gRPC projection emits must be a recognized Esri query-envelope
        // key, so callers cannot observe a non-conformant field that depends on transport.
        var conformantKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "objectIdFieldName", "globalIdFieldName", "geometryType", "spatialReference",
            "fields", "features", "objectIds", "exceededTransferLimit", "count",
        };
        foreach (var property in grpc.RootElement.EnumerateObject())
        {
            Assert.Contains(property.Name, conformantKeys);
        }

        // Documented gap: the gRPC contract does not carry these, so they are intentionally absent
        // (callers read the spatial reference from layer metadata on the gRPC path).
        Assert.False(grpc.RootElement.TryGetProperty("spatialReference", out _));
        Assert.False(grpc.RootElement.TryGetProperty("geometryType", out _));
        Assert.False(grpc.RootElement.TryGetProperty("fields", out _));
    }

    private static FeatureRecord Feature(string attributesJson, string geometryJson)
    {
        using var attributes = JsonDocument.Parse(attributesJson);
        using var geometry = JsonDocument.Parse(geometryJson);
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in attributes.RootElement.EnumerateObject())
        {
            map[property.Name] = property.Value.Clone();
        }

        return new FeatureRecord
        {
            Attributes = map,
            Geometry = geometry.RootElement.Clone(),
        };
    }

    // Normalizes an element to a transport-order-independent string by sorting object keys, so the
    // comparison pins value parity rather than incidental property ordering.
    private static string Canonical(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(writer, element);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

public sealed class GrpcEditFallbackOptionTests
{
    [Fact]
    public void AllowRestFallbackOnGrpcEditFailure_DefaultsToOff()
    {
        // Queries are safe to retry over REST, but edits are not idempotent, so the edit-specific
        // fallback must default off to avoid double-applying an edit that already reached the server.
        var options = new HonuaMobileClientOptions();

        Assert.True(options.AllowRestFallbackOnGrpcFailure);
        Assert.False(options.AllowRestFallbackOnGrpcEditFailure);
    }
}
