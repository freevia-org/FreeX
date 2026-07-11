using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R22-meta-1: a negative value formatted with a bracket-prefixed single-section date/time
// format (e.g. a [$-locale] token or a [Color] directive) reaches NumberFormatter's generic
// ApplyNumericFormat date/time branch rather than FormatNumber's plain-format fast path.
// That branch hardcoded targetWidthCharacters=null when building the invalid-value ('#'-run)
// indicator, so it always fell back to the format string's own length instead of honoring the
// real column width the caller supplied -- unlike the equivalent non-bracketed fast path, which
// already threaded targetWidthCharacters through correctly. Fixed by adding a
// targetWidthCharacters parameter to ApplyNumericFormat and threading it from both of
// FormatNumber's call sites.
public class R22_NumberFormatterBracketPrefixedInvalidDateWidthTests
{
    [Fact]
    public void NegativeValue_WithLocaleBracketPrefixedDateFormat_FillsInvalidIndicatorToRequestedWidth()
    {
        // Pre-fix: returned only 8 '#' characters (the literal length of "[$-409]m/d/yyyy"),
        // ignoring the requested column width of 20.
        var result = NumberFormatter.FormatWithColor(new NumberValue(-1), "[$-409]m/d/yyyy", 20);

        result.Text.Should().Be(new string('#', 20));
    }

    [Fact]
    public void NegativeValue_WithColorBracketPrefixedDateFormat_FillsInvalidIndicatorToRequestedWidth()
    {
        // Pre-fix: returned only 8 '#' characters (the literal length of "[Red]m/d/yyyy"),
        // ignoring the requested column width of 20.
        var result = NumberFormatter.FormatWithColor(new NumberValue(-1), "[Red]m/d/yyyy", 20);

        result.Text.Should().Be(new string('#', 20));
    }

    [Fact]
    public void NegativeValue_WithPlainDateFormat_StillFillsInvalidIndicatorToRequestedWidth()
    {
        // Sanity/regression guard: the non-bracketed fast path already handled this correctly
        // before this fix and must be unaffected.
        var result = NumberFormatter.FormatWithColor(new NumberValue(-1), "m/d/yyyy", 20);

        result.Text.Should().Be(new string('#', 20));
    }

    [Fact]
    public void NegativeValue_WithElapsedTimeBracketFormat_IsUnaffectedByWidthThreading()
    {
        // Sanity/regression guard: elapsed-time brackets ("[h]:mm:ss") are a distinct duration
        // concept handled entirely before the date/time invalid-value branch and must keep
        // rendering a real negative duration, not an all-hash indicator, regardless of the
        // requested column width.
        var result = NumberFormatter.FormatWithColor(new NumberValue(-1), "[h]:mm:ss", 20);

        result.Text.Should().Be("-24:00:00");
    }

    [Fact]
    public void NegativeValue_WithBracketPrefixedDateFormat_NoWidthSupplied_FallsBackToFormatLength()
    {
        // Sanity/regression guard: when no real column width is available (targetWidthCharacters
        // null), the format-length fallback in BuildInvalidDateTimeIndicator is unchanged --
        // by the time this fallback runs, the leading bracket directive has already been
        // stripped from the working format string, so the fallback length is that of the
        // stripped "m/d/yyyy" (8), not the original "[$-409]m/d/yyyy" (15).
        var result = NumberFormatter.Format(new NumberValue(-1), "[$-409]m/d/yyyy");

        result.Should().Be(new string('#', "m/d/yyyy".Length));
    }
}
