namespace Honua.Mobile.Field.Forms;

public sealed class FormDefinition
{
    public required string FormId { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<FormSection> Sections { get; init; } = [];
}

public sealed class FormSection
{
    public required string SectionId { get; init; }

    public required string Label { get; init; }

    public bool Repeatable { get; init; }

    public IReadOnlyList<FormField> Fields { get; init; } = [];
}

public sealed class FormField
{
    public required string FieldId { get; init; }

    public required string Label { get; init; }

    public FormFieldType Type { get; init; }

    public bool Required { get; init; }

    public IReadOnlyList<string> Choices { get; init; } = [];

    public FieldValidationRule Validation { get; init; } = new();

    public FieldVisibilityRule? VisibilityRule { get; init; }

    public string? CalculatedExpression { get; init; }
}

public sealed class FieldValidationRule
{
    public string? RegexPattern { get; init; }

    public double? MinNumericValue { get; init; }

    public double? MaxNumericValue { get; init; }

    public int? MinMediaCount { get; init; }
}

public sealed class FieldVisibilityRule
{
    public required string DependsOnFieldId { get; init; }

    public ComparisonOperator Operator { get; init; }

    public required object MatchValue { get; init; }
}

public enum ComparisonOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    Contains,
}
