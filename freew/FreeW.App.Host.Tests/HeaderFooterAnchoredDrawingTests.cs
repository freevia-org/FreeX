using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelRun = FreeW.Core.Model.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Anchored drawings inside header/footer stories (corpus stress015/stress018): a floating header logo
/// (wp:anchor picture) and Word's circled page number in the bottom margin (a positioned VML text box
/// holding a PAGE field) used to be dropped from Print/PDF composition entirely.
/// </summary>
public sealed class HeaderFooterAnchoredDrawingTests
{
    private static readonly LegacyTextBoxFrame RedCircleInBottomMargin = new(
        IsEllipse: true,
        WidthPt: 40,
        HeightPt: 40,
        FillColorHex: "#FF0000",
        StrokeColorHex: null,
        StrokeWidthPt: 0.75,
        LegacyFrameHorizontalAlignment.Center,
        LeftPt: 0,
        LegacyFrameReference.Margin,
        LegacyFrameVerticalAlignment.Center,
        TopPt: 0,
        LegacyFrameReference.BottomMarginArea,
        BehindText: false,
        LegacyFrameTextAnchor.Middle,
        InsetTopPt: 0,
        InsetBottomPt: 0);

    private static TextDocument ModelWithCircledPageNumberFooter()
    {
        var model = TextDocument.CreateEmpty();
        var footer = new HeaderFooter();
        footer.Paragraphs.Add(new ModelParagraph());
        var framed = new ModelParagraph { LegacyTextBoxFrame = RedCircleInBottomMargin };
        framed.Formatting = framed.Formatting with { Alignment = FreeW.Core.Model.TextAlignment.Center };
        framed.Runs.Add(ModelRun.ComplexFieldRun(" PAGE ", "1"));
        footer.Paragraphs.Add(framed);
        model.Footer = footer;
        return model;
    }

    [StaFact]
    public void LegacyTextBoxFrame_InFooter_DrawsItsShapeAtTheBottomMarginCentre_WithThisPagesNumber()
    {
        var model = ModelWithCircledPageNumberFooter();
        var page = model.Page;
        var footer = model.Footer!;
        var (contentWidth, _) = PageLayout.ContentAreaDip(page);
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var (marginLeft, _, _, marginBottom) = PageLayout.MarginsDip(page);
        var maxHeight = HeaderFooterBandRenderer.MaxBandHeightDip(page);
        var metrics = HeaderFooterBandRenderer.Measure(model, footer, isHeader: false, contentWidth, maxHeight);
        var origin = new Point(marginLeft, pageHeight - HeaderFooterBandRenderer.FooterDistanceDip(page) - metrics.HeightDip);

        var band = HeaderFooterBandRenderer.Render(
            model, footer, isHeader: false, new HeaderFooterBandContext(7, null, 9), contentWidth, metrics, maxHeight, page, origin);

        Assert.NotNull(band);
        band!.Offset = new Vector(origin.X, origin.Y);
        Assert.Contains("7", CollectGlyphText(band));

        var expected = HeaderFooterBandRenderer.ResolveLegacyFrameRect(RedCircleInBottomMargin, page, origin.Y, origin.Y);
        Assert.Equal(pageWidth / 2, expected.X + expected.Width / 2, 1);
        Assert.True(expected.Bottom <= pageHeight + 0.5);
        Assert.True(expected.Top >= pageHeight - Math.Max(marginBottom, pageHeight - origin.Y) - 0.5);

        var pixels = Rasterize(band, pageWidth, pageHeight);
        // Just inside the circle's left edge (the centre carries the number): the red fill.
        var probe = pixels(expected.X + expected.Width * 0.2, expected.Y + expected.Height / 2);
        Assert.True(probe.R > 200 && probe.G < 60 && probe.B < 60, $"expected red fill, got {probe}");
    }

    [StaFact]
    public void LegacyTextBoxFrame_ParagraphsLeaveTheFooterFlow()
    {
        var model = ModelWithCircledPageNumberFooter();
        var (contentWidth, _) = PageLayout.ContentAreaDip(model.Page);
        var maxHeight = HeaderFooterBandRenderer.MaxBandHeightDip(model.Page);

        var footer = model.Footer!;
        footer.Paragraphs[0].Runs.Add(new ModelRun("Footer text"));
        var withFrame = HeaderFooterBandRenderer.Measure(model, footer, isHeader: false, contentWidth, maxHeight);
        var plain = new HeaderFooter();
        plain.Paragraphs.Add(new ModelParagraph("Footer text"));
        var withoutFrame = HeaderFooterBandRenderer.Measure(model, plain, isHeader: false, contentWidth, maxHeight);

        Assert.Equal(withoutFrame.HeightDip, withFrame.HeightDip, 1);
    }

    [StaFact]
    public void FloatingPicture_InHeader_IsDrawnAtItsAnchorPosition()
    {
        var model = TextDocument.CreateEmpty();
        var image = new InlineImage(SolidPng(Colors.Blue), 40, 40, ImageFormat.Png)
        {
            Wrapping = ImageWrapping.Behind,
            HorizontalAnchor = HorizontalAnchor.Page,
            HorizontalOffsetPt = 100,
            VerticalAnchor = VerticalAnchor.Page,
            VerticalOffsetPt = 10,
        };
        var header = new HeaderFooter();
        var paragraph = new ModelParagraph();
        paragraph.Runs.Add(new ModelRun(string.Empty) { Image = image });
        paragraph.Runs.Add(new ModelRun("Header text"));
        header.Paragraphs.Add(paragraph);
        model.Header = header;

        var page = model.Page;
        var (contentWidth, _) = PageLayout.ContentAreaDip(page);
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var (marginLeft, _, _, _) = PageLayout.MarginsDip(page);
        var maxHeight = HeaderFooterBandRenderer.MaxBandHeightDip(page);
        var metrics = HeaderFooterBandRenderer.Measure(model, header, isHeader: true, contentWidth, maxHeight);
        var origin = new Point(marginLeft, HeaderFooterBandRenderer.HeaderDistanceDip(page));

        var band = HeaderFooterBandRenderer.Render(
            model, header, isHeader: true, new HeaderFooterBandContext(1, null, 1), contentWidth, metrics, maxHeight, page, origin);

        Assert.NotNull(band);
        band!.Offset = new Vector(origin.X, origin.Y);
        var pixels = Rasterize(band, pageWidth, pageHeight);
        var inside = pixels(PageLayout.PointsToDip(120), PageLayout.PointsToDip(30));
        Assert.True(inside.B > 200 && inside.R < 60, $"expected the blue picture, got {inside}");
        var outside = pixels(PageLayout.PointsToDip(60), PageLayout.PointsToDip(30));
        Assert.True(outside.B < 100 || outside.R > 200, $"expected no picture left of its anchor, got {outside}");
    }

    [StaFact]
    public void PrintPaginator_FooterPageField_HonoursSectionPageNumberStart()
    {
        var model = TextDocument.CreateEmpty();
        model.Page.PageNumberStartAt = 5;
        var footer = new HeaderFooter();
        var paragraph = new ModelParagraph();
        paragraph.Runs.Add(ModelRun.ComplexFieldRun(" PAGE ", "1"));
        footer.Paragraphs.Add(paragraph);
        model.Footer = footer;

        var flow = new FlowDocument();
        flow.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("body")));
        var inner = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(model.Page);
        inner.PageSize = new Size(pageWidth, pageHeight);
        var paginator = new HeaderFooterPaginator(inner, model, model.Page, lineHeightDip: 16);
        paginator.ComputePageCount();

        var text = CollectGlyphText(paginator.GetPage(0).Visual);

        Assert.Contains("5", text);
        Assert.DoesNotContain("1", text.Replace("body", string.Empty, StringComparison.Ordinal));
    }

    private static string CollectGlyphText(Visual root)
    {
        var builder = new StringBuilder();

        void FromDrawing(Drawing? drawing)
        {
            switch (drawing)
            {
                case System.Windows.Media.DrawingGroup group:
                    foreach (var child in group.Children)
                        FromDrawing(child);
                    break;
                case GlyphRunDrawing glyphs when glyphs.GlyphRun.Characters is { } characters:
                    builder.Append(characters.ToArray());
                    break;
            }
        }

        void Walk(DependencyObject node)
        {
            if (node is Visual visual)
                FromDrawing(VisualTreeHelper.GetDrawing(visual));
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Walk(VisualTreeHelper.GetChild(node, i));
        }

        Walk(root);
        return builder.ToString();
    }

    private static Func<double, double, Color> Rasterize(Visual band, double width, double height)
    {
        var root = new ContainerVisual();
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        root.Children.Add(background);
        root.Children.Add(band);
        var pixelWidth = (int)Math.Ceiling(width);
        var pixelHeight = (int)Math.Ceiling(height);
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var buffer = new byte[pixelWidth * pixelHeight * 4];
        bitmap.CopyPixels(buffer, pixelWidth * 4, 0);
        return (x, y) =>
        {
            var offset = ((int)y * pixelWidth + (int)x) * 4;
            return Color.FromRgb(buffer[offset + 2], buffer[offset + 1], buffer[offset]);
        };
    }

    private static byte[] SolidPng(Color color)
    {
        var pixels = Enumerable.Repeat(new[] { color.B, color.G, color.R, (byte)255 }, 16 * 16).SelectMany(p => p).ToArray();
        var source = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
