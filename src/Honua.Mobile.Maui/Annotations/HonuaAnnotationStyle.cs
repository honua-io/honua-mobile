namespace Honua.Mobile.Maui.Annotations;

/// <summary>
/// Visual styling shared by annotation primitives.
/// </summary>
public sealed record HonuaAnnotationStyle
{
    public string? FillColor { get; init; }

    public string StrokeColor { get; init; } = "#2A7FDB";

    public double StrokeWidth { get; init; } = 2;

    public double Opacity { get; init; } = 1;

    public string TextColor { get; init; } = "#1A1A1A";

    public double TextSize { get; init; } = 14;

    public static HonuaAnnotationStyle Default { get; } = new();

    public HonuaAnnotationStyle SetFillColor(string fillColor)
    {
        ValidateColor(fillColor, nameof(fillColor));
        return this with { FillColor = fillColor };
    }

    public HonuaAnnotationStyle SetStrokeColor(string strokeColor)
    {
        ValidateColor(strokeColor, nameof(strokeColor));
        return this with { StrokeColor = strokeColor };
    }

    public HonuaAnnotationStyle SetStrokeWidth(double strokeWidth)
    {
        if (strokeWidth <= 0 || double.IsNaN(strokeWidth) || double.IsInfinity(strokeWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(strokeWidth), "Stroke width must be greater than zero.");
        }

        return this with { StrokeWidth = strokeWidth };
    }

    public HonuaAnnotationStyle SetOpacity(double opacity)
    {
        if (opacity is < 0 or > 1 || double.IsNaN(opacity) || double.IsInfinity(opacity))
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be between 0 and 1.");
        }

        return this with { Opacity = opacity };
    }

    public void Validate()
    {
        ValidateColor(StrokeColor, nameof(StrokeColor));
        ValidateColor(TextColor, nameof(TextColor));

        if (!string.IsNullOrWhiteSpace(FillColor))
        {
            ValidateColor(FillColor, nameof(FillColor));
        }

        if (StrokeWidth <= 0 || double.IsNaN(StrokeWidth) || double.IsInfinity(StrokeWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(StrokeWidth), "Stroke width must be greater than zero.");
        }

        if (Opacity is < 0 or > 1 || double.IsNaN(Opacity) || double.IsInfinity(Opacity))
        {
            throw new ArgumentOutOfRangeException(nameof(Opacity), "Opacity must be between 0 and 1.");
        }

        if (TextSize <= 0 || double.IsNaN(TextSize) || double.IsInfinity(TextSize))
        {
            throw new ArgumentOutOfRangeException(nameof(TextSize), "Text size must be greater than zero.");
        }
    }

    private static void ValidateColor(string color, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            throw new ArgumentException("Color values cannot be empty.", parameterName);
        }
    }
}
