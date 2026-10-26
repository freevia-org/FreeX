using System.Globalization;
using FluentAssertions;
using FreeW.App.Presentation.Ribbon;
using Xunit;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r604: a number the USER TYPES must be read in the user's own locale.
///
/// <para>r603 fixed the opposite direction -- numbers read out of a FILE must be culture-invariant.
/// The inverse defect is here, in the ribbon boxes: <c>TryParseNonNegativePoints</c> parses indent
/// and spacing entries with <see cref="CultureInfo.InvariantCulture"/> only, so a user on any
/// comma-decimal machine who types <c>1,5</c> gets nothing -- the command returns false and silently
/// does not apply. Word accepts the locale separator in these boxes, and FreeX already settled the
/// shape in <c>CellEntryParser</c>: current culture first, invariant as a GUARDED fallback that
/// declines to reinterpret text containing the current culture's own separator.</para>
/// </summary>
public sealed class R604_TypedNumbersFollowTheUsersLocaleTests
{
    private static T UnderCulture<T>(string culture, Func<T> body)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("de-DE", "1,5", 1.5)]
    [InlineData("fr-FR", "1,5", 1.5)]
    [InlineData("de-DE", "18", 18.0)]
    [InlineData("en-US", "1.5", 1.5)]
    // These two are why the styles are NumberStyles.Float and not NumberStyles.Any. '.' is de-DE's
    // THOUSANDS separator, so under AllowThousands "1.5" would read as FIFTEEN -- correct for a
    // spreadsheet cell (FreeX's CellEntryParser allows thousands, matching Excel) and wrong for a
    // measurement box, where nobody types a grouped indent. Excluding AllowThousands makes the
    // current-culture parse FAIL here, and the invariant fallback then reads the one-and-a-half the
    // user plainly meant. FreeP's animation-duration field settled the same question the same way.
    [InlineData("de-DE", "1.5", 1.5)]
    // fa-IR reaches the fallback for a different reason: its separators are U+066B and U+066C, so
    // '.' is neither decimal nor group and the current culture cannot read "1.5" at all.
    [InlineData("fa-IR", "1.5", 1.5)]
    public void ATypedMeasurementIsReadInTheUsersLocale(string culture, string typed, double expected)
    {
        var parsed = UnderCulture(culture, () =>
            FreeWRibbonFormattingSession.TryParseNonNegativePoints(typed, out var points)
                ? points
                : double.NaN);

        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void RubbishIsStillRejected(string culture)
    {
        UnderCulture(culture, () =>
            FreeWRibbonFormattingSession.TryParseNonNegativePoints("not a number", out _))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void NonFiniteAndNegativeAreStillRejected(string culture)
    {
        // r571/r574 pinned these; the locale change must not reopen them.
        foreach (var hostile in new[] { "Infinity", "-Infinity", "NaN", "-1", "1e400" })
        {
            UnderCulture(culture, () =>
                FreeWRibbonFormattingSession.TryParseNonNegativePoints(hostile, out _))
                .Should().BeFalse($"'{hostile}' must not be accepted as a measurement");
        }
    }

    /// <summary>
    /// r604: the box must be able to read back what the box itself printed.
    ///
    /// <para>Both hosts render the font size and line spacing through FormatInvariant, so the box
    /// always DISPLAYS "10.5". The WPF host then re-read that text under the user culture with
    /// AllowThousands, and on a de-DE machine "10.5" parses as 105: opening the box and pressing
    /// Enter without editing anything multiplied the font size by ten. A source contract was
    /// asserting that exact configuration, so nothing reported it.</para>
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void TheValueTheBoxPrintsIsTheValueTheBoxReadsBack(string culture)
    {
        foreach (var original in new[] { 10.5, 1.5, 12.75, 18.0, 0.5 })
        {
            var displayed = FreeWRibbonNumericValueParser.FormatInvariant(original);

            var roundTripped = UnderCulture(culture, () =>
                FreeWRibbonNumericValueParser.TryParseTypedFontSize(displayed, out var points)
                    ? points
                    : double.NaN);

            roundTripped.Should().Be(
                original,
                $"the box displays {displayed} under {culture} and must read it back unchanged");
        }
    }

}