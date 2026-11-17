using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PrintRendererShapeTests
{
    [Fact]
    public void DrawPrintedShapes_NoWrapClipsInsteadOfAddingLinesOrEllipsis()
    {
        StaTestRunner.Run(() =>
        {
            var model = new DrawingShapeModel { HasFill = false, OutlineHasNoFill = true };
            var bounds = new LayoutRect(10, 10, 45, 65);
            var block = new PageShapeBlock(model.Id, bounds, ShapeGeometryBuilder.Build(model.Kind, bounds),
                DrawingObjectRenderMetadataPlanner.ResolveDrawingShapeRenderMetadata(model, new Workbook("test").Theme),
                "One two three four five", new PageTextFont("Segoe UI", 12, false, false, new PresentationRgb(0, 0, 0)),
                DrawingShapeTextHAlign.Left, DrawingShapeTextVAnchor.Top, false)
            { BodyClip = new LayoutRect(0, 0, 100, 90) };

            var noWrap = Render(block);
            var wrap = Render(block with { TextWrap = true });
            noWrap.Should().Contain(pixel => pixel.Y >= 10 && pixel.Y < 28);
            noWrap.Should().NotContain(pixel => pixel.Y >= 30, "a no-wrap line must be clipped rather than broken into additional lines");
            wrap.Should().Contain(pixel => pixel.Y >= 30, "wrapped text does create further lines");
            noWrap.Should().OnlyContain(pixel => pixel.X >= 10 && pixel.X < 55 && pixel.Y >= 10 && pixel.Y < 75);

            var explicitLines = Render(block with { Text = "One\nTwo" });
            explicitLines.Should().Contain(pixel => pixel.Y >= 25, "explicit line breaks remain effective without word wrapping");

            var rightAligned = block with { TextHorizontalAlignment = DrawingShapeTextHAlign.Right };
            var longSuffix = Render(rightAligned with { Text = "iiiiiiiiWWWW" }).Where(pixel => pixel.X >= 40).ToHashSet();
            var shortSuffix = Render(rightAligned with { Text = "WWWW" }).Where(pixel => pixel.X >= 40).ToHashSet();
            longSuffix.Should().NotBeEmpty();
            longSuffix.Intersect(shortSuffix).Count().Should().BeGreaterThan((int)(shortSuffix.Count * 0.85),
                "right alignment must retain the end of an overflowing line");

            // Excel permits unwrapped ink in the inset margins and clips at the shape border.
            var asymmetric = block with { Text = "MMMMMMMMMM", TextInsets = new PageShapeTextInsets(15, 10, 0, 0) };
            var leftInk = Render(asymmetric);
            leftInk.Should().Contain(pixel => pixel.X >= 45 && pixel.X < 55, "left-aligned overflow may cross the right inset");
            var rightInk = Render(asymmetric with { TextHorizontalAlignment = DrawingShapeTextHAlign.Right });
            rightInk.Should().Contain(pixel => pixel.X >= 10 && pixel.X < 25, "right-aligned overflow may cross the left inset");
            leftInk.Concat(rightInk).Should().OnlyContain(pixel => pixel.X >= 10 && pixel.X < 55);

            var rtlInk = Render(block with { Text = "مرحبا مرحبا مرحبا مرحبا", TextHorizontalAlignment = DrawingShapeTextHAlign.Right });
            rtlInk.Should().NotBeEmpty();
            rtlInk.Should().NotContain(pixel => pixel.Y >= 30, "RTL glyphs must also remain on one clipped line");
            rtlInk.Should().OnlyContain(pixel => pixel.X >= 10 && pixel.X < 55);
        });

        static (int X, int Y)[] Render(PageShapeBlock block)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                typeof(PrintRenderer).GetMethod("DrawPrintedShapes", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [dc, new List<PdfTextOverlay>(), new[] { block }, false]);
            var bitmap = new RenderTargetBitmap(100, 90, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var bytes = new byte[100 * 90 * 4];
            bitmap.CopyPixels(bytes, 100 * 4, 0);
            return Enumerable.Range(0, 100 * 90).Where(i => bytes[i * 4 + 3] != 0)
                .Select(i => (i % 100, i / 100)).ToArray();
        }
    }

    [Fact]
    public void DrawPrintedShapes_RespectsInsetsAndPositionsSelectableText()
    {
        StaTestRunner.Run(() =>
        {
            var model = new DrawingShapeModel();
            var bounds = new LayoutRect(10, 10, 160, 80);
            var block = new PageShapeBlock(model.Id, bounds, ShapeGeometryBuilder.Build(model.Kind, bounds),
                DrawingObjectRenderMetadataPlanner.ResolveDrawingShapeRenderMetadata(model, new Workbook("test").Theme),
                "Label", new PageTextFont("Segoe UI", 12, false, false, new PresentationRgb(0, 0, 0)),
                DrawingShapeTextHAlign.Left, DrawingShapeTextVAnchor.Top, true)
            { BodyClip = new LayoutRect(0, 0, 200, 120) };

            var original = RenderTextOverlay(block);
            var zero = RenderTextOverlay(block with { TextInsets = new PageShapeTextInsets(0, 0, 0, 0) });
            var inset = RenderTextOverlay(block with { TextInsets = new PageShapeTextInsets(20, 5, 12, 3) });
            original.X.Should().Be(zero.X);
            original.Y.Should().Be(zero.Y);
            inset.X.Should().BeApproximately(zero.X + 20, 0.01);
            inset.Y.Should().BeApproximately(zero.Y + 12, 0.01);
            var aligned = block with { TextHorizontalAlignment = DrawingShapeTextHAlign.Right,
                TextVerticalAlignment = DrawingShapeTextVAnchor.Bottom };
            var originalAligned = RenderTextOverlay(aligned);
            var insetAligned = RenderTextOverlay(aligned with { TextInsets = new PageShapeTextInsets(20, 5, 12, 3) });
            insetAligned.X.Should().BeApproximately(originalAligned.X - 5, 0.01);
            insetAligned.Y.Should().BeApproximately(originalAligned.Y - 3, 0.01);
        });
    }

    private static PdfTextOverlay RenderTextOverlay(PageShapeBlock block)
    {
        var overlays = new List<PdfTextOverlay>();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            typeof(PrintRenderer).GetMethod("DrawPrintedShapes", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [dc, overlays, new[] { block }, false]);
        return overlays.Should().ContainSingle().Subject;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawPrintedShapes_PaintsArrowheadsWithOutlineInk(bool blackAndWhite)
    {
        StaTestRunner.Run(() =>
        {
            var model = new DrawingShapeModel { Kind = DrawingShapeKind.Line, OutlineColor = new CellColor(0, 0, 255) };
            var bounds = new LayoutRect(5, 10, 30, 0);
            var block = new PageShapeBlock(model.Id, bounds, ShapeGeometryBuilder.Build(model.Kind, bounds),
                DrawingObjectRenderMetadataPlanner.ResolveDrawingShapeRenderMetadata(model, new Workbook("test").Theme),
                null, new PageTextFont("Segoe UI", 12, false, false, new PresentationRgb(0, 0, 0)),
                DrawingShapeTextHAlign.Left, DrawingShapeTextVAnchor.Top, true)
            {
                BodyClip = new LayoutRect(0, 0, 40, 40),
                Arrowheads = [ShapeGeometryBuilder.Build(DrawingShapeKind.Triangle, new LayoutRect(20, 20, 10, 10))]
            };
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                typeof(PrintRenderer).GetMethod("DrawPrintedShapes", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [dc, new List<PdfTextOverlay>(), new[] { block }, blackAndWhite]);
            var bitmap = new RenderTargetBitmap(40, 40, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var bytes = new byte[40 * 40 * 4];
            bitmap.CopyPixels(bytes, 40 * 4, 0);
            var pixel = ((27 * 40) + 25) * 4;
            bytes[pixel + 3].Should().Be(255);
            bytes[pixel].Should().Be(blackAndWhite ? (byte)0 : (byte)255);
        });
    }

    [Fact]
    public void RenderWorksheet_PassesBlackAndWhiteSettingToShapes()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Black and white shapes");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page"));
            sheet.PrintBlackAndWhite = true;
            sheet.DrawingShapes.Add(new DrawingShapeModel
            {
                Anchor = new CellAddress(sheet.Id, 1, 1), Width = 40, Height = 30,
                FillColor = new CellColor(255, 0, 0), OutlineHasNoFill = true
            });
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].Child;
            page.Measure(new Size(page.Width, page.Height));
            page.Arrange(new Rect(0, 0, page.Width, page.Height));
            page.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.Width), (int)Math.Ceiling(page.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(page);
            var stride = bitmap.PixelWidth * 4;
            var bytes = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(bytes, stride, 0);
            Enumerable.Range(0, bytes.Length / 4).Should().NotContain(i => bytes[i * 4 + 2] > 220 && bytes[i * 4 + 1] < 30 && bytes[i * 4] < 30);
        });
    }

    [Fact]
    public void DrawPrintedShapes_BlackAndWhiteSuppressesFillAndUsesBlackInkAndOverlay()
    {
        StaTestRunner.Run(() =>
        {
            var model = new DrawingShapeModel
            {
                FillColor = new CellColor(255, 0, 0), GradientFillEndColor = new CellColor(0, 0, 255),
                OutlineColor = new CellColor(0, 0, 255), OutlineWidthPoints = 2, OutlineDash = DrawingShapeOutlineDash.Dash
            };
            var bounds = new LayoutRect(2, 2, 76, 36);
            var block = new PageShapeBlock(model.Id, bounds, ShapeGeometryBuilder.Build(model.Kind, bounds),
                DrawingObjectRenderMetadataPlanner.ResolveDrawingShapeRenderMetadata(model, new Workbook("test").Theme),
                "Ink", new PageTextFont("Segoe UI", 12, false, false, new PresentationRgb(255, 0, 0)),
                DrawingShapeTextHAlign.Left, DrawingShapeTextVAnchor.Top, true)
            { BodyClip = new LayoutRect(0, 0, 80, 40) };
            var overlays = new List<PdfTextOverlay>();
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                typeof(PrintRenderer).GetMethod("DrawPrintedShapes", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [dc, overlays, new[] { block }, true]);
            var bitmap = new RenderTargetBitmap(80, 40, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var bytes = new byte[80 * 40 * 4];
            bitmap.CopyPixels(bytes, 80 * 4, 0);
            bytes[((30 * 80) + 40) * 4 + 3].Should().Be(0, "black-and-white printing suppresses shape fill");
            var ink = Enumerable.Range(0, bytes.Length / 4).Where(i => bytes[i * 4 + 3] > 0).ToArray();
            ink.Should().NotBeEmpty("the dashed outline and text are still printed");
            ink.Should().OnlyContain(i => bytes[i * 4] == 0 && bytes[i * 4 + 1] == 0 && bytes[i * 4 + 2] == 0);
            overlays.Should().ContainSingle().Which.Color.Should().Be(Colors.Black);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawPrintedShapes_RendersFlatLines(bool vertical)
    {
        StaTestRunner.Run(() =>
        {
            var model = new DrawingShapeModel { Kind = DrawingShapeKind.Line, OutlineColor = new CellColor(255, 0, 0), OutlineWidthPoints = 2 };
            var bounds = vertical ? new LayoutRect(20, 5, 0, 30) : new LayoutRect(5, 20, 30, 0);
            var block = new PageShapeBlock(model.Id, bounds, ShapeGeometryBuilder.Build(model.Kind, bounds),
                DrawingObjectRenderMetadataPlanner.ResolveDrawingShapeRenderMetadata(model, new Workbook("test").Theme),
                null, new PageTextFont("Segoe UI", 12, false, false, new PresentationRgb(0, 0, 0)),
                DrawingShapeTextHAlign.Left, DrawingShapeTextVAnchor.Top, true)
            { BodyClip = new LayoutRect(0, 0, 40, 40) };
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                typeof(PrintRenderer).GetMethod("DrawPrintedShapes", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [dc, new List<PdfTextOverlay>(), new[] { block }, false]);
            var bitmap = new RenderTargetBitmap(40, 40, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var bytes = new byte[40 * 40 * 4];
            bitmap.CopyPixels(bytes, 40 * 4, 0);
            bytes[((20 * 40) + 20) * 4 + 2].Should().BeGreaterThan(200);
        });
    }

    [Fact]
    public void DrawPrintedShapes_ClipsToBodyRatherThanRepeatedTitleBand()
    {
        StaTestRunner.Run(() =>
        {
            var model = new DrawingShapeModel { FillColor = new CellColor(255, 0, 0), OutlineHasNoFill = true };
            var bounds = new LayoutRect(0, 0, 40, 40);
            var block = new PageShapeBlock(model.Id, bounds, ShapeGeometryBuilder.Build(model.Kind, bounds),
                DrawingObjectRenderMetadataPlanner.ResolveDrawingShapeRenderMetadata(model, new Workbook("test").Theme),
                null, new PageTextFont("Segoe UI", 12, false, false, new PresentationRgb(0, 0, 0)),
                DrawingShapeTextHAlign.Left, DrawingShapeTextVAnchor.Top, true)
            { BodyClip = new LayoutRect(0, 20, 40, 20) };
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var method = typeof(PrintRenderer).GetMethod("DrawPrintedShapes", BindingFlags.NonPublic | BindingFlags.Static)!;
                method.Invoke(null, [dc, new List<PdfTextOverlay>(), new[] { block }, false]);
            }
            var bitmap = new RenderTargetBitmap(40, 40, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var bytes = new byte[40 * 40 * 4];
            bitmap.CopyPixels(bytes, 40 * 4, 0);
            bytes[((10 * 40) + 10) * 4 + 3].Should().Be(0);
            bytes[((30 * 40) + 10) * 4 + 2].Should().Be(255);
        });
    }

    [Fact]
    public void RenderWorksheet_UsesCrossKindZOrder()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Z order");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page"));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("End"));
            var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 40, Height = 30, FillColor = new CellColor(255, 0, 0), OutlineHasNoFill = true };
            var box = new TextBoxModel { Anchor = shape.Anchor, Width = 40, Height = 30, FillColor = new CellColor(0, 0, 255), OutlineHasNoFill = true };
            sheet.DrawingShapes.Add(shape);
            sheet.TextBoxes.Add(box);
            sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));
            sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, box.Id));
            var textBoxOnTop = RenderColors();
            sheet.DrawingObjectZOrder.Reverse();
            var shapeOnTop = RenderColors();
            textBoxOnTop.Blue.Should().BeGreaterThan(200);
            shapeOnTop.Red.Should().BeGreaterThan(200);
            shapeOnTop.Blue.Should().BeLessThan(textBoxOnTop.Blue);

            (int Red, int Blue) RenderColors()
            {
                var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
                var page = document.Pages[0].Child;
                page.Measure(new Size(page.Width, page.Height));
                page.Arrange(new Rect(0, 0, page.Width, page.Height));
                page.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.Width), (int)Math.Ceiling(page.Height), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(page);
                var stride = bitmap.PixelWidth * 4;
                var bytes = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels(bytes, stride, 0);
                var red = 0;
                var blue = 0;
                for (var i = 0; i < bytes.Length; i += 4)
                {
                    if (bytes[i + 2] > 220 && bytes[i + 1] < 30 && bytes[i] < 30) red++;
                    if (bytes[i] > 220 && bytes[i + 1] < 30 && bytes[i + 2] < 40) blue++;
                }
                return (red, blue);
            }
        });
    }

    [Fact]
    public void RenderWorksheet_FlippedShapeKeepsTextReadable()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Shapes");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page"));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("End"));
            var shape = new DrawingShapeModel
            {
                Anchor = new CellAddress(sheet.Id, 1, 1), Width = 100, Height = 30,
                FillColor = new CellColor(255, 255, 255), OutlineHasNoFill = true, ShapeText = "Readable"
            };
            sheet.DrawingShapes.Add(shape);
            var normal = RenderPixels();
            shape.FlipHorizontal = true;
            var flipped = RenderPixels();
            flipped.Should().Equal(normal, "flipping a symmetric rectangle must not mirror its text");

            byte[] RenderPixels()
            {
                var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
                var page = document.Pages[0].Child;
                PdfTextOverlayExtractor.Extract(page).Should().Contain(overlay => overlay.Text == "Readable");
                page.Measure(new Size(page.Width, page.Height));
                page.Arrange(new Rect(0, 0, page.Width, page.Height));
                page.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.Width), (int)Math.Ceiling(page.Height), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(page);
                var stride = bitmap.PixelWidth * 4;
                var bytes = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels(bytes, stride, 0);
                return bytes;
            }
        });
    }

    [Fact]
    public void RenderWorksheet_PaintsPrintableShapeAndOmitsNonprintingShape()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Shapes");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page"));
            sheet.DrawingShapes.Add(new DrawingShapeModel
            {
                Anchor = new CellAddress(sheet.Id, 1, 1), Width = 40, Height = 30,
                FillColor = new CellColor(255, 0, 0), OutlineHasNoFill = true
            });
            sheet.DrawingShapes.Add(new DrawingShapeModel
            {
                Anchor = new CellAddress(sheet.Id, 1, 1), AnchorOffsetX = 45, Width = 40, Height = 30,
                FillColor = new CellColor(0, 0, 255), OutlineHasNoFill = true, PrintObject = false
            });
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].Child;
            page.Measure(new Size(page.Width, page.Height));
            page.Arrange(new Rect(0, 0, page.Width, page.Height));
            page.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.Width), (int)Math.Ceiling(page.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(page);
            var stride = bitmap.PixelWidth * 4;
            var bytes = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(bytes, stride, 0);
            var red = 0;
            var blue = 0;
            for (var i = 0; i < bytes.Length; i += 4)
            {
                if (bytes[i + 2] > 220 && bytes[i + 1] < 30 && bytes[i] < 30) red++;
                if (bytes[i] > 220 && bytes[i + 1] < 30 && bytes[i + 2] < 30) blue++;
            }
            red.Should().BeGreaterThan(200);
            blue.Should().Be(0);
        });
    }
}
