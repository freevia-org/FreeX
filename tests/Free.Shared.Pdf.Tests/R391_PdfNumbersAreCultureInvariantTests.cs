using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// r391: every number the PDF writer emits must be culture-invariant.
///
/// <para>PDF is float-dense -- coordinates, widths, colour components, alpha, transformation
/// matrices -- and its syntax accepts only <c>.</c> as the decimal point. A single number formatted
/// under a German or French locale produces a structurally corrupt file, and it would corrupt it
/// only for the users running that locale, on machines the developer does not have. Save-as-PDF is
/// table stakes for all three apps, so this writer is shared by every one of them.</para>
///
/// <para>The writer is right today: every value funnels through one <c>FormatNumber</c> helper that
/// passes <see cref="CultureInfo.InvariantCulture"/>. That is exactly the shape a later edit erodes
/// -- one <c>$"{value}"</c> interpolation added next to the helper rather than through it, silently
/// correct on the author's machine. This pins the OUTPUT rather than the helper, so any bypass fails
/// regardless of how it is written.</para>
///
/// <para>Each case self-checks that the culture actually took effect before trusting a pass; an
/// assertion that cannot fail is worse than no assertion, and this suite has spent several rounds
/// removing that pattern.</para>
/// </summary>
public sealed class R391_PdfNumbersAreCultureInvariantTests
{
    // Locales whose number formats break PDF differently: comma decimal separator (de), comma plus
    // narrow-space grouping (fr), and a comma decimal with non-ASCII casing rules (tr).
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void EveryEmittedNumberUsesAnInvariantDecimalPoint(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);

        try
        {
            3.14.ToString().Should().Be(
                "3,14",
                "the probe must prove the culture took effect on this thread, or a pass below means " +
                "nothing");

            // Values chosen to round-trip through FormatNumber's "0.###" with a visible fraction:
            // stroke width, fractional coordinates, and a colour component (255 -> 0.substring).
            var page = new PdfContentPage(612.5, 792.25, new PdfDrawOp[]
            {
                new PdfFillRect(36.75, 700.5, 100.125, 22.875, new PdfColor(238, 242, 247)),
                new PdfStrokeRect(36.5, 700.25, 100.5, 22.5, new PdfColor(196, 202, 210), 0.5),
                new PdfText(40.125, 706.375, 12.5, PdfFontFace.Bold, PdfColor.Black, "Hello"),
            });

            var bytes = PortablePdfWriter.WriteToBytes(new PdfContentDocument(new[] { page }));
            var pdf = Encoding.ASCII.GetString(bytes);

            // A digit-comma-digit sequence cannot occur in well-formed PDF syntax outside string
            // literals, and none of the drawing text above contains one.
            var offenders = Regex.Matches(pdf, @"\d,\d+")
                .Select(match => match.Value)
                .Distinct()
                .ToList();

            offenders.Should().BeEmpty(
                "a comma decimal separator makes the PDF structurally invalid for readers, and it " +
                "would only ever reproduce on machines running this locale. Offenders: " +
                string.Join(", ", offenders));

            // Positive control: the fractional values really did reach the wire, so the negative
            // assertion above was applied to output that could have carried the defect.
            pdf.Should().Contain("0.5 w", "the fractional stroke width must be written invariantly");
            pdf.Should().Contain("100.125", "fractional coordinates must reach the content stream");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
