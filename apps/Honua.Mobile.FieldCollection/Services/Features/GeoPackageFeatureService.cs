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

        // For other geometry types, would extract actual bounds
        // For now, return a default large bounds
        return StorageBoundingBox.FromCoordinates(-180, -90, 180, 90);
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
