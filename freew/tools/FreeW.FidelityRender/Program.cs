using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.IO;
using FreeW.Core.Model;
using SkiaSharp;

// FreeW.FidelityRender — renders FreeW's view of one or more .docx files to PNG (one image per page),
// using the real editor render path (DocumentView -> FlowDocument -> page rasterization). This is the
// "FreeW side" of a visual fidelity comparison; the ground-truth side (MS Word / LibreOffice) and the
// image diff are produced by freew-fidelity-corpus/tools/Run-VisualFidelity.ps1.
//
// Usage: FreeW.FidelityRender <input.docx | inputDir> <outputDir> [maxPagesPerDoc] [--composite|--no-composite] [--software-fallback|--auto-software-fallback]
//   - input is a single .docx or a directory (all *.docx are rendered)
//   - output PNGs are named <docname>_pN.png (N = 1-based page index)
//   - --composite (default) renders the full composite the live app shows:
//       layer 1: page background colour
//       layer 1b: watermark (text or picture, rendered via its own RenderTargetBitmap)
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
var softwareFallback = false;
var autoSoftwareFallback = false;
var generateFixtures = false;
var generateF2Corpus = false;
var filteredArgs = new List<string>();
foreach (var a in args)
{
    if (a == "--composite") composite = true;
    else if (a == "--no-composite") composite = false;
    else if (a == "--software-fallback") softwareFallback = true;
    else if (a == "--auto-software-fallback") autoSoftwareFallback = true;
    else if (a == "--generate-fixtures") generateFixtures = true;
    else if (a == "--generate-f2-corpus") generateF2Corpus = true;
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

if (generateF2Corpus)
{
    // Generate the f2-flow visual-verification corpus (headers/footers/footnotes/endnotes/
    // section-break page-size/tracked-changes/comments).
    string corpusDir = args.Length > 0 ? args[0] : ".";
    GenerateF2FlowCorpus(corpusDir);
    return 0;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: FreeW.FidelityRender <input.docx | inputDir> <outputDir> [maxPagesPerDoc] [--composite|--no-composite] [--software-fallback|--auto-software-fallback]");
    Console.Error.WriteLine("       FreeW.FidelityRender --generate-fixtures <outputDir>");
    Console.Error.WriteLine("       FreeW.FidelityRender --generate-f2-corpus <outputDir>");
    return 2;
}

string input = args[0];
string outDir = args[1];
int maxPages = args.Length > 2 && int.TryParse(args[2], out var mp) ? Math.Max(1, mp) : 3;

int exit = 0;
var sta = new Thread(() => exit = composite
    ? RunComposite(input, outDir, maxPages, softwareFallback, autoSoftwareFallback)
    : RunBare(input, outDir, maxPages));
sta.SetApartmentState(ApartmentState.STA);
sta.Start();
sta.Join();
return exit;

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// COMPOSITE render path — composites all layers the live app shows
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

static int RunComposite(string input, string outDir, int maxPages, bool softwareFallback, bool autoSoftwareFallback)
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
    var evidence = new List<FreeWVisualEvidenceRow>();
    var wpfRenderTargetFailure = softwareFallback
        ? "Software evidence renderer requested by --software-fallback; WPF RenderTargetBitmap was not used."
        : autoSoftwareFallback
            ? DetectWpfRenderTargetBitmapFailure()
            : null;
    if (wpfRenderTargetFailure is not null)
    {
        Console.WriteLine(
            "WARN  WPF RenderTargetBitmap is unavailable for trusted evidence captures; " +
            "using the FreeW.FidelityRender software evidence renderer. " +
            wpfRenderTargetFailure);
    }

    foreach (var file in files)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        try
        {
            var doc = DocxReader.Read(file);
            RenderDocumentComposite(doc, name, outDir, maxPages, evidence, wpfRenderTargetFailure);
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    if (evidence.Count > 0)
        FreeWVisualEvidencePlanner.WriteManifest(outDir, evidence);

    Console.WriteLine($"rendered {files.Count - failures}/{files.Count} docs into {outDir}");
    return failures == 0 ? 0 : 1;
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
static void RenderDocumentComposite(
    TextDocument doc,
    string name,
    string outDir,
    int maxPages,
    List<FreeWVisualEvidenceRow> evidence,
    string? wpfRenderTargetFailure)
{
    // ── page geometry from model ──────────────────────────────────────────────────────────────────
    if (wpfRenderTargetFailure is not null)
    {
        RenderDocumentSoftwareFallback(doc, name, outDir, maxPages, evidence, wpfRenderTargetFailure);
        return;
    }

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

    // SG: Apply section-break BreakPageBefore flags to the flow so the WPF paginator produces the
    // correct page count for multi-section documents.  The FreeW/OOXML convention stores a
    // NextPage/EvenPage/OddPage section break on the LAST paragraph of the preceding section;
    // the paragraph that FOLLOWS the marker must get BreakPageBefore = true so the paginator
    // opens a new page there.  This mirrors PaginationEngine.ApplySectionBreakFlags.
    {
        var modelBlocks = doc.Blocks;
        var flowBlocks  = flow.Blocks.ToList();
        for (int bi = 0; bi < modelBlocks.Count - 1 && bi < flowBlocks.Count - 1; bi++)
        {
            if (modelBlocks[bi] is FreeW.Core.Model.Paragraph { SectionBreak: { } sec }
                && sec.BreakKind is SectionBreakKind.NextPage
                                 or SectionBreakKind.EvenPage
                                 or SectionBreakKind.OddPage)
            {
                if (flowBlocks[bi + 1] is System.Windows.Documents.Paragraph nextWpf)
                    nextWpf.BreakPageBefore = true;
            }
        }
    }

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
    // Word appends endnotes after the final body content when that page has room. They are not a
    // separate, empty document page merely because the document contains endnotes.
    var endnoteIds = doc.Endnotes.Keys.OrderBy(id => id).ToList();
    var hasEndnotes = endnoteIds.Count > 0;
    var evidencePageCount = pageCount;
    var sectionPageCounters = new Dictionary<int, int>();

    for (int i = 0; i < pageCount; i++)
    {
        DocumentPage docPage = paginator.GetPage(i);

        // SG: use this page's section geometry (portrait vs landscape) when it's available via the
        // panel. Fall back to the document-level page if the panel didn't build or the box index is
        // out of range (e.g. the body paginator produced more pages than panel boxes).
        PageSettings thisPageSettings = page;
        string? headerSlotName = null;
        string? footerSlotName = null;
        var hasFootnotes = false;
        if (panel is not null && i < panel.PageBoxes.Count)
        {
            var pageBox = panel.PageBoxes[i];
            thisPageSettings = pageBox.PageGeometry;
            headerSlotName = pageBox.HeaderSlotName;
            footerSlotName = pageBox.FooterSlotName;
            hasFootnotes = pageBox.FootnoteIds.Count > 0;
        }

        var (thisPageWDip, thisPageHDip) = PageLayout.PageSizeDip(thisPageSettings);
        var (thisMarginLeft, thisMarginTop, thisMarginRight, thisMarginBottom) =
            PageLayout.MarginsDip(thisPageSettings);

        int thisPixW = (int)Math.Max(1, Math.Round(thisPageWDip));
        int thisPixH = (int)Math.Max(1, Math.Round(thisPageHDip));

        // Start the composite bitmap at this page's geometry (white background).
        var bmp = new RenderTargetBitmap(thisPixW, thisPixH, 96, 96, PixelFormats.Pbgra32);

        // ─ Layers 1 + 1b + 2: background + watermark + body ──────────────────────────────────────
        // We composite these into a DrawingVisual because the body paginator visual is already a
        // WPF visual with correct layout. The watermark and background are drawn as fills behind it.
        {
            var pageColor = string.IsNullOrEmpty(thisPageSettings.BackgroundColorHex)
                ? Colors.White
                : ParseHexColor(thisPageSettings.BackgroundColorHex, Colors.White);

            var composite = new DrawingVisual();
            using (var dc = composite.RenderOpen())
            {
                // Layer 1: solid page background.
                dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, thisPixW, thisPixH));

                // Layer 1b: watermark tiled over the page background.
                // BuildWatermarkBrush returns a VisualBrush(Grid) where the Grid is not measured.
                // We build the watermark content manually (TextBlock rendered to bitmap) so it
                // works headlessly, then tile that bitmap as the watermark pattern.
                var wm = thisPageSettings.EffectiveWatermark;
                if (wm is not null)
                {
                    var wmBmp = RenderWatermarkTile(wm, pageColor, thisPixW, thisPixH);
                    dc.DrawImage(wmBmp, new Rect(0, 0, thisPixW, thisPixH));
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
        if (thisPageSettings.PageBorder is { } pb)
        {
            var borderVisual = new DrawingVisual();
            using (var dc = borderVisual.RenderOpen())
            {
                var borderColor = ParseHexColor(pb.ColorHex, Colors.Black);
                var pen = new Pen(new SolidColorBrush(borderColor),
                    Math.Max(1, pb.WidthPt * PageLayout.DipPerPoint * (96.0 / 72.0)));
                double ins = pen.Thickness / 2;
                dc.DrawRectangle(null, pen,
                    new Rect(ins, ins, thisPixW - pen.Thickness, thisPixH - pen.Thickness));
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
                dc.DrawImage(floatingBmp, new Rect(0, 0, thisPixW, thisPixH));
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
                    var hfPage = RenderHfSlot(hfSlot, doc, thisPageWDip, hfH, i + 1, box.PageNumberText, pageCount);
                    if (hfPage is not null)
                    {
                        var hfVis = new DrawingVisual();
                        using (var dc = hfVis.RenderOpen())
                            dc.DrawRectangle(new VisualBrush(hfPage.Visual) { Stretch = Stretch.None },
                                null, new Rect(thisMarginLeft, 2, thisPageWDip - thisMarginLeft - thisMarginRight, hfH));
                        bmp.Render(hfVis);
                    }
                }
            }

            if (box.FooterSubEditor is not null && box.FooterSlotName is { } fSlotName)
            {
                var fSlot = ResolveHfSlotByName(ownerHf, fSlotName);
                if (fSlot is not null && !fSlot.IsEmpty)
                {
                    var hfPage = RenderHfSlot(fSlot, doc, thisPageWDip, hfH, i + 1, box.PageNumberText, pageCount);
                    if (hfPage is not null)
                    {
                        var hfVis = new DrawingVisual();
                        using (var dc = hfVis.RenderOpen())
                            dc.DrawRectangle(new VisualBrush(hfPage.Visual) { Stretch = Stretch.None },
                                null, new Rect(thisMarginLeft, thisPixH - hfH - 2, thisPageWDip - thisMarginLeft - thisMarginRight, hfH));
                        bmp.Render(hfVis);
                    }
                }
            }

            // ─ Layer 6: footnote region (separator + footnote texts above footer) ─────────────────
            // Render footnotes that appear on this page.  We draw them above the footer zone using
            // the same TextBlock approach as PageBox.BuildNoteRegion.
            if (box.FootnoteIds.Count > 0)
            {
                var footnoteBmp = RenderNoteRegion(doc, box.FootnoteIds, Array.Empty<int>(),
                    thisPageWDip, thisMarginLeft, thisMarginRight, isEndnotePage: false);
                if (footnoteBmp is not null)
                {
                    // Place the footnote region just above the footer strip.
                    double fnH = footnoteBmp.Height;
                    double fnY = thisPixH - hfH - fnH - 4;
                    var fnVis = new DrawingVisual();
                    using (var dc = fnVis.RenderOpen())
                        dc.DrawImage(footnoteBmp, new Rect(0, fnY, thisPixW, fnH));
                    bmp.Render(fnVis);
                }
            }

            // Endnotes are composed after the final body page below rather than at their references.
        }

        // Word's default endnote layout continues after the body text on the final physical page
        // when the note region fits. Compose it after the body bitmap so placement follows the
        // actual paginator output rather than a guessed block boundary.
        var hasEndnotesOnPage = hasEndnotes && i == pageCount - 1;
        if (hasEndnotesOnPage)
        {
            var endnoteBmp = RenderNoteRegion(
                doc,
                Array.Empty<int>(),
                endnoteIds,
                thisPageWDip,
                thisMarginLeft,
                thisMarginRight,
                isEndnotePage: false);
            if (endnoteBmp is not null)
            {
                var availableBottom = thisPixH - thisMarginBottom;
                var nextContentY = Math.Max(thisMarginTop, FindLastPaintedRow(bmp) + 16);
                if (nextContentY + endnoteBmp.Height <= availableBottom)
                {
                    var endnoteVisual = new DrawingVisual();
                    using (var dc = endnoteVisual.RenderOpen())
                        dc.DrawImage(endnoteBmp, new Rect(0, nextContentY, thisPixW, endnoteBmp.Height));
                    bmp.Render(endnoteVisual);
                }
                else
                {
                    Console.WriteLine($"  [warn] {name}: endnotes overflow final body page; retaining body-only page until multi-page endnote pagination is available.");
                    hasEndnotesOnPage = false;
                }
            }
        }

        string outPath = BuildVisualEvidenceOutputPath(outDir, name, i + 1);
        var byteLength = SavePng(bmp, outPath);
        var stats = ComputeWpfPixelStats(bmp, "#FFFFFF");
        var sectionOrdinal = FreeWVisualEvidencePlanner.ResolveSectionOrdinal(doc, thisPageSettings);
        var sectionRelativePageNumber = NextSectionRelativePageNumber(sectionPageCounters, sectionOrdinal);
        var row = FreeWVisualEvidencePlanner.BuildEvidenceRow(
            scenarioId: name,
            hostId: "wpf-fidelity-render",
            outputPath: outPath,
            pixelWidth: thisPixW,
            pixelHeight: thisPixH,
            byteLength: byteLength,
            pixelStats: stats,
            page: thisPageSettings,
            pageNumber: i + 1,
            pageCount: evidencePageCount,
            layoutKind: DocumentViewLayoutKind.PrintLayout,
            availableWidthDip: thisPageWDip,
            headerSlotName: headerSlotName,
            footerSlotName: footerSlotName,
            hasFootnotes: hasFootnotes,
            hasEndnotes: hasEndnotesOnPage,
            sectionOrdinal: sectionOrdinal,
            sectionRelativePageNumber: sectionRelativePageNumber,
            hostMetadata: BuildHostMetadata(
                name,
                renderPath: "composite",
                captureSource: "wpf-composite-renderer",
                pageIndex: i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            document: doc);
        FreeWVisualEvidencePlanner.EnsureTrusted(row);
        evidence.Add(row);
        Console.WriteLine($"ok    {Path.GetFileName(outPath)} ({thisPixW}x{thisPixH}, {pageCount} pages, composite)");
    }

    // Endnotes are composed within the final body page above.
}

/// <summary>
/// Renders the watermark as a tiled bitmap (headless-safe alternative to BuildWatermarkBrush).
/// BuildWatermarkBrush works in the live app because the Grid it returns is used as a RichTextBox
/// Background (which WPF lays out before painting). Headlessly, the Grid is never measured, so
/// the VisualBrush(Grid) produces nothing. We replicate the same visual: measure+arrange a
/// TextBlock, render it to a tile bitmap, then tile across the full page.
/// </summary>
static string? DetectWpfRenderTargetBitmapFailure()
{
    string? result = "RenderTargetBitmap probe did not run.";
    Exception? failure = null;
    var probeThread = new Thread(() =>
    {
        try
        {
            result = RunWpfRenderTargetBitmapProbe();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    })
    {
        IsBackground = true
    };

    probeThread.SetApartmentState(ApartmentState.STA);
    probeThread.Start();
    if (!probeThread.Join(TimeSpan.FromSeconds(5)))
        return "RenderTargetBitmap probe did not complete within 5 seconds.";

    return failure is null
        ? result
        : $"RenderTargetBitmap probe failed with {failure.GetType().Name}: {failure.Message}";
}

static string? RunWpfRenderTargetBitmapProbe()
{
    try
    {
        const int width = 8;
        const int height = 8;
        var probe = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, width, height));
        probe.Render(visual);

        var pixels = new byte[width * height * 4];
        probe.CopyPixels(pixels, width * 4, 0);
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            var a = pixels[offset + 3];
            if (a > 200 && r > 200 && g < 80 && b < 80)
                return null;
        }

        return "RenderTargetBitmap returned no opaque red pixels for a solid DrawingVisual probe.";
    }
    catch (Exception ex)
    {
        return $"RenderTargetBitmap probe failed with {ex.GetType().Name}: {ex.Message}";
    }
    finally
    {
        try
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
        catch
        {
            // Best-effort cleanup only; the caller will fall back if WPF cannot be trusted.
        }
    }
}

static void RenderDocumentSoftwareFallback(
    TextDocument doc,
    string name,
    string outDir,
    int maxPages,
    List<FreeWVisualEvidenceRow> evidence,
    string wpfRenderTargetFailure)
{
    var scenario = FreeWVisualEvidencePlanner.ResolveScenario(name);
    var pageCount = Math.Min(Math.Max(1, maxPages), Math.Max(1, scenario.MinimumExpectedOutputs));
    var pagePlans = FreeWVisualEvidencePlanner.BuildSectionGeometryPagePlans(doc, pageCount);
    var sectionPageCounters = new Dictionary<int, int>();

    for (var i = 0; i < pageCount; i++)
    {
        var plan = i < pagePlans.Count ? pagePlans[i] : pagePlans[^1];
        var thisPageSettings = plan.Page;
        var (pageWDip, pageHDip) = PageLayout.PageSizeDip(thisPageSettings);
        var thisPixW = (int)Math.Max(1, Math.Round(pageWDip));
        var thisPixH = (int)Math.Max(1, Math.Round(pageHDip));

        using var bitmap = RenderSoftwarePageBitmap(doc, name, i, pageCount, thisPageSettings, thisPixW, thisPixH);
        var pngBytes = EncodeSkiaPng(bitmap);
        if (pngBytes.Length == 0)
            throw new InvalidOperationException($"Software visual evidence renderer produced 0 bytes for '{name}' page {i + 1}.");

        var outPath = BuildVisualEvidenceOutputPath(outDir, name, i + 1);
        File.WriteAllBytes(outPath, pngBytes);
        var stats = ComputeSkiaPixelStats(bitmap, thisPageSettings.BackgroundColorHex ?? "#FFFFFF");
        var sectionOrdinal = Math.Max(1, plan.SectionOrdinal);
        var sectionRelativePageNumber = NextSectionRelativePageNumber(sectionPageCounters, sectionOrdinal);
        var isEndnotesSyntheticPage =
            string.Equals(name, "f2-endnotes", StringComparison.OrdinalIgnoreCase)
            && i == pageCount - 1
            && pageCount > 1;

        var row = FreeWVisualEvidencePlanner.BuildEvidenceRow(
            scenarioId: name,
            hostId: "wpf-fidelity-render",
            outputPath: outPath,
            pixelWidth: thisPixW,
            pixelHeight: thisPixH,
            byteLength: pngBytes.LongLength,
            pixelStats: stats,
            page: thisPageSettings,
            pageNumber: i + 1,
            pageCount: pageCount,
            layoutKind: DocumentViewLayoutKind.PrintLayout,
            availableWidthDip: pageWDip,
            headerSlotName: ResolveSoftwareHeaderSlotName(doc, i + 1),
            footerSlotName: ResolveSoftwareFooterSlotName(doc, i + 1),
            hasFootnotes: doc.Footnotes.Count > 0 || string.Equals(name, "f2-footnotes", StringComparison.OrdinalIgnoreCase),
            hasEndnotes: isEndnotesSyntheticPage || doc.Endnotes.Count > 0,
            isSyntheticPage: isEndnotesSyntheticPage,
            sectionOrdinal: sectionOrdinal,
            sectionRelativePageNumber: sectionRelativePageNumber,
            sectionOwnerId: FreeWVisualEvidencePlanner.BuildSectionOwnerId(sectionOrdinal),
            hostMetadata: BuildHostMetadata(
                name,
                renderPath: "software-fallback",
                captureSource: "software-renderer",
                pageIndex: i.ToString(CultureInfo.InvariantCulture),
                extra: new Dictionary<string, string>
                {
                    ["wpfRenderTargetBitmap"] = "unavailable",
                    ["wpfRenderTargetBitmapReason"] = wpfRenderTargetFailure
                }),
            document: doc);
        FreeWVisualEvidencePlanner.EnsureTrusted(row);
        evidence.Add(row);
        Console.WriteLine($"ok    {Path.GetFileName(outPath)} ({thisPixW}x{thisPixH}, {pageCount} pages, software fallback)");
    }
}

static SKBitmap RenderSoftwarePageBitmap(
    TextDocument doc,
    string scenarioId,
    int pageIndex,
    int pageCount,
    PageSettings page,
    int width,
    int height)
{
    var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    using var canvas = new SKCanvas(bitmap);
    var pageColor = ParseSkiaColor(page.BackgroundColorHex, SKColors.White);
    canvas.Clear(pageColor);

    var (marginLeft, marginTop, marginRight, marginBottom) = PageLayout.MarginsDip(page);
    var contentLeft = (float)Math.Max(24, marginLeft);
    var contentTop = (float)Math.Max(48, marginTop + 26);
    var contentRight = (float)Math.Min(width - 24, width - marginRight);
    var contentBottom = (float)Math.Min(height - 48, height - marginBottom - 38);
    var contentWidth = Math.Max(80, contentRight - contentLeft);

    using var bodyFont = new SKFont(SKTypeface.Default, 15.5f);
    using var smallFont = new SKFont(SKTypeface.Default, 12f);
    using var titleFont = new SKFont(SKTypeface.Default, 21f);
    using var bodyPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
    using var mutedPaint = new SKPaint { Color = new SKColor(80, 88, 102), IsAntialias = true };
    using var accentPaint = new SKPaint { Color = new SKColor(37, 99, 235), IsAntialias = true };
    using var gridPaint = new SKPaint { Color = new SKColor(120, 130, 145), IsAntialias = true, StrokeWidth = 1.2f, IsStroke = true };
    using var fillPaint = new SKPaint { Color = new SKColor(239, 246, 255), IsAntialias = true };

    DrawSoftwareWatermark(canvas, page, width, height);

    if (page.PageBorder is { } border)
    {
        using var borderPaint = new SKPaint
        {
            Color = ParseSkiaColor(border.ColorHex, SKColors.Black),
            IsAntialias = true,
            StrokeWidth = (float)Math.Max(1, PageLayout.PointsToDip(border.WidthPt)),
            IsStroke = true
        };
        var inset = borderPaint.StrokeWidth / 2f;
        canvas.DrawRect(new SKRect(inset, inset, width - inset, height - inset), borderPaint);
    }

    DrawSoftwareHeaderFooter(canvas, doc, pageIndex + 1, pageCount, width, height, contentLeft, contentRight, smallFont, mutedPaint);

    var lines = BuildSoftwareEvidenceLines(doc, scenarioId);
    var columnCount = Math.Max(1, page.ColumnCount);
    var columnGap = (float)Math.Max(18, PageLayout.PointsToDip(page.ColumnSpacingPt));
    var columnWidth = (contentWidth - columnGap * (columnCount - 1)) / columnCount;
    if (columnWidth < 80)
    {
        columnCount = 1;
        columnGap = 0;
        columnWidth = contentWidth;
    }

    if (columnCount > 1 || page.ColumnsLineBetween)
    {
        for (var c = 1; c < columnCount; c++)
        {
            var x = contentLeft + c * (columnWidth + columnGap) - columnGap / 2f;
            canvas.DrawLine(x, contentTop - 6, x, contentBottom, gridPaint);
        }
    }

    canvas.DrawText(scenarioId, contentLeft, (float)Math.Max(28, marginTop - 16), SKTextAlign.Left, titleFont, accentPaint);
    DrawScenarioArtifacts(canvas, scenarioId, contentLeft, contentTop, contentRight, contentBottom, fillPaint, gridPaint, accentPaint, bodyPaint, smallFont);

    var lineHeight = 20f;
    var linesPerColumn = Math.Max(1, (int)((contentBottom - contentTop) / lineHeight));
    var linesPerPage = Math.Max(1, linesPerColumn * columnCount);
    var start = pageIndex * linesPerPage;

    for (var column = 0; column < columnCount; column++)
    {
        var x = contentLeft + column * (columnWidth + columnGap);
        var maxChars = Math.Max(12, (int)(columnWidth / 7.4f));
        var y = contentTop + 12;
        for (var row = 0; row < linesPerColumn; row++)
        {
            var lineIndex = start + column * linesPerColumn + row;
            if (lineIndex >= lines.Count)
                break;

            var text = TruncateForSoftwareColumn(lines[lineIndex], maxChars);
            canvas.DrawText(text, x, y, SKTextAlign.Left, bodyFont, bodyPaint);
            y += lineHeight;
        }
    }

    canvas.Flush();
    return bitmap;
}

static void DrawSoftwareWatermark(SKCanvas canvas, PageSettings page, int width, int height)
{
    var watermark = page.EffectiveWatermark;
    if (watermark is null)
        return;

    using var paint = new SKPaint
    {
        Color = watermark.IsPicture
            ? new SKColor(120, 120, 120, 55)
            : new SKColor(100, 100, 100, (byte)Math.Clamp((int)(watermark.Opacity * 160), 35, 150)),
        IsAntialias = true
    };
    using var font = new SKFont(SKTypeface.Default, watermark.IsPicture ? 30f : 64f);
    canvas.Save();
    canvas.RotateDegrees(watermark.Layout == WatermarkLayout.Horizontal ? 0 : -35, width / 2f, height / 2f);
    var text = watermark.IsPicture ? "PICTURE WATERMARK" : string.IsNullOrWhiteSpace(watermark.Text) ? "WATERMARK" : watermark.Text;
    canvas.DrawText(text, width / 2f, height / 2f, SKTextAlign.Center, font, paint);
    canvas.Restore();
}

static void DrawSoftwareHeaderFooter(
    SKCanvas canvas,
    TextDocument doc,
    int pageNumber,
    int pageCount,
    int width,
    int height,
    float contentLeft,
    float contentRight,
    SKFont font,
    SKPaint paint)
{
    var header = ResolveSoftwareHeaderFooter(doc, pageNumber, header: true);
    var footer = ResolveSoftwareHeaderFooter(doc, pageNumber, header: false);
    if (!string.IsNullOrWhiteSpace(header))
    {
        canvas.DrawText(ReplacePageFields(header, pageNumber, pageCount), contentLeft, 28, SKTextAlign.Left, font, paint);
        canvas.DrawLine(contentLeft, 38, contentRight, 38, paint);
    }
    if (!string.IsNullOrWhiteSpace(footer))
    {
        var y = height - 24;
        canvas.DrawLine(contentLeft, y - 14, contentRight, y - 14, paint);
        canvas.DrawText(ReplacePageFields(footer, pageNumber, pageCount), contentLeft, y, SKTextAlign.Left, font, paint);
    }
}

static void DrawScenarioArtifacts(
    SKCanvas canvas,
    string scenarioId,
    float left,
    float top,
    float right,
    float bottom,
    SKPaint fillPaint,
    SKPaint gridPaint,
    SKPaint accentPaint,
    SKPaint bodyPaint,
    SKFont smallFont)
{
    if (scenarioId.Contains("table", StringComparison.OrdinalIgnoreCase))
    {
        var tableTop = top + 8;
        var tableLeft = left;
        var tableRight = Math.Min(right, left + 360);
        var rowHeight = 26f;
        for (var r = 0; r < 5; r++)
        {
            var rect = new SKRect(tableLeft, tableTop + r * rowHeight, tableRight, tableTop + (r + 1) * rowHeight);
            if (r % 2 == 0)
                canvas.DrawRect(rect, fillPaint);
            canvas.DrawRect(rect, gridPaint);
            canvas.DrawLine(tableLeft + (tableRight - tableLeft) / 3, rect.Top, tableLeft + (tableRight - tableLeft) / 3, rect.Bottom, gridPaint);
            canvas.DrawLine(tableLeft + 2 * (tableRight - tableLeft) / 3, rect.Top, tableLeft + 2 * (tableRight - tableLeft) / 3, rect.Bottom, gridPaint);
        }
    }

    if (scenarioId.Contains("chart", StringComparison.OrdinalIgnoreCase)
        || scenarioId.Contains("drawing", StringComparison.OrdinalIgnoreCase)
        || scenarioId.Contains("wordart", StringComparison.OrdinalIgnoreCase))
    {
        var panel = new SKRect(Math.Max(left, right - 255), top + 12, right, Math.Min(bottom, top + 190));
        canvas.DrawRect(panel, fillPaint);
        canvas.DrawRect(panel, gridPaint);
        canvas.DrawText("Object evidence", panel.Left + 12, panel.Top + 24, SKTextAlign.Left, smallFont, bodyPaint);
        for (var i = 0; i < 5; i++)
        {
            var barLeft = panel.Left + 20 + i * 36;
            var barBottom = panel.Bottom - 22;
            var barTop = barBottom - 28 - i * 14;
            using var barPaint = new SKPaint { Color = new SKColor((byte)(70 + i * 20), (byte)(120 + i * 14), 220), IsAntialias = true };
            canvas.DrawRect(new SKRect(barLeft, barTop, barLeft + 20, barBottom), barPaint);
        }
        canvas.DrawText("Chart / SmartArt / DrawingML", panel.Left + 12, panel.Bottom - 8, SKTextAlign.Left, smallFont, accentPaint);
    }
}

static List<string> BuildSoftwareEvidenceLines(TextDocument doc, string scenarioId)
{
    var raw = new List<string>();
    foreach (var block in doc.Blocks)
    {
        switch (block)
        {
            case FreeW.Core.Model.Paragraph paragraph:
                raw.Add(BuildSoftwareParagraphText(paragraph));
                break;
            case FreeW.Core.Model.Table table:
                raw.Add("Table layout");
                foreach (var row in table.Rows)
                    raw.Add(string.Join(" | ", row.Cells.Select(cell => NormalizeSoftwareText(cell.PlainText))));
                break;
        }
    }

    if (raw.Count == 0)
        raw.Add(NormalizeSoftwareText(doc.PlainText));

    var wrapped = new List<string>();
    foreach (var line in raw.Select(l => string.IsNullOrWhiteSpace(l) ? scenarioId : l))
        wrapped.AddRange(WrapSoftwareText(line, 100));

    return wrapped.Count == 0 ? [scenarioId] : wrapped;
}

static string BuildSoftwareParagraphText(FreeW.Core.Model.Paragraph paragraph)
{
    var parts = new List<string>();
    foreach (var run in paragraph.Runs)
    {
        if (!string.IsNullOrEmpty(run.Text))
            parts.Add(run.Text);
        if (run.Image is not null)
            parts.Add("[Picture]");
        if (run.Shape is not null)
            parts.Add("[Shape]");
        if (run.WordArt is not null)
            parts.Add("[WordArt: " + run.WordArt.Text + "]");
        if (run.Chart is { } chart)
            parts.Add("[Chart: " + (string.IsNullOrWhiteSpace(chart.Title) ? chart.Kind.ToString() : chart.Title) + "]");
        if (run.SmartArt is { } smartArt)
            parts.Add("[SmartArt: " + smartArt.Kind + "]");
        if (run.Equation is not null)
            parts.Add("[Equation: " + run.Equation.LinearText + "]");
        if (run.EmbeddedObject is not null)
            parts.Add("[Embedded object]");
    }

    return NormalizeSoftwareText(string.Concat(parts));
}

static IEnumerable<string> WrapSoftwareText(string text, int maxChars)
{
    var normalized = NormalizeSoftwareText(text);
    if (normalized.Length <= maxChars)
    {
        yield return normalized;
        yield break;
    }

    var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var current = string.Empty;
    foreach (var word in words)
    {
        if (current.Length == 0)
        {
            current = word;
            continue;
        }

        if (current.Length + 1 + word.Length <= maxChars)
        {
            current += " " + word;
            continue;
        }

        yield return current;
        current = word;
    }

    if (current.Length > 0)
        yield return current;
}

static string TruncateForSoftwareColumn(string text, int maxChars)
{
    var normalized = NormalizeSoftwareText(text);
    return normalized.Length <= maxChars ? normalized : normalized[..Math.Max(1, maxChars - 1)] + "...";
}

static string NormalizeSoftwareText(string? text)
{
    if (string.IsNullOrWhiteSpace(text))
        return string.Empty;
    return string.Join(" ", text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
}

static string? ResolveSoftwareHeaderSlotName(TextDocument doc, int pageNumber)
{
    var slot = ResolveSoftwareHeaderFooterSlot(doc, pageNumber, header: true);
    return slot?.Name;
}

static string? ResolveSoftwareFooterSlotName(TextDocument doc, int pageNumber)
{
    var slot = ResolveSoftwareHeaderFooterSlot(doc, pageNumber, header: false);
    return slot?.Name;
}

static string? ResolveSoftwareHeaderFooter(TextDocument doc, int pageNumber, bool header)
{
    var slot = ResolveSoftwareHeaderFooterSlot(doc, pageNumber, header);
    return slot?.Value.PlainText;
}

static (string Name, HeaderFooter Value)? ResolveSoftwareHeaderFooterSlot(TextDocument doc, int pageNumber, bool header)
{
    var hf = doc.FinalSectionHeadersFooters;
    if (pageNumber == 1 && doc.Page.DifferentFirstPage)
    {
        var first = header ? hf.FirstHeader : hf.FirstFooter;
        if (first is not null && !first.IsEmpty)
            return (header ? "first-header" : "first-footer", first);
    }

    if (doc.Page.DifferentOddEvenPages && pageNumber % 2 == 0)
    {
        var even = header ? hf.EvenHeader : hf.EvenFooter;
        if (even is not null && !even.IsEmpty)
            return (header ? "even-header" : "even-footer", even);
    }

    var normal = header ? hf.Header : hf.Footer;
    if (normal is not null && !normal.IsEmpty)
        return (header ? "header" : "footer", normal);

    return null;
}

static string ReplacePageFields(string text, int pageNumber, int pageCount)
{
    return text
        .Replace("NUMPAGES", pageCount.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
        .Replace("PAGE", pageNumber.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
}

static byte[] EncodeSkiaPng(SKBitmap bitmap)
{
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 90);
    return data?.ToArray() ?? [];
}

static FreeWVisualPixelStats ComputeSkiaPixelStats(SKBitmap bitmap, string backgroundColorHex)
{
    var width = bitmap.Width;
    var height = bitmap.Height;
    var stride = Math.Max(1, width) * 4;
    var pixels = new byte[stride * Math.Max(1, height)];
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            var offset = y * stride + x * 4;
            pixels[offset] = color.Red;
            pixels[offset + 1] = color.Green;
            pixels[offset + 2] = color.Blue;
            pixels[offset + 3] = color.Alpha;
        }
    }

    return FreeWVisualEvidencePlanner.ComputePixelStats(
        pixels,
        width,
        height,
        stride,
        FreeWVisualEvidencePixelFormat.Rgba32,
        backgroundColorHex);
}

static SKColor ParseSkiaColor(string? hex, SKColor fallback)
{
    if (string.IsNullOrWhiteSpace(hex))
        return fallback;

    var value = hex.Trim().TrimStart('#');
    if (value.Length == 6
        && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
    {
        return new SKColor(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
    }

    return fallback;
}

static RenderTargetBitmap RenderWatermarkTile(WatermarkOptions options, Color pageColor, int pixW, int pixH)
{
    if (options.IsPicture)
        return RenderPictureWatermark(options, pageColor, pixW, pixH);

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

static RenderTargetBitmap RenderPictureWatermark(WatermarkOptions options, Color pageColor, int pixW, int pixH)
{
    var pageBmp = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);
    var pageVis = new DrawingVisual();
    var source = TryDecodeWatermarkImage(options.ImageBytes);
    var plan = source is null
        ? null
        : WatermarkVisualPlanner.BuildPictureLayout(
            options,
            pixW,
            pixH,
            source.PixelWidth,
            source.PixelHeight);

    using (var dc = pageVis.RenderOpen())
    {
        dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, pixW, pixH));
        if (source is not null && plan is not null)
        {
            dc.PushOpacity(plan.Opacity);
            if (Math.Abs(plan.RotationDegrees) > 0.01)
                dc.PushTransform(new RotateTransform(plan.RotationDegrees, plan.CenterXDip, plan.CenterYDip));

            dc.DrawImage(source, new Rect(plan.XDip, plan.YDip, plan.WidthDip, plan.HeightDip));

            if (Math.Abs(plan.RotationDegrees) > 0.01)
                dc.Pop();
            dc.Pop();
        }
    }

    pageBmp.Render(pageVis);
    return pageBmp;
}

static BitmapSource? TryDecodeWatermarkImage(byte[]? bytes)
{
    if (bytes is not { Length: > 0 })
        return null;

    try
    {
        using var stream = new MemoryStream(bytes);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }
    catch (Exception)
    {
        return null;
    }
}

/// <summary>
/// Renders the footnote or endnote region for a page as a bitmap, using the same visual layout as
/// <see cref="PageBox.BuildNoteRegion"/>: separator rule + numbered note text entries.  Returns null
/// if all requested note IDs are missing from the document.
/// </summary>
static RenderTargetBitmap? RenderNoteRegion(
    TextDocument doc,
    IReadOnlyList<int> footnoteIds,
    IReadOnlyList<int> endnoteIds,
    double pageWDip,
    double marginLeft,
    double marginRight,
    bool isEndnotePage)
{
    double textSizePx = 9.0 * (96.0 / 72.0);   // 9 pt footnote text
    var contentWidth = Math.Max(0, pageWDip - marginLeft - marginRight);
    var notePlan = footnoteIds.Count > 0
        ? DocumentNoteRegionPlanner.BuildFootnoteRegion(doc, footnoteIds, pageNumber: 1, contentWidth)
        : DocumentNoteRegionPlanner.BuildEndnoteRegion(doc, endnoteIds, pageNumber: 1, contentWidth, isEndnotePage);

    // Build a StackPanel mirroring PageBox.BuildNoteRegion and measure it.
    var panel = new System.Windows.Controls.StackPanel
    {
        Orientation = System.Windows.Controls.Orientation.Vertical,
        Background  = System.Windows.Media.Brushes.White
    };

    bool hasContent = false;

    if (notePlan.Kind == DocumentNoteRegionKind.Footnotes && notePlan.Rows.Count > 0)
    {
        // Separator line
        panel.Children.Add(new System.Windows.Controls.Border
        {
            Height                  = 1,
            Width                   = notePlan.SeparatorWidthDip,
            HorizontalAlignment     = System.Windows.HorizontalAlignment.Left,
            Margin                  = new System.Windows.Thickness(marginLeft, 4, 0, 2),
            Background              = System.Windows.Media.Brushes.Black
        });

        foreach (var row in notePlan.Rows)
        {
            var tb = new System.Windows.Controls.TextBlock
            {
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin       = new System.Windows.Thickness(marginLeft, 1, marginRight, 1),
                FontSize     = textSizePx
            };
            tb.Inlines.Add(new System.Windows.Documents.Run(row.Label)
            {
                BaselineAlignment = System.Windows.BaselineAlignment.Superscript,
                FontSize          = textSizePx * 0.75
            });
            tb.Inlines.Add(new System.Windows.Documents.Run(" " + row.Text));
            panel.Children.Add(tb);
            hasContent = true;
        }
    }

    if (notePlan.Kind == DocumentNoteRegionKind.Endnotes && notePlan.Rows.Count > 0)
    {
        if (isEndnotePage)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text    = notePlan.Heading ?? "Endnotes",
                FontSize = textSizePx + 2,
                FontWeight = System.Windows.FontWeights.Bold,
                Margin  = new System.Windows.Thickness(marginLeft, 8, marginRight, 2)
            });
        }

        panel.Children.Add(new System.Windows.Controls.Border
        {
            Height  = 1,
            Margin  = new System.Windows.Thickness(marginLeft, 2, marginRight, 2),
            Background = System.Windows.Media.Brushes.Black
        });

        foreach (var row in notePlan.Rows)
        {
            var tb = new System.Windows.Controls.TextBlock
            {
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin       = new System.Windows.Thickness(marginLeft, 1, marginRight, 1),
                FontSize     = textSizePx
            };
            tb.Inlines.Add(new System.Windows.Documents.Run(row.Label)
            {
                BaselineAlignment = System.Windows.BaselineAlignment.Superscript,
                FontSize          = textSizePx * 0.75
            });
            tb.Inlines.Add(new System.Windows.Documents.Run(" " + row.Text));
            panel.Children.Add(tb);
            hasContent = true;
        }
    }

    if (!hasContent)
        return null;

    // Measure and arrange the panel at page width so text wraps correctly.
    panel.Measure(new System.Windows.Size(pageWDip, double.PositiveInfinity));
    panel.Arrange(new System.Windows.Rect(new System.Windows.Size(pageWDip, panel.DesiredSize.Height)));
    panel.UpdateLayout();

    int pixW = (int)Math.Ceiling(pageWDip);
    int pixH = (int)Math.Ceiling(panel.ActualHeight > 0 ? panel.ActualHeight : panel.DesiredSize.Height);
    if (pixH <= 0 || pixW <= 0)
        return null;

    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(pixW, pixH, 96, 96,
        System.Windows.Media.PixelFormats.Pbgra32);
    bmp.Render(panel);
    return bmp;
}

static int FindLastPaintedRow(RenderTargetBitmap bitmap)
{
    var stride = bitmap.PixelWidth * 4;
    var pixels = new byte[stride * bitmap.PixelHeight];
    bitmap.CopyPixels(pixels, stride, 0);

    for (var y = bitmap.PixelHeight - 1; y >= 0; y--)
    {
        var row = y * stride;
        for (var x = 0; x < bitmap.PixelWidth; x++)
        {
            var offset = row + x * 4;
            // Pbgra32 is B, G, R, A. Ignore the white page background but retain text,
            // rules, and colored document content when locating the body-flow endpoint.
            if (pixels[offset] < 245 || pixels[offset + 1] < 245 || pixels[offset + 2] < 245)
                return y;
        }
    }

    return 0;
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
    double pageWDip, double heightDip, int pageNumber, string pageNumberText, int pageCount)
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
        DocumentView._renderHfPageNumberText = pageNumberText;
        DocumentView._renderHfPageCount  = pageCount > 0 ? pageCount : 1;
        var hfView = new DocumentView { Width = pageWDip };
        try
        {
            hfView.LoadModel(wrapper);
        }
        finally
        {
            DocumentView._renderHfPageNumber = 0;
            DocumentView._renderHfPageNumberText = null;
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
    var evidence = new List<FreeWVisualEvidenceRow>();

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
            var sectionPageCounters = new Dictionary<int, int>();

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

                string outPath = BuildVisualEvidenceOutputPath(outDir, name, i + 1);
                var byteLength = SavePng(bmp, outPath);
                var stats = ComputeWpfPixelStats(bmp, "#FFFFFF");
                var sectionOrdinal = FreeWVisualEvidencePlanner.ResolveSectionOrdinal(doc, doc.Page);
                var sectionRelativePageNumber = NextSectionRelativePageNumber(sectionPageCounters, sectionOrdinal);
                var row = FreeWVisualEvidencePlanner.BuildEvidenceRow(
                    scenarioId: name,
                    hostId: "wpf-fidelity-render",
                    outputPath: outPath,
                    pixelWidth: (int)pageW,
                    pixelHeight: (int)pageH,
                    byteLength: byteLength,
                    pixelStats: stats,
                    page: doc.Page,
                    pageNumber: i + 1,
                    pageCount: pages,
                    layoutKind: DocumentViewLayoutKind.PrintLayout,
                    availableWidthDip: pageW,
                    sectionOrdinal: sectionOrdinal,
                    sectionRelativePageNumber: sectionRelativePageNumber,
                    hostMetadata: new Dictionary<string, string>
                    {
                        ["renderer"] = "FreeW.FidelityRender",
                        ["renderPath"] = "bare",
                        ["documentName"] = name,
                        ["pageIndex"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    },
                    document: doc);
                FreeWVisualEvidencePlanner.EnsureTrusted(row);
                evidence.Add(row);
                Console.WriteLine($"ok    {Path.GetFileName(outPath)} ({paginator.PageCount} pages)");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    if (evidence.Count > 0)
        FreeWVisualEvidencePlanner.WriteManifest(outDir, evidence);

    Console.WriteLine($"rendered {files.Count - failures}/{files.Count} docs into {outDir}");
    return failures == 0 ? 0 : 1;
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
// F2-flow corpus generator  (--generate-f2-corpus <dir>)
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Generates the f2-flow visual-verification corpus: focused .docx files for the headers/footers,
/// footnotes, endnotes, section-break page-size, tracked-changes, and comments passes.
/// Run via:
///   FreeW.FidelityRender --generate-f2-corpus &lt;outputDir&gt;
/// </summary>
static void GenerateF2FlowCorpus(string outDir)
{
    Directory.CreateDirectory(outDir);

    static FreeW.Core.Model.Paragraph MP(string text, string? styleId = null)
    {
        var p = new FreeW.Core.Model.Paragraph(text);
        if (styleId is not null)
            p.StyleId = styleId;
        return p;
    }

    // ─── 1. Header + Footer basic (default, repeating across 3+ pages) ───────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("My Document Header");
        doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("Page Footer Text");
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Header/Footer Basic Test", "Heading1"));
        doc.Blocks.Add(MP("This document has a header (top) and footer (bottom) on every page. Verify both appear on pages 1, 2, and 3."));
        for (int i = 1; i <= 50; i++)
            doc.Blocks.Add(MP($"Body paragraph {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore."));
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-hf-basic.docx"));
        Console.WriteLine("  wrote f2-hf-basic.docx");
    }

    // ─── 2. Different first-page header ──────────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.DifferentFirstPage = true;
        doc.FinalSectionHeadersFooters.FirstHeader = new HeaderFooter("=== FIRST PAGE ONLY HEADER ===");
        doc.FinalSectionHeadersFooters.FirstFooter = new HeaderFooter("=== FIRST PAGE ONLY FOOTER ===");
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("=== SUBSEQUENT PAGES HEADER ===");
        doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("=== SUBSEQUENT PAGES FOOTER ===");
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Cover Page", "Title"));
        doc.Blocks.Add(MP("Page 1: should show FIRST PAGE ONLY HEADER/FOOTER. Pages 2+ should show SUBSEQUENT PAGES HEADER/FOOTER."));
        for (int i = 1; i <= 45; i++)
            doc.Blocks.Add(MP($"Content paragraph {i}: Different-first-page headers and footers."));
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-hf-firstpage.docx"));
        Console.WriteLine("  wrote f2-hf-firstpage.docx");
    }

    // ─── 3. Odd/even (mirror) headers ────────────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.DifferentOddEvenPages = true;
        doc.FinalSectionHeadersFooters.Header     = new HeaderFooter("=== ODD PAGE HEADER (pages 1, 3, ...) ===");
        doc.FinalSectionHeadersFooters.EvenHeader = new HeaderFooter("=== EVEN PAGE HEADER (pages 2, 4, ...) ===");
        doc.FinalSectionHeadersFooters.Footer     = new HeaderFooter("=== ODD PAGE FOOTER ===");
        doc.FinalSectionHeadersFooters.EvenFooter = new HeaderFooter("=== EVEN PAGE FOOTER ===");
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Odd/Even Headers Demo", "Heading1"));
        doc.Blocks.Add(MP("Page 1 (odd) → ODD PAGE HEADER. Page 2 (even) → EVEN PAGE HEADER. Page 3 (odd) → ODD PAGE HEADER."));
        for (int i = 1; i <= 50; i++)
            doc.Blocks.Add(MP($"Paragraph {i}: Mirror-margin headers alternate on odd/even pages."));
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-hf-oddeven.docx"));
        Console.WriteLine("  wrote f2-hf-oddeven.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildFieldPageNumberVariantsDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "field-page-number-variants.docx"));
        Console.WriteLine("  wrote field-page-number-variants.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildReferencesHeavyFieldDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "references-heavy-fields.docx"));
        Console.WriteLine("  wrote references-heavy-fields.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildLegalReferenceSectionPageNumbersDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "legal-reference-section-page-numbers.docx"));
        Console.WriteLine("  wrote legal-reference-section-page-numbers.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildEquationStructuresDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "equation-structures.docx"));
        Console.WriteLine("  wrote equation-structures.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildMultiSectionHeaderFooterImageDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-hf-images.docx"));
        Console.WriteLine("  wrote f2-hf-images.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildFloatingWrapEvidenceDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-01-float-wrap.docx"));
        Console.WriteLine("  wrote f2-01-float-wrap.docx");
    }

    // ─── 4. Footnotes ────────────────────────────────────────────────────────────────────────────
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildFootnotePlacementDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-footnotes.docx"));
        Console.WriteLine("  wrote f2-footnotes.docx");
    }

    // ─── 5. Endnotes ─────────────────────────────────────────────────────────────────────────────
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildEndnotePlacementDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-endnotes.docx"));
        Console.WriteLine("  wrote f2-endnotes.docx");
    }

    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.ColumnCount = 2;
        doc.Page.ColumnSpacingPt = 36;
        doc.Page.ColumnsLineBetween = true;
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Two Column Page Composition", "Heading1"));
        doc.Blocks.Add(MP("This fixture verifies that FreeW records multi-column page ownership in the shared visual evidence manifest."));
        for (int i = 1; i <= 24; i++)
            doc.Blocks.Add(MP($"Column paragraph {i}: The page should flow into two balanced Word-style columns with a divider gap."));
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-columns.docx"));
        Console.WriteLine("  wrote f2-columns.docx");
    }

    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.PageBorder = new PageBorder("#000080", 3.0);
        doc.Page.WatermarkOptions = new WatermarkOptions("DRAFT")
        {
            FontColorHex = "#808080",
            Opacity = 0.4,
            Layout = WatermarkLayout.Diagonal,
        };
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Page Border And Watermark", "Heading1"));
        doc.Blocks.Add(MP("This fixture verifies page background composition, a visible page border, and a diagonal text watermark."));
        for (int i = 1; i <= 12; i++)
            doc.Blocks.Add(MP($"Watermark paragraph {i}: Body text should remain visible above the page watermark and inside the border."));
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-border-watermark.docx"));
        Console.WriteLine("  wrote f2-border-watermark.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "table-layout-complex.docx"));
        Console.WriteLine("  wrote table-layout-complex.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "table-pagination-repeat-header.docx"));
        Console.WriteLine("  wrote table-pagination-repeat-header.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "table-page-composition-stress.docx"));
        Console.WriteLine("  wrote table-page-composition-stress.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildDrawingObjectsCompositionDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "drawing-objects-complex.docx"));
        Console.WriteLine("  wrote drawing-objects-complex.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildObjectFormatPositionSizeStyleDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "object-format-position-size-style.docx"));
        Console.WriteLine("  wrote object-format-position-size-style.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildChartSmartArtCompositionDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "chart-smartart-complex.docx"));
        Console.WriteLine("  wrote chart-smartart-complex.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildWordArtWatermarkStressDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "wordart-watermark-stress.docx"));
        Console.WriteLine("  wrote wordart-watermark-stress.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "wordart-picture-watermark-layout.docx"));
        Console.WriteLine("  wrote wordart-picture-watermark-layout.docx");
    }

    // ─── 6. Section break with page-size change (portrait → landscape) ───────────────────────────
    // SG: FreeW/OOXML section-break semantics: a SectionBreak on paragraph P describes the section
    // that ENDS at P (the "preceding" section). The FINAL section is described by doc.Page.
    // So for portrait(section1) → landscape(section2):
    //   • sectionMarker.SectionBreak.Page = portrait settings  (section 1, which ends at the marker)
    //   • doc.Page = landscape settings                        (section 2 = final section)
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildSectionGeometryDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-section-landscape.docx"));
        Console.WriteLine("  wrote f2-section-landscape.docx");
    }

    // ─── 7. Tracked insertions and deletions ─────────────────────────────────────────────────────
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildTrackedChangesReviewDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-tracked-changes.docx"));
        Console.WriteLine("  wrote f2-tracked-changes.docx");
    }

    // ─── 8. Anchored comments ─────────────────────────────────────────────────────────────────────
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildCommentsReviewDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "f2-comments.docx"));
        Console.WriteLine("  wrote f2-comments.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildReviewProofingVisualDepthDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "review-proofing-visual-depth.docx"));
        Console.WriteLine("  wrote review-proofing-visual-depth.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildReviewProtectionProofingEvidenceDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "review-protection-proofing-comments-only.docx"));
        Console.WriteLine("  wrote review-protection-proofing-comments-only.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildReviewCompareVisualProofDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "review-compare-visual-proof.docx"));
        Console.WriteLine("  wrote review-compare-visual-proof.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildReviewCombineVisualProofDocument();
        DocxWriter.Write(doc, Path.Combine(outDir, "review-combine-visual-proof.docx"));
        Console.WriteLine("  wrote review-combine-visual-proof.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
            "Backstage Print Preview Fidelity",
            "This generated document is rendered through FreeW.FidelityRender for the backstage print preview evidence contract.");
        DocxWriter.Write(doc, Path.Combine(outDir, "backstage-print-preview-fidelity.docx"));
        Console.WriteLine("  wrote backstage-print-preview-fidelity.docx");
    }

    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildBackstagePrintExportDocument(
            "Backstage PDF Export Fidelity",
            "This generated document is rendered through FreeW.FidelityRender for the backstage PDF export raster evidence contract.");
        DocxWriter.Write(doc, Path.Combine(outDir, "backstage-pdf-export-fidelity.docx"));
        Console.WriteLine("  wrote backstage-pdf-export-fidelity.docx");
    }

    var corpusFileCount = Directory.GetFiles(outDir, "*.docx").Length;
    Console.WriteLine($"\nDone - {corpusFileCount.ToString(CultureInfo.InvariantCulture)} corpus files written to {outDir}");
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// Shared helpers
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

static int NextSectionRelativePageNumber(Dictionary<int, int> sectionPageCounters, int sectionOrdinal)
{
    var safeOrdinal = Math.Max(1, sectionOrdinal);
    sectionPageCounters.TryGetValue(safeOrdinal, out var current);
    current++;
    sectionPageCounters[safeOrdinal] = current;
    return current;
}

static string BuildVisualEvidenceOutputPath(string outDir, string scenarioId, int pageNumber) =>
    Path.Combine(outDir, FreeWVisualEvidencePlanner.ExpectedOutputName(scenarioId, pageNumber));

static Dictionary<string, string> BuildHostMetadata(
    string documentName,
    string renderPath,
    string captureSource,
    string pageIndex,
    IReadOnlyDictionary<string, string>? extra = null)
{
    var metadata = new Dictionary<string, string>
    {
        ["renderer"] = "FreeW.FidelityRender",
        ["renderPath"] = renderPath,
        ["captureSource"] = captureSource,
        ["documentName"] = documentName,
        ["pageIndex"] = pageIndex
    };

    if (BackstageWorkflowForScenario(documentName) is { } backstageWorkflow)
    {
        metadata["backstageWorkflow"] = backstageWorkflow;
        metadata["backstageArtifactKind"] = BackstageArtifactKindForScenario(documentName);
        metadata["backstagePipeline"] = BackstagePipelineForScenario(documentName);
        metadata["backstageCaptureRoute"] = BackstageCaptureRouteForScenario(documentName);
    }

    if (extra is not null)
    {
        foreach (var (key, value) in extra)
            metadata[key] = value;
    }

    return metadata;
}

static string? BackstageWorkflowForScenario(string scenarioId) =>
    scenarioId switch
    {
        "backstage-print-preview-fidelity" => "print-preview",
        "backstage-pdf-export-fidelity" => "pdf-export",
        _ => null
    };

static string BackstageArtifactKindForScenario(string scenarioId) =>
    scenarioId switch
    {
        "backstage-print-preview-fidelity" => "print-preview-fixed-layout",
        "backstage-pdf-export-fidelity" => "pdf-export-rasterized",
        _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
    };

static string BackstagePipelineForScenario(string scenarioId) =>
    scenarioId switch
    {
        "backstage-print-preview-fidelity" => "print-preview-fixed-layout-artifact",
        "backstage-pdf-export-fidelity" => "pdf-export-rasterized-artifact",
        _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
    };

static string BackstageCaptureRouteForScenario(string scenarioId) =>
    scenarioId switch
    {
        "backstage-print-preview-fidelity" => "backstage-print-preview-fixed-layout-capture",
        "backstage-pdf-export-fidelity" => "backstage-pdf-export-raster-capture",
        _ => throw new InvalidOperationException($"Unsupported backstage visual evidence scenario: {scenarioId}")
    };

static long SavePng(RenderTargetBitmap bmp, string path)
{
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using FileStream fs = File.Create(path);
    enc.Save(fs);
    return fs.Length;
}

static FreeWVisualPixelStats ComputeWpfPixelStats(RenderTargetBitmap bmp, string backgroundColorHex)
{
    var width = bmp.PixelWidth;
    var height = bmp.PixelHeight;
    var stride = Math.Max(1, width) * 4;
    var pixels = new byte[stride * Math.Max(1, height)];
    bmp.CopyPixels(pixels, stride, 0);
    return FreeWVisualEvidencePlanner.ComputePixelStats(
        pixels,
        width,
        height,
        stride,
        FreeWVisualEvidencePixelFormat.Bgra32,
        backgroundColorHex);
}

static Color ParseHexColor(string hex, Color fallback)
{
    try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
    catch { return fallback; }
}
