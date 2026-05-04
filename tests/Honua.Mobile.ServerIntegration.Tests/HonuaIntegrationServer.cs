using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.ServerIntegration.Tests;

internal sealed class HonuaIntegrationServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    private HonuaIntegrationServer(WebApplication app)
    {
        _app = app;
    }

    public Uri BaseUri { get; private set; } = null!;

    public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

    public static async Task<HonuaIntegrationServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "IntegrationTest",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();
        var server = new HonuaIntegrationServer(app);

        app.Use(async (context, next) =>
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;

            server._requests.Enqueue(new RecordedRequest(
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                context.Request.QueryString.Value ?? string.Empty,
                context.Request.Headers.ToDictionary(
                    header => header.Key,
                    header => header.Value
                        .Where(value => value is not null)
                        .Select(value => value!)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase),
                body));

            await next(context);
        });

        MapEndpoints(app);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .Single();
        server.BaseUri = new Uri(address);

        return server;
    }

    public Uri Uri(string relativePath) => new(BaseUri, relativePath);

    public bool Received(string method, string path)
    {
        return Requests.Any(request =>
            string.Equals(request.Method, method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.Path, path, StringComparison.Ordinal));
    }

    public RecordedRequest SingleRequest(string method, string path)
    {
        return Assert.Single(Requests, request =>
            string.Equals(request.Method, method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.Path, path, StringComparison.Ordinal));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    private static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/api/scenes", (HttpContext context) =>
        {
            return HasApiKey(context)
                ? Json("""
                    {
                      "scenes": [
                        {
                          "id": "downtown-honolulu",
                          "name": "Downtown Honolulu",
                          "capabilities": ["3d-tiles", "terrain"],
                          "requiresAuthentication": true,
                          "bounds": {
                            "minLongitude": -157.875,
                            "minLatitude": 21.275,
                            "maxLongitude": -157.8,
                            "maxLatitude": 21.325
                          }
                        }
                      ]
                    }
                    """)
                : Results.Unauthorized();
        });

        app.MapGet("/api/scenes/downtown-honolulu", () => Json("""
            {
              "id": "downtown-honolulu",
              "name": "Downtown Honolulu",
              "tilesetUrl": "http://localhost/api/scenes/downtown-honolulu/tileset.json",
              "terrainUrl": "http://localhost/api/scenes/downtown-honolulu/terrain",
              "capabilities": ["3d-tiles", "terrain"],
              "center": { "latitude": 21.3069, "longitude": -157.8583 }
            }
            """));

        app.MapGet("/api/scenes/downtown-honolulu/resolve", () => Json("""
            {
              "sceneId": "downtown-honolulu",
              "tilesetUrl": "http://localhost/api/scenes/downtown-honolulu/tileset.json?sig=test",
              "terrainUrl": "http://localhost/api/scenes/downtown-honolulu/terrain?sig=test",
              "capabilities": ["3d-tiles", "terrain"],
              "requiresAuthentication": true,
              "endpoints": [
                {
                  "type": "3d-tiles",
                  "url": "http://localhost/api/scenes/downtown-honolulu/tileset.json?sig=test",
                  "format": "3d-tiles",
                  "headers": { "X-Honua-Scene": "downtown-honolulu" }
                }
              ]
            }
            """));

        app.MapPost("/api/mobile/exceptions", () => Results.Ok(new { accepted = true }));
        app.MapPost("/oauth/token", () => Json("""
            {
              "accessToken": "refreshed-access-token",
              "refreshToken": "next-refresh-token",
              "tokenType": "Bearer",
              "expiresIn": 3600
            }
            """));

        app.MapGet("/rest/services/{serviceId}/FeatureServer/{layerId:int}/query", (
            string serviceId,
            int layerId) => Json("""
            {
              "objectIdFieldName": "objectid",
              "count": 1,
              "features": [
                {
                  "attributes": { "objectid": 1, "name": "Pump Station" },
                  "geometry": { "x": -157.8, "y": 21.3 }
                }
              ]
            }
            """));

        app.MapPost("/rest/services/{serviceId}/FeatureServer/{layerId:int}/applyEdits", (
            string serviceId,
            int layerId) => Json("""
            {
              "addResults": [{ "objectId": 42, "success": true }],
              "updateResults": [{ "objectId": 43, "success": true }],
              "deleteResults": [{ "objectId": 7, "success": true }]
            }
            """));

        app.MapGet("/rest/services/{serviceId}/FeatureServer/{layerId:int}/{objectId:long}/attachments", () => Json("""
            {
              "attachmentInfos": [
                {
                  "id": 7,
                  "parentObjectId": 42,
                  "name": "photo.txt",
                  "contentType": "text/plain",
                  "size": 5,
                  "keywords": "field"
                }
              ]
            }
            """));

        app.MapGet("/rest/services/{serviceId}/FeatureServer/{layerId:int}/{objectId:long}/attachments/{attachmentId:long}", () =>
            Results.Bytes(Encoding.UTF8.GetBytes("photo"), "text/plain", "photo.txt"));

        app.MapPost("/rest/services/{serviceId}/FeatureServer/{layerId:int}/{objectId:long}/addAttachment", () => Json("""
            { "addAttachmentResult": { "objectId": 8, "success": true } }
            """));
        app.MapPost("/rest/services/{serviceId}/FeatureServer/{layerId:int}/{objectId:long}/updateAttachment", () => Json("""
            { "updateAttachmentResult": { "objectId": 7, "success": true } }
            """));
        app.MapPost("/rest/services/{serviceId}/FeatureServer/{layerId:int}/{objectId:long}/deleteAttachments", () => Json("""
            { "deleteAttachmentResults": [{ "objectId": 7, "success": true }] }
            """));

        app.MapGet("/ogc/features/collections", () => Json("""
            { "collections": [{ "id": "buildings", "title": "Buildings" }] }
            """));
        app.MapGet("/ogc/features/collections/{collectionId}/items", () => Json("""
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "id": "building-1",
                  "properties": { "name": "HQ" },
                  "geometry": { "type": "Point", "coordinates": [-157.8, 21.3] }
                }
              ]
            }
            """));
        app.MapPost("/ogc/features/collections/{collectionId}/items", () => Json("""
            { "type": "Feature", "id": "building-created", "properties": { "name": "HQ" } }
            """));
        app.MapPut("/ogc/features/collections/{collectionId}/items/{featureId}", (
            string featureId) => Json($$"""
            { "type": "Feature", "id": "{{featureId}}", "properties": { "replaced": true } }
            """));
        app.MapPatch("/ogc/features/collections/{collectionId}/items/{featureId}", (
            string featureId) => Json($$"""
            { "type": "Feature", "id": "{{featureId}}", "properties": { "patched": true } }
            """));
        app.MapDelete("/ogc/features/collections/{collectionId}/items/{featureId}", (
            string featureId) => Json($$"""
            { "id": "{{featureId}}", "deleted": true }
            """));

        app.MapPost("/rest/services/Routing/NAServer/Route/solve", () => Json("""
            {
              "routes": {
                "features": [
                  { "attributes": { "Name": "Route 1", "Total_Length": 10.5, "Total_Time": 25 } }
                ]
              },
              "directions": [
                {
                  "features": [
                    { "attributes": { "text": "Head east", "length": 0.1, "time": 1.2, "maneuverType": "esriDMTDepart" } }
                  ]
                }
              ]
            }
            """));
        app.MapPost("/rest/services/Routing/NAServer/ServiceArea/solveServiceArea", () => Json("{}"));
        app.MapPost("/rest/services/Routing/NAServer/ClosestFacility/solveClosestFacility", () => Json("""
            {
              "directions": [
                {
                  "summary": { "routeName": "Incident - Facility A", "totalLength": 2.5, "totalTime": 8 }
                }
              ]
            }
            """));

        app.MapPost("/rest/services/offline/FeatureServer/createReplica", () => Json("""
            { "replicaID": "replica-abc-123", "serverGen": 42 }
            """));
        app.MapPost("/rest/services/offline/FeatureServer/extractChanges", () => Json("""
            {
              "serverGen": 55,
              "layerChanges": [
                {
                  "id": 0,
                  "addFeatures": [{ "attributes": { "objectid": 1, "name": "New Feature" } }],
                  "updateFeatures": [{ "attributes": { "objectid": 2, "name": "Updated Feature" } }],
                  "deleteIds": [3, 4]
                }
              ]
            }
            """));
        app.MapPost("/rest/services/offline/FeatureServer/synchronizeReplica", () => Json("""
            { "serverGen": 100 }
            """));
        app.MapPost("/rest/services/offline/FeatureServer/unRegisterReplica", () => Json("""
            { "success": true }
            """));

        app.MapGet("/tiles/{layerKey}", (string layerKey) =>
            Results.Bytes(Encoding.UTF8.GetBytes($"tile-payload:{layerKey}"), "application/octet-stream"));
        app.MapGet("/scene-assets/{**assetPath}", (string assetPath, HttpContext context) =>
        {
            var payload = Encoding.UTF8.GetBytes(assetPath.Contains("tileset", StringComparison.OrdinalIgnoreCase)
                ? """{"asset":{"version":"1.1"}}"""
                : """{"sceneId":"downtown-honolulu"}""");
            context.Response.Headers.ETag = assetPath.Contains("tileset", StringComparison.OrdinalIgnoreCase)
                ? "\"tiles-1\""
                : "\"meta-1\"";
            return Results.Bytes(payload, "application/octet-stream");
        });
    }

    private static IResult Json(string json) => Results.Content(json, "application/json");

    private static bool HasApiKey(HttpContext context)
        => context.Request.Headers.ContainsKey("X-API-Key");

    public static string Sha256Hex(byte[] payload)
        => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
}

internal sealed record RecordedRequest(
    string Method,
    string Path,
    string Query,
    IReadOnlyDictionary<string, string[]> Headers,
    string Body)
{
    public string PathAndQuery => Path + Query;

    public string? Header(string name)
        => Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
}
