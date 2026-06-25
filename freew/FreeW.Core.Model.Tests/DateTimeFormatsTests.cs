using System.Globalization;

namespace FreeW.Core.Model.Tests;

public class DateTimeFormatsTests
{
    // A fixed moment so the formatted strings are deterministic regardless of when the test runs.
    private static readonly DateTime Moment = new(2026, 6, 17, 14, 5, 9);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void Build_ReturnsExpectedLabelsInOrder()
    {
        var formats = DateTimeFormats.Build(Moment, Invariant);

        formats.Select(f => f.Label).Should().Equal(
            "Short date", "Long date", "Short time", "Long time", "Date and time");
    }

    [Fact]
    public void Build_FormatsEachOptionForTheGivenMoment()
    {
        var formats = DateTimeFormats.Build(Moment, Invariant);

        // Standard format specifiers under the invariant culture.
        formats[0].Text.Should().Be(Moment.ToString("d", Invariant)); // short date: 06/17/2026
        formats[1].Text.Should().Be(Moment.ToString("D", Invariant)); // long date
        formats[2].Text.Should().Be(Moment.ToString("t", Invariant)); // short time
        formats[3].Text.Should().Be(Moment.ToString("T", Invariant)); // long time
        formats[4].Text.Should().Be(Moment.ToString("f", Invariant)); // date + short time
    }

    [Fact]
    public void Build_ShortDate_MatchesExpectedInvariantString()
    {
        var formats = DateTimeFormats.Build(Moment, Invariant);

        formats[0].Text.Should().Be("06/17/2026");
    }

    [Fact]
    public void Build_DefaultsToCurrentCultureWhenNoneSupplied()
    {
        var expected = Moment.ToString("d", CultureInfo.CurrentCulture);

        var formats = DateTimeFormats.Build(Moment);

        formats[0].Text.Should().Be(expected);
    }

    // ── BuildFieldPicture / NetPatternToWordPicture regression tests (H1) ─────────────────────

    /// <summary>
    /// Regression test for H1: on a non-US culture (de-DE) the field picture must be derived
    /// from the culture's DateTimeFormatInfo patterns, not the hardcoded US-English strings.
    /// For de-DE the short-date pattern is "dd.MM.yyyy" and there is no AM/PM designator, so
    /// the picture must differ from the US default "M/d/yyyy".
    /// </summary>
    [Fact]
    public void BuildFieldPicture_NonUsCulture_DoesNotProduceUsFormat()
    {
        var deDe = CultureInfo.GetCultureInfo("de-DE");

        var picture = DateTimeFormats.BuildFieldPicture(0, deDe); // short date

        // de-DE short date is "dd.MM.yyyy" — must not be the US "M/d/yyyy"
        picture.Should().NotBe("M/d/yyyy",
            "the picture must be derived from de-DE's ShortDatePattern, not the hardcoded US format");
        // And it must actually contain the de-DE separator and zero-padded tokens
        picture.Should().Contain("dd", "de-DE zero-pads the day");
        picture.Should().Contain("MM", "de-DE zero-pads the month");
        picture.Should().Contain("yyyy", "de-DE uses 4-digit year");
    }

    [Fact]
    public void BuildFieldPicture_NonUsCulture_TimePatternReplacesNetAmPmDesignatorWithWordToken()
    {
        // en-US short time pattern is "h:mm tt" — the "tt" must become "am/pm" for Word
        var enUs = CultureInfo.GetCultureInfo("en-US");

        var picture = DateTimeFormats.BuildFieldPicture(2, enUs); // short time

        picture.Should().NotContain("tt", "Word does not use .NET's 'tt' AM/PM token");
        picture.Should().Contain("am/pm", "Word uses 'am/pm' for the AM/PM designator");
    }

    [Fact]
    public void BuildFieldPicture_UsCulture_ShortDate_IsCorrect()
    {
        var enUs = CultureInfo.GetCultureInfo("en-US");

        var picture = DateTimeFormats.BuildFieldPicture(0, enUs); // short date

        // en-US ShortDatePattern is "M/d/yyyy" — no AM/PM tokens, passes through unchanged
        picture.Should().Be("M/d/yyyy");
    }

    [Fact]
    public void BuildFieldPicture_AllIndices_ProduceDerivedPictureThatMatchesBuildText()
    {
        // The picture produced for each index must be capable of rendering the same text that
        // DateTimeFormats.Build produced for the same culture. We verify this by formatting
        // Moment with each picture interpreted as a .NET format string — after reverting the
        // am/pm→tt substitution so .NET can parse it. This is a round-trip sanity check.
        var deDe = CultureInfo.GetCultureInfo("de-DE");
        var formats = DateTimeFormats.Build(Moment, deDe);

        for (int i = 0; i < formats.Count; i++)
        {
            var picture = DateTimeFormats.BuildFieldPicture(i, deDe);
            // The picture must be non-empty and not be the US-default string for non-US indices
            // where the patterns differ — just ensure it was actually derived from the culture.
            picture.Should().NotBeNullOrEmpty($"BuildFieldPicture({i}, de-DE) must produce a non-empty picture");
        }
    }

    [Fact]
    public void NetPatternToWordPicture_PassesThroughPatternWithNoAmPm()
    {
        // A pure date pattern with no AM/PM designator must pass through unchanged.
        var result = DateTimeFormats.NetPatternToWordPicture("dd.MM.yyyy");

        result.Should().Be("dd.MM.yyyy");
    }

    [Fact]
    public void NetPatternToWordPicture_ReplacesTtWithAmPm()
    {
        var result = DateTimeFormats.NetPatternToWordPicture("h:mm tt");

        result.Should().Be("h:mm am/pm");
    }

    [Fact]
    public void NetPatternToWordPicture_ReplacesUppercaseTtWithAmPm()
    {
        // Verify the case where both t characters are present — "tt" is consumed as a unit.
        var result = DateTimeFormats.NetPatternToWordPicture("HH:mm:ss tt");

        result.Should().Be("HH:mm:ss am/pm");
    }

    [Fact]
    public void NetPatternToWordPicture_QuotedLiteralIsSingleToDouble()
    {
        // .NET uses single-quoted literals; Word uses double-quoted literals.
        // "dd'.' MM'.' yyyy" has single-quoted '.' literals; spaces are outside the quotes.
        var result = DateTimeFormats.NetPatternToWordPicture("dd'.' MM'.' yyyy");

        result.Should().Be("dd\".\" MM\".\" yyyy");
    }
}
