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
var generateF2Corpus = false;
var filteredArgs = new List<string>();
foreach (var a in args)
{
    if (a == "--composite") composite = true;
    else if (a == "--no-composite") composite = false;
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
    Console.Error.WriteLine("usage: FreeW.FidelityRender <input.docx | inputDir> <outputDir> [maxPagesPerDoc] [--composite|--no-composite]");
    Console.Error.WriteLine("       FreeW.FidelityRender --generate-fixtures <outputDir>");
    Console.Error.WriteLine("       FreeW.FidelityRender --generate-f2-corpus <outputDir>");
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
    var evidence = new List<FreeWVisualEvidenceRow>();

    foreach (var file in files)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        try
        {
            var doc = DocxReader.Read(file);
            RenderDocumentComposite(doc, name, outDir, maxPages, evidence);
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
    List<FreeWVisualEvidenceRow> evidence)
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
    var hasSyntheticEndnotePage = panel?.PageBoxes.Any(b => b.IsEndnoteSyntheticPage && b.EndnoteIds.Count > 0) == true;
    var evidencePageCount = pageCount + (hasSyntheticEndnotePage ? 1 : 0);
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
                    var hfPage = RenderHfSlot(hfSlot, doc, thisPageWDip, hfH, i + 1, pageCount);
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
                    var hfPage = RenderHfSlot(fSlot, doc, thisPageWDip, hfH, i + 1, pageCount);
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

            // Note: EndnoteIds rendering is intentionally deferred to the post-loop synthetic page
            // (see below). Endnotes never appear inline on body pages — they collect at document end.
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
            sectionOrdinal: sectionOrdinal,
            sectionRelativePageNumber: sectionRelativePageNumber,
            hostMetadata: new Dictionary<string, string>
            {
                ["renderer"] = "FreeW.FidelityRender",
                ["renderPath"] = "composite",
                ["documentName"] = name,
                ["pageIndex"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            document: doc);
        FreeWVisualEvidencePlanner.EnsureTrusted(row);
        evidence.Add(row);
        Console.WriteLine($"ok    {Path.GetFileName(outPath)} ({thisPixW}x{thisPixH}, {pageCount} pages, composite)");
    }

    // ═══ Synthetic endnotes page (rendered after all body pages) ══════════════════════════════════
    // The PaginatedEditorPanel appends one extra PageBox for endnotes when the document has endnotes.
    // This box has no corresponding body paginator page, so we render it separately here.
    // Find it by IsEndnoteSyntheticPage (it may be at any index since overflow pagination can produce
    // a different body-page count than the panel body-box count).
    var endnotePageBox = panel?.PageBoxes.FirstOrDefault(b => b.IsEndnoteSyntheticPage);
    if (endnotePageBox is not null && endnotePageBox.EndnoteIds.Count > 0)
    {
        var endnoteBox = endnotePageBox; // the synthetic page box
        // Use the endnotes-page's own geometry (inherits final section's page settings).
        var (endnotePageWDip, endnotePageHDip) = PageLayout.PageSizeDip(endnoteBox.PageGeometry);
        var (endnoteMarginLeft, endnoteMarginTop, endnoteMarginRight, _) = PageLayout.MarginsDip(endnoteBox.PageGeometry);
        int endnotePixW = (int)Math.Max(1, Math.Round(endnotePageWDip));
        int endnotePixH = (int)Math.Max(1, Math.Round(endnotePageHDip));
        if (true)
        {
            var pageColor = Colors.White;
            var bmp = new RenderTargetBitmap(endnotePixW, endnotePixH, 96, 96, PixelFormats.Pbgra32);

            // White background page.
            var bgVis = new DrawingVisual();
            using (var dc = bgVis.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, endnotePixW, endnotePixH));
            bmp.Render(bgVis);

            // Endnote region starting at top-margin.
            var endnoteBmp = RenderNoteRegion(doc, Array.Empty<int>(), endnoteBox.EndnoteIds,
                endnotePageWDip, endnoteMarginLeft, endnoteMarginRight, isEndnotePage: true);
            if (endnoteBmp is not null)
            {
                var enVis = new DrawingVisual();
                using (var dc = enVis.RenderOpen())
                    dc.DrawImage(endnoteBmp, new Rect(0, endnoteMarginTop, endnotePixW, endnoteBmp.Height));
                bmp.Render(enVis);
            }

            string endnotePath = BuildVisualEvidenceOutputPath(outDir, name, pageCount + 1);
            var byteLength = SavePng(bmp, endnotePath);
            var stats = ComputeWpfPixelStats(bmp, "#FFFFFF");
            var sectionOrdinal = FreeWVisualEvidencePlanner.ResolveSectionOrdinal(doc, endnoteBox.PageGeometry);
            var sectionRelativePageNumber = NextSectionRelativePageNumber(sectionPageCounters, sectionOrdinal);
            var row = FreeWVisualEvidencePlanner.BuildEvidenceRow(
                scenarioId: name,
                hostId: "wpf-fidelity-render",
                outputPath: endnotePath,
                pixelWidth: endnotePixW,
                pixelHeight: endnotePixH,
                byteLength: byteLength,
                pixelStats: stats,
                page: endnoteBox.PageGeometry,
                pageNumber: pageCount + 1,
                pageCount: evidencePageCount,
                layoutKind: DocumentViewLayoutKind.PrintLayout,
                availableWidthDip: endnotePageWDip,
                hasEndnotes: true,
                isSyntheticPage: true,
                sectionOrdinal: sectionOrdinal,
                sectionRelativePageNumber: sectionRelativePageNumber,
                hostMetadata: new Dictionary<string, string>
                {
                    ["renderer"] = "FreeW.FidelityRender",
                    ["renderPath"] = "composite",
                    ["documentName"] = name,
                    ["pageIndex"] = pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["syntheticPage"] = "endnotes"
                },
                document: doc);
            FreeWVisualEvidencePlanner.EnsureTrusted(row);
            evidence.Add(row);
            Console.WriteLine($"ok    {Path.GetFileName(endnotePath)} (endnotes page, composite)");
        }
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
    double sepWidth   = isEndnotePage ? pageWDip - marginLeft - marginRight : 60.0;

    // Build a StackPanel mirroring PageBox.BuildNoteRegion and measure it.
    var panel = new System.Windows.Controls.StackPanel
    {
        Orientation = System.Windows.Controls.Orientation.Vertical,
        Background  = System.Windows.Media.Brushes.White
    };

    bool hasContent = false;

    if (footnoteIds.Count > 0)
    {
        // Separator line
        panel.Children.Add(new System.Windows.Controls.Border
        {
            Height                  = 1,
            Width                   = sepWidth,
            HorizontalAlignment     = System.Windows.HorizontalAlignment.Left,
            Margin                  = new System.Windows.Thickness(marginLeft, 4, 0, 2),
            Background              = System.Windows.Media.Brushes.Black
        });

        foreach (var id in footnoteIds)
        {
            if (!doc.Footnotes.TryGetValue(id, out var footnote)) continue;
            var text = footnote.PlainText;
            if (string.IsNullOrEmpty(text)) continue;

            var tb = new System.Windows.Controls.TextBlock
            {
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin       = new System.Windows.Thickness(marginLeft, 1, marginRight, 1),
                FontSize     = textSizePx
            };
            tb.Inlines.Add(new System.Windows.Documents.Run(id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                BaselineAlignment = System.Windows.BaselineAlignment.Superscript,
                FontSize          = textSizePx * 0.75
            });
            tb.Inlines.Add(new System.Windows.Documents.Run(" " + text));
            panel.Children.Add(tb);
            hasContent = true;
        }
    }

    if (endnoteIds.Count > 0)
    {
        if (isEndnotePage)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text    = "Endnotes",
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

        foreach (var id in endnoteIds)
        {
            if (!doc.Endnotes.TryGetValue(id, out var endnote)) continue;
            var text = endnote.PlainText;
            if (string.IsNullOrEmpty(text)) continue;

            var tb = new System.Windows.Controls.TextBlock
            {
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin       = new System.Windows.Thickness(marginLeft, 1, marginRight, 1),
                FontSize     = textSizePx
            };
            tb.Inlines.Add(new System.Windows.Documents.Run(id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                BaselineAlignment = System.Windows.BaselineAlignment.Superscript,
                FontSize          = textSizePx * 0.75
            });
            tb.Inlines.Add(new System.Windows.Documents.Run(" " + text));
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

    // ─── 4. Footnotes ────────────────────────────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Footnotes Test", "Heading1"));
        doc.Blocks.Add(MP("This tests whether footnote content appears at the foot of each page."));

        var p1 = new FreeW.Core.Model.Paragraph();
        p1.Runs.Add(new FreeW.Core.Model.Run("This sentence has a footnote reference"));
        p1.Runs.Add(new FreeW.Core.Model.Run(string.Empty) { FootnoteId = 1 });
        p1.Runs.Add(new FreeW.Core.Model.Run(". The footnote content should appear at the bottom of this page."));
        doc.Blocks.Add(p1);
        doc.Footnotes[1] = new Footnote(1, "Footnote 1: This is first footnote content. Should appear at bottom of page 1 with a separator rule.");

        for (int i = 1; i <= 22; i++)
            doc.Blocks.Add(MP($"Filler paragraph {i}: Lorem ipsum dolor sit amet consectetur adipiscing."));

        var p2 = new FreeW.Core.Model.Paragraph();
        p2.Runs.Add(new FreeW.Core.Model.Run("This sentence on page 2 has a second footnote reference"));
        p2.Runs.Add(new FreeW.Core.Model.Run(string.Empty) { FootnoteId = 2 });
        p2.Runs.Add(new FreeW.Core.Model.Run(". The second footnote should be at the bottom of page 2."));
        doc.Blocks.Add(p2);
        doc.Footnotes[2] = new Footnote(2, "Footnote 2: Second footnote content. Should appear at the bottom of page 2.");

        for (int i = 1; i <= 20; i++)
            doc.Blocks.Add(MP($"More filler {i}: Additional content to ensure footnote reference is on page 2."));

        DocxWriter.Write(doc, Path.Combine(outDir, "f2-footnotes.docx"));
        Console.WriteLine("  wrote f2-footnotes.docx");
    }

    // ─── 5. Endnotes ─────────────────────────────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Endnotes Test", "Heading1"));
        doc.Blocks.Add(MP("This tests whether endnote content appears at the end of the document."));

        var p1 = new FreeW.Core.Model.Paragraph();
        p1.Runs.Add(new FreeW.Core.Model.Run("First sentence with an endnote reference"));
        p1.Runs.Add(new FreeW.Core.Model.Run(string.Empty) { EndnoteId = 1 });
        p1.Runs.Add(new FreeW.Core.Model.Run(". Endnotes should collect at the document end."));
        doc.Blocks.Add(p1);
        doc.Endnotes[1] = new Endnote(1, "Endnote 1: This content should appear at the very end of the document, after all body text.");

        for (int i = 1; i <= 20; i++)
            doc.Blocks.Add(MP($"Body paragraph {i}: Endnote references collect at document end."));

        var p2 = new FreeW.Core.Model.Paragraph();
        p2.Runs.Add(new FreeW.Core.Model.Run("Second sentence with another endnote reference"));
        p2.Runs.Add(new FreeW.Core.Model.Run(string.Empty) { EndnoteId = 2 });
        p2.Runs.Add(new FreeW.Core.Model.Run(". Both endnotes should appear together at the end."));
        doc.Blocks.Add(p2);
        doc.Endnotes[2] = new Endnote(2, "Endnote 2: This is the second endnote. Both endnotes should be listed together at the document end.");

        for (int i = 1; i <= 20; i++)
            doc.Blocks.Add(MP($"More body content {i}: Additional text before the endnotes section."));

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

    // ─── 6. Section break with page-size change (portrait → landscape) ───────────────────────────
    // SG: FreeW/OOXML section-break semantics: a SectionBreak on paragraph P describes the section
    // that ENDS at P (the "preceding" section). The FINAL section is described by doc.Page.
    // So for portrait(section1) → landscape(section2):
    //   • sectionMarker.SectionBreak.Page = portrait settings  (section 1, which ends at the marker)
    //   • doc.Page = landscape settings                        (section 2 = final section)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Section 1: Portrait (8.5 x 11 in)", "Heading1"));
        doc.Blocks.Add(MP("This section is portrait. The page is taller than wide. A next-page section break below this paragraph should switch to landscape."));
        for (int i = 1; i <= 4; i++)
            doc.Blocks.Add(MP($"Portrait section paragraph {i}: Standard letter-size portrait page."));

        // Marker paragraph ends section 1.  Its SectionBreak.Page carries section 1's (portrait) geometry.
        var sectionMarker = MP("[ End of Portrait Section ]");
        var portraitPage = new PageSettings
        {
            WidthPt        = 612,  // 8.5in portrait
            HeightPt       = 792,  // 11in portrait
            Landscape      = false,
            MarginLeftPt   = 72,
            MarginRightPt  = 72,
            MarginTopPt    = 72,
            MarginBottomPt = 72,
        };
        sectionMarker.SectionBreak = new FreeW.Core.Model.Section(portraitPage, SectionBreakKind.NextPage);
        doc.Blocks.Add(sectionMarker);

        // Section 2 (final section): landscape — described by doc.Page.
        doc.Page.WidthPt        = 792;  // 11in landscape (wider)
        doc.Page.HeightPt       = 612;  // 8.5in landscape (shorter — swapped)
        doc.Page.Landscape      = true;
        doc.Page.MarginLeftPt   = 72;
        doc.Page.MarginRightPt  = 72;
        doc.Page.MarginTopPt    = 72;
        doc.Page.MarginBottomPt = 72;

        doc.Blocks.Add(MP("Section 2: Landscape (11 x 8.5 in)", "Heading1"));
        doc.Blocks.Add(MP("This section should be landscape. If the section break rendered correctly the page is now wider than tall, and this text spans a wider line length. The page geometry changed from portrait (8.5x11) to landscape (11x8.5)."));
        for (int i = 1; i <= 4; i++)
            doc.Blocks.Add(MP($"Landscape section paragraph {i}: Page is now wider than tall."));

        DocxWriter.Write(doc, Path.Combine(outDir, "f2-section-landscape.docx"));
        Console.WriteLine("  wrote f2-section-landscape.docx");
    }

    // ─── 7. Tracked insertions and deletions ─────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Tracked Changes Test", "Heading1"));
        doc.Blocks.Add(MP("Insertions should be underlined; deletions should be struck-through."));

        var p1 = new FreeW.Core.Model.Paragraph();
        p1.Runs.Add(new FreeW.Core.Model.Run("Normal text before. "));
        p1.Runs.Add(new FreeW.Core.Model.Run("INSERTED text by Alice.")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice",
            RevisionDateXml = "2026-06-26T09:00:00Z"
        });
        p1.Runs.Add(new FreeW.Core.Model.Run(" Normal text between. "));
        p1.Runs.Add(new FreeW.Core.Model.Run("DELETED text by Bob.")
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Bob",
            RevisionDateXml = "2026-06-26T09:30:00Z"
        });
        p1.Runs.Add(new FreeW.Core.Model.Run(" Normal text after."));
        doc.Blocks.Add(p1);

        var p2 = new FreeW.Core.Model.Paragraph();
        p2.Runs.Add(new FreeW.Core.Model.Run("This entire paragraph is a tracked insertion by Carol.")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Carol",
            RevisionDateXml = "2026-06-26T10:00:00Z"
        });
        doc.Blocks.Add(p2);

        var p3 = new FreeW.Core.Model.Paragraph();
        p3.Runs.Add(new FreeW.Core.Model.Run("Alice: "));
        p3.Runs.Add(new FreeW.Core.Model.Run("inserted-by-alice ")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice",
            RevisionDateXml = "2026-06-26T09:00:00Z"
        });
        p3.Runs.Add(new FreeW.Core.Model.Run("Bob: "));
        p3.Runs.Add(new FreeW.Core.Model.Run("deleted-by-bob ")
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Bob",
            RevisionDateXml = "2026-06-26T09:30:00Z"
        });
        p3.Runs.Add(new FreeW.Core.Model.Run("Carol: "));
        p3.Runs.Add(new FreeW.Core.Model.Run("inserted-by-carol")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Carol",
            RevisionDateXml = "2026-06-26T10:00:00Z"
        });
        doc.Blocks.Add(p3);

        for (int i = 1; i <= 40; i++)
            doc.Blocks.Add(MP($"Normal paragraph {i}: No tracked changes here."));

        DocxWriter.Write(doc, Path.Combine(outDir, "f2-tracked-changes.docx"));
        Console.WriteLine("  wrote f2-tracked-changes.docx");
    }

    // ─── 8. Anchored comments ─────────────────────────────────────────────────────────────────────
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(MP("Comments Test", "Heading1"));
        doc.Blocks.Add(MP("Comment anchors should be highlighted. Comment content should appear as balloons or in a reviewing pane."));

        var p1 = new FreeW.Core.Model.Paragraph();
        p1.Runs.Add(new FreeW.Core.Model.Run("Text before the first comment anchor. "));
        p1.Runs.Add(FreeW.Core.Model.Run.CommentReference(1));
        p1.Runs.Add(new FreeW.Core.Model.Run("The first commented span.") { CommentId = 1 });
        p1.Runs.Add(new FreeW.Core.Model.Run(" Text after the first comment anchor."));
        doc.Blocks.Add(p1);
        doc.Comments[1] = new Comment(1, "Comment 1 by Alice: This is the comment text. Should appear as a balloon in the right margin.")
        {
            Author   = "Alice",
            Initials = "A",
            DateXml  = "2026-06-26T09:00:00Z"
        };

        var p2 = new FreeW.Core.Model.Paragraph();
        p2.Runs.Add(new FreeW.Core.Model.Run("Second paragraph before comment. "));
        p2.Runs.Add(FreeW.Core.Model.Run.CommentReference(2));
        p2.Runs.Add(new FreeW.Core.Model.Run("Second commented phrase.") { CommentId = 2 });
        p2.Runs.Add(new FreeW.Core.Model.Run(" End of second paragraph."));
        doc.Blocks.Add(p2);
        doc.Comments[2] = new Comment(2, "Comment 2 by Bob: Different author, distinct comment. Both should be visible.")
        {
            Author   = "Bob",
            Initials = "B",
            DateXml  = "2026-06-26T09:30:00Z"
        };

        for (int i = 1; i <= 35; i++)
            doc.Blocks.Add(MP($"Normal paragraph {i}: No comments here."));

        DocxWriter.Write(doc, Path.Combine(outDir, "f2-comments.docx"));
        Console.WriteLine("  wrote f2-comments.docx");
    }

    {
        var doc = BuildBackstageFixtureDocument(
            "Backstage Print Preview Fidelity",
            "This generated document is rendered through FreeW.FidelityRender for the backstage print preview evidence contract.",
            "Print preview fixed-layout page");
        DocxWriter.Write(doc, Path.Combine(outDir, "backstage-print-preview-fidelity.docx"));
        Console.WriteLine("  wrote backstage-print-preview-fidelity.docx");
    }

    {
        var doc = BuildBackstageFixtureDocument(
            "Backstage PDF Export Fidelity",
            "This generated document is rendered through FreeW.FidelityRender for the backstage PDF export raster evidence contract.",
            "PDF export fixed-layout page");
        DocxWriter.Write(doc, Path.Combine(outDir, "backstage-pdf-export-fidelity.docx"));
        Console.WriteLine("  wrote backstage-pdf-export-fidelity.docx");
    }

    Console.WriteLine($"\nDone - 13 corpus files written to {outDir}");
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

static TextDocument BuildBackstageFixtureDocument(string title, string description, string pageLabel)
{
    var doc = TextDocument.CreateEmpty();
    doc.FinalSectionHeadersFooters.Header = new HeaderFooter(title);
    doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("FreeW visual evidence");
    doc.Blocks.Clear();

    doc.Blocks.Add(new FreeW.Core.Model.Paragraph(title) { StyleId = "Heading1" });
    doc.Blocks.Add(new FreeW.Core.Model.Paragraph(description));
    doc.Blocks.Add(new FreeW.Core.Model.Paragraph("The first two rendered pages are retained as real PNG evidence and normalized through the shared visual evidence manifest."));
    for (var i = 1; i <= 56; i++)
    {
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph(
            $"{pageLabel} paragraph {i}: body text, pagination, page chrome, and header/footer composition must survive the renderer capture path."));
    }

    return doc;
}

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
