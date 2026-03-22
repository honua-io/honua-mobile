namespace Honua.Mobile.Field.Forms;

/// <summary>
/// Describes a mobile data-collection form consisting of sections and fields.
/// </summary>
public sealed class FormDefinition
{
    /// <summary>
    /// Unique identifier for this form definition.
    /// </summary>
    public required string FormId { get; init; }

    /// <summary>
    /// Human-readable name of the form.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ordered list of sections that compose the form.
    /// </summary>
    public IReadOnlyList<FormSection> Sections { get; init; } = [];
}

/// <summary>
/// A logical grouping of fields within a <see cref="FormDefinition"/>.
/// </summary>
public sealed class FormSection
{
    /// <summary>
    /// Unique identifier for this section.
    /// </summary>
    public required string SectionId { get; init; }

    /// <summary>
    /// Display label shown to the field worker.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the section can be duplicated to capture multiple entries.
    /// </summary>
    public bool Repeatable { get; init; }

    /// <summary>
    /// Ordered list of fields in this section.
    /// </summary>
    public IReadOnlyList<FormField> Fields { get; init; } = [];
}

/// <summary>
/// A single input field within a <see cref="FormSection"/>.
/// </summary>
public sealed class FormField
{
    /// <summary>
    /// Unique identifier for this field, used as the key in <see cref="Records.FieldRecord.Values"/>.
    /// </summary>
    public required string FieldId { get; init; }

    /// <summary>
    /// Display label shown to the field worker.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Data type of this field (text, numeric, photo, etc.).
    /// </summary>
    public FormFieldType Type { get; init; }

    /// <summary>
    /// When <see langword="true"/>, a value must be provided before the record can be submitted.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Allowed values for <see cref="FormFieldType.SingleChoice"/> or <see cref="FormFieldType.MultipleChoice"/> fields.
    /// </summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>
    /// Additional validation constraints such as regex patterns or numeric ranges.
    /// </summary>
    public FieldValidationRule Validation { get; init; } = new();

    /// <summary>
    /// Optional rule that controls whether this field is visible based on another field's value.
    /// </summary>
    public FieldVisibilityRule? VisibilityRule { get; init; }

    /// <summary>
    /// Expression evaluated at runtime to auto-populate this field (e.g., <c>concat($first, ' ', $last)</c>).
    /// </summary>
    public string? CalculatedExpression { get; init; }
}

/// <summary>
/// Validation constraints applied to a <see cref="FormField"/>.
/// </summary>
public sealed class FieldValidationRule
{
    /// <summary>
    /// Regular expression pattern the field value must match.
    /// </summary>
    public string? RegexPattern { get; init; }

    /// <summary>
    /// Minimum allowed numeric value (inclusive).
    /// </summary>
    public double? MinNumericValue { get; init; }

    /// <summary>
    /// Maximum allowed numeric value (inclusive).
    /// </summary>
    public double? MaxNumericValue { get; init; }

    /// <summary>
    /// Minimum number of media attachments required for this field.
    /// </summary>
    public int? MinMediaCount { get; init; }
}

/// <summary>
/// Rule that conditionally shows or hides a <see cref="FormField"/> based on another field's value.
/// </summary>
public sealed class FieldVisibilityRule
{
    /// <summary>
    /// The <see cref="FormField.FieldId"/> whose value determines visibility.
    /// </summary>
    public required string DependsOnFieldId { get; init; }

    /// <summary>
    /// Comparison operator applied between the dependent field's value and <see cref="MatchValue"/>.
    /// </summary>
    public ComparisonOperator Operator { get; init; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    public required object MatchValue { get; init; }
}

/// <summary>
/// Comparison operators used in <see cref="FieldVisibilityRule"/>.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>Values must be equal.</summary>
    Equals,
    /// <summary>Values must not be equal.</summary>
    NotEquals,
    /// <summary>Actual value must be greater than match value.</summary>
    GreaterThan,
    /// <summary>Actual value must be less than match value.</summary>
    LessThan,
    /// <summary>Actual value must contain the match value as a substring.</summary>
    Contains,
}
