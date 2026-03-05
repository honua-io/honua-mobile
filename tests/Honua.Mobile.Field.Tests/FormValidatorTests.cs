using Honua.Mobile.Field.Forms;
using Honua.Mobile.Field.Records;

namespace Honua.Mobile.Field.Tests;

public sealed class FormValidatorTests
{
    [Fact]
    public void Validate_ReturnsErrors_WhenRequiredOrTypeRulesFail()
    {
        var form = new FormDefinition
        {
            FormId = "inspection",
            Name = "Inspection",
            Sections =
            [
                new FormSection
                {
                    SectionId = "main",
                    Label = "Main",
                    Fields =
                    [
                        new FormField { FieldId = "asset_id", Label = "Asset ID", Type = FormFieldType.Text, Required = true },
                        new FormField
                        {
                            FieldId = "score",
                            Label = "Score",
                            Type = FormFieldType.Numeric,
                            Validation = new FieldValidationRule { MinNumericValue = 1, MaxNumericValue = 5 },
                        },
                        new FormField
                        {
                            FieldId = "site_url",
                            Label = "Site URL",
                            Type = FormFieldType.Hyperlink,
                            Required = true,
                        },
                    ],
                },
            ],
        };

        var record = new FieldRecord
        {
            RecordId = "r1",
            FormId = "inspection",
            Values =
            {
                ["score"] = 7,
                ["site_url"] = "not-a-url",
            },
        };

        var validator = new FormValidator();
        var result = validator.Validate(form, record);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.FieldId == "asset_id");
        Assert.Contains(result.Errors, error => error.FieldId == "score");
        Assert.Contains(result.Errors, error => error.FieldId == "site_url");
    }

    [Fact]
    public void CalculatedFieldEvaluator_ComputesConcatAndSum()
    {
        var form = new FormDefinition
        {
            FormId = "survey",
            Name = "Survey",
            Sections =
            [
                new FormSection
                {
                    SectionId = "main",
                    Label = "Main",
                    Fields =
                    [
                        new FormField { FieldId = "first", Label = "First", Type = FormFieldType.Text },
                        new FormField { FieldId = "second", Label = "Second", Type = FormFieldType.Text },
                        new FormField { FieldId = "a", Label = "A", Type = FormFieldType.Numeric },
                        new FormField { FieldId = "b", Label = "B", Type = FormFieldType.Numeric },
                        new FormField { FieldId = "display", Label = "Display", Type = FormFieldType.Calculated, CalculatedExpression = "concat($first,'-', $second)" },
                        new FormField { FieldId = "total", Label = "Total", Type = FormFieldType.Calculated, CalculatedExpression = "sum($a,$b)" },
                    ],
                },
            ],
        };

        var record = new FieldRecord
        {
            RecordId = "r2",
            FormId = "survey",
            Values =
            {
                ["first"] = "alpha",
                ["second"] = "beta",
                ["a"] = 3,
                ["b"] = 5,
            },
        };

        var evaluator = new CalculatedFieldEvaluator();
        evaluator.ApplyCalculatedFields(form, record);

        Assert.Equal("alpha-beta", record.Values["display"]);
        Assert.Equal(8d, record.Values["total"]);
    }

    [Fact]
    public void Validate_RequiredMultipleChoiceRejectsEmptyCollection()
    {
        var form = new FormDefinition
        {
            FormId = "inspection",
            Name = "Inspection",
            Sections =
            [
                new FormSection
                {
                    SectionId = "main",
                    Label = "Main",
                    Fields =
                    [
                        new FormField
                        {
                            FieldId = "tags",
                            Label = "Tags",
                            Type = FormFieldType.MultipleChoice,
                            Required = true,
                        },
                    ],
                },
            ],
        };

        var record = new FieldRecord
        {
            RecordId = "r-empty-tags",
            FormId = "inspection",
            Values =
            {
                ["tags"] = Array.Empty<string>(),
            },
        };

        var validator = new FormValidator();
        var result = validator.Validate(form, record);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.FieldId == "tags");
    }
}
