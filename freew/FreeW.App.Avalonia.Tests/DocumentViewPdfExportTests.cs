using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Pdf;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.DocumentView;
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
    public Task BuildPdfContent_ExportsRunBackgroundPrecedenceAndItalicFace() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Highlighted", RunFormatting.Default with
            {
                HighlightColorHex = "#FFFF00",
                Italic = true,
            }));
            paragraph.Runs.Add(new Run(" Shaded", RunFormatting.Default with
            {
                HighlightColorHex = "#FF0000",
                CharacterShadingHex = "#00FF00",
            }));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            var ops = pdf.Pages[0].Ops.ToList();
            var fills = ops.OfType<PdfFillRect>().ToList();
            fills.Select(fill => fill.Color).Should().Contain(new PdfColor(0xFF, 0xFF, 0x00));
            fills.Select(fill => fill.Color).Should().Contain(new PdfColor(0x00, 0xFF, 0x00));
            fills.Select(fill => fill.Color).Should().NotContain(new PdfColor(0xFF, 0x00, 0x00),
                "character shading takes precedence over highlight in live Print Layout");
            fills.Should().OnlyContain(fill => fill.Width > 0 && fill.Height > 0);

            var italic = ops.OfType<PdfText>().Single(text => text.Text == "Highlighted");
            italic.Face.Should().Be(PdfFontFace.Italic);
            ops.IndexOf(fills.Single(fill => fill.Color == new PdfColor(0xFF, 0xFF, 0x00)))
                .Should().BeLessThan(ops.IndexOf(italic));
            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_PreservesResolvedRunFontFamilies() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Serif", RunFormatting.Default with
            {
                FontFamily = "Times New Roman",
            }));
            paragraph.Runs.Add(new Run("Mono", RunFormatting.Default with
            {
                FontFamily = "Courier New",
            }));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var text = view.BuildPdfContent().Pages[0].Ops.OfType<PdfText>().ToArray();

            text.Should().Contain(item => item.Text == "Serif" && item.FontFamily == "Times New Roman");
            text.Should().Contain(item => item.Text == "Mono" && item.FontFamily == "Courier New");
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ExportsUnderlineStrikeAndHyperlinkVisualStyle() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Under", RunFormatting.Default with
            {
                Underline = true,
                ColorHex = "#CC0000",
            }));
            paragraph.Runs.Add(new Run(" Strike", RunFormatting.Default with
            {
                Strikethrough = true,
                ColorHex = "#0055AA",
            }));
            paragraph.Runs.Add(new Run(" Link")
            {
                HyperlinkUrl = "https://example.com",
            });
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            var ops = pdf.Pages[0].Ops.ToList();
            var underlineText = ops.OfType<PdfText>().Single(text => text.Text == "Under");
            var strikeText = ops.OfType<PdfText>().Single(text => text.Text == " Strike");
            var linkText = ops.OfType<PdfText>().Single(text => text.Text == " Link");
            linkText.Color.Should().Be(new PdfColor(0x05, 0x63, 0xC1));

            var underline = ops.OfType<PdfLine>().Single(line => line.Color == underlineText.Color);
            var strike = ops.OfType<PdfLine>().Single(line => line.Color == strikeText.Color);
            var linkUnderline = ops.OfType<PdfLine>().Single(line => line.Color == linkText.Color);
            strike.Y1.Should().BeGreaterThan(underline.Y1);
            underline.X1.Should().BeApproximately(underlineText.X, 0.001);
            linkUnderline.X1.Should().BeApproximately(linkText.X, 0.001);
            new[] { underline, strike, linkUnderline }.Should().OnlyContain(line =>
                line.X2 > line.X1 && line.LineWidth > 0 && Math.Abs(line.Y1 - line.Y2) < 0.001);
            ops.IndexOf(underline).Should().BeGreaterThan(ops.IndexOf(underlineText));
            ops.IndexOf(strike).Should().BeGreaterThan(ops.IndexOf(strikeText));
            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ExportsExternalAndCrossPageBookmarkHyperlinks() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("First")
            {
                HyperlinkUrl = "https://first.example/path",
                HyperlinkTooltip = "First tip",
            });
            paragraph.Runs.Add(new Run("Second")
            {
                HyperlinkUrl = "https://second.example/path",
                HyperlinkTooltip = "Second tip",
            });
            paragraph.Runs.Add(new Run("Bookmark")
            {
                HyperlinkAnchor = "Target1",
                HyperlinkTooltip = "Jump to target",
            });
            document.Blocks.Add(paragraph);
            document.Page.WidthPt = 260;
            document.Page.HeightPt = 180;
            document.Page.MarginTopPt = 18;
            document.Page.MarginBottomPt = 18;
            document.Page.MarginLeftPt = 18;
            document.Page.MarginRightPt = 18;
            for (var index = 0; index < 12; index++)
                document.Blocks.Add(new Paragraph($"Filler {index + 1}"));
            var target = new Paragraph();
            target.Runs.Add(new Run("Prefix "));
            target.Runs.Add(new Run("Target paragraph", RunFormatting.Default with { Bold = true }));
            target.BookmarkNames.Add("Target1");
            target.BookmarkBoundaries.Add(new BookmarkBoundary(
                "target-pair",
                BookmarkBoundaryKind.Start,
                RunIndex: 1,
                Name: "Target1"));
            target.BookmarkBoundaries.Add(new BookmarkBoundary(
                "target-pair",
                BookmarkBoundaryKind.End,
                RunIndex: 2));
            document.Blocks.Add(target);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            pdf.Pages.Should().HaveCountGreaterThan(1);
            var links = pdf.Pages[0].LinkOverlays.Should().NotBeNull().And.Subject!.ToArray();
            links.Should().HaveCount(3);
            links.Take(2).Select(link => link.Uri).Should().Equal(
                "https://first.example/path",
                "https://second.example/path");
            links.Take(2).Select(link => link.Tooltip).Should().Equal("First tip", "Second tip");
            links[2].Uri.Should().BeNull();
            links[2].DestinationName.Should().Be("Target1");
            links[2].Tooltip.Should().Be("Jump to target");
            links.Should().OnlyContain(link =>
                link.X >= 0
                && link.Y >= 0
                && link.Width > 0
                && link.Height > 0
                && link.X + link.Width <= pdf.Pages[0].WidthPoints
                && link.Y + link.Height <= pdf.Pages[0].HeightPoints);
            links[1].X.Should().BeGreaterThan(links[0].X + links[0].Width - 0.01);
            var destinationPage = pdf.Pages
                .Skip(1)
                .Single(page => page.NamedDestinations is { Count: > 0 });
            var destination = destinationPage.NamedDestinations.Should().ContainSingle().Which;
            destination.Name.Should().Be("Target1");
            var targetText = destinationPage.Ops.OfType<PdfText>()
                .First(text => text.Face == PdfFontFace.Bold
                    && text.Text.Contains("Target", StringComparison.Ordinal));
            destination.X.Should().BeApproximately(targetText.X, 0.01);

            var portable = Encoding.Latin1.GetString(PortablePdfWriter.WriteToBytes(pdf));
            portable.Should().Contain("/URI (https://first.example/path)");
            portable.Should().Contain("/URI (https://second.example/path)");
            portable.Should().Contain("/Dest [");
            portable.Should().Contain("/Contents (Jump to target)");

            var skia = Encoding.Latin1.GetString(SkiaPdfWriter.WriteToBytes(pdf));
            skia.Should().Contain("/URI (https://first.example/path)");
            skia.Should().Contain("/URI (https://second.example/path)");
            skia.Should().Contain("Target1");
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ExportsSelectiveCharacterBorderPath() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Selective border", RunFormatting.Default with
            {
                CharacterBorder = new ParagraphBorder("#7F6000", 1.5)
                {
                    LineStyle = BorderLineStyle.Dotted,
                    Top = false,
                    Left = true,
                    Bottom = true,
                    Right = false,
                },
            }));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            var ops = pdf.Pages[0].Ops.ToList();
            var text = ops.OfType<PdfText>().Single(item => item.Text == "Selective border");
            var border = ops.OfType<PdfPath>().Single(path => path.StrokeColor == new PdfColor(0x7F, 0x60, 0x00));
            border.Contours.Should().HaveCount(2, "only the authored left and bottom edges are visible");
            border.Contours.Should().OnlyContain(contour => contour.Segments.Count == 1 && !contour.Closed);
            border.StrokeWidth.Should().BeApproximately(1.5, 0.001);
            border.StrokeDash.Should().NotBeNull();
            border.StrokeDash!.Segments.Should().Equal(0.75, 1.5);
            ops.IndexOf(border).Should().BeGreaterThan(ops.IndexOf(text));
            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ExportsSuperscriptAndSubscriptScaleAndBaselines() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Base", RunFormatting.Default with { FontSizePt = 12 }));
            paragraph.Runs.Add(new Run("Super", RunFormatting.Default with
            {
                FontSizePt = 12,
                VerticalAlign = VerticalAlign.Superscript,
            }));
            paragraph.Runs.Add(new Run("Sub", RunFormatting.Default with
            {
                FontSizePt = 12,
                VerticalAlign = VerticalAlign.Subscript,
            }));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            var text = pdf.Pages[0].Ops.OfType<PdfText>().ToDictionary(item => item.Text);
            text["Base"].FontSize.Should().BeApproximately(12, 0.001);
            text["Super"].FontSize.Should().BeApproximately(12 * 0.583, 0.001);
            text["Sub"].FontSize.Should().BeApproximately(12 * 0.583, 0.001);
            text["Super"].Y.Should().BeGreaterThan(text["Base"].Y);
            text["Super"].Y.Should().BeGreaterThan(text["Sub"].Y);
            Math.Abs(text["Sub"].Y - text["Base"].Y).Should().BeGreaterThan(0.01);
            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ExportsResolvedFirstEvenAndDefaultHeaderImages() =>
        Session.Dispatch(() =>
        {
            var firstBytes = SolidPng(SKColors.Red);
            var evenBytes = SolidPng(SKColors.Green);
            var defaultBytes = SolidPng(SKColors.Blue);
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 260;
            document.Page.HeightPt = 180;
            document.Page.MarginTopPt = 24;
            document.Page.MarginBottomPt = 18;
            document.Page.MarginLeftPt = 18;
            document.Page.MarginRightPt = 18;
            document.Page.DifferentFirstPage = true;
            document.Page.DifferentOddEvenPages = true;
            document.FinalSectionHeadersFooters.FirstHeader = ImageHeader(firstBytes, TextAlignment.Left);
            document.FinalSectionHeadersFooters.EvenHeader = ImageHeader(evenBytes, TextAlignment.Center);
            document.FinalSectionHeadersFooters.Header = ImageHeader(defaultBytes, TextAlignment.Right);
            for (var index = 0; index < 60; index++)
            {
                document.Blocks.Add(new Paragraph($"Header image page {index + 1}")
                {
                    Formatting = ParagraphFormatting.Default with
                    {
                        SpaceAfterPt = 0,
                        SpaceAfterIsSet = true,
                    },
                });
            }

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            pdf.Pages.Should().HaveCountGreaterThanOrEqualTo(3);
            var first = pdf.Pages[0].Ops.OfType<PdfImage>().Single();
            var even = pdf.Pages[1].Ops.OfType<PdfImage>().Single();
            var defaultOdd = pdf.Pages[2].Ops.OfType<PdfImage>().Single();
            first.ImageBytes.Should().Equal(firstBytes);
            even.ImageBytes.Should().Equal(evenBytes);
            defaultOdd.ImageBytes.Should().Equal(defaultBytes);
            first.X.Should().BeLessThan(even.X);
            even.X.Should().BeLessThan(defaultOdd.X);
            new[] { first, even, defaultOdd }.Should().OnlyContain(image =>
                image.Y >= 0 && image.Y + image.Height <= document.Page.HeightPt);
            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task Save_SurfacesImageDiagnostics_WhenInlineImageBytesAreUndecodable() =>
        Session.Dispatch(() =>
        {
            // R133-imageDiagnostics-wiring: an inline picture with bytes the PDF writer cannot decode
            // (corrupt/unrecognized format) used to be silently omitted from the page with no trace
            // anywhere -- the shared writer's imageDiagnostics sink existed since r132 but no
            // production caller ever passed a collection in. This exercises the exact seam FreeW's
            // File -> Export to PDF / Print uses (FreeWAvaloniaPdfExport.Save) and asserts the loss
            // reaches the caller instead of being discarded.
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(new InlineImage([0x00, 0x01, 0x02, 0x03, 0x04], 30, 12)
            {
                Wrapping = ImageWrapping.Inline,
            }));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            using var stream = new MemoryStream();
            var result = FreeWAvaloniaPdfExport.Save(view, stream);

            result.ImageDiagnostics.Should().NotBeEmpty(
                "the undecodable inline image's bytes must be surfaced, not silently dropped");
        }, CancellationToken.None);

    [Fact]
    public Task Save_NoImageDiagnostics_WhenNoPicturesAreEmbedded() =>
        Session.Dispatch(() =>
        {
            // Sibling no-regression: an export with no embedded pictures at all must not spuriously
            // report image warnings -- the diagnostics collection stays empty.
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("Plain text, no pictures."));

            var view = new DocumentView();
            view.LoadDocument(document);

            using var stream = new MemoryStream();
            var result = FreeWAvaloniaPdfExport.Save(view, stream);

            result.ImageDiagnostics.Should().BeEmpty();
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
    public Task BuildPdfContent_IncludesParagraphShadingAndSelectiveDashedBordersBeforeText() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("Shaded bordered paragraph")
            {
                Formatting = ParagraphFormatting.Default with
                {
                    ShadingColorHex = "#D9EAD3",
                    Border = new ParagraphBorder("#C00000", 1.5)
                    {
                        LineStyle = BorderLineStyle.Dashed,
                        Right = false,
                    },
                    SpaceAfterPt = 0,
                    SpaceAfterIsSet = true,
                },
            });
            document.Blocks.Add(new Paragraph("Bottom rule")
            {
                Formatting = ParagraphFormatting.Default with
                {
                    Border = new ParagraphBorder("#0070C0", 1, BottomOnly: true)
                    {
                        LineStyle = BorderLineStyle.Dotted,
                    },
                    SpaceAfterPt = 0,
                    SpaceAfterIsSet = true,
                },
            });

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var ops = pdf.Pages.Single().Ops.ToList();
            var shading = ops.OfType<PdfFillRect>()
                .Single(fill => fill.Color == new PdfColor(0xD9, 0xEA, 0xD3));
            var dashed = ops.OfType<PdfPath>()
                .Single(path => path.StrokeColor == new PdfColor(0xC0, 0x00, 0x00));
            var bottomOnly = ops.OfType<PdfPath>()
                .Single(path => path.StrokeColor == new PdfColor(0x00, 0x70, 0xC0));
            var firstText = ops.OfType<PdfText>()
                .Single(text => text.Text.Contains("Shaded bordered paragraph", StringComparison.Ordinal));

            shading.Width.Should().BeGreaterThan(0);
            shading.Height.Should().BeGreaterThan(0);
            dashed.Contours.Should().HaveCount(3);
            dashed.StrokeWidth.Should().Be(1.5);
            dashed.StrokeDash.Should().NotBeNull();
            dashed.StrokeDash!.Segments.Should().Equal(6, 4.5);
            bottomOnly.Contours.Should().ContainSingle();
            bottomOnly.StrokeWidth.Should().Be(1);
            bottomOnly.StrokeDash.Should().NotBeNull();
            bottomOnly.StrokeDash!.Segments.Should().Equal(1, 2);
            ops.IndexOf(shading).Should().BeLessThan(ops.IndexOf(firstText));
            ops.IndexOf(dashed).Should().BeLessThan(ops.IndexOf(firstText));
            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var greenPixels = 0;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red == 0xD9 && pixel.Green == 0xEA && pixel.Blue == 0xD3)
                    greenPixels++;
            }
            greenPixels.Should().BeGreaterThan(100);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_DoesNotEmitParagraphSurfacesWithoutAuthoredDecoration() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            var view = new DocumentView();
            view.LoadDocument(document);

            var ops = view.BuildPdfContent().Pages.SelectMany(page => page.Ops).ToArray();
            ops.OfType<PdfFillRect>().Should().BeEmpty();
            ops.OfType<PdfPath>().Should().BeEmpty();
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_RepeatsResolvedColumnRulesOnEveryPage() =>
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
            document.Page.ColumnCount = 3;
            document.Page.ColumnSpacingPt = 12;
            document.Page.ColumnsLineBetween = true;
            for (var index = 0; index < 40; index++)
            {
                document.Blocks.Add(new Paragraph($"Column line {index + 1}")
                {
                    Formatting = ParagraphFormatting.Default with
                    {
                        SpaceAfterPt = 0,
                        SpaceAfterIsSet = true,
                    },
                });
            }

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            pdf.Pages.Should().HaveCountGreaterThan(1);
            foreach (var page in pdf.Pages)
            {
                var rules = page.Ops.OfType<PdfLine>()
                    .Where(line => line.Color == PdfColor.Black && Math.Abs(line.X1 - line.X2) < 0.001)
                    .ToArray();
                rules.Should().HaveCount(2);
                rules.Select(rule => rule.X1).Should().BeInAscendingOrder();
                rules.Should().OnlyContain(rule => rule.X1 > 18 && rule.X1 < 242);
                rules.Should().OnlyContain(rule => Math.Abs(rule.Y1 - 18) < 0.001);
                rules.Should().OnlyContain(rule => Math.Abs(rule.Y2 - 162) < 0.001);
                rules.Should().OnlyContain(rule => Math.Abs(rule.LineWidth - 0.75) < 0.001);
            }

            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_DoesNotEmitColumnRulesWhenSeparatorIsDisabled() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.ColumnCount = 2;
            document.Page.ColumnsLineBetween = false;
            var view = new DocumentView();
            view.LoadDocument(document);

            view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfLine>()
                .Should().NotContain(line => line.Color == PdfColor.Black && Math.Abs(line.X1 - line.X2) < 0.001);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_IncludesPageBorderOnEveryPageInFrontOfDocumentContent() =>
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
            document.Page.PageBorder = new PageBorder("#24536B", 1.5)
            {
                SpacePt = 12,
                LineStyle = BorderLineStyle.Dashed,
            };
            for (var i = 0; i < 30; i++)
                document.Blocks.Add(new Paragraph($"Bordered line {i + 1}"));

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            pdf.Pages.Should().HaveCountGreaterThan(1);
            foreach (var page in pdf.Pages)
            {
                var border = page.Ops[^1].Should().BeOfType<PdfStrokeRect>().Subject;
                border.X.Should().BeApproximately(13.125, 0.001);
                border.Y.Should().BeApproximately(13.125, 0.001);
                border.Width.Should().BeApproximately(233.75, 0.001);
                border.Height.Should().BeApproximately(153.75, 0.001);
                border.Color.Should().Be(new PdfColor(0x24, 0x53, 0x6B));
                border.LineWidth.Should().Be(1.5);
                border.Dash.Should().NotBeNull();
                border.Dash!.Segments.Should().Equal(4.5, 3.0);
                border.Dash.Phase.Should().Be(0);
            }

            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).First());
            var borderPixels = 0;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red == 0x24 && pixel.Green == 0x53 && pixel.Blue == 0x6B)
                    borderPixels++;
            }
            borderPixels.Should().BeGreaterThan(100);
        }, CancellationToken.None);

    [Theory]
    [InlineData(PageBorderZOrder.Front, false)]
    [InlineData(PageBorderZOrder.Behind, true)]
    public Task BuildPdfContent_RespectsPageBorderZOrder(
        PageBorderZOrder zOrder,
        bool borderIsFirst) =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.PageBorder = new PageBorder("#24536B", 1.5)
            {
                SpacePt = 12,
                ZOrder = zOrder,
            };
            document.Blocks.Add(new Paragraph("Body text above or below the authored border."));

            var view = new DocumentView();
            view.LoadDocument(document);

            var ops = view.BuildPdfContent().Pages.Single().Ops;
            var borderIndex = ops
                .Select((op, index) => (op, index))
                .Single(item => item.op is PdfStrokeRect stroke
                    && stroke.Color == new PdfColor(0x24, 0x53, 0x6B))
                .index;

            borderIndex.Should().Be(borderIsFirst ? 0 : ops.Count - 1);
        }, CancellationToken.None);

    [Theory]
    [InlineData(PageBorderDisplay.FirstPage, true)]
    [InlineData(PageBorderDisplay.NotFirstPage, false)]
    public Task BuildPdfContent_RespectsPageBorderDisplay(
        PageBorderDisplay display,
        bool firstPageHasBorder) =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 260;
            document.Page.HeightPt = 180;
            document.Page.MarginTopPt = 18;
            document.Page.MarginBottomPt = 18;
            document.Page.PageBorder = new PageBorder("#24536B", 1.5)
            {
                SpacePt = 12,
                Display = display,
            };
            for (var i = 0; i < 30; i++)
                document.Blocks.Add(new Paragraph($"Bordered line {i + 1}"));

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            pdf.Pages.Should().HaveCountGreaterThan(1);
            var borderPresence = pdf.Pages
                .Select(page => page.Ops.OfType<PdfStrokeRect>()
                    .Any(border => border.Color == new PdfColor(0x24, 0x53, 0x6B)))
                .ToArray();

            borderPresence[0].Should().Be(firstPageHasBorder);
            borderPresence.Skip(1).Should().OnlyContain(present => present != firstPageHasBorder);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesTextOffsetAndDoublePageBorderGeometry() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.MarginLeftPt = 72;
            document.Page.MarginRightPt = 54;
            document.Page.MarginBottomPt = 60;
            document.Page.HeaderDistancePt = 36;
            document.PageBordersDoNotSurroundFooter = true;
            document.Page.PageBorder = new PageBorder("#A020F0", 2)
            {
                OffsetFrom = PageBorderOffsetFrom.Text,
                SpacePt = 6,
                LineStyle = BorderLineStyle.Double,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var borders = view.BuildPdfContent().Pages.Single().Ops.OfType<PdfStrokeRect>().ToArray();

            borders.Should().HaveCount(2);
            borders[0].Should().Be(new PdfStrokeRect(65, 53, 500, 710, new PdfColor(0xA0, 0x20, 0xF0), 2));
            borders[1].Should().Be(new PdfStrokeRect(68.125, 56.125, 493.75, 703.75, new PdfColor(0xA0, 0x20, 0xF0), 2));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_PreservesRunBaselinePositionForBodyAndHeader() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var body = new Paragraph();
            body.Runs.Add(new Run("BodyBase"));
            body.Runs.Add(new Run("BodyRaised", RunFormatting.Default with { PositionPt = 3 }));
            document.Blocks.Add(body);

            var headerParagraph = new Paragraph();
            headerParagraph.Runs.Add(new Run("HeaderBase"));
            headerParagraph.Runs.Add(new Run("HeaderLowered", RunFormatting.Default with { PositionPt = -2 }));
            document.FinalSectionHeadersFooters.Header = new HeaderFooter();
            document.FinalSectionHeadersFooters.Header.Paragraphs.Add(headerParagraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var text = view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>().ToArray();
            var bodyBase = text.Single(item => item.Text == "BodyBase");
            var bodyRaised = text.Single(item => item.Text == "BodyRaised");
            var headerBase = text.Single(item => item.Text == "HeaderBase");
            var headerLowered = text.Single(item => item.Text == "HeaderLowered");

            bodyRaised.Y.Should().BeApproximately(bodyBase.Y + 3, 0.001);
            headerLowered.Y.Should().BeApproximately(headerBase.Y - 2, 0.001);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ExportsDoubleStrikeAsTwoLinesAndPreservesSingleStrikeControl() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Double", RunFormatting.Default with
            {
                DoubleStrikethrough = true,
                ColorHex = "#CC0000",
            }));
            paragraph.Runs.Add(new Run(" Single", RunFormatting.Default with
            {
                Strikethrough = true,
                ColorHex = "#0055AA",
            }));
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadDocument(document);

            var ops = view.BuildPdfContent().Pages[0].Ops;
            var doubleLines = ops.OfType<PdfLine>()
                .Where(line => line.Color == new PdfColor(0xCC, 0x00, 0x00))
                .ToArray();
            var singleLines = ops.OfType<PdfLine>()
                .Where(line => line.Color == new PdfColor(0x00, 0x55, 0xAA))
                .ToArray();

            doubleLines.Should().HaveCount(2);
            doubleLines.Select(line => line.Y1).Should().OnlyHaveUniqueItems();
            singleLines.Should().ContainSingle();
            doubleLines.Concat(singleLines).Should().OnlyContain(line =>
                line.X2 > line.X1 && line.LineWidth > 0 && Math.Abs(line.Y1 - line.Y2) < 0.001);
        }, CancellationToken.None);

    [Fact]
    public Task HiddenText_RemainsAddressableButIsSuppressedAcrossLiveLayoutAndPdf() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();

            var body = new Paragraph();
            body.Runs.Add(new Run("A"));
            body.Runs.Add(new Run("23456789", RunFormatting.Default with { Hidden = true }));
            body.Runs.Add(new Run("B"));
            body.Runs.Add(Run.FootnoteReference(1));
            document.Blocks.Add(body);

            var table = Table.Create(1, 1);
            var cellParagraph = new Paragraph();
            cellParagraph.Runs.Add(new Run("C"));
            cellParagraph.Runs.Add(new Run("JKLMNOPQ", RunFormatting.Default with { Hidden = true }));
            cellParagraph.Runs.Add(new Run("D"));
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(cellParagraph);
            document.Blocks.Add(table);

            var header = new HeaderFooter();
            var headerParagraph = new Paragraph();
            headerParagraph.Runs.Add(new Run("Header "));
            headerParagraph.Runs.Add(new Run("HEADER_SECRET", RunFormatting.Default with { Hidden = true }));
            headerParagraph.Runs.Add(new Run("Visible"));
            header.Paragraphs.Add(headerParagraph);
            document.FinalSectionHeadersFooters.Header = header;

            var footnote = new Footnote(1);
            var noteParagraph = new Paragraph();
            noteParagraph.Runs.Add(new Run("Footnote "));
            noteParagraph.Runs.Add(new Run("NOTE_SECRET", RunFormatting.Default with { Hidden = true }));
            noteParagraph.Runs.Add(new Run("Visible"));
            footnote.Content.Add(noteParagraph);
            document.Footnotes[1] = footnote;

            var view = new DocumentView();
            view.LoadDocument(document);
            var pdf = view.BuildPdfContent();

            var bodyPlacement = view.GetPlacedForBlock(0);
            string.Concat(bodyPlacement.Select(glyph => glyph.Ch)).Should().Contain("23456789");
            bodyPlacement
                .Where(glyph => glyph.Ch is >= '2' and <= '9')
                .Should().OnlyContain(glyph => glyph.W == 0);

            var tablePlacement = view.GetPlacedForBlock(1);
            string.Concat(tablePlacement.Select(glyph => glyph.Ch)).Should().Contain("JKLMNOPQ");
            tablePlacement
                .Where(glyph => glyph.Ch is >= 'J' and <= 'Q')
                .Should().OnlyContain(glyph => glyph.W == 0);

            var exportedText = string.Concat(pdf.Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfText>()
                .Select(text => text.Text));
            exportedText.Should().Contain("A").And.Contain("B");
            exportedText.Should().Contain("Header").And.Contain("Visible");
            exportedText.Should().Contain("Footnote");
            exportedText.Should().NotContain("23456789");
            exportedText.Should().NotContain("JKLMNOPQ");
            exportedText.Should().NotContain("HEADER_SECRET");
            exportedText.Should().NotContain("NOTE_SECRET");
        }, CancellationToken.None);

    [Fact]
    public Task WebHiddenText_CollapsesOnlyInWebLayoutWhilePdfUsesPrintLayout() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("A"));
            paragraph.Runs.Add(new Run("23456789", RunFormatting.Default with { WebHidden = true }));
            paragraph.Runs.Add(new Run("B"));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            var printPdfText = string.Concat(view.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfText>()
                .Select(text => text.Text));
            printPdfText.Should().Contain("23456789");
            view.GetPlacedForBlock(0)
                .Where(glyph => glyph.Ch is >= '2' and <= '9')
                .Should().OnlyContain(glyph => glyph.W > 0);

            view.ViewMode = DocumentViewMode.WebLayout;
            view.Measure(new Size(900, 1200));
            view.GetPlacedForBlock(0)
                .Where(glyph => glyph.Ch is >= '2' and <= '9')
                .Should().OnlyContain(glyph => glyph.W == 0);

            var webModePdfText = string.Concat(view.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfText>()
                .Select(text => text.Text));
            webModePdfText.Should().Contain("23456789",
                "PDF export uses Word's print-layout semantics even when the live editor is in Web Layout");

            view.ViewMode = DocumentViewMode.Draft;
            view.Measure(new Size(900, 1200));
            view.GetPlacedForBlock(0)
                .Where(glyph => glyph.Ch is >= '2' and <= '9')
                .Should().OnlyContain(glyph => glyph.W > 0);
        }, CancellationToken.None);

    [Theory]
    [InlineData(false, false, 23, 740)]
    [InlineData(true, false, 23, 704)]
    [InlineData(false, true, 53, 710)]
    [InlineData(true, true, 53, 674)]
    public Task BuildPdfContent_AppliesTextRelativeHeaderAndFooterExclusions(
        bool doNotSurroundHeader,
        bool doNotSurroundFooter,
        double expectedBottom,
        double expectedHeight) =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.MarginLeftPt = 72;
            document.Page.MarginRightPt = 54;
            document.Page.MarginTopPt = 72;
            document.Page.MarginBottomPt = 60;
            document.Page.HeaderDistancePt = 36;
            document.Page.FooterDistancePt = 30;
            document.PageBordersDoNotSurroundHeader = doNotSurroundHeader;
            document.PageBordersDoNotSurroundFooter = doNotSurroundFooter;
            document.Page.PageBorder = new PageBorder("#A020F0", 2)
            {
                OffsetFrom = PageBorderOffsetFrom.Text,
                SpacePt = 6,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var border = view.BuildPdfContent().Pages.Single().Ops
                .OfType<PdfStrokeRect>()
                .Single();

            border.Should().Be(new PdfStrokeRect(
                65,
                expectedBottom,
                500,
                expectedHeight,
                new PdfColor(0xA0, 0x20, 0xF0),
                2));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_IgnoresHeaderAndFooterExclusionsForPageRelativeBorders() =>
        Session.Dispatch(() =>
        {
            PdfStrokeRect Render(bool exclusionsEnabled)
            {
                var document = TextDocument.CreateEmpty();
                document.Page.WidthPt = 612;
                document.Page.HeightPt = 792;
                document.Page.MarginTopPt = 90;
                document.Page.MarginBottomPt = 90;
                document.Page.HeaderDistancePt = 18;
                document.Page.FooterDistancePt = 18;
                document.PageBordersDoNotSurroundHeader = exclusionsEnabled;
                document.PageBordersDoNotSurroundFooter = exclusionsEnabled;
                document.Page.PageBorder = new PageBorder("#A020F0", 2)
                {
                    OffsetFrom = PageBorderOffsetFrom.Page,
                    SpacePt = 6,
                };

                var view = new DocumentView();
                view.LoadDocument(document);
                return view.BuildPdfContent().Pages.Single().Ops.OfType<PdfStrokeRect>().Single();
            }

            Render(exclusionsEnabled: true).Should().Be(Render(exclusionsEnabled: false));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedWavePageBorderSegments() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 260;
            document.Page.HeightPt = 180;
            document.Page.PageBorder = new PageBorder("#24536B", 3)
            {
                SpacePt = 12,
                LineStyle = BorderLineStyle.Wave,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var lines = view.BuildPdfContent().Pages.Single().Ops
                .TakeWhile(op => op is PdfLine)
                .Cast<PdfLine>()
                .ToArray();
            var opacity = PageBorderWaveVisualPlanner.StrokeOpacity;
            byte Composite(byte channel) => (byte)Math.Round(255 + (channel - 255) * opacity);
            var color = new PdfColor(Composite(0x24), Composite(0x53), Composite(0x6B));

            lines.Should().NotBeEmpty();
            lines[0].Should().Be(new PdfLine(15, 168, 17.25, 165.75, color, 0.75));
            lines[1].Should().Be(new PdfLine(15, 15, 17.25, 12.75, color, 0.75));
            view.BuildPdfContent().Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedApplesPageBorderMotifs() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.ApplesArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var paths = pdf.Pages.Single().Ops.OfType<PdfPath>().ToArray();
            paths.Should().HaveCount(102 * 3);
            paths[0].FillColor.Should().Be(new PdfColor(0xB5, 0, 0));
            paths[0].Contours.Single().Start.Should().Be(new PdfPathPoint(36, 762.72));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            bitmap.Width.Should().BeOneOf(816, 817);
            bitmap.Height.Should().BeOneOf(1056, 1057);
            bitmap.GetPixel(48, 48).Red.Should().BeGreaterThan((byte)150);
            bitmap.GetPixel(48, 48).Green.Should().BeLessThan((byte)30);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedShadowedSquaresPageBorderMotifs() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.ShadowedSquaresArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            var strokes = pdf.Pages.Single().Ops.OfType<PdfStrokeRect>().ToArray();
            fills.Should().HaveCount(102 * 6);
            strokes.Should().BeEmpty();
            fills[0].Should().Be(new PdfFillRect(24, 747, 21, 21, new PdfColor(0, 0, 0x80)));
            fills[1].Should().Be(new PdfFillRect(28.5, 744, 19.5, 19.5, new PdfColor(255, 255, 255)));
            fills[2].Should().Be(new PdfFillRect(27.75, 763.5, 21, 0.75, new PdfColor(0, 0, 0x80)));

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            bitmap.GetPixel(35, 35).Blue.Should().BeGreaterThan((byte)100);
            bitmap.GetPixel(35, 35).Red.Should().BeLessThan((byte)30);
            bitmap.GetPixel(48, 48).Should().Be(SKColors.White);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedShorebirdTracksPageBorderMotifs() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.ShorebirdTracksArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var lines = pdf.Pages.Single().Ops.OfType<PdfLine>().ToArray();
            lines.Should().HaveCount(72 * 4);
            lines[0].Should().Be(new PdfLine(
                54,
                751.125,
                60.75,
                751.125,
                PdfColor.Black,
                0.375));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var footprintInk = 0;
            for (var y = 44; y < 65; y++)
            for (var x = 70; x < 106; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 245)
                    footprintInk++;
            }
            footprintInk.Should().BeGreaterThan(20);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedDecorativeArchPageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.DecorativeArchArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            var paths = pdf.Pages.Single().Ops.OfType<PdfPath>().ToArray();
            fills.Should().HaveCount(25);
            paths.Should().HaveCount(16);
            fills[0].Should().Be(new PdfFillRect(
                36,
                761.25,
                540,
                0.75,
                new PdfColor(0x33, 0x33, 0x33)));
            fills[21].Should().Be(new PdfFillRect(27.75, 744, 15.75, 24, PdfColor.Black));
            paths[0].StrokeWidth.Should().Be(7.5);
            paths[0].StrokeColor.Should().Be(PdfColor.Black);
            paths[0].Contours.Single().Start.Should().Be(new PdfPathPoint(28.5, 745.5));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            bitmap.GetPixel(300, 50).Red.Should().BeLessThan((byte)20);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedHandmade2PageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.Handmade2ArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var paths = pdf.Pages.Single().Ops.OfType<PdfPath>().ToArray();
            pdf.Pages.Single().Ops.OfType<PdfFillRect>().Should().BeEmpty();
            paths.Should().HaveCount(8);
            paths[0].StrokeColor.Should().Be(PdfColor.Black);
            paths[0].StrokeWidth.Should().Be(2.25);
            paths[0].Contours.Single().Start.Should().Be(new PdfPathPoint(27, 764.25));
            paths[4].StrokeColor.Should().Be(PdfColor.Black);
            paths[4].StrokeWidth.Should().Be(1.5);
            paths[4].Contours.Single().Start.Should().Be(new PdfPathPoint(33, 758.25));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var topInk = 0;
            for (var y = 32; y < 52; y++)
            for (var x = 80; x < 736; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 80)
                    topInk++;
            }
            topInk.Should().BeGreaterThan(1_000);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedBatsPageBorderMotifs() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.BatsArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var paths = pdf.Pages.Single().Ops.OfType<PdfPath>().ToArray();
            paths.Should().HaveCount(102);
            paths[0].FillColor.Should().Be(PdfColor.Black);
            paths[0].Contours.Single().Start.Should().Be(new PdfPathPoint(27, 762.75));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var batInk = 0;
            for (var y = 32; y < 64; y++)
            for (var x = 32; x < 64; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                    batInk++;
            }
            batInk.Should().BeGreaterThan(80);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedWeavingRibbonPageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.WeavingRibbonArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            var paths = pdf.Pages.Single().Ops.OfType<PdfPath>().ToArray();
            fills.Should().HaveCount(8972);
            paths.Should().BeEmpty();
            fills[0].Should().Be(new PdfFillRect(24, 744, 564, 24, PdfColor.Black));
            fills[4].Should().Be(new PdfFillRect(50.25, 767.25, 0.75, 0.75, new PdfColor(0xC0, 0xC0, 0xC0)));
            fills[5].Should().Be(new PdfFillRect(51, 767.25, 4.5, 0.75, new PdfColor(255, 255, 255)));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var railInk = 0;
            for (var y = 32; y < 64; y++)
            for (var x = 96; x < 160; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 245)
                    railInk++;
            }
            railInk.Should().BeGreaterThan(600);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedPapyrusPageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.PapyrusArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            var paths = pdf.Pages.Single().Ops.OfType<PdfPath>().ToArray();
            fills.Should().HaveCount(8);
            paths.Should().HaveCount(208);
            fills[0].Should().Be(new PdfFillRect(24, 750, 564, 12.75, PdfColor.Black));
            paths[0].FillColor.Should().Be(new PdfColor(0x7F, 0x7F, 0x7F));
            paths[0].Contours.Single().Start.Should().Be(new PdfPathPoint(51, 756.375));
            paths[1].FillColor.Should().Be(PdfColor.Black);
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var railInk = 0;
            for (var y = 39; y < 56; y++)
            for (var x = 96; x < 160; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 245)
                    railInk++;
            }
            railInk.Should().BeGreaterThan(700);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedVinePageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.VineArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            var paths = pdf.Pages.Single().Ops.OfType<PdfPath>().ToArray();
            fills.Should().HaveCount(3573);
            paths.Should().BeEmpty();
            fills[0].Should().Be(new PdfFillRect(24, 744, 564, 24, PdfColor.Black));
            fills[4].Should().Be(new PdfFillRect(67.5, 764.25, 7.5, 0.75, new PdfColor(255, 255, 255)));
            fills[3313].Should().Be(new PdfFillRect(39, 764.25, 0.75, 0.75, new PdfColor(255, 255, 255)));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var railInk = 0;
            for (var y = 32; y < 64; y++)
            for (var x = 96; x < 160; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 245)
                    railInk++;
            }
            railInk.Should().BeGreaterThan(700);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedMapleMuffinsPageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.MapleMuffinsArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            var paths = pdf.Pages.Single().Ops.OfType<PdfPath>().ToArray();
            paths.Should().BeEmpty();
            fills.Should().HaveCount(41004);
            fills[0].Should().Be(new PdfFillRect(31.5, 766.5, 1.5, 0.75, new PdfColor(0xEF, 0xEF, 0xEF)));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xFE, 0x7F, 0));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xBE, 0x41, 0));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0x14, 0x0A, 0x04));
            fills.Should().NotContain(fill => fill.Color == new PdfColor(0xFF, 0xFF, 0xFF));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var orangeInk = 0;
            for (var y = 32; y < 64; y++)
            for (var x = 32; x < 64; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 180 && pixel.Green is > 35 and < 170 && pixel.Blue < 30)
                    orangeInk++;
            }
            orangeInk.Should().BeGreaterThan(300);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedCakeSlicePageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.CakeSliceArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            pdf.Pages.Single().Ops.OfType<PdfPath>().Should().BeEmpty();
            fills.Should().HaveCount(18972);
            fills[0].Should().Be(new PdfFillRect(39, 767.25, 3, 0.75, PdfColor.Black));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xFF, 0xEE, 0xCA));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xFF, 0x99, 0xC2));
            fills.Should().NotContain(fill => fill.Color == new PdfColor(0xFF, 0xFF, 0xFF));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var coloredInk = 0;
            for (var y = 32; y < 64; y++)
            for (var x = 32; x < 64; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 240 && pixel.Green is > 130 and < 245 && pixel.Blue is > 150 and < 230)
                    coloredInk++;
            }
            coloredInk.Should().BeInRange(295, 305);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedBirdsFlightPageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.BirdsFlightArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            pdf.Pages.Single().Ops.OfType<PdfPath>().Should().BeEmpty();
            fills.Should().HaveCount(10710);
            fills[0].Should().Be(new PdfFillRect(39.75, 766.5, 0.75, 0.75, new PdfColor(0xAE, 0xAF, 0xC6)));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0x04, 0x07, 0x50));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0x62, 0x64, 0x92));
            fills.Should().NotContain(fill => fill.Color == new PdfColor(0xFF, 0xFF, 0xFF));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var navyInk = 0;
            for (var y = 32; y < 64; y++)
            for (var x = 32; x < 64; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Blue < 120 && pixel.Red < 40 && pixel.Green < 40)
                    navyInk++;
            }
            navyInk.Should().BeGreaterThan(280);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedPaintedEggsPageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.PaintedEggsArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            pdf.Pages.Single().Ops.OfType<PdfPath>().Should().BeEmpty();
            fills.Should().HaveCount(23562);
            fills[0].Should().Be(new PdfFillRect(33, 767.25, 0.75, 0.75, new PdfColor(0x55, 0x55, 0x55)));
            fills.Should().Contain(fill => fill.Color == PdfColor.Black);
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xAA, 0xAA, 0xAA));
            fills.Should().NotContain(fill => fill.Color == new PdfColor(255, 255, 255));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var blackInk = 0;
            var whiteEggInterior = 0;
            for (var y = 30; y < 64; y++)
            for (var x = 32; x < 64; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 40 && pixel.Green < 40 && pixel.Blue < 40)
                    blackInk++;
                if (pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245)
                    whiteEggInterior++;
            }
            blackInk.Should().BeInRange(140, 220);
            whiteEggInterior.Should().BeGreaterThan(400);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedCandyCornPageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.CandyCornArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var paths = pdf.Pages.Single().Ops.OfType<PdfPath>().ToArray();
            pdf.Pages.Single().Ops.OfType<PdfFillRect>().Should().BeEmpty();
            paths.Should().HaveCount(1272);
            paths[0].FillColor.Should().Be(PdfColor.Black);
            paths[0].Contours.Single().Start.Should().Be(new PdfPathPoint(42, 767.25));
            paths[1].FillColor.Should().Be(new PdfColor(0xF5, 0xC6, 0x0A));
            paths[2].FillColor.Should().Be(new PdfColor(0xFE, 0x45, 0x01));
            paths[3].FillColor.Should().Be(new PdfColor(255, 255, 255));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var orangeInk = 0;
            var yellowInk = 0;
            for (var y = 32; y < 64; y++)
            for (var x = 48; x < 80; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 220 && pixel.Green < 110 && pixel.Blue < 40)
                    orangeInk++;
                if (pixel.Red > 210 && pixel.Green > 130 && pixel.Blue < 60)
                    yellowInk++;
            }
            orangeInk.Should().BeGreaterThan(100);
            yellowInk.Should().BeGreaterThan(80);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedIceCreamConesPageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.IceCreamConesArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            pdf.Pages.Single().Ops.OfType<PdfPath>().Should().BeEmpty();
            fills.Should().HaveCount(13056);
            fills[0].Should().Be(new PdfFillRect(34.5, 767.25, 0.75, 0.75, new PdfColor(0xEF, 0xEF, 0xEF)));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xFE, 0xFE, 0x7F));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xFC, 0x7F, 0xFC));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0x57, 0x3F, 0x27));
            fills.Should().NotContain(fill => fill.Color == new PdfColor(0xFF, 0xFF, 0xFF));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var brownInk = 0;
            var pinkInk = 0;
            var yellowInk = 0;
            for (var y = 32; y < 64; y++)
            for (var x = 32; x < 64; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red is > 60 and < 130 && pixel.Green < 100 && pixel.Blue < 70)
                    brownInk++;
                if (pixel.Red > 220 && pixel.Green is > 70 and < 180 && pixel.Blue > 220)
                    pinkInk++;
                if (pixel.Red > 220 && pixel.Green > 220 && pixel.Blue is > 70 and < 180)
                    yellowInk++;
            }
            brownInk.Should().BeGreaterThan(50);
            pinkInk.Should().BeGreaterThan(40);
            yellowInk.Should().BeGreaterThan(60);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedPeoplePageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.PeopleArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            pdf.Pages.Single().Ops.OfType<PdfPath>().Should().BeEmpty();
            fills.Should().HaveCount(18462);
            fills[0].Should().Be(new PdfFillRect(33, 765, 0.75, 0.75, new PdfColor(0xEF, 0xEF, 0xEF)));
            fills.Should().Contain(fill => fill.Color == PdfColor.Black);
            fills.Should().Contain(fill => fill.Color == new PdfColor(0x80, 0x80, 0x80));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xFF, 0xFF, 0xFF));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var outlineInk = 0;
            for (var y = 32; y < 64; y++)
            for (var x = 32; x < 64; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 60 && pixel.Green < 60 && pixel.Blue < 60)
                    outlineInk++;
            }
            outlineInk.Should().BeGreaterThan(25);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesSharedFlowersRosesPageBorderPlan() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.PageBorder = new PageBorder("#000000", 3)
            {
                SpacePt = 24,
                ArtId = PageBorderArtVisualPlanner.FlowersRosesArtId,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var fills = pdf.Pages.Single().Ops.OfType<PdfFillRect>().ToArray();
            pdf.Pages.Single().Ops.OfType<PdfPath>().Should().BeEmpty();
            fills.Should().HaveCount(41208);
            fills[0].Should().Be(new PdfFillRect(26.25, 767.25, 0.75, 0.75, new PdfColor(0xB3, 0xB2, 0xB3)));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xE7, 0x69, 0xD1));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0x1A, 0xB3, 0));
            fills.Should().Contain(fill => fill.Color == new PdfColor(0xA8, 0x4D, 0x98));
            pdf.Pages.Single().Ops.Should().NotContain(op => op is PdfStrokeRect);

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var pinkInk = 0;
            var greenInk = 0;
            for (var y = 30; y < 66; y++)
            for (var x = 30; x < 66; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 160 && pixel.Blue > 120 && pixel.Green < 180)
                    pinkInk++;
                if (pixel.Green > 100 && pixel.Red < 80 && pixel.Blue < 80)
                    greenInk++;
            }
            pinkInk.Should().BeGreaterThan(80);
            greenInk.Should().BeGreaterThan(25);
            bitmap.GetPixel(408, 528).Should().Be(SKColors.White);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_DoesNotEmitPageBorderWithoutAuthoredBorder() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            var view = new DocumentView();
            view.LoadDocument(document);

            view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfStrokeRect>()
                .Should().BeEmpty();
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_IncludesTextWatermarkBehindPageBorderOnEveryPage() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 260;
            document.Page.HeightPt = 180;
            document.Page.MarginTopPt = 18;
            document.Page.MarginBottomPt = 18;
            document.Page.PageBorder = new PageBorder("#24536B", 1);
            document.Page.WatermarkOptions = new WatermarkOptions("CONFIDENTIAL")
            {
                FontColorHex = "#7F8A99",
                Layout = WatermarkLayout.Diagonal,
                Opacity = 0.35,
            };
            for (var i = 0; i < 30; i++)
                document.Blocks.Add(new Paragraph($"Watermarked line {i + 1}"));

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            pdf.Pages.Should().HaveCountGreaterThan(1);
            foreach (var page in pdf.Pages)
            {
                var clip = page.Ops[0].Should().BeOfType<PdfClipGroup>().Subject;
                clip.X.Should().Be(0);
                clip.Y.Should().Be(0);
                clip.Width.Should().Be(260);
                clip.Height.Should().Be(180);
                var rotation = clip.Ops.Single().Should().BeOfType<PdfRotationGroup>().Subject;
                rotation.CenterX.Should().Be(130);
                rotation.CenterY.Should().Be(90);
                rotation.RotationDegrees.Should().Be(-45);
                var opacity = rotation.Ops.Single().Should().BeOfType<PdfOpacityGroup>().Subject;
                opacity.Opacity.Should().Be(0.35);
                var text = opacity.Ops.Single().Should().BeOfType<PdfText>().Subject;
                text.Text.Should().Be("CONFIDENTIAL");
                text.Color.Should().Be(new PdfColor(0x7F, 0x8A, 0x99));
                text.FontSize.Should().BeGreaterThan(0);
                page.Ops[^1].Should().BeOfType<PdfStrokeRect>();
                page.Ops.Skip(1).SkipLast(1).Should().Contain(op => op is PdfText);
            }

            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).First());
            CountNonWhitePixels(bitmap).Should().BeGreaterThan(100);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_IncludesPictureWatermarkGeometryRotationAndOpacity() =>
        Session.Dispatch(() =>
        {
            var imageBytes = SolidPng(SKColors.Green);
            using var sourceBitmap = SKBitmap.Decode(imageBytes);
            sourceBitmap.Width.Should().Be(16);
            sourceBitmap.Height.Should().Be(8);
            var document = TextDocument.CreateEmpty();
            document.Page.WidthPt = 612;
            document.Page.HeightPt = 792;
            document.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
            {
                ImageBytes = imageBytes,
                Layout = WatermarkLayout.Diagonal,
                Opacity = 0.4,
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var clip = pdf.Pages.Single().Ops[0]
                .Should().BeOfType<PdfClipGroup>().Subject;
            var image = clip.Ops.Single().Should().BeOfType<PdfImage>().Subject;

            new[] { image.X, image.Y, image.Width, image.Height }
                .Should().BeEquivalentTo([107.1, 296.55, 397.8, 198.9], options => options
                    .Using<double>(context => context.Subject.Should().BeApproximately(context.Expectation, 0.01))
                    .WhenTypeIs<double>());
            image.RotationDegrees.Should().Be(-45);
            image.Opacity.Should().Be(0.4);
            image.ContentType.Should().Be("image/png");
            image.ImageBytes.Should().NotBeEmpty();
            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            using var rendered = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var greenPixels = 0;
            for (var y = 0; y < rendered.Height; y++)
            for (var x = 0; x < rendered.Width; x++)
            {
                var pixel = rendered.GetPixel(x, y);
                if (pixel.Green > pixel.Red + 20 && pixel.Green > pixel.Blue + 20)
                    greenPixels++;
            }
            greenPixels.Should().BeGreaterThan(100);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_SuppressesImportedNativeVmlTextWatermark() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.WatermarkOptions = new WatermarkOptions("STALE")
            {
                NativeVmlTextPathXml = "<v:textpath string=\"STALE\" />",
            };

            var view = new DocumentView();
            view.LoadDocument(document);

            view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfClipGroup>()
                .Should().BeEmpty();
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_IncludesResolvedLineNumbersBeforeBodyText() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.LineNumberMode = LineNumberMode.Continuous;
            document.Page.LineNumberStartAt = 3;
            document.Page.LineNumberCountBy = 2;
            document.Blocks.Add(new Paragraph("First"));
            document.Blocks.Add(new Paragraph("Suppressed")
            {
                Formatting = ParagraphFormatting.Default with
                {
                    SuppressLineNumbers = true,
                    SuppressLineNumbersIsSet = true,
                },
            });
            document.Blocks.Add(new Paragraph("Third"));

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var ops = pdf.Pages.Single().Ops.ToList();
            var lineNumbers = ops.OfType<PdfText>()
                .Where(text => text.Color == new PdfColor(0x60, 0x60, 0x60))
                .ToArray();

            lineNumbers.Select(text => text.Text).Should().Equal("3", "5");
            lineNumbers.Should().OnlyContain(text => text.FontSize == 8 && text.X < document.Page.MarginLeftPt);
            ops.IndexOf(lineNumbers[0]).Should().BeLessThan(
                ops.FindIndex(op => op is PdfText text && text.Text.Contains("First", StringComparison.Ordinal)));
            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            using var bitmap = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var gutterRight = (int)Math.Ceiling(document.Page.MarginLeftPt * 96 / 72);
            var gutterInk = 0;
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < gutterRight; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 180 && pixel.Green < 180 && pixel.Blue < 180)
                    gutterInk++;
            }
            gutterInk.Should().BeGreaterThan(3);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_RestartsLineNumbersOnEachPdfPage() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 260;
            document.Page.HeightPt = 180;
            document.Page.MarginTopPt = 18;
            document.Page.MarginBottomPt = 18;
            document.Page.LineNumberMode = LineNumberMode.RestartEachPage;
            document.Page.LineNumberStartAt = 2;
            document.Page.LineNumberCountBy = 1;
            for (var index = 0; index < 30; index++)
                document.Blocks.Add(new Paragraph($"Numbered line {index + 1}"));

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();

            pdf.Pages.Should().HaveCountGreaterThan(1);
            foreach (var page in pdf.Pages)
            {
                page.Ops.OfType<PdfText>()
                    .Where(text => text.Color == new PdfColor(0x60, 0x60, 0x60))
                    .Select(text => text.Text)
                    .First()
                    .Should().Be("2");
            }
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_RestartsLineNumbersAtAContinuousSectionBoundary() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var firstSectionPage = new PageSettings
            {
                LineNumberMode = LineNumberMode.RestartEachSection,
                LineNumberStartAt = 4,
            };
            document.Page.LineNumberMode = LineNumberMode.RestartEachSection;
            document.Page.LineNumberStartAt = 9;
            document.Blocks.Add(new Paragraph("First section")
            {
                SectionBreak = new Section(firstSectionPage, SectionBreakKind.Continuous),
            });
            document.Blocks.Add(new Paragraph("Second section"));

            var view = new DocumentView();
            view.LoadDocument(document);
            var lineNumbers = view.BuildPdfContent().Pages.Single().Ops.OfType<PdfText>()
                .Where(text => text.Color == new PdfColor(0x60, 0x60, 0x60))
                .Select(text => text.Text);

            lineNumbers.Should().Equal("4", "9");
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_DoesNotEmitLineNumbersWhenDisabled() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Page.LineNumberMode = LineNumberMode.None;
            var view = new DocumentView();
            view.LoadDocument(document);

            view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>()
                .Should().NotContain(text => text.Color == new PdfColor(0x60, 0x60, 0x60));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_IncludesInlineChartWordArtAndSmartArtBeforeBodyText() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();

            var chart = Chart.Create(
                ChartKind.Column,
                ["A", "B", "C"],
                [10.0, 25.0, 15.0],
                "Series 1",
                "Inline PDF Chart");
            chart.WidthPt = 220;
            chart.HeightPt = 120;
            var chartParagraph = new Paragraph();
            chartParagraph.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            document.Blocks.Add(chartParagraph);

            var wordArt = new WordArt("Inline PDF WordArt", WordArtStyle.FillBlue, 26);
            var wordArtParagraph = new Paragraph();
            wordArtParagraph.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wordArt });
            document.Blocks.Add(wordArtParagraph);

            var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Ship", "Review"]);
            smartArt.WidthPt = 260;
            smartArt.HeightPt = 110;
            var smartArtParagraph = new Paragraph();
            smartArtParagraph.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = smartArt });
            document.Blocks.Add(smartArtParagraph);
            document.Blocks.Add(new Paragraph("Tail body text"));

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var ops = pdf.Pages.SelectMany(page => page.Ops).ToList();
            var flattened = FlattenPdfOps(ops).ToList();
            var texts = flattened.OfType<PdfText>().ToArray();
            var chartTitle = texts.Single(text => text.Text == "Inline PDF Chart");
            var planText = texts.Single(text => text.Text == "Plan");
            var shipText = texts.Single(text => text.Text == "Ship");
            var reviewText = texts.Single(text => text.Text == "Review");
            var tailText = texts.Single(text => text.Text.Contains("Tail body text", StringComparison.Ordinal));

            string.Concat(texts.Select(text => text.Text)).Should().Contain("Inline PDF WordArt");
            flattened.IndexOf(chartTitle).Should().BeLessThan(flattened.IndexOf(planText));
            flattened.IndexOf(planText).Should().BeLessThan(flattened.IndexOf(shipText));
            flattened.IndexOf(shipText).Should().BeLessThan(flattened.IndexOf(reviewText));
            flattened.IndexOf(reviewText).Should().BeLessThan(flattened.IndexOf(tailText));
            ops.OfType<PdfFillRect>().Should().Contain(fill => fill.Color == new PdfColor(0xF9, 0xF9, 0xF9));
            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            using var rendered = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            CountNonWhitePixels(rendered).Should().BeGreaterThan(500);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_UsesResolvedLinkedPreviewWithoutEmbeddingItInTheModel() =>
        Session.Dispatch(() =>
        {
            var preview = SolidPng(SKColors.Red);
            var image = new InlineImage([], 72, 36)
            {
                LinkedImageTarget = "linked.png",
                ResolvedLinkedImageBytes = preview
            };
            var document = TextDocument.CreateEmpty();
            document.Paragraphs.Single().Runs.Add(Run.FromImage(image));
            var view = new DocumentView();
            view.LoadDocument(document);

            var imageOp = view.BuildPdfContent().Pages.SelectMany(page => page.Ops)
                .OfType<PdfImage>().Single();

            imageOp.ImageBytes.Should().BeSameAs(preview);
            image.Bytes.Should().BeEmpty();
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
            ops.OfType<PdfEffectGroup>().Should().BeEmpty("pictures without reflection must retain the direct image path");
            imageIndex.Should().BeGreaterThan(0, "table surfaces must remain before the inline image");
            ops.Take(imageIndex).Any(op => op is PdfFillRect or PdfStrokeRect or PdfLine)
                .Should().BeTrue("table surfaces must remain before the inline image");
            ops.Skip(imageIndex + 1).Any(op => op is PdfText)
                .Should().BeTrue("the image pass must precede body text");
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ExportsPictureReflectionBeforeSourceAndBorderThroughSharedEffectGroup() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var image = new InlineImage(SolidPng(SKColors.Red), 72, 36)
            {
                ImportedEffects = new ShapeEffectLst
                {
                    HasReflection = true,
                    ReflectionStartAlpha = 45000,
                    ReflectionStartPosition = 12000,
                    ReflectionEndAlpha = 5000,
                    ReflectionEndPosition = 90000,
                    ReflectionDist = 19050,
                },
                BorderColorHex = "#C00000",
                BorderWidthPt = 2.25,
                RotationAngle = 17,
                FlipH = true,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(image));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var transform = pdf.Pages.Single().Ops.OfType<PdfRotationGroup>().Single();
            transform.RotationDegrees.Should().BeApproximately(17, 0.001);
            transform.FlipH.Should().BeTrue();
            transform.Ops.Should().HaveCount(3);

            var reflection = transform.Ops[0].Should().BeOfType<PdfEffectGroup>().Which;
            reflection.Kind.Should().Be(PdfEffectKind.Reflection);
            reflection.Parameters.Opacity.Should().BeApproximately(0.45, 0.001);
            reflection.Parameters.ReflectionEndOpacity.Should().BeApproximately(0.05, 0.001);
            reflection.Parameters.ReflectionStartPosition.Should().BeApproximately(0.12, 0.001);
            reflection.Parameters.ReflectionEndPosition.Should().BeApproximately(0.9, 0.001);
            reflection.Parameters.ReflectionGap.Should().BeApproximately(1.5, 0.001);
            reflection.Parameters.ReflectionDirectionDegrees.Should().Be(90);
            reflection.Parameters.ReflectionScaleY.Should().Be(-1);

            var reflectedImage = reflection.Ops.Should().ContainSingle().Which.Should().BeOfType<PdfImage>().Which;
            var sourceImage = transform.Ops[1].Should().BeOfType<PdfImage>().Which;
            reflectedImage.Should().BeSameAs(sourceImage);
            sourceImage.RotationDegrees.Should().Be(0);
            transform.Ops[2].Should().BeOfType<PdfStrokeRect>();

            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            SkiaPdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            SkiaPdfWriter.RenderPagesToPng(pdf).Single().Length.Should().BeGreaterThan(100);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_IncludesInlineShapeAndMixedInlineDrawingFamilies() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(new InlineImage(SolidPng(SKColors.Red), 48, 24)));

            var shape = Shape.TextBoxWith("Inline PDF Shape", 150, 60, "#4472C4");
            shape.OutlineColorHex = "#1F4E79";
            shape.OutlineWidthPt = 1.5;
            paragraph.Runs.Add(Run.FromShape(shape));

            var chart = Chart.Create(
                ChartKind.Column,
                ["A", "B"],
                [5.0, 9.0],
                "Series 1",
                "Mixed Inline Chart");
            chart.WidthPt = 180;
            chart.HeightPt = 90;
            paragraph.Runs.Add(new Run(string.Empty) { Chart = chart });
            document.Blocks.Add(paragraph);
            document.Blocks.Add(new Paragraph("Tail after mixed objects"));

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            view.InlineShapeCount.Should().Be(1);
            view.InlineChartCount.Should().Be(1);
            view.InlineShapeRects.Single().Should().Match<(Rect Rect, ShapeKind Kind, string? Text)>(item =>
                item.Kind == ShapeKind.TextBox
                && item.Text == "Inline PDF Shape"
                && item.Rect.Width > 0
                && item.Rect.Height > 0);

            var flattened = FlattenPdfOps(pdf.Pages.SelectMany(page => page.Ops)).ToList();
            var shapeFill = flattened.OfType<PdfFillRect>()
                .Single(fill => fill.Color == new PdfColor(0x44, 0x72, 0xC4)
                    && Math.Abs(fill.Width - 150) < 0.01
                    && Math.Abs(fill.Height - 60) < 0.01);
            var pdfTexts = flattened.OfType<PdfText>().ToList();
            string.Concat(pdfTexts.Select(text => text.Text)).Should().Contain("Inline PDF Shape");
            var shapeText = pdfTexts.First(text => text.Text.Contains("Inline", StringComparison.Ordinal));
            var chartTitle = flattened.OfType<PdfText>().Single(text => text.Text == "Mixed Inline Chart");
            var tail = flattened.OfType<PdfText>()
                .Single(text => text.Text.Contains("Tail after mixed objects", StringComparison.Ordinal));
            flattened.IndexOf(shapeFill).Should().BeLessThan(flattened.IndexOf(shapeText));
            flattened.IndexOf(shapeText).Should().BeLessThan(flattened.IndexOf(chartTitle));
            flattened.IndexOf(chartTitle).Should().BeLessThan(flattened.IndexOf(tail));

            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            using var rendered = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            CountNonWhitePixels(rendered).Should().BeGreaterThan(500);
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

    [Fact]
    public Task BuildPdfContent_IncludesFloatingImagesWithSharedPageGeometryAndLayerOrder() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 240;
            document.Page.HeightPt = 180;
            document.Page.MarginLeftPt = 18;
            document.Page.MarginRightPt = 18;
            document.Page.MarginTopPt = 18;
            document.Page.MarginBottomPt = 18;

            var behind = new InlineImage(SolidPng(SKColors.Red), 36, 24)
            {
                Wrapping = ImageWrapping.Behind,
                HorizontalAnchor = HorizontalAnchor.Page,
                HorizontalOffsetPt = 24,
                VerticalAnchor = VerticalAnchor.Page,
                VerticalOffsetPt = 36,
                CropLeft = 0.10,
                CropTop = 0.05,
                CropRight = 0.15,
                CropBottom = 0.20,
                TransparencyPct = 25,
                RotationAngle = 12,
                ZOrderIndex = 1,
            };
            var front = new InlineImage(SolidPng(SKColors.Blue), 30, 20)
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Page,
                HorizontalOffsetPt = 96,
                VerticalAnchor = VerticalAnchor.Page,
                VerticalOffsetPt = 72,
                ZOrderIndex = 2,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Anchor text"));
            paragraph.Runs.Add(Run.FromImage(behind));
            paragraph.Runs.Add(Run.FromImage(front));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new global::Avalonia.Size(800, 1200));

            var ops = view.BuildPdfContent().Pages.Single().Ops.ToList();
            var images = ops.OfType<PdfImage>().ToList();
            images.Should().HaveCount(2);
            var textIndex = ops.FindIndex(op => op is PdfText text && text.Text.Contains("Anchor", StringComparison.Ordinal));
            var behindOp = images.Single(op => op.ImageBytes.SequenceEqual(behind.Bytes));
            var frontOp = images.Single(op => op.ImageBytes.SequenceEqual(front.Bytes));

            ops.IndexOf(behindOp).Should().BeLessThan(textIndex, "behind-text images must precede body glyphs");
            ops.IndexOf(frontOp).Should().BeGreaterThan(textIndex, "in-front images must follow body glyphs");
            behindOp.X.Should().BeApproximately(24, 0.001);
            behindOp.Y.Should().BeApproximately(120, 0.001);
            behindOp.Width.Should().BeApproximately(36, 0.001);
            behindOp.Height.Should().BeApproximately(24, 0.001);
            behindOp.SourceCrop.Should().Be(new PdfImageSourceCrop(0.10, 0.05, 0.15, 0.20));
            behindOp.Opacity.Should().BeApproximately(0.75, 0.001);
            behindOp.RotationDegrees.Should().BeApproximately(12, 0.001);
            frontOp.X.Should().BeApproximately(96, 0.001);
            frontOp.Y.Should().BeApproximately(88, 0.001);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_PreservesInlineAndFloatingImageFlipsThroughSharedTransforms() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 240;
            document.Page.HeightPt = 180;
            document.Page.MarginLeftPt = 18;
            document.Page.MarginRightPt = 18;
            document.Page.MarginTopPt = 18;
            document.Page.MarginBottomPt = 18;

            var inline = new InlineImage(SplitPng(SKColors.Red, SKColors.Blue), 48, 24)
            {
                FlipH = true,
            };
            var floating = new InlineImage(SplitPng(SKColors.Green, SKColors.Yellow), 36, 24)
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Page,
                HorizontalOffsetPt = 120,
                VerticalAnchor = VerticalAnchor.Page,
                VerticalOffsetPt = 72,
                RotationAngle = 17,
                FlipV = true,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(inline));
            paragraph.Runs.Add(Run.FromImage(floating));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new global::Avalonia.Size(800, 1000));

            var pdf = view.BuildPdfContent();
            var transforms = pdf.Pages.Single().Ops.OfType<PdfRotationGroup>().ToList();
            var inlineTransform = transforms.Single(group => group.FlipH);
            var floatingTransform = transforms.Single(group => group.FlipV);

            inlineTransform.RotationDegrees.Should().Be(0);
            var inlineImage = inlineTransform.Ops.Should().ContainSingle().Which.Should().BeOfType<PdfImage>()
                .Which;
            inlineImage.RotationDegrees.Should().Be(0);
            floatingTransform.RotationDegrees.Should().BeApproximately(17, 0.001);
            floatingTransform.Ops.Should().ContainSingle().Which.Should().BeOfType<PdfImage>()
                .Which.RotationDegrees.Should().Be(0);

            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            SkiaPdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));

            using var rendered = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            var scale = 96.0 / 72.0;
            var sampleY = (int)Math.Round((pdf.Pages[0].HeightPoints
                - (inlineImage.Y + inlineImage.Height / 2.0)) * scale);
            var leftPixel = rendered.GetPixel(
                (int)Math.Round((inlineImage.X + inlineImage.Width * 0.25) * scale),
                sampleY);
            var rightPixel = rendered.GetPixel(
                (int)Math.Round((inlineImage.X + inlineImage.Width * 0.75) * scale),
                sampleY);
            leftPixel.Blue.Should().BeGreaterThan(200, "the blue source half must move to the left");
            leftPixel.Red.Should().BeLessThan(80);
            rightPixel.Red.Should().BeGreaterThan(200, "the red source half must move to the right");
            rightPixel.Blue.Should().BeLessThan(80);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_PreservesPictureBorderColorWidthDashAndTransform() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var image = new InlineImage(SolidPng(SKColors.White), 72, 36)
            {
                BorderColorHex = "#C00000",
                BorderWidthPt = 2.25,
                BorderDash = "lgDashDot",
                RotationAngle = 23,
                FlipH = true,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(image));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);

            var pdf = view.BuildPdfContent();
            var transform = pdf.Pages.Single().Ops.OfType<PdfRotationGroup>().Single();
            transform.RotationDegrees.Should().BeApproximately(23, 0.001);
            transform.FlipH.Should().BeTrue();
            transform.Ops.OfType<PdfImage>().Should().ContainSingle()
                .Which.RotationDegrees.Should().Be(0);
            var border = transform.Ops.OfType<PdfStrokeRect>().Should().ContainSingle().Which;
            border.Color.Should().Be(new PdfColor(0xC0, 0x00, 0x00));
            border.LineWidth.Should().BeApproximately(2.25, 0.001);
            border.Dash!.Segments.Should().Equal(8, 2, 1, 2);

            PortablePdfWriter.WriteToBytes(pdf).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            using var rendered = SKBitmap.Decode(SkiaPdfWriter.RenderPagesToPng(pdf, dpi: 96).Single());
            rendered.Pixels.Count(pixel => pixel.Red > 140 && pixel.Green < 80 && pixel.Blue < 80)
                .Should().BeGreaterThan(50, "the authored red picture border must reach the rendered page");
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_IncludesFloatingShapesAsVectorGeometryTextAndMergedLayering() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 240;
            document.Page.HeightPt = 180;
            document.Page.MarginLeftPt = 18;
            document.Page.MarginRightPt = 18;
            document.Page.MarginTopPt = 18;
            document.Page.MarginBottomPt = 18;

            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("Body text between drawing layers"));
            var behind = new Shape(ShapeKind.Ellipse, 48, 36, "#FF0000")
            {
                OutlineColorHex = "#000000",
                OutlineWidthPt = 1.5,
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Behind,
                    HorizontalAnchor = HorizontalAnchor.Page,
                    HorizontalOffsetPt = 24,
                    VerticalAnchor = VerticalAnchor.Page,
                    VerticalOffsetPt = 30,
                    ZOrderIndex = 1,
                },
            };
            var front = Shape.TextBoxWith("Vector shape text", 72, 36, "#00AA00");
            front.OutlineColorHex = "#0000FF";
            front.OutlineWidthPt = 2;
            front.Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Page,
                HorizontalOffsetPt = 96,
                VerticalAnchor = VerticalAnchor.Page,
                VerticalOffsetPt = 42,
                ZOrderIndex = 3,
            };
            paragraph.Runs.Add(Run.FromShape(behind));
            paragraph.Runs.Add(Run.FromShape(front));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new global::Avalonia.Size(800, 1200));

            var ops = view.BuildPdfContent().Pages.Single().Ops.ToList();
            var bodyIndex = ops.FindIndex(op => op is PdfText text
                && text.Text.Contains("Body text", StringComparison.Ordinal));
            var shapeTextIndex = ops.FindIndex(op => op is PdfText text
                && text.Text.Contains("Vector", StringComparison.Ordinal));

            var ellipse = ops.OfType<PdfFillEllipse>().Single(op => op.Color == new PdfColor(255, 0, 0));
            var ellipseOutline = ops.OfType<PdfStrokeEllipse>().Single(op => op.Color == PdfColor.Black);
            var rectangle = ops.OfType<PdfFillRect>().Single(op => op.Color == new PdfColor(0, 170, 0));
            var rectangleOutline = ops.OfType<PdfStrokeRect>().Single(op => op.Color == new PdfColor(0, 0, 255));

            ellipse.X.Should().BeApproximately(24, 0.001);
            ellipse.Y.Should().BeApproximately(114, 0.001);
            ellipse.Width.Should().BeApproximately(48, 0.001);
            ellipse.Height.Should().BeApproximately(36, 0.001);
            ellipseOutline.LineWidth.Should().BeApproximately(1.5, 0.001);
            rectangle.X.Should().BeApproximately(96, 0.001);
            rectangle.Y.Should().BeApproximately(102, 0.001);
            rectangle.Width.Should().BeApproximately(72, 0.001);
            rectangle.Height.Should().BeApproximately(36, 0.001);
            rectangleOutline.LineWidth.Should().BeApproximately(2, 0.001);
            ops.IndexOf(ellipse).Should().BeLessThan(bodyIndex, "behind shapes must precede body glyphs");
            ops.IndexOf(rectangle).Should().BeGreaterThan(bodyIndex, "in-front shapes must follow body glyphs");
            shapeTextIndex.Should().BeGreaterThan(
                ops.IndexOf(rectangle),
                "shape text should be emitted after the front shape; ops={0}",
                string.Join(",", ops.Select(op => op is PdfText text ? $"text:{text.Text}" : op.GetType().Name)));

            var pdfBytes = SkiaPdfWriter.WriteToBytes(view.BuildPdfContent());
            pdfBytes.Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_ExportsShapeAndWordArtEffectsThroughSharedGroups() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 300;
            document.Page.HeightPt = 220;

            var shape = Shape.TextBoxWith("Effects", 96, 48, "#4472C4");
            shape.Effects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowColorHex = "202020",
                ShadowAlpha = 55000,
                ShadowBlurRad = 76200,
                ShadowDist = 38100,
                ShadowDir = 2700000,
                HasGlow = true,
                GlowColorHex = "FF8000",
                GlowAlpha = 65000,
                GlowRad = 63500,
                HasSoftEdge = true,
                SoftEdgeRad = 38100,
                HasReflection = true,
                ReflectionBlurRad = 25400,
                ReflectionStartAlpha = 45000,
                ReflectionStartPosition = 12000,
                ReflectionEndAlpha = 5000,
                ReflectionEndPosition = 90000,
                ReflectionDist = 19050,
                ReflectionDir = 5400000,
                ReflectionFadeDir = 3000000,
                ReflectionScaleX = 85000,
                ReflectionScaleY = -95000,
                ReflectionSkewX = 600000,
                ReflectionSkewY = -300000,
                HasBevel = true,
                BevelW = 38100,
                BevelH = 50800,
            };
            shape.Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Page,
                HorizontalOffsetPt = 24,
                VerticalAnchor = VerticalAnchor.Page,
                VerticalOffsetPt = 32,
            };

            var groupedWordArt = new WordArt("Glow", WordArtStyle.GlowGold, 28);
            var group = new DrawingGroup { WidthPt = 160, HeightPt = 80 };
            group.Children.Add(groupedWordArt);
            group.ChildOffsets.Add((12, 12));

            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromShape(shape));
            paragraph.Runs.Add(Run.FromDrawingGroup(group));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new global::Avalonia.Size(900, 1200));

            static IEnumerable<PdfDrawOp> Descendants(IEnumerable<PdfDrawOp> ops)
            {
                foreach (var op in ops)
                {
                    yield return op;
                    switch (op)
                    {
                        case PdfRotationGroup rotation:
                            foreach (var child in Descendants(rotation.Ops))
                                yield return child;
                            break;
                        case PdfClipGroup clip:
                            foreach (var child in Descendants(clip.Ops))
                                yield return child;
                            break;
                        case PdfEffectGroup effect:
                            foreach (var child in Descendants(effect.Ops))
                                yield return child;
                            break;
                    }
                }
            }

            var operations = Descendants(view.BuildPdfContent().Pages.SelectMany(page => page.Ops)).ToArray();
            operations.OfType<PdfEffectGroup>().Select(effect => effect.Kind)
                .Should().Contain([PdfEffectKind.Shadow, PdfEffectKind.Glow, PdfEffectKind.SoftEdge,
                    PdfEffectKind.Reflection, PdfEffectKind.Bevel]);
            operations.OfType<PdfEffectGroup>().Should().Contain(effect => effect.Parameters.Opacity > 0);
            var bevel = operations.OfType<PdfEffectGroup>().Single(effect => effect.Kind == PdfEffectKind.Bevel);
            bevel.Parameters.BevelWidth.Should().BeApproximately(3, 0.001);
            bevel.Parameters.BevelHeight.Should().BeApproximately(4, 0.001);
            PdfRenderGeometry.GetBevelBands(bevel).Should().HaveCount(8);
            var reflection = operations.OfType<PdfEffectGroup>().Single(effect => effect.Kind == PdfEffectKind.Reflection);
            reflection.Parameters.Radius.Should().BeApproximately(2, 0.001);
            reflection.Parameters.ReflectionEndOpacity.Should().BeApproximately(0.05, 0.001);
            reflection.Parameters.ReflectionStartPosition.Should().BeApproximately(0.12, 0.001);
            reflection.Parameters.ReflectionEndPosition.Should().BeApproximately(0.9, 0.001);
            reflection.Parameters.ReflectionFadeDirectionDegrees.Should().BeApproximately(50, 0.001);
            reflection.Parameters.ReflectionScaleX.Should().BeApproximately(0.85, 0.001);
            reflection.Parameters.ReflectionScaleY.Should().BeApproximately(-0.95, 0.001);
            reflection.Parameters.ReflectionSkewXDegrees.Should().BeApproximately(10, 0.001);
            reflection.Parameters.ReflectionSkewYDegrees.Should().BeApproximately(-5, 0.001);
            operations.OfType<PdfText>().Select(text => text.Text).Should().Contain(text => text.Contains("Effects", StringComparison.Ordinal));
            string.Concat(operations.OfType<PdfText>().Select(text => text.Text))
                .Should().Contain("Glow");

            var content = view.BuildPdfContent();
            PortablePdfWriter.WriteToBytes(content).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            SkiaPdfWriter.WriteToBytes(content).Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            SkiaPdfWriter.RenderPagesToPng(content).Single().Length.Should().BeGreaterThan(100);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_PreservesFloatingShapeFlipsAndDashStyle() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 240;
            document.Page.HeightPt = 180;

            var shape = Shape.TextBoxWith("Flipped dashed shape", 72, 36, "#00AA00");
            shape.OutlineColorHex = "#0000FF";
            shape.OutlineWidthPt = 2;
            shape.OutlineDash = "dashDot";
            shape.RotationAngle = 17;
            shape.FlipH = true;
            shape.FlipV = true;
            shape.Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Page,
                HorizontalOffsetPt = 48,
                VerticalAnchor = VerticalAnchor.Page,
                VerticalOffsetPt = 42,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromShape(shape));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new global::Avalonia.Size(800, 1200));

            var group = view.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfRotationGroup>()
                .Single();
            group.FlipH.Should().BeTrue();
            group.FlipV.Should().BeTrue();
            group.RotationDegrees.Should().BeApproximately(17, 0.001);

            var outline = group.Ops.OfType<PdfStrokeRect>().Single();
            outline.Dash.Should().NotBeNull();
            outline.Dash!.Segments.Should().Equal(4, 2, 1, 2);
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_PreservesFloatingShapePatternForegroundBackgroundOutlineAndText() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 240;
            document.Page.HeightPt = 180;

            var shape = Shape.TextBoxWith("Pattern text", 84, 42, "#FFFFFF");
            shape.ExtendedFill = ShapeFill.Patterned("pct50", "#C00000", "#FFFFFF");
            shape.OutlineColorHex = "#000000";
            shape.OutlineWidthPt = 1.5;
            shape.OutlineDash = "dash";
            shape.RotationAngle = 21;
            shape.Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Page,
                HorizontalOffsetPt = 48,
                VerticalAnchor = VerticalAnchor.Page,
                VerticalOffsetPt = 42,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromShape(shape));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new global::Avalonia.Size(800, 1200));

            var group = view.BuildPdfContent().Pages
                .SelectMany(page => page.Ops)
                .OfType<PdfRotationGroup>()
                .Single();
            var fill = group.Ops.OfType<PdfFillRectPattern>().Single();
            var outline = group.Ops.OfType<PdfStrokeRect>().Single();

            fill.Pattern.Kind.Should().Be(PdfPatternKind.DownDiagonal);
            fill.Pattern.Foreground.Should().Be(new PdfColor(0xC0, 0x00, 0x00));
            fill.Pattern.Background.Should().Be(new PdfColor(0xFF, 0xFF, 0xFF));
            fill.Pattern.TileWidth.Should().BeApproximately(8 / (96.0 / 72.0), 0.001);
            outline.Dash!.Segments.Should().Equal(4, 3);
            string.Concat(group.Ops.OfType<PdfText>().Select(op => op.Text)).Should().Contain("Pattern text");

            var pdfBytes = SkiaPdfWriter.WriteToBytes(view.BuildPdfContent());
            pdfBytes.Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_RecursivelyExportsNestedGroupChildrenWithTransformsBoundsAndOrder() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 300;
            document.Page.HeightPt = 220;

            var inner = new DrawingGroup
            {
                WidthPt = 84,
                HeightPt = 52,
                RotationAngle = 19,
                FlipH = true,
            };
            var patternedEllipse = new Shape(ShapeKind.Ellipse, 36, 24, "#FFFFFF")
            {
                OutlineColorHex = "#000000",
                OutlineWidthPt = 1.25,
                OutlineDash = "dash",
            };
            patternedEllipse.ExtendedFill = ShapeFill.Patterned("pct50", "#C00000", "#FFFFFF");
            var innerText = Shape.TextBoxWith("Nested", 42, 24, "#00AA00");
            innerText.OutlineColorHex = "#0000FF";
            innerText.OutlineWidthPt = 1.5;
            inner.Children.Add(patternedEllipse);
            inner.ChildOffsets.Add((4, 6));
            inner.Children.Add(innerText);
            inner.ChildOffsets.Add((40, 24));

            var outer = new DrawingGroup
            {
                WidthPt = 180,
                HeightPt = 100,
                RotationAngle = 27,
                FlipV = true,
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.InFront,
                    HorizontalAnchor = HorizontalAnchor.Page,
                    HorizontalOffsetPt = 30,
                    VerticalAnchor = VerticalAnchor.Page,
                    VerticalOffsetPt = 40,
                    ZOrderIndex = 7,
                },
            };
            outer.Children.Add(inner);
            outer.ChildOffsets.Add((20, 15));
            var frontShape = Shape.TextBoxWith("Front child", 54, 28, "#4472C4");
            frontShape.RotationAngle = -11;
            frontShape.FlipH = true;
            frontShape.OutlineColorHex = "#ED7D31";
            frontShape.OutlineWidthPt = 2;
            outer.Children.Add(frontShape);
            outer.ChildOffsets.Add((112, 62));

            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromDrawingGroup(outer));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new global::Avalonia.Size(900, 1200));

            var pageOps = view.BuildPdfContent().Pages.Single().Ops;
            var outerTransform = pageOps.OfType<PdfRotationGroup>().Single();
            outerTransform.RotationDegrees.Should().BeApproximately(27, 0.001);
            outerTransform.FlipV.Should().BeTrue();
            var outerClip = outerTransform.Ops.OfType<PdfClipGroup>().Single();
            outerClip.Width.Should().BeApproximately(180, 0.001);
            outerClip.Height.Should().BeApproximately(100, 0.001);
            outerClip.Ops.Should().HaveCount(2, "group list order must remain the PDF z-order");

            var innerTransform = outerClip.Ops[0].Should().BeOfType<PdfRotationGroup>().Subject;
            innerTransform.RotationDegrees.Should().BeApproximately(19, 0.001);
            innerTransform.FlipH.Should().BeTrue();
            var innerClip = innerTransform.Ops.OfType<PdfClipGroup>().Single();
            innerClip.Width.Should().BeApproximately(84, 0.001);
            innerClip.Height.Should().BeApproximately(52, 0.001);
            innerClip.Ops.OfType<PdfFillEllipsePattern>().Should().ContainSingle();
            innerClip.Ops.OfType<PdfStrokeEllipse>().Single().Dash!.Segments.Should().Equal(4, 3);
            string.Concat(innerClip.Ops.OfType<PdfText>().Select(op => op.Text)).Should().Contain("Nested");

            var frontOps = outerClip.Ops.Skip(1).ToArray();
            frontOps.OfType<PdfRotationGroup>().Should().ContainSingle();
            var frontTransform = frontOps.OfType<PdfRotationGroup>().Single();
            frontTransform.RotationDegrees.Should().BeApproximately(-11, 0.001);
            frontTransform.FlipH.Should().BeTrue();
            string.Concat(frontTransform.Ops.OfType<PdfText>().Select(op => op.Text)).Should().Contain("Front child");

            var pdfBytes = SkiaPdfWriter.WriteToBytes(view.BuildPdfContent());
            pdfBytes.Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_RecursivelyExportsGroupedChartSmartArtAndWordArtVectorPlans() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 320;
            document.Page.HeightPt = 240;

            var chart = Chart.Create(
                ChartKind.Line,
                ["Q1", "Q2", "Q3"],
                [2.0, 5.0, 3.0],
                seriesName: "Revenue",
                title: "Grouped chart");
            chart.WidthPt = 118;
            chart.HeightPt = 72;
            chart.ShowLegend = true;
            chart.QuickLayoutId = 5;

            var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Ship", "Review"]);
            smartArt.WidthPt = 126;
            smartArt.HeightPt = 68;
            smartArt.LayoutId = "process1";
            smartArt.StyleId = "moderate1";

            var wordArt = new WordArt("Grouped WordArt", WordArtStyle.GlowGold, 24)
            {
                Warp = WordArtWarp.Wave1,
                RotationAngle = 9,
            };

            var inner = new DrawingGroup
            {
                WidthPt = 220,
                HeightPt = 150,
                RotationAngle = 16,
                FlipH = true,
            };
            inner.Children.Add(chart);
            inner.ChildOffsets.Add((8, 6));
            inner.Children.Add(smartArt);
            inner.ChildOffsets.Add((112, 8));
            inner.Children.Add(wordArt);
            inner.ChildOffsets.Add((34, 86));

            var outer = new DrawingGroup
            {
                WidthPt = 260,
                HeightPt = 180,
                RotationAngle = -12,
                FlipV = true,
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.InFront,
                    HorizontalAnchor = HorizontalAnchor.Page,
                    HorizontalOffsetPt = 26,
                    VerticalAnchor = VerticalAnchor.Page,
                    VerticalOffsetPt = 30,
                    ZOrderIndex = 4,
                },
            };
            outer.Children.Add(inner);
            outer.ChildOffsets.Add((18, 12));

            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromDrawingGroup(outer));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new global::Avalonia.Size(900, 1200));

            var pageOps = view.BuildPdfContent().Pages.Single().Ops;
            var outerTransform = pageOps.OfType<PdfRotationGroup>().Single();
            outerTransform.RotationDegrees.Should().BeApproximately(-12, 0.001);
            outerTransform.FlipV.Should().BeTrue();
            var outerClip = outerTransform.Ops.OfType<PdfClipGroup>().Single();
            var innerTransform = outerClip.Ops.OfType<PdfRotationGroup>().Single();
            innerTransform.RotationDegrees.Should().BeApproximately(16, 0.001);
            innerTransform.FlipH.Should().BeTrue();
            var innerClip = innerTransform.Ops.OfType<PdfClipGroup>().Single();
            static IEnumerable<PdfDrawOp> Descendants(IEnumerable<PdfDrawOp> ops)
            {
                foreach (var op in ops)
                {
                    yield return op;
                    if (op is PdfRotationGroup rotation)
                    {
                        foreach (var child in Descendants(rotation.Ops))
                            yield return child;
                    }
                    else if (op is PdfClipGroup clip)
                    {
                        foreach (var child in Descendants(clip.Ops))
                            yield return child;
                    }
                }
            }

            var childOps = Descendants(innerClip.Ops).ToArray();
            childOps.OfType<PdfLine>().Should().NotBeEmpty("the chart scene must remain vector geometry");
            childOps.OfType<PdfText>().Select(op => op.Text)
                .Should().Contain("Grouped chart");
            string.Concat(childOps.OfType<PdfText>().Select(op => op.Text))
                .Should().Contain("Grouped WordArt");
            childOps.OfType<PdfText>().Select(op => op.Text)
                .Should().Contain("Plan");
            childOps.OfType<PdfPath>().Should().NotBeEmpty("SmartArt nodes must remain vector paths");
            childOps.OfType<PdfRotationGroup>().Any(group => Math.Abs(group.RotationDegrees - 9) < 0.001)
                .Should().BeTrue();

            var pdfBytes = SkiaPdfWriter.WriteToBytes(view.BuildPdfContent());
            pdfBytes.Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
            var portableBytes = PortablePdfWriter.WriteToBytes(view.BuildPdfContent());
            portableBytes.Should().StartWith(Encoding.ASCII.GetBytes("%PDF-"));
        }, CancellationToken.None);

    [Fact]
    public Task BuildPdfContent_RendersFloatingImagesAtTheirPageSpacePixels() =>
        Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Page.WidthPt = 200;
            document.Page.HeightPt = 160;
            document.Page.MarginLeftPt = 12;
            document.Page.MarginRightPt = 12;
            document.Page.MarginTopPt = 12;
            document.Page.MarginBottomPt = 12;

            var image = new InlineImage(SolidPng(SKColors.Yellow), 40, 30)
            {
                Wrapping = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Page,
                HorizontalOffsetPt = 20,
                VerticalAnchor = VerticalAnchor.Page,
                VerticalOffsetPt = 30,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(image));
            document.Blocks.Add(paragraph);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new global::Avalonia.Size(800, 1000));

            var png = SkiaPdfWriter.RenderPagesToPng(view.BuildPdfContent(), dpi: 96).Single();
            using var bitmap = SKBitmap.Decode(png);
            var scale = 96.0 / 72.0;
            var greenPixels = new List<(int X, int Y)>();
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 180 && pixel.Green > 180 && pixel.Blue < 180)
                    greenPixels.Add((x, y));
            }

            greenPixels.Should().NotBeEmpty("the floating image must be present in the rendered PDF page");
            var centerX = greenPixels.Average(pixel => pixel.X);
            var centerY = greenPixels.Average(pixel => pixel.Y);
            centerX.Should().BeApproximately((20 + 40 / 2.0) * scale, 2.0);
            centerY.Should().BeApproximately((30 + 30 / 2.0) * scale, 2.0);
        }, CancellationToken.None);

    private static byte[] SolidPng(SKColor color)
    {
        using var bitmap = new SKBitmap(16, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] SplitPng(SKColor left, SKColor right)
    {
        using var bitmap = new SKBitmap(16, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(left);
        using var paint = new SKPaint { Color = right };
        canvas.DrawRect(8, 0, 8, 8, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static HeaderFooter ImageHeader(byte[] bytes, TextAlignment alignment)
    {
        var header = new HeaderFooter();
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { Alignment = alignment },
        };
        paragraph.Runs.Add(Run.FromImage(new InlineImage(bytes, 30, 12)
        {
            Wrapping = ImageWrapping.Inline,
        }));
        header.Paragraphs.Add(paragraph);
        return header;
    }

    private static int CountNonWhitePixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.Red < 250 || pixel.Green < 250 || pixel.Blue < 250)
                count++;
        }

        return count;
    }

    private static IEnumerable<PdfDrawOp> FlattenPdfOps(IEnumerable<PdfDrawOp> ops)
    {
        foreach (var op in ops)
        {
            yield return op;
            IReadOnlyList<PdfDrawOp>? children = op switch
            {
                PdfRotationGroup rotation => rotation.Ops,
                PdfClipGroup clip => clip.Ops,
                PdfOpacityGroup opacity => opacity.Ops,
                PdfEffectGroup effect => effect.Ops,
                _ => null,
            };
            if (children is null)
                continue;
            foreach (var child in FlattenPdfOps(children))
                yield return child;
        }
    }
}
