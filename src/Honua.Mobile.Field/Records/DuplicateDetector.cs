namespace Honua.Mobile.Field.Records;

public sealed class DuplicateDetector
{
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

public sealed class DuplicateDetectionOptions
{
    public double MaxDistanceMeters { get; init; } = 15;

    public IReadOnlyList<string> MatchFieldIds { get; init; } = [];
}

public sealed record PotentialDuplicate(string RecordId, double DistanceMeters, IReadOnlyList<string> MatchedFieldIds);
