using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-145 numeric-boundaries finding F1: engineering-notation number formats (e.g.
/// "##0.00E+0" -- Excel's engineering-notation pattern, also FreeX's built-in numFmtId 48
/// family) must round the mantissa the same way the rest of the codebase rounds decimals --
/// via a decimal-first conversion -- rather than via Math.Round/"F" on the raw IEEE double
/// produced by absValue / Math.Pow(10, exponent). Raw-double rounding disagrees with Excel at
/// exact-half decimal boundaries such as 1005 -> mantissa 1.005, which is actually stored as
/// 1.0049999999999999... and rounds DOWN under naive double rounding but UP (matching Excel)
/// under decimal-first rounding.
/// </summary>
public sealed class R145_EngineeringNumberFormatMantissaRoundingTests
{
    // ── F1: engineering-format mantissa must round like Excel's decimal-first rule ──────────

    [Fact]
    public void FormatEngineering_1005_RoundsMantissaUpLikeExcel()
    {
        // 1005 / 1000 = 1.005, whose true double value is 1.0049999999999999... . Excel (and
        // FreeX's own "0.00E+00" plain-scientific path / "0.00" path / ROUND()) rounds this
        // UP to 1.01, not down to 1.00.
        var result = NumberFormatter.Format(new NumberValue(1005), "##0.00E+0");
        result.Should().Be("1.01E+3");
    }

    [Theory]
    [InlineData(4145, "4.15E+3")]
    [InlineData(9075, "9.08E+3")]
    [InlineData(8575, "8.58E+3")]
    [InlineData(4895, "4.90E+3")]
    [InlineData(4395, "4.40E+3")]
    [InlineData(2445, "2.45E+3")] // 2445/1000 = 2.445 -> raw double is 2.4449999999999998...
    [InlineData(66475, "66.48E+3")]
    [InlineData(77725, "77.73E+3")]
    [InlineData(9165, "9.17E+3")]
    public void FormatEngineering_HalfDecimalBoundaryValues_MatchExcelRounding(double input, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(input), "##0.00E+0");
        result.Should().Be(expected);
    }

    [Fact]
    public void FormatEngineering_MatchesPlainScientificPathRoundingForSameMantissa()
    {
        // The plain-scientific path ("0.00E+00", which does NOT go through FormatEngineering)
        // already rounds correctly via .NET's own double-to-decimal-string formatting. The
        // engineering path (which DOES go through FormatEngineering) must agree with it for the
        // same underlying mantissa value.
        var engineering = NumberFormatter.Format(new NumberValue(1005), "##0.00E+0");
        var plainScientific = NumberFormatter.Format(new NumberValue(1005), "0.00E+00");

        engineering.Should().Be("1.01E+3");
        plainScientific.Should().Be("1.01E+03");
    }

    // ── Adjacent no-regression case: ordinary (non-half-boundary) engineering rounding ──────

    [Fact]
    public void FormatEngineering_OrdinaryValue_StillRoundsCorrectly()
    {
        // 1234 / 1000 = 1.234 -> rounds to 1.23 (no half-boundary ambiguity); confirms the
        // decimal-first change didn't disturb the common case.
        var result = NumberFormatter.Format(new NumberValue(1234), "##0.00E+0");
        result.Should().Be("1.23E+3");
    }

    [Fact]
    public void FormatEngineering_ExponentBumpOnOverflow_StillWorks()
    {
        // 999950 with 1 decimal place: mantissa before rounding is 999.95 (exponent 3), which
        // rounds to 1000.0 and must bump the exponent to 6, producing "1.0E+6" not "1000.0E+3".
        var result = NumberFormatter.Format(new NumberValue(999950), "##0.0E+0");
        result.Should().Be("1.0E+6");
    }

    [Fact]
    public void FormatEngineering_ZeroValue_StillFormatsAsZero()
    {
        // Excel fills every digit placeholder in the "##0" whole-part group (all 3 of them)
        // with zero when the value is exactly 0.
        var result = NumberFormatter.Format(new NumberValue(0), "##0.00E+0");
        result.Should().Be("000.00E+0");
    }

    [Fact]
    public void FormatEngineering_NegativeValue_StillFormatsWithSign()
    {
        var result = NumberFormatter.Format(new NumberValue(-1005), "##0.00E+0");
        result.Should().Be("-1.01E+3");
    }
}
