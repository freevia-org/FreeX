using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Round-91 regressions for two NumberFormatter rendering bugs verified against Excel:
//
//  R91-io-numfmt-edge-5-1: a DECORATED "General" keyword (a literal prefix/suffix or a sign
//  character wrapped around the bare word "General") rendered as the literal text itself
//  instead of the value's General-format rendering. E.g. the common "General;[Red]-General"
//  negative-coloring trick showed the literal word "-General" for a negative value instead of
//  "-42", and a quoted-prefix single-section format like "\"Value: \"General" showed the
//  literal "Value: General" instead of "Value: 42".
//
//  R91-io-numfmt-edge-5-2: a serial value past the representable date range (year > 9999)
//  formatted with a calendar date format rendered the raw number instead of Excel's all-'#'
//  invalid-value indicator -- the same indicator FreeX already correctly shows for a negative
//  serial under a date format (R21-number-format-render-deep-1).
public class R91_NumberFormatterGeneralAndDateOverflowTests
{
    private static bool IsAllHashCharacters(string text) =>
        text.Length > 0 && text.All(c => c == '#');

    // ── R91-io-numfmt-edge-5-1: decorated "General" keyword ────────────────────────────────

    [Fact]
    public void NumberValue_NegativeSignDecoratedGeneral_RendersValueNotLiteralWord()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(-42), "General;-General") ==
        // "-General" (the literal word), instead of Excel's "-42".
        var result = NumberFormatter.Format(new NumberValue(-42), "General;-General");

        result.Should().Be("-42");
    }

    [Fact]
    public void NumberValue_QuotedPrefixDecoratedGeneral_RendersValueNotLiteralWord()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(42), "\"Value: \"General") ==
        // "Value: General" (literal), instead of Excel's "Value: 42".
        var result = NumberFormatter.Format(new NumberValue(42), "\"Value: \"General");

        result.Should().Be("Value: 42");
    }

    [Fact]
    public void NumberValue_ColorAndSignDecoratedGeneral_RendersValueWithColorApplied()
    {
        // The real-world motivating case: color negatives via "General;[Red]-General".
        var result = NumberFormatter.FormatWithColor(new NumberValue(-42), "General;[Red]-General");

        result.Text.Should().Be("-42");
        result.ColorHex.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void NumberValue_PositiveSectionOfDecoratedGeneralFormat_StillRendersPlainGeneral()
    {
        // Sanity/regression guard: the unconditioned first ("General") section of a decorated
        // multi-section format must keep working exactly as plain General does today.
        var result = NumberFormatter.Format(new NumberValue(42), "General;-General");

        result.Should().Be("42");
    }

    [Fact]
    public void NumberValue_ExactGeneralFormat_IsUnaffected()
    {
        // Sanity/regression guard: the plain, undecorated "General" keyword (handled by the
        // pre-existing IsGeneralFormat exact match) must be completely unaffected by this fix.
        var result = NumberFormatter.Format(new NumberValue(1234.5), "General");

        result.Should().Be(NumberFormatter.Format(new NumberValue(1234.5), ""));
    }

    [Fact]
    public void NumberValue_RealNumericPatternContainingLetterG_StillFormatsAsNumber()
    {
        // Sanity/regression guard: a genuine numeric placeholder pattern must never be
        // misdetected as a decorated-General format, even if it happens to contain "General"
        // as literal quoted text alongside real digit placeholders.
        var result = NumberFormatter.Format(new NumberValue(42), "0\" General\"");

        result.Should().Be("42 General");
    }

    // ── R91-io-numfmt-edge-5-2: date-format serial overflow ────────────────────────────────

    [Fact]
    public void NumberValue_SerialPastMaxDateRangeWithDateFormat_ShowsInvalidIndicatorNotRawNumber()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(5000000), "m/d/yyyy") == "5000000"
        // (the raw number), instead of Excel's all-'#' invalid-value indicator.
        var result = NumberFormatter.Format(new NumberValue(5000000), "m/d/yyyy");

        IsAllHashCharacters(result).Should().BeTrue(
            $"Excel shows an invalid-value indicator (all '#') for an out-of-range date value, but got \"{result}\"");
    }

    [Fact]
    public void NumberValue_SerialJustPastMaxDateRangeWithDateFormat_ShowsInvalidIndicator()
    {
        // DateTime.MaxValue.Date is 9999-12-31; one day past its serial must already overflow.
        var maxSerial = (DateTime.MaxValue.Date - new DateTime(1899, 12, 30)).TotalDays;
        var result = NumberFormatter.Format(new NumberValue(maxSerial + 1), "m/d/yyyy");

        IsAllHashCharacters(result).Should().BeTrue(
            $"Excel shows an invalid-value indicator (all '#') for an out-of-range date value, but got \"{result}\"");
    }

    [Fact]
    public void NumberValue_SerialWithinMaxDateRangeWithDateFormat_StillRendersRealDate()
    {
        // Sanity/regression guard: the last valid in-range serial (9999-12-31) must keep
        // rendering as a real date, completely unaffected by the overflow fix.
        var maxSerial = (DateTime.MaxValue.Date - new DateTime(1899, 12, 30)).TotalDays;
        var result = NumberFormatter.Format(new NumberValue(maxSerial), "m/d/yyyy");

        result.Should().Be("12/31/9999");
    }

    [Fact]
    public void NumberValue_ModerateSerialWithDateFormat_StillRendersRealDate()
    {
        // Sanity/regression guard: an ordinary, well-in-range serial is unaffected.
        var result = NumberFormatter.Format(new NumberValue(1), "m/d/yyyy");

        result.Should().Be("1/1/1900");
    }
}
