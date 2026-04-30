using Honua.Sdk.GeoServices.FeatureServer.Models;

namespace Honua.Mobile.Sdk.Models;

public sealed class QueryFeaturesRequest
{
    public required string ServiceId { get; init; }

    public required int LayerId { get; init; }

    public string Where { get; init; } = "1=1";

    public IReadOnlyList<long>? ObjectIds { get; init; }

    public IReadOnlyList<string>? OutFields { get; init; }

    public bool ReturnGeometry { get; init; } = true;

    public int? ResultOffset { get; init; }

    public int? ResultRecordCount { get; init; }

    public string? OrderBy { get; init; }

    public bool ReturnDistinct { get; init; }

    public bool ReturnCountOnly { get; init; }

    public bool ReturnIdsOnly { get; init; }

    public bool ReturnExtentOnly { get; init; }

    public string ResponseFormat { get; init; } = "json";
}

public sealed class ApplyEditsRequest
{
    public required string ServiceId { get; init; }

    public required int LayerId { get; init; }

    public IReadOnlyList<FeatureServerFeature>? Adds { get; init; }

    public IReadOnlyList<FeatureServerFeature>? Updates { get; init; }

    public IReadOnlyList<long>? Deletes { get; init; }

    public string? AddsJson { get; init; }

    public string? UpdatesJson { get; init; }

    public string? DeletesCsv { get; init; }

    public bool RollbackOnFailure { get; init; } = false;

    public bool ForceWrite { get; init; } = false;

    public string ResponseFormat { get; init; } = "json";
}

public sealed class OgcItemsRequest
{
    public required string CollectionId { get; init; }

    public int? Limit { get; init; }

    public int? Offset { get; init; }

    public IReadOnlyList<string>? PropertyNames { get; init; }

    public string? CqlFilter { get; init; }

    public string ResponseFormat { get; init; } = "json";
}

public sealed class OgcCreateItemRequest
{
    public required string CollectionId { get; init; }

    public required object Feature { get; init; }
}

public sealed class OgcReplaceItemRequest
{
    public required string CollectionId { get; init; }

    public required string FeatureId { get; init; }

    public required object Feature { get; init; }
}

public sealed class OgcPatchItemRequest
{
    public required string CollectionId { get; init; }

    public required string FeatureId { get; init; }

    public required object Patch { get; init; }
}

public sealed class OgcDeleteItemRequest
{
    public required string CollectionId { get; init; }

    public required string FeatureId { get; init; }
}
