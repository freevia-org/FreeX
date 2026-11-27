using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Xunit.Abstractions;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewPdfListMarkerTests
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);
    private const double DipPerPoint = 96.0 / 72;
    private static readonly RunFormatting Format = RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 14 };

    public DocumentViewPdfListMarkerTests(ITestOutputHelper output)
    {
        var assembly = typeof(DocumentView).Assembly;
        output.WriteLine($"Loaded application: {assembly.Location}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))}; Version={assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DecimalRestartExportsOneAndTenWithTheirLiveHangingPositions(bool shaped) => Run(() =>
    {
        var document = Document();
        document.Blocks.Add(Item(Text(shaped), ListKind.Number));
        var tenth = Item(Text(shaped), ListKind.Number);
        tenth.Formatting = tenth.Formatting with { ListStartOverride = 10 };
        document.Blocks.Add(tenth);
        var view = Layout(document);
        view.RenderedMarkerPositionsForTest.Select(marker => marker.Text).Should().Equal("1.", "10.");
        AssertExportedMarkers(view, document, Format);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BulletsRemainSeparateFromTheAuthoredParagraphText(bool shaped) => Run(() =>
    {
        var document = Document(); var paragraph = Item(Text(shaped), ListKind.Bullet);
        var source = paragraph.Runs.Single(); document.Blocks.Add(paragraph);
        var view = Layout(document);
        view.RenderedMarkerPositionsForTest.Select(marker => marker.Text).Should().Equal("•");
        AssertExportedMarkers(view, document, Format);
        paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(source);
        paragraph.PlainText.Should().Be(Text(shaped));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NativeRomanAndMultilevelMarkersUseTheResolvedLiveStrings(bool shaped) => Run(() =>
    {
        var document = Document();
        document.MultiLevelList.SetNumberFormat(0, ListNumberFormat.UpperRoman);
        document.MultiLevelList.SetNumberFormat(1, ListNumberFormat.LowerLetter);
        document.Blocks.Add(Item(Text(shaped), ListKind.MultiLevel));
        document.Blocks.Add(Item(Text(shaped), ListKind.MultiLevel));
        document.Blocks.Add(new Paragraph("Separator"));
        document.Blocks.Add(Item(Text(shaped), ListKind.MultiLevel));
        var child = Item(Text(shaped), ListKind.MultiLevel); child.Formatting = child.Formatting with { ListLevel = 1 };
        document.Blocks.Add(child);
        var view = Layout(document);
        view.RenderedMarkerPositionsForTest.Select(marker => marker.Text).Should().Equal("I.", "II.", "III.", "III.a.");
        AssertExportedMarkers(view, document, Format);
    });

    [Fact]
    public Task PreservedRomanBodyAndTableMarkersAreExportedExactlyOnce() => Run(() =>
    {
        var document = Document();
        document.Preserved.OriginalNumbering = XElement.Parse("""
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="4"/>
                <w:numFmt w:val="upperRoman"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
              <w:num w:numId="7"><w:abstractNumId w:val="1"/></w:num>
            </w:numbering>
            """);
        Paragraph Preserved(string text) { var p = Item(text, ListKind.None); p.PreservedNumbering = new PreservedNumbering(7, 0); return p; }
        document.Blocks.Add(Preserved("Body"));
        var cell = new TableCell(); cell.Paragraphs.Clear(); cell.Paragraphs.Add(Preserved("Cell"));
        var row = new TableRow(); row.Cells.Add(cell);
        var table = new Table(); table.Rows.Add(row); table.ColumnWidthsPt.Add(180); document.Blocks.Add(table);
        document.Blocks.Add(Preserved("After"));
        var view = Layout(document);
        view.RenderedMarkerPositionsForTest.Select(marker => marker.Text).Should().Equal("IV.", "V.", "VI.");
        AssertExportedMarkers(view, document, Format);
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public Task MarkerFontFamilyPointSizeColourAndAllFacesFollowLiveFormatting(bool shaped, bool bold, bool italic) => Run(() =>
    {
        var format = Format with { FontSizePt = 19, ColorHex = "#9B2226", Bold = bold, Italic = italic };
        var document = Document(); var paragraph = Item(Text(shaped), ListKind.Number);
        paragraph.Runs.Single().Formatting = format; document.Blocks.Add(paragraph);
        AssertExportedMarkers(Layout(document), document, format);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MarkersOnLaterPagesUsePageLocalCoordinatesWithoutPageGap(bool shaped) => Run(() =>
    {
        var document = Document(); document.Page.HeightPt = 360;
        for (var index = 0; index < 40; index++) document.Blocks.Add(Item(Text(shaped), ListKind.Number));
        var view = Layout(document);
        view.PageCount.Should().BeGreaterThan(1);
        view.RenderedMarkerPositionsForTest.Should().HaveCount(40);
        AssertExportedMarkers(view, document, Format);
    });

    [Theory]
    [InlineData(DocumentViewMode.WebLayout)]
    [InlineData(DocumentViewMode.Draft)]
    public Task NonPrintViewsExportMarkersThroughTheExistingPrintAdapter(DocumentViewMode mode) => Run(() =>
    {
        var document = Document(); document.Blocks.Add(Item("Body", ListKind.Number));
        var print = Layout(document).BuildPdfContent();
        var view = Layout(document); view.ViewMode = mode;
        view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>().Should().Equal(
            print.Pages.SelectMany(page => page.Ops).OfType<PdfText>());
        print.Pages.SelectMany(page => page.Ops).OfType<PdfText>().Should().ContainSingle(text => text.Text == "1.");
    });

    [Fact]
    public Task RepeatedExportLeavesModelRunsFormattingAndLiveMarkersUnchanged() => Run(() =>
    {
        var document = Document(); document.Blocks.Add(Item("Body😀", ListKind.Number));
        var paragraph = (Paragraph)document.Blocks.Single();
        var runs = paragraph.Runs.ToArray(); var formatting = paragraph.Formatting; var runFormatting = runs[0].Formatting;
        var view = Layout(document); var markers = view.RenderedMarkerPositionsForTest.ToArray();
        var first = view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>().ToArray();
        first.Should().ContainSingle(text => text.Text == "1.");
        for (var repeat = 0; repeat < 3; repeat++)
            view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>().Should().Equal(first);
        paragraph.Runs.Should().Equal(runs); paragraph.Formatting.Should().BeSameAs(formatting);
        runs[0].Formatting.Should().BeSameAs(runFormatting); paragraph.PlainText.Should().Be("Body😀");
        view.RenderedMarkerPositionsForTest.Should().Equal(markers);
    });

    private static void AssertExportedMarkers(DocumentView view, TextDocument document, RunFormatting format)
    {
        var markers = view.RenderedMarkerPositionsForTest.ToArray(); markers.Should().NotBeEmpty();
        var pdf = view.BuildPdfContent();
        var markerStrings = markers.Select(marker => marker.Text).ToHashSet();
        var exported = pdf.Pages.SelectMany((page, index) => page.Ops.OfType<PdfText>().Where(text => markerStrings.Contains(text.Text)).Select(text => (Page: index, Text: text))).ToArray();
        exported.Should().HaveCount(markers.Length, "the renderer's marker stream must be exported once, including table markers");
        foreach (var marker in markers)
        {
            var page = Enumerable.Range(0, view.PageCount).Single(index => marker.Y >= view.RenderedPageOriginForTest(index).Y
                && marker.Y < view.RenderedPageOriginForTest(index).Y + document.Page.HeightPt * DipPerPoint);
            var origin = view.RenderedPageOriginForTest(page);
            var expectedX = (marker.X - origin.X) / DipPerPoint;
            var face = (format.Bold, format.Italic) switch { (true, true) => PdfFontFace.BoldItalic, (true, false) => PdfFontFace.Bold, (false, true) => PdfFontFace.Italic, _ => PdfFontFace.Regular };
            var independent = new FormattedText(marker.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(format.FontFamily!, format.Italic ? FontStyle.Italic : FontStyle.Normal, format.Bold ? FontWeight.Bold : FontWeight.Normal),
                format.FontSizePt!.Value * DipPerPoint, Brushes.Black);
            var expectedY = document.Page.HeightPt - (marker.Y - origin.Y + independent.Baseline) / DipPerPoint;
            var match = exported.Should().ContainSingle(item => item.Page == page && item.Text.Text == marker.Text
                && Math.Abs(item.Text.X - expectedX) < 0.01 && Math.Abs(item.Text.Y - expectedY) < 0.01).Which.Text;
            match.Face.Should().Be(face); match.FontFamily.Should().Be(format.FontFamily); match.FontSize.Should().Be(format.FontSizePt!.Value);
            match.Color.Should().Be(format.ColorHex == "#9B2226" ? new PdfColor(155, 34, 38) : PdfColor.Black);
        }
    }

    private static string Text(bool shaped) => shaped ? "Body😀" : "Body";
    private static TextDocument Document()
    {
        var document = TextDocument.CreateEmpty(); document.Blocks.Clear(); document.DefaultRun = Format;
        document.Page.WidthPt = 360; document.Page.HeightPt = 480;
        document.Page.MarginLeftPt = document.Page.MarginRightPt = document.Page.MarginTopPt = document.Page.MarginBottomPt = 36;
        return document;
    }
    private static Paragraph Item(string text, ListKind kind)
    {
        var p = new Paragraph(); p.Runs.Add(new Run(text, Format));
        p.Formatting = ParagraphFormatting.Default with { ListKind = kind, LineRule = LineSpacingRule.Exact, LineHeightPt = 24, SpaceAfterPt = 0,
            ListLevelIndentLeftPt = 36, ListLevelFirstLineIndentPt = -18 };
        return p;
    }
    private static DocumentView Layout(TextDocument document) { var view = new DocumentView(); view.LoadDocument(document); view.Measure(new Size(900, 4000)); view.Arrange(new Rect(0, 0, 900, 4000)); return view; }
    private static Task Run(Action action) => Session.Dispatch(action, CancellationToken.None);
}
