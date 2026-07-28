using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Smoke tests for the FidelityRender composite path: verifies that layers the live app shows
/// (floating objects, page border, watermark, columns, headers/footers) actually produce
/// non-background pixels in the rendered PNGs.  These are STA/WPF tests because the composite
/// path uses DocumentView, FlowDocument pagination, PaginatedEditorPanel, and WPF rendering.
///
/// Headless WPF rendering note: VisualBrush on unconnected elements produces blank output.
/// We use RenderTargetBitmap.Render(element) after Measure+Arrange for all detached visuals.
/// </summary>
public sealed class FidelityRenderCompositeTests
{
    // ── solid 10×10 red PNG for floating image fixtures ───────────────────────────────────────────
    // Generated via WPF's own RenderTargetBitmap so the PNG bytes are guaranteed valid and the pixel
    // content is a fully opaque red, clearly visible when composited over a white page background.
    private static byte[] MakeSolidRedPng(int width = 10, int height = 10)
    {
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, width, height));
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private const double PageW = 816;
    private const double PageH = 1056;

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the composite render for a single-page TextDocument and returns the rendered bitmap.
    /// Mirrors the logic in FreeW.FidelityRender/Program.cs RenderDocumentComposite.
    /// Uses RenderTargetBitmap.Render(element) for all detached visuals (headless-safe).
    /// </summary>
    private static RenderTargetBitmap RenderComposite(TextDocument doc)
    {
        var page = doc.Page;
        var (pageWDip, pageHDip) = PageLayout.PageSizeDip(page);
        var (marginLeft, marginTop, marginRight, marginBottom) = PageLayout.MarginsDip(page);
        int pixW = (int)Math.Max(1, Math.Round(pageWDip));
        int pixH = (int)Math.Max(1, Math.Round(pageHDip));

        // Build FlowDocument with column layout.
        var bodyView = new DocumentView { Width = pageWDip };
        bodyView.LoadModel(doc);
        var flow = bodyView.Document;
        bodyView.Document = new FlowDocument();
        flow.PageWidth   = pageWDip;
        flow.PageHeight  = pageHDip;
        flow.PagePadding = new Thickness(marginLeft, marginTop, marginRight, marginBottom);
        DocumentView.ApplyColumnLayout(flow, page);

        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(pageWDip, pageHDip);
        paginator.ComputePageCount();

        // Floating canvas: populate. We then rasterize per-child (not the whole Canvas), mirroring
        // the Program.cs approach: RenderTargetBitmap.Render(canvas) produces blank output for
        // UIElement children not connected to a live visual tree. Iterating children and drawing
        // each Image.Source directly via dc.DrawImage is the headless-safe path.
        var floatingCanvas = new Canvas { Width = pageWDip, Height = pageHDip };
        bodyView.SetFloatingCanvas(floatingCanvas);
        floatingCanvas.Measure(new Size(pageWDip, pageHDip));
        floatingCanvas.Arrange(new Rect(0, 0, pageWDip, pageHDip));
        floatingCanvas.UpdateLayout();

        RenderTargetBitmap? floatingBmp = null;
        if (floatingCanvas.Children.Count > 0)
        {
            var floatDv = new DrawingVisual();
            using (var dc = floatDv.RenderOpen())
            {
                foreach (System.Windows.UIElement child in floatingCanvas.Children)
                {
                    double left = Canvas.GetLeft(child);
                    double top  = Canvas.GetTop(child);
                    if (double.IsNaN(left)) left = 0;
                    if (double.IsNaN(top))  top  = 0;
                    if (child is System.Windows.Controls.Image img && img.Source is ImageSource src)
                    {
                        double w = img.Width, h = img.Height;
                        if (!double.IsNaN(w) && !double.IsNaN(h) && w > 0 && h > 0)
                            dc.DrawImage(src, new Rect(left, top, w, h));
                    }
                    else if (child is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                    {
                        dc.DrawRectangle(new VisualBrush(fe) { Stretch = Stretch.Fill },
                            null, new Rect(left, top, fe.ActualWidth, fe.ActualHeight));
                    }
                }
            }
            floatingBmp = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);
            floatingBmp.Render(floatDv);
        }

        // Header/footer panel (resolved for per-page slot names).
        PaginatedEditorPanel? panel = null;
        try
        {
            var panelSource = new DocumentView { Width = pageWDip };
            panelSource.LoadModel(doc);
            panel = PaginatedEditorPanel.Build(panelSource);
        }
        catch { panel = null; }

        // Composite page 0.
        var docPage   = paginator.GetPage(0);
        var pageColor = Colors.White;
        var bmp       = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);

        // Layer 1: background.
        var bgVis = new DrawingVisual();
        using (var dc = bgVis.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, pixW, pixH));
        bmp.Render(bgVis);

        // Layer 1b: fixed VML watermark.
        var wm = page.EffectiveWatermark;
        if (wm is not null)
        {
            var wmBmp = RenderWatermarkPage(wm, pageColor, pixW, pixH);
            var wmVis = new DrawingVisual();
            using (var dc = wmVis.RenderOpen())
                dc.DrawImage(wmBmp, new Rect(0, 0, pixW, pixH));
            bmp.Render(wmVis);
        }

        // Layer 2: body (paginator Visual is a fully-realized WPF visual, VisualBrush works here).
        var bodyVis = new DrawingVisual();
        using (var dc = bodyVis.RenderOpen())
            dc.DrawRectangle(new VisualBrush(docPage.Visual) { Stretch = Stretch.None },
                null, new Rect(0, 0, pageWDip, pageHDip));
        bmp.Render(bodyVis);

        // Layer 3: page border.
        if (page.PageBorder is { } pb)
        {
            var bv = new DrawingVisual();
            using (var dc = bv.RenderOpen())
            {
                var bc  = ParseHexColor(pb.ColorHex, Colors.Black);
                var edgeInset = Math.Min(PageLayout.PointsToDip(24), Math.Min(pixW, pixH) / 4.0);
                var borderWidth = Math.Max(1, pb.WidthPt * PageLayout.DipPerPoint);
                if (pb.LineStyle == BorderLineStyle.Double)
                {
                    var pen = new Pen(new SolidColorBrush(bc), borderWidth * 0.75);
                    DrawPageBorderFrame(dc, pen, edgeInset, pixW, pixH);
                    DrawPageBorderFrame(dc, pen, edgeInset + borderWidth * 2.0, pixW, pixH);
                }
                else
                {
                    var pen = new Pen(new SolidColorBrush(bc), borderWidth);
                    DrawPageBorderFrame(dc, pen, edgeInset, pixW, pixH);
                }
            }
            bmp.Render(bv);
        }

        // Layer 4: floating canvas (pre-rasterized).
        if (floatingBmp is not null)
        {
            var fv = new DrawingVisual();
            using (var dc = fv.RenderOpen())
                dc.DrawImage(floatingBmp, new Rect(0, 0, pixW, pixH));
            bmp.Render(fv);
        }

        // Layer 5: header + footer via paginator (headless-safe, same approach as Program.cs).
        // RenderTargetBitmap.Render(DocumentView) produces blank output for disconnected RichTextBox;
        // paginator's GetPage(0).Visual IS a fully-realized WPF visual that works headlessly.
        if (panel is not null && panel.PageBoxes.Count > 0)
        {
            var box = panel.PageBoxes[0];
            const double hfH = 36;
            var ownerHf = box.OwnerSectionHf ?? doc.FinalSectionHeadersFooters;

            if (box.HeaderSubEditor is not null && box.HeaderSlotName is { } hSlotName)
            {
                var hSlot = ResolveHfSlot(ownerHf, hSlotName);
                if (hSlot is not null && !hSlot.IsEmpty)
                {
                    var hfPage = RenderHfSlot(hSlot, doc, pageWDip, hfH, 1, 1);
                    if (hfPage is not null)
                    {
                        var hv = new DrawingVisual();
                        using (var dc = hv.RenderOpen())
                            dc.DrawRectangle(new VisualBrush(hfPage.Visual)
                            {
                                Stretch = Stretch.None,
                                AlignmentX = AlignmentX.Left,
                                AlignmentY = AlignmentY.Top
                            },
                                null, new Rect(marginLeft, 2, pageWDip - marginLeft - marginRight, hfH));
                        bmp.Render(hv);
                    }
                }
            }

            if (box.FooterSubEditor is not null && box.FooterSlotName is { } fSlotName)
            {
                var fSlot = ResolveHfSlot(ownerHf, fSlotName);
                if (fSlot is not null && !fSlot.IsEmpty)
                {
                    var hfPage = RenderHfSlot(fSlot, doc, pageWDip, hfH, 1, 1);
                    if (hfPage is not null)
                    {
                        var fv = new DrawingVisual();
                        using (var dc = fv.RenderOpen())
                            dc.DrawRectangle(new VisualBrush(hfPage.Visual)
                            {
                                Stretch = Stretch.None,
                                AlignmentX = AlignmentX.Left,
                                AlignmentY = AlignmentY.Top
                            },
                                null, new Rect(marginLeft, pixH - hfH - 2, pageWDip - marginLeft - marginRight, hfH));
                        bmp.Render(fv);
                    }
                }
            }
        }

        return bmp;
    }

    /// <summary>
    /// Renders Word's single fixed-size VML watermark shape for the composite smoke tests.
    /// </summary>
    private static RenderTargetBitmap RenderWatermarkPage(WatermarkOptions options, Color pageColor, int pixW, int pixH)
    {
        var baseColor  = ParseHexColor(options.FontColorHex, Color.FromRgb(0x80, 0x80, 0x80));
        var alpha      = (byte)Math.Clamp((int)Math.Round(options.Opacity * 255), 0, 255);
        var foreground = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));

        var pageBmp = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);
        var pageVis = new DrawingVisual();
        using (var dc = pageVis.RenderOpen())
        {
            var plan = WatermarkVisualPlanner.BuildTextLayout(options, pixW, pixH);
            if (plan is not null)
            {
                var typeface = new Typeface(new FontFamily(options.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var unitText = new FormattedText(options.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 1, foreground, 1);
                var fontSize = WatermarkVisualPlanner.ResolveTextPathFontSize(plan, unitText.Width);
                var text = new FormattedText(options.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, foreground, 1);
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, pixW, pixH)));
                if (Math.Abs(plan.RotationDegrees) > 0.01)
                    dc.PushTransform(new RotateTransform(plan.RotationDegrees, plan.CenterXDip, plan.CenterYDip));
                dc.DrawText(text, new Point(plan.CenterXDip - text.Width / 2, plan.CenterYDip - text.Height / 2));
                if (Math.Abs(plan.RotationDegrees) > 0.01)
                    dc.Pop();
                dc.Pop();
            }
        }
        pageBmp.Render(pageVis);
        return pageBmp;
    }

    private static byte[] GetPixels(RenderTargetBitmap bmp)
    {
        var pixels = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
        bmp.CopyPixels(pixels, bmp.PixelWidth * 4, 0);
        return pixels;
    }

    private static bool HasNonWhitePixelsInRegion(byte[] pixels, int stride, Rect region)
    {
        int x0 = (int)region.Left, y0 = (int)region.Top;
        int x1 = (int)region.Right, y1 = (int)region.Bottom;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            int idx = y * stride + x * 4;
            byte b = pixels[idx], g = pixels[idx+1], r = pixels[idx+2], a = pixels[idx+3];
            if (a > 10 && (b < 240 || g < 240 || r < 240))
                return true;
        }
        return false;
    }

    private static int MinInkX(byte[] pixels, int stride, Rect region)
    {
        var x0 = Math.Max(0, (int)region.Left);
        var y0 = Math.Max(0, (int)region.Top);
        var x1 = Math.Min(stride / 4, (int)region.Right);
        var y1 = Math.Min(pixels.Length / stride, (int)region.Bottom);
        var min = x1;
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
        {
            var idx = y * stride + x * 4;
            if (pixels[idx + 3] > 10 && (pixels[idx] < 240 || pixels[idx + 1] < 240 || pixels[idx + 2] < 240))
                min = Math.Min(min, x);
        }

        return min;
    }

    private static Color ParseHexColor(string hex, Color fallback)
    {
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    private static void DrawPageBorderFrame(DrawingContext drawingContext, Pen pen, double edgeInset, double width, double height)
    {
        var inset = edgeInset + pen.Thickness / 2;
        drawingContext.DrawRectangle(null, pen,
            new Rect(inset, inset,
                Math.Max(0, width - 2 * inset),
                Math.Max(0, height - 2 * inset)));
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 4: Floating object
    // ════════════════════════════════════════════════════════════════════════════════════════════════

    [StaFact]
    public void CompositeRender_FloatingObject_ProducesNonBlankPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph("Hello floating world");
        para.Runs.Add(Run.FromImage(new InlineImage(MakeSolidRedPng(), widthPt: 100, heightPt: 75)
        {
            Wrapping            = ImageWrapping.Square,
            HorizontalOffsetPt  = 50,
            VerticalOffsetPt    = 50,
            HorizontalAnchor    = HorizontalAnchor.Margin,
            VerticalAnchor      = VerticalAnchor.Page,
            ZOrderIndex         = 1,
        }));
        doc.Blocks.Add(para);

        var bmp    = RenderComposite(doc);
        var pixels = GetPixels(bmp);
        int stride = bmp.PixelWidth * 4;

        HasNonWhitePixelsInRegion(pixels, stride, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight))
            .Should().BeTrue("composite render of a floating-image doc must produce non-blank output");
    }

    [StaFact]
    public void CompositeRender_FloatingObject_CanvasHasChildren()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph("text");
        para.Runs.Add(Run.FromImage(new InlineImage(MakeSolidRedPng(), widthPt: 72, heightPt: 54)
        {
            Wrapping           = ImageWrapping.Square,
            HorizontalOffsetPt = 36,
            VerticalOffsetPt   = 36,
            HorizontalAnchor   = HorizontalAnchor.Margin,
            VerticalAnchor     = VerticalAnchor.Page,
        }));
        doc.Blocks.Add(para);

        var bodyView      = new DocumentView { Width = PageW };
        bodyView.LoadModel(doc);
        var floatingCanvas = new Canvas { Width = PageW, Height = PageH };
        bodyView.SetFloatingCanvas(floatingCanvas);
        floatingCanvas.Measure(new Size(PageW, PageH));
        floatingCanvas.Arrange(new Rect(0, 0, PageW, PageH));
        floatingCanvas.UpdateLayout();

        floatingCanvas.Children.Count.Should().BeGreaterThan(0,
            "floating canvas must contain at least one child for a doc with a floating image");
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════
    // Layers 1b + 3: Watermark + Page border
    // ════════════════════════════════════════════════════════════════════════════════════════════════

    [StaFact]
    public void CompositeRender_PageBorderAndWatermark_ProducesNonBlankPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.PageBorder       = new PageBorder("#000000", 2.0);
        doc.Page.WatermarkOptions = new WatermarkOptions("DRAFT");
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Document with border and watermark"));

        var bmp    = RenderComposite(doc);
        var pixels = GetPixels(bmp);
        int stride = bmp.PixelWidth * 4;

        HasNonWhitePixelsInRegion(pixels, stride, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight))
            .Should().BeTrue("page border + watermark must produce visible non-white pixels");
    }

    [StaFact]
    public void CompositeRender_PageBorder_UsesWordPageEdgeInset()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.PageBorder = new PageBorder("#000000", 3.0);
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Border test"));

        var bmp    = RenderComposite(doc);
        var pixels = GetPixels(bmp);
        int stride = bmp.PixelWidth * 4;
        double edgeInset = PageLayout.PointsToDip(24);

        HasNonWhitePixelsInRegion(pixels, stride, new Rect(10, 1, bmp.PixelWidth - 20, 8))
            .Should().BeFalse("Word's page border must not be painted on the bitmap edge");
        HasNonWhitePixelsInRegion(pixels, stride,
                new Rect(10, edgeInset - 3, bmp.PixelWidth - 20, 8))
            .Should().BeTrue("page border must draw at Word's 24pt page-edge inset");
    }

    [StaFact]
    public void CompositeRender_DoublePageBorder_RendersSeparatedStrokes()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.PageBorder = new PageBorder("#000000", 2.25) { LineStyle = BorderLineStyle.Double };
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Double border test"));

        var bmp = RenderComposite(doc);
        var pixels = GetPixels(bmp);
        int stride = bmp.PixelWidth * 4;
        double edgeInset = PageLayout.PointsToDip(24);

        HasNonWhitePixelsInRegion(pixels, stride, new Rect(100, edgeInset - 1, 80, 3))
            .Should().BeTrue();
        HasNonWhitePixelsInRegion(pixels, stride, new Rect(100, edgeInset + 3, 80, 1))
            .Should().BeFalse("double borders preserve a clear gap between their two strokes");
        HasNonWhitePixelsInRegion(pixels, stride, new Rect(100, edgeInset + 6, 80, 3))
            .Should().BeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 2: Columns
    // ════════════════════════════════════════════════════════════════════════════════════════════════

    [StaFact]
    public void CompositeRender_TwoColumns_ProducesNonBlankPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.ColumnCount     = 2;
        doc.Page.ColumnSpacingPt = 36;
        doc.Blocks.Clear();
        for (int i = 0; i < 20; i++)
            doc.Blocks.Add(new Paragraph($"Column layout paragraph {i + 1}: lorem ipsum dolor sit amet, consectetur adipiscing elit."));

        var bmp    = RenderComposite(doc);
        var pixels = GetPixels(bmp);
        int stride = bmp.PixelWidth * 4;

        HasNonWhitePixelsInRegion(pixels, stride, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight))
            .Should().BeTrue("two-column doc composite render must produce non-blank output");
    }

    [StaFact]
    public void CompositeRender_TwoColumns_ApplyColumnLayout_SetsColumnWidth()
    {
        var page = new PageSettings();
        page.ColumnCount     = 2;
        page.ColumnSpacingPt = 36;

        var flow = new FlowDocument();
        DocumentView.ApplyColumnLayout(flow, page);

        double.IsInfinity(flow.ColumnWidth).Should().BeFalse(
            "two-column layout must set a finite ColumnWidth on the FlowDocument");
        flow.ColumnWidth.Should().BeGreaterThan(0);
        flow.ColumnGap.Should().BeApproximately(PageLayout.PointsToDip(36), 1.0);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 5: Headers / footers
    // ════════════════════════════════════════════════════════════════════════════════════════════════

    [StaFact]
    public void CompositeRender_HeaderFooter_ProducesNonBlankPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("Page Header Text");
        doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("Page Footer Text");
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body content with header and footer."));

        var bmp    = RenderComposite(doc);
        var pixels = GetPixels(bmp);
        int stride = bmp.PixelWidth * 4;

        HasNonWhitePixelsInRegion(pixels, stride, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight))
            .Should().BeTrue("header/footer doc composite render must produce non-blank output");
    }

    [StaFact]
    public void CompositeRender_HeaderFooter_IsNotClippedAtPrintableLeftMargin()
    {
        var doc = TextDocument.CreateEmpty();
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("Page Header Text");
        doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("Page Footer Text");
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("body"));

        var bmp = RenderComposite(doc);
        var pixels = GetPixels(bmp);
        var stride = bmp.PixelWidth * 4;
        var leftMargin = (int)Math.Round(PageLayout.PointsToDip(doc.Page.MarginLeftPt));

        MinInkX(pixels, stride, new Rect(0, 0, bmp.PixelWidth, 36))
            .Should().BeGreaterThanOrEqualTo(leftMargin - 2);
        MinInkX(pixels, stride, new Rect(0, bmp.PixelHeight - 38, bmp.PixelWidth, 38))
            .Should().BeGreaterThanOrEqualTo(leftMargin - 2);
    }

    [StaFact]
    public void CompositeRender_HeaderFooter_PanelBuilds()
    {
        var doc = TextDocument.CreateEmpty();
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("Header");
        doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("Footer");
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("body"));

        var panelSource = new DocumentView { Width = PageW };
        panelSource.LoadModel(doc);
        var panel = PaginatedEditorPanel.Build(panelSource);

        panel.PageBoxes.Should().NotBeEmpty("panel must produce at least one page box");
        var firstBox = panel.PageBoxes[0];
        (firstBox.HeaderSubEditor is not null || firstBox.FooterSubEditor is not null)
            .Should().BeTrue("first page box should have at least one HF sub-editor when header/footer slots are set");
    }

    // ── HF helpers ──────────────────────────────────────────────────────────────────────────────────

    private static HeaderFooter? ResolveHfSlot(SectionHeadersFooters hf, string slotName) =>
        slotName switch
        {
            "header"        => hf.Header,
            "footer"        => hf.Footer,
            "even-header"   => hf.EvenHeader,
            "even-footer"   => hf.EvenFooter,
            "first-header"  => hf.FirstHeader,
            "first-footer"  => hf.FirstFooter,
            _               => null,
        };

    private static DocumentPage? RenderHfSlot(HeaderFooter slot, TextDocument sourceDoc,
        double pageWDip, double heightDip, int pageNumber, int pageCount)
    {
        try
        {
            var wrapper = TextDocument.CreateEmpty();
            wrapper.DefaultRun       = sourceDoc.DefaultRun;
            wrapper.DefaultParagraph = sourceDoc.DefaultParagraph;
            wrapper.Blocks.Clear();
            foreach (var para in slot.Paragraphs)
                wrapper.Blocks.Add(para);
            if (wrapper.Blocks.Count == 0)
                return null;

            DocumentView._renderHfPageNumber = pageNumber;
            DocumentView._renderHfPageCount  = pageCount > 0 ? pageCount : 1;
            var hfView = new DocumentView { Width = pageWDip };
            try { hfView.LoadModel(wrapper); }
            finally { DocumentView._renderHfPageNumber = 0; DocumentView._renderHfPageCount = 0; }

            var hfFlow = hfView.Document;
            hfView.Document = new FlowDocument();
            hfFlow.PageWidth   = pageWDip;
            hfFlow.PageHeight  = heightDip;
            hfFlow.PagePadding = new Thickness(0);
            hfFlow.ColumnWidth = double.PositiveInfinity;

            var hfPag = ((IDocumentPaginatorSource)hfFlow).DocumentPaginator;
            hfPag.PageSize = new Size(pageWDip, heightDip);
            hfPag.ComputePageCount();
            return hfPag.PageCount > 0 ? hfPag.GetPage(0) : null;
        }
        catch { return null; }
    }
}
