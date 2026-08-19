using FluentAssertions;
using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r151 remediation for the scope-audit gap on the meta-lens finding.
///
/// Round 151 taught <c>ExcelTextNumberParser</c> that a culture whose group separator is itself a
/// whitespace character (fr-FR's U+202F, fi-FI's U+00A0) has to accept the whitespace variants real
/// text actually carries -- an ordinary U+0020 from a keyboard, or a plain U+00A0 from another
/// application. The audit then found a second, hand-written copy of the same grouping rule in
/// <c>PasteCommandFactory.TryParseCultureGroupedNumber</c>, reached by an ordinary Ctrl+V, which
/// still recognised only the one exact code point. That is precisely the class of defect this
/// round was sweeping for, so the paste path now SHARES the parser's normaliser rather than
/// restating it.
///
/// The symptom differed from the formula path and was quieter: the paste path falls through to
/// <see cref="TextValue"/> on failure, so a French user pasting "1\u202F234,56" got a left-aligned
/// string that no longer sums, with no error to explain it.
///
/// The theory separators are written as \u escapes on purpose: the whole defect is about distinct
/// whitespace code points, and typing them literally would let a later edit silently collapse the
/// three cases into one. (The malformed-grouping case below does carry a literal U+202F inside its
/// string, which is why it says so here rather than looking like an ordinary space.) The
/// formula-side half of this rule is covered separately by
/// R151_TextNumberParserSpaceGroupingAndCurrencyTests.
/// </summary>
public sealed class R151_ClipboardPasteCultureGroupedNumberTests
{
    [Theory]
    [InlineData('\u0020')] // an ordinary space -- what a keyboard produces
    [InlineData('\u00A0')] // a plain non-breaking space -- what other apps and web pages emit
    [InlineData('\u202F')] // the narrow no-break space fr-FR's CultureInfo actually reports
    public void ParseClipboardValue_FrFr_AcceptsEveryWhitespaceGroupingVariant(char separator)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var pasted = "1" + separator + "234,56";
            var value = PasteCommandFactory.ParseClipboardValue(pasted);

            value.Should().BeOfType<NumberValue>(
                "Ctrl+V of a French-grouped number must land as a number whichever whitespace the "
                + "source application used, not as text that no longer sums");
            ((NumberValue)value).Value.Should().BeApproximately(1234.56, 1e-9);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData('\u0020')]
    [InlineData('\u00A0')]
    [InlineData('\u202F')]
    public void ParseClipboardValue_FrFr_ProducesTheSameValueForEveryVariant(char separator)
    {
        // The three spellings are the same number to a user, so they must agree with each other --
        // asserted as agreement rather than three separate literals, because the defect was the
        // variants being treated differently.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var canonical = PasteCommandFactory.ParseClipboardValue("1\u202F234,56");
            var variant = PasteCommandFactory.ParseClipboardValue("1" + separator + "234,56");

            canonical.Should().BeOfType<NumberValue>();
            variant.Should().BeOfType<NumberValue>();
            ((NumberValue)variant).Value.Should().Be(((NumberValue)canonical).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("en-US", "1,234.56", 1234.56)]
    [InlineData("de-DE", "1.234,56", 1234.56)]
    public void ParseClipboardValue_NonSpaceSeparatorCultures_AreUnaffected(
        string cultureName, string pasted, double expected)
    {
        // Sibling no-regression: the normaliser must be a no-op wherever the group separator is not
        // whitespace, so en-US and de-DE keep parsing exactly as before.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            var value = PasteCommandFactory.ParseClipboardValue(pasted);

            value.Should().BeOfType<NumberValue>();
            ((NumberValue)value).Value.Should().BeApproximately(expected, 1e-9);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ParseClipboardValue_FrFr_StillRejectsMalformedGroupingAsText()
    {
        // The normaliser must not turn badly grouped text into a number: "1\u202F23,4" is not a French
        // number, and Excel keeps it as text rather than silently misreading it.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var malformed = "1 23,4";
            PasteCommandFactory.ParseClipboardValue(malformed).Should().BeOfType<TextValue>();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
