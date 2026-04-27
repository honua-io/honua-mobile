using System.Globalization;
using Honua.Mobile.Field.Records;

namespace Honua.Mobile.Field.Forms;

/// <summary>
/// Evaluates calculated field expressions (e.g., <c>concat</c>, <c>sum</c>) and
/// writes the results back into the <see cref="FieldRecord"/>.
/// </summary>
public sealed class CalculatedFieldEvaluator
{
    /// <summary>
    /// Evaluates all calculated fields in <paramref name="form"/> and writes results into <paramref name="record"/>.
    /// </summary>
    /// <param name="form">The form definition containing fields with <see cref="FormField.CalculatedExpression"/> values.</param>
    /// <param name="record">The field record whose values are used as inputs and updated with results.</param>
    /// <remarks>
    /// Supported functions: <c>concat(...)</c> for string concatenation and <c>sum(...)</c> for numeric addition.
    /// Field references use <c>$fieldId</c> syntax; string literals use single quotes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="form"/> or <paramref name="record"/> is <see langword="null"/>.</exception>
    public void ApplyCalculatedFields(FormDefinition form, FieldRecord record)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(record);

        foreach (var field in form.Sections.SelectMany(section => section.Fields))
        {
            if (string.IsNullOrWhiteSpace(field.CalculatedExpression))
            {
                continue;
            }

            var value = EvaluateExpression(field.CalculatedExpression!, record);
            record.Values[field.FieldId] = value;
        }
    }

    private static object? EvaluateExpression(string expression, FieldRecord record)
    {
        var openParen = expression.IndexOf('(');
        var closeParen = expression.LastIndexOf(')');

        if (openParen <= 0 || closeParen <= openParen)
        {
            return expression;
        }

        var function = expression[..openParen].Trim().ToLowerInvariant();
        var args = expression[(openParen + 1)..closeParen]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(arg => ResolveArg(arg, record))
            .ToArray();

        return function switch
        {
            "concat" => string.Concat(args.Select(arg => arg?.ToString() ?? string.Empty)),
            "sum" => args.Sum(ParseDouble),
            _ => expression,
        };
    }

    private static object? ResolveArg(string arg, FieldRecord record)
    {
        if (arg.StartsWith('$'))
        {
            var key = arg[1..];
            return record.Values.TryGetValue(key, out var value) ? value : null;
        }

        if (arg.Length >= 2 && arg[0] == '\'' && arg[^1] == '\'')
        {
            return arg[1..^1];
        }

        return arg;
    }

    private static double ParseDouble(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        return double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
