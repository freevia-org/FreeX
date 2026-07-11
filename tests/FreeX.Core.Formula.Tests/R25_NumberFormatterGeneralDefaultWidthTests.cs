using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R25-meta-2 regression coverage: round-24's column-width-aware General format under-displayed
/// significant digits at the *real production* default column width.
///
/// ViewportService.GetDisplayText never passes a hand-picked width -- for a sheet's default
/// column (Sheet.DefaultColumnWidth = 8.43), it computes:
///   ColumnWidthToPixels(8.43)      = round(8.43 * 7 + 5)  = 64
///   EstimateCharacterWidth(64)     = round((64 - 5) / 7)  = 8
/// so the real value that reaches NumberFormatter.Format(..., "General", width) for the single
/// most common column width in the app is 8, not 11. Round 24's tests only ever exercised a
/// hand-picked width of 11 directly, so this discrepancy (width 8 fitting only 6 significant
/// digits, "0.333333", instead of Excel's actual 9-digit "0.333333333") was untested and live.
///
/// NumberFormatter.General.cs now applies a calibrated bonus (GeneralFormatDigitBudgetBonus = 3)
/// on top of the caller's generic average-character width estimate before fitting, so that the
/// real production width of 8 reproduces Excel's actual ~11-character General-format digit
/// budget at the default column width.
/// </summary>
public sealed class R25_NumberFormatterGeneralDefaultWidthTests
{
    // The real width value ViewportService.GetDisplayText computes for a sheet's default
    // column (Sheet.DefaultColumnWidth = 8.43): EstimateCharacterWidth(ColumnWidthToPixels(8.43))
    // = EstimateCharacterWidth(64) = round((64 - 5) / 7) = 8.
    private const int RealDefaultColumnWidthCharacters = 8;

    [Fact]
    public void General_Fraction_AtRealDefaultColumnWidth_Shows9SignificantDigitsLikeExcel()
    {
        // Bug case: at the real production default-column width (8, not the hand-picked 11 the
        // round-24 tests used), Excel shows 1/3 as "0.333333333" (9 threes), not the
        // under-displayed "0.333333" (6 threes) round 24's fitting loop produced for width 8.
        NumberFormatter.Format(new NumberValue(1.0 / 3.0), "General", RealDefaultColumnWidthCharacters)
            .Should().Be("0.333333333");
    }

    [Fact]
    public void General_ComplementaryFraction_AtRealDefaultColumnWidth_Shows9SignificantDigitsLikeExcel()
    {
        // Sibling case (rounds the last digit up, unlike 1/3): 2/3 -> "0.666666667".
        NumberFormatter.Format(new NumberValue(2.0 / 3.0), "General", RealDefaultColumnWidthCharacters)
            .Should().Be("0.666666667");
    }

    [Fact]
    public void General_LargeIntegerExceedingBudget_AtRealDefaultColumnWidth_FallsBackToScientificNotation()
    {
        // Sibling case that round 24 fixed and must keep working: a 15-digit integer still
        // can't fit even the corrected 11-character budget, so it still falls back to "1E+14"
        // instead of regressing to the pre-round-24 behavior of always showing the full integer.
        NumberFormatter.Format(new NumberValue(1e14), "General", RealDefaultColumnWidthCharacters)
            .Should().Be("1E+14");
    }

    [Fact]
    public void General_WithoutColumnWidthContext_StillKeepsFullPrecisionUnconstrained()
    {
        // Regression guard: callers with no column-width context at all (formula bar,
        // text-coercion) must be completely unaffected by the width-fitting/budget-bonus logic.
        NumberFormatter.Format(new NumberValue(1e14), "General")
            .Should().Be("100000000000000");

        NumberFormatter.Format(new NumberValue(1.0 / 3.0), "General")
            .Should().Be("0.3333333333");
    }

    [Fact]
    public void General_WideColumn_StillShowsMoreDigitsThanTheDefaultColumnWidth()
    {
        // Sibling case: a genuinely wide column (e.g. a 20-character-wide column, well above the
        // default) must still be able to show full double precision -- the additive calibration
        // bonus must not cap wide columns down to the default-width digit budget.
        NumberFormatter.Format(new NumberValue(1.0 / 3.0), "General", 20)
            .Should().Be("0.333333333333333");

        NumberFormatter.Format(new NumberValue(1e14), "General", 20)
            .Should().Be("100000000000000");
    }

    [Fact]
    public void General_VeryNarrowColumn_StillFallsBackToScientificNotationForALargeInteger()
    {
        // Sibling case: a much narrower-than-default column must still be narrower than the
        // default-width digit budget, not wider -- the calibration bonus is a fixed offset, so
        // relative ordering between column widths is preserved.
        NumberFormatter.Format(new NumberValue(1e14), "General", 1)
            .Should().Be("1E+14");
    }
}
