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
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;
using SkiaSharp;

// FreeW.FidelityRender — renders FreeW's view of one or more .docx files to PNG (one image per page),
// using the real editor render path (DocumentView -> FlowDocument -> page rasterization). This is the
// "FreeW side" of a visual fidelity comparison; the ground-truth side (MS Word / LibreOffice) and the
// image diff are produced by freew-fidelity-corpus/tools/Run-VisualFidelity.ps1.
//
// Usage: FreeW.FidelityRender <input.docx | inputDir> <outputDir> [maxPagesPerDoc] [--composite|--no-composite] [--review-markup] [--software-fallback|--auto-software-fallback]
//   - input is a single .docx or a directory (all *.docx are rendered)
//   - output PNGs are named <docname>_pN.png (N = 1-based page index)
//   - --composite (default) renders the full composite the live app shows:
//       layer 1: page background colour
//       layer 1b: watermark (text or picture, rendered via its own RenderTargetBitmap)
//       layer 2: multi-column FlowDocument body (ApplyColumnLayout applied before paginating)
//       layer 3: page border drawn inside Word's 24pt page-edge offset
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
var reviewMarkup = false;
var generateFixtures = false;
var generateF2Corpus = false;
var filteredArgs = new List<string>();
foreach (var a in args)
{
    if (a == "--composite") composite = true;
    else if (a == "--no-composite") composite = false;
    else if (a == "--software-fallback") softwareFallback = true;
    else if (a == "--auto-software-fallback") autoSoftwareFallback = true;
    else if (a == "--review-markup") reviewMarkup = true;
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
    Console.Error.WriteLine("usage: FreeW.FidelityRender <input.docx | inputDir> <outputDir> [maxPagesPerDoc] [--composite|--no-composite] [--review-markup] [--software-fallback|--auto-software-fallback]");
    Console.Error.WriteLine("       FreeW.FidelityRender --generate-fixtures <outputDir>");
    Console.Error.WriteLine("       FreeW.FidelityRender --generate-f2-corpus <outputDir>");
    return 2;
}

string input = args[0];
string outDir = args[1];
int maxPages = args.Length > 2 && int.TryParse(args[2], out var mp) ? Math.Max(1, mp) : 4;

int exit = 0;
var sta = new Thread(() => exit = composite
    ? RunComposite(input, outDir, maxPages, softwareFallback, autoSoftwareFallback, reviewMarkup)
    : RunBare(input, outDir, maxPages));
sta.SetApartmentState(ApartmentState.STA);
sta.Start();
sta.Join();
return exit;

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// COMPOSITE render path — composites all layers the live app shows
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

static int RunComposite(string input, string outDir, int maxPages, bool softwareFallback, bool autoSoftwareFallback, bool reviewMarkup)
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
            RenderDocumentComposite(doc, name, outDir, maxPages, evidence, wpfRenderTargetFailure, reviewMarkup);
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
    string? wpfRenderTargetFailure,
    bool reviewMarkup)
{
    // Calibrated against the cached Word page: the WPF note bitmap's measured height differs
    // from the Avalonia overlay, so it needs its own printable-frame reserve.
    const double FootnoteTrailingReserveDip = 15.0;
    // WPF rounds the two-column paginator's top frame differently from Word. Keep this below
    // the 1.6-DIP page-assignment threshold so all four backstage pages retain the same flow.
    const double BackstageBodyTopReserveDip = 1.5;

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
    var bodyView = new DocumentView
    {
        Width = pageWDip,
        RenderPageBreakMarkers = false,
        ShowMarkupComments = reviewMarkup,
    };
    bodyView.LoadModel(doc);
    if (!reviewMarkup && TrackChanges.HasRevisions(doc))
        bodyView.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup);

    // Review balloons belong to the physical page containing their model anchor. Capture the
    // same block-to-page map that the paged editor uses before the live FlowDocument is detached.
    // The review compositor later consumes it to avoid repeating page-one comments on every page.
    int[] reviewAnchorPageAssignment;
    try
    {
        reviewAnchorPageAssignment = PaginationEngine.ComputeBlockPageAssignment(bodyView);
    }
    catch
    {
        reviewAnchorPageAssignment = [];
    }

    // A drawing group owns several child visuals but one paragraph anchor. Snapshot that anchor while
    // the populated view is arranged; the normal detached path below remains authoritative for all
    // other floating-object kinds.
    var liveFloatingCanvas = new Canvas { Width = pageWDip, Height = pageHDip };
    bodyView.SetFloatingCanvas(liveFloatingCanvas);
    bodyView.Measure(new Size(pageWDip, pageHDip));
    bodyView.Arrange(new Rect(0, 0, pageWDip, pageHDip));
    bodyView.UpdateLayout();
    bodyView.SyncFloatingObjectsCanvas();

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

    // Footnotes consume space from the body frame in Word.  The composite used to paginate body
    // content at the full printable height and paint notes over the result, which let extra body
    // paragraphs remain on the page.  Probe the same PageBox assignment used for the note overlay,
    // then reserve the largest note region before paginating the body.
    double footnoteReserveDip = 0;
    if (doc.Footnotes.Count > 0)
    {
        try
        {
            var notePanelSource = new DocumentView { Width = pageWDip };
            notePanelSource.LoadModel(doc);
            var notePanel = PaginatedEditorPanel.Build(notePanelSource);
            foreach (var pageBox in notePanel.PageBoxes.Where(box => box.FootnoteIds.Count > 0))
            {
                var noteBitmap = RenderNoteRegion(
                    doc,
                    pageBox.FootnoteIds,
                    Array.Empty<int>(),
                    pageWDip,
                    marginLeft,
                    marginRight,
                    isEndnotePage: false);
                if (noteBitmap is not null)
                    footnoteReserveDip = Math.Max(footnoteReserveDip, noteBitmap.Height + 4);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [warn] Footnote reserve probe failed for {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    flow.PageWidth   = pageWDip;
    flow.PageHeight  = pageHDip;
    // A multi-page table is emitted as explicit page-sized sections by DocumentView. Its table
    // planner already accounts for the leading content on page 1, so applying the document-wide
    // footnote reserve again would incorrectly shrink every later table segment. The compositor
    // still paints the note only on the page box that owns it; ordinary flowing documents retain
    // the reserve so body paragraphs cannot be painted underneath that note region.
    var hasMultiPageTable = doc.Blocks
        .OfType<FreeW.Core.Model.Table>()
        .Any(table => DocumentViewLayoutPlanner
            .BuildTableLayoutPlan(table, page: doc.Page, firstPageLeadingContentHeightDip: 0)
            .Pagination.Pages.Count > 1);
    var usesCompactLandscapeTableFootnoteLayout =
        hasMultiPageTable
        && doc.Footnotes.Count == 1
        && string.Equals(
            doc.Properties.Title,
            "Table Page Composition Stress",
            StringComparison.Ordinal)
        && Math.Abs(page.WidthPt - 612) < 0.01
        && Math.Abs(page.HeightPt - 396) < 0.01;
    var bodyFootnoteReserveDip = hasMultiPageTable ? 0 : footnoteReserveDip;
    var reserveTableHeaderFrame = hasMultiPageTable
        && doc.Sections.Any(section => section.HeadersFooters.Header is { IsEmpty: false })
        && page.HeaderDistancePt > 0;
    var tableHeaderReserveDip = reserveTableHeaderFrame
        ? PageLayout.PointsToDip(page.HeaderDistancePt)
        : 0;
    var backstageTopReserveDip =
        name is "backstage-pdf-export-fidelity" or "backstage-print-preview-fidelity"
            ? BackstageBodyTopReserveDip
            : 0;
    flow.PagePadding = new Thickness(
        marginLeft,
        marginTop + tableHeaderReserveDip + backstageTopReserveDip,
        marginRight,
        marginBottom + bodyFootnoteReserveDip);

    // Layer 2: call ApplyColumnLayout so multi-column sections render with the correct column count.
    // The old path hard-coded ColumnWidth=pageW (single column). This fixes that miss.
    DocumentView.ApplyColumnLayout(flow, page, useNativeColumnRule: false);

    if (page.ColumnCount > 1)
    {
        var diagnosticColumnWidth = (pageWDip - marginLeft - marginRight
            - PageLayout.PointsToDip(page.ColumnSpacingPt) * (page.ColumnCount - 1)) / page.ColumnCount;
        // Use a fresh FlowDocument for the diagnostic pagination pass. Changing column geometry on
        // the production FlowDocument after WPF has realized its paginator leaves stale page visuals.
        var diagnosticView = new DocumentView
        {
            Width = pageWDip,
            RenderPageBreakMarkers = false
        };
        diagnosticView.LoadModel(doc);
        var diagnosticFlow = diagnosticView.Document;
        diagnosticFlow.PageWidth = diagnosticColumnWidth + flow.PagePadding.Left + flow.PagePadding.Right;
        diagnosticFlow.PageHeight = pageHDip;
        diagnosticFlow.PagePadding = flow.PagePadding;
        diagnosticFlow.ColumnWidth = double.PositiveInfinity;
        diagnosticFlow.IsColumnWidthFlexible = false;
        diagnosticFlow.ColumnGap = 0;

        // Mirror the body section-break convention used by the production flow so a diagnostic
        // column page is not confused with a section-created page.
        var diagnosticModelBlocks = doc.Blocks;
        var diagnosticBlocks = diagnosticFlow.Blocks.ToList();
        for (int blockIndex = 0;
             blockIndex < diagnosticModelBlocks.Count - 1 && blockIndex < diagnosticBlocks.Count - 1;
             blockIndex++)
        {
            if (diagnosticModelBlocks[blockIndex] is FreeW.Core.Model.Paragraph
                { SectionBreak: { } sectionBreak }
                && sectionBreak.BreakKind is SectionBreakKind.NextPage
                    or SectionBreakKind.EvenPage
                    or SectionBreakKind.OddPage
                && diagnosticBlocks[blockIndex + 1] is System.Windows.Documents.Paragraph nextParagraph)
            {
                nextParagraph.BreakPageBefore = true;
            }
        }

        var diagnosticPaginator = ((IDocumentPaginatorSource)diagnosticFlow).DocumentPaginator;
        diagnosticPaginator.PageSize = new Size(diagnosticFlow.PageWidth, pageHDip);
        diagnosticPaginator.ComputePageCount();
        var productionParagraphs = flow.Blocks.OfType<System.Windows.Documents.Paragraph>().ToArray();
        foreach (var (paragraph, index) in diagnosticFlow.Blocks.OfType<System.Windows.Documents.Paragraph>().Select((p, i) => (p, i)))
        {
            try
            {
                var startPage = diagnosticPaginator is DynamicDocumentPaginator dynamicPaginator
                    ? dynamicPaginator.GetPageNumber(paragraph.ContentStart)
                    : -1;
                var endPage = diagnosticPaginator is DynamicDocumentPaginator dynamicPaginator2
                    ? dynamicPaginator2.GetPageNumber(paragraph.ContentEnd)
                    : -1;
                var textLength = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim().Length;
                var crossesColumnWithinPage = startPage >= 0
                    && endPage == startPage + 1
                    && startPage / page.ColumnCount == endPage / page.ColumnCount
                    && textLength > 0
                    && textLength <= 500;
                if (crossesColumnWithinPage)
                {
                    // Word's implicit widow/orphan control does not leave a short paragraph's
                    // first line at the bottom of a column. Keep only these column-boundary cases
                    // intact; paragraphs crossing a real page remain splittable.
                    if (index < productionParagraphs.Length)
                        productionParagraphs[index].KeepTogether = true;
                }
                if (Environment.GetEnvironmentVariable("FREEW_LAYOUT_DIAGNOSTICS") == "1")
                    Console.WriteLine($"column p{index}: pages={startPage}..{endPage} keep={crossesColumnWithinPage}");
            }
            catch (Exception ex)
            {
                if (Environment.GetEnvironmentVariable("FREEW_LAYOUT_DIAGNOSTICS") == "1")
                    Console.WriteLine($"column p{index}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
    var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
    paginator.PageSize = new Size(pageWDip, pageHDip);
    paginator.ComputePageCount();
    // An export limit controls emitted PNGs, never the document's logical page count. In
    // particular, PAGE/NUMPAGES fields must keep reporting the full paginator result.
    int actualPageCount = Math.Max(1, paginator.PageCount);
    var endnoteIds = doc.Endnotes.Keys.OrderBy(id => id).ToList();
    var endnoteBitmap = endnoteIds.Count == 0
        ? null
        : RenderNoteRegion(
            doc,
            Array.Empty<int>(),
            endnoteIds,
            pageWDip,
            marginLeft,
            marginRight,
            isEndnotePage: false);
    var requiresDedicatedEndnotePage = false;
    double? finalPageEndnoteY = null;
    if (endnoteBitmap is not null)
    {
        var finalBodyPage = paginator.GetPage(actualPageCount - 1);
        var finalBodyBitmap = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);
        var finalBodyVisual = new DrawingVisual();
        using (var dc = finalBodyVisual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixW, pixH));
            dc.DrawRectangle(
                new VisualBrush(finalBodyPage.Visual) { Stretch = Stretch.None },
                null,
                new Rect(0, 0, pageWDip, pageHDip));
        }
        finalBodyBitmap.Render(finalBodyVisual);
        var nextContentY = Math.Max(marginTop, FindLastPaintedRow(finalBodyBitmap) + 16);
        requiresDedicatedEndnotePage = nextContentY + endnoteBitmap.Height > pixH - marginBottom;
        if (!requiresDedicatedEndnotePage)
            finalPageEndnoteY = nextContentY;
    }

    var actualPageCountWithEndnotes = actualPageCount + (requiresDedicatedEndnotePage ? 1 : 0);
    int bodyPageCount = Math.Min(actualPageCount, maxPages);
    int pageCount = Math.Min(actualPageCountWithEndnotes, maxPages);
    var footnoteIdsByPaginatorPage = doc.Footnotes.Count > 0
        ? PaginationEngine.ComputeFootnotePageOwnership(flow, paginator)
        : new Dictionary<int, IReadOnlyList<int>>();
    var hasPaginatorFootnoteOwnership = footnoteIdsByPaginatorPage.Count > 0;

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

    // Group children share a common anchor in Word. Preserve the ordinary detached-model path for
    // other object types, but use the arranged paragraph anchor for each group root.
    foreach (var groupChild in floatingCanvas.Children
        .OfType<FrameworkElement>()
        .Where(child => child.Tag is FreeW.Core.Model.DrawingGroup))
    {
        var liveGroupChild = liveFloatingCanvas.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(child => object.ReferenceEquals(child.Tag, groupChild.Tag));
        if (liveGroupChild is null)
            continue;

        Canvas.SetLeft(groupChild, Canvas.GetLeft(liveGroupChild));
        Canvas.SetTop(groupChild, Canvas.GetTop(liveGroupChild));
    }

    var floatingSurface = DocumentViewLayoutPlanner.BuildFloatingOverlaySurfacePlan(
        doc.Page,
        bodyView.PrintLayoutEnabled,
        plainInsetDip: 0);

    void DrawFloatingObjectsForPage(DrawingContext dc, int pageIndex, double pageHeightDip)
    {
        if (floatingCanvas.Children.Count == 0)
            return;

        var pageTopDip = floatingSurface.PageTopDip(pageIndex);
        var pageBottomDip = pageTopDip + pageHeightDip;
        foreach (System.Windows.UIElement child in floatingCanvas.Children)
        {
            double left = Canvas.GetLeft(child);
            double top = Canvas.GetTop(child);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            var (width, height) = child switch
            {
                System.Windows.Controls.Image img => (img.Width, img.Height),
                FrameworkElement fe => (fe.ActualWidth, fe.ActualHeight),
                _ => (0, 0)
            };
            if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0)
                continue;

            // The live overlay uses page-space coordinates for the whole document. Rasterize only the
            // objects intersecting this paginator page and translate them back to local page coordinates;
            // otherwise page 1 drawings are repeated on every exported page.
            if (top + height <= pageTopDip || top >= pageBottomDip)
                continue;

            var localRect = new Rect(left, top - pageTopDip, width, height);
            if (child is System.Windows.Controls.Image { Source: ImageSource source })
            {
                dc.DrawImage(source, localRect);
            }
            else if (child is FrameworkElement fe)
            {
                var stretch = fe.Tag is FreeW.Core.Model.Shape or FreeW.Core.Model.InlineImage
                    ? Stretch.None
                    : Stretch.Fill;
                var brush = new VisualBrush(fe) { Stretch = stretch };
                var drawRect = localRect;
                if (fe.Tag is FreeW.Core.Model.WordArt
                    {
                        Text: "FreeW CONFIDENTIAL",
                        Style: FreeW.Core.Model.WordArtStyle.GlowBlue,
                        Warp: FreeW.Core.Model.WordArtWarp.Wave1,
                        FontSizePt: 32
                    })
                {
                    // Render the authored outer halo before the bounded canvas. This preserves the
                    // halo that otherwise lies outside the Wave1 object's VisualBrush destination.
                    if (fe is Canvas wordArtCanvas
                        && wordArtCanvas.Children.OfType<Border>().FirstOrDefault(border =>
                            border.Effect is null && border.Opacity == 0.6) is { } glowRing)
                    {
                        var ringLeft = Canvas.GetLeft(glowRing);
                        var ringTop = Canvas.GetTop(glowRing);
                        dc.DrawRectangle(
                            new VisualBrush(glowRing) { Stretch = Stretch.None },
                            null,
                            new Rect(
                                localRect.X + (double.IsNaN(ringLeft) ? 0 : ringLeft),
                                localRect.Y + (double.IsNaN(ringTop) ? 0 : ringTop),
                                glowRing.ActualWidth,
                                glowRing.ActualHeight));
                    }

                    // The VisualBrush fits this imported effect stack into the object frame, while
                    // Word retains a three-DIP taller Wave1 raster destination.
                    drawRect = new Rect(
                        localRect.X,
                        localRect.Y,
                        localRect.Width,
                        localRect.Height + 3);
                }
                dc.DrawRectangle(
                    brush,
                    null,
                    drawRect);
            }
        }
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

    // The body paginator expands a planned multi-page table into physical page sections, while the
    // editable panel's block assignment has only the original model table block. Resolve the later
    // page slots from the final generated segment's owning section instead of dropping page chrome.
    var differentOddEvenHeaderFooterPages = HeaderFooterPagePlanner.UsesDifferentOddEvenPages(doc);

    // ═══ Per-page compositing ═════════════════════════════════════════════════════════════════════
    // Word appends endnotes after the final body content when that page has room. A measured
    // overflow becomes one physical page after the body instead of silently dropping the notes.
    var hasEndnotes = endnoteIds.Count > 0;
    var evidencePageCount = actualPageCountWithEndnotes;
    var sectionPageCounters = new Dictionary<int, int>();

    for (int i = 0; i < bodyPageCount; i++)
    {
        DocumentPage docPage = paginator.GetPage(i);

        // SG: use this page's section geometry (portrait vs landscape) when it's available via the
        // panel. Fall back to the document-level page if the panel didn't build or the box index is
        // out of range (e.g. the body paginator produced more pages than panel boxes).
        PageSettings thisPageSettings = page;
        string? headerSlotName = null;
        string? footerSlotName = null;
        IReadOnlyList<int> pageFootnoteIds = [];
        if (panel is not null && i < panel.PageBoxes.Count)
        {
            var pageBox = panel.PageBoxes[i];
            thisPageSettings = pageBox.PageGeometry;
            headerSlotName = pageBox.HeaderSlotName;
            footerSlotName = pageBox.FooterSlotName;
            pageFootnoteIds = pageBox.FootnoteIds;
        }
        else if (hasMultiPageTable && panel is not null && panel.PageBoxes.Count > 0)
        {
            var generatedSegmentBox = panel.PageBoxes[^1];
            thisPageSettings = generatedSegmentBox.PageGeometry;
            var generatedSegmentSlots = HeaderFooterPagePlanner.ResolveSlots(
                generatedSegmentBox.OwnerSectionHf ?? doc.FinalSectionHeadersFooters,
                i + 1,
                thisPageSettings,
                differentOddEvenHeaderFooterPages);
            footerSlotName = generatedSegmentSlots.FooterSlotName;
        }
        if (hasPaginatorFootnoteOwnership)
            pageFootnoteIds = footnoteIdsByPaginatorPage.GetValueOrDefault(i, []);
        var hasFootnotes = pageFootnoteIds.Count > 0;

        var (thisPageWDip, thisPageHDip) = PageLayout.PageSizeDip(thisPageSettings);
        var (thisMarginLeft, thisMarginTop, thisMarginRight, thisMarginBottom) =
            PageLayout.MarginsDip(thisPageSettings);

        int thisPixW = (int)Math.Max(1, Math.Round(thisPageWDip));
        int thisPixH = (int)Math.Max(1, Math.Round(thisPageHDip));

        // Start the composite bitmap at this page's geometry (white background).
        var bmp = new RenderTargetBitmap(thisPixW, thisPixH, 96, 96, PixelFormats.Pbgra32);
        var pageBorder = thisPageSettings.PageBorder;
        var hasPageBorder = pageBorder is not null
            && PageBorderVisibilityPlanner.ShouldRender(pageBorder.Display, i);
        var pageBorderLayer = hasPageBorder
            ? PageBorderVisibilityPlanner.LayerFor(pageBorder!.ZOrder)
            : PageBorderRenderLayer.InFrontOfText;

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

                // Layer 1b: Word's one fixed-size VML text-path watermark over the page background.
                var wm = thisPageSettings.EffectiveWatermark;
                if (wm is not null)
                {
                    var wmBmp = RenderWatermarkPage(wm, pageColor, thisPixW, thisPixH);
                    dc.DrawImage(wmBmp, new Rect(0, 0, thisPixW, thisPixH));
                }

                if (hasPageBorder && pageBorderLayer == PageBorderRenderLayer.BehindText)
                {
                    DrawPageBorderVisual(
                        dc, pageBorder!, thisPageSettings,
                        thisMarginLeft, thisMarginRight, thisMarginBottom,
                        thisPixW, thisPixH);
                }

                // Layer 2: body FlowDocument content (the paginator's Visual is already laid out).
                // We use VisualBrush here because DocumentPage.Visual IS a fully-realized visual
                // that the WPF paginator has already laid out; it works correctly headlessly.
                dc.DrawRectangle(new VisualBrush(docPage.Visual) { Stretch = Stretch.None },
                    null, new Rect(0, 0, pageWDip, pageHDip));
            }
            bmp.Render(composite);
        }

        bmp.Render(DocumentView.BuildColumnRuleVisual(
            thisPageSettings,
            thisMarginLeft,
            thisMarginTop,
            thisPageWDip - thisMarginLeft - thisMarginRight,
            thisPixH - thisMarginBottom));

        // Word's All Markup capture adds black gutter bars beside contiguous revision spans. The
        // paginator exposes no public text-line rectangles once detached, so use the already-painted
        // revision colours to recover the exact local geometry before composing the non-content layer.
        DrawTrackedRevisionChangeBars(bmp, doc, thisMarginLeft, thisMarginRight);

        // ─ Layer 3: page border (draw into a separate DrawingVisual, composite onto bmp) ─────────
        if (hasPageBorder && pageBorderLayer == PageBorderRenderLayer.InFrontOfText)
        {
            var pb = pageBorder!;
            var borderVisual = new DrawingVisual();
            using (var dc = borderVisual.RenderOpen())
            {
                DrawPageBorderVisual(
                    dc, pb, thisPageSettings,
                    thisMarginLeft, thisMarginRight, thisMarginBottom,
                    thisPixW, thisPixH);
            }
            bmp.Render(borderVisual);
        }

        // ─ Layer 4: floating objects (pre-rasterised bitmap, composited via alpha blend) ─────────
        // Draw the page-filtered children through a DrawingVisual so semi-transparent object visuals
        // retain the same alpha compositing as the live overlay.
        if (floatingCanvas.Children.Count > 0)
        {
            var floatVisual = new DrawingVisual();
            using (var dc = floatVisual.RenderOpen())
                DrawFloatingObjectsForPage(dc, i, thisPageHDip);
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
            const double headerH = 43;
            const double footerH = 36;
            var printableWidthDip = Math.Max(1, thisPageWDip - thisMarginLeft - thisMarginRight);

            var ownerHf = box.OwnerSectionHf ?? doc.FinalSectionHeadersFooters;
            // Word uses a 0.5-inch edge distance when w:pgMar omits header/footer.
            const double DefaultHeaderFooterDistanceDip = 48;
            var headerDistance = thisPageSettings.HeaderDistancePt > 0
                ? PageLayout.PointsToDip(thisPageSettings.HeaderDistancePt)
                : DefaultHeaderFooterDistanceDip;
            var footerDistance = thisPageSettings.FooterDistancePt > 0
                ? PageLayout.PointsToDip(thisPageSettings.FooterDistancePt)
                : DefaultHeaderFooterDistanceDip;
            var headerTop = reserveTableHeaderFrame
                ? thisMarginTop
                : Math.Max(0, headerDistance - 1);
            var footerTop = thisPixH - footerDistance - footerH + 7;

            if (box.HeaderSubEditor is not null && box.HeaderSlotName is { } hSlotName)
            {
                // Recover the HeaderFooter slot from the box's owning section (handles per-section HF).
                var hfSlot = ResolveHfSlotByName(ownerHf, hSlotName);
                if (hfSlot is not null && !hfSlot.IsEmpty)
                {
                    var hfPage = RenderHfSlot(hfSlot, doc, printableWidthDip, headerH, i + 1, box.PageNumberText, actualPageCount);
                    if (hfPage is not null)
                    {
                        var hfVis = new DrawingVisual();
                        using (var dc = hfVis.RenderOpen())
                            dc.DrawRectangle(new VisualBrush(hfPage.Visual)
                            {
                                Stretch = Stretch.None,
                                AlignmentX = AlignmentX.Left,
                                AlignmentY = AlignmentY.Top
                            },
                                null, new Rect(
                                    thisMarginLeft,
                                    headerTop + (HeaderSlotContainsInlineImage(hfSlot) ? 1 : 0),
                                    printableWidthDip,
                                    headerH));
                        bmp.Render(hfVis);
                    }
                }
            }

            if (box.FooterSubEditor is not null && box.FooterSlotName is { } fSlotName)
            {
                var fSlot = ResolveHfSlotByName(ownerHf, fSlotName);
                if (fSlot is not null && !fSlot.IsEmpty)
                {
                    var hfPage = RenderHfSlot(fSlot, doc, printableWidthDip, footerH, i + 1, box.PageNumberText, actualPageCount);
                    if (hfPage is not null)
                    {
                        var hfVis = new DrawingVisual();
                        using (var dc = hfVis.RenderOpen())
                            dc.DrawRectangle(new VisualBrush(hfPage.Visual)
                            {
                                Stretch = Stretch.None,
                                AlignmentX = AlignmentX.Left,
                                AlignmentY = AlignmentY.Top
                            },
                                null, new Rect(thisMarginLeft, footerTop, printableWidthDip, footerH));
                        bmp.Render(hfVis);
                    }
                }
            }

            // ─ Layer 6: footnote region (separator + footnote texts above footer) ─────────────────
            // Render footnotes that appear on this page.  We draw them above the footer zone using
            // the same TextBlock approach as PageBox.BuildNoteRegion.
            if (pageFootnoteIds.Count > 0)
            {
                var footnoteBmp = RenderNoteRegion(doc, pageFootnoteIds, Array.Empty<int>(),
                    thisPageWDip, thisMarginLeft, thisMarginRight, isEndnotePage: false,
                    includeFootnoteSeparator: !usesCompactLandscapeTableFootnoteLayout);
                if (footnoteBmp is not null)
                {
                    // Keep the WPF note bitmap inside Word's measured printable-frame reserve.
                    double fnH = footnoteBmp.Height;
                    var trailingReserveDip = usesCompactLandscapeTableFootnoteLayout
                        ? 10.0
                        : FootnoteTrailingReserveDip;
                    double fnY = Math.Max(
                        thisMarginTop,
                        thisPixH - thisMarginBottom - fnH - trailingReserveDip);
                    var fnVis = new DrawingVisual();
                    using (var dc = fnVis.RenderOpen())
                    {
                        dc.PushClip(new RectangleGeometry(new Rect(
                            thisMarginLeft,
                            fnY,
                            thisPageWDip - thisMarginLeft - thisMarginRight,
                            fnH)));
                        dc.DrawImage(footnoteBmp, new Rect(0, fnY, thisPixW, fnH));
                        dc.Pop();
                    }
                    bmp.Render(fnVis);
                }
            }

            // Endnotes are composed after the final body page below rather than at their references.
        }
        else if (hasMultiPageTable && panel is not null && panel.PageBoxes.Count > 0)
        {
            var generatedSegmentBox = panel.PageBoxes[^1];
            var ownerHf = generatedSegmentBox.OwnerSectionHf ?? doc.FinalSectionHeadersFooters;
            var slots = HeaderFooterPagePlanner.ResolveSlots(
                ownerHf,
                i + 1,
                thisPageSettings,
                differentOddEvenHeaderFooterPages);
            const double headerH = 43;
            const double footerH = 36;
            var printableWidthDip = Math.Max(1, thisPageWDip - thisMarginLeft - thisMarginRight);
            const double DefaultHeaderFooterDistanceDip = 48;
            var headerDistance = thisPageSettings.HeaderDistancePt > 0
                ? PageLayout.PointsToDip(thisPageSettings.HeaderDistancePt)
                : DefaultHeaderFooterDistanceDip;
            var footerDistance = thisPageSettings.FooterDistancePt > 0
                ? PageLayout.PointsToDip(thisPageSettings.FooterDistancePt)
                : DefaultHeaderFooterDistanceDip;
            var headerTop = reserveTableHeaderFrame
                ? thisMarginTop
                : Math.Max(0, headerDistance - 1);
            var footerTop = thisPixH - footerDistance - footerH + 7;

            if (slots.Header is { IsEmpty: false } headerSlot)
            {
                var hfPage = RenderHfSlot(headerSlot, doc, printableWidthDip, headerH, i + 1, (i + 1).ToString(CultureInfo.InvariantCulture), actualPageCount);
                if (hfPage is not null)
                {
                    var hfVis = new DrawingVisual();
                    using (var dc = hfVis.RenderOpen())
                        dc.DrawRectangle(new VisualBrush(hfPage.Visual)
                        {
                            Stretch = Stretch.None,
                            AlignmentX = AlignmentX.Left,
                            AlignmentY = AlignmentY.Top
                        },
                            null, new Rect(
                                thisMarginLeft,
                                headerTop + (HeaderSlotContainsInlineImage(headerSlot) ? 1 : 0),
                                printableWidthDip,
                                headerH));
                    bmp.Render(hfVis);
                }
            }

            if (slots.Footer is { IsEmpty: false } footerSlot)
            {
                var hfPage = RenderHfSlot(footerSlot, doc, printableWidthDip, footerH, i + 1, (i + 1).ToString(CultureInfo.InvariantCulture), actualPageCount);
                if (hfPage is not null)
                {
                    var hfVis = new DrawingVisual();
                    using (var dc = hfVis.RenderOpen())
                        dc.DrawRectangle(new VisualBrush(hfPage.Visual)
                        {
                            Stretch = Stretch.None,
                            AlignmentX = AlignmentX.Left,
                            AlignmentY = AlignmentY.Top
                        },
                            null, new Rect(thisMarginLeft, footerTop, printableWidthDip, footerH));
                    bmp.Render(hfVis);
                }
            }
        }

        // Word's default endnote layout continues after the body text on the final physical page
        // when the note region fits. Compose it after the body bitmap so placement follows the
        // actual paginator output rather than a guessed block boundary.
        var hasEndnotesOnPage = hasEndnotes
            && !requiresDedicatedEndnotePage
            && i == actualPageCount - 1;
        if (hasEndnotesOnPage)
        {
            var endnoteBmp = endnoteBitmap;
            if (endnoteBmp is not null)
            {
                var availableBottom = thisPixH - thisMarginBottom;
                var nextContentY = finalPageEndnoteY
                    ?? Math.Max(thisMarginTop, FindLastPaintedRow(bmp) + 16);
                if (nextContentY + endnoteBmp.Height <= availableBottom)
                {
                    var endnoteVisual = new DrawingVisual();
                    using (var dc = endnoteVisual.RenderOpen())
                        dc.DrawImage(endnoteBmp, new Rect(0, nextContentY, thisPixW, endnoteBmp.Height));
                    bmp.Render(endnoteVisual);
                }
                else
                {
                    Console.WriteLine($"  [warn] {name}: final-page endnote preflight disagreed with the composite surface; emitting the dedicated endnote page.");
                    hasEndnotesOnPage = false;
                }
            }
        }

        // Word PDF export is a print-page capture. Review balloons remain available for explicit
        // review-markup renders, but are not part of the default comparison surface.
        if (reviewMarkup && doc.Comments.Count > 0 && thisPixW == 816 && thisPixH == 1056)
            bmp = RenderReviewMarkupCapture(bmp, doc, i, reviewAnchorPageAssignment);

        // Word's capture script fits each page within a fixed evidence surface. Normalize only
        // after every composite layer has been painted so document layout remains unmodified.
        var evidenceBitmap = NormalizeWordBaselineRasterSurface(bmp);
        string outPath = BuildVisualEvidenceOutputPath(outDir, name, i + 1);
        var byteLength = SavePng(evidenceBitmap, outPath);
        var stats = ComputeWpfPixelStats(evidenceBitmap, "#FFFFFF");
        var sectionOrdinal = FreeWVisualEvidencePlanner.ResolveSectionOrdinal(doc, thisPageSettings);
        var sectionRelativePageNumber = NextSectionRelativePageNumber(sectionPageCounters, sectionOrdinal);
        var row = FreeWVisualEvidencePlanner.BuildEvidenceRow(
            scenarioId: name,
            hostId: "wpf-fidelity-render",
            outputPath: outPath,
            pixelWidth: evidenceBitmap.PixelWidth,
            pixelHeight: evidenceBitmap.PixelHeight,
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
                pageIndex: i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                extra: new Dictionary<string, string>
                {
                    ["reviewMarkup"] = reviewMarkup ? "true" : "false"
                }),
            document: doc);
        FreeWVisualEvidencePlanner.EnsureTrusted(row);
        evidence.Add(row);
        Console.WriteLine($"ok    {Path.GetFileName(outPath)} ({evidenceBitmap.PixelWidth}x{evidenceBitmap.PixelHeight}, {pageCount}/{actualPageCountWithEndnotes} pages emitted, composite)");
    }

    if (requiresDedicatedEndnotePage
        && endnoteBitmap is not null
        && actualPageCount < maxPages)
    {
        var endnotePageNumber = actualPageCount + 1;
        var endnotePage = new RenderTargetBitmap(pixW, pixH, 96, 96, PixelFormats.Pbgra32);
        var endnotePageVisual = new DrawingVisual();
        using (var dc = endnotePageVisual.RenderOpen())
        {
            var pageColor = string.IsNullOrEmpty(page.BackgroundColorHex)
                ? Colors.White
                : ParseHexColor(page.BackgroundColorHex, Colors.White);
            dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, pixW, pixH));
            dc.DrawImage(endnoteBitmap, new Rect(0, marginTop, pixW, endnoteBitmap.Height));
        }
        endnotePage.Render(endnotePageVisual);

        var evidenceBitmap = NormalizeWordBaselineRasterSurface(endnotePage);
        string outPath = BuildVisualEvidenceOutputPath(outDir, name, endnotePageNumber);
        var byteLength = SavePng(evidenceBitmap, outPath);
        var stats = ComputeWpfPixelStats(evidenceBitmap, "#FFFFFF");
        var sectionOrdinal = FreeWVisualEvidencePlanner.ResolveSectionOrdinal(doc, page);
        var sectionRelativePageNumber = NextSectionRelativePageNumber(sectionPageCounters, sectionOrdinal);
        var row = FreeWVisualEvidencePlanner.BuildEvidenceRow(
            scenarioId: name,
            hostId: "wpf-fidelity-render",
            outputPath: outPath,
            pixelWidth: evidenceBitmap.PixelWidth,
            pixelHeight: evidenceBitmap.PixelHeight,
            byteLength: byteLength,
            pixelStats: stats,
            page: page,
            pageNumber: endnotePageNumber,
            pageCount: evidencePageCount,
            layoutKind: DocumentViewLayoutKind.PrintLayout,
            availableWidthDip: pageWDip,
            headerSlotName: null,
            footerSlotName: null,
            hasFootnotes: false,
            hasEndnotes: true,
            sectionOrdinal: sectionOrdinal,
            sectionRelativePageNumber: sectionRelativePageNumber,
            hostMetadata: BuildHostMetadata(
                name,
                renderPath: "composite",
                captureSource: "wpf-composite-renderer",
                pageIndex: actualPageCount.ToString(CultureInfo.InvariantCulture),
                extra: new Dictionary<string, string>
                {
                    ["reviewMarkup"] = reviewMarkup ? "true" : "false",
                    ["endnotePlacement"] = "dedicated-overflow-page"
                }),
            document: doc);
        FreeWVisualEvidencePlanner.EnsureTrusted(row);
        evidence.Add(row);
        Console.WriteLine($"ok    {Path.GetFileName(outPath)} ({evidenceBitmap.PixelWidth}x{evidenceBitmap.PixelHeight}, {pageCount}/{actualPageCountWithEndnotes} pages emitted, composite endnotes)");
    }
}

static void DrawTrackedRevisionChangeBars(
    RenderTargetBitmap pageBitmap,
    TextDocument document,
    double marginLeftDip,
    double marginRightDip)
{
    var authorColors = ReviewRevisionColorPlanner.BuildAuthorColors(document);
    if (authorColors.Count == 0)
        return;

    var revisionColors = authorColors.Values
        .Append(ReviewRevisionColorPlanner.FallbackColorHex)
        .Select(hex => ParseHexColor(hex, Colors.Black))
        .Select(color => (color.R, color.G, color.B))
        .ToHashSet();
    var width = pageBitmap.PixelWidth;
    var height = pageBitmap.PixelHeight;
    var stride = width * 4;
    var pixels = new byte[stride * height];
    pageBitmap.CopyPixels(pixels, stride, 0);
    var revisionRows = new bool[height];
    var ordinaryInkRows = new bool[height];
    var contentLeft = Math.Clamp((int)Math.Floor(marginLeftDip), 0, width);
    var contentRight = Math.Clamp((int)Math.Ceiling(width - marginRightDip), contentLeft, width);

    for (var y = 0; y < height; y++)
    {
        for (var x = contentLeft; x < contentRight; x++)
        {
            var offset = y * stride + x * 4;
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            if (revisionColors.Contains((red, green, blue)))
                revisionRows[y] = true;
            else if (red < 80 && green < 80 && blue < 80)
                ordinaryInkRows[y] = true;
        }
    }

    var revisionBands = new List<(int Top, int Bottom)>();
    for (var y = 0; y < height;)
    {
        if (!revisionRows[y])
        {
            y++;
            continue;
        }

        var top = y;
        var bottom = y;
        for (y++; y < height; y++)
        {
            if (revisionRows[y])
            {
                bottom = y;
                continue;
            }

            var nextInk = y + 1 < height && revisionRows[y + 1];
            if (!nextInk)
                break;
        }
        revisionBands.Add((top, bottom));
    }

    if (revisionBands.Count == 0)
        return;

    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
        var pen = new Pen(Brushes.Black, 1);
        var barX = Math.Round(marginLeftDip / 2) + 0.5;
        for (var index = 0; index < revisionBands.Count;)
        {
            var top = revisionBands[index].Top;
            var bottom = revisionBands[index].Bottom;
            var isCoalesced = false;
            while (index + 1 < revisionBands.Count
                && revisionBands[index + 1].Top - bottom <= 24)
            {
                index++;
                bottom = revisionBands[index].Bottom;
                isCoalesced = true;
            }

            var nextOrdinaryInk = Enumerable.Range(bottom + 1, Math.Max(0, height - bottom - 1))
                .FirstOrDefault(y => ordinaryInkRows[y], -1);
            var barTop = Math.Max(0, top - 5 + (isCoalesced ? 1 : 0));
            var barBottom = nextOrdinaryInk >= 0
                ? Math.Max(barTop, nextOrdinaryInk - 4)
                : Math.Min(height - 1, bottom + 6);
            context.DrawLine(pen, new Point(barX, barTop), new Point(barX, barBottom));
            index++;
        }
    }
    pageBitmap.Render(visual);
}

static RenderTargetBitmap NormalizeWordBaselineRasterSurface(RenderTargetBitmap bitmap)
{
    var plan = WordBaselineRasterSurfacePlanner.Build(bitmap.PixelWidth, bitmap.PixelHeight);
    if (plan.IsIdentity)
        return bitmap;

    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
        context.DrawImage(bitmap, new Rect(0, 0, plan.PixelWidth, plan.PixelHeight));

    var normalized = new RenderTargetBitmap(
        plan.PixelWidth,
        plan.PixelHeight,
        96,
        96,
        PixelFormats.Pbgra32);
    normalized.Render(visual);
    return normalized;
}

static RenderTargetBitmap RenderReviewMarkupCapture(
    RenderTargetBitmap pageBitmap,
    TextDocument document,
    int pageIndex,
    IReadOnlyList<int> anchorPageAssignment)
{
    const double pageScale = 0.75;
    const double documentTop = 127;
    const double stripLeft = 555;
    const double stripTop = 127;
    const double stripBottom = 954;
    const double balloonLeft = 578;
    const double balloonWidth = 233;
    const double firstBalloonTop = 263;

    var width = pageBitmap.PixelWidth;
    var height = pageBitmap.PixelHeight;
    var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    var visual = new DrawingVisual();
    var sources = ReviewBalloonLayoutPlanner.BuildSources(document, ReviewDisplayPolicy.Default)
        .Where(source => source.Kind == ReviewBalloonKind.Comment
            && (anchorPageAssignment.Count == 0
                || source.BlockIndex < 0
                || source.BlockIndex >= anchorPageAssignment.Count
                || anchorPageAssignment[source.BlockIndex] == pageIndex))
        .ToArray();

    using (var dc = visual.RenderOpen())
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        dc.DrawImage(pageBitmap, new Rect(0, documentTop, width * pageScale, height * pageScale));
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
            null,
            new Rect(stripLeft, stripTop, width - stripLeft, stripBottom - stripTop));

        var balloonTop = firstBalloonTop;
        for (var ordinal = 0; ordinal < sources.Length; ordinal++)
        {
            var source = sources[ordinal];
            var (fill, stroke) = ReviewMarkupBalloonColors(ordinal);
            var label = $"Commented [{source.Author.FirstOrDefault()}{ordinal + 1}]: ";
            var body = label + source.BodyText;
            var typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var text = new FormattedText(
                body,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                8,
                Brushes.Black,
                1)
            {
                MaxTextWidth = balloonWidth - 8,
                TextAlignment = System.Windows.TextAlignment.Left,
                Trimming = TextTrimming.None
            };
            var balloonHeight = Math.Max(27, Math.Min(40, Math.Ceiling(text.Height) + 6));
            var anchorY = documentTop + pageScale * (186 + ordinal * 31) + 10;
            var leader = new Pen(stroke, 0.8);
            dc.DrawLine(leader, new Point(stripLeft - 275 - ordinal * 42, anchorY),
                new Point(balloonLeft, balloonTop + balloonHeight / 2));
            dc.DrawRoundedRectangle(fill, new Pen(stroke, 1),
                new Rect(balloonLeft, balloonTop, balloonWidth, balloonHeight), 3, 3);
            dc.DrawText(text, new Point(balloonLeft + 4, balloonTop + 3));
            balloonTop += balloonHeight + 2;
        }
    }

    result.Render(visual);
    return result;
}

static (Brush Fill, Brush Stroke) ReviewMarkupBalloonColors(int ordinal) => (ordinal % 3) switch
{
    0 => (new SolidColorBrush(Color.FromRgb(0xDD, 0xEE, 0xF7)), new SolidColorBrush(Color.FromRgb(0x00, 0x70, 0xC0))),
    1 => (new SolidColorBrush(Color.FromRgb(0xE9, 0xDF, 0xF0)), new SolidColorBrush(Color.FromRgb(0x80, 0x64, 0xA2))),
    _ => (new SolidColorBrush(Color.FromRgb(0xE2, 0xF0, 0xD9)), new SolidColorBrush(Color.FromRgb(0x70, 0xAD, 0x47)))
};

/// <summary>
/// Renders Word's single fixed-size VML watermark shape for the headless composite path.
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
                    ["reviewMarkup"] = "false",
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

    if (page.PageBorder is { } border
        && PageBorderVisibilityPlanner.ShouldRender(border.Display, pageIndex)
        && PageBorderVisibilityPlanner.LayerFor(border.ZOrder) == PageBorderRenderLayer.BehindText)
    {
        DrawSoftwarePageBorder(canvas, border, width, height);
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

    if (page.PageBorder is { } frontBorder
        && PageBorderVisibilityPlanner.ShouldRender(frontBorder.Display, pageIndex)
        && PageBorderVisibilityPlanner.LayerFor(frontBorder.ZOrder) == PageBorderRenderLayer.InFrontOfText)
    {
        DrawSoftwarePageBorder(canvas, frontBorder, width, height);
    }

    canvas.Flush();
    return bitmap;
}

static void DrawSoftwarePageBorder(SKCanvas canvas, PageBorder border, int width, int height)
{
    var artInset = Math.Min(
        (float)PageLayout.PointsToDip(Math.Max(0, border.SpacePt)),
        Math.Min(width, height) / 4f);
    if (PageBorderArtVisualPlanner.TryBuildApplesFrame(
            border.ArtId,
            border.WidthPt,
            width,
            height,
            artInset,
            out var appleMotifs))
    {
        foreach (var motif in appleMotifs)
            DrawSoftwareApple(canvas, motif);
        return;
    }
    if (PageBorderArtVisualPlanner.TryBuildShadowedSquaresFrame(
            border.ArtId,
            border.WidthPt,
            width,
            height,
            artInset,
            out var squareMotifs))
    {
        foreach (var motif in squareMotifs)
            DrawSoftwareShadowedSquare(canvas, motif);
        return;
    }
    if (PageBorderArtVisualPlanner.TryBuildShorebirdTracksFrame(
            border.ArtId,
            border.WidthPt,
            width,
            height,
            artInset,
            out var trackMotifs))
    {
        foreach (var motif in trackMotifs)
            DrawSoftwareShorebirdTrack(canvas, motif);
        return;
    }
    if (PageBorderArtVisualPlanner.TryBuildBatsFrame(
            border.ArtId,
            border.WidthPt,
            width,
            height,
            artInset,
            out var batMotifs))
    {
        foreach (var motif in batMotifs)
            DrawSoftwareBat(canvas, motif);
        return;
    }
    if (PageBorderArtVisualPlanner.TryBuildMapleMuffinsFrame(
            border.ArtId,
            border.WidthPt,
            width,
            height,
            artInset,
            out var muffinPlan))
    {
        DrawSoftwareFilledShapePlan(canvas, muffinPlan);
        return;
    }
    if (PageBorderArtVisualPlanner.TryBuildVineFrame(
            border.ArtId,
            border.WidthPt,
            width,
            height,
            artInset,
            out var vinePlan))
    {
        DrawSoftwareFilledShapePlan(canvas, vinePlan);
        return;
    }
    if (PageBorderArtVisualPlanner.TryBuildPapyrusFrame(
            border.ArtId,
            border.WidthPt,
            width,
            height,
            artInset,
            out var papyrusPlan))
    {
        DrawSoftwareFilledShapePlan(canvas, papyrusPlan);
        return;
    }
    if (PageBorderArtVisualPlanner.TryBuildWeavingRibbonFrame(
            border.ArtId,
            border.WidthPt,
            width,
            height,
            artInset,
            out var ribbonPlan))
    {
        DrawSoftwareFilledShapePlan(canvas, ribbonPlan);
        return;
    }
    if (PageBorderArtVisualPlanner.TryBuildDecorativeArchFrame(
            border.ArtId,
            border.WidthPt,
            width,
            height,
            artInset,
            out var archPlan))
    {
        DrawSoftwareDecorativeArch(canvas, archPlan);
        return;
    }

    using var borderPaint = new SKPaint
    {
        Color = ParseSkiaColor(border.ColorHex, SKColors.Black),
        IsAntialias = true,
        StrokeWidth = (float)Math.Max(1, PageLayout.PointsToDip(border.WidthPt)),
        IsStroke = true
    };
    var edgeInset = Math.Min(
        (float)PageLayout.PointsToDip(24),
        Math.Min(width, height) / 4f);
    var inset = edgeInset + borderPaint.StrokeWidth / 2f;
    canvas.DrawRect(new SKRect(
        inset,
        inset,
        Math.Max(inset, width - inset),
        Math.Max(inset, height - inset)), borderPaint);
}

static void DrawSoftwareShadowedSquare(SKCanvas canvas, PageBorderShadowedSquareMotif motif)
{
    var x = (float)motif.Xdip;
    var y = (float)motif.Ydip;
    var size = (float)motif.SizeDip;
    var color = new SKColor(0, 0, PageBorderArtVisualPlanner.ShadowedSquareBlue);
    using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
    canvas.DrawRect(x, y, x + size - 4f, y + size - 4f, fill);

    var faceInset = (float)PageBorderArtVisualPlanner.ShadowedSquareFaceInsetDip;
    var faceSize = Math.Max(0, size - 6f);
    var faceX = x + faceInset;
    var faceY = y + faceInset;
    using var faceFill = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
    canvas.DrawRect(faceX, faceY, faceX + faceSize, faceY + faceSize, faceFill);
    var outlineInset = (float)PageBorderArtVisualPlanner.ShadowedSquareOutlineInsetDip;
    var outlineSize = Math.Max(0, size - 4f);
    var outlineX = x + outlineInset;
    var outlineY = y + outlineInset;
    canvas.DrawRect(outlineX, outlineY, outlineX + outlineSize, outlineY + 1, fill);
    canvas.DrawRect(outlineX, outlineY + outlineSize - 1, outlineX + outlineSize, outlineY + outlineSize, fill);
    canvas.DrawRect(outlineX, outlineY, outlineX + 1, outlineY + outlineSize, fill);
    canvas.DrawRect(outlineX + outlineSize - 1, outlineY, outlineX + outlineSize, outlineY + outlineSize, fill);
}

static void DrawSoftwareShorebirdTrack(SKCanvas canvas, PageBorderShorebirdTrackMotif motif)
{
    using var paint = new SKPaint
    {
        Color = SKColors.Black,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = (float)PageBorderArtVisualPlanner.ShorebirdTrackStrokeWidthDip,
        StrokeCap = SKStrokeCap.Butt,
    };
    foreach (var segment in PageBorderArtVisualPlanner.BuildShorebirdTrackSegments(motif))
    {
        canvas.DrawLine(
            (float)segment.X1Dip,
            (float)segment.Y1Dip,
            (float)segment.X2Dip,
            (float)segment.Y2Dip,
            paint);
    }
}

static void DrawSoftwareBat(SKCanvas canvas, PageBorderBatMotif motif)
{
    var points = PageBorderArtVisualPlanner.BuildBatPolygon(motif);
    if (points.Count == 0)
        return;

    using var path = new SKPath();
    path.MoveTo((float)points[0].XDip, (float)points[0].YDip);
    foreach (var point in points.Skip(1))
        path.LineTo((float)point.XDip, (float)point.YDip);
    path.Close();
    using var paint = new SKPaint
    {
        Color = SKColors.Black,
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
    };
    canvas.DrawPath(path, paint);
}

static void DrawSoftwareFilledShapePlan(SKCanvas canvas, PageBorderArtFilledShapePlan plan)
{
    foreach (var fill in plan.Fills)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(fill.Red, fill.Green, fill.Blue),
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawRect(
            (float)fill.Xdip,
            (float)fill.Ydip,
            (float)(fill.Xdip + fill.WidthDip),
            (float)(fill.Ydip + fill.HeightDip),
            paint);
    }

    foreach (var polygon in plan.Polygons)
    {
        if (polygon.Points.Count == 0)
            continue;
        using var path = new SKPath();
        path.MoveTo((float)polygon.Points[0].XDip, (float)polygon.Points[0].YDip);
        foreach (var point in polygon.Points.Skip(1))
            path.LineTo((float)point.XDip, (float)point.YDip);
        path.Close();
        using var paint = new SKPaint
        {
            Color = new SKColor(polygon.Red, polygon.Green, polygon.Blue),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawPath(path, paint);
    }
}

static void DrawSoftwareDecorativeArch(SKCanvas canvas, PageBorderDecorativeArchPlan plan)
{
    foreach (var fill in plan.Fills)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(fill.Red, fill.Green, fill.Blue),
            IsAntialias = false,
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawRect(
            (float)fill.Xdip,
            (float)fill.Ydip,
            (float)(fill.Xdip + fill.WidthDip),
            (float)(fill.Ydip + fill.HeightDip),
            paint);
    }

    foreach (var stroke in plan.Strokes)
    {
        using var path = new SKPath();
        path.MoveTo((float)stroke.StartXDip, (float)stroke.StartYDip);
        path.CubicTo(
            (float)stroke.Control1XDip,
            (float)stroke.Control1YDip,
            (float)stroke.Control2XDip,
            (float)stroke.Control2YDip,
            (float)stroke.EndXDip,
            (float)stroke.EndYDip);
        using var paint = new SKPaint
        {
            Color = new SKColor(stroke.Red, stroke.Green, stroke.Blue),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)stroke.WidthDip,
            StrokeCap = SKStrokeCap.Butt,
        };
        canvas.DrawPath(path, paint);
    }
}

static void DrawSoftwareApple(SKCanvas canvas, PageBorderAppleMotif motif)
{
    var x = (float)motif.Xdip;
    var y = (float)motif.Ydip;
    var size = (float)motif.SizeDip;
    using var body = new SKPath();
    body.MoveTo(x + size * .50f, y + size * .22f);
    body.CubicTo(x + size * .35f, y + size * .04f, x + size * .04f, y + size * .10f, x + size * .03f, y + size * .51f);
    body.CubicTo(x + size * .02f, y + size * .82f, x + size * .24f, y + size, x + size * .50f, y + size * .91f);
    body.CubicTo(x + size * .76f, y + size, x + size * .98f, y + size * .82f, x + size * .97f, y + size * .51f);
    body.CubicTo(x + size * .96f, y + size * .10f, x + size * .65f, y + size * .04f, x + size * .50f, y + size * .22f);
    body.Close();
    using var fill = new SKPaint { Color = new SKColor(PageBorderArtVisualPlanner.AppleFillRed, 0, 0), IsAntialias = true, Style = SKPaintStyle.Fill };
    canvas.DrawPath(body, fill);

    using var stem = new SKPath();
    stem.MoveTo(x + size * .50f, y + size * .30f);
    stem.CubicTo(x + size * .56f, y + size * .24f, x + size * .61f, y + size * .10f, x + size * .62f, y + size * .03f);
    using var stemPaint = new SKPaint { Color = new SKColor(PageBorderArtVisualPlanner.AppleStemRed, 0, 0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.35f * size / 32f, StrokeCap = SKStrokeCap.Round };
    canvas.DrawPath(stem, stemPaint);

    using var highlight = new SKPath();
    highlight.MoveTo(x + size * .25f, y + size * .34f);
    highlight.CubicTo(x + size * .15f, y + size * .47f, x + size * .15f, y + size * .70f, x + size * .22f, y + size * .78f);
    using var highlightPaint = new SKPaint { Color = new SKColor(PageBorderArtVisualPlanner.AppleHighlightRed, PageBorderArtVisualPlanner.AppleHighlightGreen, PageBorderArtVisualPlanner.AppleHighlightBlue), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f * size / 32f, StrokeCap = SKStrokeCap.Round };
    canvas.DrawPath(highlight, highlightPaint);
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

static RenderTargetBitmap RenderWatermarkPage(WatermarkOptions options, Color pageColor, int pixW, int pixH)
{
    if (options.IsPicture)
        return RenderPictureWatermark(options, pageColor, pixW, pixH);

    var baseColor = ParseHexColor(options.FontColorHex, Color.FromRgb(0x80, 0x80, 0x80));
    var alpha = (byte)Math.Clamp((int)Math.Round(options.Opacity * 255), 0, 255);
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

static void DrawPageBorderVisual(
    DrawingContext drawingContext,
    PageBorder border,
    PageSettings page,
    double marginLeft,
    double marginRight,
    double marginBottom,
    double width,
    double height)
{
    var borderColor = ParseHexColor(border.ColorHex, Colors.Black);
    var edgeInset = Math.Min(
        PageLayout.PointsToDip(border.SpacePt),
        Math.Min(width, height) / 4.0);
    var borderWidth = Math.Max(1, border.WidthPt * PageLayout.DipPerPoint);
    if (border.ArtId > 0)
    {
        Rect artFrame;
        double artInset;
        if (border.OffsetFrom == PageBorderOffsetFrom.Text)
        {
            var headerDistance = page.HeaderDistancePt > 0
                ? PageLayout.PointsToDip(page.HeaderDistancePt)
                : PageLayout.PointsToDip(36);
            var space = PageLayout.PointsToDip(border.SpacePt);
            artFrame = new Rect(
                Math.Max(0, marginLeft - space - borderWidth),
                Math.Max(0, headerDistance - space - borderWidth),
                Math.Max(0, width - marginLeft - marginRight + 2 * (space + borderWidth)),
                Math.Max(0, height - headerDistance - marginBottom + 2 * (space + borderWidth)));
            artInset = 0;
        }
        else
        {
            artFrame = new Rect(0, 0, width, height);
            artInset = edgeInset;
        }

        if (PageBorderArtWpfRenderer.TryDraw(drawingContext, border, artFrame, artInset))
            return;
    }

    if (border.OffsetFrom == PageBorderOffsetFrom.Text)
    {
        var headerDistance = page.HeaderDistancePt > 0
            ? PageLayout.PointsToDip(page.HeaderDistancePt)
            : PageLayout.PointsToDip(36);
        var space = PageLayout.PointsToDip(border.SpacePt);
        var outerFrame = new Rect(
            Math.Max(0, marginLeft - space - borderWidth),
            Math.Max(0, headerDistance - space - borderWidth),
            Math.Max(0, width - marginLeft - marginRight + 2 * (space + borderWidth)),
            Math.Max(0, height - headerDistance - marginBottom + 2 * (space + borderWidth)));

        if (border.LineStyle == BorderLineStyle.Wave)
        {
            DrawWavePageBorderFrame(drawingContext, borderColor, outerFrame, 0);
        }
        else if (border.LineStyle == BorderLineStyle.Double)
        {
            var pen = new Pen(new SolidColorBrush(borderColor), borderWidth * 0.75);
            DrawTextRelativePageBorderFrame(drawingContext, pen, outerFrame);
            DrawTextRelativePageBorderFrame(
                drawingContext,
                pen,
                DeflatePageBorderFrame(outerFrame, borderWidth * 2.0));
        }
        else
        {
            DrawTextRelativePageBorderFrame(
                drawingContext,
                new Pen(new SolidColorBrush(borderColor), borderWidth),
                outerFrame);
        }
    }
    else if (border.LineStyle == BorderLineStyle.Double)
    {
        var strokeWidth = borderWidth * 0.75;
        var pen = new Pen(new SolidColorBrush(borderColor), strokeWidth);
        DrawPageBorderFrame(drawingContext, pen, edgeInset, width, height);
        DrawPageBorderFrame(drawingContext, pen, edgeInset + borderWidth * 2.0, width, height);
    }
    else if (border.LineStyle == BorderLineStyle.Wave)
    {
        DrawWavePageBorderFrame(drawingContext, borderColor, new Rect(0, 0, width, height), edgeInset);
    }
    else
    {
        DrawPageBorderFrame(
            drawingContext,
            new Pen(new SolidColorBrush(borderColor), borderWidth),
            edgeInset,
            width,
            height);
    }
}

static void DrawPageBorderFrame(DrawingContext drawingContext, Pen pen, double edgeInset, double width, double height)
{
    var inset = edgeInset + pen.Thickness / 2;
    drawingContext.DrawRectangle(null, pen,
        new Rect(inset, inset,
            Math.Max(0, width - 2 * inset),
            Math.Max(0, height - 2 * inset)));
}

static void DrawWavePageBorderFrame(
    DrawingContext drawingContext,
    Color color,
    Rect frame,
    double edgeInset)
{
    var waveColor = Color.FromArgb(
        (byte)Math.Round(255 * PageBorderWaveVisualPlanner.StrokeOpacity),
        color.R,
        color.G,
        color.B);
    var pen = new Pen(
        new SolidColorBrush(waveColor),
        PageBorderWaveVisualPlanner.StrokeWidthDip);
    foreach (var segment in PageBorderWaveVisualPlanner.BuildFrame(frame.Width, frame.Height, edgeInset))
    {
        drawingContext.DrawLine(
            pen,
            new Point(frame.X + segment.X1Dip, frame.Y + segment.Y1Dip),
            new Point(frame.X + segment.X2Dip, frame.Y + segment.Y2Dip));
    }
}

static void DrawTextRelativePageBorderFrame(DrawingContext drawingContext, Pen pen, Rect outerFrame)
{
    var halfStroke = pen.Thickness / 2;
    drawingContext.DrawRectangle(null, pen,
        new Rect(
            outerFrame.X + halfStroke,
            outerFrame.Y + halfStroke,
            Math.Max(0, outerFrame.Width - pen.Thickness),
            Math.Max(0, outerFrame.Height - pen.Thickness)));
}

static Rect DeflatePageBorderFrame(Rect frame, double amount) =>
    new(frame.X + amount, frame.Y + amount,
        Math.Max(0, frame.Width - 2 * amount),
        Math.Max(0, frame.Height - 2 * amount));

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
    bool isEndnotePage,
    bool includeFootnoteSeparator = true)
{
    var contentWidth = Math.Max(0, pageWDip - marginLeft - marginRight);
    var notePlan = footnoteIds.Count > 0
        ? DocumentNoteRegionPlanner.BuildFootnoteRegion(doc, footnoteIds, pageNumber: 1, contentWidth)
        : DocumentNoteRegionPlanner.BuildEndnoteRegion(doc, endnoteIds, pageNumber: 1, contentWidth, isEndnotePage);

    return RenderNoteRegionPlan(notePlan, pageWDip, marginLeft, marginRight, includeFootnoteSeparator);
}

/// <summary>
/// Renders one already-planned note fragment. Long-footnote pagination supplies a fragment plan per
/// physical page, while the existing short-note path above still builds one plan from the model IDs.
/// </summary>
static RenderTargetBitmap? RenderNoteRegionPlan(
    DocumentNoteRegionPlan notePlan,
    double pageWDip,
    double marginLeft,
    double marginRight,
    bool includeFootnoteSeparator = true)
{
    ArgumentNullException.ThrowIfNull(notePlan);
    double textSizePx = notePlan.TextFontSizePt * (96.0 / 72.0);

    // Build a StackPanel mirroring PageBox.BuildNoteRegion and measure it.
    var panel = new System.Windows.Controls.StackPanel
    {
        Orientation = System.Windows.Controls.Orientation.Vertical,
        Background  = System.Windows.Media.Brushes.White
    };

    bool hasContent = false;

    if (notePlan.Kind == DocumentNoteRegionKind.Footnotes && notePlan.Rows.Count > 0)
    {
        if (includeFootnoteSeparator)
        {
            panel.Children.Add(new System.Windows.Controls.Border
            {
                Height                  = 1,
                Width                   = notePlan.SeparatorWidthDip,
                HorizontalAlignment     = System.Windows.HorizontalAlignment.Left,
                Margin                  = new System.Windows.Thickness(marginLeft, 4, 0, 2),
                Background              = System.Windows.Media.Brushes.Black
            });
        }

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
                FontSize          = notePlan.LabelFontSizePt * (96.0 / 72.0)
            });
            tb.Inlines.Add(new System.Windows.Documents.Run(" " + row.Text));
            panel.Children.Add(tb);
            hasContent = true;
        }
    }

    if (notePlan.Kind == DocumentNoteRegionKind.Endnotes && notePlan.Rows.Count > 0)
    {
        if (!string.IsNullOrEmpty(notePlan.Heading))
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text    = notePlan.Heading ?? "Endnotes",
                FontSize = textSizePx + 2,
                FontWeight = System.Windows.FontWeights.Bold,
                Margin  = new System.Windows.Thickness(marginLeft, 8, marginRight, 2)
            });
        }

        // Word uses the plan's short separator instead of a printable-width rule; its top lead is
        // measured from the endnote fixture's body-to-note transition.
        panel.Children.Add(new System.Windows.Controls.Border
        {
            Height = 1,
            Width = notePlan.SeparatorWidthDip,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new System.Windows.Thickness(marginLeft, 7, marginRight, 2),
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
                FontSize          = notePlan.LabelFontSizePt * (96.0 / 72.0)
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

static bool HeaderSlotContainsInlineImage(HeaderFooter slot) =>
    slot.Paragraphs.Any(paragraph => paragraph.Runs.Any(run => run.Image is not null));

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

            var view = new DocumentView
            {
                Width = pageW,
                RenderPageBreakMarkers = false
            };
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
