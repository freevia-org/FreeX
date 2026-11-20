using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Free.Shared.Pdf.Skia;

namespace Free.Shared.Pdf.Tests;

public sealed class SkiaPdfCatalogViewerTests
{
    [Fact]
    public void AppendUpdate_ReplacesExistingLanguageAndPreservesOriginalDocumentAndTrailerEntries()
    {
        var document = new PdfContentDocument(
            [new PdfContentPage(100, 100, [])],
            new PdfDocumentProperties(Title: "Example") { Language = "en-US" });
        using var stream = new MemoryStream();
        PortablePdfWriter.Write(document, stream);
        stream.Write(" \t\r\n"u8);
        var original = stream.ToArray();
        var originalText = Encoding.ASCII.GetString(original);
        var xrefOffset = originalText.IndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var infoStart = originalText.IndexOf("/Info ", StringComparison.Ordinal);
        var infoReference = originalText[infoStart..originalText.IndexOf(" >>", infoStart, StringComparison.Ordinal)];

        SkiaPdfCatalog.AppendUpdate(stream, new PdfDocumentProperties { Language = "fr-CA" }, pageCount: 1);

        var updated = stream.ToArray();
        updated[..original.Length].Should().Equal(original);
        var revision = Encoding.ASCII.GetString(updated[original.Length..]);
        revision.Should().StartWith("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Lang <FEFF00660072002D00430041> >>");
        revision.Should().NotContain("(en-US)");
        revision.Should().Contain(infoReference);
        revision.Should().Contain($"/Prev {xrefOffset.ToString(CultureInfo.InvariantCulture)}");
    }

    [Fact]
    public void Write_SetsViewerPreferencesAndLinksOutlinesToTheActualNestedPageTree()
    {
        var pages = Enumerable.Range(0, 12)
            .Select(index => new PdfContentPage(100 + index, 80, []))
            .ToArray();
        pages[0] = pages[0] with
        {
            LinkOverlays = [new PdfLinkOverlay(10, 10, 40, 20, null, DestinationName: "ExistingTarget")],
        };
        pages[11] = pages[11] with
        {
            NamedDestinations = [new PdfNamedDestination("ExistingTarget", 12, 20)],
        };
        var properties = new PdfDocumentProperties(Title: "Interactive workbook")
        {
            Language = "fr-CA",
            ViewerSettings = new(PdfViewerPageLayout.TwoColumnRight, PdfViewerOpenMode.Normal, DisplayDocumentTitle: true),
            Outlines = [
                new PdfOutlineEntry("  Résumé Ω  ", 10),
                new PdfOutlineEntry("First page", 0),
                new PdfOutlineEntry(" ", 1),
                new PdfOutlineEntry("Missing page", 12)
            ],
        };

        var pdf = Encoding.Latin1.GetString(SkiaPdfWriter.WriteToBytes(new PdfContentDocument(pages, properties)));

        var trailer = ReadLatestTrailer(pdf);
        var catalogNumber = ReadReference(trailer, "/Root");
        var catalog = ReadLatestObject(pdf, catalogNumber);
        catalog.Should().Contain("/Version /1.7").And.Contain("/PageLayout /TwoColumnRight")
            .And.Contain("/PageMode /UseOutlines").And.Contain("/Lang <FEFF00660072002D00430041>")
            .And.Contain("/PrintScaling /None").And.Contain("/FitWindow true").And.Contain("/CenterWindow true")
            .And.Contain("/PickTrayByPDFSize true").And.Contain("/DisplayDocTitle true")
            .And.Contain("/NonFullScreenPageMode /UseOutlines");

        var outlineRootNumber = ReadReference(catalog, "/Outlines");
        var outlineRoot = ReadLatestObject(pdf, outlineRootNumber);
        outlineRoot.Should().Contain("/Type /Outlines").And.Contain("/Count 2");
        var firstNumber = ReadReference(outlineRoot, "/First");
        var lastNumber = ReadReference(outlineRoot, "/Last");
        var first = ReadLatestObject(pdf, firstNumber);
        var last = ReadLatestObject(pdf, lastNumber);
        ReadTitle(first).Should().Be("Résumé Ω");
        ReadTitle(last).Should().Be("First page");
        ReadReference(first, "/Parent").Should().Be(outlineRootNumber);
        ReadReference(last, "/Parent").Should().Be(outlineRootNumber);
        ReadReference(first, "/Next").Should().Be(lastNumber);
        ReadReference(last, "/Prev").Should().Be(firstNumber);
        first.Should().NotContain("/Prev");
        last.Should().NotContain("/Next");
        ReadLatestObject(pdf, ReadDestination(first)).Should().Contain("/MediaBox [0 0 110 80]");
        ReadLatestObject(pdf, ReadDestination(last)).Should().Contain("/MediaBox [0 0 100 80]");
        foreach (var number in new[] { catalogNumber, outlineRootNumber, firstNumber, lastNumber })
            AssertUpdatedCrossReferenceOffset(pdf, number);

        var destinations = ReadLatestObject(pdf, ReadReference(catalog, "/Dests"));
        destinations.Should().Contain("/ExistingTarget");
        pdf.Should().Contain("/Subtype /Link").And.Contain("ExistingTarget");
        ReadLatestObject(pdf, ReadReference(trailer, "/Info")).Should().Contain("/Title (Interactive workbook)");
    }

    [Fact]
    public void Write_SetsFullscreenEvenWithoutLanguageOrOutlines()
    {
        var document = new PdfContentDocument(
            [new PdfContentPage(100, 80, [])],
            new PdfDocumentProperties
            {
                ViewerSettings = new(PdfViewerPageLayout.OneColumn, PdfViewerOpenMode.FullScreen),
            });

        var pdf = Encoding.Latin1.GetString(SkiaPdfWriter.WriteToBytes(document));
        var catalog = ReadLatestObject(pdf, ReadReference(ReadLatestTrailer(pdf), "/Root"));

        catalog.Should().Contain("/PageLayout /OneColumn").And.Contain("/PageMode /FullScreen");
        catalog.Should().NotContain("/Lang").And.NotContain("/Outlines").And.NotContain("/NonFullScreenPageMode");
    }

    [Fact]
    public void AppendUpdate_ReplacesExistingViewerFieldsRatherThanDuplicatingThem()
    {
        var original = new PdfContentDocument(
            [new PdfContentPage(100, 80, [])],
            new PdfDocumentProperties
            {
                ViewerSettings = new(PdfViewerPageLayout.OneColumn, PdfViewerOpenMode.Normal),
            });
        using var stream = new MemoryStream();
        PortablePdfWriter.Write(original, stream);

        SkiaPdfCatalog.AppendUpdate(stream, new PdfDocumentProperties
        {
            ViewerSettings = new(PdfViewerPageLayout.TwoColumnLeft, PdfViewerOpenMode.FullScreen),
        }, pageCount: 1);

        var pdf = Encoding.Latin1.GetString(stream.ToArray());
        var catalog = ReadLatestObject(pdf, ReadReference(ReadLatestTrailer(pdf), "/Root"));
        catalog.Should().Contain("/PageLayout /TwoColumnLeft").And.Contain("/PageMode /FullScreen")
            .And.NotContain("/OneColumn").And.NotContain("/UseNone");
        foreach (var field in new[] { "/Version", "/PageLayout", "/PageMode", "/ViewerPreferences" })
            Regex.Matches(catalog, Regex.Escape(field)).Should().ContainSingle();
    }

    private static string ReadLatestTrailer(string pdf) => Regex.Matches(
        pdf, @"\ntrailer\n(?<trailer><<.*?)>>\nstartxref\n", RegexOptions.Singleline).Last().Groups["trailer"].Value;

    private static int ReadReference(string dictionary, string key) =>
        ParseMatchNumber(Regex.Match(dictionary, $@"{Regex.Escape(key)}\s+(?<number>\d+) 0 R"));

    private static int ReadDestination(string item) =>
        ParseMatchNumber(Regex.Match(item, @"/Dest \[(?<number>\d+) 0 R /XYZ null null null\]"));

    private static int ParseMatchNumber(Match match)
    {
        match.Success.Should().BeTrue();
        return int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
    }

    private static string ReadLatestObject(string pdf, int number) =>
        Regex.Matches(pdf, FormattableString.Invariant($@"(?m)^{number} 0 obj\n(?<object>[\s\S]*?)\nendobj\n"))
            .Last().Groups["object"].Value;

    private static string ReadTitle(string item)
    {
        var hex = Regex.Match(item, @"/Title <FEFF(?<text>[0-9A-F]+)>").Groups["text"].Value;
        return Encoding.BigEndianUnicode.GetString(Convert.FromHexString(hex));
    }

    private static void AssertUpdatedCrossReferenceOffset(string pdf, int objectNumber)
    {
        var xrefOffset = ParseMatchNumber(Regex.Matches(pdf, @"\nstartxref\n(?<number>\d+)\n%%EOF").Last());
        var objectOffset = pdf.LastIndexOf(FormattableString.Invariant($"\n{objectNumber} 0 obj\n"), StringComparison.Ordinal) + 1;
        objectOffset.Should().BeGreaterThan(0);
        pdf[xrefOffset..].Should().Contain(FormattableString.Invariant($"\n{objectOffset:D10} 00000 n \n"));
    }
}
