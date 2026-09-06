using System.IO.Compression;
using FluentAssertions;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Skia;
using Free.Shared.Xps;
using Xunit;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// r469: the XPS sibling of r468 -- the same non-finite coordinate defect, in the other writer of
/// the same shared assembly, reached by propagating a fix rather than waiting for a second report.
///
/// <para>A NaN or Infinity was formatted straight into the markup, producing a FixedPage with
/// <c>Width="NaN"</c> and paths reading <c>M NaN,NaN L ...</c>. Neither is a number in the
/// abbreviated geometry syntax, so the result is a well-formed OPC package wrapping an unparseable
/// page -- a file that looks entirely healthy and opens in nothing.</para>
///
/// <para>The writer already has an exportability gate (<see cref="XpsExportabilityReport"/> ->
/// <see cref="XpsUnsupportedContentException"/>) which declared these documents exportable, so the
/// obvious fix was to refuse through it. That would have been wrong, and the test below pins why:
/// the only caller answers that exception by rasterising the SAME document through Skia, which
/// accepts non-finite geometry and returns a page. Refusing in the writer's own idiom would have
/// converted a broken coordinate into a silently blank page -- trading one invisible failure for
/// another.</para>
/// </summary>
public sealed class R469_XpsNonFiniteCoordinatesAreRefusedTests
{
    private static readonly PdfColor Black = new(0, 0, 0);

    private static PdfContentDocument DocumentWith(PdfDrawOp op) =>
        new([new PdfContentPage(612, 792, [op])]);

    private static string PageMarkup(byte[] xps)
    {
        using var archive = new ZipArchive(new MemoryStream(xps), ZipArchiveMode.Read);
        var page = archive.Entries.First(e => e.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(page.Open());
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData(double.NaN, 10, 10, 10)]
    [InlineData(10, double.NaN, 10, 10)]
    [InlineData(10, 10, double.NaN, 10)]
    [InlineData(10, 10, 10, double.NaN)]
    [InlineData(double.PositiveInfinity, 10, 10, 10)]
    [InlineData(10, double.NegativeInfinity, 10, 10)]
    public void ANonFiniteCoordinateRefusesTheExport(double x, double y, double width, double height)
    {
        var export = () => PortableXpsWriter.WriteToBytes(DocumentWith(new PdfFillRect(x, y, width, height, Black)));

        export.Should().Throw<InvalidOperationException>().WithMessage("*non-finite*");
    }

    [Fact]
    public void ANonFinitePageSizeRefusesTheExport()
    {
        // Page dimensions reach the same formatter through the FixedPage attributes; asserted
        // rather than assumed, because they do not travel through any draw operation.
        var export = () => PortableXpsWriter.WriteToBytes(
            new PdfContentDocument([new PdfContentPage(double.NaN, 792, [])]));

        export.Should().Throw<InvalidOperationException>().WithMessage("*non-finite*");
    }

    [Fact]
    public void TheRefusalIsNotTheTypeThatTriggersTheRasterFallback()
    {
        // The load-bearing assertion. FreeWAvaloniaXpsExport catches XpsUnsupportedContentException
        // and retries by rasterising, so raising that type here would hide the defect instead of
        // reporting it. XpsUnsupportedContentException derives from InvalidOperationException, so
        // the assertion above would pass either way -- this is what actually pins the choice.
        var export = () => PortableXpsWriter.WriteToBytes(
            DocumentWith(new PdfFillRect(double.NaN, 10, 10, 10, Black)));

        export.Should().Throw<InvalidOperationException>()
            .And.Should().NotBeOfType<XpsUnsupportedContentException>(
                "the caller answers that exception by rasterising the same document, which would " +
                "turn an invalid coordinate into a silently blank page");
    }

    [Fact]
    public void TheRasterFallbackReallyDoesAcceptANonFiniteDocument()
    {
        // The measurement behind the choice above, kept executable so it cannot rot into folklore.
        // If Skia ever starts rejecting non-finite geometry, this fails and the reasoning in
        // TheRefusalIsNotTheTypeThatTriggersTheRasterFallback can be revisited on evidence.
        var pages = SkiaPdfWriter.RenderPagesToPng(
            DocumentWith(new PdfFillRect(double.NaN, double.NaN, 10, 10, Black)));

        pages.Should().HaveCount(1);
        pages[0].Should().NotBeEmpty("Skia renders the page regardless, which is precisely the danger");
    }

    [Fact]
    public void OrdinaryContentStillExportsAndItsMarkupIsClean()
    {
        // The control, and the narrowness check: a normal export must be undisturbed. A control is
        // kept because the first detector for the PDF sibling reported a false hit on one.
        var bytes = PortableXpsWriter.WriteToBytes(DocumentWith(new PdfFillRect(10, 10, 100, 50, Black)));

        var markup = PageMarkup(bytes);
        markup.Should().Contain("Width=\"612\"").And.Contain("Height=\"792\"");
        markup.Should().NotContain("NaN");
        markup.Should().NotContain("Infinity");
    }

    [Fact]
    public void AVeryLargeButFiniteCoordinateIsStillAccepted()
    {
        // Only NON-finite values are refused; a large finite coordinate is unusual but representable.
        var export = () => PortableXpsWriter.WriteToBytes(DocumentWith(new PdfFillRect(1e6, 10, 10, 10, Black)));

        export.Should().NotThrow();
    }
}
