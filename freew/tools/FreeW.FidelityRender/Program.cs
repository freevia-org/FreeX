using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;

// FreeW.FidelityRender — renders FreeW's view of one or more .docx files to PNG (one image per page),
// using the real editor render path (DocumentView -> FlowDocument -> page rasterization). This is the
// "FreeW side" of a visual fidelity comparison; the ground-truth side (MS Word / LibreOffice) and the
// image diff are produced by freew-fidelity-corpus/tools/Run-VisualFidelity.ps1.
//
// Usage: FreeW.FidelityRender <input.docx | inputDir> <outputDir> [maxPagesPerDoc] [--composite|--no-composite]
//   - input is a single .docx or a directory (all *.docx are rendered)
//   - output PNGs are named <docname>_pN.png (N = 1-based page index)
//   - --composite (default) renders the full composite the live app shows:
//       layer 1: page background colour
//       layer 1b: watermark (tiled text from BuildWatermarkBrush, rendered via its own RenderTargetBitmap)
//       layer 2: multi-column FlowDocument body (ApplyColumnLayout applied before paginating)
//       layer 3: page border drawn around the body
//       layer 4: floating-object overlay canvas (SyncFloatingObjectsCanvas), composited per-page
//       layer 5: headers/footers via PaginatedEditorPanel PageBox sub-editors
//   - --no-composite uses the original bare FlowDocument path (for regression comparison)
//
// Headless WPF rendering note: VisualBrush on unconnected elements produces blank output. For all
// detached elements (canvas, watermark grid, HF sub-editors) we use RenderTargetBitmap.Render(element)
// after Measure+Arrange, which is the reliable off-screen rendering path.

var composite = true; // composite is the default
var generateFixtures = false;
var filteredArgs = new List<string>();
foreach (var a in args)
{
    if (a == "--composite") composite = true;
    else if (a == "--no-composite") composite = false;
    else if (a == "--generate-fixtures") generateFixtures = true;
    else filteredArgs.Add(a);
}
args = filteredArgs.ToArray();

if (generateFixtures)
{
    // Generate test fixture .docx files (used by integration tests and manual render comparison).
    string fixtureDir = args.Length > 0 ? args[0] : ".";
    int exit2 = 0;
    var sta2 = new Thread(() => exit2 = GenerateFixtures(fixtureDir));
    sta2.SetApartmentState(ApartmentState.STA);
    sta2.Start();
    sta2.Join();
    return exit2;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: FreeW.FidelityRender <input.docx | inputDir> <outputDir> [maxPagesPerDoc] [--composite|--no-composite]");
    Console.Error.WriteLine("       FreeW.FidelityRender --generate-fixtures <outputDir>");
    return 2;
}

string input = args[0];
string outDir = args[1];
int maxPages = args.Length > 2 && int.TryParse(args[2], out var mp) ? Math.Max(1, mp) : 3;

int exit = 0;
var sta = new Thread(() => exit = composite
    ? RunComposite(input, outDir, maxPages)
    : RunBare(input, outDir, maxPages));
sta.SetApartmentState(ApartmentState.STA);
sta.Start();
sta.Join();
return exit;

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// COMPOSITE render path — composites all layers the live app shows
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

static int RunComposite(string input, string outDir, int maxPages)
{
    Directory.CreateDirectory(outDir);

    List<string> files;
    if (Directory.Exists(input))
        files = Directory.GetFiles(input, "*.docx").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    else if (File.Exists(input))
        files = [input];
    else
    {
        Console.Error.WriteLine($"input not found: {input}");
        return 2;
    }

    if (files.Count == 0)
    {
        Console.Error.WriteLine($"no .docx files under {input}");
        return 2;
    }

    int failures = 0;

    foreach (var file in files)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        try
        {
            var doc = DocxReader.Read(file);
            RenderDocumentComposite(doc, name, outDir, maxPages);
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    Console.WriteLine($"rendered {files.Count - failures}/{files.Count} docs into {outDir}");
    return 0;
}

/// <summary>
/// Composite render for a single document. Produces per-page PNGs that include:
///   Layer 1  – page background colour
///   Layer 1b – watermark (tiled text, rendered to its own bitmap via RenderTargetBitmap.Render)
///   Layer 2  – multi-column FlowDocument body (ApplyColumnLayout applied to the flow before paginating)
///   Layer 3  – page border rectangle drawn over the body
///   Layer 4  – floating objects from the overlay canvas (composited via RenderTargetBitmap.Render)
///   Layer 5  – headers/footers from PaginatedEditorPanel PageBox sub-editors
///
/// Headless rendering note: VisualBrush on unconnected WPF elements silently produces blank output
/// because the WPF rendering pipeline depends on a live visual tree (HwndSource). For all detached
/// visual elements we instead call RenderTargetBitmap.Render(element) after Measure+Arrange, which
/// is the reliable off-screen path and is what WPF's own print/XPS pipeline uses internally.
/// </summary>
static void RenderDocumentComposite(TextDocument doc, string name, string outDir, int maxPages)
{
    // ── page geometry from model ──────────────────────────────────────────────────────────────────
    var page = doc.Page;
    var (pageWDip, pageHDip) = PageLayout.PageSizeDip(page);
    var (marginLeft, marginTop, marginRight, marginBottom) = PageLayout.MarginsDip(page);

    // Render at 96dpi (WPF native). Default 8.5×11" page = 816×1056px.
    int pixW = (int)Math.Max(1, Math.Round(pageWDip));
    int pixH = (int)Math.Max(1, Math.Round(pageHDip));

    // ═══ LAYER 2: Build FlowDocument with correct column layout ═══════════════════════════════════
    var bodyView = new DocumentView { Width = pageWDip };
    bodyView.LoadModel(doc);

    // Detach the FlowDocument so we can paginate it ourselves (same pattern as bare path).
    FlowDocument flow = bodyView.Document;
    bodyView.Document = new FlowDocument();

    flow.PageWidth   = pageWDip;
    flow.PageHeight  = pageHDip;
    flow.PagePadding = new Thickness(marginLeft, marginTop, marginRight, marginBottom);

    // Layer 2: call ApplyColumnLayout so multi-column sections render with the correct column count.
    // The old path hard-coded ColumnWidth=pageW (single column). This fixes that miss.
    DocumentView.ApplyColumnLayout(flow, page);

    var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
    paginator.PageSize = new Size(pageWDip, pageHDip);
    paginator.ComputePageCount();
    int pageCount = Math.Min(Math.Max(1, paginator.PageCount), maxPages);

    // ═══ LAYER 4: Floating objects ════════════════════════════════════════════════════════════════
    // Build the floating-objects canvas exactly as the live editor does, then rasterize its
    // children individually. RenderTargetBitmap.Render(canvas) silently produces blank output for
    // UIElement children (Image controls) that haven't been added to a live visual tree — the WPF
    // compositor doesn't flush them. Instead, we walk the canvas children and composite each
    // FrameworkElement by drawing it via a DrawingVisual, which is the headless-safe path.
    var floatingCanvas = new Canvas { Width = pageWDip, Height = pageHDip };
    bodyView.SetFloatingCanvas(floatingCanvas);
    floatingCanvas.Measure(new Size(pageWDip, pageHDip));
    floatingCanvas.Arrange(new Rect(0, 0, pageWDip, pageHDip));
    floatingCanvas.UpdateLayout();

    // Pre-rasterise each floating child to a composite bitmap.
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

                // For Image controls (the primary floating-image type), extract the Source and
                // draw it directly as an ImageBrush. For other FrameworkElements, use VisualBrush
                // after ensuring the element is measured (they were arranged by floatingCanvas above).
                if (child is System.Windows.Controls.Image img
                    && img.Source is ImageSource src)
                {
                    double w = img.Width;
                    double h = img.Height;
                    if (!double.IsNaN(w) && !double.IsNaN(h) && w > 0 && h > 0)
                        dc.DrawImage(src, new Rect(left, top, w, h));
                }
                else if (child is FrameworkElement fe
                    && !double.IsNaN(fe.ActualWidth) && fe.ActualWidth > 0
                    && !double.IsNaN(fe.ActualHeight) && fe.ActualHeight > 0)
                {
                    dc.DrawRectangle(
                        new VisualBrush(fe) { Stretch = Stretch.Fill },
                        null,
                        new Rect(left, top, fe.ActualWidth, fe.ActualHeight));
                }
            }
        }
        floatingBmp = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);
        floatingBmp.Render(floatDv);
    }

    // ═══ LAYER 5: Headers / footers ══════════════════════════════════════════════════════════════
    // We use the PaginatedEditorPanel to resolve the correct header/footer slot per page (honouring
    // DifferentFirstPage, DifferentOddEvenPages, per-section slots). However, for rasterization we
    // bypass the sub-editor DocumentView (which doesn't render headlessly via RenderTargetBitmap
    // because RichTextBox needs a live visual tree). Instead, we re-render each slot's content
    // through the same DocumentView→FlowDocument paginator path used for the body: load a wrapper
    // document, extract page 0's Visual, and draw it. This is headless-safe and produces the same
    // visual output as the live PageBox header/footer sub-editors.
    PaginatedEditorPanel? panel = null;
    try
    {
        var panelSource = new DocumentView { Width = pageWDip };
        panelSource.LoadModel(doc);
        panel = PaginatedEditorPanel.Build(panelSource);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [warn] PaginatedEditorPanel.Build failed for {name}: {ex.GetType().Name}: {ex.Message}");
        panel = null;
    }

    // ═══ Per-page compositing ═════════════════════════════════════════════════════════════════════
    for (int i = 0; i < pageCount; i++)
    {
        DocumentPage docPage = paginator.GetPage(i);

        // Start the composite bitmap (white background).
        var bmp = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);

        // ─ Layers 1 + 1b + 2: background + watermark + body ──────────────────────────────────────
        // We composite these into a DrawingVisual because the body paginator visual is already a
        // WPF visual with correct layout. The watermark and background are drawn as fills behind it.
        {
            var pageColor = string.IsNullOrEmpty(page.BackgroundColorHex)
                ? Colors.White
                : ParseHexColor(page.BackgroundColorHex, Colors.White);

            var composite = new DrawingVisual();
            using (var dc = composite.RenderOpen())
            {
                // Layer 1: solid page background.
                dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, pixW, pixH));

                // Layer 1b: watermark tiled over the page background.
                // BuildWatermarkBrush returns a VisualBrush(Grid) where the Grid is not measured.
                // We build the watermark content manually (TextBlock rendered to bitmap) so it
                // works headlessly, then tile that bitmap as the watermark pattern.
                var wm = page.EffectiveWatermark;
                if (wm is not null)
                {
                    var wmBmp = RenderWatermarkTile(wm, pageColor, pixW, pixH);
                    dc.DrawImage(wmBmp, new Rect(0, 0, pixW, pixH));
                }

                // Layer 2: body FlowDocument content (the paginator's Visual is already laid out).
                // We use VisualBrush here because DocumentPage.Visual IS a fully-realized visual
                // that the WPF paginator has already laid out; it works correctly headlessly.
                dc.DrawRectangle(new VisualBrush(docPage.Visual) { Stretch = Stretch.None },
                    null, new Rect(0, 0, pageWDip, pageHDip));
            }
            bmp.Render(composite);
        }

        // ─ Layer 3: page border (draw into a separate DrawingVisual, composite onto bmp) ─────────
        if (page.PageBorder is { } pb)
        {
            var borderVisual = new DrawingVisual();
            using (var dc = borderVisual.RenderOpen())
            {
                var borderColor = ParseHexColor(pb.ColorHex, Colors.Black);
                var pen = new Pen(new SolidColorBrush(borderColor),
                    Math.Max(1, pb.WidthPt * PageLayout.DipPerPoint * (96.0 / 72.0)));
                double ins = pen.Thickness / 2;
                dc.DrawRectangle(null, pen,
                    new Rect(ins, ins, pixW - pen.Thickness, pixH - pen.Thickness));
            }
            bmp.Render(borderVisual);
        }

        // ─ Layer 4: floating objects (pre-rasterised bitmap, composited via alpha blend) ─────────
        // RenderTargetBitmap.Render composites with alpha blending when the bitmap has Pbgra32
        // format, so floating objects that are semi-transparent render correctly.
        if (floatingBmp is not null)
        {
            var floatVisual = new DrawingVisual();
            using (var dc = floatVisual.RenderOpen())
                dc.DrawImage(floatingBmp, new Rect(0, 0, pixW, pixH));
            bmp.Render(floatVisual);
        }

        // ─ Layer 5: header + footer (render via FlowDocument paginator, headless-safe) ────────────
        // We read the slot from the PaginatedEditorPanel's resolved box, then render it through a
        // fresh DocumentView + paginator. This mirrors BuildHfSubEditor's wrapper-document approach
        // (see PageBox.cs) and works headlessly because the paginator's DocumentPage.Visual is a
        // fully-realized WPF visual that RenderTargetBitmap can rasterize.
        if (panel is not null && i < panel.PageBoxes.Count)
        {
            var box = panel.PageBoxes[i];
            const double hfH = 36;

            var ownerHf = box.OwnerSectionHf ?? doc.FinalSectionHeadersFooters;

            if (box.HeaderSubEditor is not null && box.HeaderSlotName is { } hSlotName)
            {
                // Recover the HeaderFooter slot from the box's owning section (handles per-section HF).
                var hfSlot = ResolveHfSlotByName(ownerHf, hSlotName);
                if (hfSlot is not null && !hfSlot.IsEmpty)
                {
                    var hfPage = RenderHfSlot(hfSlot, doc, pageWDip, hfH, i + 1, pageCount);
                    if (hfPage is not null)
                    {
                        var hfVis = new DrawingVisual();
                        using (var dc = hfVis.RenderOpen())
                            dc.DrawRectangle(new VisualBrush(hfPage.Visual) { Stretch = Stretch.None },
                                null, new Rect(marginLeft, 2, pageWDip - marginLeft - marginRight, hfH));
                        bmp.Render(hfVis);
                    }
                }
            }

            if (box.FooterSubEditor is not null && box.FooterSlotName is { } fSlotName)
            {
                var fSlot = ResolveHfSlotByName(ownerHf, fSlotName);
                if (fSlot is not null && !fSlot.IsEmpty)
                {
                    var hfPage = RenderHfSlot(fSlot, doc, pageWDip, hfH, i + 1, pageCount);
                    if (hfPage is not null)
                    {
                        var hfVis = new DrawingVisual();
                        using (var dc = hfVis.RenderOpen())
                            dc.DrawRectangle(new VisualBrush(hfPage.Visual) { Stretch = Stretch.None },
                                null, new Rect(marginLeft, pixH - hfH - 2, pageWDip - marginLeft - marginRight, hfH));
                        bmp.Render(hfVis);
                    }
                }
            }
        }

        string outPath = Path.Combine(outDir, $"{name}_p{i + 1}.png");
        SavePng(bmp, outPath);
        Console.WriteLine($"ok    {Path.GetFileName(outPath)} ({pageCount} pages, composite)");
    }
}

/// <summary>
/// Renders the watermark as a tiled bitmap (headless-safe alternative to BuildWatermarkBrush).
/// BuildWatermarkBrush works in the live app because the Grid it returns is used as a RichTextBox
/// Background (which WPF lays out before painting). Headlessly, the Grid is never measured, so
/// the VisualBrush(Grid) produces nothing. We replicate the same visual: measure+arrange a
/// TextBlock, render it to a tile bitmap, then tile across the full page.
/// </summary>
static RenderTargetBitmap RenderWatermarkTile(WatermarkOptions options, Color pageColor, int pixW, int pixH)
{
    var baseColor = ParseHexColor(options.FontColorHex, Color.FromRgb(0x80, 0x80, 0x80));
    var alpha = (byte)Math.Clamp((int)Math.Round(options.Opacity * 255), 0, 255);
    var foreground = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));

    // Build the TextBlock tile (matches BuildWatermarkBrush exactly).
    var label = new System.Windows.Controls.TextBlock
    {
        Text           = options.Text,
        FontSize       = 48,
        FontWeight     = FontWeights.Bold,
        FontFamily     = new FontFamily(options.FontFamily),
        Foreground     = foreground,
        LayoutTransform = options.Layout == WatermarkLayout.Horizontal
            ? null
            : new RotateTransform(-45),
    };
    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    label.Arrange(new Rect(label.DesiredSize));

    // The tile size matches BuildWatermarkBrush.Viewport.
    int tileW = (int)Math.Max(240, label.DesiredSize.Width  + 80);
    int tileH = (int)Math.Max(240, label.DesiredSize.Height + 80);

    // Render the tile: page background colour + watermark label centred.
    var tileBmp = new RenderTargetBitmap(tileW, tileH, 96, 96, PixelFormats.Pbgra32);
    var tileVis = new DrawingVisual();
    using (var dc = tileVis.RenderOpen())
    {
        // Page background behind the watermark text.
        dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, tileW, tileH));
        // Centre the label in the tile.
        double offX = (tileW - label.DesiredSize.Width)  / 2;
        double offY = (tileH - label.DesiredSize.Height) / 2;
        dc.PushTransform(new TranslateTransform(offX, offY));
        dc.DrawRectangle(new VisualBrush(label) { Stretch = Stretch.None },
            null, new Rect(0, 0, label.DesiredSize.Width, label.DesiredSize.Height));
        dc.Pop();
    }
    tileBmp.Render(tileVis);

    // Tile the watermark across the full page.
    var pageBmp = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);
    var pageVis = new DrawingVisual();
    using (var dc = pageVis.RenderOpen())
    {
        for (int y = 0; y < pixH; y += tileH)
        for (int x = 0; x < pixW; x += tileW)
            dc.DrawImage(tileBmp, new Rect(x, y, tileW, tileH));
    }
    pageBmp.Render(pageVis);
    return pageBmp;
}

/// <summary>
/// Resolves a named header/footer slot ("header", "footer", "first-header", etc.) from the
/// given <see cref="SectionHeadersFooters"/> (the page box's OwnerSectionHf).  Returns null
/// for an unrecognised name or an empty slot.
/// </summary>
static HeaderFooter? ResolveHfSlotByName(SectionHeadersFooters hf, string slotName)
{
    return slotName switch
    {
        "header"        => hf.Header,
        "footer"        => hf.Footer,
        "even-header"   => hf.EvenHeader,
        "even-footer"   => hf.EvenFooter,
        "first-header"  => hf.FirstHeader,
        "first-footer"  => hf.FirstFooter,
        _               => null,
    };
}

/// <summary>
/// Renders a <see cref="HeaderFooter"/> slot's content to a <see cref="DocumentPage"/> via the
/// DocumentView + FlowDocument paginator pipeline (headless-safe). Sets the PAGE/NUMPAGES context
/// fields so field runs in the header/footer resolve to the correct page number.
/// Returns null if the slot is empty or rendering fails.
/// </summary>
static DocumentPage? RenderHfSlot(HeaderFooter slot, TextDocument sourceDoc,
    double pageWDip, double heightDip, int pageNumber, int pageCount)
{
    try
    {
        // Build wrapper document (same pattern as PageBox.BuildHfSubEditor).
        var wrapper = TextDocument.CreateEmpty();
        wrapper.DefaultRun       = sourceDoc.DefaultRun;
        wrapper.DefaultParagraph = sourceDoc.DefaultParagraph;
        wrapper.Blocks.Clear();
        foreach (var para in slot.Paragraphs)
            wrapper.Blocks.Add(para);
        if (wrapper.Blocks.Count == 0)
            return null;

        // Inject PAGE/NUMPAGES context.
        DocumentView._renderHfPageNumber = pageNumber;
        DocumentView._renderHfPageCount  = pageCount > 0 ? pageCount : 1;
        var hfView = new DocumentView { Width = pageWDip };
        try
        {
            hfView.LoadModel(wrapper);
        }
        finally
        {
            DocumentView._renderHfPageNumber = 0;
            DocumentView._renderHfPageCount  = 0;
        }

        var hfFlow = hfView.Document;
        hfView.Document = new FlowDocument();
        hfFlow.PageWidth   = pageWDip;
        hfFlow.PageHeight  = heightDip;
        hfFlow.PagePadding = new Thickness(0);
        hfFlow.ColumnWidth = double.PositiveInfinity;

        var hfPag = ((IDocumentPaginatorSource)hfFlow).DocumentPaginator;
        hfPag.PageSize = new Size(pageWDip, heightDip);
        hfPag.ComputePageCount();
        if (hfPag.PageCount == 0)
            return null;

        return hfPag.GetPage(0);
    }
    catch
    {
        return null;
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// BARE render path — original FlowDocument-only path (kept for --no-composite regression comparison)
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

static int RunBare(string input, string outDir, int maxPages)
{
    Directory.CreateDirectory(outDir);

    List<string> files;
    if (Directory.Exists(input))
        files = Directory.GetFiles(input, "*.docx").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    else if (File.Exists(input))
        files = [input];
    else
    {
        Console.Error.WriteLine($"input not found: {input}");
        return 2;
    }

    if (files.Count == 0)
    {
        Console.Error.WriteLine($"no .docx files under {input}");
        return 2;
    }

    const double pageW = 816;   // 8.5in @ 96dpi
    const double pageH = 1056;  // 11in  @ 96dpi
    int failures = 0;

    foreach (var file in files)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        try
        {
            var doc = DocxReader.Read(file);

            var view = new DocumentView { Width = pageW };
            view.LoadModel(doc);

            FlowDocument flow = view.Document;
            view.Document = new FlowDocument();

            flow.PageWidth   = pageW;
            flow.PageHeight  = pageH;
            flow.PagePadding = new Thickness(64);
            flow.ColumnWidth = pageW;
            flow.ColumnGap   = 0;

            var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
            paginator.PageSize = new Size(pageW, pageH);
            paginator.ComputePageCount();
            int pages = Math.Min(Math.Max(1, paginator.PageCount), maxPages);

            for (int i = 0; i < pages; i++)
            {
                DocumentPage docPage = paginator.GetPage(i);
                var bmp = new RenderTargetBitmap((int)pageW, (int)pageH, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));
                    dc.DrawRectangle(new VisualBrush(docPage.Visual), null, new Rect(0, 0, pageW, pageH));
                }
                bmp.Render(dv);

                string outPath = Path.Combine(outDir, $"{name}_p{i + 1}.png");
                SavePng(bmp, outPath);
                Console.WriteLine($"ok    {Path.GetFileName(outPath)} ({paginator.PageCount} pages)");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    Console.WriteLine($"rendered {files.Count - failures}/{files.Count} docs into {outDir}");
    return 0;
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// Test fixture generator  (--generate-fixtures <dir>)
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Generates the composite-render smoke-test fixture .docx files that are used by the integration
/// test suite (FidelityRenderCompositeTests) and by manual render comparisons.  Run via:
///   FreeW.FidelityRender --generate-fixtures &lt;outputDir&gt;
/// Calling this ensures the fixtures are always generated by the same code that renders them, so
/// the PNG pixel content is always correct and valid (generated via WPF's own encoder, not hardcoded
/// byte literals that may have incorrect CRCs or transparency).
/// </summary>
static int GenerateFixtures(string outDir)
{
    Directory.CreateDirectory(outDir);

    // ── Build a solid-color PNG via WPF (guaranteed valid, opaque, clearly visible) ──────────────
    // A 40×40 red square at 96 dpi is displayed as 40×40px in WPF and covers a large enough area
    // to be visible in the composite render even if it is scaled down.
    static byte[] MakeSolidColorPng(Color color, int width = 40, int height = 40)
    {
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new System.IO.MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    var redPng = MakeSolidColorPng(Colors.Red);

    // Alias FreeW.Core.Model.Paragraph to avoid ambiguity with System.Windows.Documents.Paragraph.
    static FreeW.Core.Model.Paragraph MP(string text) => new(text);

    // ── 1. Floating image fixture ─────────────────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Floating image test — the red box should appear overlaid on this text."));
        var para = MP("(inline text continues here for multiple lines to show wrap)");
        var img = new InlineImage(redPng, widthPt: 144, heightPt: 108)
        {
            Wrapping           = ImageWrapping.Square,
            HorizontalOffsetPt = 72,
            VerticalOffsetPt   = 100,
            HorizontalAnchor   = HorizontalAnchor.Margin,
            VerticalAnchor     = VerticalAnchor.Page,
            ZOrderIndex        = 1,
        };
        para.Runs.Add(FreeW.Core.Model.Run.FromImage(img));
        doc.Blocks.Add(para);
        for (int i = 0; i < 5; i++)
            doc.Blocks.Add(MP($"Paragraph {i + 1}: lorem ipsum dolor sit amet."));
        DocxWriter.Write(doc, Path.Combine(outDir, "fixture-floating.docx"));
        Console.WriteLine("  wrote fixture-floating.docx");
    }

    // ── 2. Two-column fixture ─────────────────────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.ColumnCount     = 2;
        doc.Page.ColumnSpacingPt = 36;
        doc.Blocks.Clear();
        for (int i = 0; i < 30; i++)
            doc.Blocks.Add(MP($"Column text paragraph {i + 1}: lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor."));
        DocxWriter.Write(doc, Path.Combine(outDir, "fixture-columns.docx"));
        Console.WriteLine("  wrote fixture-columns.docx");
    }

    // ── 3. Page border + DRAFT watermark ─────────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.PageBorder       = new PageBorder("#000080", 3.0);
        doc.Page.WatermarkOptions = new WatermarkOptions("DRAFT")
        {
            FontColorHex = "#808080",
            Opacity      = 0.4,
            Layout       = WatermarkLayout.Diagonal,
        };
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("This document has a navy page border and a DRAFT watermark."));
        doc.Blocks.Add(MP("The border should appear around the page edges."));
        doc.Blocks.Add(MP("The watermark should tile diagonally across the page."));
        DocxWriter.Write(doc, Path.Combine(outDir, "fixture-border-watermark.docx"));
        Console.WriteLine("  wrote fixture-border-watermark.docx");
    }

    // ── 4. Header + footer fixture ────────────────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("=== MY DOCUMENT HEADER ===");
        doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("=== PAGE FOOTER ===");
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("This document has a header at the top and a footer at the bottom."));
        for (int i = 0; i < 5; i++)
            doc.Blocks.Add(MP($"Body paragraph {i + 1}."));
        DocxWriter.Write(doc, Path.Combine(outDir, "fixture-hf.docx"));
        Console.WriteLine("  wrote fixture-hf.docx");
    }

    Console.WriteLine($"Done — 4 fixtures written to {outDir}");
    return 0;
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// Shared helpers
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

static void SavePng(RenderTargetBitmap bmp, string path)
{
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using FileStream fs = File.Create(path);
    enc.Save(fs);
}

static Color ParseHexColor(string hex, Color fallback)
{
    try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
    catch { return fallback; }
}
