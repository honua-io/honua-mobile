namespace Honua.Mobile.Offline.GeoPackage;

public sealed class GeoPackageSyncStoreOptions
{
    public required string DatabasePath { get; init; }

    public bool AutoCreateDirectory { get; init; } = true;
}

public enum OfflineOperationType
{
    Add,
    Update,
    Delete,
}

public sealed class OfflineEditOperation
{
    public string OperationId { get; init; } = Guid.NewGuid().ToString("N");

    public required string LayerKey { get; init; }

    public required string TargetCollection { get; init; }

    public required OfflineOperationType OperationType { get; init; }

    public required string PayloadJson { get; init; }

    public int Priority { get; init; } = 100;

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public int AttemptCount { get; init; }
}

public sealed record BoundingBox(double MinLongitude, double MinLatitude, double MaxLongitude, double MaxLatitude);

public sealed class MapAreaPackage
{
    public required string AreaId { get; init; }

    public required string Name { get; init; }

    public required BoundingBox BoundingBox { get; init; }

    public int MinZoom { get; init; }

    public int MaxZoom { get; init; }

    public required string GeoPackagePath { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
