using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia;
using FreeW.Core.Model;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Skia;
using SkiaSharp;

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

    [Fact]
    public Task BuildPdfContent_IncludesLaidOutInlineImageWithSharedCropOpacityRotationAndOrdering() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();

            var table = Table.Create(1, 1);
            table.Formatting = TableFormatting.Default with { Borders = true };
            table.Rows[0].Cells[0] = new TableCell("Surface");
            document.Blocks.Add(table);

            var image = new InlineImage(SolidPng(SKColors.Red), 180, 72)
            {
                CropLeft = 0.10,
                CropTop = 0.15,
                CropRight = 0.20,
                CropBottom = 0.05,
                TransparencyPct = 25,
                RotationAngle = 18,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Before"));
            paragraph.Runs.Add(Run.FromImage(image));
            paragraph.Runs.Add(new Run("After"));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var ops = pdf.Pages.SelectMany(page => page.Ops).ToList();
            var imageOp = ops.OfType<PdfImage>().Single();
            var imageIndex = ops.IndexOf(imageOp);

            imageOp.ContentType.Should().Be("image/png");
            imageOp.ImageBytes.Should().BeSameAs(image.Bytes);
            imageOp.SourceCrop.Should().Be(new PdfImageSourceCrop(0.10, 0.15, 0.20, 0.05));
            imageOp.Opacity.Should().BeApproximately(0.75, 0.001);
            imageOp.RotationDegrees.Should().BeApproximately(18, 0.001);
            imageOp.Width.Should().BeApproximately(view.InlineImageRects.Single().Width / (96.0 / 72.0), 0.001);
            imageIndex.Should().BeGreaterThan(0, "table surfaces must remain before the inline image");
            ops.Take(imageIndex).Any(op => op is PdfFillRect or PdfStrokeRect or PdfLine)
                .Should().BeTrue("table surfaces must remain before the inline image");
            ops.Skip(imageIndex + 1).Any(op => op is PdfText)
                .Should().BeTrue("the image pass must precede body text");
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_RendersInlineImageThroughSharedSkiaBackend() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var image = new InlineImage(SolidPng(SKColors.Red), 144, 72);
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(image));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pagePng = SkiaPdfWriter.RenderPagesToPng(view.BuildPdfContent()).Single();
            using var bitmap = SKBitmap.Decode(pagePng);
            var redPixels = 0;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 200 && pixel.Green < 80 && pixel.Blue < 80)
                    redPixels++;
            }

            redPixels.Should().BeGreaterThan(500, "the shared PDF renderer must paint the laid-out inline image");
        }, CancellationToken.None);

    private static byte[] SolidPng(SKColor color)
    {
        using var bitmap = new SKBitmap(16, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
