using System.Globalization;
using System.Text.RegularExpressions;
using Honua.Mobile.Field.Records;

namespace Honua.Mobile.Field.Forms;

public sealed class FormValidator
{
    public FormValidationResult Validate(FormDefinition form, FieldRecord record)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(record);

        var errors = new List<FormValidationError>();

        foreach (var field in form.Sections.SelectMany(section => section.Fields))
        {
            var isVisible = IsVisible(field, record);
            record.Values.TryGetValue(field.FieldId, out var rawValue);

            if (field.Required && isVisible && IsMissing(rawValue))
            {
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} is required."));
                continue;
            }

            if (!isVisible || IsMissing(rawValue))
            {
                continue;
            }

            if (rawValue is null)
            {
                continue;
            }

            ValidateType(errors, field, rawValue);
            ValidateRules(errors, field, rawValue);
        }

        return new FormValidationResult(errors);
    }

    private static bool IsVisible(FormField field, FieldRecord record)
    {
        if (field.VisibilityRule is null)
        {
            return true;
        }

        if (!record.Values.TryGetValue(field.VisibilityRule.DependsOnFieldId, out var actual))
        {
            return false;
        }

        var expected = field.VisibilityRule.MatchValue;

        return field.VisibilityRule.Operator switch
        {
            ComparisonOperator.Equals => AreEqual(actual, expected),
            ComparisonOperator.NotEquals => !AreEqual(actual, expected),
            ComparisonOperator.GreaterThan => TryAsDouble(actual, out var a) && TryAsDouble(expected, out var b) && a > b,
            ComparisonOperator.LessThan => TryAsDouble(actual, out var c) && TryAsDouble(expected, out var d) && c < d,
            ComparisonOperator.Contains => actual?.ToString()?.Contains(expected.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true,
            _ => true,
        };
    }

    private static void ValidateType(ICollection<FormValidationError> errors, FormField field, object rawValue)
    {
        switch (field.Type)
        {
            case FormFieldType.Numeric when !TryAsDouble(rawValue, out _):
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} must be numeric."));
                break;
            case FormFieldType.YesNo when rawValue is not bool:
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} must be true/false."));
                break;
            case FormFieldType.SingleChoice when field.Choices.Count > 0 && !field.Choices.Contains(rawValue.ToString() ?? string.Empty):
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} must match an allowed choice."));
                break;
            case FormFieldType.MultipleChoice when rawValue is not IEnumerable<string>:
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} must provide a list of choices."));
                break;
            case FormFieldType.Hyperlink when !Uri.TryCreate(rawValue.ToString(), UriKind.Absolute, out _):
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} must be a valid URL."));
                break;
            case FormFieldType.Date when !DateOnly.TryParse(rawValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} must be a date."));
                break;
            case FormFieldType.Time when !TimeOnly.TryParse(rawValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} must be a time."));
                break;
        }
    }

    private static void ValidateRules(ICollection<FormValidationError> errors, FormField field, object rawValue)
    {
        if (!string.IsNullOrWhiteSpace(field.Validation.RegexPattern) &&
            !Regex.IsMatch(rawValue.ToString() ?? string.Empty, field.Validation.RegexPattern))
        {
            errors.Add(new FormValidationError(field.FieldId, $"{field.Label} does not match the required format."));
        }

        if (TryAsDouble(rawValue, out var numericValue))
        {
            if (field.Validation.MinNumericValue is { } min && numericValue < min)
            {
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} must be >= {min}."));
            }

            if (field.Validation.MaxNumericValue is { } max && numericValue > max)
            {
                errors.Add(new FormValidationError(field.FieldId, $"{field.Label} must be <= {max}."));
            }
        }

        if (field.Validation.MinMediaCount is { } minMedia &&
            rawValue is IEnumerable<MediaAttachment> media &&
            media.Count() < minMedia)
        {
            errors.Add(new FormValidationError(field.FieldId, $"{field.Label} requires at least {minMedia} media item(s)."));
        }
    }

    private static bool IsMissing(object? value)
    {
        return value switch
        {
            null => true,
            string text => string.IsNullOrWhiteSpace(text),
            _ => false,
        };
    }

    private static bool TryAsDouble(object? value, out double parsed)
    {
        switch (value)
        {
            case null:
                parsed = 0;
                return true;
            case double d:
                parsed = d;
                return true;
            case float f:
                parsed = f;
                return true;
            case decimal m:
                parsed = (double)m;
                return true;
            case int i:
                parsed = i;
                return true;
            case long l:
                parsed = l;
                return true;
            default:
                return double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed);
        }
    }

    private static bool AreEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (TryAsDouble(left, out var l) && TryAsDouble(right, out var r))
        {
            return Math.Abs(l - r) < 0.000001;
        }

        return string.Equals(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class FormValidationResult
{
    public FormValidationResult(IReadOnlyList<FormValidationError> errors)
    {
        Errors = errors;
    }

    public IReadOnlyList<FormValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;
}

public sealed record FormValidationError(string FieldId, string Message);
