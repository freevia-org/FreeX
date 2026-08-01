using System.Text;
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
    public Task BuildPdfContent_IncludesPageBorderOnEveryPageBeforeDocumentContent() =>
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
                var border = page.Ops[0].Should().BeOfType<PdfStrokeRect>().Subject;
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
                page.Ops[1].Should().BeOfType<PdfStrokeRect>();
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
