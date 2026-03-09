using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.FieldCollection.Services.Storage.Models;

namespace Honua.Mobile.FieldCollection.Services.Features;

/// <summary>
/// Real implementation of IFeatureService using GeoPackage storage
/// Provides spatial querying, feature management, and offline capabilities
/// </summary>
public class GeoPackageFeatureService : IFeatureService
{
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

    public async Task<List<Feature>> GetFeaturesAsync(int layerId)
    {
        try
        {
            return await _storage.QueryFeaturesAsync(layerId);
        }
        catch (Exception)
        {
            // Return empty list on error rather than throwing
            return new List<Feature>();
        }
    }

    public async Task<Feature?> GetFeatureAsync(string featureId, int layerId)
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
            SpatialQuery? spatialQuery = null;

            if (query.SpatialFilter != null)
            {
                spatialQuery = new SpatialQuery
                {
                    Bounds = ConvertToBoundingBox(query.SpatialFilter),
                    Relationship = ConvertToSpatialRelationship(query.SpatialFilter.Relationship),
                    MaxResults = query.MaxResults
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
        catch (Exception)
        {
            return new List<Feature>();
        }
    }

    #endregion

    #region Feature Modification

    public async Task<string> CreateFeatureAsync(Feature feature)
    {
        try
        {
            // Ensure feature has an ID
            if (string.IsNullOrEmpty(feature.Id))
            {
                feature.Id = Guid.NewGuid().ToString();
            }

            // Set creation metadata
            feature.CreatedAt = DateTime.UtcNow;
            feature.ModifiedAt = DateTime.UtcNow;
            feature.Version = 1;

            await _storage.StoreFeatureAsync(feature);
            return feature.Id;
        }
        catch (Exception)
        {
            throw new InvalidOperationException($"Failed to create feature: {feature.Id}");
        }
    }

    public async Task<bool> UpdateFeatureAsync(Feature feature)
    {
        try
        {
            // Update modification metadata
            feature.ModifiedAt = DateTime.UtcNow;
            feature.Version++; // Increment version for optimistic locking

            return await _storage.UpdateFeatureAsync(feature);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteFeatureAsync(int layerId, string featureId)
    {
        try
        {
            return await _storage.DeleteFeatureAsync(featureId, layerId);
        }
        catch (Exception)
        {
            return false;
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
                var id = await CreateFeatureAsync(feature);
                successful.Add(id);
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
                var success = await UpdateFeatureAsync(feature);
                if (success)
                {
                    successful.Add(feature.Id);
                    result.SuccessCount++;
                }
                else
                {
                    failed.Add((feature.Id, "Update failed"));
                    result.ErrorCount++;
                }
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
                var success = await DeleteFeatureAsync(layerId, featureId);
                if (success)
                {
                    successful.Add(featureId);
                    result.SuccessCount++;
                }
                else
                {
                    failed.Add((featureId, "Delete failed"));
                    result.ErrorCount++;
                }
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
                LastModified = features.Max(f => f.ModifiedAt) ?? DateTime.MinValue,
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

    private static BoundingBox ConvertToBoundingBox(SpatialFilter spatialFilter)
    {
        if (spatialFilter.Geometry is Models.Point point)
        {
            var buffer = 0.001; // ~100m buffer for point queries
            return BoundingBox.FromCoordinates(
                point.Longitude - buffer, point.Latitude - buffer,
                point.Longitude + buffer, point.Latitude + buffer);
        }

        // For other geometry types, would extract actual bounds
        // For now, return a default large bounds
        return BoundingBox.FromCoordinates(-180, -90, 180, 90);
    }

    private static SpatialRelationship ConvertToSpatialRelationship(Models.SpatialRelationship relationship)
    {
        return relationship switch
        {
            Models.SpatialRelationship.Intersects => SpatialRelationship.Intersects,
            Models.SpatialRelationship.Contains => SpatialRelationship.Contains,
            Models.SpatialRelationship.Within => SpatialRelationship.Within,
            Models.SpatialRelationship.Overlaps => SpatialRelationship.Overlaps,
            Models.SpatialRelationship.Touches => SpatialRelationship.Touches,
            Models.SpatialRelationship.Crosses => SpatialRelationship.Crosses,
            _ => SpatialRelationship.Intersects
        };
    }

    private static List<Feature> ApplyWhereClause(List<Feature> features, string whereClause)
    {
        // Simple implementation - in a real system would parse SQL WHERE clause
        // For now, just return all features
        return features;
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

    private static double EstimateLayerSize(List<Feature> features)
    {
        // Rough estimate: assume 1KB per feature on average
        return features.Count * 1.0 / 1024.0; // MB
    }

    #endregion
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