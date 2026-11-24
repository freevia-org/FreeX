using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelTextNumberParserPercentLocaleTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("bg-BG")]
    [InlineData("de-DE")]
    public void RepeatedPercent_StrictParserValueAndArithmeticRejectText(string cultureName)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Percent text");
        foreach (var text in new[] { "50%%", "50%%%" })
        {
            ExcelTextNumberParser.TryParse(text, out var number).Should().BeFalse();
            number.Should().Be(0, "a failed parse must not expose a partially parsed number");

            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));
            foreach (var formula in new[] { $"=VALUE(\"{text}\")", "=VALUE(A1)", "=A1+0" })
                evaluator.Evaluate(formula, sheet).Should().Be(ErrorValue.Value,
                    $"{formula} uses strict text coercion under {cultureName}");
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("bg-BG")]
    [InlineData("de-DE")]
    public void SinglePercent_StrictParserValueAndArithmeticStillAcceptText(string cultureName)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        ExcelTextNumberParser.TryParse("50%", out var number).Should().BeTrue();
        number.Should().Be(0.5);

        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Percent text");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("50%"));
        foreach (var formula in new[] { "=VALUE(\"50%\")", "=VALUE(A1)", "=A1+0" })
            evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(0.5),
                $"one percent sign is valid strict text coercion under {cultureName}");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("bg-BG")]
    [InlineData("de-DE")]
    public void NumberValue_KeepsLenientRepeatedPercentScaling(string cultureName)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Percent text");
        foreach (var (text, expected) in new[] { ("50%", 0.5), ("50%%", 0.005), ("50%%%", 0.00005) })
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));
            foreach (var formula in new[] { $"=NUMBERVALUE(\"{text}\")", "=NUMBERVALUE(A1)" })
                evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected),
                    $"NUMBERVALUE intentionally scales every percent sign under {cultureName}");
        }
    }
}
