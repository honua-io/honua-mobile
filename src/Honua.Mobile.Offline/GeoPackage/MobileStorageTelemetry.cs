using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Honua.Mobile.Offline.GeoPackage;

/// <summary>
/// Telemetry sources emitted by the Honua mobile storage layer.
/// </summary>
public static class MobileStorageTelemetry
{
    /// <summary>
    /// Activity source name for storage operations.
    /// </summary>
    public const string ActivitySourceName = "Honua.Mobile.Storage";

    /// <summary>
    /// Meter name for storage metrics.
    /// </summary>
    public const string MeterName = "Honua.Mobile.Storage";

    /// <summary>
    /// Activity source for local GeoPackage operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Queries = Meter.CreateCounter<long>(
        "mobile_storage_queries_total",
        description: "Number of local mobile storage feature queries.");
    private static readonly Counter<long> Evictions = Meter.CreateCounter<long>(
        "mobile_storage_evictions_total",
        description: "Number of cached features evicted from mobile storage.");
    private static readonly UpDownCounter<long> SizeBytes = Meter.CreateUpDownCounter<long>(
        "mobile_storage_size_bytes",
        unit: "bytes",
        description: "Observed mobile storage size in bytes.");

    /// <summary>
    /// Records a local feature query.
    /// </summary>
    /// <param name="result">Query outcome.</param>
    public static void RecordQuery(string result)
        => Queries.Add(1, new KeyValuePair<string, object?>("result", result));

    /// <summary>
    /// Records evicted feature rows.
    /// </summary>
    /// <param name="count">Number of rows evicted.</param>
    public static void RecordEvictions(long count)
        => Evictions.Add(count);

    /// <summary>
    /// Records an observed storage size.
    /// </summary>
    /// <param name="bytes">Storage size in bytes.</param>
    public static void RecordSizeBytes(long bytes)
        => SizeBytes.Add(bytes);
}
