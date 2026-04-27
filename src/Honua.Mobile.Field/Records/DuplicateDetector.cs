namespace Honua.Mobile.Field.Records;

/// <summary>
/// Detects potential duplicate records by comparing geographic proximity and field value matches.
/// </summary>
public sealed class DuplicateDetector
{
    /// <summary>
    /// Finds existing records that may be duplicates of <paramref name="candidate"/> based on
    /// distance (Haversine formula) and optional field value matches.
    /// </summary>
    /// <param name="existing">The set of previously collected records to compare against.</param>
    /// <param name="candidate">The new record to check for duplicates.</param>
    /// <param name="options">Detection thresholds; defaults to 15-meter radius with no field matching.</param>
    /// <returns>A list of potential duplicates with distance and matched field information.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="existing"/> or <paramref name="candidate"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<PotentialDuplicate> FindPotentialDuplicates(
        IEnumerable<FieldRecord> existing,
        FieldRecord candidate,
        DuplicateDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        var resolvedOptions = options ?? new DuplicateDetectionOptions();
        var matches = new List<PotentialDuplicate>();

        foreach (var record in existing)
        {
            if (record.RecordId == candidate.RecordId)
            {
                continue;
            }

            var distance = CalculateDistanceMeters(record.Location, candidate.Location);
            if (distance is null || distance.Value > resolvedOptions.MaxDistanceMeters)
            {
                continue;
            }

            var matchedFields = resolvedOptions.MatchFieldIds
                .Where(fieldId =>
                    record.Values.TryGetValue(fieldId, out var existingValue) &&
                    candidate.Values.TryGetValue(fieldId, out var candidateValue) &&
                    string.Equals(existingValue?.ToString(), candidateValue?.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (resolvedOptions.MatchFieldIds.Count > 0 && matchedFields.Length == 0)
            {
                continue;
            }

            matches.Add(new PotentialDuplicate(record.RecordId, distance.Value, matchedFields));
        }

        return matches;
    }

    private static double? CalculateDistanceMeters(GeoPoint? left, GeoPoint? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        var radius = 6_371_000d;
        var dLat = DegreesToRadians(right.Latitude - left.Latitude);
        var dLon = DegreesToRadians(right.Longitude - left.Longitude);
        var lat1 = DegreesToRadians(left.Latitude);
        var lat2 = DegreesToRadians(right.Latitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return radius * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}

/// <summary>
/// Options controlling duplicate detection thresholds and field matching.
/// </summary>
public sealed class DuplicateDetectionOptions
{
    /// <summary>
    /// Maximum distance in meters between two records to consider them potential duplicates. Defaults to 15.
    /// </summary>
    public double MaxDistanceMeters { get; init; } = 15;

    /// <summary>
    /// Field IDs whose values must match (case-insensitive) for a duplicate to be reported.
    /// When empty, only distance is considered.
    /// </summary>
    public IReadOnlyList<string> MatchFieldIds { get; init; } = [];
}

/// <summary>
/// Represents a record identified as a potential duplicate.
/// </summary>
/// <param name="RecordId">The ID of the existing record flagged as a potential duplicate.</param>
/// <param name="DistanceMeters">Distance in meters between the candidate and the existing record.</param>
/// <param name="MatchedFieldIds">Field IDs whose values matched between the two records.</param>
public sealed record PotentialDuplicate(string RecordId, double DistanceMeters, IReadOnlyList<string> MatchedFieldIds);
