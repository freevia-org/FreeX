using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia;
using FreeW.Core.Model;
using Free.Shared.Pdf;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Paired with WPF's HeaderFooterPaginatorTests: PDF export must retain the text regions that the
/// paginated editor and print preview already show, not only the body glyph stream.
/// </summary>
public sealed class DocumentViewPdfExportTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task BuildPdfContent_IncludesHeaderFooterFootnoteAndSeparator() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var body = new Paragraph();
            body.Runs.Add(new Run("Body text "));
            body.Runs.Add(Run.FootnoteReference(1));
            document.Blocks.Add(body);
            document.FinalSectionHeadersFooters.Header = new HeaderFooter("Header text");
            document.FinalSectionHeadersFooters.Footer = new HeaderFooter("Footer text");
            document.Footnotes[1] = new Footnote(1, "Footnote body");

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var text = pdf.Pages.SelectMany(page => page.Ops).OfType<PdfText>().Select(op => op.Text).ToArray();

            text.Should().Contain("Header text");
            text.Should().Contain("Footer text");
            text.Should().Contain("Footnote ");
            text.Should().Contain("body");
            pdf.Pages.SelectMany(page => page.Ops).Should().Contain(op => op is PdfLine);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_IncludesTableSurfacesBeforeCellText() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var table = Table.Create(1, 2);
            table.Formatting = TableFormatting.Default with { Borders = true };
            table.Rows[0].Cells[0] = new TableCell("Red cell")
            {
                ShadingColorHex = "#FF0000",
                Borders = new CellBorders
                {
                    Top = new CellBorderEdge(BorderLineStyle.Double, "#00AA00", 1.0),
                },
            };
            table.Rows[0].Cells[1] = new TableCell("Blue cell")
            {
                ShadingColorHex = "#0000FF",
            };
            document.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var ops = pdf.Pages[0].Ops.ToList();
            var firstTextIndex = ops.FindIndex(op => op is PdfText text && text.Text.Contains("Red", StringComparison.Ordinal));

            ops.OfType<PdfFillRect>().Select(op => op.Color).Should().Contain(new PdfColor(0xFF, 0x00, 0x00));
            ops.OfType<PdfFillRect>().Select(op => op.Color).Should().Contain(new PdfColor(0x00, 0x00, 0xFF));
            ops.OfType<PdfStrokeRect>().Should().NotBeEmpty();
            ops.OfType<PdfLine>().Should().Contain(line => line.Color == new PdfColor(0x00, 0xAA, 0x00));
            firstTextIndex.Should().BeGreaterThan(0);
            ops.Take(firstTextIndex).Any(op => op is PdfFillRect or PdfStrokeRect or PdfLine).Should().BeTrue();
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ClipsTableSurfacesToOwningPages() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 260;
            document.Page.HeightPt = 180;
            document.Page.MarginTopPt = 18;
            document.Page.MarginBottomPt = 18;
            document.Page.MarginLeftPt = 18;
            document.Page.MarginRightPt = 18;

            var table = Table.Create(18, 1);
            table.Formatting = TableFormatting.Default with { Borders = true };
            for (var row = 0; row < table.Rows.Count; row++)
            {
                table.Rows[row].Cells[0] = new TableCell($"Row {row + 1}")
                {
                    ShadingColorHex = row % 2 == 0 ? "#EEEEEE" : null,
                };
            }
            document.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            pdf.Pages.Should().HaveCountGreaterThan(1);
            foreach (var page in pdf.Pages)
            {
                foreach (var op in page.Ops.OfType<PdfFillRect>())
                {
                    op.X.Should().BeGreaterThanOrEqualTo(0);
                    op.Y.Should().BeGreaterThanOrEqualTo(0);
                    (op.X + op.Width).Should().BeLessThanOrEqualTo(page.WidthPoints + 0.01);
                    (op.Y + op.Height).Should().BeLessThanOrEqualTo(page.HeightPoints + 0.01);
                }

                foreach (var op in page.Ops.OfType<PdfStrokeRect>())
                {
                    op.X.Should().BeGreaterThanOrEqualTo(0);
                    op.Y.Should().BeGreaterThanOrEqualTo(0);
                    (op.X + op.Width).Should().BeLessThanOrEqualTo(page.WidthPoints + 0.01);
                    (op.Y + op.Height).Should().BeLessThanOrEqualTo(page.HeightPoints + 0.01);
                }
            }
        }, CancellationToken.None);
}
