using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-151 finding meta-F1: the r150 fix (commit 5a33906e49) generalized
/// ExcelTextNumberParser's grouping/decimal regex from hardcoded ',' / '.' to
/// culture.NumberFormat.NumberGroupSeparator / NumberDecimalSeparator, but for any locale whose
/// group separator is a whitespace character (fr-FR uses U+202F narrow no-break space), the fast-path
/// guard and the validation regex only recognized that one exact code point — not the ordinary space
/// (U+0020, what a keyboard actually produces) or the plain non-breaking space (U+00A0, common from
/// other apps/websites) that real text actually uses for the same grouping. The currency-symbol match
/// in the same regex (and in LooksNumeric) also stayed hardcoded to '$' regardless of
/// culture.NumberFormat.CurrencySymbol, so a Euro-suffixed amount under de-DE also failed.
/// Fixed by normalizing any whitespace character in the text to the culture's canonical group
/// separator before grouping detection/validation/parse (NormalizeGroupSeparatorSpaceVariants), and
/// by reading the currency symbol from culture.NumberFormat.CurrencySymbol instead of a literal '$'
/// in both GetValidGroupingRegex and LooksNumeric.
/// </summary>
public sealed class R151_TextNumberParserSpaceGroupingAndCurrencyTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    // ── fr-FR (U+202F narrow no-break space grouping, ',' decimal): the fix ────────────────

    [Fact]
    public void FrFrCulture_ExactCultureNbsp_PlusZero_CoercesToNumber()
    {
        // Sibling no-regression: the r150 fix already got this one right (the exact code point
        // CultureInfo reports) — must keep working after the normalization change.
        using var culture = new TestCultureScope("fr-FR");
        string groupSep = System.Globalization.CultureInfo.GetCultureInfo("fr-FR").NumberFormat.NumberGroupSeparator;

        _eval.Evaluate($"=\"1{groupSep}234,56\"+0", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void FrFrCulture_PlainAsciiSpace_UserTyped_PlusZero_CoercesToNumber()
    {
        // The bug: an ordinary space (U+0020, what a keyboard actually produces) used as the
        // thousands separator returned #VALUE! even though it visually matches fr-FR grouping.
        using var culture = new TestCultureScope("fr-FR");

        _eval.Evaluate("=\"1 234,56\"+0", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void FrFrCulture_PlainAsciiSpace_ValueFunction_CoercesToNumber()
    {
        using var culture = new TestCultureScope("fr-FR");

        _eval.Evaluate("=VALUE(\"1 234,56\")", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void FrFrCulture_RegularNonBreakingSpace_U00A0_PlusZero_CoercesToNumber()
    {
        // The bug: a plain non-breaking space (U+00A0, common from other apps/websites) also
        // returned #VALUE! — .NET's own double.TryParse does not accept it either (verified via a
        // standalone probe), so this exercises the normalization step rather than relying on .NET
        // leniency the way the plain-ASCII-space case incidentally could.
        using var culture = new TestCultureScope("fr-FR");

        _eval.Evaluate("=\"1 234,56\"+0", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void FrFrCulture_MultipleGroups_PlainAsciiSpace_CoercesToNumber()
    {
        using var culture = new TestCultureScope("fr-FR");

        _eval.Evaluate("=\"1 234 567,5\"+0", Sheet())
            .Should().Be(new NumberValue(1234567.5));
    }

    [Fact]
    public void FrFrCulture_BadSpaceGrouping_IsValueError_NotMisreadAsDate()
    {
        // Malformed grouping ("1 2" is not a valid 3-digit group) must stay #VALUE!, mirroring
        // the en-US "1,2" / de-DE "1.2" contracts — not silently accepted or misread as a date.
        using var culture = new TestCultureScope("fr-FR");

        _eval.Evaluate("=\"1 2\"+0", Sheet())
            .Should().Be(ErrorValue.Value);
    }

    // ── de-DE ('.' grouping, ',' decimal) with a Euro currency symbol: the fix ─────────────

    [Fact]
    public void DeDeCulture_EuroCurrencySymbolWithSpace_PlusZero_CoercesToNumber()
    {
        // The bug: the regex's currency-symbol check was hardcoded to '$' regardless of
        // culture.NumberFormat.CurrencySymbol, so a Euro-suffixed amount under de-DE returned
        // #VALUE! even though double.TryParse itself can parse it once the shape gate lets it
        // through.
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=\"1.234,56 €\"+0", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void DeDeCulture_EuroCurrencySymbolNoSpace_ValueFunction_CoercesToNumber()
    {
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=VALUE(\"1.234,56€\")", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    // ── sibling no-regression: r150's own coverage (en-US ',' / de-DE '.') stays correct ───

    [Theory]
    [InlineData("=\"1,234\"+0", 1234.0)]
    [InlineData("=\"1,234,567.5\"+0", 1234567.5)]
    [InlineData("=0+\"$1,234.50\"", 1234.5)]
    public void EnUsCulture_CommaGroupingAndDollarCurrency_StillCoercesToNumber(string formula, double expected)
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=\"1,2\"+0")]
    [InlineData("=\"12,34\"+0")]
    public void EnUsCulture_BadCommaGrouping_StillValueError(string formula)
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void DeDeCulture_DotGroupedCommaDecimal_NoCurrency_StillCoercesToNumber()
    {
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=\"1.234,56\"+0", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void EnUsCulture_DateTextWithCommas_StillParsesAsDate_NotBlockedByCurrencyCheck()
    {
        // Sibling no-regression for LooksNumeric: a date string starting with a month name must
        // still reach the DateTime fallback (not be misclassified as "looks numeric").
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate("=\"March 14, 2026\"+0", Sheet())
            .Should().NotBe(ErrorValue.Value);
    }
}
