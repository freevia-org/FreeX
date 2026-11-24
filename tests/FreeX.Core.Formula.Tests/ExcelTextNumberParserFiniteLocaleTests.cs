using System.Globalization;

using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelTextNumberParserFiniteLocaleTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("bg-BG")]
    [InlineData("de-DE")]
    public void NonFiniteText_IsRejectedWithSafeOutputInEveryLocale(string cultureName)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        var culture = CultureInfo.CurrentCulture;
        foreach (var text in NonFiniteTexts(culture))
        {
            ExcelTextNumberParser.TryParse(text, out var number).Should().BeFalse(
                $"'{text}' is not a finite spreadsheet number under {cultureName}");
            number.Should().Be(0, "a failed parse must not expose an infinity or NaN to callers");
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("bg-BG")]
    [InlineData("de-DE")]
    public void FiniteLocalizedDecimalsGroupingAndPercent_StillParse(string cultureName)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        var culture = CultureInfo.CurrentCulture;
        var inputs = new (string Text, double Expected)[]
        {
            (12.5.ToString("0.0", culture), 12.5),
            (1234.5.ToString("N2", culture), 1234.5),
            ((-1234.5).ToString("N2", culture), -1234.5),
            (12.5.ToString("0.0", culture) + "%", 0.125),
            ("1E3", 1000),
        };
        foreach (var (text, expected) in inputs)
        {
            ExcelTextNumberParser.TryParse(text, out var number).Should().BeTrue(
                $"'{text}' is a valid localized number under {cultureName}");
            number.Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("bg-BG")]
    [InlineData("de-DE")]
    public void NonFiniteText_ValueArithmeticAndFinancialCoercion_ReturnValueError(string cultureName)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Finite text");
        foreach (var text in NonFiniteTexts(CultureInfo.CurrentCulture))
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));
            foreach (var formula in new[] { "=VALUE(A1)", "=A1+0", "=PMT(A1,60,10000)" })
                evaluator.Evaluate(formula, sheet).Should().Be(ErrorValue.Value,
                    $"{formula} must reject non-finite text '{text}' under {cultureName}");
        }
    }

    private static IEnumerable<string> NonFiniteTexts(CultureInfo culture)
    {
        var symbols = culture.NumberFormat;
        foreach (var text in new[]
        {
            "1E309", "-1E309", symbols.NaNSymbol,
            symbols.PositiveInfinitySymbol, symbols.NegativeInfinitySymbol,
        })
        {
            yield return text;
            yield return text + "%";
        }

        // This reaches the grouping-validation parse path rather than just the exponent fast path.
        yield return string.Join(symbols.NumberGroupSeparator, Enumerable.Repeat("999", 110)) +
            symbols.NumberDecimalSeparator + "00";
    }
}
