using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-150 finding number-format-locale F1: ExcelTextNumberParser.TryParseNumericStrict hardcoded
/// ',' as the only recognized thousands-grouping character (both in the "no comma -> fast path" guard
/// and in ValidGroupingRegex's literal ',' / '.' pattern), regardless of the culture parameter already
/// threaded through the method. Under a culture whose group separator isn't ',' (e.g. de-DE, where '.'
/// groups and ',' is the decimal separator):
///   - "1.234,56"+0 / VALUE("1.234,56") incorrectly returned #VALUE! (ValidGroupingRegex, built with a
///     literal ',' grouping / '.' decimal pattern, never matched the de-DE-grouped text).
///   - "1.234"+0 incorrectly fell through to DateTime.TryParse and returned a garbage date serial,
///     because the fast path only skipped grouping validation when the text lacked a literal ',' -
///     "1.234" has no ',' so it took the fast path, failed StylesWithoutThousands (since '.' isn't
///     de-DE's decimal separator), and was never re-tried with AllowThousands under the correct culture.
/// Fixed by reading the group/decimal separators from CultureInfo.NumberFormat (already passed into
/// TryParseNumericStrict) instead of hardcoding ',' / '.', and caching the resulting regex per
/// (group separator, decimal separator) pair.
/// </summary>
public sealed class R150_TextNumberParserLocaleGroupingTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    // ── de-DE ('.' groups, ',' decimal): the fix ────────────────────────────────────────────

    [Fact]
    public void DeDeCulture_DotGroupedCommaDecimal_PlusZero_CoercesToNumber()
    {
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=\"1.234,56\"+0", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void DeDeCulture_DotGroupedCommaDecimal_ValueFunction_CoercesToNumber()
    {
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=VALUE(\"1.234,56\")", Sheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void DeDeCulture_DotGroupedIntegerNoDecimal_PlusZero_CoercesToNumber_NotGarbageDateSerial()
    {
        // Pre-fix this fell through to DateTime.TryParse and produced a wildly wrong serial
        // (e.g. -608493) instead of 1234, because the fast path never validated grouping for a
        // '.'-grouping culture.
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=\"1.234\"+0", Sheet())
            .Should().Be(new NumberValue(1234.0));
    }

    [Fact]
    public void DeDeCulture_DotGroupedThreeGroups_PlusZero_CoercesToNumber()
    {
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=\"1.234.567\"+0", Sheet())
            .Should().Be(new NumberValue(1234567.0));
    }

    [Fact]
    public void DeDeCulture_BadDotGrouping_IsValueError_NotMisreadAsDate()
    {
        // Malformed grouping ("1.2" is not a valid 3-digit group) must stay #VALUE!, not be
        // silently misread as a date/garbage serial, mirroring the en-US "1,2" contract.
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=\"1.2\"+0", Sheet())
            .Should().Be(ErrorValue.Value);
    }

    // ── en-US (',' groups, '.' decimal): sibling no-regression ─────────────────────────────

    [Theory]
    [InlineData("=\"1,234\"+0", 1234.0)]
    [InlineData("=\"1,234,567.5\"+0", 1234567.5)]
    [InlineData("=0+\"$1,234.50\"", 1234.5)]
    public void EnUsCulture_CommaGrouping_StillCoercesToNumber(string formula, double expected)
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=\"1,2\"+0")]
    [InlineData("=\"12,34\"+0")]
    [InlineData("=\"1,2345\"+0")]
    public void EnUsCulture_BadCommaGrouping_StillValueError(string formula)
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }
}
