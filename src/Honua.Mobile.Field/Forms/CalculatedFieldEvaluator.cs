using System.Globalization;
using Honua.Mobile.Field.Records;

namespace Honua.Mobile.Field.Forms;

public sealed class CalculatedFieldEvaluator
{
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
