using System.Windows;
using System.Windows.Documents;
using FreeW.App.Presentation.DocumentView;
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
    /// Returns the canonical FlowDocument offsets at which WPF starts each realized paginator page.
    /// Consumers that need page-specific body regions must carry these positions forward instead of
    /// rebuilding model-block fragments, which loses WPF's line-level continuation ownership.
    /// </summary>
    internal static IReadOnlyList<int> ComputeCanonicalPageStartOffsets(
        FlowDocument flow,
        DocumentPaginator paginator)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(paginator);

        paginator.ComputePageCount();
        if (paginator is not DynamicDocumentPaginator dynamicPaginator)
            return [0];

        var pageCount = Math.Max(1, paginator.PageCount);
        var starts = new List<int>(pageCount);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var position = dynamicPaginator.GetPagePosition(paginator.GetPage(pageIndex));
            if (position is not TextPointer pointer)
                return [0];

            starts.Add(Math.Max(0, flow.ContentStart.GetOffsetToPosition(pointer)));
        }

        var origin = starts[0];
        return starts.Select(offset => Math.Max(0, offset - origin)).ToList();
    }

    /// <summary>
    /// Resolves each rendered footnote marker to the physical page owned by the supplied paginator.
    /// This must use the exact FlowDocument that a compositor renders: a separately paginated editor
    /// surface may use different page padding and therefore assign the same marker to a different page.
    /// </summary>
    internal static IReadOnlyDictionary<int, IReadOnlyList<int>> ComputeFootnotePageOwnership(
        FlowDocument flow,
        DocumentPaginator paginator)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(paginator);

        paginator.ComputePageCount();
        if (paginator is not DynamicDocumentPaginator dynamicPaginator)
            return new Dictionary<int, IReadOnlyList<int>>();

        var pageCount = Math.Max(1, paginator.PageCount);
        try
        {
            return DocumentView.CollectFootnoteMarkers(flow.Blocks)
                .Select(marker => (marker.FootnoteId, PageIndex: dynamicPaginator.GetPageNumber(marker.Position)))
                .Where(marker => marker.PageIndex >= 0 && marker.PageIndex < pageCount)
                .GroupBy(marker => marker.PageIndex)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<int>)group.Select(marker => marker.FootnoteId).Distinct().ToList());
        }
        catch (NotSupportedException)
        {
            return new Dictionary<int, IReadOnlyList<int>>();
        }
        catch (InvalidOperationException)
        {
            return new Dictionary<int, IReadOnlyList<int>>();
        }
    }

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
    ///   <item>Walk the scratch clone's top-level blocks in order and query the WPF paginator for
    ///   each block's page. Blocks containing footnote references use the reference position itself
    ///   so a paragraph that straddles a boundary keeps its note with the page containing the mark.</item>
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

        // Build scratch clone (same as Compute()) purely to obtain Tag-preserving WPF blocks that
        // line up 1:1 with modelBlocks. It is never paginated as one whole: TextDocument.Page (and
        // therefore this flow's PageWidth/PageHeight/PagePadding) is only the FINAL section's
        // geometry, so pagination must be split per SG-defined page segment (see BuildPageSegments)
        // and each segment measured against its OWN section's PageSettings. A single uniform
        // PageSize here would size an earlier, smaller-content-area section (e.g. a landscape
        // section followed by a taller portrait final section) against the wrong page box, packing
        // more content per page than actually fits and silently dropping the overflow on
        // print/preview.
        FlowDocument flow;
        System.Windows.Documents.Block[] scratchBlocks;
        try
        {
            flow = PrintLayout.BuildPaginatedDocument(editor);
            ApplySectionBreakFlags(editor, flow);
            scratchBlocks = flow.Blocks.ToArray();
            flow.Blocks.Clear(); // detach so segment slices can be re-parented into per-segment flows
        }
        catch
        {
            // If pagination fails, assign all blocks to page 0.
            return new int[modelBlocks.Count];
        }

        var segments = BuildPageSegments(modelBlocks, editor.Model.Page);
        var assignment = new int[modelBlocks.Count];

        try
        {
            var pageOffset = 0;
            foreach (var segment in segments)
            {
                pageOffset += ComputeSegmentPageAssignment(
                    flow, scratchBlocks, modelBlocks, editor.Model, segment, pageOffset, assignment);
            }
        }
        catch
        {
            // If pagination fails, assign all blocks to page 0.
            return new int[modelBlocks.Count];
        }

        return assignment;
    }

    /// <summary>
    /// Paginates one <see cref="PageSegment"/> — a run of top-level model blocks that all share one
    /// section's <see cref="PageSettings"/> — at that section's own geometry, and writes each of the
    /// segment's blocks' 0-based page index (offset by <paramref name="pageOffset"/>) into
    /// <paramref name="assignment"/>. Returns the number of physical pages this segment occupies, so
    /// the caller can advance <paramref name="pageOffset"/> for the next segment.
    /// </summary>
    private static int ComputeSegmentPageAssignment(
        FlowDocument sourceFlow,
        System.Windows.Documents.Block[] scratchBlocks,
        IReadOnlyList<FreeW.Core.Model.Block> modelBlocks,
        TextDocument document,
        PageSegment segment,
        int pageOffset,
        int[] assignment)
    {
        var end = Math.Min(segment.End, Math.Min(scratchBlocks.Length, modelBlocks.Count));
        if (segment.Start >= end)
            return 0; // empty segment (e.g. a section break marker was the document's last block)

        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(segment.Page);
        var (left, top, right, bottom) = PageLayout.MarginsDip(segment.Page);
        var (_, contentHeight) = PageLayout.ContentAreaDip(segment.Page);
        if (contentHeight <= 0)
        {
            for (var i = segment.Start; i < end; i++)
                assignment[i] = pageOffset;
            return 1;
        }

        // Mirror PrintLayout.BuildPaginatedDocument's footnote body-reserve: when the model has
        // footnotes, PrintLayout shrinks the body's usable height by the estimated rendered height
        // of the footnote region (plus a fixed frame clearance) so the note region never collides
        // with body text. Without doing the same here, this segment's PagePadding.Bottom is shorter
        // than what print/preview actually reserve, so the WPF paginator packs more content per page
        // than the real (footnote-bearing) page box has room for — over-filling pages and pushing
        // content under the footnote region.
        //
        // The reserve itself must be sized per page, not for the whole document: reserving the
        // combined height of every footnote the document owns on every single page (the round-140
        // bug) shrinks the usable body area roughly (footnote count) times too much for any document
        // whose footnotes are spread across more than one page. A reserve-free provisional pass over
        // this segment's own blocks discovers which footnotes actually land on each page, and only
        // the tallest single page's region is reserved.
        var footnoteReserveDip = 0.0;
        if (document.Footnotes.Count > 0)
        {
            var (_, contentWidthDip) = PageLayout.ContentAreaDip(segment.Page);
            var provisionalFlow = new FlowDocument
            {
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                PagePadding = new Thickness(left, top, right, bottom),
                FontFamily = sourceFlow.FontFamily,
                FontSize = sourceFlow.FontSize
            };
            DocumentView.ApplyColumnLayout(provisionalFlow, segment.Page, useNativeColumnRule: false);
            for (var i = segment.Start; i < end; i++)
                provisionalFlow.Blocks.Add(scratchBlocks[i]);
            try
            {
                footnoteReserveDip = ComputeMaxPerPageFootnoteReserveDip(
                    provisionalFlow,
                    modelBlocks.Skip(segment.Start).Take(end - segment.Start).ToList(),
                    document,
                    segment.Page,
                    contentWidthDip);
            }
            finally
            {
                // Detach so the same scratch blocks can be re-parented into the real segment flow
                // below — a WPF element may belong to only one FlowDocument at a time.
                provisionalFlow.Blocks.Clear();
            }
        }

        var segmentFlow = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(left, top, right, bottom + footnoteReserveDip),
            FontFamily = sourceFlow.FontFamily,
            FontSize = sourceFlow.FontSize
        };
        DocumentView.ApplyColumnLayout(segmentFlow, segment.Page, useNativeColumnRule: false);
        for (var i = segment.Start; i < end; i++)
            segmentFlow.Blocks.Add(scratchBlocks[i]);

        var innerPaginator = ((IDocumentPaginatorSource)segmentFlow).DocumentPaginator;
        innerPaginator.PageSize = new Size(pageWidth, pageHeight);
        innerPaginator.ComputePageCount();
        var segmentPageCount = Math.Max(1, innerPaginator.PageCount);

        // Ask the paginator for each block's actual page. The previous implementation only
        // advanced on explicit BreakPageBefore flags, leaving all ordinary overflow blocks on
        // page 0 and attaching later-page footnotes to the wrong body page.
        var dynamicPaginator = innerPaginator as DynamicDocumentPaginator;
        var currentLocalPage = 0;

        for (var i = segment.Start; i < end; i++)
        {
            var scratch = scratchBlocks[i];
            try
            {
                if (dynamicPaginator is not null)
                {
                    var markerPositions = DocumentView.CollectFootnoteMarkerPositions([scratch]);
                    var pageNumber = markerPositions.Count > 0
                        ? markerPositions
                            .Select(dynamicPaginator.GetPageNumber)
                            .Where(page => page >= 0)
                            .DefaultIfEmpty(dynamicPaginator.GetPageNumber(scratch.ContentStart))
                            .Max()
                        : dynamicPaginator.GetPageNumber(scratch.ContentStart);
                    if (pageNumber >= 0)
                        currentLocalPage = Math.Clamp(pageNumber, 0, segmentPageCount - 1);
                }
            }
            catch (NotSupportedException) { }
            catch (InvalidOperationException) { }

            assignment[i] = pageOffset + currentLocalPage;
        }

        return segmentPageCount;
    }

    /// <summary>
    /// Given a fully laid-out provisional <see cref="FlowDocument"/> (its real blocks already added,
    /// page geometry and margins-only <see cref="FlowDocument.PagePadding"/> already set — no footnote
    /// reserve applied yet), returns the reserve height needed at the foot of the tallest single page,
    /// based on the footnotes that actually land on each of that flow's pages — the same per-page
    /// reserve Print/Print Preview/PDF/XPS need. This is deliberately NOT the combined height of every
    /// footnote the whole document owns: that would shrink every page's usable body area far more than
    /// Word does whenever a document's footnotes are spread across more than one page.
    /// <para>
    /// <paramref name="modelBlocks"/> must line up 1:1, by index, with <paramref name="provisionalFlow"/>'s
    /// top-level blocks (the invariant every clone built by <c>PrintLayout.BuildPaginatedDocument</c>
    /// preserves). Footnote ids are read directly from each model block's <c>Run.FootnoteId</c> rather
    /// than from the WPF clone's elements: <c>PrintLayout.CloneElement</c>'s XamlWriter/XamlReader round
    /// trip never carries the non-public <c>Tag</c> a live editor surface stamps its <c>Run</c>s with
    /// back onto the deserialized clone, so a Tag-based marker lookup (<see cref="DocumentView.CollectFootnoteMarkers"/>)
    /// silently finds nothing on any print/preview/PDF/XPS or page-break-gutter clone flow.
    /// </para>
    /// </summary>
    internal static double ComputeMaxPerPageFootnoteReserveDip(
        FlowDocument provisionalFlow,
        IReadOnlyList<FreeW.Core.Model.Block> modelBlocks,
        TextDocument document,
        PageSettings page,
        double contentWidthDip)
    {
        ArgumentNullException.ThrowIfNull(provisionalFlow);
        ArgumentNullException.ThrowIfNull(modelBlocks);
        ArgumentNullException.ThrowIfNull(document);

        if (document.Footnotes.Count == 0)
            return 0;

        const double footnoteFrameClearanceDip = 24.0;

        var provisionalPaginator = ((IDocumentPaginatorSource)provisionalFlow).DocumentPaginator;
        provisionalPaginator.PageSize = new Size(provisionalFlow.PageWidth, provisionalFlow.PageHeight);
        provisionalPaginator.ComputePageCount();

        var footnoteIdsByPage = ComputeFootnoteIdsByPageFromModel(provisionalFlow, provisionalPaginator, modelBlocks);
        var maxReserveDip = 0.0;
        foreach (var pageFootnoteIds in footnoteIdsByPage.Values)
        {
            if (pageFootnoteIds.Count == 0)
                continue;
            var pagePlan = DocumentNoteRegionPlanner.BuildFootnoteRegion(
                document, pageFootnoteIds, pageNumber: 1, contentWidthDip);
            if (pagePlan.EstimatedHeightDip <= 0)
                continue;
            var pageReserveDip = ClampFootnoteReserveDip(
                pagePlan.EstimatedHeightDip + footnoteFrameClearanceDip, page);
            if (pageReserveDip > maxReserveDip)
                maxReserveDip = pageReserveDip;
        }

        // No page in this flow resolved to carrying any footnote reference (e.g. the flow's paginator
        // does not support the dynamic page-number query this needs): fall back to the whole-document
        // estimate as a safe upper bound rather than silently reserving nothing.
        if (footnoteIdsByPage.Count == 0)
        {
            var noteIds = document.Footnotes.Keys.OrderBy(id => id).ToList();
            var wholeDocumentPlan = DocumentNoteRegionPlanner.BuildFootnoteRegion(
                document, noteIds, pageNumber: 1, contentWidthDip);
            if (wholeDocumentPlan.EstimatedHeightDip > 0)
            {
                maxReserveDip = ClampFootnoteReserveDip(
                    wholeDocumentPlan.EstimatedHeightDip + footnoteFrameClearanceDip, page);
            }
        }

        return maxReserveDip;
    }

    /// <summary>
    /// Maps each physical page of <paramref name="paginator"/> to the distinct footnote ids referenced
    /// by the model blocks that land on it, by pairing <paramref name="flow"/>'s top-level blocks 1:1
    /// by index with <paramref name="modelBlocks"/> and reading each block's own footnote reference ids
    /// (<c>Run.FootnoteId</c>) directly from the model — see <see cref="ComputeMaxPerPageFootnoteReserveDip"/>
    /// for why this must not rely on a WPF clone's <c>Run.Tag</c>.
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlyList<int>> ComputeFootnoteIdsByPageFromModel(
        FlowDocument flow,
        DocumentPaginator paginator,
        IReadOnlyList<FreeW.Core.Model.Block> modelBlocks)
    {
        var result = new Dictionary<int, List<int>>();
        if (paginator is not DynamicDocumentPaginator dynamicPaginator)
            return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value);

        var flowBlocks = flow.Blocks.ToArray();
        var pageCount = Math.Max(1, paginator.PageCount);
        var currentPage = 0;

        for (var i = 0; i < flowBlocks.Length && i < modelBlocks.Count; i++)
        {
            try
            {
                var pageNumber = dynamicPaginator.GetPageNumber(flowBlocks[i].ContentStart);
                if (pageNumber >= 0)
                    currentPage = Math.Clamp(pageNumber, 0, pageCount - 1);
            }
            catch (NotSupportedException) { }
            catch (InvalidOperationException) { }

            foreach (var footnoteId in FootnoteIdsInModelBlock(modelBlocks[i]))
            {
                if (!result.TryGetValue(currentPage, out var list))
                    result[currentPage] = list = new List<int>();
                if (!list.Contains(footnoteId))
                    list.Add(footnoteId);
            }
        }

        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value);
    }

    private static IEnumerable<int> FootnoteIdsInModelBlock(FreeW.Core.Model.Block block)
    {
        switch (block)
        {
            case FreeW.Core.Model.Paragraph paragraph:
                foreach (var run in paragraph.Runs)
                    if (run.FootnoteId is { } id)
                        yield return id;
                break;

            case FreeW.Core.Model.Table table:
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var paragraph in cell.Paragraphs)
                            foreach (var run in paragraph.Runs)
                                if (run.FootnoteId is { } id)
                                    yield return id;
                break;
        }
    }

    /// <summary>
    /// Caps a footnote body reserve so the body it is subtracted from keeps a usable height.
    /// <para>
    /// The estimate grows with the footnote text, and nothing bounds it: footnotes long enough
    /// relative to the page reserve the whole content area, leaving the WPF paginator a zero or
    /// negative page box, which it rejects. <see cref="PageLayout.ContentAreaDip"/> clamps the
    /// margins case the same way, but the reserve is added afterwards and so escaped it. Capping
    /// means very long footnotes overflow their region instead of failing print and print preview.
    /// </para>
    /// </summary>
    internal static double ClampFootnoteReserveDip(double reserveDip, PageSettings page)
    {
        if (reserveDip <= 0)
            return 0;

        // Leave at least one line's worth of body; below that the page cannot flow anything anyway.
        const double minimumBodyHeightDip = 24.0;
        var (_, contentHeightDip) = PageLayout.ContentAreaDip(page);
        var available = contentHeightDip - minimumBodyHeightDip;
        return available <= 0 ? 0 : Math.Min(reserveDip, available);
    }

    /// <summary>
    /// Splits <paramref name="modelBlocks"/> into ordered ranges that each get measured against
    /// exactly one section's own <see cref="PageSettings"/>.
    ///
    /// <para>
    /// A new range starts at <em>every</em> section break — page-type
    /// (<see cref="SectionBreakKind.NextPage"/> / <see cref="SectionBreakKind.EvenPage"/> /
    /// <see cref="SectionBreakKind.OddPage"/>) or <see cref="SectionBreakKind.Continuous"/> alike —
    /// because a <c>Continuous</c> break can still change page width, margins, or column count, and
    /// content on either side of it must be measured against its own section's box, not a
    /// neighbouring section's. Folding a Continuous-broken section into whichever later segment
    /// happens to close the range (the previous behaviour) measured that earlier section's content
    /// with a foreign page box — wrong column widths at best, silently dropped/clipped overflow at
    /// worst when the foreign box was larger than the section's real one.
    /// </para>
    ///
    /// <para>
    /// Ranges separated only by a Continuous break are then merged back together when their two
    /// sections' layout geometry is identical (<see cref="SectionsShareLayoutGeometry"/>), so a
    /// document that uses Continuous breaks purely to vary headers/footers (no geometry change)
    /// keeps flowing as one uninterrupted run instead of gaining spurious page boundaries — mirroring
    /// <see cref="ApplySectionBreakFlags"/>, which only forces <c>BreakPageBefore</c> for page-type
    /// breaks. A page-type break always keeps its own segment, even when its geometry happens to
    /// match its neighbour's, because it must force a fresh physical page regardless.
    /// </para>
    /// </summary>
    private static List<PageSegment> BuildPageSegments(
        IReadOnlyList<FreeW.Core.Model.Block> modelBlocks,
        PageSettings finalPage)
    {
        // Pass 1: one raw range per section, tagging each with whether the break that STARTS it
        // (i.e. the break ending the previous section) is page-type — the only kind that must force
        // a fresh physical page regardless of geometry.
        var raw = new List<(int Start, int End, PageSettings Page, bool StartsNewPage)>();
        var start = 0;
        var startsNewPage = false; // the first range has no preceding break to force a new page.
        for (var i = 0; i < modelBlocks.Count; i++)
        {
            if (modelBlocks[i] is FreeW.Core.Model.Paragraph { SectionBreak: { } sectionBreak })
            {
                raw.Add((start, i + 1, sectionBreak.Page, startsNewPage));
                start = i + 1;
                startsNewPage = IsPageTypeSectionBreak(sectionBreak);
            }
        }
        raw.Add((start, modelBlocks.Count, finalPage, startsNewPage));

        // Pass 2: merge a Continuous-started range into the one before it when the two sections
        // share identical page geometry, so same-geometry Continuous breaks don't fragment pagination.
        var segments = new List<PageSegment>(raw.Count);
        foreach (var range in raw)
        {
            if (!range.StartsNewPage
                && segments.Count > 0
                && SectionsShareLayoutGeometry(segments[^1].Page, range.Page))
            {
                segments[^1] = segments[^1] with { End = range.End };
            }
            else
            {
                segments.Add(new PageSegment(range.Start, range.End, range.Page));
            }
        }

        return segments;
    }

    /// <summary>
    /// Whether two sections' <see cref="PageSettings"/> would lay out content identically — same
    /// page box, margins/gutter, and column plan — so a Continuous break between them can be treated
    /// as a no-op for pagination purposes instead of forcing a separate measurement pass. Compares
    /// exactly the fields <see cref="PageLayout.PageSizeDip"/>, <see cref="PageLayout.MarginsDip"/>,
    /// and <see cref="DocumentView.ApplyColumnLayout"/> read.
    /// </summary>
    private static bool SectionsShareLayoutGeometry(PageSettings a, PageSettings b)
    {
        if (ReferenceEquals(a, b))
            return true;

        return a.WidthPt.Equals(b.WidthPt)
            && a.HeightPt.Equals(b.HeightPt)
            && a.MarginLeftPt.Equals(b.MarginLeftPt)
            && a.MarginRightPt.Equals(b.MarginRightPt)
            && a.MarginTopPt.Equals(b.MarginTopPt)
            && a.MarginBottomPt.Equals(b.MarginBottomPt)
            && a.GutterPt.Equals(b.GutterPt)
            && a.GutterAtTop == b.GutterAtTop
            && a.MirrorMargins == b.MirrorMargins
            && a.ColumnCount == b.ColumnCount
            && a.ColumnSpacingPt.Equals(b.ColumnSpacingPt)
            && a.ColumnsLineBetween == b.ColumnsLineBetween
            && ColumnWidthsEqual(a.ColumnWidthsPt, b.ColumnWidthsPt);
    }

    private static bool ColumnWidthsEqual(IReadOnlyList<double>? a, IReadOnlyList<double>? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
            if (!a[i].Equals(b[i]))
                return false;
        return true;
    }

    private readonly record struct PageSegment(int Start, int End, PageSettings Page);

    internal static void ApplySectionBreakFlags(DocumentView editor, FlowDocument flow)
    {
        var modelParagraphs = editor.Model.Blocks.OfType<FreeW.Core.Model.Paragraph>().ToList();
        var renderedParagraphs = EnumerateRenderedBodyParagraphs(flow.Blocks).ToList();

        // Lists coalesce several model paragraphs into one top-level WPF List, while hidden or
        // unsupported blocks can remove/introduce rendered paragraphs. Apply paragraph boundaries
        // only when the sequence is complete; an uncertain mapping must not move a section break.
        if (modelParagraphs.Count == renderedParagraphs.Count)
        {
            for (int i = 0; i < modelParagraphs.Count - 1; i++)
            {
                if (IsPageTypeSectionBreak(modelParagraphs[i].SectionBreak))
                    renderedParagraphs[i + 1].BreakPageBefore = true;
            }
        }

        ApplyTableSectionBreakFlags(editor.Model, flow);
    }

    private static void ApplyTableSectionBreakFlags(TextDocument document, FlowDocument flow)
    {
        var renderedBlocks = flow.Blocks.ToList();
        var firstRenderedBlockByTableIndex = new Dictionary<int, int>();
        var renderedIndex = 0;

        for (var modelIndex = 0; modelIndex < document.Blocks.Count; modelIndex++)
        {
            if (document.Blocks[modelIndex] is FreeW.Core.Model.Paragraph
                {
                    Formatting.ListKind: not ListKind.None
                } firstListParagraph)
            {
                var kind = firstListParagraph.Formatting.ListKind;
                while (modelIndex + 1 < document.Blocks.Count
                       && document.Blocks[modelIndex + 1] is FreeW.Core.Model.Paragraph
                       {
                           Formatting.ListKind: var nextKind
                       }
                       && nextKind == kind)
                {
                    modelIndex++;
                }

                renderedIndex++;
                continue;
            }

            if (document.Blocks[modelIndex] is FreeW.Core.Model.Table table)
            {
                firstRenderedBlockByTableIndex[modelIndex] = renderedIndex;
                renderedIndex += RenderedTopLevelBlockCount(document, table, modelIndex);
                continue;
            }

            renderedIndex++;
        }

        // Outline-collapsed or unsupported blocks can change the rendered sequence. Refuse to apply
        // any table boundary unless the renderer-neutral count reproduces the cloned flow exactly.
        if (renderedIndex != renderedBlocks.Count)
            return;

        foreach (var (modelIndex, firstRenderedIndex) in firstRenderedBlockByTableIndex)
        {
            if (modelIndex == 0
                || document.Blocks[modelIndex - 1] is not FreeW.Core.Model.Paragraph previous
                || !IsPageTypeSectionBreak(previous.SectionBreak))
            {
                continue;
            }

            ApplyBreakBefore(flow, renderedBlocks[firstRenderedIndex]);
        }
    }

    private static void ApplyBreakBefore(FlowDocument flow, System.Windows.Documents.Block target)
    {
        if (target is not System.Windows.Documents.Table table)
        {
            target.BreakPageBefore = true;
            return;
        }

        // WPF stores Table.BreakPageBefore but its paginator does not honor it. A display-only
        // Section is the nearest effective block owner and leaves the nested editable table intact.
        var previous = table.PreviousBlock;
        flow.Blocks.Remove(table);
        var wrapper = new System.Windows.Documents.Section(table)
        {
            BreakPageBefore = true,
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };
        if (previous is not null)
        {
            flow.Blocks.InsertAfter(previous, wrapper);
        }
        else if (flow.Blocks.FirstBlock is { } first)
        {
            flow.Blocks.InsertBefore(first, wrapper);
        }
        else
        {
            flow.Blocks.Add(wrapper);
        }
    }

    private static int RenderedTopLevelBlockCount(
        TextDocument document,
        FreeW.Core.Model.Table table,
        int sourceBlockIndex)
    {
        var leadingContentHeightDip = DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(
            document,
            sourceBlockIndex);
        var plan = DocumentViewLayoutPlanner.BuildTableLayoutPlan(
            table,
            page: document.Page,
            firstPageLeadingContentHeightDip: leadingContentHeightDip);
        var hasVerticalMerges = table.Rows
            .SelectMany(row => row.Cells)
            .Any(cell => cell.VerticalMerge != VerticalMergeState.None);
        return plan.Pagination.Pages.Count > 1 && !hasVerticalMerges
            ? plan.Pagination.Pages.Count
            : 1;
    }

    private static bool IsPageTypeSectionBreak(FreeW.Core.Model.Section? section) =>
        section?.BreakKind is SectionBreakKind.NextPage
                            or SectionBreakKind.EvenPage
                            or SectionBreakKind.OddPage;

    private static IEnumerable<System.Windows.Documents.Paragraph> EnumerateRenderedBodyParagraphs(
        IEnumerable<System.Windows.Documents.Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is System.Windows.Documents.Paragraph paragraph)
            {
                yield return paragraph;
                continue;
            }

            if (block is not System.Windows.Documents.List list)
                continue;

            foreach (var item in list.ListItems)
                foreach (var nestedParagraph in EnumerateRenderedBodyParagraphs(item.Blocks))
                    yield return nestedParagraph;
        }
    }
}
