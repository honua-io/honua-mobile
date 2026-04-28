namespace Honua.Mobile.Maui.Annotations;

/// <summary>
/// In-memory annotation layer that exposes drawing, styling, management, and spatial query APIs.
/// Platform-specific renderers can bind to the layer's annotation collection.
/// </summary>
public sealed class HonuaAnnotationLayer
{
    private readonly Dictionary<string, HonuaAnnotation> _annotations = new(StringComparer.Ordinal);
    private HonuaAnnotationStyle _defaultStyle = HonuaAnnotationStyle.Default;

    public HonuaAnnotationStyle DefaultStyle => _defaultStyle;

    public IReadOnlyList<HonuaAnnotation> Annotations => _annotations.Values.ToArray();

    public HonuaAnnotationLayer SetFillColor(string fillColor)
    {
        _defaultStyle = _defaultStyle.SetFillColor(fillColor);
        return this;
    }

    public HonuaAnnotationLayer SetStrokeColor(string strokeColor)
    {
        _defaultStyle = _defaultStyle.SetStrokeColor(strokeColor);
        return this;
    }

    public HonuaAnnotationLayer SetStrokeWidth(double strokeWidth)
    {
        _defaultStyle = _defaultStyle.SetStrokeWidth(strokeWidth);
        return this;
    }

    public HonuaAnnotationLayer SetOpacity(double opacity)
    {
        _defaultStyle = _defaultStyle.SetOpacity(opacity);
        return this;
    }

    public HonuaAnnotation DrawPoint(
        HonuaMapCoordinate coordinate,
        string? id = null,
        HonuaAnnotationStyle? style = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        return AddAnnotation(CreateAnnotation(
            HonuaAnnotationType.Point,
            [coordinate],
            id,
            text: null,
            style,
            metadata));
    }

    public HonuaAnnotation DrawPolyline(
        IEnumerable<HonuaMapCoordinate> coordinates,
        string? id = null,
        HonuaAnnotationStyle? style = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        return AddAnnotation(CreateAnnotation(
            HonuaAnnotationType.Polyline,
            MaterializeCoordinates(coordinates),
            id,
            text: null,
            style,
            metadata));
    }

    public HonuaAnnotation DrawPolygon(
        IEnumerable<HonuaMapCoordinate> coordinates,
        string? id = null,
        HonuaAnnotationStyle? style = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        return AddAnnotation(CreateAnnotation(
            HonuaAnnotationType.Polygon,
            MaterializeCoordinates(coordinates),
            id,
            text: null,
            style,
            metadata));
    }

    public HonuaAnnotation DrawText(
        HonuaMapCoordinate coordinate,
        string text,
        string? id = null,
        HonuaAnnotationStyle? style = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text annotations require non-empty text.", nameof(text));
        }

        return AddAnnotation(CreateAnnotation(
            HonuaAnnotationType.Text,
            [coordinate],
            id,
            text,
            style,
            metadata));
    }

    public HonuaAnnotation AddAnnotation(HonuaAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ValidateAnnotation(annotation);

        if (_annotations.ContainsKey(annotation.Id))
        {
            throw new InvalidOperationException($"Annotation '{annotation.Id}' already exists.");
        }

        var stored = SnapshotAnnotation(annotation);
        _annotations.Add(stored.Id, stored);
        return stored;
    }

    public HonuaAnnotation UpdateAnnotation(HonuaAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ValidateAnnotation(annotation);

        if (!_annotations.TryGetValue(annotation.Id, out var existing))
        {
            throw new KeyNotFoundException($"Annotation '{annotation.Id}' was not found.");
        }

        var updated = SnapshotAnnotation(annotation) with
        {
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        _annotations[annotation.Id] = updated;
        return updated;
    }

    public HonuaAnnotation UpdateAnnotation(string annotationId, Func<HonuaAnnotation, HonuaAnnotation> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationId);
        ArgumentNullException.ThrowIfNull(update);

        if (!_annotations.TryGetValue(annotationId, out var existing))
        {
            throw new KeyNotFoundException($"Annotation '{annotationId}' was not found.");
        }

        var updated = update(existing);
        if (!string.Equals(updated.Id, annotationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Annotation updates cannot change the annotation ID.");
        }

        return UpdateAnnotation(updated);
    }

    public bool RemoveAnnotation(string annotationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationId);
        return _annotations.Remove(annotationId);
    }

    public IReadOnlyList<HonuaAnnotation> GetAnnotationsInBounds(HonuaAnnotationBounds bounds)
    {
        return _annotations.Values
            .Where(annotation => annotation.Bounds.Intersects(bounds))
            .ToArray();
    }

    public IReadOnlyList<HonuaAnnotation> GetAnnotationsByType(HonuaAnnotationType type)
    {
        return _annotations.Values
            .Where(annotation => annotation.Type == type)
            .ToArray();
    }

    public void Clear() => _annotations.Clear();

    private HonuaAnnotation CreateAnnotation(
        HonuaAnnotationType type,
        IReadOnlyList<HonuaMapCoordinate> coordinates,
        string? id,
        string? text,
        HonuaAnnotationStyle? style,
        IReadOnlyDictionary<string, object?>? metadata)
    {
        var now = DateTimeOffset.UtcNow;
        return new HonuaAnnotation
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            Type = type,
            Coordinates = coordinates,
            Text = text,
            Style = style ?? _defaultStyle,
            Metadata = metadata ?? new Dictionary<string, object?>(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static IReadOnlyList<HonuaMapCoordinate> MaterializeCoordinates(IEnumerable<HonuaMapCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        return coordinates.ToArray();
    }

    private static HonuaAnnotation SnapshotAnnotation(HonuaAnnotation annotation)
    {
        return annotation with
        {
            Coordinates = annotation.Coordinates.ToArray(),
            Metadata = new Dictionary<string, object?>(annotation.Metadata),
        };
    }

    private static void ValidateAnnotation(HonuaAnnotation annotation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(annotation.Id);
        ArgumentNullException.ThrowIfNull(annotation.Coordinates);
        annotation.Style.Validate();

        switch (annotation.Type)
        {
            case HonuaAnnotationType.Point:
                RequireCoordinateCount(annotation, 1, exact: true);
                break;
            case HonuaAnnotationType.Text:
                RequireCoordinateCount(annotation, 1, exact: true);
                if (string.IsNullOrWhiteSpace(annotation.Text))
                {
                    throw new ArgumentException("Text annotations require non-empty text.", nameof(annotation));
                }
                break;
            case HonuaAnnotationType.Polyline:
                RequireCoordinateCount(annotation, 2, exact: false);
                break;
            case HonuaAnnotationType.Polygon:
                RequireCoordinateCount(annotation, 3, exact: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(annotation), annotation.Type, "Unsupported annotation type.");
        }
    }

    private static void RequireCoordinateCount(HonuaAnnotation annotation, int count, bool exact)
    {
        var actual = annotation.Coordinates.Count;
        if (exact && actual != count)
        {
            throw new ArgumentException($"Annotation type {annotation.Type} requires exactly {count} coordinate(s).", nameof(annotation));
        }

        if (!exact && actual < count)
        {
            throw new ArgumentException($"Annotation type {annotation.Type} requires at least {count} coordinate(s).", nameof(annotation));
        }
    }
}
