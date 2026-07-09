using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Round-20 regressions for three NumberFormatter rendering bugs verified against Excel:
//
//  R20-text-functions-1: TEXT() (targetWidthCharacters=null, i.e. no column context) dropped
//  underscore/asterisk fill directives entirely instead of rendering the reserved space.
//  Excel: TEXT(1234,"#,##0_)") == "1,234 " (one trailing space reserving room for ")");
//  a bare asterisk fill directive must likewise render at least one occurrence rather than
//  being silently dropped.
//
//  R20-text-functions-2: fraction format codes (e.g. "# ?/4") used bare Math.Round (banker's
//  round-half-to-even) when converting the fractional part to a numerator, so an exact tie
//  (e.g. 0.125 * 4 == 0.5) rounded to 0 and dropped the fraction entirely instead of rounding
//  away from zero like Excel (and like every other rounding call site in this project).
//
//  R20-number-format-rendering-1: a 2- or 3-section number format (no dedicated 4th text
//  section) wrongly applied its first (positive) section to TEXT values whenever that first
//  section happened to contain '@' — Excel only ever applies a section to text via a true
//  4th section, or when the whole format is a single section.
public class R20_text_format_rendering_Tests
{
    private readonly FormulaEvaluator _evaluator = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        return _evaluator.Evaluate(formula, sheet, wb);
    }

    // ── R20-text-functions-1: underscore/asterisk fill directives dropped by TEXT() ────────

    [Fact]
    public void Text_UnderscoreFillDirective_RendersReservedSpaceWithNoTargetWidth()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(1234), "#,##0_)") == "1,234" (no space).
        // Excel:  TEXT(1234,"#,##0_)") == "1,234 " (one trailing space for the ")" it reserves).
        var result = NumberFormatter.Format(new NumberValue(1234), "#,##0_)");

        result.Should().Be("1,234 ");
    }

    [Fact]
    public void Text_UnderscoreFillDirective_RendersReservedSpaceThroughFormula()
    {
        var result = Eval("=TEXT(1234,\"#,##0_)\")");

        result.Should().Be(new TextValue("1,234 "));
    }

    [Fact]
    public void Text_AsteriskFillDirective_RendersAtLeastOneCharacterWithNoTargetWidth()
    {
        // Pre-fix: the "*-" fill directive (no accounting symbol prefix) was dropped entirely,
        // yielding "12" instead of reserving/repeating the fill character at least once.
        var result = NumberFormatter.Format(new NumberValue(12), "0*-");

        result.Should().Be("12-");
    }

    // ── R20-text-functions-2: fraction rounding ties dropped by banker's rounding ──────────

    [Theory]
    [InlineData("=TEXT(3.125,\"# ?/4\")", "3 1/4")]
    [InlineData("=TEXT(3.0625,\"# ?/8\")", "3 1/8")]
    public void Text_FractionFormat_RoundsExactTiesAwayFromZero(string formula, string expected) =>
        Eval(formula).Should().Be(new TextValue(expected));

    [Fact]
    public void Text_FractionFormat_ZeroWholePartRoundsExactTieAwayFromZero()
    {
        // 0.25 * 2 == 0.5 is an exact tie; Excel rounds away from zero to 1/2, not 0.
        var result = Eval("=TEXT(0.25,\"# ?/2\")");

        result.Should().Be(new TextValue(" 1/2"));
    }

    [Fact]
    public void FormatSimpleFraction_ExactTie_DoesNotDropFractionViaBankersRounding()
    {
        // Direct formatter check mirroring the finding's cited repro: pre-fix this returned
        // "3    " (whole number plus blank padding, fraction silently dropped).
        var result = NumberFormatter.Format(new NumberValue(3.125), "# ?/4");

        result.Should().Be("3 1/4");
    }

    // ── R20-number-format-rendering-1: 2/3-section '@' format wrongly applied to text ─────

    [Fact]
    public void TextValue_TwoSectionFormatContainingAtSign_IsUnaffectedByPositiveSection()
    {
        // "USD "@ is the positive/zero section (2-section format: positive/zero, negative).
        // With no dedicated 4th text section, Excel never applies any section to a text
        // value, even though the first section contains '@'.
        var result = NumberFormatter.Format(new TextValue("Total"), "\"USD \"@;-0.00");

        result.Should().Be("Total");
    }

    [Fact]
    public void TextValue_ThreeSectionFormatContainingAtSign_IsUnaffectedByPositiveSection()
    {
        var result = NumberFormatter.Format(new TextValue("Total"), "\"USD \"@;-0.00;0.00");

        result.Should().Be("Total");
    }

    [Fact]
    public void TextValue_SingleSectionFormatContainingAtSign_StillApplies()
    {
        // Sanity check: the single-section case (no ';' at all) must still apply, since the
        // fix narrows the "sections.Length <= 3" check to "sections.Length == 1" rather than
        // disabling text-section application altogether.
        var result = NumberFormatter.Format(new TextValue("Total"), "\"USD \"@");

        result.Should().Be("USD Total");
    }

    [Fact]
    public void TextValue_FourSectionFormat_StillAppliesDedicatedTextSection()
    {
        // Sanity check: a true 4th (text) section must still apply normally.
        var result = NumberFormatter.Format(new TextValue("Total"), "0;-0;0;\"USD \"@");

        result.Should().Be("USD Total");
    }
}
