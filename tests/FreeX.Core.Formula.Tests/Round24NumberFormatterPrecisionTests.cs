using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-24 regression coverage for three NumberFormatter display-precision bugs:
///   - R24-number-precision-edge-1: General format ignored column width entirely, so large
///     integers never fell back to scientific notation and decimals were always shown with a
///     fixed 10 significant digits regardless of how narrow/wide the column actually was.
///   - R24-number-precision-edge-2: -0.0 (IEEE negative zero) displayed with a spurious leading
///     minus sign under ordinary numeric ("0.00") and scientific ("0.00E+00") formats.
///   - R24-cell-editing-deep-1: a leading force-text apostrophe (e.g. '5) was rendered literally
///     in the General-format grid display instead of being stripped (Excel keeps the apostrophe
///     in the formula bar only).
/// </summary>
public sealed class Round24NumberFormatterPrecisionTests
{
    // ── R24-number-precision-edge-1 ──────────────────────────────────────────

    [Fact]
    public void General_LargeIntegerAtDefaultColumnWidth_FallsBackToScientificNotation()
    {
        // 10^14 is a 15-digit integer. At Excel's canonical default-column-width digit budget
        // (11 characters), the plain integer can't fit, so Excel switches to "1E+14".
        NumberFormatter.Format(new NumberValue(1e14), "General", 11)
            .Should().Be("1E+14");
    }

    [Fact]
    public void General_Fraction_AtDefaultColumnWidth_Shows9SignificantDigitsLikeExcel()
    {
        // Excel's own canonical example: at the default column width, General format shows
        // 1/3 as "0.333333333" (9 threes) and 2/3 as "0.666666667" (rounded), not FreeX's
        // previous fixed-10-significant-digit "0.3333333333"/"0.6666666667".
        NumberFormatter.Format(new NumberValue(1.0 / 3.0), "General", 11)
            .Should().Be("0.333333333");

        NumberFormatter.Format(new NumberValue(2.0 / 3.0), "General", 11)
            .Should().Be("0.666666667");
    }

    [Fact]
    public void General_WithoutColumnWidthContext_KeepsFullPrecisionUnconstrained()
    {
        // Callers with no live column-width context (e.g. formula-bar/text-coercion paths)
        // must be unaffected by the new width-aware behavior: a 15-digit integer still shows
        // in full, matching the pre-existing (and still-correct-for-a-wide-enough-column)
        // General rendering.
        NumberFormatter.Format(new NumberValue(1e14), "General")
            .Should().Be("100000000000000");
    }

    // ── R24-number-precision-edge-2 ──────────────────────────────────────────

    [Fact]
    public void NegativeZero_UnderPlainNumericFormat_NeverShowsMinusSign()
    {
        // =0*-1 and =-1*0 both produce IEEE-754 negative zero.
        NumberFormatter.Format(new NumberValue(0.0 * -1.0), "0.00")
            .Should().Be("0.00");

        NumberFormatter.Format(new NumberValue(-1.0 * 0.0), "0")
            .Should().Be("0");
    }

    [Fact]
    public void NegativeZero_UnderScientificFormat_NeverShowsMinusSign()
    {
        NumberFormatter.Format(new NumberValue(0.0 * -1.0), "0.00E+00")
            .Should().Be("0.00E+00");
    }

    // ── R24-cell-editing-deep-1 ───────────────────────────────────────────────

    [Fact]
    public void General_LeadingForceTextApostrophe_IsHiddenFromGridDisplay()
    {
        // Typing '5 into a cell stores the text "'5" (the apostrophe is a formula-bar-only
        // "force text" marker in Excel), but the grid itself must display only "5".
        NumberFormatter.Format(new TextValue("'5"), "General")
            .Should().Be("5");
    }

    [Fact]
    public void General_TextWithoutLeadingApostrophe_IsUnaffected()
    {
        NumberFormatter.Format(new TextValue("Hello"), "General")
            .Should().Be("Hello");
    }
}
