using System.Globalization;
using Microsoft.Data.Sqlite;
using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.MapAreas;

public sealed class MapAreaDownloader : IMapAreaDownloader
{
    private readonly HttpClient _httpClient;
    private readonly IGeoPackageSyncStore _syncStore;

    public MapAreaDownloader(HttpClient httpClient, IGeoPackageSyncStore syncStore)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _syncStore = syncStore ?? throw new ArgumentNullException(nameof(syncStore));
    }

    public async Task<MapAreaDownloadResult> DownloadAsync(MapAreaDownloadRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Layers.Count == 0)
        {
            throw new InvalidOperationException("At least one map layer source is required.");
        }

        Directory.CreateDirectory(request.OutputDirectory);

        var packagePath = Path.Combine(request.OutputDirectory, $"{request.AreaId}.gpkg");
        await using var packageConnection = new SqliteConnection($"Data Source={packagePath}");
        await packageConnection.OpenAsync(ct).ConfigureAwait(false);

        await EnsureGeoPackageTablesAsync(packageConnection, ct).ConfigureAwait(false);

        var layerCount = 0;
        long downloadedBytes = 0;

        foreach (var layer in request.Layers.OrderBy(layer => layer.Priority))
        {
            var url = ApplyTemplate(layer.SourceUrl, request);
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (layer.Required)
                {
                    response.EnsureSuccessStatusCode();
                }

                continue;
            }

            var payload = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var contentType = layer.ContentType ?? response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

            await UpsertLayerPayloadAsync(packageConnection, layer, url, payload, contentType, ct).ConfigureAwait(false);
            layerCount++;
            downloadedBytes += payload.LongLength;
        }

        await _syncStore.InitializeAsync(ct).ConfigureAwait(false);
        await _syncStore.UpsertMapAreaAsync(new MapAreaPackage
        {
            AreaId = request.AreaId,
            Name = request.Name,
            BoundingBox = request.BoundingBox,
            MinZoom = request.MinZoom,
            MaxZoom = request.MaxZoom,
            GeoPackagePath = packagePath,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);

        return new MapAreaDownloadResult
        {
            GeoPackagePath = packagePath,
            DownloadedLayerCount = layerCount,
            DownloadedBytes = downloadedBytes,
        };
    }

    private static async Task EnsureGeoPackageTablesAsync(SqliteConnection connection, CancellationToken ct)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS gpkg_spatial_ref_sys (
    srs_name TEXT NOT NULL,
    srs_id INTEGER NOT NULL PRIMARY KEY,
    organization TEXT NOT NULL,
    organization_coordsys_id INTEGER NOT NULL,
    definition TEXT NOT NULL,
    description TEXT
);

INSERT OR IGNORE INTO gpkg_spatial_ref_sys (srs_name, srs_id, organization, organization_coordsys_id, definition, description)
VALUES ('WGS 84 geodetic', 4326, 'EPSG', 4326, 'GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563]],PRIMEM[""Greenwich"",0],UNIT[""degree"",0.0174532925199433]]', 'longitude/latitude coordinates in decimal degrees on the WGS 84 spheroid');

CREATE TABLE IF NOT EXISTS gpkg_contents (
    table_name TEXT NOT NULL PRIMARY KEY,
    data_type TEXT NOT NULL,
    identifier TEXT UNIQUE,
    description TEXT DEFAULT '',
    last_change DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    min_x DOUBLE,
    min_y DOUBLE,
    max_x DOUBLE,
    max_y DOUBLE,
    srs_id INTEGER,
    CONSTRAINT fk_gc_r_srs_id FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
);

CREATE TABLE IF NOT EXISTS honua_layer_payloads (
    layer_key TEXT PRIMARY KEY,
    source_url TEXT NOT NULL,
    priority INTEGER NOT NULL,
    content_type TEXT NOT NULL,
    payload_blob BLOB NOT NULL,
    downloaded_at_utc TEXT NOT NULL
);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_layer_payloads', 'attributes', 'honua_layer_payloads', 'Layer payload cache for offline map area package', 4326);
";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpsertLayerPayloadAsync(
        SqliteConnection connection,
        MapLayerDownloadSource layer,
        string sourceUrl,
        byte[] payload,
        string contentType,
        CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO honua_layer_payloads (layer_key, source_url, priority, content_type, payload_blob, downloaded_at_utc)
VALUES ($layer_key, $source_url, $priority, $content_type, $payload_blob, $downloaded_at_utc)
ON CONFLICT(layer_key) DO UPDATE SET
  source_url = excluded.source_url,
  priority = excluded.priority,
  content_type = excluded.content_type,
  payload_blob = excluded.payload_blob,
  downloaded_at_utc = excluded.downloaded_at_utc;
";

        cmd.Parameters.AddWithValue("$layer_key", layer.LayerKey);
        cmd.Parameters.AddWithValue("$source_url", sourceUrl);
        cmd.Parameters.AddWithValue("$priority", layer.Priority);
        cmd.Parameters.AddWithValue("$content_type", contentType);
        cmd.Parameters.AddWithValue("$payload_blob", payload);
        cmd.Parameters.AddWithValue("$downloaded_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string ApplyTemplate(string sourceUrl, MapAreaDownloadRequest request)
    {
        return sourceUrl
            .Replace("{minLon}", request.BoundingBox.MinLongitude.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{minLat}", request.BoundingBox.MinLatitude.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{maxLon}", request.BoundingBox.MaxLongitude.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{maxLat}", request.BoundingBox.MaxLatitude.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{minZoom}", request.MinZoom.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{maxZoom}", request.MaxZoom.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
