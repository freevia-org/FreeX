using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R16-number-format-render-deep-3: ExcelDateTimeFormatConverter had no
/// mapping for the locale day-of-week tokens "aaa"/"aaaa" (used by some non-English Excel number
/// format codes), so the TryConsume chain fell through to the generic "copy verbatim" branch and
/// the literal text "aaa"/"aaaa" leaked into the rendered string instead of the abbreviated/full
/// weekday name. Fixed by mapping aaaa -&gt; dddd (full weekday) and aaa -&gt; ddd (abbreviated
/// weekday), consulted after the AM/PM and A/P checks so those (which require a literal slash)
/// still win first.
///
/// Note on R16-number-format-render-deep-1 (time format with no seconds token rounding to the
/// coarser displayed unit): NOT implemented. The repo's own real-Excel-captured golden fixture
/// (tests/FreeX.Core.Calc.Tests/TestData/ExcelNumberFormatMatrix.csv, captured via Excel COM's
/// range.Text in tools/FreeX.NumberFormatParity) directly contradicts the finding's premise: row
/// "0.3333,Number,h:mm AM/PM,7:59 AM" corresponds to an underlying time of 7:59:57.12 (57.12 elided
/// seconds - well past any half-minute threshold), yet real Excel still displays "7:59 AM", not
/// "8:00 AM". This shows Excel does NOT round the displayed minute/hour based on an omitted finer
/// unit; it only rounds to the nearest whole SECOND (already implemented via RoundToNearestSecond)
/// and then simply omits whatever unit isn't in the format string. Implementing the finding as
/// described would regress NumberFormatterParityTests.cs (real Excel ground truth) and the existing
/// CustomNumberSubset_DisambiguatesMinutesAcrossQuotedLiterals test, so no code change was made for
/// that finding. See task notes for details.
/// </summary>
public sealed class R16NumFmtDateTests
{
    [Theory]
    [InlineData("aaaa, mmmm d, yyyy", "Wednesday, January 3, 2024")]
    [InlineData("aaa, mmmm d, yyyy", "Wed, January 3, 2024")]
    public void CustomNumberSubset_MapsDayOfWeekLocaleTokensToWeekdayNames(string format, string expected)
    {
        // 2024-01-03 is a Wednesday.
        var value = new DateTimeValue(new DateTime(2024, 1, 3).ToOADate());

        var result = NumberFormatter.Format(value, format);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("h:mm AM/PM", "1:30 PM")]
    [InlineData("h:mm A/P", "1:30 P")]
    public void CustomNumberSubset_StillFormatsAmPmTokensAlongsideDayOfWeekFix(string format, string expected)
    {
        var value = new DateTimeValue(new DateTime(2024, 1, 3, 13, 30, 0).ToOADate());

        var result = NumberFormatter.Format(value, format);

        result.Should().Be(expected);
    }
}
