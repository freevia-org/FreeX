using System.Windows;
using System.Windows.Documents;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Computes authoritative page-break positions for the live editing surface by reusing the same
/// WPF paginator that drives Print Preview and printing. Pure computation — no side effects on the
/// editor or model. Callers (e.g. <see cref="PageBreakAdorner"/>) cache the result and invalidate on
/// content or page-settings change.
/// </summary>
internal static class PaginationEngine
{
    /// <summary>
    /// Paginates <paramref name="editor"/>'s current content at the model's page geometry and returns
    /// the page count and inter-page Y offsets in the editor's DIP coordinate space.
    ///
    /// <para>
    /// Each break Y is the position — measured from the top of the first content line in the live
    /// editor, matching the <c>PageBreakAdorner.FirstContentTop</c> anchor — where the adorner
    /// should draw its dashed marker. For overflow-driven breaks the position equals a multiple of
    /// <c>contentHeight</c> (identical to the old uniform-step approximation). For explicit
    /// <c>PageBreakBefore</c> or NextPage section breaks, the position is derived from the live
    /// editor's actual block layout, so the marker lands between the two adjacent paragraphs rather
    /// than at a fixed multiple.
    /// </para>
    ///
    /// <para>
    /// Must be called on the UI/STA thread because it calls into WPF's
    /// <see cref="DocumentPaginator"/> (layout engine) and queries text-pointer geometry on the
    /// live <see cref="DocumentView"/>.
    /// </para>
    /// </summary>
    internal static DocumentPagination Compute(DocumentView editor)
    {
        // --- Step 1: paginate a scratch clone via the authoritative print path ------------------
        var flow = PrintLayout.BuildPaginatedDocument(editor);

        // Post-process: a NextPage/EvenPage/OddPage section break is stored on the last paragraph
        // of the *preceding* section (the FreeW/docx convention). The XAML round-trip in
        // BuildPaginatedDocument strips the model Tag that carries SectionBreak, so the paginator
        // sees no break for those paragraphs. Fix by scanning the model blocks in parallel and
        // setting BreakPageBefore=true on the scratch paragraph that *follows* a page-type section
        // break marker.
        ApplySectionBreakFlags(editor, flow);

        var page = editor.Model.Page;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var (_, contentHeight) = PageLayout.ContentAreaDip(page);
        if (contentHeight <= 0)
            return DocumentPagination.Empty;

        var innerPaginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        innerPaginator.PageSize = new Size(pageWidth, pageHeight);
        innerPaginator.ComputePageCount();

        var pageCount = innerPaginator.PageCount;
        if (pageCount <= 1)
            return new DocumentPagination(Math.Max(1, pageCount), Array.Empty<double>());

        // --- Step 2: identify which blocks open new pages (explicit-break detection) -------------
        // Walk the scratch document's top-level blocks looking for BreakPageBefore. These map 1:1
        // to the live editor's FlowDocument blocks (same clone order). For each explicit-break
        // block, we can query its live Y directly; for overflow-driven pages we fall back to the
        // uniform contentHeight multiple.
        var scratchBlocks = flow.Blocks.ToArray();
        var liveDoc = editor.Document;
        if (liveDoc is null)
        {
            // No live layout — return uniform breaks.
            return new DocumentPagination(pageCount, UniformBreaks(pageCount, contentHeight));
        }

        var liveBlocks = liveDoc.Blocks.ToArray();

        // Ensure the live editor has a valid layout so GetCharacterRect calls below succeed.
        // When the editor is in a window this is a no-op (layout is already valid). When the
        // editor has never been arranged (e.g. in unit tests), this forces WPF to compute layout.
        try
        {
            editor.Measure(new System.Windows.Size(editor.ActualWidth > 0 ? editor.ActualWidth : pageWidth,
                double.PositiveInfinity));
            editor.Arrange(new Rect(new System.Windows.Size(
                editor.ActualWidth > 0 ? editor.ActualWidth : pageWidth, editor.DesiredSize.Height)));
        }
        catch (InvalidOperationException)
        {
            // If Measure/Arrange fails (unusual), fall through to the GetCharacterRect calls which
            // will also fail gracefully with the fallback path below.
        }

        // Get the adorner's Y origin (top of first content line).
        double topY;
        try
        {
            var firstRect = liveDoc.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (firstRect.IsEmpty)
                return new DocumentPagination(pageCount, UniformBreaks(pageCount, contentHeight));
            topY = firstRect.Top;
        }
        catch (InvalidOperationException)
        {
            return new DocumentPagination(pageCount, UniformBreaks(pageCount, contentHeight));
        }

        // Build per-page cumulative content heights from the paginator's actual DocumentPage
        // geometry. This gives overflow breaks an accurate Y rather than the uniform contentHeight
        // multiple (objective 3a).
        var cumulativePageY = new double[pageCount]; // cumulativePageY[pg] = Y where page pg ENDS
        double cumY = 0;
        for (int pg = 0; pg < pageCount; pg++)
        {
            double pageContentH;
            try
            {
                var dpg = innerPaginator.GetPage(pg);
                pageContentH = dpg.ContentBox.IsEmpty ? contentHeight : dpg.ContentBox.Height;
            }
            catch
            {
                pageContentH = contentHeight;
            }
            cumY += pageContentH;
            cumulativePageY[pg] = cumY;
        }

        // --- Step 3: build per-page break Ys ---------------------------------------------------
        // For each explicit-break paragraph (BreakPageBefore=true in the scratch clone), record
        // the page it opens and the Y of the corresponding block in the live editor. Overflow-
        // driven page breaks are represented by cumulative per-page content heights derived from
        // the paginator's actual DocumentPage geometry.
        //
        // We assign explicit breaks to successive pages in order of appearance. Any page not
        // covered by an explicit break is an overflow break using cumulativePageY.
        var explicitBreakAtPage = new Dictionary<int, double>(); // page index → break Y

        int currentPage = 0;
        for (int blockIdx = 0; blockIdx < scratchBlocks.Length && currentPage < pageCount - 1; blockIdx++)
        {
            var block = scratchBlocks[blockIdx];

            // A BreakPageBefore paragraph forces itself onto a new page.
            bool isExplicitBreak = block is System.Windows.Documents.Paragraph p && p.BreakPageBefore;

            if (isExplicitBreak && blockIdx > 0)
            {
                currentPage++;
                if (currentPage >= pageCount)
                    break;

                // Get the live Y of this block's first insertion point.
                double breakY = GetLiveBlockTopY(liveBlocks, blockIdx, topY, currentPage, contentHeight);
                explicitBreakAtPage[currentPage] = breakY;
            }
        }

        // Build the final break array: explicit breaks use live Ys; overflow breaks use per-page
        // cumulative heights from the paginator's actual DocumentPage geometry.
        var breaks = new double[pageCount - 1];
        for (var pg = 1; pg < pageCount; pg++)
        {
            breaks[pg - 1] = explicitBreakAtPage.TryGetValue(pg, out var explicitY)
                ? explicitY
                : cumulativePageY[pg - 1];
        }

        return new DocumentPagination(pageCount, breaks);
    }

    private static double GetLiveBlockTopY(
        System.Windows.Documents.Block[] liveBlocks,
        int blockIdx,
        double topY,
        int pageIndex,
        double contentHeight)
    {
        if (blockIdx >= liveBlocks.Length)
            return pageIndex * contentHeight;

        try
        {
            var liveBlock = liveBlocks[blockIdx];
            var ptr = liveBlock.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            if (ptr is null)
                return pageIndex * contentHeight;
            var rect = ptr.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty)
                return pageIndex * contentHeight;
            // Y of the block relative to the adorner's origin.
            return rect.Top - topY;
        }
        catch (InvalidOperationException)
        {
            return pageIndex * contentHeight;
        }
    }

    private static double[] UniformBreaks(int pageCount, double contentHeight)
    {
        var breaks = new double[pageCount - 1];
        for (var i = 0; i < breaks.Length; i++)
            breaks[i] = (i + 1) * contentHeight;
        return breaks;
    }

    /// <summary>
    /// Post-processes the scratch <see cref="FlowDocument"/> produced by
    /// <see cref="PrintLayout.BuildPaginatedDocument"/> to honour FreeW section breaks.
    ///
    /// <para>
    /// In the FreeW/docx model a <c>NextPage</c>, <c>EvenPage</c>, or <c>OddPage</c> section break
    /// is stored on the <em>last</em> paragraph of the preceding section (as
    /// <see cref="FreeW.Core.Model.Paragraph.SectionBreak"/>). Because the XAML round-trip in
    /// <see cref="PrintLayout.BuildPaginatedDocument"/> strips the non-public Tag that carries
    /// this metadata, the scratch clone's WPF paragraphs have no <c>BreakPageBefore</c> for those
    /// positions. This method repairs that by scanning the model blocks and setting
    /// <c>BreakPageBefore = true</c> on the WPF paragraph that immediately <em>follows</em> a
    /// page-type section-break marker.
    /// </para>
    /// </summary>
    /// <summary>
    /// Computes which 0-based page index each top-level model block belongs on, using the same
    /// scratch-clone pagination that <see cref="Compute"/> uses.  The returned array has one entry
    /// per top-level block in <paramref name="editor"/>'s model; entry <c>i</c> is the 0-based page
    /// index for model block <c>i</c>.
    ///
    /// <para>
    /// <strong>Algorithm:</strong>
    /// <list type="number">
    ///   <item>Build a scratch <see cref="FlowDocument"/> clone and apply section-break flags
    ///   (identical to <see cref="Compute"/>).</item>
    ///   <item>Run the WPF paginator to determine the page count.</item>
    ///   <item>Walk the scratch clone's top-level blocks in order.  A block whose
    ///   <c>BreakPageBefore</c> is <c>true</c> is the first block of the <em>next</em> page.
    ///   All other blocks stay on the current page.  This mirrors how the WPF paginator assigns
    ///   blocks to pages for explicit breaks.  Overflow-driven page transitions are not detectable
    ///   from the block list alone; the engine falls back to even distribution for any pages that
    ///   are not covered by an explicit break.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// When the model is empty or pagination fails the method returns a zero-length array or an
    /// array of zeros (all blocks on page 0).  <see cref="PaginatedEditorPanel"/> treats this as a
    /// single-page document.
    /// </para>
    ///
    /// <para>Must be called on the UI/STA thread.</para>
    /// </summary>
    internal static int[] ComputeBlockPageAssignment(DocumentView editor)
    {
        var modelBlocks = editor.Model.Blocks;
        if (modelBlocks.Count == 0)
            return [];

        // Build scratch clone and run the paginator — same as Compute().
        FlowDocument flow;
        int pageCount;
        try
        {
            flow = PrintLayout.BuildPaginatedDocument(editor);
            ApplySectionBreakFlags(editor, flow);

            var page = editor.Model.Page;
            var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
            var (_, contentHeight) = PageLayout.ContentAreaDip(page);
            if (contentHeight <= 0)
                return new int[modelBlocks.Count]; // all page 0

            var innerPaginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
            innerPaginator.PageSize = new Size(pageWidth, pageHeight);
            innerPaginator.ComputePageCount();
            pageCount = Math.Max(1, innerPaginator.PageCount);
        }
        catch
        {
            // If pagination fails, assign all blocks to page 0.
            return new int[modelBlocks.Count];
        }

        if (pageCount == 1)
            return new int[modelBlocks.Count]; // all page 0

        // Walk scratch blocks: assign each to its page using BreakPageBefore.
        // A block with BreakPageBefore=true is the FIRST block of a new page.
        var scratchBlocks = flow.Blocks.ToArray();
        var assignment = new int[modelBlocks.Count];
        int currentPage = 0;

        for (int i = 0; i < scratchBlocks.Length && i < modelBlocks.Count; i++)
        {
            var scratch = scratchBlocks[i];
            bool isExplicitBreak = scratch is System.Windows.Documents.Paragraph p && p.BreakPageBefore;

            // Block 0 can never start a new page (it is the first block of page 0).
            if (isExplicitBreak && i > 0)
            {
                currentPage = Math.Min(currentPage + 1, pageCount - 1);
            }

            assignment[i] = currentPage;
        }

        // If the explicit breaks only account for fewer pages than the paginator says, the
        // remaining pages are overflow-driven.  In that case the last explicit-break page
        // effectively absorbs all overflow blocks — the coordinator will still round-trip them
        // correctly because the commit path doesn't care which page box a block lives in, only
        // the document order.  No further redistribution is needed for 3b-1.

        return assignment;
    }

    private static void ApplySectionBreakFlags(DocumentView editor, FlowDocument flow)
    {
        var modelBlocks = editor.Model.Blocks;
        var scratchBlocks = flow.Blocks.ToArray();

        for (int i = 0; i < modelBlocks.Count - 1 && i < scratchBlocks.Length - 1; i++)
        {
            if (modelBlocks[i] is FreeW.Core.Model.Paragraph { SectionBreak: { } sec }
                && sec.BreakKind is SectionBreakKind.NextPage
                                 or SectionBreakKind.EvenPage
                                 or SectionBreakKind.OddPage)
            {
                if (scratchBlocks[i + 1] is System.Windows.Documents.Paragraph nextWpf)
                    nextWpf.BreakPageBefore = true;
            }
        }
    }
}
