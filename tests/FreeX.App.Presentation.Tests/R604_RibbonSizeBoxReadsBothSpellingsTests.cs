using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation.SheetUI;
using Xunit;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// r604: the ribbon font-size box, in both spellings and on both hosts.
///
/// <para>The WPF host parsed it with the current culture only and the Avalonia host with the
/// invariant culture only, so the same box rejected opposite halves of the world depending on the
/// platform. Worse, the current-culture parse allowed thousands: '.' is de-DE's group separator, so
/// a box showing "10.5" read back as 105 and pressing Enter without editing multiplied the size by
/// ten.</para>
///
/// <para>FreeX had already settled this shape three times over -- NumericInputParser's own
/// parameterless overloads, ChartDialogValueParser and FormatCellsInputParser all try the current
/// culture then the invariant one -- so this is the ribbon path catching up, not a new policy.</para>
/// </summary>
public sealed class R604_RibbonSizeBoxReadsBothSpellingsTests
{
    private static double ParseUnder(string culture, string text)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            return WorksheetSizeInputParser.TryParsePositiveSize(text, out var size) ? size : double.NaN;
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("de-DE", "10,5", 10.5)]
    [InlineData("fr-FR", "10,5", 10.5)]
    [InlineData("en-US", "10.5", 10.5)]
    [InlineData("de-DE", "11", 11.0)]
    // The one that used to become 105.
    [InlineData("de-DE", "10.5", 10.5)]
    [InlineData("fr-FR", "10.5", 10.5)]
    [InlineData("fa-IR", "10.5", 10.5)]
    public void BothSpellingsOfASizeAreAccepted(string culture, string typed, double expected) =>
        ParseUnder(culture, typed).Should().Be(expected);

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void HostileValuesAreStillRejected(string culture)
    {
        foreach (var hostile in new[] { "Infinity", "NaN", "0", "-3", "1e400", "not a size" })
            ParseUnder(culture, hostile).Should().Be(double.NaN, $"'{hostile}' is not a usable size");
    }
}
