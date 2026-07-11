using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Round-21 regressions for two NumberFormatter rendering bugs verified against Excel:
//
//  R21-number-format-render-deep-1: a negative value formatted with a genuine calendar
//  date/time-only format code (no elapsed-time brackets) produced a fabricated, plausible-
//  looking but bogus date/time instead of Excel's invalid-value indicator. Excel treats a
//  negative value as fundamentally invalid for ANY date/time format -- this is not a column-
//  width artifact (widening the column never reveals a real date underneath). Elapsed-time
//  bracket formats ("[h]:mm:ss" etc.) are a distinct duration concept and are exempt: Excel
//  (and FreeX, both before and after this fix) correctly renders those with a leading '-' for
//  negative elapsed durations.
//
//  R21-number-format-render-deep-2: a custom format with irregular (Indian lakh/crore-style)
//  thousands grouping written directly as literal comma positions in the pattern (e.g.
//  "#,##,##0") silently collapsed to standard Western 3-digit grouping unless a matching
//  [$-locale] token was also present. Excel derives irregular grouping purely from the comma
//  positions actually written in the format code, with no locale token required.
public class R21_NumberFormatterInvalidDateAndIrregularGroupingTests
{
    private static bool IsAllHashCharacters(string text) =>
        text.Length > 0 && text.All(c => c == '#');

    // ── R21-number-format-render-deep-1: negative value + date/time-only format ────────────

    [Fact]
    public void NumberValue_NegativeIntegerWithDateFormat_ShowsInvalidIndicatorNotFabricatedDate()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(-1), "m/d/yyyy") == "-1/1/1900".
        // Excel shows an all-'#' invalid-value indicator instead of a fabricated date.
        var result = NumberFormatter.Format(new NumberValue(-1), "m/d/yyyy");

        IsAllHashCharacters(result).Should().BeTrue(
            $"Excel shows an invalid-value indicator (all '#') for a negative date value, but got \"{result}\"");
    }

    [Fact]
    public void NumberValue_NegativeFractionalWithDateFormat_ShowsInvalidIndicatorNotFabricatedDate()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(-0.5), "m/d/yyyy") == "-12/31/1899".
        var result = NumberFormatter.Format(new NumberValue(-0.5), "m/d/yyyy");

        IsAllHashCharacters(result).Should().BeTrue(
            $"Excel shows an invalid-value indicator (all '#') for a negative date value, but got \"{result}\"");
    }

    [Fact]
    public void NumberValue_NegativeWithPlainTimeFormat_ShowsInvalidIndicatorNotSilentZero()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(-1), "h:mm:ss") == "0:00:00" -- the
        // magnitude (1 full day) happens to format to an all-zero clock time, silently
        // dropping the sign and hiding that the source value is invalid for this format.
        var result = NumberFormatter.Format(new NumberValue(-1), "h:mm:ss");

        IsAllHashCharacters(result).Should().BeTrue(
            $"Excel shows an invalid-value indicator (all '#') for a negative time value, but got \"{result}\"");
    }

    [Fact]
    public void DateTimeValue_NegativeSerialWithDateFormat_ShowsInvalidIndicatorNotFabricatedDate()
    {
        // Pre-fix: NumberFormatter.Format(new DateTimeValue(-1), "m/d/yyyy") == "12/30/1899"
        // -- the sign was silently dropped entirely, showing a normal-looking wrong date.
        var result = NumberFormatter.Format(new DateTimeValue(-1), "m/d/yyyy");

        IsAllHashCharacters(result).Should().BeTrue(
            $"Excel shows an invalid-value indicator (all '#') for a negative date value, but got \"{result}\"");
    }

    [Fact]
    public void NumberValue_NegativeWithBracketPrefixedLocaleDateFormat_ShowsInvalidIndicator()
    {
        // Covers the general (multi-section-parser) path for a single, unconditioned section
        // that starts with a bracket directive (e.g. a [$-locale] token) rather than the plain
        // fast path used by a bracket-free format like "m/d/yyyy".
        var result = NumberFormatter.Format(new NumberValue(-1), "[$-409]m/d/yyyy");

        IsAllHashCharacters(result).Should().BeTrue(
            $"Excel shows an invalid-value indicator (all '#') for a negative date value, but got \"{result}\"");
    }

    [Fact]
    public void NumberValue_NegativeWithElapsedTimeBracketFormat_StillRendersNegativeDurationCorrectly()
    {
        // Sanity/regression guard: elapsed-time bracket formats are a distinct "duration"
        // concept (not a calendar date/time) and Excel DOES support negative durations here.
        // This must NOT be affected by the invalid-value fix above.
        var result = NumberFormatter.Format(new NumberValue(-1), "[h]:mm:ss");

        result.Should().Be("-24:00:00");
    }

    [Fact]
    public void NumberValue_PositiveWithDateFormat_StillRendersRealDate()
    {
        // Sanity/regression guard: positive values are completely unaffected by the fix.
        var result = NumberFormatter.Format(new NumberValue(1), "m/d/yyyy");

        result.Should().Be("1/1/1900");
    }

    // ── R21-number-format-render-deep-2: irregular (Indian lakh/crore) comma grouping ──────

    [Fact]
    public void NumberValue_IndianGroupingPattern_HonorsIrregularGroupingWithNoLocaleToken()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(1234567), "#,##,##0") == "1,234,567"
        // (plain Western 3-digit grouping). Excel: "12,34,567".
        var result = NumberFormatter.Format(new NumberValue(1234567), "#,##,##0");

        result.Should().Be("12,34,567");
    }

    [Fact]
    public void NumberValue_IndianGroupingPattern_HonorsIrregularGroupingForLargerValue()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(12345678), "#,##,##0") ==
        // "12,345,678". Excel: "1,23,45,678".
        var result = NumberFormatter.Format(new NumberValue(12345678), "#,##,##0");

        result.Should().Be("1,23,45,678");
    }

    [Fact]
    public void NumberValue_IndianGroupingPattern_HonorsIrregularGroupingWithDecimals()
    {
        var result = NumberFormatter.Format(new NumberValue(1234567), "#,##,##0.00");

        result.Should().Be("12,34,567.00");
    }

    [Fact]
    public void NumberValue_StandardWesternGroupingPattern_IsUnaffected()
    {
        // Sanity/regression guard: a plain "#,##0"-shaped pattern (uniform 3-digit grouping,
        // the overwhelmingly common case) must keep rendering exactly as before.
        var result = NumberFormatter.Format(new NumberValue(1234567), "#,##0");

        result.Should().Be("1,234,567");
    }

    [Fact]
    public void NumberValue_IndianGroupingPatternWithLocaleToken_StillWorksAsBefore()
    {
        // Sanity/regression guard: the already-working [$-439] locale-token-driven path
        // (NumberFormatter.Locale.cs's NumberGroupSizes wiring) must be unaffected.
        var result = NumberFormatter.Format(new NumberValue(1234567), "[$-439]#,##,##0");

        result.Should().Be("12,34,567");
    }
}
