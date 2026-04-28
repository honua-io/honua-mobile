namespace Honua.Mobile.Maui.Annotations;

/// <summary>
/// Programmatic map annotation that can be rendered by platform-specific map adapters.
/// </summary>
public sealed record HonuaAnnotation
{
    public required string Id { get; init; }

    public required HonuaAnnotationType Type { get; init; }

    public required IReadOnlyList<HonuaMapCoordinate> Coordinates { get; init; }

    public string? Text { get; init; }

    public HonuaAnnotationStyle Style { get; init; } = HonuaAnnotationStyle.Default;

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public HonuaAnnotationBounds Bounds => HonuaAnnotationBounds.FromCoordinates(Coordinates);

    public HonuaAnnotation WithStyle(HonuaAnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        style.Validate();

        return this with
        {
            Style = style,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
}
