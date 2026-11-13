using System.Text;
using System.Windows;
using System.Windows.Documents;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

using ModelBlock = FreeW.Core.Model.Block;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelTable = FreeW.Core.Model.Table;
using WpfBlock = System.Windows.Documents.Block;
using WpfList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfSection = System.Windows.Documents.Section;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Incremental, layout-only page assignment for the paged editor: the same result as
/// <see cref="PaginationEngine.ComputeBlockPageAssignment(DocumentView, out int, List{PaginationEngine.SegmentPagination}?, bool)"/>
/// with <c>layoutOnly</c>, computed from what changed since the previous call.
/// <list type="bullet">
///   <item><b>Range render cache.</b> The tag-free layout render is kept per render unit (one model
///   block, or one run of list paragraphs that renders as one WPF List), keyed by the unit's model block
///   instances plus a signature of the document context its render reads (list numbering, preserved
///   numbering markers, note numbers, a table's leading-content estimate). Missing units are rendered
///   in contiguous ranges through <see cref="DocumentView.RenderBodyUnitRange"/>; contextual spacing and
///   section-break flags are applied when a unit is placed, so a cached unit never carries its old
///   neighbours' effects.</item>
///   <item><b>Resumable pagination.</b> Every page (a pass of the per-page footnote path, or a page of a
///   plain flow) records the state it started from. After an edit pagination restarts at the last page
///   that starts on a whole block before the page holding the first changed block, and stops as soon as
///   a page starts on an unchanged block with the same state as in the previous run (stabilisation);
///   the rest of the previous result is reused, shifted.</item>
///   <item><b>Windowed layout.</b> Only the blocks a page needs are laid out (the page plus two more),
///   so neither restart nor first pages wait for the whole document.</item>
/// </list>
/// The cache is valid only while the model changes by whole-block replacement (the paged editor's own
/// commits, and undo/redo of them); <see cref="Invalidate"/> must be called on any other model change.
/// Documents this cannot represent exactly (outline collapse, inline page/column-break fragments,
/// page-anchored wrap reservations, multi-column footnoted sections, page-type section breaks together
/// with tables, non-paragraph/table blocks) fall back to the full computation.
/// </summary>
internal sealed class PagedLayoutSession
{
    /// <summary>What the last <see cref="ComputeAssignment"/> did.</summary>
    internal sealed record LayoutStatistics(
        bool Incremental,
        string? FallbackReason,
        int UnitsRendered,
        int UnitsReused,
        int PagesLaidOut,
        int PagesReused);

    private readonly DocumentView _source;
    private DocumentView? _twin;
    private TextDocument? _boundModel;
    private readonly Dictionary<ModelBlock, CachedUnit> _cache = new(ReferenceEqualityComparer.Instance);
    private RunResult? _last;

    // Per-run working state.
    private UnitPlan[] _plans = [];
    private PlacedUnit?[] _placed = [];
    private int[] _unitOfRendered = [];
    private int _unitsRendered;
    private int _unitsReused;
    private int _pagesLaidOut;
    private int _pagesReused;

    internal PagedLayoutSession(DocumentView source)
    {
        _source = source;
    }

    internal LayoutStatistics LastStatistics { get; private set; } = new(false, "not run", 0, 0, 0, 0);

    /// <summary>Units rendered together when pagination reaches one that is not cached.</summary>
    internal static int RenderAheadUnits { get; set; } = 8;

    /// <summary>Forgets every cached render and the previous pagination (cancels a run in progress).</summary>
    internal void Invalidate()
    {
        CancelActiveRun();
        ClearCaches();
    }

    private void ClearCaches()
    {
        _cache.Clear();
        _last = null;
        _twin = null;
        _boundModel = null;
    }

    private LayoutRun? _activeRun;

    /// <summary>The run in progress, if any.</summary>
    internal LayoutRun? ActiveRun => _activeRun;

    /// <summary>Cancels the run in progress (its partial result is discarded; the previous run stays the base).</summary>
    internal void CancelActiveRun()
    {
        var run = _activeRun;
        _activeRun = null;
        run?.Cancel();
    }

    /// <summary>
    /// Each top-level model block's 0-based page and the physical body page count, identical to the full
    /// layout-only page assignment.
    /// </summary>
    internal int[] ComputeAssignment(out int totalPageCount)
    {
        var run = BeginRun();
        run.RunToEnd();
        totalPageCount = run.TotalPageCount;
        return run.Assignment;
    }

    /// <summary>
    /// Starts a stepwise pagination of the current model (cancelling any run in progress). Each
    /// <see cref="LayoutRun.Step"/> lays out about one page, so a caller can paginate only as far as it
    /// needs now and continue the rest later; pages before <see cref="LayoutRun.FinalPageCount"/> are
    /// final while the run is in progress. The model must not change while the run is in progress.
    /// </summary>
    internal LayoutRun BeginRun()
    {
        CancelActiveRun();
        var run = new LayoutRun(this);
        _activeRun = run;
        run.Attach(RunSteps(run));
        return run;
    }

    private void OnRunEnded(LayoutRun run)
    {
        if (ReferenceEquals(_activeRun, run))
            _activeRun = null;
        _placed = [];
    }

    private void FallBack(LayoutRun run, Exception ex)
    {
        if (ReferenceEquals(_activeRun, run))
            _activeRun = null;
        Invalidate();
        var reason = ex is UnsupportedException unsupported ? unsupported.Message : ex.GetType().Name + ": " + ex.Message;
        LastStatistics = new LayoutStatistics(false, reason, 0, 0, 0, 0);
        run.Assignment = PaginationEngine.ComputeBlockPageAssignment(_source, out var total, null, layoutOnly: true);
        run.TotalPageCount = total;
    }

    /// <summary>
    /// One pagination of the model, advanced a page at a time. Pages before <see cref="FinalPageCount"/>
    /// and the model blocks before <see cref="FrontierModelBlock"/> have their final page; once
    /// <see cref="IsComplete"/>, <see cref="Assignment"/> is the whole result (identical to
    /// <see cref="ComputeAssignment"/>).
    /// </summary>
    internal sealed class LayoutRun
    {
        private readonly PagedLayoutSession _session;
        private IEnumerator<bool>? _steps;
        private int[] _completedGlobalPage = [];

        internal LayoutRun(PagedLayoutSession session) => _session = session;

        internal void Attach(IEnumerable<bool> steps) => _steps = steps.GetEnumerator();

        internal bool IsComplete { get; private set; }

        internal bool IsCancelled { get; private set; }

        internal int[] Assignment { get; set; } = [];

        internal int TotalPageCount { get; set; } = 1;

        // Progress, written by the run.
        internal int[] RenderedStartOfModel = [];
        internal int RenderedCount;
        internal int FrontierRendered;
        internal int FrontierPage;
        internal SegmentResult? CurrentSegment;
        internal int CurrentSegmentOffset;

        /// <summary>The longest single step so far (diagnostics).</summary>
        internal double LongestStepMs { get; private set; }

        /// <summary>Pages whose set of starting blocks is final.</summary>
        internal int FinalPageCount => IsComplete ? TotalPageCount : FrontierPage;

        /// <summary>The first model block whose page is not final yet (the model's block count once complete).</summary>
        internal int FrontierModelBlock
        {
            get
            {
                if (IsComplete)
                    return Assignment.Length;
                var m = 0;
                while (m < RenderedStartOfModel.Length && RenderedStartOfModel[m] < FrontierRendered)
                    m++;
                return m;
            }
        }

        /// <summary>The final page of every model block before <see cref="FrontierModelBlock"/> (-1 after it).</summary>
        internal int[] PartialAssignment()
        {
            if (IsComplete)
                return Assignment;
            var result = new int[RenderedStartOfModel.Length];
            var frontier = FrontierModelBlock;
            for (var m = 0; m < result.Length; m++)
            {
                if (m >= frontier)
                {
                    result[m] = -1;
                    continue;
                }
                var r = RenderedStartOfModel[m];
                var segment = CurrentSegment;
                result[m] = segment is not null && r >= segment.RenderedStart
                    ? CurrentSegmentOffset + segment.LocalPage[r - segment.RenderedStart]
                    : r < _completedGlobalPage.Length ? _completedGlobalPage[r] : 0;
            }
            return result;
        }

        internal void OnSegmentCompleted(SegmentResult segment, int offset)
        {
            if (_completedGlobalPage.Length != RenderedCount)
                Array.Resize(ref _completedGlobalPage, RenderedCount);
            for (var r = segment.RenderedStart; r < segment.RenderedEnd; r++)
                _completedGlobalPage[r] = offset + segment.LocalPage[r - segment.RenderedStart];
            CurrentSegment = null;
        }

        /// <summary>Lays out about one more page. Returns true once the run is complete.</summary>
        internal bool Step()
        {
            if (IsComplete || IsCancelled)
                return IsComplete;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!_steps!.MoveNext())
                    Finish();
                LongestStepMs = Math.Max(LongestStepMs, clock.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                DisposeSteps();
                _session.FallBack(this, ex);
                Finish();
            }
            return IsComplete;
        }

        internal void RunToEnd()
        {
            while (!Step() && !IsCancelled)
            {
            }
        }

        /// <summary>Steps until <paramref name="done"/> holds, the run completes, or <paramref name="budget"/> elapses.</summary>
        internal void RunUntil(Func<LayoutRun, bool> done, TimeSpan? budget = null)
        {
            var clock = budget is null ? null : System.Diagnostics.Stopwatch.StartNew();
            while (!IsComplete && !IsCancelled && !done(this))
            {
                Step();
                if (clock is not null && clock.Elapsed >= budget!.Value)
                    break;
            }
        }

        private void Finish()
        {
            DisposeSteps();
            IsComplete = true;
            FrontierPage = TotalPageCount;
            _session.OnRunEnded(this);
        }

        internal void Cancel()
        {
            if (IsComplete || IsCancelled)
                return;
            IsCancelled = true;
            DisposeSteps();
            _session.OnRunEnded(this);
        }

        private void DisposeSteps()
        {
            var steps = _steps;
            _steps = null;
            try
            {
                steps?.Dispose();
            }
            catch
            {
                // Releasing the layout flows of an abandoned run must never fail the caller.
            }
        }
    }

    internal sealed class UnsupportedException(string message) : Exception(message);

    // ── model scan ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>One render unit of the current model and what its render depends on.</summary>
    internal sealed class UnitPlan
    {
        public int Start;
        public int End;
        public ModelBlock[] Blocks = [];
        public string? Signature;               // null: never cached (fields, comments, controls)
        public DocumentView.BodyRenderUnitKind Kind;
        public int PredictedRenderedCount;
        public int RenderedStart;
        public bool[] SectionFlags = [];        // per model paragraph of the unit: break page before
    }

    /// <summary>A cached render of one unit, exactly as the range render produced it.</summary>
    private sealed class CachedUnit
    {
        public ModelBlock[] Blocks = [];
        public string Signature = "";
        public List<WpfBlock> TopBlocks = [];
        public Thickness NaturalMargin;
        public List<(WpfBlock Block, bool BreakPageBefore)> NaturalBreaks = [];
        public List<WpfParagraph> RenderedParagraphs = [];
    }

    /// <summary>A unit prepared for this run's layout: its blocks with this run's context applied.</summary>
    private sealed class PlacedUnit
    {
        public List<WpfBlock> TopBlocks = [];
        public CachedUnit Source = null!;
    }

    private static string? UnitSignatureContext(
        TextDocument model,
        int start,
        int end,
        IReadOnlyList<DocumentListMarkerPlan>? listPlans,
        IReadOnlyDictionary<int, PreservedNumberingMarkerPlan> preserved,
        IReadOnlyDictionary<int, int> footnoteSequence,
        IReadOnlyDictionary<int, int> endnoteSequence,
        bool signTableIndex)
    {
        var builder = new StringBuilder();
        for (var index = start; index < end; index++)
        {
            var block = model.Blocks[index];
            switch (block)
            {
                case ModelParagraph paragraph:
                    if (!AppendParagraphContext(builder, paragraph, model, footnoteSequence, endnoteSequence))
                        return null;
                    break;
                case ModelTable table:
                    builder.Append("T:").Append(DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(model, index).ToString("R"));
                    if (signTableIndex)
                        builder.Append('@').Append(index);
                    foreach (var row in table.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var cellParagraph in cell.Paragraphs)
                                if (!AppendParagraphContext(builder, cellParagraph, model, footnoteSequence, endnoteSequence))
                                    return null;
                    break;
                default:
                    return null;
            }

            if (preserved.TryGetValue(index, out var marker))
                builder.Append("|P:").Append(marker);
            builder.Append(';');
        }

        if (listPlans is not null)
        {
            foreach (var plan in listPlans)
                builder.Append("|L:").Append(plan);
        }

        return builder.ToString();
    }

    private static bool AppendParagraphContext(
        StringBuilder builder,
        ModelParagraph paragraph,
        TextDocument model,
        IReadOnlyDictionary<int, int> footnoteSequence,
        IReadOnlyDictionary<int, int> endnoteSequence)
    {
        if (paragraph.SpanningFieldStart is not null || paragraph.SpanningFieldOwner is not null)
            return false;

        foreach (var run in paragraph.Runs)
        {
            // Field results, comment marks and content controls resolve against the whole document (or
            // carry block positions); such units are rendered afresh every time.
            if (run.FieldKind != RunFieldKind.None
                || run.ComplexField is not null
                || run.CrossReference is not null
                || run.TableFormula is not null
                || run.Citation is not null
                || run.CommentId is not null
                || run.IsCommentReference
                || run.Control is not null)
            {
                return false;
            }

            if (run.FootnoteId is { } footnoteId)
            {
                builder.Append("F").Append(footnoteSequence.TryGetValue(footnoteId, out var sequence) ? sequence : -1)
                    .Append('/').Append(model.FootnoteNumbering.GetHashCode());
            }
            if (run.EndnoteId is { } endnoteId)
            {
                builder.Append("E").Append(endnoteSequence.TryGetValue(endnoteId, out var sequence) ? sequence : -1)
                    .Append('/').Append(model.EndnoteNumbering.GetHashCode());
            }
        }

        return true;
    }

    internal static UnitPlan[] BuildUnitPlans(TextDocument model, bool signTableIndex = false)
    {
        var blocks = model.Blocks;
        var plans = new List<UnitPlan>();
        var sequence = new DocumentListMarkerSequencePlanner(
            model.MultiLevelList.NumberFormats, model.MultiLevelList.LevelTexts);
        var preserved = PreservedNumberingMarkerPlanner.Build(model);
        var footnoteSequence = model.Footnotes.Count > 0
            ? DocumentNoteRegionPlanner.ComputeSequenceById(model, isFootnote: true)
            : new Dictionary<int, int>();
        var endnoteSequence = model.Endnotes.Count > 0
            ? DocumentNoteRegionPlanner.ComputeSequenceById(model, isFootnote: false)
            : new Dictionary<int, int>();

        // Section-break flags: the paragraph (in paragraph order, tables skipped) after a page-type
        // section break starts a new page -- PaginationEngine.ApplySectionBreakFlags.
        var flagNextParagraph = false;
        var renderedIndex = 0;
        var i = 0;
        while (i < blocks.Count)
        {
            var plan = new UnitPlan { Start = i, RenderedStart = renderedIndex };
            List<DocumentListMarkerPlan>? listPlans = null;
            if (blocks[i] is ModelParagraph { Formatting.ListKind: not ListKind.None } first)
            {
                var kind = first.Formatting.ListKind;
                listPlans = [];
                while (i < blocks.Count
                    && blocks[i] is ModelParagraph { Formatting.ListKind: var k } listParagraph
                    && k == kind
                    && (listPlans.Count == 0
                        || kind != ListKind.Number
                        || !listParagraph.Formatting.ListStartOverride.HasValue))
                {
                    listPlans.Add(sequence.Advance(listParagraph));
                    i++;
                }

                plan.Kind = kind == ListKind.MultiLevel
                    && listPlans.Count == 1
                    && string.Equals(first.StyleId, "Heading1", StringComparison.OrdinalIgnoreCase)
                        ? DocumentView.BodyRenderUnitKind.Transparent
                        : DocumentView.BodyRenderUnitKind.Reset;
                plan.PredictedRenderedCount = 1;
            }
            else if (blocks[i] is ModelTable table)
            {
                TableCellListMarkerPlanner.AdvanceThroughTable(table, sequence);
                plan.Kind = DocumentView.BodyRenderUnitKind.Reset;
                plan.PredictedRenderedCount = PaginationEngine.RenderedTopLevelBlockCount(model, table, i);
                i++;
            }
            else if (blocks[i] is ModelParagraph)
            {
                plan.Kind = DocumentView.BodyRenderUnitKind.BodyParagraph;
                plan.PredictedRenderedCount = 1;
                i++;
            }
            else
            {
                throw new UnsupportedException("non-paragraph, non-table block");
            }

            plan.End = i;
            plan.Blocks = new ModelBlock[plan.End - plan.Start];
            for (var b = plan.Start; b < plan.End; b++)
                plan.Blocks[b - plan.Start] = blocks[b];

            var paragraphCount = plan.Blocks.Count(block => block is ModelParagraph);
            plan.SectionFlags = new bool[paragraphCount];
            var p = 0;
            foreach (var block in plan.Blocks)
            {
                if (block is not ModelParagraph paragraph)
                    continue;
                plan.SectionFlags[p++] = flagNextParagraph;
                flagNextParagraph = PaginationEngine.IsPageTypeSectionBreak(paragraph.SectionBreak);
            }

            plan.Signature = UnitSignatureContext(
                model, plan.Start, plan.End, listPlans, preserved, footnoteSequence, endnoteSequence, signTableIndex);
            renderedIndex += plan.PredictedRenderedCount;
            plans.Add(plan);
        }

        return plans.ToArray();
    }

    // ── unit materialisation ────────────────────────────────────────────────────────────────────────

    private bool IsCached(UnitPlan plan) =>
        plan.Signature is not null
        && _cache.TryGetValue(plan.Blocks[0], out var cached)
        && cached.Signature == plan.Signature
        && cached.Blocks.AsSpan().SequenceEqual(plan.Blocks);

    /// <summary>Renders (or takes from the cache) units <paramref name="from"/>..<paramref name="to"/> (exclusive).</summary>
    private void EnsurePlaced(int from, int to)
    {
        to = Math.Min(to, _plans.Length);
        var u = from;
        while (u < to)
        {
            if (_placed[u] is not null)
            {
                u++;
                continue;
            }

            if (IsCached(_plans[u]))
            {
                _placed[u] = Place(u, _cache[_plans[u].Blocks[0]]);
                _unitsReused++;
                u++;
                continue;
            }

            // A contiguous range of units that need rendering, rendered in one pass.
            var rangeEnd = u + 1;
            while (rangeEnd < to && _placed[rangeEnd] is null && !IsCached(_plans[rangeEnd]))
                rangeEnd++;
            RenderUnits(u, rangeEnd);
            for (var k = u; k < rangeEnd; k++)
                _placed[k] = Place(k, _cache[_plans[k].Blocks[0]]);
            u = rangeEnd;
        }
    }

    private void RenderUnits(int fromUnit, int toUnit)
    {
        var units = _twin!.RenderBodyUnitRange(_plans[fromUnit].Start, _plans[toUnit - 1].End);
        if (units.Count != toUnit - fromUnit)
            throw new UnsupportedException("range render produced a different unit split");

        for (var k = 0; k < units.Count; k++)
        {
            var plan = _plans[fromUnit + k];
            var unit = units[k];
            if (unit.Start != plan.Start || unit.End != plan.End || unit.Kind != plan.Kind)
                throw new UnsupportedException("range render produced a different unit split");
            if (unit.Blocks.Count != plan.PredictedRenderedCount)
                throw new UnsupportedException("rendered block count differs from the prediction");

            var cached = new CachedUnit
            {
                Blocks = plan.Blocks,
                Signature = plan.Signature ?? "",
                TopBlocks = unit.Blocks.ToList(),
                NaturalMargin = unit.Blocks[0].Margin
            };
            foreach (var block in cached.TopBlocks)
            {
                PaginationEngine.ClearTags(block);
                cached.NaturalBreaks.Add((block, block.BreakPageBefore));
                if (block is WpfList list)
                {
                    foreach (var paragraph in EnumerateListParagraphs(list))
                        cached.NaturalBreaks.Add((paragraph, paragraph.BreakPageBefore));
                }
            }
            foreach (var block in cached.TopBlocks)
            {
                if (block is WpfParagraph paragraph)
                    cached.RenderedParagraphs.Add(paragraph);
                else if (block is WpfList list)
                    cached.RenderedParagraphs.AddRange(EnumerateListParagraphs(list));
            }
            if (cached.RenderedParagraphs.Count != plan.SectionFlags.Length)
                throw new UnsupportedException("rendered paragraphs do not pair with model paragraphs");

            // Uncacheable units still go through the cache for this run; a later run renders them again
            // because their (null) signature never matches.
            _cache[plan.Blocks[0]] = cached;
            _unitsRendered++;
        }
    }

    private static IEnumerable<WpfParagraph> EnumerateListParagraphs(WpfList list)
    {
        foreach (var item in list.ListItems)
        {
            foreach (var block in item.Blocks)
            {
                if (block is WpfParagraph paragraph)
                    yield return paragraph;
                else if (block is WpfList nested)
                    foreach (var nestedParagraph in EnumerateListParagraphs(nested))
                        yield return nestedParagraph;
            }
        }
    }

    /// <summary>
    /// Readies a cached unit for this run: detached from wherever an earlier layout left it, its own
    /// page-break flags restored, then this run's contextual spacing and section-break flags applied.
    /// </summary>
    private PlacedUnit Place(int unitIndex, CachedUnit cached)
    {
        foreach (var block in cached.TopBlocks)
            Detach(block);
        foreach (var (block, breakBefore) in cached.NaturalBreaks)
            block.BreakPageBefore = breakBefore;

        var plan = _plans[unitIndex];
        if (plan.Kind == DocumentView.BodyRenderUnitKind.BodyParagraph)
        {
            var paragraph = (WpfParagraph)cached.TopBlocks[0];
            var margin = cached.NaturalMargin;
            var model = (ModelParagraph)plan.Blocks[0];
            var top = margin.Top;
            var bottom = margin.Bottom;
            if (PreviousBodyParagraph(unitIndex) is { } previous
                && DocumentView.SuppressesContextualSpacingForLayout(previous, model, _boundModel!))
                top = 0;
            if (NextBodyParagraph(unitIndex) is { } next
                && DocumentView.SuppressesContextualSpacingForLayout(model, next, _boundModel!))
                bottom = 0;
            paragraph.Margin = new Thickness(margin.Left, top, margin.Right, bottom);
        }

        for (var p = 0; p < plan.SectionFlags.Length; p++)
        {
            if (plan.SectionFlags[p])
                cached.RenderedParagraphs[p].BreakPageBefore = true;
        }

        return new PlacedUnit { TopBlocks = cached.TopBlocks, Source = cached };
    }

    private ModelParagraph? PreviousBodyParagraph(int unitIndex)
    {
        for (var u = unitIndex - 1; u >= 0; u--)
        {
            switch (_plans[u].Kind)
            {
                case DocumentView.BodyRenderUnitKind.Transparent:
                    continue;
                case DocumentView.BodyRenderUnitKind.BodyParagraph:
                    return (ModelParagraph)_plans[u].Blocks[0];
                default:
                    return null;
            }
        }
        return null;
    }

    private ModelParagraph? NextBodyParagraph(int unitIndex)
    {
        for (var u = unitIndex + 1; u < _plans.Length; u++)
        {
            switch (_plans[u].Kind)
            {
                case DocumentView.BodyRenderUnitKind.Transparent:
                    continue;
                case DocumentView.BodyRenderUnitKind.BodyParagraph:
                    return (ModelParagraph)_plans[u].Blocks[0];
                default:
                    return null;
            }
        }
        return null;
    }

    private static void Detach(WpfBlock block)
    {
        switch (block.Parent)
        {
            case FlowDocument flow:
                flow.Blocks.Remove(block);
                break;
            case WpfSection section:
                section.Blocks.Remove(block);
                break;
            case ListItem item:
                item.Blocks.Remove(block);
                break;
            case System.Windows.Documents.TableCell cell:
                cell.Blocks.Remove(block);
                break;
        }
    }

    /// <summary>The top-level rendered block <paramref name="renderedIndex"/>, rendering its unit on demand.</summary>
    private WpfBlock RenderedBlock(int renderedIndex)
    {
        var unit = _unitOfRendered[renderedIndex];
        if (_placed[unit] is null)
        {
            // Render ahead: neighbouring missing units in one range keeps the render passes few, and a
            // short range keeps one pagination step (the unit of background work) short.
            EnsurePlaced(unit, Math.Min(_plans.Length, unit + RenderAheadUnits));
        }
        var placed = _placed[unit]!;
        return placed.TopBlocks[renderedIndex - _plans[unit].RenderedStart];
    }

    /// <summary>The model block whose footnotes a rendered block carries (PaginationEngine's renderedOwners).</summary>
    private ModelBlock OwnerOf(int renderedIndex)
    {
        var plan = _plans[_unitOfRendered[renderedIndex]];
        // A list run maps every paragraph to the one List; only its last paragraph owns the rendered
        // block in the full computation's owner table.
        return plan.Blocks[^1];
    }

    /// <summary>Evicts the unit of a rendered paragraph the pagination split (its content moved).</summary>
    private void EvictRendered(int renderedIndex)
    {
        var plan = _plans[_unitOfRendered[renderedIndex]];
        _cache.Remove(plan.Blocks[0]);
    }

    // ── run state ───────────────────────────────────────────────────────────────────────────────────

    internal sealed record CarryLink(int[] FootnoteIds, double BandHeightDip, int PageNumber);

    /// <summary>The state a pass (footnote path) or page (plain path) starts from.</summary>
    internal sealed record PassRecord(
        int StartRendered,          // first rendered block of the pass (fresh start) or of its remainder
        bool StartsWithRemainder,   // the pass begins with the rest of a paragraph split on the page before
        double CarriedNoteDip,
        CarryLink[] CarryChain,
        int LocalPageStart);

    internal enum SegmentMode
    {
        Empty,
        ZeroHeight,
        Plain,
        Footnote
    }

    internal sealed class SegmentResult
    {
        public int RenderedStart;
        public int RenderedEnd;
        public PageSettings Page = null!;
        public SegmentMode Mode;
        public int PageCount;
        public int[] LocalPage = [];          // per rendered block of the segment
        public List<PassRecord> Passes = [];
        public bool UsesFootnotePath;
        public int DecisionFrontier;         // rendered index (exclusive) the footnote-path decision read
    }

    private sealed class RunResult
    {
        public TextDocument Model = null!;
        public UnitPlan[] Plans = [];
        public int RenderedCount;
        public List<SegmentResult> Segments = [];
    }

    // ── the run ─────────────────────────────────────────────────────────────────────────────────────

    private IEnumerable<bool> RunSteps(LayoutRun runState)
    {
        runState.TotalPageCount = 1;
        _unitsRendered = _unitsReused = _pagesLaidOut = _pagesReused = 0;
        var model = _source.Model;
        if (model.Blocks.Count == 0)
        {
            LastStatistics = new LayoutStatistics(true, null, 0, 0, 0, 0);
            runState.Assignment = [];
            yield break;
        }

        if (PaginationEngine.UseReferencePagination)
            throw new UnsupportedException("reference pagination requested");
        if (_source.HasHiddenOutlineBlocks)
            throw new UnsupportedException("outline levels collapsed");
        if (DocumentView.HasRendererInlineFlowBreakFragments(model))
            throw new UnsupportedException("inline page/column-break fragments");
        if (_source.HasLeadingWrapReservations())
            throw new UnsupportedException("page-anchored wrap reservations");
        if (model.Blocks.Any(block => block is ModelTable)
            && model.Blocks.OfType<ModelParagraph>().Any(paragraph => PaginationEngine.IsPageTypeSectionBreak(paragraph.SectionBreak)))
            throw new UnsupportedException("page-type section breaks with tables");

        if (!ReferenceEquals(_boundModel, model) || _twin is null)
        {
            ClearCaches();
            _twin = _source.CreateLayoutRenderTwin(renderBody: false)
                ?? throw new UnsupportedException("no layout twin");
            _boundModel = model;
        }

        _plans = BuildUnitPlans(model);
        _placed = new PlacedUnit?[_plans.Length];
        var renderedCount = _plans.Length == 0 ? 0 : _plans[^1].RenderedStart + _plans[^1].PredictedRenderedCount;
        _unitOfRendered = new int[renderedCount];
        var renderedStartOfModel = new int[model.Blocks.Count];
        for (var u = 0; u < _plans.Length; u++)
        {
            for (var r = 0; r < _plans[u].PredictedRenderedCount; r++)
                _unitOfRendered[_plans[u].RenderedStart + r] = u;
            for (var m = _plans[u].Start; m < _plans[u].End; m++)
                renderedStartOfModel[m] = _plans[u].RenderedStart;
        }

        // Units whose render reads document state the scan cannot sign (field results, comment marks,
        // content controls) are rendered every run and signed by the text they resolved to.
        for (var u = 0; u < _plans.Length; u++)
        {
            if (_plans[u].Signature is not null)
                continue;
            EnsurePlaced(u, u + 1);
            var text = new StringBuilder("V:");
            foreach (var block in _placed[u]!.TopBlocks)
                text.Append(new TextRange(block.ContentStart, block.ContentEnd).Text).Append('\u0001');
            _plans[u].Signature = text.ToString();
            _cache[_plans[u].Blocks[0]].Signature = _plans[u].Signature!;
        }

        // The full computation pairs model and rendered blocks through its own prediction and, when that
        // misses (a Number list restarted by an override renders as a second List), pairs them by index.
        // Only the predicted case is reproduced here.
        if (PaginationEngine.MapModelBlocksToRenderedBlocks(model, renderedCount) is not { } predictedMap
            || !predictedMap.AsSpan().SequenceEqual(renderedStartOfModel))
        {
            throw new UnsupportedException("model-to-rendered block map not predictable");
        }

        int RenderedBoundary(int modelIndex) =>
            modelIndex < renderedStartOfModel.Length ? renderedStartOfModel[modelIndex] : renderedCount;

        var segments = PaginationEngine.BuildPageSegments(model.Blocks, model.Page)
            .Select(segment => segment with { Start = RenderedBoundary(segment.Start), End = RenderedBoundary(segment.End) })
            .ToList();

        // What changed since the previous run, in units: common prefix and suffix of identical units.
        var last = _last is { } previous && ReferenceEquals(previous.Model, model) ? previous : null;
        var prefixUnits = 0;
        var suffixUnits = 0;
        if (last is not null)
        {
            bool SameUnit(UnitPlan a, UnitPlan b) =>
                a.Signature is not null
                && a.Signature == b.Signature
                && a.PredictedRenderedCount == b.PredictedRenderedCount
                && a.Blocks.AsSpan().SequenceEqual(b.Blocks)
                && a.SectionFlags.AsSpan().SequenceEqual(b.SectionFlags);

            var maxCommon = Math.Min(last.Plans.Length, _plans.Length);
            while (prefixUnits < maxCommon && SameUnit(last.Plans[prefixUnits], _plans[prefixUnits]))
                prefixUnits++;
            while (suffixUnits < maxCommon - prefixUnits
                && SameUnit(last.Plans[^(suffixUnits + 1)], _plans[^(suffixUnits + 1)]))
                suffixUnits++;
        }

        var changedStartRendered = prefixUnits < _plans.Length ? _plans[prefixUnits].RenderedStart : renderedCount;
        var changedEndRendered = suffixUnits > 0 ? _plans[^suffixUnits].RenderedStart : renderedCount;
        var renderedDelta = last is null ? 0 : renderedCount - last.RenderedCount;
        var nothingChanged = last is not null
            && prefixUnits == _plans.Length
            && last.Plans.Length == _plans.Length;

        var run = new RunResult { Model = model, Plans = _plans, RenderedCount = renderedCount };
        var assignment = new int[model.Blocks.Count];
        var pageOffset = 0;
        var reusableSegments = last is not null && last.Segments.Count == segments.Count;
        runState.RenderedStartOfModel = renderedStartOfModel;
        runState.RenderedCount = renderedCount;
        for (var s = 0; s < segments.Count; s++)
        {
            var segment = segments[s];
            runState.FrontierRendered = Math.Min(segment.Start, renderedCount);
            runState.FrontierPage = pageOffset;
            var previousSegment = reusableSegments ? last!.Segments[s] : null;
            if (previousSegment is not null && !ReferenceEquals(previousSegment.Page, segment.Page))
                previousSegment = null;

            SegmentResult result;
            if (previousSegment is not null && nothingChanged)
            {
                result = previousSegment;
                _pagesReused += result.PageCount;
            }
            else if (previousSegment is not null
                && segment.End <= changedStartRendered
                && previousSegment.RenderedStart == segment.Start
                && previousSegment.RenderedEnd == segment.End)
            {
                result = previousSegment; // wholly before the change
                _pagesReused += result.PageCount;
            }
            else if (previousSegment is not null
                && segment.Start >= changedEndRendered
                && previousSegment.RenderedStart + renderedDelta == segment.Start
                && previousSegment.RenderedEnd + renderedDelta == segment.End)
            {
                result = Shift(previousSegment, renderedDelta, 0); // wholly after the change
                _pagesReused += result.PageCount;
            }
            else
            {
                var restartFrom = previousSegment is not null && previousSegment.RenderedStart == segment.Start
                    ? previousSegment
                    : null;
                result = new SegmentResult();
                runState.CurrentSegment = result;
                runState.CurrentSegmentOffset = pageOffset;
                foreach (var step in PaginateSegment(
                    model, segment, restartFrom, changedStartRendered, changedEndRendered, renderedDelta, result, runState, pageOffset))
                {
                    yield return step;
                }
            }

            run.Segments.Add(result);
            runState.OnSegmentCompleted(result, pageOffset);
            pageOffset += result.PageCount;
        }

        var totalPageCount = Math.Max(1, pageOffset);

        // Map rendered pages back to model blocks (PaginationEngine: assignment[m] = page of renderedStart[m]).
        var segmentAssignment = new int[renderedCount];
        var offset = 0;
        foreach (var result in run.Segments)
        {
            for (var r = result.RenderedStart; r < result.RenderedEnd; r++)
                segmentAssignment[r] = offset + result.LocalPage[r - result.RenderedStart];
            offset += result.PageCount;
        }
        for (var m = 0; m < model.Blocks.Count; m++)
        {
            assignment[m] = renderedStartOfModel[m] < segmentAssignment.Length
                ? segmentAssignment[renderedStartOfModel[m]]
                : Math.Max(0, totalPageCount - 1);
        }

        _last = run;
        LastStatistics = new LayoutStatistics(
            last is not null, null, _unitsRendered, _unitsReused, _pagesLaidOut, _pagesReused);
        runState.Assignment = assignment;
        runState.TotalPageCount = totalPageCount;
    }

    private static SegmentResult Shift(SegmentResult source, int renderedDelta, int pageDelta) => new()
    {
        RenderedStart = source.RenderedStart + renderedDelta,
        RenderedEnd = source.RenderedEnd + renderedDelta,
        Page = source.Page,
        Mode = source.Mode,
        PageCount = source.PageCount + pageDelta,
        LocalPage = source.LocalPage.Select(page => page + pageDelta).ToArray(),
        Passes = source.Passes.Select(pass => pass with
        {
            StartRendered = pass.StartRendered + renderedDelta,
            LocalPageStart = pass.LocalPageStart + pageDelta
        }).ToList(),
        UsesFootnotePath = source.UsesFootnotePath,
        DecisionFrontier = source.DecisionFrontier + renderedDelta
    };

    // ── one segment ─────────────────────────────────────────────────────────────────────────────────

    private IEnumerable<bool> PaginateSegment(
        TextDocument document,
        PaginationEngine.PageSegment segment,
        SegmentResult? previous,
        int changedStart,
        int changedEnd,
        int renderedDelta,
        SegmentResult result,
        LayoutRun run,
        int pageOffset)
    {
        result.RenderedStart = segment.Start;
        result.RenderedEnd = Math.Max(segment.Start, segment.End);
        result.Page = segment.Page;
        var end = Math.Min(segment.End, _unitOfRendered.Length);
        if (segment.Start >= end)
        {
            result.Mode = SegmentMode.Empty;
            result.RenderedEnd = segment.Start;
            result.PageCount = 0;
            yield break;
        }

        result.RenderedEnd = end;
        result.LocalPage = new int[end - segment.Start];
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(segment.Page);
        var (left, top, right, bottom) = PageLayout.MarginsDip(segment.Page);
        var (_, contentHeight) = PageLayout.ContentAreaDip(segment.Page);
        if (contentHeight <= 0)
        {
            result.Mode = SegmentMode.ZeroHeight;
            result.PageCount = 1;
            yield break;
        }

        var (runningHeader, runningFooter) = PaginationEngine.ResolveRunningHeaderFooter(document, segment.Page);
        var (topInsetDip, bottomInsetDip) = PrintLayout.ResolveHeaderFooterBodyInsetsDip(
            document, segment.Page, runningHeader, runningFooter);
        top += topInsetDip;
        bottom += bottomInsetDip;
        var pageSize = new Size(pageWidth, pageHeight);
        var padding = new Thickness(left, top, right, bottom);
        var fontFamily = new System.Windows.Media.FontFamily(document.DefaultRun.FontFamily ?? "Calibri");
        var fontSize = (document.DefaultRun.FontSizePt ?? 11) * PageLayout.PointsToDip(1);

        FlowDocument NewFlow(double reserveDip)
        {
            var flow = new FlowDocument
            {
                PageWidth = pageSize.Width,
                PageHeight = pageSize.Height,
                PagePadding = new Thickness(padding.Left, padding.Top, padding.Right, padding.Bottom + reserveDip),
                FontFamily = fontFamily,
                FontSize = fontSize
            };
            DocumentView.ApplyColumnLayout(flow, segment.Page, useNativeColumnRule: false);
            return flow;
        }

        // Which path: the per-page footnote path when any page of this single-column segment needs a
        // footnote reserve (PaginationEngine.HasPositivePerPageFootnoteReserve), else one plain flow.
        var usesFootnotePath = false;
        var decisionFrontier = end;
        if (document.Footnotes.Count > 0)
        {
            var anyReference = false;
            for (var r = segment.Start; r < end && !anyReference; r++)
                anyReference = PaginationEngine.FootnoteIdsInModelBlock(OwnerOf(r)).Any();

            if (segment.Page.ColumnCount > 1)
            {
                if (anyReference)
                    throw new UnsupportedException("multi-column footnoted section");
            }
            else if (anyReference)
            {
                if (previous is not null && previous.DecisionFrontier <= changedStart)
                {
                    usesFootnotePath = previous.UsesFootnotePath;
                    decisionFrontier = previous.DecisionFrontier;
                }
                else
                {
                    var (contentWidthDip, _) = PageLayout.ContentAreaDip(segment.Page);
                    usesFootnotePath = DecideFootnotePath(
                        document, segment, end, NewFlow(0), contentWidthDip, out decisionFrontier);
                }
            }
        }

        result.UsesFootnotePath = usesFootnotePath;
        result.DecisionFrontier = decisionFrontier;
        if (previous is not null && previous.UsesFootnotePath != usesFootnotePath)
            previous = null;

        var steps = usesFootnotePath
            ? PaginateFootnotePath(document, segment, end, result, previous, changedStart, changedEnd, renderedDelta, NewFlow, run, pageOffset)
            : PaginatePlain(segment, end, result, previous, changedStart, changedEnd, renderedDelta, () => NewFlow(0), run, pageOffset);
        result.Mode = usesFootnotePath ? SegmentMode.Footnote : SegmentMode.Plain;
        foreach (var step in steps)
            yield return step;
    }

    /// <summary>
    /// PaginationEngine.HasPositivePerPageFootnoteReserve over this segment, laying out only as far as
    /// the first page whose notes need a reserve. <paramref name="frontier"/> is the first rendered block
    /// the decision did not depend on.
    /// </summary>
    private bool DecideFootnotePath(
        TextDocument document,
        PaginationEngine.PageSegment segment,
        int end,
        FlowDocument flow,
        double contentWidthDip,
        out int frontier)
    {
        frontier = end;
        const double footnoteFrameClearanceDip = 24.0;
        bool PageNeedsReserve(IReadOnlyList<int> ids)
        {
            if (ids.Count == 0)
                return false;
            var plan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, ids, pageNumber: 1, contentWidthDip);
            return plan.EstimatedHeightDip > 0
                && PaginationEngine.ClampFootnoteReserveDip(plan.EstimatedHeightDip + footnoteFrameClearanceDip, segment.Page) > 0;
        }

        var window = new WindowedFlow(flow, segment.Start, end, RenderedBlock);
        try
        {
            var currentPage = 0;
            var pageIds = new List<int>();
            for (var r = segment.Start; r < end; r++)
            {
                var pageNumber = window.PageOfStart(r);
                if (pageNumber >= 0 && pageNumber != currentPage)
                {
                    if (PageNeedsReserve(pageIds))
                    {
                        // Everything the answer read lies before the window's end.
                        frontier = window.End;
                        return true;
                    }
                    pageIds.Clear();
                    currentPage = pageNumber;
                }

                foreach (var id in PaginationEngine.FootnoteIdsInModelBlock(OwnerOf(r)))
                {
                    if (!pageIds.Contains(id))
                        pageIds.Add(id);
                }
            }

            frontier = end;
            return PageNeedsReserve(pageIds);
        }
        finally
        {
            window.Release();
        }
    }

    /// <summary>
    /// A flow holding rendered blocks <c>start</c>..<c>End</c> of a longer sequence, grown on demand so a
    /// block's page is only read once the flow holds at least one whole page past it: page breaks never
    /// depend on content more than a page ahead.
    /// </summary>
    private sealed class WindowedFlow
    {
        private readonly FlowDocument _flow;
        private readonly int _limit;
        private readonly Func<int, WpfBlock> _blockAt;
        private readonly DynamicDocumentPaginator _paginator;
        private readonly List<WpfBlock> _blocks = [];
        private readonly int _start;

        public WindowedFlow(FlowDocument flow, int start, int limit, Func<int, WpfBlock> blockAt)
        {
            _flow = flow;
            _start = start;
            _limit = limit;
            _blockAt = blockAt;
            var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
            paginator.PageSize = new Size(flow.PageWidth, flow.PageHeight);
            _paginator = (DynamicDocumentPaginator)paginator;
            End = start;
        }

        public int End { get; private set; }

        public FlowDocument Flow => _flow;

        public DynamicDocumentPaginator Paginator => _paginator;

        public WpfBlock this[int renderedIndex] => _blocks[renderedIndex - _start];

        private void Grow(int minimumEnd)
        {
            var target = Math.Min(_limit, Math.Max(minimumEnd, End + Math.Max(24, End - _start)));
            while (End < target)
            {
                var block = _blockAt(End);
                _blocks.Add(block);
                _flow.Blocks.Add(block);
                End++;
            }
        }

        public int PageOf(TextPointer position) => _paginator.GetPageNumber(position);

        /// <summary>The page block <paramref name="renderedIndex"/> starts on, final for the whole sequence.</summary>
        public int PageOfStart(int renderedIndex)
        {
            if (End <= renderedIndex)
                Grow(renderedIndex + 1);
            while (true)
            {
                var page = _paginator.GetPageNumber(this[renderedIndex].ContentStart);
                if (End >= _limit)
                    return page;
                var lastPage = _paginator.GetPageNumber(_blocks[^1].ContentStart);
                if (lastPage >= page + 2)
                    return page;
                Grow(End + 1);
            }
        }

        /// <summary>Makes sure every page up to <paramref name="page"/> is final.</summary>
        public void EnsurePagesFinal(int page)
        {
            while (End < _limit && _paginator.GetPageNumber(_blocks[^1].ContentStart) < page + 2)
                Grow(End + 1);
        }

        public void Release() => _flow.Blocks.Clear();
    }

    // ── plain path ──────────────────────────────────────────────────────────────────────────────────

    private IEnumerable<bool> PaginatePlain(
        PaginationEngine.PageSegment segment,
        int end,
        SegmentResult result,
        SegmentResult? previous,
        int changedStart,
        int changedEnd,
        int renderedDelta,
        Func<FlowDocument> newFlow,
        LayoutRun run,
        int pageOffset)
    {
        // Restart point: the last page of the previous run that started cleanly on a whole block, at
        // least one page before the page holding the first changed block.
        var restart = previous is null ? null : FindPlainRestart(previous, changedStart);
        var startRendered = restart?.StartRendered ?? segment.Start;
        var pageBase = restart?.LocalPageStart ?? 0;
        if (restart is not null)
        {
            for (var r = segment.Start; r < startRendered; r++)
                result.LocalPage[r - segment.Start] = previous!.LocalPage[r - segment.Start];
            result.Passes.AddRange(previous!.Passes.TakeWhile(pass => pass.StartRendered < startRendered));
            _pagesReused += pageBase;
        }

        // A fresh flow already starts a page: an explicit break on its first block would add a blank one.
        var restoreBreak = (WpfBlock?)null;
        if (restart is not null && RenderedBlock(startRendered) is { BreakPageBefore: true } firstBlock)
        {
            firstBlock.BreakPageBefore = false;
            restoreBreak = firstBlock;
        }

        var flow = newFlow();
        var window = new WindowedFlow(flow, startRendered, end, RenderedBlock);
        var oldCleanStarts = previous?.Passes
            .Where(pass => !pass.StartsWithRemainder)
            .ToDictionary(pass => pass.StartRendered, pass => pass);
        try
        {
            var currentLocal = -1;
            for (var r = startRendered; r < end; r++)
            {
                var page = window.PageOfStart(r);
                if (page < 0)
                    page = Math.Max(0, currentLocal);
                if (page != currentLocal)
                {
                    // A new page starts at r. Clean when the block before it ended on an earlier page.
                    var clean = r == startRendered
                        || window.PageOf(window[r - 1].ContentEnd) < page;
                    var record = new PassRecord(r, !clean, 0, [], pageBase + page);
                    if (clean && r >= changedEnd && previous is not null && r > startRendered
                        && oldCleanStarts!.TryGetValue(r - renderedDelta, out var old))
                    {
                        // Stabilised: from here on the previous run's pages repeat, shifted.
                        var pageDelta = record.LocalPageStart - old.LocalPageStart;
                        for (var k = r; k < end; k++)
                            result.LocalPage[k - segment.Start] = previous.LocalPage[k - renderedDelta - previous.RenderedStart] + pageDelta;
                        result.Passes.AddRange(previous.Passes
                            .Skip(previous.Passes.IndexOf(old))
                            .Select(pass => pass with
                            {
                                StartRendered = pass.StartRendered + renderedDelta,
                                LocalPageStart = pass.LocalPageStart + pageDelta
                            }));
                        result.PageCount = previous.PageCount + pageDelta;
                        _pagesReused += previous.PageCount - old.LocalPageStart;
                        yield break;
                    }
                    result.Passes.Add(record);
                    currentLocal = page;
                    _pagesLaidOut++;

                    // Every block before r has its final page, and no later block starts before this page.
                    run.FrontierRendered = r;
                    run.FrontierPage = pageOffset + pageBase + page;
                    yield return false;
                }
                result.LocalPage[r - segment.Start] = pageBase + page;
            }

            // The last block may run on past the page it starts on.
            window.EnsurePagesFinal(int.MaxValue / 4);
            window.Paginator.ComputePageCount();
            result.PageCount = pageBase + Math.Max(1, window.Paginator.PageCount);
        }
        finally
        {
            window.Release();
            if (restoreBreak is not null)
                restoreBreak.BreakPageBefore = true;
        }
    }

    private static PassRecord? FindPlainRestart(SegmentResult previous, int changedStart)
    {
        // The page holding the first changed block, then one page back (a keep-with-next chain may pull
        // that page's first lines back), then back to a page that starts on a whole block.
        var holding = -1;
        for (var k = 0; k < previous.Passes.Count; k++)
        {
            if (previous.Passes[k].StartRendered <= changedStart)
                holding = k;
            else
                break;
        }
        for (var k = holding - 1; k >= 1; k--)
        {
            if (!previous.Passes[k].StartsWithRemainder)
                return previous.Passes[k];
        }
        return null;
    }

    // ── per-page footnote path ──────────────────────────────────────────────────────────────────────

    private IEnumerable<bool> PaginateFootnotePath(
        TextDocument document,
        PaginationEngine.PageSegment segment,
        int end,
        SegmentResult result,
        SegmentResult? previous,
        int changedStart,
        int changedEnd,
        int renderedDelta,
        Func<double, FlowDocument> newFlow,
        LayoutRun run,
        int pageOffset)
    {
        const double footnoteFrameClearanceDip = 0.0;
        const double minimumSplitBandDip = 36.0;
        var (contentWidthDip, _) = PageLayout.ContentAreaDip(segment.Page);
        var work = newFlow(0);
        var padding = work.PagePadding;
        var pageSize = new Size(work.PageWidth, work.PageHeight);
        var bodyHeightDip = Math.Max(0, pageSize.Height - padding.Top - padding.Bottom);
        var workPaginator = ((IDocumentPaginatorSource)work).DocumentPaginator;
        workPaginator.PageSize = pageSize;
        var dynamicWork = (DynamicDocumentPaginator)workPaginator;

        var pending = new List<(WpfBlock Block, int Rendered, bool IsContinuation)>();
        var nextRendered = segment.Start;
        var carriedNoteDip = 0.0;
        var carryChain = new List<(IReadOnlyList<int> FootnoteIds, double BandHeightDip, int PageNumber)>();
        var localPage = 0;

        // Restart from the previous run where it is safe: the pass holding the first changed block, one
        // pass back, then back to a pass that starts on a whole block.
        if (previous is not null)
        {
            var holding = -1;
            for (var k = 0; k < previous.Passes.Count; k++)
            {
                if (previous.Passes[k].StartRendered <= changedStart)
                    holding = k;
                else
                    break;
            }
            PassRecord? restart = null;
            var restartIndex = -1;
            for (var k = holding - 1; k >= 1; k--)
            {
                if (!previous.Passes[k].StartsWithRemainder)
                {
                    restart = previous.Passes[k];
                    restartIndex = k;
                    break;
                }
            }

            if (restart is not null)
            {
                for (var r = segment.Start; r < restart.StartRendered; r++)
                    result.LocalPage[r - segment.Start] = previous.LocalPage[r - segment.Start];
                result.Passes.AddRange(previous.Passes.Take(restartIndex));
                nextRendered = restart.StartRendered;
                carriedNoteDip = restart.CarriedNoteDip;
                carryChain.AddRange(restart.CarryChain.Select(link =>
                    ((IReadOnlyList<int>)link.FootnoteIds, link.BandHeightDip, link.PageNumber)));
                localPage = restart.LocalPageStart;
                _pagesReused += localPage;
            }
        }

        var oldFreshStarts = previous?.Passes
            .Where(pass => !pass.StartsWithRemainder)
            .ToDictionary(pass => pass.StartRendered, pass => pass);

        void TopUp(int count)
        {
            while (pending.Count < count && nextRendered < end)
            {
                var block = RenderedBlock(nextRendered);
                pending.Add((block, nextRendered, false));
                work.Blocks.Add(block);
                nextRendered++;
            }
        }

        // Pages are final for page 0 once the work flow holds a block that starts two pages on.
        void EnsureSafe()
        {
            while (nextRendered < end
                && (pending.Count == 0 || dynamicWork.GetPageNumber(pending[^1].Block.ContentStart) < 2))
            {
                TopUp(pending.Count + Math.Max(16, pending.Count));
            }
        }

        int CountBlocksStartingOnFirstPage()
        {
            var count = 0;
            foreach (var entry in pending)
            {
                if (dynamicWork.GetPageNumber(entry.Block.ContentStart) > 0)
                    break;
                count++;
            }
            return count;
        }

        List<int> NoteIdsOnFirstPage(int blockCount)
        {
            var ids = new List<int>();
            foreach (var entry in pending.Take(blockCount))
            {
                // Layout renders carry no tags, so (as in the full computation) a block's notes come from
                // its model owner, and a split-off remainder carries none of its own.
                if (!entry.IsContinuation)
                    ids.AddRange(PaginationEngine.FootnoteIdsInModelBlock(OwnerOf(entry.Rendered)));
            }
            return ids.Distinct().ToList();
        }

        double NoteDemandFor(int blockCount)
        {
            var ids = NoteIdsOnFirstPage(blockCount);
            if (ids.Count == 0)
                return carriedNoteDip;
            var own = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, ids, pageNumber: 1, contentWidthDip);
            return carriedNoteDip <= 0
                ? own.EstimatedHeightDip
                : own.EstimatedHeightDip + carriedNoteDip - own.SeparatorHeightDip;
        }

        void UpdateCarry(IReadOnlyList<int> pageIds, double reserveDip, int take)
        {
            if (carriedNoteDip <= 0 && NoteDemandFor(take) <= reserveDip + 0.5)
            {
                carriedNoteDip = 0;
                carryChain.Clear();
                return;
            }

            carryChain.Add((pageIds, reserveDip, carryChain.Count + 1));
            var probe = carryChain.Append((Array.Empty<int>(), 1_000_000.0, carryChain.Count + 1)).ToList();
            var bands = DocumentNoteRegionPlanner.BuildSequentialFootnoteBands(document, probe, contentWidthDip);
            carriedNoteDip = bands.Count > 0 && bands[^1].HasContent ? bands[^1].EstimatedHeightDip : 0;
            if (carriedNoteDip <= 0)
                carryChain.Clear();
        }

        double ReserveFor(int blockCount)
        {
            var demand = NoteDemandFor(blockCount);
            return demand <= 0
                ? 0
                : PaginationEngine.ClampFootnoteReserveDip(Math.Min(demand, bodyHeightDip) + footnoteFrameClearanceDip, segment.Page);
        }

        void SetReserve(double reserveDip) =>
            work.PagePadding = new Thickness(padding.Left, padding.Top, padding.Right, padding.Bottom + reserveDip);

        int LastReferencingEntry(int blockCount)
        {
            for (var index = Math.Min(blockCount, pending.Count) - 1; index >= 0; index--)
            {
                if (!pending[index].IsContinuation
                    && PaginationEngine.FootnoteIdsInModelBlock(OwnerOf(pending[index].Rendered)).Any())
                    return index;
            }
            return -1;
        }

        double LargestReserveKeeping(int lastReferencing, double upperDip)
        {
            var minimumDip = Math.Min(upperDip, minimumSplitBandDip);
            SetReserve(minimumDip);
            if (CountBlocksStartingOnFirstPage() <= lastReferencing)
                return 0;

            var low = minimumDip;
            var high = upperDip;
            for (var step = 0; step < 10 && high - low > 1; step++)
            {
                var middle = (low + high) / 2;
                SetReserve(middle);
                if (CountBlocksStartingOnFirstPage() > lastReferencing)
                    low = middle;
                else
                    high = middle;
            }
            return Math.Floor(low);
        }

        var lastTake = 8;
        var passesThisRun = 0;
        try
        {
            TopUp(32);
            while (pending.Count > 0)
            {
                // Record (and maybe stop at) the state this pass starts from.
                var head = pending[0];
                var state = new PassRecord(
                    head.Rendered,
                    head.IsContinuation,
                    carriedNoteDip,
                    carryChain.Select(link => new CarryLink(link.FootnoteIds.ToArray(), link.BandHeightDip, link.PageNumber)).ToArray(),
                    localPage);
                if (!head.IsContinuation
                    && previous is not null
                    && head.Rendered >= changedEnd
                    && passesThisRun > 0
                    && oldFreshStarts!.TryGetValue(head.Rendered - renderedDelta, out var old)
                    && SameCarry(old, state))
                {
                    var pageDelta = localPage - old.LocalPageStart;
                    for (var k = head.Rendered; k < end; k++)
                        result.LocalPage[k - segment.Start] = previous.LocalPage[k - renderedDelta - previous.RenderedStart] + pageDelta;
                    result.Passes.AddRange(previous.Passes
                        .Skip(previous.Passes.IndexOf(old))
                        .Select(pass => pass with
                        {
                            StartRendered = pass.StartRendered + renderedDelta,
                            LocalPageStart = pass.LocalPageStart + pageDelta
                        }));
                    result.PageCount = previous.PageCount + pageDelta;
                    _pagesReused += previous.PageCount - old.LocalPageStart;
                    yield break;
                }
                result.Passes.Add(state);
                passesThisRun++;

                // Blocks before the head (and a split head, whose start page is already known) are final.
                run.FrontierRendered = head.IsContinuation ? head.Rendered + 1 : head.Rendered;
                run.FrontierPage = pageOffset + localPage;
                yield return false;

                if (pending[0].Block.BreakPageBefore)
                    pending[0] = SetBreakBefore(pending[0], false);

                TopUp(Math.Max(32, lastTake * 4));
                SetReserve(0);
                EnsureSafe();
                // Mid-pass yield (no progress to report): keeps one step of background work short.
                yield return false;
                var countWithoutReserve = CountBlocksStartingOnFirstPage();
                var reserveDip = ReserveFor(countWithoutReserve);
                if (reserveDip > 0)
                {
                    SetReserve(reserveDip);
                    var lastReferencing = LastReferencingEntry(countWithoutReserve);
                    if (lastReferencing >= 0 && CountBlocksStartingOnFirstPage() <= lastReferencing)
                    {
                        var keptReserve = LargestReserveKeeping(lastReferencing, reserveDip);
                        if (keptReserve > 0)
                        {
                            reserveDip = keptReserve;
                        }
                        else
                        {
                            var smaller = ReserveFor(CountBlocksStartingOnFirstPage());
                            if (smaller < reserveDip)
                            {
                                SetReserve(smaller);
                                if (ReserveFor(CountBlocksStartingOnFirstPage()) <= smaller)
                                    reserveDip = smaller;
                            }
                        }
                        SetReserve(reserveDip);
                    }
                }

                var take = pending.Count;
                for (var index = 0; index < pending.Count; index++)
                {
                    var block = pending[index].Block;
                    if (dynamicWork.GetPageNumber(block.ContentStart) > 0)
                    {
                        take = index;
                        break;
                    }

                    if (dynamicWork.GetPageNumber(block.ContentEnd) <= 0)
                        continue;

                    take = index + 1;
                    if (block is WpfParagraph paragraph
                        && PaginationEngine.FindFirstPositionOnLaterPage(dynamicWork, paragraph) is { } splitAt
                        && PaginationEngine.HasText(paragraph.ContentStart, splitAt))
                    {
                        if (!PaginationEngine.HasText(splitAt, paragraph.ContentEnd))
                            break;
                        EvictRendered(pending[index].Rendered);
                        if (PaginationEngine.SplitParagraphAt(paragraph, splitAt) is { } remainder)
                            pending.Insert(index + 1, (remainder, pending[index].Rendered, true));
                    }
                    else if (block is WpfParagraph && index > 0)
                    {
                        take = index;
                    }

                    break;
                }
                take = Math.Clamp(take, 1, pending.Count);
                lastTake = take;
                yield return false;
                var chunkFootnoteIds = NoteIdsOnFirstPage(take);
                UpdateCarry(chunkFootnoteIds, Math.Max(0, reserveDip - footnoteFrameClearanceDip), take);
                var chunk = newFlow(reserveDip);
                for (var i = 0; i < take; i++)
                {
                    work.Blocks.Remove(pending[i].Block);
                    chunk.Blocks.Add(pending[i].Block);
                }

                var chunkPaginator = ((IDocumentPaginatorSource)chunk).DocumentPaginator;
                chunkPaginator.PageSize = pageSize;
                chunkPaginator.ComputePageCount();
                var chunkPages = Math.Max(1, chunkPaginator.PageCount);
                var dynamicChunk = chunkPaginator as DynamicDocumentPaginator;
                for (var i = 0; i < take; i++)
                {
                    if (pending[i].IsContinuation)
                        continue;
                    var page = chunkPages > 1 && dynamicChunk is not null
                        ? Math.Clamp(dynamicChunk.GetPageNumber(pending[i].Block.ContentStart), 0, chunkPages - 1)
                        : 0;
                    result.LocalPage[pending[i].Rendered - segment.Start] = localPage + page;
                }

                chunk.Blocks.Clear();
                localPage += chunkPages;
                _pagesLaidOut += chunkPages;
                pending.RemoveRange(0, take);
                if (pending.Count == 0)
                    TopUp(Math.Max(32, lastTake * 4));
            }

            for (var guard = 0; carriedNoteDip > 0.5 && guard < 200; guard++)
            {
                var reserveDip = PaginationEngine.ClampFootnoteReserveDip(Math.Min(carriedNoteDip, bodyHeightDip), segment.Page);
                if (reserveDip <= 0)
                    break;
                UpdateCarry(Array.Empty<int>(), reserveDip, 0);
                localPage++;
            }

            result.PageCount = Math.Max(1, localPage);
        }
        finally
        {
            work.Blocks.Clear();
        }
    }

    private static (WpfBlock Block, int Rendered, bool IsContinuation) SetBreakBefore(
        (WpfBlock Block, int Rendered, bool IsContinuation) entry,
        bool value)
    {
        entry.Block.BreakPageBefore = value;
        return entry;
    }

    private static bool SameCarry(PassRecord old, PassRecord current)
    {
        if (!old.CarriedNoteDip.Equals(current.CarriedNoteDip) || old.CarryChain.Length != current.CarryChain.Length)
            return false;
        for (var i = 0; i < old.CarryChain.Length; i++)
        {
            var a = old.CarryChain[i];
            var b = current.CarryChain[i];
            if (!a.BandHeightDip.Equals(b.BandHeightDip)
                || a.PageNumber != b.PageNumber
                || !a.FootnoteIds.AsSpan().SequenceEqual(b.FootnoteIds))
                return false;
        }
        return true;
    }
}
