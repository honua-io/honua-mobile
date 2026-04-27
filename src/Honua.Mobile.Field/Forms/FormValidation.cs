using System.Globalization;
using System.Collections;
using System.Text.RegularExpressions;
using Honua.Mobile.Field.Records;

namespace Honua.Mobile.Field.Forms;

/// <summary>
/// Validates a <see cref="FieldRecord"/> against its <see cref="FormDefinition"/>,
/// checking required fields, type constraints, regex patterns, and numeric ranges.
/// </summary>
public sealed class FormValidator
{
    private static readonly TimeSpan RegexEvaluationTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Validates every visible field in <paramref name="form"/> against the values in <paramref name="record"/>.
    /// </summary>
    /// <param name="form">The form definition describing expected fields and validation rules.</param>
    /// <param name="record">The field record containing user-entered values.</param>
    /// <returns>A <see cref="FormValidationResult"/> containing any validation errors found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="form"/> or <paramref name="record"/> is <see langword="null"/>.</exception>
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
            case FormFieldType.SingleChoice when field.Choices.Count > 0 && !field.Choices.Contains(rawValue.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase):
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
            !IsRegexMatch(rawValue.ToString() ?? string.Empty, field.Validation.RegexPattern!, out var regexErrorMessage))
        {
            errors.Add(new FormValidationError(field.FieldId, regexErrorMessage ?? $"{field.Label} does not match the required format."));
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
            IEnumerable collection => !collection.Cast<object?>().Any(),
            _ => false,
        };
    }

    private static bool TryAsDouble(object? value, out double parsed)
    {
        switch (value)
        {
            case null:
                parsed = default;
                return false;
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

    private static bool IsRegexMatch(string input, string pattern, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.None, RegexEvaluationTimeout);
        }
        catch (ArgumentException)
        {
            errorMessage = "Validation pattern is invalid.";
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            errorMessage = "Validation pattern timed out.";
            return false;
        }
    }
}

/// <summary>
/// Result of validating a <see cref="FieldRecord"/> against a <see cref="FormDefinition"/>.
/// </summary>
public sealed class FormValidationResult
{
    /// <summary>
    /// Initializes a new <see cref="FormValidationResult"/> with the given validation errors.
    /// </summary>
    /// <param name="errors">The list of validation errors. An empty list indicates a valid record.</param>
    public FormValidationResult(IReadOnlyList<FormValidationError> errors)
    {
        Errors = errors;
    }

    /// <summary>
    /// Validation errors found during validation. Empty when the record is valid.
    /// </summary>
    public IReadOnlyList<FormValidationError> Errors { get; }

    /// <summary>
    /// <see langword="true"/> when no validation errors were found.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// A single validation error identifying the field and describing the problem.
/// </summary>
/// <param name="FieldId">The <see cref="FormField.FieldId"/> that failed validation.</param>
/// <param name="Message">A human-readable description of the validation failure.</param>
public sealed record FormValidationError(string FieldId, string Message);
