using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;
using CoreModels = Honua.Mobile.FieldCollection.Models;
using StorageBoundingBox = Honua.Mobile.FieldCollection.Services.Storage.Models.BoundingBox;
using StorageSpatialQuery = Honua.Mobile.FieldCollection.Services.Storage.Models.SpatialQuery;
using StorageSpatialRelationship = Honua.Mobile.FieldCollection.Services.Storage.Models.SpatialRelationship;

namespace Honua.Mobile.FieldCollection.Services.Features;

/// <summary>
/// Real implementation of IFeatureService using GeoPackage storage
/// Provides spatial querying, feature management, and offline capabilities
/// </summary>
public class GeoPackageFeatureService : IFeatureService
{
    private static readonly Regex WherePredicateRegex = new(
        @"^\s*(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*(?<op>==|!=|<>|>=|<=|=|>|<)\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly GeoPackageStorageService _storage;
    private readonly ISyncService _syncService;

    public GeoPackageFeatureService(
        GeoPackageStorageService storage,
        ISyncService syncService)
    {
        _storage = storage;
        _syncService = syncService;
    }

    #region Feature Retrieval

    public async Task<IEnumerable<Feature>> GetFeaturesAsync(int layerId, CoreModels.Polygon? spatialFilter = null)
    {
        try
        {
            if (spatialFilter != null)
            {
                return await QueryFeaturesAsync(layerId, new FeatureQuery
                {
                    SpatialFilter = new SpatialFilter { Geometry = spatialFilter }
                });
            }

            return await _storage.QueryFeaturesAsync(layerId);
        }
        catch (Exception)
        {
            // Return empty list on error rather than throwing
            return new List<Feature>();
        }
    }

    public async Task<Feature?> GetFeatureAsync(int layerId, string featureId)
    {
        try
        {
            return await _storage.GetFeatureAsync(featureId, layerId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<Feature>> QueryFeaturesAsync(int layerId, FeatureQuery query)
    {
        try
        {
            // Convert FeatureQuery to SpatialQuery for storage layer
            StorageSpatialQuery? spatialQuery = null;

            if (query.SpatialFilter != null)
            {
                spatialQuery = new StorageSpatialQuery
                {
                    Bounds = ConvertToBoundingBox(query.SpatialFilter),
                    Relationship = ConvertToSpatialRelationship(query.SpatialFilter.Relationship),
                    MaxResults = string.IsNullOrWhiteSpace(query.WhereClause) && query.OrderBy?.Any() != true
                        ? query.MaxResults
                        : null
                };
            }

            var features = await _storage.QueryFeaturesAsync(layerId, spatialQuery);

            // Apply additional filters in memory
            if (!string.IsNullOrEmpty(query.WhereClause))
            {
                features = ApplyWhereClause(features, query.WhereClause);
            }

            if (query.OrderBy?.Any() == true)
            {
                features = ApplyOrderBy(features, query.OrderBy);
            }

            if (query.MaxResults.HasValue)
            {
                features = features.Take(query.MaxResults.Value).ToList();
            }

            return features;
        }
        catch (Exception ex) when (ex is not NotSupportedException and not ArgumentException)
        {
            return new List<Feature>();
        }
    }

    #endregion

    #region Feature Modification

    public async Task<Feature> CreateFeatureAsync(int layerId, Feature feature)
    {
        try
        {
            feature.LayerId = layerId;

            // Ensure feature has an ID
            if (string.IsNullOrEmpty(feature.Id))
            {
                feature.Id = Guid.NewGuid().ToString();
            }

            // Set creation metadata
            feature.CreatedAt = DateTime.UtcNow;
            feature.ModifiedAt = DateTime.UtcNow;
            feature.UpdatedAt = feature.ModifiedAt;
            feature.Version = 1;

            await _storage.StoreFeatureAsync(feature);
            return feature;
        }
        catch (Exception)
        {
            throw new InvalidOperationException($"Failed to create feature: {feature.Id}");
        }
    }

    public async Task<Feature> UpdateFeatureAsync(int layerId, Feature feature)
    {
        try
        {
            feature.LayerId = layerId;

            // Update modification metadata
            feature.ModifiedAt = DateTime.UtcNow;
            feature.UpdatedAt = feature.ModifiedAt;
            feature.Version++; // Increment version for optimistic locking

            var updated = await _storage.UpdateFeatureAsync(feature);
            if (!updated)
            {
                throw new InvalidOperationException($"Feature not found: {feature.Id}");
            }

            return feature;
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task DeleteFeatureAsync(int layerId, string featureId)
    {
        try
        {
            await _storage.DeleteFeatureAsync(featureId, layerId);
        }
        catch (Exception)
        {
            throw;
        }
    }

    #endregion

    #region Bulk Operations

    public async Task<BatchResult> CreateFeaturesAsync(List<Feature> features)
    {
        var result = new BatchResult();
        var successful = new List<string>();
        var failed = new List<(string Id, string Error)>();

        foreach (var feature in features)
        {
            try
            {
                var created = await CreateFeatureAsync(feature.LayerId, feature);
                successful.Add(created.Id);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                failed.Add((feature.Id, ex.Message));
                result.ErrorCount++;
            }
        }

        result.SuccessfulIds = successful;
        result.FailedItems = failed.Select(f => new BatchError
        {
            Id = f.Id,
            ErrorMessage = f.Error
        }).ToList();

        return result;
    }

    public async Task<BatchResult> UpdateFeaturesAsync(List<Feature> features)
    {
        var result = new BatchResult();
        var successful = new List<string>();
        var failed = new List<(string Id, string Error)>();

        foreach (var feature in features)
        {
            try
            {
                var updated = await UpdateFeatureAsync(feature.LayerId, feature);
                successful.Add(updated.Id);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                failed.Add((feature.Id, ex.Message));
                result.ErrorCount++;
            }
        }

        result.SuccessfulIds = successful;
        result.FailedItems = failed.Select(f => new BatchError
        {
            Id = f.Id,
            ErrorMessage = f.Error
        }).ToList();

        return result;
    }

    public async Task<BatchResult> DeleteFeaturesAsync(int layerId, List<string> featureIds)
    {
        var result = new BatchResult();
        var successful = new List<string>();
        var failed = new List<(string Id, string Error)>();

        foreach (var featureId in featureIds)
        {
            try
            {
                await DeleteFeatureAsync(layerId, featureId);
                successful.Add(featureId);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                failed.Add((featureId, ex.Message));
                result.ErrorCount++;
            }
        }

        result.SuccessfulIds = successful;
        result.FailedItems = failed.Select(f => new BatchError
        {
            Id = f.Id,
            ErrorMessage = f.Error
        }).ToList();

        return result;
    }

    #endregion

    #region Statistics and Metadata

    public async Task<FeatureStatistics> GetFeatureStatisticsAsync(int layerId)
    {
        try
        {
            var features = await _storage.QueryFeaturesAsync(layerId);
            var pendingChanges = await _storage.GetPendingChangesAsync(layerId);

            return new FeatureStatistics
            {
                LayerId = layerId,
                TotalFeatures = features.Count,
                PendingSync = pendingChanges.Count,
                LastModified = features.Count > 0
                    ? features.Max(f => f.ModifiedAt ?? f.UpdatedAt ?? f.CreatedAt)
                    : DateTime.MinValue,
                SizeEstimateMB = EstimateLayerSize(features)
            };
        }
        catch (Exception)
        {
            return new FeatureStatistics { LayerId = layerId };
        }
    }

    public async Task<List<LayerInfo>> GetLayersAsync()
    {
        try
        {
            return await _storage.GetLayersAsync();
        }
        catch (Exception)
        {
            return new List<LayerInfo>();
        }
    }

    #endregion

    #region Helper Methods

    private static StorageBoundingBox ConvertToBoundingBox(SpatialFilter spatialFilter)
    {
        if (spatialFilter.Geometry is CoreModels.Point point)
        {
            var buffer = 0.001; // ~100m buffer for point queries
            return StorageBoundingBox.FromCoordinates(
                point.Longitude - buffer, point.Latitude - buffer,
                point.Longitude + buffer, point.Latitude + buffer);
        }

        if (spatialFilter.Geometry is CoreModels.LineString line && line.Coordinates.Count > 0)
        {
            return ConvertPointsToBoundingBox(line.Coordinates);
        }

        if (spatialFilter.Geometry is CoreModels.Polygon polygon)
        {
            var points = polygon.Coordinates.SelectMany(ring => ring).ToList();
            if (points.Count > 0)
            {
                return ConvertPointsToBoundingBox(points);
            }
        }

        throw new NotSupportedException("Spatial filters require a point, line, or polygon with coordinates.");
    }

    private static StorageBoundingBox ConvertPointsToBoundingBox(IEnumerable<CoreModels.Point> points)
    {
        var pointList = points.ToList();
        return StorageBoundingBox.FromCoordinates(
            pointList.Min(point => point.Longitude),
            pointList.Min(point => point.Latitude),
            pointList.Max(point => point.Longitude),
            pointList.Max(point => point.Latitude));
    }

    private static StorageSpatialRelationship ConvertToSpatialRelationship(CoreModels.SpatialRelationship relationship)
    {
        return relationship switch
        {
            CoreModels.SpatialRelationship.Intersects => StorageSpatialRelationship.Intersects,
            CoreModels.SpatialRelationship.Contains => StorageSpatialRelationship.Contains,
            CoreModels.SpatialRelationship.Within => StorageSpatialRelationship.Within,
            CoreModels.SpatialRelationship.Overlaps => StorageSpatialRelationship.Overlaps,
            CoreModels.SpatialRelationship.Touches => StorageSpatialRelationship.Touches,
            CoreModels.SpatialRelationship.Crosses => StorageSpatialRelationship.Crosses,
            _ => StorageSpatialRelationship.Intersects
        };
    }

    private static List<Feature> ApplyWhereClause(List<Feature> features, string whereClause)
    {
        var predicates = ParseWhereClause(whereClause);
        if (predicates.Count == 0)
        {
            return features;
        }

        return features
            .Where(feature => predicates.All(predicate => MatchesWherePredicate(feature, predicate)))
            .ToList();
    }

    private static List<Feature> ApplyOrderBy(List<Feature> features, List<OrderByClause> orderBy)
    {
        // Simple implementation - order by first field
        if (orderBy.FirstOrDefault() is var firstOrder && firstOrder != null)
        {
            if (firstOrder.Ascending)
            {
                return features.OrderBy(f => GetFieldValue(f, firstOrder.FieldName)).ToList();
            }
            else
            {
                return features.OrderByDescending(f => GetFieldValue(f, firstOrder.FieldName)).ToList();
            }
        }

        return features;
    }

    private static object? GetFieldValue(Feature feature, string fieldName)
    {
        return feature.Attributes.TryGetValue(fieldName, out var value) ? value : null;
    }

    private static IReadOnlyList<WherePredicate> ParseWhereClause(string whereClause)
    {
        var trimmed = whereClause.Trim();
        if (trimmed.Length == 0 ||
            string.Equals(trimmed, "1=1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var parts = SplitWhereClause(trimmed);
        var predicates = new List<WherePredicate>(parts.Count);

        foreach (var part in parts)
        {
            var match = WherePredicateRegex.Match(part);
            if (!match.Success)
            {
                throw new NotSupportedException($"Unsupported where clause predicate: '{part}'.");
            }

            predicates.Add(new WherePredicate(
                match.Groups["field"].Value,
                match.Groups["op"].Value,
                ParseLiteral(match.Groups["value"].Value)));
        }

        return predicates;
    }

    private static IReadOnlyList<string> SplitWhereClause(string whereClause)
    {
        var parts = new List<string>();
        var startIndex = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0; index < whereClause.Length; index++)
        {
            var character = whereClause[index];

            if (character == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && index + 1 < whereClause.Length && whereClause[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (character == '"' && !inSingleQuote)
            {
                if (inDoubleQuote && index + 1 < whereClause.Length && whereClause[index + 1] == '"')
                {
                    index++;
                    continue;
                }

                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && IsAndSeparator(whereClause, index))
            {
                parts.Add(whereClause[startIndex..index].Trim());
                index += 2;
                startIndex = index + 1;
            }
        }

        parts.Add(whereClause[startIndex..].Trim());
        return parts;
    }

    private static bool IsAndSeparator(string value, int index)
    {
        return index > 0 &&
            index + 3 < value.Length &&
            char.IsWhiteSpace(value[index - 1]) &&
            char.IsWhiteSpace(value[index + 3]) &&
            string.Compare(value, index, "AND", 0, 3, ignoreCase: true, CultureInfo.InvariantCulture) == 0;
    }

    private static object? ParseLiteral(string literal)
    {
        var trimmed = literal.Trim();

        if (string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericValue))
        {
            return numericValue;
        }

        return trimmed;
    }

    private static bool MatchesWherePredicate(Feature feature, WherePredicate predicate)
    {
        if (!feature.Attributes.TryGetValue(predicate.FieldName, out var rawValue))
        {
            return predicate.Value is null && (predicate.Operator is "=" or "==");
        }

        var fieldValue = NormalizeAttributeValue(rawValue);

        return predicate.Operator switch
        {
            "=" or "==" => ValuesEqual(fieldValue, predicate.Value),
            "!=" or "<>" => !ValuesEqual(fieldValue, predicate.Value),
            ">" => CompareValues(fieldValue, predicate.Value) > 0,
            ">=" => CompareValues(fieldValue, predicate.Value) >= 0,
            "<" => CompareValues(fieldValue, predicate.Value) < 0,
            "<=" => CompareValues(fieldValue, predicate.Value) <= 0,
            _ => false
        };
    }

    private static object? NormalizeAttributeValue(object? value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetDouble(out var number) => number,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => element.ToString()
            };
        }

        return value;
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (TryGetDouble(left, out var leftNumber) && TryGetDouble(right, out var rightNumber))
        {
            return Math.Abs(leftNumber - rightNumber) < double.Epsilon;
        }

        if (TryGetBoolean(left, out var leftBoolean) && TryGetBoolean(right, out var rightBoolean))
        {
            return leftBoolean == rightBoolean;
        }

        if (TryGetDateTimeOffset(left, out var leftDate) && TryGetDateTimeOffset(right, out var rightDate))
        {
            return leftDate == rightDate;
        }

        return string.Equals(
            Convert.ToString(left, CultureInfo.InvariantCulture),
            Convert.ToString(right, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private static int CompareValues(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null ? 0 : left is null ? -1 : 1;
        }

        if (TryGetDouble(left, out var leftNumber) && TryGetDouble(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (TryGetDateTimeOffset(left, out var leftDate) && TryGetDateTimeOffset(right, out var rightDate))
        {
            return leftDate.CompareTo(rightDate);
        }

        return string.Compare(
            Convert.ToString(left, CultureInfo.InvariantCulture),
            Convert.ToString(right, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private static bool TryGetDouble(object? value, out double number)
    {
        switch (value)
        {
            case double doubleValue:
                number = doubleValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case string stringValue:
                return double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            default:
                number = 0;
                return false;
        }
    }

    private static bool TryGetBoolean(object? value, out bool result)
    {
        if (value is bool booleanValue)
        {
            result = booleanValue;
            return true;
        }

        if (value is string stringValue && bool.TryParse(stringValue, out result))
        {
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryGetDateTimeOffset(object? value, out DateTimeOffset result)
    {
        switch (value)
        {
            case DateTime dateTimeValue:
                result = dateTimeValue;
                return true;
            case DateTimeOffset dateTimeOffsetValue:
                result = dateTimeOffsetValue;
                return true;
            case string stringValue:
                return DateTimeOffset.TryParse(
                    stringValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out result);
            default:
                result = default;
                return false;
        }
    }

    private static double EstimateLayerSize(List<Feature> features)
    {
        // Rough estimate: assume 1KB per feature on average
        return features.Count * 1.0 / 1024.0; // MB
    }

    #endregion

    private sealed record WherePredicate(string FieldName, string Operator, object? Value);
}

/// <summary>
/// Statistics about features in a layer
/// </summary>
public class FeatureStatistics
{
    public int LayerId { get; set; }
    public int TotalFeatures { get; set; }
    public int PendingSync { get; set; }
    public DateTime LastModified { get; set; }
    public double SizeEstimateMB { get; set; }
}

/// <summary>
/// Result of batch operations
/// </summary>
public class BatchResult
{
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> SuccessfulIds { get; set; } = new();
    public List<BatchError> FailedItems { get; set; } = new();
    public bool HasErrors => ErrorCount > 0;
    public int TotalCount => SuccessCount + ErrorCount;
}

/// <summary>
/// Error information for failed batch items
/// </summary>
public class BatchError
{
    public string Id { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
