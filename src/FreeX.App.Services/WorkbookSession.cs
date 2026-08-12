using FreeX.App.Presentation;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using CommandHistoryEntry = Free.Shared.Commands.CommandHistoryEntry;
using CoreSortKey = FreeX.Core.Commands.SortKey;

namespace FreeX.App.Services;

public sealed class WorkbookSession : IDisposable
{
    private sealed class ReplaceSubtotalRowsCommand : IWorkbookCommand
    {
        private const string SubtotalFormulaPrefix = "SUBTOTAL(";

        private readonly SheetId _sheetId;
        private readonly GridRange _range;
        private readonly uint _groupColumnOffset;
        private readonly IReadOnlyList<uint> _subtotalColumnOffsets;
        private readonly int _functionNumber;
        private readonly bool _pageBreakBetweenGroups;
        private readonly bool _summaryBelowData;
        private CompositeWorkbookCommand? _appliedCommand;

        public string Label => "Subtotal";

        public ReplaceSubtotalRowsCommand(
            SheetId sheetId,
            GridRange range,
            uint groupColumnOffset,
            IReadOnlyList<uint> subtotalColumnOffsets,
            int functionNumber,
            bool pageBreakBetweenGroups,
            bool summaryBelowData)
        {
            _sheetId = sheetId;
            _range = range;
            _groupColumnOffset = groupColumnOffset;
            _subtotalColumnOffsets = subtotalColumnOffsets;
            _functionNumber = functionNumber;
            _pageBreakBetweenGroups = pageBreakBetweenGroups;
            _summaryBelowData = summaryBelowData;
        }

        public CommandOutcome Apply(ICommandContext ctx)
        {
            var sheet = ctx.GetSheet(_sheetId);
            var sheetRange = _range;
            var compactedRange = CompactRangeAfterExistingSubtotalRemoval(
                sheet,
                sheetRange);

            _appliedCommand = new CompositeWorkbookCommand(
                "Subtotal",
                [
                    new RemoveSubtotalRowsCommand(_sheetId, sheetRange),
                    new SubtotalCommand(
                        _sheetId,
                        compactedRange,
                        _groupColumnOffset,
                        _subtotalColumnOffsets,
                        _functionNumber,
                        _pageBreakBetweenGroups,
                        _summaryBelowData)
                ]);
            return _appliedCommand.Apply(ctx);
        }

        public void Revert(ICommandContext ctx)
        {
            _appliedCommand?.Revert(ctx);
            _appliedCommand = null;
        }

        private static GridRange CompactRangeAfterExistingSubtotalRemoval(Sheet sheet, GridRange sheetRange)
        {
            var subtotalRowCount = CountSubtotalRows(sheet, sheetRange);
            var compactedRowCount = sheetRange.RowCount > subtotalRowCount
                ? sheetRange.RowCount - (uint)subtotalRowCount
                : 1;
            return new GridRange(
                sheetRange.Start,
                new CellAddress(
                    sheetRange.End.Sheet,
                    sheetRange.Start.Row + compactedRowCount - 1,
                    sheetRange.End.Col));
        }

        private static int CountSubtotalRows(Sheet sheet, GridRange range)
        {
            if (!sheet.HasFormulas)
                return 0;

            var rows = new HashSet<uint>();
            foreach (var address in sheet.EnumerateFormulaCells())
            {
                if (address.Row < range.Start.Row ||
                    address.Row > range.End.Row ||
                    address.Col < range.Start.Col ||
                    address.Col > range.End.Col)
                {
                    continue;
                }

                if (IsSubtotalFormula(sheet.GetCell(address)?.FormulaText))
                    rows.Add(address.Row);
            }

            return rows.Count;
        }

        private static bool IsSubtotalFormula(string? formula) =>
            formula is not null &&
            formula.AsSpan().TrimStart().StartsWith(
                SubtotalFormulaPrefix,
                StringComparison.OrdinalIgnoreCase);
    }

    private const string MultiRangeClipboardErrorSuffix =
        " does not support multiple selected ranges yet.";

    private static readonly StyleDiff EmptyStyleDiff = new();

    private readonly IReadOnlyList<IFileAdapter> _adapters;
    private readonly StartupWorkbookLoadResult _source;
    private readonly WorkbookCellEditService _cellEditService;
    private readonly WorkbookSheetSelectionService _sheetSelectionService;
    private readonly IViewportService _viewportService;
    private readonly FindReplaceWorkflowSession _findReplaceWorkflow;
    private readonly bool _includeObjects;
    private readonly WorkbookSession? _sharedDocumentStateOwner;
    private readonly WorkbookDocumentState _documentState;
    private int _siblingViewCount;
    private int _isDisposed;
    private int _documentRetired;
    private WorkbookFileAccessIdentity? _currentFileAccessIdentity;
    private XlsxFeatureReport? _currentXlsxFeatureReport;
    private readonly WorkbookSelectionStatsCache _selectionStatsCache = new();
    private readonly WorksheetSelectionStore _worksheetSelections = new();
    private readonly HashSet<SheetId> _groupedSheetIds = [];
    private SheetId? _sheetGroupAnchor;
    private readonly WorkbookClipboardSession _workbookClipboardSession = new();

    /// <summary>
    /// True while a Copy/Cut snapshot is still live (i.e. a Paste right now would honor it).
    /// Host shells use this as the single source of truth for whether their own Copy/Cut
    /// marching-ants overlay should still be shown: rather than re-deriving "did the edit I just
    /// committed invalidate the clipboard" at every individual commit call site (a pattern that has
    /// had to be re-applied — and re-missed at new sites — three times on the Avalonia shell:
    /// R127C's Insert/Delete sites, an earlier ribbon/undo/clear pass, and the proofing/spelling/
    /// symbol/data-validation sites this property was added to fix), a shell's shared post-edit
    /// refresh choke point (Avalonia's <c>RefreshShell</c>) can simply compare its overlay state
    /// against this property once and clear the overlay whenever they disagree. Any future commit
    /// path that flows through that choke point inherits correct marquee-clearing automatically,
    /// with no new call site required.
    /// </summary>
    public bool HasPendingClipboardMarquee => _workbookClipboardSession.HasContent;
    private SheetId? _formatPainterSourceSheetId;
    private GridRange? _formatPainterSourceRange;
    private bool _formatPainterPersistent;
    private double _viewportHeight;
    private double _viewportWidth;
    /// <summary>
    /// Per-sheet Window ▸ Split (<see cref="Sheet.SplitRow"/>/<see cref="Sheet.SplitColumn"/>,
    /// distinct from Freeze Panes) independent-scroll offsets for the TopRight/BottomLeft
    /// quadrants, mirroring the WPF host's own <c>_splitPaneViewportOffsets</c> field. Excel lets
    /// each of the four split quadrants scroll independently; without this, TopRight/BottomLeft
    /// always mirror the main (BottomRight) pane's scroll position. Entries are dropped once a
    /// sheet's split is removed or its offsets fall back to matching the main pane, so this stays
    /// small and never grows unbounded across many sheets.
    /// </summary>
    private readonly Dictionary<SheetId, SplitPaneViewportOffsets> _splitPaneViewportOffsets = [];
    private readonly Dictionary<SheetId, (uint TopRow, uint LeftCol)> _viewViewportOrigins = [];
    /// <summary>
    /// This view's own zoom-percent snapshot per sheet (R85: view-window independence). Excel
    /// treats zoom as a per-window setting (each open window on a workbook can show the same sheet
    /// at a different zoom level), but <see cref="Sheet.ZoomPercent"/> lives on the shared
    /// <see cref="Sheet"/> model so it can round-trip through file save/load. Without this cache,
    /// <see cref="ZoomPercent"/> would read that shared field directly, so zooming one
    /// <see cref="CreateSiblingView"/> window would instantly "leak" into every other open window on
    /// the same sheet. Entries are lazily seeded from <see cref="Sheet.ZoomPercent"/> on first read
    /// (see <see cref="ZoomPercent"/>) and invalidated in <see cref="ApplySuccessfulWorkbookMetadataResult"/>
    /// (covering both this view's own <see cref="SetZoomPercent"/> and shared Undo/Redo of it) so a
    /// stale value is never returned; <see cref="SetZoomPercent"/> immediately reseeds its own entry
    /// with the value it just applied so a sibling view's later zoom change can't retroactively
    /// change what this view reports.
    /// </summary>
    private readonly Dictionary<SheetId, int> _viewZoomOverrides = [];
    /// <summary>
    /// This view's own Show Gridlines / Show Headings / Show Formulas / Freeze Panes snapshots per
    /// sheet (R86: extends the R85 <see cref="_viewZoomOverrides"/> per-view-independence pattern to
    /// the rest of the per-window view settings). Excel keeps all four as attributes of the
    /// per-window <c>sheetView</c>/<c>pane</c> OOXML elements, but <see cref="Sheet.ShowGridlines"/>,
    /// <see cref="Sheet.ShowHeadings"/>, <see cref="Sheet.ShowFormulas"/>, <see cref="Sheet.FrozenRows"/>,
    /// and <see cref="Sheet.FrozenCols"/> live on the shared <see cref="Sheet"/> model so they can
    /// round-trip through file save/load. Without these caches, <see cref="IsShowingGridlines"/> /
    /// <see cref="IsShowingHeadings"/> / <see cref="IsShowingFormulas"/> / the freeze-pane scroll
    /// helpers would read those shared fields directly, so toggling any of them in one
    /// <see cref="CreateSiblingView"/> window would instantly "leak" into every other open window on
    /// the same sheet. <see cref="IsShowingGridlines"/>/<see cref="IsShowingHeadings"/>/
    /// <see cref="IsShowingFormulas"/> entries are lazily seeded from the shared <see cref="Sheet"/>
    /// fields on first read and invalidated in <see cref="ApplySuccessfulWorkbookMetadataResult"/>
    /// (covering both this view's own setters and shared Undo/Redo of them) so a stale value is never
    /// returned; each setter immediately reseeds its own entry/entries with the value(s) it just
    /// applied so a sibling view's later change can't retroactively change what this view reports.
    /// The <see cref="_viewFrozenRowsOverrides"/>/<see cref="_viewFrozenColsOverrides"/> entries below
    /// instead follow the up-front-snapshot pattern described on <see cref="GetEffectiveFrozenRows"/>
    /// (R87-window-state-regression-fix: a mere read must never itself seed/freeze a value -- see that
    /// remark for why).
    /// </summary>
    private readonly Dictionary<SheetId, bool> _viewShowGridlinesOverrides = [];
    private readonly Dictionary<SheetId, bool> _viewShowHeadingsOverrides = [];
    private readonly Dictionary<SheetId, bool> _viewShowFormulasOverrides = [];
    private readonly Dictionary<SheetId, uint> _viewFrozenRowsOverrides = [];
    private readonly Dictionary<SheetId, uint> _viewFrozenColsOverrides = [];
    /// <summary>
    /// This view's own Window ▸ Split row/column snapshot per sheet (R87: extends the R86
    /// <see cref="_viewFrozenRowsOverrides"/>/<see cref="_viewFrozenColsOverrides"/> per-view-independence
    /// pattern to Split, which -- like Freeze Panes -- is a distinct per-window Excel feature. Excel
    /// lets each open window on a workbook have its own independent split, but <see cref="Sheet.SplitRow"/>/
    /// <see cref="Sheet.SplitColumn"/> live on the shared <see cref="Sheet"/> model so they can round-trip
    /// through file save/load. Without these caches, <see cref="HasIndependentSplitPaneTopRight"/>/
    /// <see cref="HasIndependentSplitPaneBottomLeft"/> would read those shared fields directly, so
    /// splitting (or clearing a split) in one <see cref="CreateSiblingView"/> window would instantly
    /// "leak" into every other open window on the same sheet. Entries are explicitly seeded up front by
    /// <see cref="SeedViewSplitAndFrozenOverrides"/> (this session's constructor and
    /// <see cref="InitializeSiblingView"/>) and invalidated both in <see cref="ApplySuccessfulWorkbookMetadataResult"/>
    /// (covering Undo/Redo of a split change) and in <see cref="ApplySuccessfulEditResult"/> (covering this
    /// view's own forward-apply of <c>SetSplitPanesCommand</c>, which today reaches this session only
    /// through the generic <see cref="ExecuteReviewCommand"/> path rather than a dedicated setter) so a
    /// stale value is never returned. <see cref="GetEffectiveSplitRow"/>/<see cref="GetEffectiveSplitCol"/>
    /// deliberately do NOT seed-on-read (R87-window-state-regression-fix): a <see cref="RefreshViewport"/>
    /// triggered incidentally by e.g. <see cref="SelectSheet"/> must never itself freeze in whatever the
    /// shared field holds at that instant (that instant may predate a caller finishing setting up the
    /// sheet's Split, e.g. via direct <see cref="Sheet.SplitRow"/>/<see cref="Sheet.SplitColumn"/>
    /// assignment) -- absent an entry, they simply fall back to the live shared field on every read.
    /// </summary>
    private readonly Dictionary<SheetId, uint?> _viewSplitRowOverrides = [];
    private readonly Dictionary<SheetId, uint?> _viewSplitColOverrides = [];
    /// <summary>
    /// This view's own worksheet-view-mode snapshot per sheet (R86: view-window independence for
    /// View Mode, mirroring <see cref="_viewZoomOverrides"/> for Zoom). Excel treats view mode
    /// (Normal/Page Layout/Page Break Preview) as a per-window setting, but <see cref="Sheet.ViewMode"/>
    /// lives on the shared <see cref="Sheet"/> model so it can round-trip through file save/load.
    /// Without this cache, <see cref="ViewMode"/> would read that shared field directly, so
    /// switching view mode in one <see cref="CreateSiblingView"/> window would instantly "leak"
    /// into every other open window on the same sheet. Entries are lazily seeded from
    /// <see cref="Sheet.ViewMode"/> on first read (see <see cref="ViewMode"/>) and invalidated in
    /// <see cref="ApplySuccessfulWorkbookMetadataResult"/> (covering both this view's own
    /// <see cref="SetWorksheetViewMode"/> and shared Undo/Redo of it) so a stale value is never
    /// returned; <see cref="SetWorksheetViewMode"/> immediately reseeds its own entry with the
    /// value it just applied so a sibling view's later change can't retroactively change what this
    /// view reports.
    /// </summary>
    private readonly Dictionary<SheetId, WorksheetViewMode> _viewModeOverrides = [];
    private ulong _selectionStatsRevision;
    internal WorkbookSession(
        StartupWorkbookLoadResult source,
        IReadOnlyList<IFileAdapter> adapters,
        WorkbookCellEditService cellEditService,
        WorkbookSheetSelectionService sheetSelectionService,
        IViewportService viewportService,
        double viewportHeight,
        double viewportWidth,
        bool includeObjects,
        WorkbookSession? sharedDocumentStateOwner = null,
        WorkbookDocumentState? documentState = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(cellEditService);
        ArgumentNullException.ThrowIfNull(sheetSelectionService);
        ArgumentNullException.ThrowIfNull(viewportService);
        if (sharedDocumentStateOwner is not null && documentState is not null)
            throw new ArgumentException("A sibling session already supplies the shared document state.", nameof(documentState));

        _source = source;
        _adapters = adapters;
        _cellEditService = cellEditService;
        _sheetSelectionService = sheetSelectionService;
        _viewportService = viewportService;
        _viewportHeight = NormalizeViewportDimension(viewportHeight, fallback: 1);
        _viewportWidth = NormalizeViewportDimension(viewportWidth, fallback: 1);
        _includeObjects = includeObjects;
        _sharedDocumentStateOwner = sharedDocumentStateOwner;
        _documentState = sharedDocumentStateOwner?._documentState ?? documentState ?? new WorkbookDocumentState();

        Workbook = source.Workbook;
        if (sharedDocumentStateOwner is null)
        {
            _documentState.SetCurrentFilePath(source.OpenedAsTemplate ? null : source.SourcePath);
            CurrentFileAccessIdentity = ResolveCurrentFileAccessIdentity(source);
            CurrentXlsxFeatureReport = source.FeatureReport;
        }
        OpenFormats = BuildFormats(adapters, static format => format.CanOpen);
        SaveFormats = BuildFormats(adapters, static format => format.CanSave);

        var selection = _sheetSelectionService.EnsureActiveSheet(Workbook);
        ActiveSheet = selection.Sheet;
        SheetTabs = selection.Tabs;
        SelectSingleSheetGroup(ActiveSheet.Id);
        RefreshSheetTabsForActiveSheet();
        ActiveCell = GetInitialActiveCell(ActiveSheet);
        SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
        SeedViewSplitAndFrozenOverrides();
        Viewport = BuildViewport();
        _findReplaceWorkflow = new FindReplaceWorkflowSession(
            () => Workbook,
            () => ActiveCell,
            GoToCell,
            command => _cellEditService.ExecuteEditCommand(Workbook, command));
    }

    public IReadOnlyList<IFileAdapter> FileAdapters => _adapters;

    /// <summary>
    /// Creates a view-local session over this session's document. Workbook, command history,
    /// save/dirty metadata, and file identity remain shared; selection, viewport, formula-edit,
    /// grouped-sheet, clipboard, and prompt state belong to the returned view.
    /// </summary>
    public WorkbookSession CreateSiblingView(double viewportHeight, double viewportWidth)
    {
        ThrowIfDisposed();
        var documentOwner = _sharedDocumentStateOwner ?? this;
        documentOwner.AcquireSiblingView();
        try
        {
            var sibling = new WorkbookSession(
                _source,
                _adapters,
                _cellEditService,
                new WorkbookSheetSelectionService(),
                new ViewportService(),
                viewportHeight,
                viewportWidth,
                _includeObjects,
                documentOwner);
            sibling.InitializeSiblingView(ActiveSheet.Id);
            return sibling;
        }
        catch
        {
            documentOwner.ReleaseSiblingView();
            throw;
        }
    }

    /// <summary>
    /// Releases this view's event subscriptions and, once every sibling view has
    /// gone away, retires the shared workbook from command history, recalculation, and XLSX
    /// source-package state. Root and sibling sessions therefore have explicit,
    /// bounded ownership without allowing one window to invalidate another
    /// window's shared document.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        WorkbookChanged = null;
        if (_sharedDocumentStateOwner is { } owner)
        {
            owner.ReleaseSiblingView();
            return;
        }

        TryRetireDocument();
    }

    private void AcquireSiblingView() =>
        Interlocked.Increment(ref _siblingViewCount);

    private void ReleaseSiblingView()
    {
        if (Interlocked.Decrement(ref _siblingViewCount) == 0 &&
            Volatile.Read(ref _isDisposed) != 0)
        {
            TryRetireDocument();
        }
    }

    private void TryRetireDocument()
    {
        if (Volatile.Read(ref _siblingViewCount) != 0 ||
            Interlocked.Exchange(ref _documentRetired, 1) != 0)
        {
            return;
        }

        _cellEditService.RetireWorkbook(Workbook);
        XlsxFileAdapter.DetachSourcePackage(Workbook);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
            throw new ObjectDisposedException(nameof(WorkbookSession));
    }

    private void InitializeSiblingView(SheetId sheetId)
    {
        ActiveSheet = Workbook.GetSheet(sheetId) ?? ActiveSheet;
        RefreshSheetTabsForActiveSheet();
        ActiveCell = new CellAddress(ActiveSheet.Id, 1, 1);
        SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
        FormulaEditAddress = null;
        SeedViewSplitAndFrozenOverrides();
        _viewViewportOrigins[ActiveSheet.Id] = (
            GetScrollableRowStart(),
            GetScrollableColumnStart());
        Viewport = BuildViewport();
    }

    /// <summary>
    /// Snapshots this view's Window ▸ Split (<see cref="_viewSplitRowOverrides"/>/
    /// <see cref="_viewSplitColOverrides"/>) and Freeze Panes (<see cref="_viewFrozenRowsOverrides"/>/
    /// <see cref="_viewFrozenColsOverrides"/>) state from the shared <see cref="ActiveSheet"/> fields
    /// the moment this view starts observing the document -- called from this session's constructor
    /// and from <see cref="InitializeSiblingView"/>, always before this view's first
    /// <see cref="BuildViewport"/>. <see cref="GetEffectiveSplitRow"/>/<see cref="GetEffectiveSplitCol"/>/
    /// <see cref="GetEffectiveFrozenRows"/>/<see cref="GetEffectiveFrozenCols"/> are pure peek-the-cache-
    /// or-fall-back-to-the-live-field reads with no write-on-read side effect of their own (a
    /// <see cref="RefreshViewport"/> triggered by, say, <see cref="SelectSheet"/> must never silently
    /// freeze in whatever the sheet's Split/Freeze fields happen to hold at that instant), so without
    /// this explicit up-front snapshot a freshly created sibling view would have nothing recorded yet
    /// and would transparently pick up a sibling's later Split/Freeze change on the same shared Sheet --
    /// exactly the cross-view leak R87 set out to fix. Each setter/command-apply path
    /// (<see cref="SetFreezePanes"/>, <see cref="ApplySuccessfulWorkbookMetadataResult"/>,
    /// <see cref="ApplySuccessfulEditResult"/>) subsequently keeps this view's own entries in sync as
    /// its own Split/Freeze state actually changes.
    /// </summary>
    private void SeedViewSplitAndFrozenOverrides()
    {
        _viewSplitRowOverrides[ActiveSheet.Id] = ActiveSheet.SplitRow;
        _viewSplitColOverrides[ActiveSheet.Id] = ActiveSheet.SplitColumn;
        _viewFrozenRowsOverrides[ActiveSheet.Id] = ActiveSheet.FrozenRows;
        _viewFrozenColsOverrides[ActiveSheet.Id] = ActiveSheet.FrozenCols;
    }

    /// <summary>
    /// Reconciles this view's own per-window overrides (zoom, view mode, gridlines, headings,
    /// show-formulas, freeze panes, split) onto the shared <see cref="Sheet"/> fields for every
    /// sheet this view has diverged on, immediately before this view serializes the workbook
    /// (R120-corewriter-persist-saving-window-view-1). <see cref="Sheet.ZoomPercent"/>/
    /// <see cref="Sheet.ViewMode"/>/<see cref="Sheet.ShowGridlines"/>/<see cref="Sheet.ShowHeadings"/>/
    /// <see cref="Sheet.ShowFormulas"/>/<see cref="Sheet.FrozenRows"/>/<see cref="Sheet.FrozenCols"/>/
    /// <see cref="Sheet.SplitRow"/>/<see cref="Sheet.SplitColumn"/> are one shared field per sheet,
    /// mutated in place by whichever sibling view's command last executed -- the <c>_view*Overrides</c>
    /// caches above exist so THIS view keeps displaying its own remembered value even after a
    /// sibling view changes those shared fields, but every writer (e.g. <c>XlsxWorksheetViewWriter</c>)
    /// still only ever reads the shared fields directly. Without this reconciliation, saving from a
    /// view whose own state has diverged from the shared fields would silently persist whichever
    /// sibling view last touched them instead of this view's own -- the same bug
    /// <see cref="WorksheetViewStateStore"/>'s WPF-host counterpart exists to fix. Only sheets
    /// present in one of the override caches are touched; a sheet this view never diverged on keeps
    /// its already-correct shared value untouched. (<see cref="Sheet.ShowRulers"/> is intentionally
    /// excluded -- unlike the WPF host, this shell has no per-view Show Rulers override to begin
    /// with; see <see cref="SetShowRulers"/>'s remarks.)
    /// </summary>
    public void ReconcileViewStateForSave()
    {
        var sheetIds = new HashSet<SheetId>();
        sheetIds.UnionWith(_viewZoomOverrides.Keys);
        sheetIds.UnionWith(_viewModeOverrides.Keys);
        sheetIds.UnionWith(_viewShowGridlinesOverrides.Keys);
        sheetIds.UnionWith(_viewShowHeadingsOverrides.Keys);
        sheetIds.UnionWith(_viewShowFormulasOverrides.Keys);
        sheetIds.UnionWith(_viewFrozenRowsOverrides.Keys);
        sheetIds.UnionWith(_viewFrozenColsOverrides.Keys);
        sheetIds.UnionWith(_viewSplitRowOverrides.Keys);
        sheetIds.UnionWith(_viewSplitColOverrides.Keys);

        foreach (var sheetId in sheetIds)
        {
            if (Workbook.GetSheet(sheetId) is not { } sheet)
                continue;

            if (_viewZoomOverrides.TryGetValue(sheetId, out var zoom))
                sheet.ZoomPercent = zoom;
            if (_viewModeOverrides.TryGetValue(sheetId, out var viewMode))
                sheet.ViewMode = viewMode;
            if (_viewShowGridlinesOverrides.TryGetValue(sheetId, out var showGridlines))
                sheet.ShowGridlines = showGridlines;
            if (_viewShowHeadingsOverrides.TryGetValue(sheetId, out var showHeadings))
                sheet.ShowHeadings = showHeadings;
            if (_viewShowFormulasOverrides.TryGetValue(sheetId, out var showFormulas))
                sheet.ShowFormulas = showFormulas;
            if (_viewFrozenRowsOverrides.TryGetValue(sheetId, out var frozenRows))
                sheet.FrozenRows = frozenRows;
            if (_viewFrozenColsOverrides.TryGetValue(sheetId, out var frozenCols))
                sheet.FrozenCols = frozenCols;
            if (_viewSplitRowOverrides.TryGetValue(sheetId, out var splitRow))
                sheet.SplitRow = splitRow;
            if (_viewSplitColOverrides.TryGetValue(sheetId, out var splitCol))
                sheet.SplitColumn = splitCol;
        }
    }

    public Workbook Workbook { get; }

    /// <summary>
    /// Raised after a workbook or its document metadata changes. Hosts with multiple views can
    /// use this to refresh sibling viewports without polling or wiring every individual command
    /// entry point.
    /// </summary>
    public event EventHandler? WorkbookChanged;

    /// <summary>
    /// Cells the session's <c>RecalcEngine</c> most recently classified as part of a non-iterative
    /// circular reference. Reflects the state as of the last recalculation (<see cref="RecalculateWorkbook"/>
    /// / <see cref="RecalculateActiveSheet"/> / any edit that triggered automatic recalculation) --
    /// callers that need this up to date should recalculate first. Feed straight into
    /// <c>FormulaAuditingService.FindFormulaErrors</c>/<c>FindFormulaErrorIssues</c>'s
    /// <c>cyclicCells</c> parameter to surface the "Formulas with circular references" Error-Checking rule.
    /// </summary>
    public IReadOnlyCollection<CellAddress> CyclicCells => _cellEditService.CyclicCells;

    public Sheet ActiveSheet { get; private set; }

    public ViewportModel Viewport { get; private set; }

    public double ViewportHeight => _viewportHeight;

    public double ViewportWidth => _viewportWidth;

    public CellAddress ActiveCell { get; private set; }

    public GridRange SelectedRange { get; private set; }

    public IReadOnlyList<GridRange> SelectedRanges { get; private set; } = [];

    public CellAddress? FormulaEditAddress { get; private set; }

    /// <summary>
    /// Optional host hook that resolves a Warning/Information ("AskToContinue") data-validation
    /// alert for <see cref="CommitCellText"/> -- mirrors the WPF host's
    /// <c>IUserMessageService.ShowMessage</c> prompt (Warning alert style: Yes/No/Cancel;
    /// Information alert style: OK/Cancel). <see cref="UserMessageResult.Yes"/> or
    /// <see cref="UserMessageResult.Ok"/> commits the invalid entry; anything else does not. Left
    /// null (the default), an AskToContinue violation keeps this session's original pass-through
    /// behavior -- silently accepted -- so a host that hasn't wired a prompt is unaffected.
    /// </summary>
    public Func<DataValidationPromptRequest, UserMessageResult>? DataValidationPromptResolver { get; set; }

    public IReadOnlyList<WorkbookSheetTab> SheetTabs { get; private set; }

    public bool IsWorkbookGrouped =>
        _groupedSheetIds.Contains(ActiveSheet.Id) &&
        GetSelectableSheetIds().Count(_groupedSheetIds.Contains) > 1;

    /// <summary>
    /// Returns the sheets targeted by a grouped edit, preserving the active sheet as the first
    /// target. Hosts use this to build the same undoable per-sheet command that WPF's grouped
    /// ribbon actions execute.
    /// </summary>
    public IReadOnlyList<SheetId> GetCurrentGroupedEditSheetIds() => CurrentGroupedEditSheetIds();

    public bool IsShowingGridlines
    {
        get
        {
            if (_viewShowGridlinesOverrides.TryGetValue(ActiveSheet.Id, out var showGridlines))
                return showGridlines;

            showGridlines = ActiveSheet.ShowGridlines;
            _viewShowGridlinesOverrides[ActiveSheet.Id] = showGridlines;
            return showGridlines;
        }
    }

    public bool IsShowingHeadings
    {
        get
        {
            if (_viewShowHeadingsOverrides.TryGetValue(ActiveSheet.Id, out var showHeadings))
                return showHeadings;

            showHeadings = ActiveSheet.ShowHeadings;
            _viewShowHeadingsOverrides[ActiveSheet.Id] = showHeadings;
            return showHeadings;
        }
    }

    public bool IsShowingFormulas
    {
        get
        {
            if (_viewShowFormulasOverrides.TryGetValue(ActiveSheet.Id, out var showFormulas))
                return showFormulas;

            showFormulas = ActiveSheet.ShowFormulas;
            _viewShowFormulasOverrides[ActiveSheet.Id] = showFormulas;
            return showFormulas;
        }
    }

    public int ZoomPercent
    {
        get
        {
            if (_viewZoomOverrides.TryGetValue(ActiveSheet.Id, out var zoom))
                return zoom;

            zoom = ActiveSheet.ZoomPercent;
            _viewZoomOverrides[ActiveSheet.Id] = zoom;
            return zoom;
        }
    }

    /// <summary>
    /// This view's own worksheet view mode (Normal/Page Layout/Page Break Preview). See
    /// <see cref="_viewModeOverrides"/> remarks -- callers that display or branch on view mode
    /// should read this instead of <c>ActiveSheet.ViewMode</c> directly, so a sibling
    /// <see cref="CreateSiblingView"/> window's view-mode change never leaks into this view.
    /// </summary>
    public WorksheetViewMode ViewMode
    {
        get
        {
            if (_viewModeOverrides.TryGetValue(ActiveSheet.Id, out var viewMode))
                return viewMode;

            viewMode = ActiveSheet.ViewMode;
            _viewModeOverrides[ActiveSheet.Id] = viewMode;
            return viewMode;
        }
    }

    public bool IsFormatPainterActive =>
        _formatPainterSourceSheetId is not null &&
        _formatPainterSourceRange is not null;

    public bool IsFormatPainterPersistent => IsFormatPainterActive && _formatPainterPersistent;

    public IReadOnlyList<WorkbookHiddenSheet> HiddenSheets =>
        Workbook.Sheets
            .Where(sheet => sheet.IsHidden && !sheet.IsVeryHidden)
            .Select(sheet => new WorkbookHiddenSheet(sheet.Id, sheet.Name))
            .ToList();

    public bool CanHideActiveSheet =>
        Workbook.Sheets.Any(sheet => sheet.Id != ActiveSheet.Id && !sheet.IsHidden && !sheet.IsVeryHidden);

    public string? CurrentFilePath
    {
        get => _documentState.CurrentFilePath;
        private set => _documentState.SetCurrentFilePath(value);
    }

    public WorkbookFileAccessIdentity? CurrentFileAccessIdentity
    {
        get => _sharedDocumentStateOwner?.CurrentFileAccessIdentity ?? _currentFileAccessIdentity;
        private set
        {
            if (_sharedDocumentStateOwner is { } owner)
                owner.CurrentFileAccessIdentity = value;
            else
                _currentFileAccessIdentity = value;
        }
    }

    public XlsxFeatureReport? CurrentXlsxFeatureReport
    {
        get => _sharedDocumentStateOwner?.CurrentXlsxFeatureReport ?? _currentXlsxFeatureReport;
        private set
        {
            if (_sharedDocumentStateOwner is { } owner)
                owner.CurrentXlsxFeatureReport = value;
            else
                _currentXlsxFeatureReport = value;
        }
    }

    public bool IsDirty => _documentState.IsDirty;

    /// <summary>
    /// Controls the next close prompt for this document. This belongs to the shared document
    /// state so every renderer and every sibling view observes the same lifecycle decision.
    /// </summary>
    public bool SuppressClosePrompt
    {
        get => _documentState.SuppressClosePrompt;
        set => _documentState.SuppressClosePrompt = value;
    }

    /// <summary>
    /// Monotonically-increasing counter, incremented with every transition to dirty.
    /// The async save path captures the shared <see cref="WorkbookDocumentState"/> generation
    /// before awaiting and compares afterwards to detect edits that arrived mid-save.
    /// </summary>
    public int DirtyGeneration => _documentState.DirtyGeneration;

    public bool IsFallback => _source.IsFallback;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CurrentFilePath)
            ? _source.DisplayName
            : Path.GetFileName(CurrentFilePath);

    public string StartupStatus => FormatStartupStatus(_source);

    public IReadOnlyList<FileFormatDescriptor> OpenFormats { get; }

    public IReadOnlyList<FileFormatDescriptor> SaveFormats { get; }

    public bool CanUndo => _cellEditService.CanUndo(Workbook.Id);

    public bool CanRedo => _cellEditService.CanRedo(Workbook.Id);

    public IReadOnlyList<CommandHistoryEntry> GetUndoHistory(int maxCount) =>
        _cellEditService.GetUndoHistory(Workbook.Id, maxCount);

    public IReadOnlyList<CommandHistoryEntry> GetRedoHistory(int maxCount) =>
        _cellEditService.GetRedoHistory(Workbook.Id, maxCount);

    /// <summary>Whether a repeatable command is available for <see cref="RepeatLastAction"/> (F4).</summary>
    public bool CanRepeatLastAction => _cellEditService.CanRepeatLastEdit(Workbook.Id);

    // NOTE: these read GetCellStyle(ActiveCell), NOT GetCellStyle(SelectedRange.Start). SelectedRange.Start
    // is GridRange's normalized top-left corner (see GridRange.cs), which is only the same cell as
    // ActiveCell when the selection gesture happened to run down/right. ActiveCell is pinned to the actual
    // anchor cell the user started the drag/shift-extend from (see SelectAnchoredRange), and that is the
    // cell whose formatting Excel's ribbon toggles show/flip -- e.g. click C5 (bold), shift-click up to A1:
    // Excel keeps the active cell at C5 and shows Bold as pressed, even though most of A1:C5 isn't bold.
    // Reading Start here instead would report/flip against the wrong corner on any upward or leftward drag.
    public bool IsSelectedRangeStartBold => GetCellStyle(ActiveCell).Bold;

    public bool IsSelectedRangeStartItalic => GetCellStyle(ActiveCell).Italic;

    public bool IsSelectedRangeStartUnderline
    {
        get
        {
            var style = GetCellStyle(ActiveCell);
            return style.Underline && !style.Strikethrough;
        }
    }

    public bool IsSelectedRangeStartStrikethrough => GetCellStyle(ActiveCell).Strikethrough;

    public bool IsSelectedRangeStartDoubleUnderline => GetCellStyle(ActiveCell).DoubleUnderline;

    public bool IsSelectedRangeStartWrapText => GetCellStyle(ActiveCell).WrapText;

    public bool IsSelectedRangeStartLocked => GetCellStyle(ActiveCell).Locked;

    public bool IsSelectedRangeMerged => CellMergePlanner.IsSelectionMerged(ActiveSheet, SelectedRange);

    public HorizontalAlignment SelectedRangeStartHorizontalAlignment =>
        GetCellStyle(ActiveCell).HorizontalAlignment;

    public VerticalAlignment SelectedRangeStartVerticalAlignment =>
        GetCellStyle(ActiveCell).VerticalAlignment;

    public int SelectedRangeStartIndentLevel =>
        GetCellStyle(ActiveCell).IndentLevel;

    public double SelectedRangeStartFontSize =>
        GetCellStyle(ActiveCell).FontSize;

    public int SelectedRangeStartTextRotation =>
        GetCellStyle(ActiveCell).TextRotation;

    public CellColor SelectedRangeStartFontColor =>
        GetCellStyle(ActiveCell).FontColor;

    public CellColor? SelectedRangeStartFillColor =>
        GetCellStyle(ActiveCell).FillColor;

    /// <summary>Returns the concrete style used to seed Format Cells, including theme references.</summary>
    public CellStyle SelectedRangeStartStyle => GetCellStyle(ActiveCell).Clone();

    public string SelectedRangeStartNumberFormat =>
        GetCellStyle(ActiveCell).NumberFormat;

    public WorkbookSelectionStats SelectionStats =>
        _selectionStatsCache.GetOrCalculate(ActiveSheet, SelectedRanges, _selectionStatsRevision);

    public string SelectionStatsText =>
        WorkbookSelectionStatsFormatter.Format(SelectionStats);

    public string LastFindText => _findReplaceWorkflow.LastFindText;

    public StyleDiff? CreateFormatDiffFromActiveCell() =>
        CreateFormatDiffFromCell(ActiveCell);

    public StyleDiff? CreateFormatDiffFromCell(CellAddress address)
    {
        var sheet = Workbook.GetSheet(address.Sheet);
        var styleId = sheet?.GetCell(address)?.StyleId ??
            sheet?.GetStyleOnly(address.Row, address.Col);
        return styleId is { } resolvedStyleId
            ? StyleDiff.FromStyle(Workbook.GetStyle(resolvedStyleId))
            : null;
    }

    public void SelectCell(CellAddress address)
    {
        ActiveCell = address;
        ActiveSheet.ActiveRow = address.Row;
        ActiveSheet.ActiveCol = address.Col;
        SetSingleSelectedRange(new GridRange(address, address));
        FormulaEditAddress = null;
        EnsureActiveCellVisible();
    }

    public void SelectRange(GridRange range)
    {
        ValidateSelectionRange(range, nameof(range));
        SelectRanges(range, [range]);
    }

    /// <summary>
    /// Projects a range selected in a second formula-point workbook window into that source
    /// window's portable selection state. Native hosts retain sheet-tab, grid, focus, and status
    /// rendering after this transition.
    /// </summary>
    public bool SelectFormulaPointModeSourceRange(GridRange range)
    {
        if (!range.Start.Sheet.Equals(range.End.Sheet) ||
            Workbook.GetSheet(range.Start.Sheet) is null)
        {
            return false;
        }

        if (!ActiveSheet.Id.Equals(range.Start.Sheet))
            SelectSheet(range.Start.Sheet);
        if (!ActiveSheet.Id.Equals(range.Start.Sheet))
            return false;

        SelectRange(range);
        return true;
    }

    /// <summary>
    /// Selects a range while a formula is being edited, keeping the formula's source cell
    /// separate from the pointed-to worksheet selection. This is the state Excel exposes while
    /// formula point mode is active: the grid highlights the reference range, but Enter still
    /// commits the edit to <paramref name="formulaEditAddress"/>. When supplied,
    /// <paramref name="selectionAnchor"/> preserves the directional cell that started the
    /// formula-point gesture instead of normalizing the active cell to the range's top-left.
    /// </summary>
    public void SelectRangeForFormulaEdit(
        GridRange range,
        CellAddress formulaEditAddress,
        CellAddress? selectionAnchor = null)
    {
        ValidateSelectionRange(range, nameof(range));
        if (!IsValidAddress(formulaEditAddress) || Workbook.GetSheet(formulaEditAddress.Sheet) is null)
            throw new ArgumentOutOfRangeException(
                nameof(formulaEditAddress),
                "The formula edit cell must belong to an existing worksheet and be inside the worksheet bounds.");

        var activeCell = selectionAnchor ?? range.Start;
        if (!range.Contains(activeCell))
            throw new ArgumentOutOfRangeException(
                nameof(selectionAnchor),
                "The formula selection anchor must be inside the selected range.");

        SetSelectedRanges(range, [range]);
        ActiveCell = activeCell;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        FormulaEditAddress = formulaEditAddress;
        EnsureActiveCellVisible();
    }

    public void SelectRanges(GridRange primaryRange, IReadOnlyList<GridRange> ranges) =>
        SelectRanges(primaryRange, ranges, primaryRange.Start);

    /// <summary>
    /// Selects <paramref name="primaryRange"/> (plus any additional <paramref name="ranges"/> areas) and
    /// pins <see cref="ActiveCell"/> to <paramref name="activeCell"/> -- the cell within the selection
    /// that is "current" for editing and for commands that read the active cell -- while keeping the
    /// selected rectangle itself intact. Unlike <see cref="SelectAnchoredRange"/> (which follows the
    /// moving cursor end of a drag / shift-extend), the viewport scrolls to <paramref name="activeCell"/>
    /// itself. Excel's Ctrl+. corner-cycling uses this to walk the active cell around the four corners of
    /// a selection WITHOUT shrinking it (matching WPF's CycleSelectionCorner, MainWindow.Selection.cs,
    /// which leaves SheetGrid.SelectedRange untouched and only moves _selectionAnchor). When
    /// <paramref name="activeCell"/> falls outside <paramref name="primaryRange"/> it falls back to the
    /// normalized top-left, preserving the active-cell-inside-selection invariant.
    /// </summary>
    public void SelectRanges(GridRange primaryRange, IReadOnlyList<GridRange> ranges, CellAddress activeCell)
    {
        ValidateSelectionRange(primaryRange, nameof(primaryRange));
        if (ranges.Count == 0)
            throw new ArgumentException("At least one selected range is required.", nameof(ranges));
        foreach (var range in ranges)
            ValidateSelectionRange(range, nameof(ranges));

        SetSelectedRanges(primaryRange, ranges);
        ActiveCell = primaryRange.Contains(activeCell) ? activeCell : primaryRange.Start;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        FormulaEditAddress = null;
        EnsureActiveCellVisible();
    }

    /// <summary>
    /// Synchronizes selection state supplied by a renderer that still owns viewport scrolling.
    /// Unlike the interactive selection methods, this does not scroll or rebuild the portable
    /// viewport, so adopting the state cannot overwrite that renderer's per-window view origin.
    /// </summary>
    public void SynchronizeSelectionState(
        SheetId sheetId,
        GridRange primaryRange,
        IReadOnlyList<GridRange> ranges,
        CellAddress activeCell,
        IReadOnlyCollection<SheetId>? groupedSheetIds = null,
        SheetId? sheetGroupAnchor = null,
        CellAddress? formulaEditAddress = null)
    {
        ThrowIfDisposed();
        if (primaryRange.Start.Sheet != sheetId || primaryRange.End.Sheet != sheetId)
            throw new ArgumentException("The primary range must belong to the synchronized sheet.", nameof(primaryRange));
        if (ranges.Count == 0)
            throw new ArgumentException("At least one selected range is required.", nameof(ranges));
        foreach (var range in ranges)
        {
            if (range.Start.Sheet != sheetId || range.End.Sheet != sheetId)
                throw new ArgumentException("Every selected range must belong to the synchronized sheet.", nameof(ranges));
        }

        if (!primaryRange.Contains(activeCell))
            throw new ArgumentOutOfRangeException(nameof(activeCell), "The active cell must be inside the primary range.");
        if (formulaEditAddress is { } editAddress &&
            (!IsValidAddress(editAddress) || Workbook.GetSheet(editAddress.Sheet) is null))
        {
            throw new ArgumentOutOfRangeException(
                nameof(formulaEditAddress),
                "The formula edit cell must belong to an existing worksheet and be inside the worksheet bounds.");
        }

        if (ActiveSheet.Id != sheetId)
            RememberActiveWorksheetSelection();

        if (groupedSheetIds is not null)
        {
            SetGroupedSheetIds(groupedSheetIds, sheetId);
            _sheetGroupAnchor = sheetGroupAnchor is { } anchor && _groupedSheetIds.Contains(anchor)
                ? anchor
                : sheetId;
        }

        var selection = _sheetSelectionService.SelectSheet(Workbook, sheetId, _groupedSheetIds);
        if (selection.Sheet.Id != sheetId)
            throw new ArgumentOutOfRangeException(nameof(sheetId), "The synchronized sheet must be visible and selectable.");

        ActiveSheet = selection.Sheet;
        RefreshSheetTabsForActiveSheet();
        ValidateSelectionRange(primaryRange, nameof(primaryRange));
        foreach (var range in ranges)
            ValidateSelectionRange(range, nameof(ranges));
        SetSelectedRanges(primaryRange, ranges);
        ActiveCell = activeCell;
        ActiveSheet.ActiveRow = activeCell.Row;
        ActiveSheet.ActiveCol = activeCell.Col;
        FormulaEditAddress = formulaEditAddress;
    }

    /// <summary>
    /// Selects the rectangle spanning <paramref name="anchor"/>..<paramref name="cursor"/> and pins
    /// <see cref="ActiveCell"/> to <paramref name="anchor"/> -- the fixed corner the user started the
    /// drag / shift-extend from. That corner is NOT the range's normalized top-left
    /// <see cref="GridRange.Start"/> whenever the gesture ran upward or leftward (e.g. click C5, drag to
    /// A1), so plain <see cref="SelectRange(GridRange)"/> -- which always collapses <see cref="ActiveCell"/>
    /// onto Start -- would lose the true anchor. Excel keeps the active cell at that original corner so
    /// View &gt; Split, Freeze Panes and the active-cell box resolve against it, while the viewport
    /// follows the moving <paramref name="cursor"/>. Mirrors the WPF host, whose persistent
    /// _selectionAnchor / _selectionCursor stay distinct (MainWindow.xaml.cs / MainWindow.Selection.cs);
    /// the Avalonia shell used to keep the anchor only in gesture-scoped fields that were cleared the
    /// instant the drag ended, leaving just the collapsed Start behind (split-creation-selection-anchor
    /// parity).
    /// </summary>
    public void SelectAnchoredRange(CellAddress anchor, CellAddress cursor)
    {
        var range = new GridRange(anchor, cursor);
        ValidateSelectionRange(range, nameof(anchor));
        SetSingleSelectedRange(range);
        ActiveCell = anchor;
        ActiveSheet.ActiveRow = anchor.Row;
        ActiveSheet.ActiveCol = anchor.Col;
        FormulaEditAddress = null;
        // Follow the moving end of the gesture, not the (stationary) anchor -- otherwise an upward /
        // leftward shift-extend would stop scrolling to reveal the cursor once it left the viewport.
        EnsureCellVisible(cursor);
    }

    /// <summary>
    /// Moves the active cell to <paramref name="address"/> WITHOUT touching <see cref="SelectedRange"/>
    /// or <see cref="SelectedRanges"/> -- unlike <see cref="SelectCell"/>, which always collapses the
    /// selection down to a single cell. Used by Enter/Tab active-cell-cycling-within-a-selection
    /// (R78-render-selection-namebox-5-2): when a multi-cell range is already selected, Excel moves
    /// the active cell within it (wrapping at the range's edges) while keeping the whole range
    /// highlighted, mirroring the WPF host's MoveActiveCellWithinSelection (MainWindow.Selection.cs).
    /// </summary>
    public void MoveActiveCellWithinSelection(CellAddress address)
    {
        if (!IsValidAddress(address))
            throw new ArgumentOutOfRangeException(nameof(address), "Active cell must be inside the worksheet bounds.");

        ActiveCell = address;
        ActiveSheet.ActiveRow = address.Row;
        ActiveSheet.ActiveCol = address.Col;
        EnsureActiveCellVisible();
    }

    private void ValidateSelectionRange(GridRange range, string paramName)
    {
        if (!range.Start.Sheet.Equals(ActiveSheet.Id))
            throw new ArgumentException("Selected range must be on the active sheet.", paramName);
        if (!range.End.Sheet.Equals(ActiveSheet.Id))
            throw new ArgumentException("Selected range must be on the active sheet.", paramName);
        if (!IsValidAddress(range.Start) || !IsValidAddress(range.End))
            throw new ArgumentOutOfRangeException(paramName, "Selected range must be inside the worksheet bounds.");
    }

    private void SetSingleSelectedRange(GridRange range) =>
        SetSelectedRanges(range, [range]);

    private void SetSelectedRanges(GridRange primaryRange, IReadOnlyList<GridRange> ranges)
    {
        SelectedRange = primaryRange;
        SelectedRanges = ranges.ToArray();
    }

    private void RememberActiveWorksheetSelection()
    {
        if (Workbook.GetSheet(ActiveSheet.Id) is null)
            return;

        IReadOnlyList<GridRange> ranges = SelectedRanges.Count == 0
            ? new[] { SelectedRange }
            : SelectedRanges.ToArray();
        _worksheetSelections.Save(
            ActiveSheet.Id,
            new WorksheetSelectionSnapshot(
                ActiveCell,
                SelectedRange.End,
                SelectedRange,
                ranges));
    }

    private bool TryRestoreActiveWorksheetSelection()
    {
        if (!_worksheetSelections.TryGet(ActiveSheet.Id, out var snapshot) ||
            !IsSnapshotForSheet(snapshot, ActiveSheet.Id))
        {
            return false;
        }

        IReadOnlyList<GridRange> ranges = snapshot.AdditionalRanges is { Count: > 0 }
            ? snapshot.AdditionalRanges
            : new[] { snapshot.PrimaryRange };
        ActiveCell = snapshot.Anchor;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        SetSelectedRanges(snapshot.PrimaryRange, ranges);
        return true;
    }

    private static bool IsSnapshotForSheet(WorksheetSelectionSnapshot snapshot, SheetId sheetId)
    {
        if (!snapshot.Anchor.Sheet.Equals(sheetId) ||
            !snapshot.Cursor.Sheet.Equals(sheetId) ||
            !snapshot.PrimaryRange.Start.Sheet.Equals(sheetId) ||
            !snapshot.PrimaryRange.End.Sheet.Equals(sheetId))
        {
            return false;
        }

        return snapshot.AdditionalRanges is null ||
            snapshot.AdditionalRanges.All(range =>
                range.Start.Sheet.Equals(sheetId) &&
                range.End.Sheet.Equals(sheetId));
    }

    public GridRange SelectCurrentRegionOrAll()
    {
        if (SelectionRangeService.GetCurrentRegion(ActiveSheet, ActiveCell) is { } currentRegion &&
            SelectedRange != currentRegion)
        {
            SelectRange(currentRegion);
            return currentRegion;
        }

        var wholeSheet = new GridRange(
            new CellAddress(ActiveSheet.Id, 1, 1),
            new CellAddress(ActiveSheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));
        SelectRange(wholeSheet);
        return wholeSheet;
    }

    public WorkbookNavigationResult GoToCell(CellAddress address) =>
        GoToRange(new GridRange(address, address));

    public WorkbookNavigationResult GoToRange(GridRange range) =>
        NavigateToRange(range);

    public WorkbookNavigationResult GoToReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return WorkbookNavigationResult.Failed("Reference is required.");

        if (!WorkbookReferenceNavigator.TryParseReferenceRange(
                reference,
                ActiveSheet.Id,
                ResolveSheetIdByName,
                Workbook.NamedRanges,
                out var range))
            return WorkbookNavigationResult.Failed("Reference is not valid.");

        return GoToRange(range);
    }

    /// <summary>
    /// Resolves an A1 reference / named range to a <see cref="GridRange"/> on the active sheet without
    /// changing the selection. Used by dialogs (e.g. conditional-format applies-to editing) that need to
    /// parse a reference the same way Go To does.
    /// </summary>
    public bool TryResolveReferenceRange(string reference, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        return WorkbookReferenceNavigator.TryParseReferenceRange(
            reference,
            ActiveSheet.Id,
            ResolveSheetIdByName,
            Workbook.NamedRanges,
            out range);
    }

    // R112-model-active-cell-vs-selection-1-1 sibling fix: read ActiveCell, NOT SelectedRange.Start.
    // A selection made upward/leftward (e.g. drag from D4 up to A1) pins ActiveCell to D4 while
    // SelectedRange.Start normalizes to A1 -- Excel opens the hyperlink under the ACTIVE cell, not
    // the selection's normalized top-left corner. See the ribbon-toggle-state precedent a few
    // members above (IsSelectedRangeStartBold etc.) for the same ActiveCell-vs-Start distinction.
    public bool CanOpenSelectedHyperlink =>
        HyperlinkNavigationPlanner.TryCreatePlan(ActiveSheet, ActiveCell, CurrentFilePath, out _);

    public bool TryGetSelectedHyperlinkPlan(out HyperlinkNavigationPlan? plan) =>
        TryGetHyperlinkPlan(ActiveCell, out plan);

    public bool TryGetHyperlinkPlan(CellAddress address, out HyperlinkNavigationPlan? plan) =>
        HyperlinkNavigationPlanner.TryCreatePlan(ActiveSheet, address, CurrentFilePath, out plan);

    public WorkbookNavigationResult OpenSelectedHyperlink() =>
        OpenHyperlink(ActiveCell);

    public WorkbookNavigationResult OpenHyperlink(CellAddress address)
    {
        if (!HyperlinkNavigationPlanner.TryCreatePlan(ActiveSheet, address, CurrentFilePath, out var plan) || plan is null)
            return WorkbookNavigationResult.Failed("Hyperlink target was not found.");

        return plan.Kind switch
        {
            HyperlinkNavigationKind.WorksheetCell => GoToReference(plan.Target),
            HyperlinkNavigationKind.LocalFile =>
                WorkbookNavigationResult.Failed("Local file hyperlinks require a platform file-opening route."),
            _ => WorkbookNavigationResult.Failed("External hyperlinks are not supported on this platform.")
        };
    }

    public WorkbookGoToSpecialResult GoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)
    {
        // CurrentRegion/Precedents/Dependents trace relationships from the user's true active
        // cell/selection, not the (possibly auto-expanded-to-used-range) content search range;
        // otherwise Precedents/Dependents would trace the whole used range instead of just the
        // cell (or cells) the user actually selected. Mirrors the WPF host's
        // SelectGoToSpecialMatches guard.
        var trueSelection = SelectedRange;
        var searchRange = kind is GoToSpecialKind.CurrentRegion or GoToSpecialKind.Precedents or GoToSpecialKind.Dependents
            ? trueSelection
            : ResolveGoToSpecialSearchRange();
        var matches = GoToSpecialService.Find(Workbook, ActiveSheet, searchRange, kind, ActiveCell, options);
        if (matches.Count == 0)
            return WorkbookGoToSpecialResult.Failed("No cells found.");

        var ranges = SelectionRangeService.CompressAddresses(matches);
        var selectedRange = ranges[0];
        SelectRanges(selectedRange, ranges);
        return WorkbookGoToSpecialResult.Selected(selectedRange, ranges, matches.Count);
    }

    /// <summary>
    /// Determines the range that Go To Special's content-search kinds (Constants/Blanks/Formulas/
    /// etc.) should search. Matching Excel (and the WPF host's ResolveGoToSpecialSearchRange), a
    /// single active cell (the ordinary result of clicking one cell) searches the whole used range
    /// of the sheet; an explicit multi-cell selection is honored as-is. CurrentRegion/Precedents/
    /// Dependents kinds bypass this expansion entirely and use the true selection instead (see
    /// GoToSpecial above).
    /// </summary>
    private GridRange ResolveGoToSpecialSearchRange()
    {
        var selected = SelectedRange;
        if (selected.Start != selected.End)
            return selected;

        return ActiveSheet.GetUsedRange() ?? selected;
    }

    public ReviewWorkflowPlan GetReviewWorkflowPlan(
        IReadOnlySet<string>? customDictionary = null,
        ISet<string>? ignoredWords = null,
        ISet<SpellingIssueKey>? ignoredIssues = null) =>
        ReviewWorkflowPlanner.CreatePlan(
            Workbook,
            ActiveSheet.Id,
            customDictionary,
            ignoredWords,
            ignoredIssues);

    public WorkbookNavigationResult GoToNextNote(bool previous = false) =>
        GoToReviewNavigationPlan(ReviewWorkflowPlanner.FindNextNote(ActiveSheet, ActiveCell, previous));

    public WorkbookNavigationResult GoToNextThreadedComment(bool previous = false) =>
        GoToReviewNavigationPlan(ReviewWorkflowPlanner.FindNextThreadedComment(ActiveSheet, ActiveCell, previous));

    public WorkbookNavigationResult GoToAccessibilityIssue(AccessibilityIssue issue) =>
        GoToCell(ReviewWorkflowPlanner.GetAccessibilityNavigationTarget(issue));

    public WorkbookCellEditResult ExecuteReviewCommand(IWorkbookCommand command, CellAddress? fallbackAddress = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success || result.IsNoOp)
            return result;

        ApplySuccessfulEditResult(result, fallbackAddress ?? ActiveCell);
        // R127B-services-clipboard-structural-cancel-1: Insert/Delete Rows/Columns/Cells reach
        // the model exclusively through this generic executor on the Avalonia shell (the WPF host
        // has a dedicated TryExecuteRepeatableCurrentRangeCommand path instead), so a type-scoped
        // check here -- rather than one call at every UI call site -- is the only choke point that
        // actually covers all of them, including the Ribbon's multi-area Insert/Delete Sheet
        // Rows/Columns (a CompositeWorkbookCommand of per-area structural commands) and the
        // worksheet context menu's single-row/column insert, none of which cancelled the pending
        // Copy/Cut snapshot before this fix. See IsStructuralCellShiftCommand for the exact family;
        // matches the WPF host's ClearClipboardMarqueeAfterStructuralEdit (MainWindow.CellsCommands
        // .cs), which is called unconditionally on success from its Insert/Delete Rows/Columns/Cells
        // handlers -- deliberately NOT from its own generic executor (TryExecuteCommand), which is
        // shared by many unrelated command kinds that must NOT cancel the clipboard on every use.
        if (IsStructuralCellShiftCommand(command))
            CancelPendingCutAfterMutatingEdit();
        return result;
    }

    public QuickAnalysisWorkbookOperationResult ExecuteQuickAnalysisTotal(
        QuickAnalysisHostOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var range = SelectedRange;
        var title = operation.TotalCommandTitle ?? "Quick Analysis Total";
        if (operation.Kind is not (
                QuickAnalysisHostOperationKind.InsertAggregateTotalFormula or
                QuickAnalysisHostOperationKind.InsertPercentTotalFormula or
                QuickAnalysisHostOperationKind.InsertRunningTotalFormula) ||
            !QuickAnalysisHostOperationPlanner.TryBuildTotalFormulaEdits(operation, range, out var edits))
        {
            return QuickAnalysisOperationFailure(
                range,
                title,
                QuickAnalysisWorkbookOperationFailure.InvalidOperation,
                "The Quick Analysis operation is not a total formula operation.");
        }

        var result = ExecuteRepeatableCommandPreservingSelection(
            () => new EditCellsCommand(ActiveSheet.Id, edits));
        if (!result.Success || result.IsNoOp)
        {
            return new QuickAnalysisWorkbookOperationResult(
                result,
                result.Success
                    ? QuickAnalysisWorkbookOperationFailure.None
                    : QuickAnalysisWorkbookOperationFailure.CommandFailed,
                AppliedItemCount: 0,
                range,
                SelectedCell: null,
                title);
        }

        var selectedCell = edits[^1].Address;
        SelectCell(selectedCell);
        return new QuickAnalysisWorkbookOperationResult(
            result,
            QuickAnalysisWorkbookOperationFailure.None,
            edits.Count,
            range,
            selectedCell,
            title);
    }

    public QuickAnalysisWorkbookOperationResult ExecuteQuickAnalysisSparklines(
        QuickAnalysisHostOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var range = SelectedRange;
        const string title = "Quick Analysis Sparklines";
        if (!QuickAnalysisHostOperationPlanner.TryBuildSparklineCommands(
                operation,
                ActiveSheet,
                range,
                out var commands))
        {
            return QuickAnalysisOperationFailure(
                range,
                title,
                QuickAnalysisWorkbookOperationFailure.InvalidSparklineSelection,
                "Quick Analysis sparklines require a supported multi-column selection.");
        }

        var appliedCount = 0;
        foreach (var command in commands)
        {
            var result = ExecuteReviewCommand(command);
            if (!result.Success)
            {
                return new QuickAnalysisWorkbookOperationResult(
                    result,
                    QuickAnalysisWorkbookOperationFailure.CommandFailed,
                    appliedCount,
                    range,
                    SelectedCell: null,
                    title);
            }

            if (!result.IsNoOp)
                appliedCount++;
        }

        return new QuickAnalysisWorkbookOperationResult(
            new WorkbookCellEditResult(
                Success: true,
                ErrorMessage: null,
                AffectedCells: [],
                RecalcReport: null,
                IsNoOp: appliedCount == 0),
            QuickAnalysisWorkbookOperationFailure.None,
            appliedCount,
            range,
            SelectedCell: null,
            title);
    }

    private static QuickAnalysisWorkbookOperationResult QuickAnalysisOperationFailure(
        GridRange range,
        string title,
        QuickAnalysisWorkbookOperationFailure failure,
        string errorMessage) =>
        new(
            new WorkbookCellEditResult(
                Success: false,
                ErrorMessage: errorMessage,
                AffectedCells: [],
                RecalcReport: null),
            failure,
            AppliedItemCount: 0,
            range,
            SelectedCell: null,
            title);

    /// <summary>
    /// Executes an undoable workbook command while preserving the renderer-synchronized cell
    /// selection. The session owns dependency maintenance, calculation policy, dirty state,
    /// linked-picture refresh, and structural-sheet fallback; the renderer remains responsible
    /// for command construction and native UI aftermath.
    /// </summary>
    public WorkbookCellEditResult ExecuteCommandPreservingSelection(IWorkbookCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sheetIdsBefore = CaptureSheetIds();
        var hiddenStatesBefore = CaptureSheetHiddenStates();
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        ApplySuccessfulPreservedSelectionCommandResult(result, sheetIdsBefore, hiddenStatesBefore);
        return result;
    }

    /// <summary>
    /// Executes a Custom Views command through the session-owned command history. Save/Delete
    /// preserve the renderer-synchronized selection; Apply adopts the active sheet/cell restored
    /// by the saved view instead of overwriting it with the pre-command selection.
    /// </summary>
    public WorkbookCellEditResult ExecuteCustomViewCommand(IWorkbookCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is not ApplyCustomViewCommand)
            return ExecuteCommandPreservingSelection(command);

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success || result.IsNoOp)
            return result;

        var activeSheet = Workbook.ActiveSheetIndex is { } index &&
            index >= 0 &&
            index < Workbook.Sheets.Count &&
            !Workbook.Sheets[index].IsHidden &&
            !Workbook.Sheets[index].IsVeryHidden
                ? Workbook.Sheets[index]
                : ActiveSheet;

        if (ActiveSheet.Id != activeSheet.Id)
            RememberActiveWorksheetSelection();

        var selection = _sheetSelectionService.SelectSheet(Workbook, activeSheet.Id, _groupedSheetIds);
        ActiveSheet = selection.Sheet;
        SelectSingleSheetGroup(ActiveSheet.Id);
        RefreshSheetTabsForActiveSheet();

        ActiveCell = new CellAddress(
            ActiveSheet.Id,
            Math.Clamp(ActiveSheet.ActiveRow ?? 1u, 1u, CellAddress.MaxRow),
            Math.Clamp(ActiveSheet.ActiveCol ?? 1u, 1u, CellAddress.MaxCol));
        SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
        FormulaEditAddress = null;
        RefreshLinkedPicturesForEditedCells(result);
        MarkDirty();
        _selectionStatsRevision++;
        foreach (var sheet in Workbook.Sheets)
            InvalidateAllPerViewOverridesForSheet(sheet.Id);
        RefreshViewport();
        EnsureActiveCellVisible();
        return result;
    }

    /// <summary>
    /// Executes and records a repeatable workbook command while preserving the current selection.
    /// The factory is re-evaluated by Repeat Last Action, so it may resolve live renderer state.
    /// </summary>
    public WorkbookCellEditResult ExecuteRepeatableCommandPreservingSelection(
        Func<IWorkbookCommand> commandFactory)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);

        var sheetIdsBefore = CaptureSheetIds();
        var hiddenStatesBefore = CaptureSheetHiddenStates();
        var result = _cellEditService.ExecuteRepeatableEditCommand(Workbook, commandFactory);
        ApplySuccessfulPreservedSelectionCommandResult(result, sheetIdsBefore, hiddenStatesBefore);
        return result;
    }

    // Worksheet structure edits

    public WorkbookWorksheetStructureResult InsertSelectedCells(InsertCellsShiftDirection direction) =>
        ExecuteSelectedWorksheetStructureOperation(direction == InsertCellsShiftDirection.Right
            ? WorkbookWorksheetStructureOperation.InsertCellsShiftRight
            : WorkbookWorksheetStructureOperation.InsertCellsShiftDown);

    public WorkbookWorksheetStructureResult DeleteSelectedCells(DeleteCellsShiftDirection direction) =>
        ExecuteSelectedWorksheetStructureOperation(direction == DeleteCellsShiftDirection.Left
            ? WorkbookWorksheetStructureOperation.DeleteCellsShiftLeft
            : WorkbookWorksheetStructureOperation.DeleteCellsShiftUp);

    public WorkbookWorksheetStructureResult InsertSelectedRows() =>
        ExecuteSelectedWorksheetStructureOperation(WorkbookWorksheetStructureOperation.InsertRows);

    public WorkbookWorksheetStructureResult InsertSelectedColumns() =>
        ExecuteSelectedWorksheetStructureOperation(WorkbookWorksheetStructureOperation.InsertColumns);

    public WorkbookWorksheetStructureResult DeleteSelectedRows() =>
        ExecuteSelectedWorksheetStructureOperation(WorkbookWorksheetStructureOperation.DeleteRows);

    public WorkbookWorksheetStructureResult DeleteSelectedColumns() =>
        ExecuteSelectedWorksheetStructureOperation(WorkbookWorksheetStructureOperation.DeleteColumns);

    public WorkbookWorksheetStructureResult InsertRows(uint beforeRow, uint count = 1) =>
        ExecuteExplicitWorksheetStructureOperation(
            WorkbookWorksheetStructureOperation.InsertRows,
            CreateWholeRowRange(beforeRow, count));

    public WorkbookWorksheetStructureResult InsertColumns(uint beforeColumn, uint count = 1) =>
        ExecuteExplicitWorksheetStructureOperation(
            WorkbookWorksheetStructureOperation.InsertColumns,
            CreateWholeColumnRange(beforeColumn, count));

    public WorkbookWorksheetStructureResult DeleteRows(uint startRow, uint count = 1) =>
        ExecuteExplicitWorksheetStructureOperation(
            WorkbookWorksheetStructureOperation.DeleteRows,
            CreateWholeRowRange(startRow, count));

    public WorkbookWorksheetStructureResult DeleteColumns(uint startColumn, uint count = 1) =>
        ExecuteExplicitWorksheetStructureOperation(
            WorkbookWorksheetStructureOperation.DeleteColumns,
            CreateWholeColumnRange(startColumn, count));

    private WorkbookWorksheetStructureResult ExecuteSelectedWorksheetStructureOperation(
        WorkbookWorksheetStructureOperation operation)
    {
        var targetRange = SelectedRange;
        var title = WorkbookWorksheetStructureResult.GetCommandTitle(operation);
        var result = ExecuteRepeatableCommandPreservingSelection(
            () => CreateGroupedSelectionRangeCommand(
                title,
                OrderStructuralRanges(operation, GetSelectionSizingRanges()),
                (sheetId, range) => CreateWorksheetStructureCommand(operation, range, sheetId)));
        return new WorkbookWorksheetStructureResult(result, operation, targetRange);
    }

    private static IReadOnlyList<GridRange> OrderStructuralRanges(
        WorkbookWorksheetStructureOperation operation,
        IReadOnlyList<GridRange> ranges) =>
        operation is WorkbookWorksheetStructureOperation.InsertRows or
            WorkbookWorksheetStructureOperation.DeleteRows or
            WorkbookWorksheetStructureOperation.InsertCellsShiftDown or
            WorkbookWorksheetStructureOperation.DeleteCellsShiftUp
            ? ranges.OrderByDescending(range => range.Start.Row)
                .ThenByDescending(range => range.Start.Col)
                .ToArray()
            : ranges.OrderByDescending(range => range.Start.Col)
                .ThenByDescending(range => range.Start.Row)
                .ToArray();

    private WorkbookWorksheetStructureResult ExecuteExplicitWorksheetStructureOperation(
        WorkbookWorksheetStructureOperation operation,
        GridRange targetRange)
    {
        var result = ExecuteRepeatableCommandPreservingSelection(
            () => CreateWorksheetStructureCommand(
                operation,
                RemapRangeToSheet(targetRange, ActiveSheet.Id)));
        return new WorkbookWorksheetStructureResult(result, operation, targetRange);
    }

    private IWorkbookCommand CreateWorksheetStructureCommand(
        WorkbookWorksheetStructureOperation operation,
        GridRange range)
    {
        var title = WorkbookWorksheetStructureResult.GetCommandTitle(operation);
        return CreateGroupedSheetCommand(
            title,
            sheetId => CreateWorksheetStructureCommand(
                operation,
                RemapRangeToSheet(range, sheetId),
                sheetId));
    }

    private static IWorkbookCommand CreateWorksheetStructureCommand(
        WorkbookWorksheetStructureOperation operation,
        GridRange range,
        SheetId sheetId) =>
        operation switch
        {
            WorkbookWorksheetStructureOperation.InsertCellsShiftRight =>
                new InsertCellsCommand(sheetId, range, InsertCellsShiftDirection.Right),
            WorkbookWorksheetStructureOperation.InsertCellsShiftDown =>
                new InsertCellsCommand(sheetId, range, InsertCellsShiftDirection.Down),
            WorkbookWorksheetStructureOperation.InsertRows =>
                new InsertRowsCommand(sheetId, range.Start.Row, range.RowCount),
            WorkbookWorksheetStructureOperation.InsertColumns =>
                new InsertColumnsCommand(sheetId, range.Start.Col, range.ColCount),
            WorkbookWorksheetStructureOperation.DeleteCellsShiftLeft =>
                new DeleteCellsCommand(sheetId, range, DeleteCellsShiftDirection.Left),
            WorkbookWorksheetStructureOperation.DeleteCellsShiftUp =>
                new DeleteCellsCommand(sheetId, range, DeleteCellsShiftDirection.Up),
            WorkbookWorksheetStructureOperation.DeleteRows =>
                new DeleteRowsCommand(sheetId, range.Start.Row, range.RowCount),
            _ => new DeleteColumnsCommand(sheetId, range.Start.Col, range.ColCount),
        };

    private GridRange CreateWholeRowRange(uint startRow, uint count)
    {
        ArgumentOutOfRangeException.ThrowIfZero(count);
        var endRow = checked(startRow + count - 1);
        return new GridRange(
            new CellAddress(ActiveSheet.Id, startRow, 1),
            new CellAddress(ActiveSheet.Id, endRow, CellAddress.MaxCol));
    }

    private GridRange CreateWholeColumnRange(uint startColumn, uint count)
    {
        ArgumentOutOfRangeException.ThrowIfZero(count);
        var endColumn = checked(startColumn + count - 1);
        return new GridRange(
            new CellAddress(ActiveSheet.Id, 1, startColumn),
            new CellAddress(ActiveSheet.Id, CellAddress.MaxRow, endColumn));
    }
    /// <summary>
    /// True for the Insert/Delete Rows/Columns/Cells family (and a composite made entirely of
    /// them, for multi-area edits) that must retire a pending Copy/Cut the same way an ordinary
    /// cell edit already does -- see <see cref="ExecuteReviewCommand"/> and
    /// <see cref="CancelPendingCutAfterMutatingEdit"/>. Deliberately excludes every other command
    /// ExecuteReviewCommand runs (formatting, comments, charts, pivot, protection, ...), which
    /// Excel leaves the active Copy/Cut marquee alone for.
    /// </summary>
    private static bool IsStructuralCellShiftCommand(IWorkbookCommand command) => command switch
    {
        InsertRowsCommand or InsertColumnsCommand or InsertCellsCommand or
        DeleteRowsCommand or DeleteColumnsCommand or DeleteCellsCommand => true,
        CompositeWorkbookCommand composite => composite.Commands.Count > 0 &&
            composite.Commands.All(IsStructuralCellShiftCommand),
        _ => false,
    };

    public WorkbookGoalSeekResult ExecuteGoalSeek(GoalSeekRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = _cellEditService.ExecuteGoalSeek(Workbook, request);
        if (!result.Success || result.EditResult is null)
            return result;

        ApplySuccessfulEditResult(result.EditResult, request.ChangingCell);
        return result;
    }

    /// <summary>Calculates a Goal Seek proposal without applying it, for hosts with a confirmation step.</summary>
    public GoalSeekResult FindGoalSeekSolution(GoalSeekRequest request) =>
        _cellEditService.FindGoalSeekSolution(Workbook, request);

    /// <summary>Validates and calculates a Goal Seek proposal without applying it.</summary>
    public WorkbookGoalSeekProposal FindGoalSeekProposal(GoalSeekRequest request) =>
        _cellEditService.FindGoalSeekProposal(Workbook, request);

    public WorkbookCellEditResult ExecuteDataTablePlan(DataTablePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var result = _cellEditService.ExecuteEditCommand(Workbook, plan.CreateCommand());
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, plan.OutputRange);
        return result;
    }

    public WorkbookCellEditResult ExecuteAdvancedFilterPlan(AdvancedFilterPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var result = _cellEditService.ExecuteEditCommand(Workbook, plan.CreateCommand());
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, GetAdvancedFilterSelectedRange(plan));
        return result;
    }

    public WorkbookRemoveDuplicatesResult ExecuteRemoveDuplicatesPlan(RemoveDuplicatesPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.SelectedColumnOffsets.Count == 0)
        {
            var invalidResult = new WorkbookCellEditResult(
                false,
                "Select at least one column.",
                [],
                RecalcReport: null);
            return new WorkbookRemoveDuplicatesResult(
                false,
                invalidResult.ErrorMessage,
                RemovedRowCount: 0,
                invalidResult);
        }

        RemoveDuplicateRowsCommand? activeSheetCommand = null;
        var command = CreateGroupedSheetCommand(
            "Remove Duplicates",
            sheetId =>
            {
                var removeCommand = plan.CreateCommand(sheetId);
                if (sheetId == ActiveSheet.Id)
                    activeSheetCommand = removeCommand;

                return removeCommand;
            });

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return new WorkbookRemoveDuplicatesResult(false, result.ErrorMessage, RemovedRowCount: 0, result);

        ApplySuccessfulRangeEditResult(result, plan.SourceRange);
        return new WorkbookRemoveDuplicatesResult(
            true,
            null,
            activeSheetCommand?.RemovedRowCount ?? 0,
            result);
    }

    public WorkbookCellEditResult ExecuteSubtotalOptions(SubtotalInputOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!SubtotalPlanner.TryCreateSourceRange(ActiveSheet, SelectedRange, out var range, out var sourceRangeError))
            return new WorkbookCellEditResult(false, sourceRangeError, [], RecalcReport: null);

        var command = CreateGroupedSheetCommand(
            options.ReplaceExisting ? "Replace Subtotals" : "Subtotal",
            sheetId =>
            {
                var sheetRange = RemapRangeToSheet(range, sheetId);
                var subtotalCommand = new SubtotalCommand(
                    sheetId,
                    sheetRange,
                    options.GroupColumnOffset,
                    options.SubtotalColumnOffsets,
                    options.FunctionNumber,
                    options.PageBreakBetweenGroups,
                    options.SummaryBelowData);
                return options.ReplaceExisting
                    ? new ReplaceSubtotalRowsCommand(
                        sheetId,
                        sheetRange,
                        options.GroupColumnOffset,
                        options.SubtotalColumnOffsets,
                        options.FunctionNumber,
                        options.PageBreakBetweenGroups,
                        options.SummaryBelowData)
                    : subtotalCommand;
            });

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(
            result,
            SubtotalPlanner.ExpandRangeForInsertedSubtotalRows(range, result.AffectedCells));
        return result;
    }

    public WorkbookCellEditResult RemoveSelectedRangeSubtotals()
    {
        var range = SubtotalPlanner.NormalizeSourceRange(ActiveSheet, SelectedRange);
        var command = CreateGroupedSheetCommand(
            "Remove Subtotals",
            sheetId =>
            {
                var sheetRange = RemapRangeToSheet(range, sheetId);
                return new RemoveSubtotalRowsCommand(sheetId, sheetRange);
            });

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult ExecuteForecastSheetPlan(ForecastSheetPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.TryCreateCommand() is not { } command)
        {
            return new WorkbookCellEditResult(
                false,
                plan.StatusText,
                [],
                RecalcReport: null);
        }

        var sheetIdsBefore = CaptureSheetIds();
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulForecastSheetResult(result, sheetIdsBefore, plan);
        return result;
    }

    public WorkbookCellEditResult SaveScenario(ScenarioManagerSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteScenarioManagerSavePlan(
            ScenarioManagerPlanner.CreateSavePlan(Workbook, request),
            request);
    }

    public WorkbookCellEditResult ShowScenario(string? scenarioName) =>
        ExecuteScenarioManagerShowPlan(ScenarioManagerPlanner.CreateShowPlan(Workbook, scenarioName));

    public WorkbookCellEditResult DeleteScenario(string? scenarioName) =>
        ExecuteScenarioManagerDeletePlan(ScenarioManagerPlanner.CreateDeletePlan(Workbook, scenarioName));

    public WorkbookCellEditResult CreateScenarioSummaryReport(IReadOnlyList<CellAddress>? resultCells = null) =>
        ExecuteScenarioManagerSummaryReportPlan(ScenarioManagerPlanner.CreateSummaryReportPlan(Workbook, resultCells));

    public WorkbookCellEditResult ExecuteScenarioManagerSavePlan(
        ScenarioManagerPlan plan,
        ScenarioManagerSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);

        if (ValidateScenarioManagerPlan(plan, ScenarioManagerOperation.Save) is { } validationResult)
            return validationResult;

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SaveScenarioCommand(
                request.Name,
                request.ChangingCells,
                request.Comment,
                request.Hidden,
                request.Locked,
                request.ReplaceScenarioName));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    public WorkbookCellEditResult ExecuteScenarioManagerShowPlan(ScenarioManagerPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (ValidateScenarioManagerPlan(plan, ScenarioManagerOperation.Show) is { } validationResult)
            return validationResult;
        if (plan.SelectedScenario is null)
            return FailedScenarioManagerResult("Select a scenario before continuing.");

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ApplyScenarioCommand(plan.SelectedScenario.Name));
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, FirstAffectedCellOrDefault(plan.AffectedCells, ActiveCell));
        return result;
    }

    public WorkbookCellEditResult ExecuteScenarioManagerDeletePlan(ScenarioManagerPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (ValidateScenarioManagerPlan(plan, ScenarioManagerOperation.Delete) is { } validationResult)
            return validationResult;
        if (plan.SelectedScenario is null)
            return FailedScenarioManagerResult("Select a scenario before continuing.");

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new DeleteScenarioCommand(plan.SelectedScenario.Name));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    public WorkbookCellEditResult ExecuteScenarioManagerSummaryReportPlan(ScenarioManagerPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (ValidateScenarioManagerPlan(plan, ScenarioManagerOperation.SummaryReport) is { } validationResult)
            return validationResult;

        var sheetIdsBefore = CaptureSheetIds();
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ScenarioSummaryReportCommand(
                plan.ResultCells,
                // Always recalculate here, independent of the workbook's calculation mode: the
                // summary report's whole purpose is to show each scenario's distinct computed
                // result, so Manual mode must not leave every scenario column reading the same
                // stale pre-report value (Excel's own Scenario Summary always computes fresh
                // per-scenario results).
                (workbook, changedCells) => _cellEditService.RecalculateAlways(workbook, changedCells)));
        if (!result.Success)
            return result;

        ApplySuccessfulHistoryResult(result, sheetIdsBefore);
        return result;
    }

    public WorkbookNavigationResult FindNext(
        string? searchText = null,
        FindOptions? options = null,
        bool matchCase = false,
        bool matchEntireCell = false)
    {
        var result = _findReplaceWorkflow.FindNext(searchText, options, matchCase, matchEntireCell);
        if (!result.Success || result.SelectedMatch is not { } match || result.SelectedRange is null)
            return WorkbookNavigationResult.Failed(result.ErrorMessage ?? "Find failed.");

        return WorkbookNavigationResult.Found(
            result.SelectedRange.Value,
            match.MatchedText,
            result.SelectedIndex + 1,
            result.Matches.Count);
    }

    public WorkbookFindAllResult FindAll(
        string searchText,
        FindOptions? options = null,
        bool matchCase = false,
        bool matchEntireCell = false)
    {
        var result = _findReplaceWorkflow.FindAll(searchText, options, matchCase, matchEntireCell);
        return result.Success
            ? WorkbookFindAllResult.Found(result.Matches.Select(CreateFindAllMatch).ToList())
            : WorkbookFindAllResult.Failed(result.ErrorMessage ?? "Find All failed.");
    }

    public WorkbookReplaceResult ReplaceAllValues(
        string searchText,
        string replaceText,
        bool matchCase = false,
        bool matchEntireCell = false) =>
        ReplaceAllValues(searchText, replaceText, options: null, matchCase, matchEntireCell);

    public WorkbookReplaceResult ReplaceAllValues(
        string searchText,
        string replaceText,
        FindOptions? options,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
    {
        var selectedRange = SelectedRange;
        var result = _findReplaceWorkflow.ReplaceAll(
            searchText,
            replaceText,
            options,
            matchCase,
            matchEntireCell,
            replacementFormat);
        if (!result.Success)
            return WorkbookReplaceResult.Failed(result.ErrorMessage ?? "Replace All failed.");
        if (result.EditResult is { } editResult)
            ApplySuccessfulRangeEditResult(editResult, selectedRange);

        return WorkbookReplaceResult.Replaced(
            result.ReplacedCount,
            matchCount: result.MatchCount);
    }

    public WorkbookReplaceResult ReplaceNextValue(
        string searchText,
        string replaceText,
        bool matchCase = false,
        bool matchEntireCell = false) =>
        ReplaceNextValue(searchText, replaceText, options: null, matchCase, matchEntireCell);

    public WorkbookReplaceResult ReplaceNextValue(
        string searchText,
        string replaceText,
        FindOptions? options,
        bool matchCase = false,
        bool matchEntireCell = false,
        StyleDiff? replacementFormat = null)
    {
        var result = _findReplaceWorkflow.ReplaceNext(
            searchText,
            replaceText,
            options,
            matchCase,
            matchEntireCell,
            replacementFormat);
        if (!result.Success)
            return WorkbookReplaceResult.Failed(result.ErrorMessage ?? "Replace failed.");
        if (result.EditResult is { } editResult && result.ReplacedMatch is { } replacedMatch)
            ApplySuccessfulEditResult(editResult, replacedMatch.Address);

        var replacedRange = result.ReplacedMatch is { } match
            ? new GridRange(match.Address, match.Address)
            : result.SelectedRange;
        return WorkbookReplaceResult.Replaced(
            result.ReplacedCount,
            replacedRange,
            result.MatchIndex,
            result.MatchCount);
    }

    public void MoveActiveCell(int rowDelta, int colDelta)
    {
        var address = new CellAddress(
            ActiveSheet.Id,
            Offset(ActiveCell.Row, rowDelta, CellAddress.MaxRow),
            Offset(ActiveCell.Col, colDelta, CellAddress.MaxCol));
        SelectCell(address);
    }

    public bool PanViewport(int rowDelta, int colDelta)
    {
        var nextTopRow = Offset(GetViewTopRow(), rowDelta, CellAddress.MaxRow);
        var nextLeftCol = Offset(GetViewLeftCol(), colDelta, CellAddress.MaxCol);
        return SetViewportOrigin(nextTopRow, nextLeftCol);
    }

    public bool SetViewportOrigin(uint topRow, uint leftCol)
    {
        var normalizedTopRow = Math.Clamp(topRow, GetScrollableRowStart(), CellAddress.MaxRow);
        var normalizedLeftCol = Math.Clamp(leftCol, GetScrollableColumnStart(), CellAddress.MaxCol);
        var currentTopRow = GetViewTopRow();
        var currentLeftCol = GetViewLeftCol();
        if (normalizedTopRow == currentTopRow && normalizedLeftCol == currentLeftCol)
            return false;

        SetViewViewportOrigin(normalizedTopRow, normalizedLeftCol);
        RefreshViewport();
        return true;
    }

    /// <summary>Current scroll origin for this view; unlike the workbook's persisted sheet view, this is per-window.</summary>
    public (uint TopRow, uint LeftCol) ViewportOrigin => (GetViewTopRow(), GetViewLeftCol());

    /// <summary>
    /// True when this view's effective Window ▸ Split column boundary (see <see cref="GetEffectiveSplitCol"/>)
    /// is set, i.e. there is a TopRight pane that can scroll independently of the main (BottomRight) pane.
    /// </summary>
    public bool HasIndependentSplitPaneTopRight => GetEffectiveSplitCol() is not null;

    /// <summary>
    /// True when this view's effective Window ▸ Split row boundary (see <see cref="GetEffectiveSplitRow"/>)
    /// is set, i.e. there is a BottomLeft pane that can scroll independently of the main (BottomRight) pane.
    /// </summary>
    public bool HasIndependentSplitPaneBottomLeft => GetEffectiveSplitRow() is not null;

    /// <summary>
    /// This view's effective Window ▸ Split row boundary (see <see cref="_viewSplitRowOverrides"/> remarks):
    /// a pure peek at this view's own snapshot (seeded up front by <see cref="SeedViewSplitAndFrozenOverrides"/>,
    /// kept in sync by every Split-changing command apply/undo/redo), falling back to the shared
    /// <see cref="Sheet.SplitRow"/> only when this view has no snapshot at all -- so a sibling view's
    /// Split change never retroactively changes what this view shows/renders around, and merely reading
    /// this (e.g. from <see cref="BuildViewport"/> during an unrelated <see cref="RefreshViewport"/>)
    /// never itself freezes in a stale value.
    /// </summary>
    public uint? GetEffectiveSplitRow() =>
        _viewSplitRowOverrides.TryGetValue(ActiveSheet.Id, out var splitRow) ? splitRow : ActiveSheet.SplitRow;

    /// <summary>
    /// This view's effective Window ▸ Split column boundary. See <see cref="GetEffectiveSplitRow"/>.
    /// </summary>
    public uint? GetEffectiveSplitCol() =>
        _viewSplitColOverrides.TryGetValue(ActiveSheet.Id, out var splitCol) ? splitCol : ActiveSheet.SplitColumn;

    /// <summary>
    /// Scrolls the TopRight split-pane quadrant's columns independently of the main (BottomRight)
    /// pane's <see cref="ActiveSheet.ViewLeftCol"/>, matching Excel/the WPF host's
    /// TryScrollIndependentSplitPane. No-op when the active sheet has no column split.
    /// </summary>
    public bool ScrollSplitPaneTopRight(int colDelta) =>
        HasIndependentSplitPaneTopRight && SetSplitPaneTopRightLeftCol(
            Offset(GetSplitPaneTopRightLeftCol(), colDelta, CellAddress.MaxCol));

    /// <summary>
    /// Scrolls the BottomLeft split-pane quadrant's rows independently of the main (BottomRight)
    /// pane's <see cref="ActiveSheet.ViewTopRow"/>, matching Excel/the WPF host's
    /// TryScrollIndependentSplitPane. No-op when the active sheet has no row split.
    /// </summary>
    public bool ScrollSplitPaneBottomLeft(int rowDelta) =>
        HasIndependentSplitPaneBottomLeft && SetSplitPaneBottomLeftTopRow(
            Offset(GetSplitPaneBottomLeftTopRow(), rowDelta, CellAddress.MaxRow));

    /// <summary>Current first-visible column of the TopRight split-pane quadrant, defaulting to the main pane's when no independent offset has been recorded yet.</summary>
    public uint GetSplitPaneTopRightLeftCol() =>
        _splitPaneViewportOffsets.TryGetValue(ActiveSheet.Id, out var offsets) && offsets.TopRightLeftCol is { } leftCol
            ? leftCol
            : GetViewLeftCol();

    /// <summary>Current first-visible row of the BottomLeft split-pane quadrant, defaulting to the main pane's when no independent offset has been recorded yet.</summary>
    public uint GetSplitPaneBottomLeftTopRow() =>
        _splitPaneViewportOffsets.TryGetValue(ActiveSheet.Id, out var offsets) && offsets.BottomLeftTopRow is { } topRow
            ? topRow
            : GetViewTopRow();

    /// <summary>Sets the TopRight split-pane quadrant's first-visible column directly (e.g. from a dedicated scrollbar drag).</summary>
    public bool SetSplitPaneTopRightLeftCol(uint leftCol)
    {
        if (!HasIndependentSplitPaneTopRight)
            return false;

        var normalized = Math.Clamp(leftCol, 1, CellAddress.MaxCol);
        if (normalized == GetSplitPaneTopRightLeftCol())
            return false;

        var existing = _splitPaneViewportOffsets.TryGetValue(ActiveSheet.Id, out var offsets)
            ? offsets
            : new SplitPaneViewportOffsets();
        _splitPaneViewportOffsets[ActiveSheet.Id] = existing with { TopRightLeftCol = normalized };
        RefreshViewport();
        return true;
    }

    /// <summary>Sets the BottomLeft split-pane quadrant's first-visible row directly (e.g. from a dedicated scrollbar drag).</summary>
    public bool SetSplitPaneBottomLeftTopRow(uint topRow)
    {
        if (!HasIndependentSplitPaneBottomLeft)
            return false;

        var normalized = Math.Clamp(topRow, 1, CellAddress.MaxRow);
        if (normalized == GetSplitPaneBottomLeftTopRow())
            return false;

        var existing = _splitPaneViewportOffsets.TryGetValue(ActiveSheet.Id, out var offsets)
            ? offsets
            : new SplitPaneViewportOffsets();
        _splitPaneViewportOffsets[ActiveSheet.Id] = existing with { BottomLeftTopRow = normalized };
        RefreshViewport();
        return true;
    }

    /// <summary>
    /// Drops independent quadrant offsets after a split divider is moved or recreated. WPF clears
    /// these view-local offsets at the same boundary so the new split starts at its new anchor.
    /// </summary>
    public void ResetSplitPaneOffsets() => _splitPaneViewportOffsets.Remove(ActiveSheet.Id);

    public bool UpdateViewportSize(double viewportHeight, double viewportWidth)
    {
        var normalizedHeight = NormalizeViewportDimension(viewportHeight, _viewportHeight);
        var normalizedWidth = NormalizeViewportDimension(viewportWidth, _viewportWidth);
        if (normalizedHeight == _viewportHeight && normalizedWidth == _viewportWidth)
            return false;

        _viewportHeight = normalizedHeight;
        _viewportWidth = normalizedWidth;
        RefreshViewport();
        EnsureActiveCellVisible();
        return true;
    }

    public bool SelectSheet(SheetId sheetId)
        => SelectSheet(sheetId, selectRange: false, toggle: false);

    /// <summary>
    /// Activates another worksheet without ending the active formula point edit. The pointed
    /// worksheet owns the visible selection, while <see cref="FormulaEditAddress"/> remains the
    /// source cell that receives the eventual commit. This is the shared state transition used by
    /// both worksheet hosts when a formula edit crosses a sheet tab.
    /// </summary>
    public bool SelectSheetForFormulaEdit(SheetId sheetId)
        => SelectSheetForFormulaEdit(sheetId, selectRange: false, toggle: false);

    /// <summary>
    /// Selects a worksheet tab while keeping the active formula point edit alive. WPF keeps the
    /// source cell edit open even when Shift/Ctrl tab modifiers change the grouped-sheet state;
    /// the selected worksheet then owns the next pointed range while FormulaEditAddress remains
    /// the source cell that receives commit or cancel.
    /// </summary>
    public bool SelectSheetForFormulaEdit(SheetId sheetId, bool selectRange, bool toggle)
    {
        if (FormulaEditAddress is null)
            throw new InvalidOperationException("A formula edit must be active before switching sheets for formula pointing.");

        return SelectSheet(sheetId, selectRange, toggle, preserveFormulaEdit: true);
    }

    public bool SelectSheetFromTab(SheetId sheetId, bool selectRange, bool toggle)
        => SelectSheet(sheetId, selectRange, toggle);

    /// <summary>
    /// True when <paramref name="sheetId"/> is already part of the active multi-sheet group
    /// selection (i.e. right-clicking it should keep the group rather than collapse it to a
    /// single tab). Mirrors the WPF host's <c>_groupedSheetIds.Count &gt; 1 &amp;&amp;
    /// _groupedSheetIds.Contains(tab.Id)</c> check.
    /// </summary>
    public bool IsSheetInActiveGroupSelection(SheetId sheetId) =>
        _groupedSheetIds.Count > 1 && _groupedSheetIds.Contains(sheetId);

    /// <summary>
    /// Activates <paramref name="sheetId"/> as the current sheet WITHOUT touching the current
    /// multi-sheet group selection - used by sheet-tab context-menu commands so right-clicking a
    /// tab that is already part of an active group preserves the group instead of collapsing it
    /// to just the clicked tab (see F21). Callers must first confirm
    /// <see cref="IsSheetInActiveGroupSelection"/> for <paramref name="sheetId"/>.
    /// </summary>
    public bool SelectSheetPreservingGroup(SheetId sheetId)
    {
        var previousSheetId = ActiveSheet.Id;
        if (!previousSheetId.Equals(sheetId))
            RememberActiveWorksheetSelection();

        var selection = _sheetSelectionService.SelectSheet(Workbook, sheetId, _groupedSheetIds);
        var sheetChanged = previousSheetId != selection.Sheet.Id;

        ActiveSheet = selection.Sheet;
        _sheetGroupAnchor = sheetId;
        RefreshSheetTabsForActiveSheet();
        FormulaEditAddress = null;

        if (sheetChanged)
        {
            if (!TryRestoreActiveWorksheetSelection())
            {
                ActiveCell = GetInitialActiveCell(ActiveSheet);
                SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
            }

            RefreshViewport();
        }

        return sheetChanged;
    }

    private bool SelectSheet(SheetId sheetId, bool selectRange, bool toggle)
        => SelectSheet(sheetId, selectRange, toggle, preserveFormulaEdit: false);

    private bool SelectSheet(
        SheetId sheetId,
        bool selectRange,
        bool toggle,
        bool preserveFormulaEdit)
    {
        var previousSheetId = ActiveSheet.Id;
        var previousGroupedSheetIds = _groupedSheetIds.ToHashSet();
        if (!previousSheetId.Equals(sheetId))
            RememberActiveWorksheetSelection();

        var selection = _sheetSelectionService.SelectSheet(Workbook, sheetId);
        var sheetChanged = previousSheetId != selection.Sheet.Id;

        ActiveSheet = selection.Sheet;
        UpdateGroupedSheetsForTabSelection(ActiveSheet.Id, selectRange, toggle);
        RefreshSheetTabsForActiveSheet();
        if (!preserveFormulaEdit)
            FormulaEditAddress = null;

        if (sheetChanged)
        {
            if (!TryRestoreActiveWorksheetSelection())
            {
                ActiveCell = GetInitialActiveCell(ActiveSheet);
                SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
            }

            RefreshViewport();
        }

        return sheetChanged || !previousGroupedSheetIds.SetEquals(_groupedSheetIds);
    }

    public bool SelectAllVisibleSheets()
    {
        var changed = SetGroupedSheetIds(
            SheetGroupSelectionService.SelectAll(GetSelectableSheetIds()),
            ActiveSheet.Id);
        _sheetGroupAnchor = ActiveSheet.Id;
        RefreshSheetTabsForActiveSheet();
        return changed;
    }

    public bool UngroupSheets()
    {
        var changed = SetGroupedSheetIds([ActiveSheet.Id], ActiveSheet.Id);
        _sheetGroupAnchor = ActiveSheet.Id;
        RefreshSheetTabsForActiveSheet();
        return changed;
    }

    public WorkbookCellEditResult AddSheet()
    {
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new AddSheetCommand(SheetTabListPlanner.GenerateUniqueSheetName(Workbook)));
        if (!result.Success)
            return result;

        ApplySuccessfulNewWorksheetResult(Workbook.Sheets[^1].Id);
        return result;
    }

    public WorkbookCellEditResult DuplicateActiveSheet()
    {
        var sourceSheetId = ActiveSheet.Id;
        var sourceIndex = FindSheetIndex(sourceSheetId, notFoundIndex: -1);
        if (sourceIndex < 0)
        {
            return new WorkbookCellEditResult(
                false,
                "Active sheet was not found.",
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new DuplicateSheetCommand(sourceSheetId));
        if (!result.Success)
            return result;

        // Duplicating a sheet can change which sheets fall inside a 3-D span reference
        // (e.g. =SUM(Sheet1:Sheet3!A1)), so recalculate the whole workbook just like the
        // WPF host does after Duplicate Sheet -- the command's own AffectedCells is empty
        // and would otherwise leave those span refs stale.
        RecalculateWorkbook();

        var copyIndex = Math.Min(sourceIndex + 1, Workbook.Sheets.Count - 1);
        ApplySuccessfulNewWorksheetResult(Workbook.Sheets[copyIndex].Id);
        return result;
    }

    public WorkbookCellEditResult MoveActiveSheetLeft() =>
        MoveActiveSheetBy(offset: -1);

    public WorkbookCellEditResult MoveActiveSheetRight() =>
        MoveActiveSheetBy(offset: 1);

    /// <summary>
    /// Moves the active sheet to an absolute 0-based position. Backs the Move-or-Copy dialog, which
    /// resolves an arbitrary target index (vs. the single-step <see cref="MoveActiveSheetLeft"/> /
    /// <see cref="MoveActiveSheetRight"/>). Rebuilds the sheet-tab ordering so the shell reflects the
    /// new position. Undo/redo aware via the shared edit-command path.
    /// </summary>
    public WorkbookCellEditResult MoveActiveSheetTo(int targetIndex)
    {
        var sheetId = ActiveSheet.Id;
        var fromIndex = FindSheetIndex(sheetId, notFoundIndex: -1);
        if (fromIndex < 0)
        {
            return new WorkbookCellEditResult(
                false,
                "Active sheet was not found.",
                [],
                RecalcReport: null);
        }

        var toIndex = Math.Clamp(targetIndex, 0, Math.Max(0, Workbook.Sheets.Count - 1));
        if (toIndex == fromIndex)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new MoveSheetCommand(fromIndex, toIndex));
        if (!result.Success)
            return result;

        // Moving a sheet can change which sheets fall inside a 3-D span reference
        // (e.g. =SUM(Sheet1:Sheet3!A1)), so recalculate the whole workbook just like the
        // WPF host does after a sheet-tab drag/Move-or-Copy move -- the command's own
        // AffectedCells is empty and would otherwise leave those span refs stale.
        RecalculateWorkbook();
        ApplySuccessfulWorkbookMetadataResult(sheetId);
        return result;
    }

    public WorkbookCellEditResult SetActiveSheetTabColor(CellColor? color)
    {
        if (ActiveSheet.TabColor == color)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetSheetTabColorCommand(ActiveSheet.Id, color));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    // ── Row height / column width / AutoFit ──────────────────────────────────
    // Home ▸ Cells ▸ Format and the row/column header context menus. The selection→span math, the
    // undoable mutation (Set{Row,Column} commands) and the AutoFit content measurement are fully
    // portable: they live in FreeX.App.Services.Ribbon.RowColumnSizingPlanner + the shared
    // AutoFitSizingService, so the cross-platform shell and the Windows host plan identically.

    /// <summary>The explicit/default height of the first row in the selection, for the Row Height dialog.</summary>
    public double GetSelectedRowHeight() =>
        Ribbon.RowColumnSizingPlanner.GetRowHeightDialogValue(ActiveSheet, SelectedRange);

    /// <summary>The explicit/default width of the first column in the selection, for the Column Width dialog.</summary>
    public double GetSelectedColumnWidth() =>
        Ribbon.RowColumnSizingPlanner.GetColumnWidthDialogValue(ActiveSheet, SelectedRange);

    /// <summary>Applies an explicit height (points) to every selected row on every grouped sheet.</summary>
    public WorkbookCellEditResult SetSelectedRowsHeight(double height) =>
        ExecuteRepeatableStructureCommand(() =>
            CreateSelectionSizingCommand(
                "Row Height",
                (sheetId, range) => Ribbon.RowColumnSizingPlanner.CreateRowHeightCommand(
                    sheetId,
                    range,
                    height)));

    /// <summary>Applies an explicit width (characters) to every selected column on every grouped sheet.</summary>
    public WorkbookCellEditResult SetSelectedColumnsWidth(double width) =>
        ExecuteRepeatableStructureCommand(() =>
            CreateSelectionSizingCommand(
                "Column Width",
                (sheetId, range) => Ribbon.RowColumnSizingPlanner.CreateColumnWidthCommand(
                    sheetId,
                    range,
                    width)));

    /// <summary>Commits a native row-header drag, whose measured size is already in model pixels.</summary>
    public WorkbookCellEditResult SetRowsHeightPixels(uint startRow, uint endRow, double heightPixels) =>
        ExecuteRepeatableStructureCommand(() =>
            CreateGroupedSheetCommand(
                "Row Height",
                sheetId => new SetRowHeightCommand(sheetId, startRow, endRow, heightPixels)));

    /// <summary>Commits a native column-header drag after converting renderer pixels to model width.</summary>
    public WorkbookCellEditResult SetColumnsWidthPixels(uint startColumn, uint endColumn, double widthPixels) =>
        ExecuteRepeatableStructureCommand(() =>
            CreateGroupedSheetCommand(
                "Column Width",
                sheetId => new SetColumnWidthCommand(
                    sheetId,
                    startColumn,
                    endColumn,
                    ColumnWidthPixelMapper.PixelsToColumnWidth(widthPixels))));

    public WorkbookCellEditResult SetSelectedRowsHidden(bool hidden) =>
        ExecuteRepeatableStructureCommand(() =>
            CreateSelectionSizingCommand(
                hidden ? "Hide Row" : "Unhide Row",
                (sheetId, range) => Ribbon.RowColumnSizingPlanner.CreateRowsHiddenCommand(
                    sheetId,
                    range,
                    hidden)));

    public WorkbookCellEditResult SetSelectedColumnsHidden(bool hidden) =>
        ExecuteRepeatableStructureCommand(() =>
            CreateSelectionSizingCommand(
                hidden ? "Hide Column" : "Unhide Column",
                (sheetId, range) => Ribbon.RowColumnSizingPlanner.CreateColumnsHiddenCommand(
                    sheetId,
                    range,
                    hidden)));

    /// <summary>
    /// Sizes each selected row's height to its tallest cell content (content-based estimate via the
    /// shared AutoFitSizingService — character/line counts, not true glyph metrics), across every
    /// disjoint area of a multi-area selection (R126-cellscmds-multiarea-rowheight-2, see
    /// <see cref="SetSelectedRowsHeight"/>). Returns a success result when there is nothing
    /// measurable in any area (e.g. a whole-sheet selection with no used range).
    /// </summary>
    public WorkbookCellEditResult AutoFitSelectedRowHeight()
    {
        return ExecuteRepeatableStructureCommand(() =>
            CreateSelectionSizingCommand(
                "Auto Row Height",
                (sheetId, range) => CreateAutoFitRowHeightCommand(
                    sheetId,
                    range)));
    }

    /// <summary>
    /// Sizes each selected column's width to its widest cell content (content-based estimate via the
    /// shared AutoFitSizingService), across every disjoint area of a multi-area selection
    /// (R126-cellscmds-multiarea-rowheight-2, see <see cref="SetSelectedRowsHeight"/>). Returns a
    /// success result when there is nothing measurable in any area.
    /// </summary>
    public WorkbookCellEditResult AutoFitSelectedColumnWidth()
    {
        return ExecuteRepeatableStructureCommand(() =>
            CreateSelectionSizingCommand(
                "Auto Column Width",
                (sheetId, range) => CreateAutoFitColumnWidthCommand(
                    sheetId,
                    range)));
    }

    public WorkbookCellEditResult AutoFitRows(uint startRow, uint endRow) =>
        ExecuteRepeatableStructureCommand(() =>
            CreateGroupedSheetCommand(
                "Auto Row Height",
                sheetId => CreateAutoFitRowHeightCommand(
                    sheetId,
                    new GridRange(
                        new CellAddress(sheetId, startRow, 1),
                        new CellAddress(sheetId, endRow, CellAddress.MaxCol)))));

    public WorkbookCellEditResult AutoFitColumns(uint startColumn, uint endColumn) =>
        ExecuteRepeatableStructureCommand(() =>
            CreateGroupedSheetCommand(
                "Auto Column Width",
                sheetId => CreateAutoFitColumnWidthCommand(
                    sheetId,
                    new GridRange(
                        new CellAddress(sheetId, 1, startColumn),
                        new CellAddress(sheetId, CellAddress.MaxRow, endColumn)))));

    /// <summary>
    /// Resolves the current selection into its disjoint areas (falling back to the single
    /// <see cref="SelectedRange"/> when there is no multi-area selection) via the same
    /// <see cref="SelectionStyleCommandPlanner.ResolveRanges"/> choke point the WPF host's
    /// GetCurrentSelectionRanges and the Avalonia shell's own Group/Ungroup and Outline fixes use
    /// (R126-cellscmds-multiarea-rowheight-2).
    /// </summary>
    private IReadOnlyList<GridRange> GetSelectionSizingRanges()
    {
        var ranges = SelectionStyleCommandPlanner.ResolveRanges(SelectedRange, SelectedRanges);
        return ranges.Count > 0 ? ranges : [SelectedRange];
    }

    /// <summary>Builds one command per grouped sheet and disjoint selected area.</summary>
    private IWorkbookCommand CreateSelectionSizingCommand(
        string title,
        Func<SheetId, GridRange, IWorkbookCommand> createCommand) =>
        CreateGroupedSelectionRangeCommand(title, GetSelectionSizingRanges(), createCommand);

    private IWorkbookCommand CreateGroupedSelectionRangeCommand(
        string title,
        IReadOnlyList<GridRange> ranges,
        Func<SheetId, GridRange, IWorkbookCommand> createCommand)
    {
        var commands = new List<IWorkbookCommand>();
        foreach (var sheetId in CurrentGroupedEditSheetIds())
        {
            commands.AddRange(ranges.Select(range =>
                createCommand(sheetId, RemapRangeToSheet(range, sheetId))));
        }

        return ToCommand(title, commands);
    }

    /// <summary>
    /// Runs a row/column sizing command and restores the selection afterwards. The shared command
    /// pipeline collapses the selection to the active cell on success (it is built for cell edits),
    /// but a dimension change must leave the resized rows/columns selected (Excel parity) so a
    /// follow-up resize targets the same span -- including every disjoint area of a multi-area
    /// selection (R126-cellscmds-multiarea-rowheight-2), not just the active one.
    /// </summary>
    private WorkbookCellEditResult ExecuteRepeatableStructureCommand(Func<IWorkbookCommand> commandFactory) =>
        ExecuteRepeatableCommandPreservingSelection(commandFactory);

    private IWorkbookCommand CreateAutoFitRowHeightCommand(SheetId sheetId, GridRange range)
    {
        var sheet = Workbook.GetSheet(sheetId);
        if (sheet is null)
            return new CompositeWorkbookCommand("Auto Row Height", []);

        var plans = Ribbon.RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            range,
            sheet.GetUsedRange(),
            (row, col) => GetAutoFitDisplayText(sheet, row, col),
            sheet.DefaultRowHeight);
        return Ribbon.RowColumnSizingPlanner.CreateAutoFitRowHeightCommand(sheetId, plans)
            ?? new CompositeWorkbookCommand("Auto Row Height", []);
    }

    private IWorkbookCommand CreateAutoFitColumnWidthCommand(SheetId sheetId, GridRange range)
    {
        var sheet = Workbook.GetSheet(sheetId);
        if (sheet is null)
            return new CompositeWorkbookCommand("Auto Column Width", []);

        var plans = Ribbon.RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            range,
            sheet.GetUsedRange(),
            (row, col) => GetAutoFitDisplayText(sheet, row, col),
            sheet.DefaultColumnWidth);
        return Ribbon.RowColumnSizingPlanner.CreateAutoFitColumnWidthCommand(sheetId, plans)
            ?? new CompositeWorkbookCommand("Auto Column Width", []);
    }

    private AutoFitCellText? GetAutoFitDisplayText(Sheet sheet, uint row, uint col)
    {
        if (sheet.GetCell(row, col) is not { } cell)
            return null;

        var style = Workbook.GetStyle(cell.StyleId);

        if (IsShowingFormulas && cell.FormulaText is not null)
            return new AutoFitCellText("=" + cell.FormulaText, style.WrapText, TextRotation: style.TextRotation, FontSize: style.FontSize);

        var text = FreeX.Core.Formula.NumberFormatter.Format(cell.Value, style.NumberFormat);
        return new AutoFitCellText(text, style.WrapText, TextRotation: style.TextRotation, FontSize: style.FontSize);
    }

    public WorkbookCellEditResult GroupSelectedOutline() =>
        ExecuteRepeatableStructureCommand(() =>
            CreateSelectionSizingCommand(
                "Group",
                (sheetId, range) => CreateOutlineCommand(
                    sheetId,
                    sheet => WorksheetStructureCommandPlanner.CreateGroupCommand(
                        sheet,
                        range))));

    public WorkbookCellEditResult UngroupSelectedOutline() =>
        ExecuteRepeatableStructureCommand(() =>
            CreateSelectionSizingCommand(
                "Ungroup",
                (sheetId, range) => CreateOutlineCommand(
                    sheetId,
                    sheet => WorksheetStructureCommandPlanner.CreateUngroupCommand(
                        sheet,
                        range))));

    public WorkbookCellEditResult ClearActiveWorksheetOutline() =>
        ExecuteCommandPreservingSelection(
            CreateGroupedSheetCommand(
                "Clear Outline",
                sheetId => new ClearWorksheetOutlineCommand(sheetId)));

    public WorkbookCellEditResult SetSelectedOutlineGroupsCollapsed(bool collapse) =>
        ExecuteRepeatableStructureCommand(() =>
            CreateSelectionSizingCommand(
                collapse ? "Collapse Group" : "Expand Group",
                (sheetId, range) => CreateOutlineCommand(
                    sheetId,
                    sheet => WorksheetStructureCommandPlanner.CreateSelectedOutlineVisibilityCommand(
                        sheet,
                        range,
                        collapse))));

    public WorkbookCellEditResult SetOutlineGroupCollapsed(
        OutlineGroupingAxis axis,
        uint start,
        uint end,
        int level,
        bool collapse) =>
        ExecuteRepeatableStructureCommand(() =>
            WorksheetStructureCommandPlanner.CreateOutlineGroupToggleCommand(
                ActiveSheet.Id,
                axis,
                start,
                end,
                level,
                collapse));

    private IWorkbookCommand CreateOutlineCommand(
        SheetId sheetId,
        Func<Sheet, IWorkbookCommand> createCommand) =>
        Workbook.GetSheet(sheetId) is { } sheet
            ? createCommand(sheet)
            : new CompositeWorkbookCommand("Worksheet Outline", []);

    public WorkbookCellEditResult SetShowFormulas(bool showFormulas)
    {
        if (IsShowingFormulas == showFormulas)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetWorksheetShowFormulasCommand(ActiveSheet.Id, showFormulas));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        // Reseed this view's own cache with the value it just applied -- see _viewShowFormulasOverrides
        // remarks (mirrors SetZoomPercent's reseed of _viewZoomOverrides).
        _viewShowFormulasOverrides[ActiveSheet.Id] = showFormulas;
        return result;
    }

    public WorkbookCellEditResult SetShowGridlines(bool showGridlines)
    {
        if (IsShowingGridlines == showGridlines)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        return SetWorksheetViewOptions(
            showGridlines,
            IsShowingHeadings,
            ActiveSheet.ShowRulers);
    }

    public WorkbookCellEditResult SetShowHeadings(bool showHeadings)
    {
        if (IsShowingHeadings == showHeadings)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        return SetWorksheetViewOptions(
            IsShowingGridlines,
            showHeadings,
            ActiveSheet.ShowRulers);
    }

    public bool IsShowingRulers => ActiveSheet.ShowRulers;

    public WorkbookCellEditResult SetShowRulers(bool showRulers)
    {
        if (ActiveSheet.ShowRulers == showRulers)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        return SetWorksheetViewOptions(
            IsShowingGridlines,
            IsShowingHeadings,
            showRulers);
    }

    public WorkbookCellEditResult SetWorksheetViewMode(WorksheetViewMode viewMode)
    {
        if (ViewMode == viewMode)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetWorksheetViewModeCommand(ActiveSheet.Id, viewMode));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        // Reseed this view's own cache with the value it just applied -- see _viewModeOverrides
        // remarks (mirrors SetZoomPercent's reseed of _viewZoomOverrides).
        _viewModeOverrides[ActiveSheet.Id] = viewMode;
        return result;
    }

    public WorkbookCellEditResult SetZoomPercent(int zoomPercent)
    {
        zoomPercent = Math.Clamp(
            zoomPercent,
            SetWorksheetZoomCommand.MinZoomPercent,
            SetWorksheetZoomCommand.MaxZoomPercent);
        if (ZoomPercent == zoomPercent)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetWorksheetZoomCommand(ActiveSheet.Id, zoomPercent));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        // Reseed this view's own cache with the value it just applied -- ApplySuccessfulWorkbookMetadataResult
        // already invalidated any stale entry, but without this a sibling view that changes zoom on
        // the same sheet before this view's next read would otherwise be able to overwrite it (see
        // _viewZoomOverrides remarks).
        _viewZoomOverrides[ActiveSheet.Id] = zoomPercent;
        return result;
    }

    public WorkbookCellEditResult FreezePanesAtActiveCell()
    {
        var frozenRows = ActiveCell.Row > 1 ? ActiveCell.Row - 1 : 0;
        var frozenCols = ActiveCell.Col > 1 ? ActiveCell.Col - 1 : 0;
        return SetFreezePanes(frozenRows, frozenCols);
    }

    public WorkbookCellEditResult FreezeTopRow() =>
        SetFreezePanes(frozenRows: 1, frozenCols: 0);

    public WorkbookCellEditResult FreezeFirstColumn() =>
        SetFreezePanes(frozenRows: 0, frozenCols: 1);

    public WorkbookCellEditResult UnfreezePanes() =>
        SetFreezePanes(frozenRows: 0, frozenCols: 0);

    public WorkbookCellEditResult ToggleSplitPanesAtActiveCell(
        IReadOnlyList<RowMetric>? viewportRows = null,
        IReadOnlyList<ColMetric>? viewportColumns = null)
    {
        var wasSplit = GetEffectiveSplitRow() is not null || GetEffectiveSplitCol() is not null;
        var rows = viewportRows ?? Viewport.RowMetrics;
        var columns = viewportColumns ?? Viewport.ColMetrics;
        var (splitRow, splitColumn) = WorksheetStructureCommandPlanner.ResolveSplitTarget(
            ActiveCell.Row,
            ActiveCell.Col,
            wasSplit,
            rows,
            columns);
        return SetSplitPanes(splitRow, splitColumn);
    }

    public WorkbookCellEditResult SetSplitPanes(uint? splitRow, uint? splitColumn)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var result = ExecuteCommandPreservingSelection(
            CreateGroupedSheetCommand(
                "Split",
                sheetId => new SetSplitPanesCommand(sheetId, splitRow, splitColumn)));
        if (!result.Success || result.IsNoOp)
            return result;

        foreach (var sheetId in targetSheetIds)
        {
            _viewSplitRowOverrides[sheetId] = splitRow;
            _viewSplitColOverrides[sheetId] = splitColumn;
        }

        ResetSplitPaneOffsets();
        RefreshViewport();
        return result;
    }

    public WorkbookCellEditResult HideActiveSheet()
    {
        var sheetId = ActiveSheet.Id;
        var sheetIndex = FindSheetIndex(sheetId, notFoundIndex: -1);
        if (sheetIndex < 0)
        {
            return new WorkbookCellEditResult(
                false,
                "Active sheet was not found.",
                [],
                RecalcReport: null);
        }

        var preferredSheetId = FindPreferredVisibleSheetIdAfterHidden(sheetIndex, sheetId);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetSheetHiddenCommand(sheetId, hidden: true));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookStructureResult(preferredSheetId ?? ActiveSheet.Id);
        return result;
    }

    public WorkbookCellEditResult UnhideSheet(SheetId sheetId)
    {
        var sheet = Workbook.GetSheet(sheetId);
        if (sheet is null)
        {
            return new WorkbookCellEditResult(
                false,
                "Hidden sheet was not found.",
                [],
                RecalcReport: null);
        }

        if (sheet.IsVeryHidden)
        {
            return new WorkbookCellEditResult(
                false,
                "Very hidden sheets cannot be unhidden from this menu.",
                [],
                RecalcReport: null);
        }

        if (!sheet.IsHidden)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetSheetHiddenCommand(sheetId, hidden: false));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookStructureResult(sheetId);
        return result;
    }

    public WorkbookCellEditResult DeleteActiveSheet()
    {
        var sheetId = ActiveSheet.Id;
        var sheetIndex = FindSheetIndex(sheetId, notFoundIndex: -1);
        if (sheetIndex < 0)
        {
            return new WorkbookCellEditResult(
                false,
                "Active sheet was not found.",
                [],
                RecalcReport: null);
        }

        var preferredSheetId = FindPreferredSheetIdAfterRemoval(sheetIndex, sheetId);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new RemoveSheetCommand(sheetId));
        if (!result.Success)
            return result;

        // R126-viewstate-delete-purge-1: drop this view's own per-sheet caches for the just-deleted
        // sheet id -- InvalidateAllPerViewOverridesForSheet/_splitPaneViewportOffsets are otherwise
        // only ever invalidated for the *active* sheet (metadata-setter forward-apply and Undo/Redo
        // re-seeding), never for a deletion, so each deleted sheet would leave one stale entry behind
        // in every one of those SheetId-keyed dictionaries for the rest of this session's lifetime.
        // R127-viewstate-delete-purge-2: _viewViewportOrigins (this view's own remembered scroll
        // TopRow/LeftCol per sheet, seeded in InitializeSiblingView/the constructor and read/written
        // by GetViewTopRow/GetViewLeftCol/SetViewViewportOrigin) is the same kind of per-view cache
        // but lives outside InvalidateAllPerViewOverridesForSheet's choke point, so r126 missed it --
        // purge it here too.
        InvalidateAllPerViewOverridesForSheet(sheetId);
        _splitPaneViewportOffsets.Remove(sheetId);
        _viewViewportOrigins.Remove(sheetId);

        // Deleting a sheet can change which sheets fall inside a 3-D span reference
        // (e.g. =SUM(Sheet1:Sheet3!A1)), so recalculate the whole workbook just like the
        // WPF host does after Move/Duplicate Sheet -- the command's own AffectedCells is
        // empty and would otherwise leave those span refs stale.
        RecalculateWorkbook();

        ApplySuccessfulWorkbookStructureResult(preferredSheetId ?? Workbook.Sheets[0].Id);
        return result;
    }

    public WorkbookCellEditResult RenameActiveSheet(string? name)
    {
        var newName = (name ?? "").Trim();
        if (string.Equals(newName, ActiveSheet.Name, StringComparison.Ordinal))
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new RenameSheetCommand(ActiveSheet.Id, newName));
        if (!result.Success)
            return result;

        // Renaming a sheet can change which sheets fall inside a 3-D span reference
        // (e.g. =SUM(Sheet1:Sheet3!A1)), so recalculate the whole workbook just like the
        // WPF host does after Move/Duplicate Sheet -- the command's own AffectedCells is
        // empty and would otherwise leave those span refs stale.
        RecalculateWorkbook();

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    private WorkbookCellEditResult MoveActiveSheetBy(int offset)
    {
        var sheetId = ActiveSheet.Id;
        var fromIndex = FindSheetIndex(sheetId, notFoundIndex: -1);
        if (fromIndex < 0)
        {
            return new WorkbookCellEditResult(
                false,
                "Active sheet was not found.",
                [],
                RecalcReport: null);
        }

        var toIndex = fromIndex + offset;
        if (toIndex < 0 || toIndex >= Workbook.Sheets.Count)
        {
            var edge = offset < 0 ? "first" : "last";
            return new WorkbookCellEditResult(
                false,
                $"Active sheet is already the {edge} sheet.",
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new MoveSheetCommand(fromIndex, toIndex));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(sheetId);
        return result;
    }

    public void BeginFormulaEdit(CellAddress address)
    {
        ActiveCell = address;
        ActiveSheet.ActiveRow = address.Row;
        ActiveSheet.ActiveCol = address.Col;

        // Mirror Excel / the WPF host: starting to edit a cell that already sits inside the current
        // (possibly multi-cell) selection leaves the selection rectangle intact, so a following
        // Ctrl+Enter fills the whole originally-selected range and a plain Enter can advance within
        // it. The WPF host never touches SheetGrid.SelectedRange when an inline edit starts; the old
        // unconditional collapse here silently shrank a multi-cell selection to the single edited
        // cell the moment typing began, so live Ctrl+Enter only ever filled that one cell. Only
        // collapse when the edited address falls OUTSIDE the current selection, which keeps the
        // ActiveCell-inside-SelectedRange invariant for callers that begin an edit elsewhere.
        if (!CurrentSelectionContains(address))
            SetSingleSelectedRange(new GridRange(address, address));

        FormulaEditAddress = address;
    }

    /// <summary>
    /// Whether <paramref name="address"/> lies within the current selection — the primary
    /// <see cref="SelectedRange"/> or any area of a multi-area <see cref="SelectedRanges"/>.
    /// </summary>
    private bool CurrentSelectionContains(CellAddress address)
    {
        if (SelectedRange.Contains(address))
            return true;

        foreach (var range in SelectedRanges)
        {
            if (range.Contains(address))
                return true;
        }

        return false;
    }

    public void CancelFormulaEdit()
    {
        if (FormulaEditAddress is { } formulaEditAddress &&
            !ActiveSheet.Id.Equals(formulaEditAddress.Sheet))
        {
            RememberActiveWorksheetSelection();
            var selection = _sheetSelectionService.SelectSheet(Workbook, formulaEditAddress.Sheet);
            ActiveSheet = selection.Sheet;
            RefreshSheetTabsForActiveSheet();
            ActiveCell = formulaEditAddress;
            ActiveSheet.ActiveRow = formulaEditAddress.Row;
            ActiveSheet.ActiveCol = formulaEditAddress.Col;
            SetSingleSelectedRange(new GridRange(formulaEditAddress, formulaEditAddress));
            RefreshViewport();
            EnsureActiveCellVisible();
        }

        FormulaEditAddress = null;
    }

    /// <summary>
    /// Excel auto-clears a cell's red "Circle Invalid Data" oval the instant the flagged value is
    /// corrected -- the user never has to manually re-run Data &gt; Data Validation &gt; Circle
    /// Invalid Data. Both shells keep their circled-cell overlay as a simple list that is otherwise
    /// only ever (re)populated by a fresh Circle Invalid Data run and cleared by Clear Validation
    /// Circles, so this shared, host-agnostic helper re-checks <paramref name="circledCells"/>
    /// against a fresh <see cref="DataValidationCirclePlanner.FindInvalidDataCells"/> scan of
    /// <paramref name="activeSheet"/> and drops any entry that no longer violates its rule. Entries
    /// that belong to a different (inactive) sheet are left untouched, since the fresh scan only
    /// covers <paramref name="activeSheet"/>. Returns <paramref name="circledCells"/> unchanged
    /// (same reference) when nothing needed pruning, so callers can cheaply detect a no-op.
    /// </summary>
    public static IReadOnlyList<CellAddress> PruneCorrectedValidationCircles(
        Workbook workbook, Sheet activeSheet, IReadOnlyList<CellAddress> circledCells)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(activeSheet);
        ArgumentNullException.ThrowIfNull(circledCells);

        if (circledCells.Count == 0)
            return circledCells;

        var stillInvalid = new HashSet<CellAddress>(DataValidationCirclePlanner.FindInvalidDataCells(workbook, activeSheet));
        var pruned = circledCells
            .Where(address => address.Sheet != activeSheet.Id || stillInvalid.Contains(address))
            .ToList();

        return pruned.Count == circledCells.Count ? circledCells : pruned;
    }

    public WorkbookCellEditResult CommitCellText(string text, bool useR1C1ReferenceStyle = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var address = FormulaEditAddress ?? ActiveCell;
        var failure = TryBuildValidatedCellEntryEdits(
            text,
            [address],
            useR1C1ReferenceStyle,
            out var edits);
        if (failure is not null)
            return failure;

        // Formula point mode can leave the visible sheet on the reference target while the edit
        // belongs to the original source sheet. Build the command against the source sheet in that
        // case; using the visible ActiveSheet here would write the source address into the pointed
        // sheet and leave the real edit cell untouched.
        var editCommand = address.Sheet.Equals(ActiveSheet.Id)
            ? CreateEditCellsCommand(edits)
            : new EditCellsCommand(address.Sheet, edits);
        var result = _cellEditService.ExecuteEditCommand(Workbook, editCommand);

        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, address);
        GrowRowHeightForAlreadyWrappedCellIfNeeded(address);
        CancelPendingCutAfterMutatingEdit();
        return result;
    }

    /// <summary>
    /// Commits one entry to every cell in the current single- or multi-area selection as one
    /// undoable command while preserving the selection. Both desktop renderers use this for
    /// Ctrl+Enter, so parsing, validation, grouped-sheet targeting, recalculation, and dirty-state
    /// ownership stay in the application session.
    /// </summary>
    public WorkbookCellEditResult CommitCellTextAcrossSelection(
        string text,
        bool useR1C1ReferenceStyle = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var primaryRange = SelectedRange;
        var selectedRanges = SelectedRanges.Count > 0
            ? SelectedRanges.ToArray()
            : [primaryRange];
        var addresses = new List<CellAddress>();
        var seenAddresses = new HashSet<CellAddress>();
        foreach (var range in selectedRanges)
        {
            ValidateSelectionRange(range, nameof(SelectedRanges));
            foreach (var address in range.AllCells())
            {
                if (seenAddresses.Add(address))
                    addresses.Add(address);
            }
        }

        if (addresses.Count == 0)
            return new WorkbookCellEditResult(false, "The current selection contains no cells.", [], null);

        var failure = TryBuildValidatedCellEntryEdits(
            text,
            addresses,
            useR1C1ReferenceStyle,
            out var edits);
        if (failure is not null)
            return failure;

        var activeCell = ActiveCell;
        var result = _cellEditService.ExecuteEditCommand(Workbook, CreateEditCellsCommand(edits));
        if (!result.Success)
            return result;

        ApplySuccessfulSelectionEditResult(result, primaryRange, selectedRanges, activeCell);
        foreach (var address in addresses)
            GrowRowHeightForAlreadyWrappedCellIfNeeded(address);
        CancelPendingCutAfterMutatingEdit();
        return result;
    }

    private WorkbookCellEditResult? TryBuildValidatedCellEntryEdits(
        string text,
        IReadOnlyList<CellAddress> addresses,
        bool useR1C1ReferenceStyle,
        out IReadOnlyList<(CellAddress Address, Cell NewCell)> edits)
    {
        var plan = CellEntryCommitPlanner.BuildSelection(
            text,
            addresses,
            useR1C1ReferenceStyle,
            Workbook);
        if (!plan.Success)
        {
            edits = [];
            return new WorkbookCellEditResult(
                false,
                plan.ErrorMessage,
                [],
                RecalcReport: null,
                new WorkbookCellEditFailure(WorkbookCellEditFailureKind.InvalidEntrySyntax));
        }

        foreach (var (address, cell) in plan.Edits)
        {
            var check = EvaluateDataValidationForEntry(cell, address);
            if (check.Outcome == DataValidationEntryOutcome.Blocked)
            {
                edits = [];
                return new WorkbookCellEditResult(
                    false,
                    check.Message,
                    [],
                    RecalcReport: null,
                    new WorkbookCellEditFailure(
                        WorkbookCellEditFailureKind.DataValidationBlocked,
                        check.Title,
                        check.AlertStyle));
            }

            if (check.Outcome != DataValidationEntryOutcome.NeedsConfirmation ||
                DataValidationPromptResolver is not { } resolvePrompt)
            {
                continue;
            }

            var decision = resolvePrompt(new DataValidationPromptRequest(check.Message!, check.Title!, check.AlertStyle));
            if (decision is UserMessageResult.Yes or UserMessageResult.Ok)
                continue;

            edits = [];
            return new WorkbookCellEditResult(
                false,
                check.Message,
                [],
                RecalcReport: null,
                new WorkbookCellEditFailure(
                    WorkbookCellEditFailureKind.DataValidationDeclined,
                    check.Title,
                    check.AlertStyle,
                    decision));
        }

        edits = plan.Edits;
        return null;
    }

    /// <summary>
    /// Excel cancels an active Copy/Cut's marching-ants mode -- and with it, a subsequent Paste's
    /// ability to reuse the captured snapshot -- as soon as an ordinary edit or Clear Contents commits
    /// elsewhere on the sheet: for a Cut this prevents a later Paste from silently MOVING (and
    /// blanking out) a source range the user has since typed over or cleared; for a Copy it just
    /// retires a now-stale snapshot the same way pressing Esc would, matching the single marquee-mode
    /// semantics Excel uses regardless of which operation started it. Mirrors the WPF host's
    /// <c>MainWindow.CommandExecution.TryExecuteEditCells</c> (R54) and
    /// <c>MainWindow.CellsCommands.ClearClipboardMarqueeAfterStructuralEdit</c> (R75), which both clear
    /// the workbook clipboard session unconditionally (no <c>IsCut</c> check) -- scoped here to the specific
    /// "committed a mutating edit that is not the paste itself" call sites
    /// (<see cref="CommitCellText"/>, <see cref="ClearSelectedRangeContents"/>,
    /// <see cref="ClearActiveCellContents"/>, <see cref="UndoLastEdit"/>, <see cref="RedoLastEdit"/>,
    /// and <see cref="ExecuteReviewCommand"/> for the structural Insert/Delete Rows/Columns/Cells
    /// family only -- see <see cref="IsStructuralCellShiftCommand"/>, R127B-services-clipboard-
    /// structural-cancel-1) that this host-agnostic session shares with the Avalonia shell.
    /// Previously only cancelled a CUT, which left a plain Copy's snapshot alive across
    /// Undo/Redo/edits on Avalonia (and FreeW/FreeP, which share this tier) even though the WPF
    /// sibling this comment claimed to mirror always cancelled both
    /// (R127-services-clipboard-formats-copy-cancel-1).
    /// </summary>
    private void CancelPendingCutAfterMutatingEdit()
    {
        if (_workbookClipboardSession.HasContent)
            _workbookClipboardSession.Clear();
    }

    /// <summary>
    /// Excel re-measures an auto-height row on every edit of a cell that already has Wrap Text on
    /// -- not just on the WrapText style flag's off-to-on transition (that one-time grow is handled
    /// separately by <see cref="CreateWrapTextGrowthCommands"/>/<see cref="SetSelectedRangeWrapText"/>).
    /// Runs after the edit (and its recalculation) has landed, so the cell's committed value/formula
    /// result is what gets measured. Matches the existing "only ever grows a row" contract shared with
    /// the wrap-toggle-on path: a row a user has manually resized taller (or that already fits) is
    /// never shrunk back down just because a shorter value was typed.
    /// </summary>
    private void GrowRowHeightForAlreadyWrappedCellIfNeeded(CellAddress address)
    {
        var sheet = Workbook.GetSheet(address.Sheet);
        if (sheet is null)
            return;
        if (sheet.GetCell(address.Row, address.Col) is not { } cell)
            return;
        if (!Workbook.GetStyle(cell.StyleId).WrapText)
            return;
        if (sheet.IsMerged(address) || sheet.IsRowEffectivelyHidden(address.Row))
            return;

        var singleCellRange = new GridRange(address, address);
        var plans = Ribbon.RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            singleCellRange,
            usedRange: singleCellRange,
            (row, col) => GetAutoFitDisplayText(sheet, row, col),
            sheet.DefaultRowHeight);

        if (plans.Count != 1)
            return;

        var currentHeight = sheet.RowHeights.TryGetValue(address.Row, out var height) ? height : sheet.DefaultRowHeight;
        if (plans[0].Size <= currentHeight)
            return;

        var growResult = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetRowHeightCommand(sheet.Id, address.Row, address.Row, plans[0].Size));
        if (growResult.Success)
        {
            MarkDirty();
            RefreshViewport();
        }
    }

    private enum DataValidationEntryOutcome { Allowed, Blocked, NeedsConfirmation }

    private readonly record struct DataValidationEntryCheck(
        DataValidationEntryOutcome Outcome,
        string? Message,
        string? Title,
        DvAlertStyle AlertStyle)
    {
        public static readonly DataValidationEntryCheck Allowed =
            new(DataValidationEntryOutcome.Allowed, null, null, default);
    }

    /// <summary>
    /// Classifies the first applicable data-validation rule that <paramref name="cell"/> violates
    /// at <paramref name="address"/> (Excel's "first rule wins" behavior): <c>Blocked</c> for a
    /// Stop-alert rule with <c>ShowErrorMessage</c> set (<see cref="CommitCellText"/> rejects the
    /// entry outright), <c>NeedsConfirmation</c> for a Warning/Information ("AskToContinue") rule
    /// (<see cref="CommitCellText"/> consults <see cref="DataValidationPromptResolver"/>), or
    /// <c>Allowed</c> when no rule is violated, or the violated rule has <c>ShowErrorMessage</c>
    /// off (Excel treats that as unrestricted).
    /// </summary>
    private DataValidationEntryCheck EvaluateDataValidationForEntry(Cell cell, CellAddress address)
    {
        var sheet = Workbook.GetSheet(address.Sheet);
        if (sheet == null)
            return DataValidationEntryCheck.Allowed;

        var value = cell.HasFormula
            ? new FreeX.Core.Formula.FormulaEvaluator().Evaluate(cell.FormulaText!, sheet, Workbook, currentCell: address)
            : cell.Value;

        foreach (var dv in DataValidationService.GetApplicable(sheet, address))
        {
            var msg = DataValidationService.Validate(dv, value, sheet, address, Workbook);
            if (msg == null)
                continue;

            var action = DataValidationService.GetInvalidEntryAction(dv);
            var title = dv.ErrorTitle ?? "Validation Error";
            if (action == DataValidationInvalidEntryAction.Block)
                return new DataValidationEntryCheck(DataValidationEntryOutcome.Blocked, msg, title, dv.AlertStyle);

            if (action == DataValidationInvalidEntryAction.AskToContinue)
                return new DataValidationEntryCheck(DataValidationEntryOutcome.NeedsConfirmation, msg, title, dv.AlertStyle);

            // Allow (ShowErrorMessage off) -- only the first violated rule matters for Excel's
            // "first rule wins" behavior, so stop here without blocking.
            break;
        }

        return DataValidationEntryCheck.Allowed;
    }

    public WorkbookCellEditResult InsertAutoSumFormula(string functionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        if (!AutoSumFormulaPlanner.TryCreatePlan(ActiveSheet, functionName, SelectedRange, out var plan))
            return new WorkbookCellEditResult(false, "AutoSum target is outside the worksheet bounds.", [], null);

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateEditCellsCommand([(plan.Target, Cell.FromFormula(plan.Formula))]));
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, plan.Target);
        return result;
    }

    public string CopyActiveCellText()
    {
        var range = new GridRange(ActiveCell, ActiveCell);
        return ClipboardSerializer.Serialize(Viewport, range);
    }

    public string CopySelectedRangeText()
    {
        var result = TryCopySelectedRangeText();
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage);

        return result.Text!;
    }

    public WorkbookClipboardTextResult TryCopySelectedRangeText()
    {
        if (SelectedRanges.Count > 1)
        {
            // Excel copies a multiple selection only when its areas share the same rows or the
            // same columns; otherwise the command is rejected.
            if (!MultiRangeCopyPlanner.TryPlan(SelectedRanges, out var layout) || layout is null)
                return WorkbookClipboardTextResult.Failed(
                    ClipboardFeedbackPlanner.MultiRangeSelectionUnsupported(isCut: false).FallbackText);

            var blockText = SerializeMultiRangeCopy(layout);
            // The combined block is copied as concatenated values through the text path; clear any
            // FreeX-owned single-range clipboard so paste does not reuse a stale payload whose
            // formula/format rebasing would not match the gap-collapsed block.
            _workbookClipboardSession.Clear();
            return WorkbookClipboardTextResult.Succeeded(blockText);
        }

        // R14-clipboard-formats-deep-1: Viewport only materializes the on-screen scroll position
        // (BuildViewport() sizes it to ActiveSheet.ViewTopRow/ViewLeftCol + _viewportHeight/
        // _viewportWidth). Serializing straight off that truncates any part of a selection that
        // scrolled out of view to blank in both the plain-text clipboard payload and the CF_HTML
        // fragment (built by the Avalonia shell from the Viewport this result carries). Build a
        // viewport sized to the copied range itself instead, mirroring the WPF host's
        // BuildFullRangeViewportForClipboard (P41), so external copy/paste always reflects the full
        // selection regardless of what is currently scrolled into view.
        var fullRangeViewport = BuildFullRangeViewportForClipboard(SelectedRange);
        var text = ClipboardSerializer.Serialize(fullRangeViewport, SelectedRange);
        var snapshot = _workbookClipboardSession.Capture(
            CaptureInternalClipboard(SelectedRange, text, isCut: false, fullRangeViewport));
        return WorkbookClipboardTextResult.Succeeded(text, fullRangeViewport, snapshot.Marker);
    }

    public string CutSelectedRangeText()
    {
        var result = TryCutSelectedRangeText();
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage);

        return result.Text!;
    }

    public WorkbookClipboardTextResult TryCutSelectedRangeText()
    {
        if (SelectedRanges.Count > 1)
        {
            return WorkbookClipboardTextResult.Failed(
                ClipboardFeedbackPlanner.MultiRangeSelectionUnsupported(isCut: true).FallbackText);
        }

        // Same rationale as TryCopySelectedRangeText: use a full-range viewport, not the on-screen
        // Viewport, so cutting a selection taller/wider than the visible area does not blank out the
        // off-screen part of the clipboard payload (R14-clipboard-formats-deep-1).
        var fullRangeViewport = BuildFullRangeViewportForClipboard(SelectedRange);
        var text = ClipboardSerializer.Serialize(fullRangeViewport, SelectedRange);
        var snapshot = _workbookClipboardSession.Capture(
            CaptureInternalClipboard(SelectedRange, text, isCut: true, fullRangeViewport));
        return WorkbookClipboardTextResult.Succeeded(text, fullRangeViewport, snapshot.Marker);
    }

    /// <summary>
    /// Builds a <see cref="ViewportModel"/> that materializes every cell in <paramref name="range"/>,
    /// independent of the current scroll position. Mirrors the WPF host's
    /// <c>MainWindow.BuildFullRangeViewportForClipboard</c> (P41 / R14-clipboard-formats-deep-1):
    /// requesting a viewport whose top-left is the range's own start and whose available
    /// height/width is sized (generously) to the range's own row/column span guarantees every cell
    /// in the range is present regardless of what is currently scrolled into view.
    /// </summary>
    private ViewportModel BuildFullRangeViewportForClipboard(GridRange range)
    {
        // Generous per-row/per-column pixel bounds so the viewport's internal "stop materializing"
        // heuristic (which walks actual row heights/column widths, not these estimates) always
        // reaches past the end of the requested range even for tall rows / wide columns, while still
        // being a small constant multiple of the range size rather than the whole sheet.
        const double MaxPlausibleRowHeight = 500.0;
        const double MaxPlausibleColWidth = 2000.0;

        var rowSpan = (double)range.RowCount;
        var colSpan = (double)range.ColCount;
        var availableHeight = Math.Min(double.MaxValue / 2, (rowSpan + 2) * MaxPlausibleRowHeight);
        var availableWidth = Math.Min(double.MaxValue / 2, (colSpan + 2) * MaxPlausibleColWidth);

        var request = new ViewportRequest(
            TopRow: range.Start.Row,
            LeftCol: range.Start.Col,
            AvailableHeight: availableHeight,
            AvailableWidth: availableWidth,
            IncludeObjects: false,
            SplitPaneOffsets: null);

        return _viewportService.GetViewport(Workbook, range.Start.Sheet, request);
    }

    public WorkbookCellEditResult PasteClipboardTextAtActiveCell(
        string? text,
        bool preserveText = false,
        bool clipboardReadFailed = false,
        string? html = null,
        string? clipboardMarker = null)
    {
        // Paste Special > Text / Unicode Text (preserveText: true — Excel semantics: paste the
        // clipboard's plain text only) must always go through the external-clipboard plain-text path
        // below, even right after an in-app copy where the OS clipboard text still matches the
        // internal clipboard's text. Otherwise the internal-clipboard branch below wins (its
        // text-equality check can't distinguish "explicitly asked for text" from "clipboard
        // unchanged") and silently performs a full formatted internal paste instead (review P44),
        // mirroring the WPF host's ExecutePaste externalTextAsText bypass.
        if (!preserveText && _workbookClipboardSession.HasContent)
        {
            var resolution = _workbookClipboardSession.ResolvePaste(
                new WorkbookClipboardReadObservation(
                    Available: true,
                    Text: text,
                    Marker: clipboardMarker,
                    ReadFailed: clipboardReadFailed));
            if (resolution.Plan == ClipboardPastePlan.ReadFailed)
            {
                // A transient OS-clipboard read failure must never be silently reinterpreted as
                // "clipboard unchanged" — that would risk pasting a stale internal copy over content
                // the user just copied elsewhere. Surface it instead of guessing, mirroring the WPF
                // host's shared workbook clipboard-session guard.
                return new WorkbookCellEditResult(
                    false,
                    ClipboardFeedbackPlanner.ReadFailed.FallbackText,
                    [],
                    RecalcReport: null);
            }

            if (resolution.Snapshot is { } internalClipboard)
                return PasteInternalClipboardAtActiveCell(internalClipboard, PasteCellsMode.All, default);
        }

        if (string.IsNullOrEmpty(text))
        {
            return new WorkbookCellEditResult(
                false,
                "Clipboard does not contain text.",
                [],
                RecalcReport: null);
        }

        return PasteExternalTextAtActiveCell(text, preserveText, default, html);
    }

    public WorkbookCellEditResult PasteSpecialClipboardAtActiveCell(
        string? text,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths = false,
        bool clipboardReadFailed = false,
        string? html = null)
    {
        if (!Enum.IsDefined(mode))
        {
            return new WorkbookCellEditResult(
                false,
                "Paste Special mode is not supported.",
                [],
                RecalcReport: null);
        }

        if (clipboardReadFailed)
        {
            // Mirror PasteClipboardTextAtActiveCell: a transient OS-clipboard read failure must not
            // be treated as "clipboard changed" (which would drop the internal clipboard) nor as
            // "clipboard unchanged" (which would silently paste a possibly-stale internal copy) —
            // surface it so the caller can tell the user and let them retry.
            return new WorkbookCellEditResult(
                false,
                ClipboardFeedbackPlanner.ReadFailed.FallbackText,
                [],
                RecalcReport: null);
        }

        if (_workbookClipboardSession.Content is not { } internalClipboard)
        {
            // No FreeX-internal clipboard at all — fall back to an external-text Paste Special
            // instead of unconditionally rejecting, matching Excel (Paste Special on a copied
            // external TSV/CSV block still applies Transpose/Skip Blanks/Operation) and the WPF
            // host's PasteSpecialBtn_Click, which only routes to PasteSpecialAction.ExternalText
            // when the workbook clipboard session is empty at click time (review P46 — this shell used to reject
            // with "Paste Special requires copied FreeX cells." for any external text, silently
            // dropping the selected options instead of honoring them).
            if (string.IsNullOrEmpty(text))
            {
                return new WorkbookCellEditResult(
                    false,
                    "Paste Special requires copied FreeX cells.",
                    [],
                    RecalcReport: null);
            }

            return PasteExternalTextAtActiveCell(text, preserveText: false, options, html);
        }

        if (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal))
        {
            // A FreeX-internal clipboard exists, but the live OS clipboard text no longer matches
            // it (another app/window changed the platform clipboard since the FreeX copy). Matching
            // the WPF host's ExecutePaste (which treats the shared session's
            // UseExternalClipboardText result as "clipboard changed externally" and falls through to
            // CreateExternalTextPasteCommand with the selected options), clear the stale internal
            // clipboard and fall back to an external-text Paste Special instead of hard-rejecting, so
            // the live external text still gets the chosen Transpose/Skip Blanks/Operation options
            // applied (review P46 corollary — the null-internal-clipboard branch above already does
            // this; this branch used to unconditionally reject instead).
            _workbookClipboardSession.Clear();
            if (string.IsNullOrEmpty(text))
            {
                return new WorkbookCellEditResult(
                    false,
                    "Paste Special requires copied FreeX cells.",
                    [],
                    RecalcReport: null);
            }

            return PasteExternalTextAtActiveCell(text, preserveText: false, options, html);
        }

        if (SelectedRanges.Count > 1)
            return PasteInternalClipboardToSelectedRanges(
                internalClipboard,
                mode,
                options,
                keepSourceColumnWidths);

        return PasteInternalClipboardAtActiveCell(internalClipboard, mode, options, keepSourceColumnWidths);
    }

    public WorkbookCellEditResult PasteColumnWidthsFromClipboardAtActiveCell(string? text)
    {
        if (_workbookClipboardSession.Content is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _workbookClipboardSession.Clear();
            return new WorkbookCellEditResult(
                false,
                "Paste Column Widths requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        // Tile the pasted column widths across the whole selected destination columns when it
        // is a whole multiple of the copied source range's columns, matching the
        // Values/Formulas/Formats/All paste tiling behavior
        // (PasteCommandFactory.CreateInternalPasteCommand / GetSinglePasteDestinationRange)
        // instead of only ever filling the source range's own column footprint anchored at the
        // selection's start column (R36-commands-paste-special-4-3).
        var destinationRange = GetSinglePasteDestinationRange(destination);
        var command = CreateGroupedSheetCommand(
            "Paste Column Widths",
            sheetId => new PasteColumnWidthsCommand(
                sheetId,
                internalClipboard.SourceRange,
                RemapAddressToSheet(destination, sheetId).Col,
                RemapRangeToSheet(destinationRange, sheetId).ColCount));
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    public WorkbookCellEditResult PasteCommentsFromClipboardAtActiveCell(string? text, bool transpose = false)
    {
        if (_workbookClipboardSession.Content is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _workbookClipboardSession.Clear();
            return new WorkbookCellEditResult(
                false,
                "Paste Comments requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        // Tile the pasted comment(s) across the whole selected destination when it is a whole
        // multiple of the copied source range, matching the Values/Formulas/Formats/All paste
        // tiling behavior (PasteCommandFactory.CreateInternalPasteCommand /
        // GetSinglePasteDestinationRange) instead of only ever filling the selection's top-left
        // cell (R36-commands-paste-special-4-1).
        var destinationRange = GetSinglePasteDestinationRange(destination);
        var pasteSize = GetPasteDimensions(internalClipboard.SourceRange, transpose);
        if (destinationRange.RowCount > pasteSize.RowCount || destinationRange.ColCount > pasteSize.ColCount)
        {
            pasteSize = (
                Math.Max(pasteSize.RowCount, destinationRange.RowCount),
                Math.Max(pasteSize.ColCount, destinationRange.ColCount));
        }

        if (!TryGetRectangleEnd(destination, pasteSize.RowCount, pasteSize.ColCount, out _))
        {
            return new WorkbookCellEditResult(
                false,
                "Paste destination range is outside the worksheet bounds.",
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateGroupedSheetCommand(
                "Paste Comments",
                sheetId => new PasteCommentsCommand(
                    sheetId,
                    internalClipboard.SourceRange,
                    RemapRangeToSheet(destinationRange, sheetId),
                    transpose)));
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        SelectPastedRange(destination, pasteSize.RowCount, pasteSize.ColCount);
        return result;
    }

    public WorkbookCellEditResult PasteDataValidationFromClipboardAtActiveCell(string? text, bool transpose = false)
    {
        if (_workbookClipboardSession.Content is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _workbookClipboardSession.Clear();
            return new WorkbookCellEditResult(
                false,
                "Paste Validation requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        // Tile the pasted validation rule(s) across the whole selected destination when it is a
        // whole multiple of the copied source range, matching the Values/Formulas/Formats/All
        // paste tiling behavior (PasteCommandFactory.CreateInternalPasteCommand /
        // GetSinglePasteDestinationRange) instead of only ever filling the selection's top-left
        // cell (R36-commands-paste-special-4-1).
        var destinationRange = GetSinglePasteDestinationRange(destination);
        var pasteSize = GetPasteDimensions(internalClipboard.SourceRange, transpose);
        if (destinationRange.RowCount > pasteSize.RowCount || destinationRange.ColCount > pasteSize.ColCount)
        {
            pasteSize = (
                Math.Max(pasteSize.RowCount, destinationRange.RowCount),
                Math.Max(pasteSize.ColCount, destinationRange.ColCount));
        }

        if (!TryGetRectangleEnd(destination, pasteSize.RowCount, pasteSize.ColCount, out _))
        {
            return new WorkbookCellEditResult(
                false,
                "Paste destination range is outside the worksheet bounds.",
                [],
                RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateGroupedSheetCommand(
                "Paste Data Validation",
                sheetId => new PasteDataValidationCommand(
                    sheetId,
                    internalClipboard.SourceRange,
                    RemapRangeToSheet(destinationRange, sheetId),
                    transpose)));
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        SelectPastedRange(destination, pasteSize.RowCount, pasteSize.ColCount);
        return result;
    }

    public WorkbookDataValidationMutationResult ApplyDataValidationToSelectedRange(DataValidation rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var command = CreateSetSelectedRangeDataValidationCommand(rule);
        if (command is null)
            return WorkbookDataValidationMutationResult.NoMutation();

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return WorkbookDataValidationMutationResult.FromEditResult(result, mutated: false);

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return WorkbookDataValidationMutationResult.FromEditResult(result, mutated: true);
    }

    /// <summary>
    /// Applies <paramref name="rule"/> to the current selection AND to every other data-validation
    /// range whose settings match <paramref name="existingRule"/>, on the active sheet and — when
    /// sheet tabs are grouped — on every other grouped visible sheet too (mirroring
    /// <see cref="CurrentGroupedEditSheetIds"/>'s use by every other grouped-edit session API). This
    /// is the shared session equivalent of the WPF host's data-validation sweep
    /// (CreateDataValidationCommand / HasSameDataValidationSettings run per grouped sheet via
    /// TryExecuteRepeatableGroupedSheetCommand), so every shell can drive the "apply to all cells
    /// with the same settings" checkbox through one undoable composite command instead of
    /// reimplementing it locally.
    /// </summary>
    public WorkbookDataValidationMutationResult ApplyDataValidationToSelectedRangeAndMatchingRanges(
        DataValidation rule,
        DataValidation existingRule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(existingRule);

        var activeSheetId = ActiveSheet.Id;
        var selectedRanges = GetCurrentSelectedRanges();
        var commands = new List<IWorkbookCommand>();
        foreach (var sheetId in CurrentGroupedEditSheetIds())
        {
            var sheet = Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            var matches = sheet.DataValidations
                .Where(candidate => candidate.HasSameSettings(existingRule))
                .Select(candidate => (IWorkbookCommand)new SetDataValidationCommand(
                    sheetId,
                    rule.CloneWithNewIdentity(candidate.AppliesTo, candidate.AdditionalRanges)))
                .ToList();

            if (matches.Count == 0)
            {
                // No existing range matched existingRule's settings, so fall back to the current
                // selection itself — but the selection may be a Ctrl+click multi-area selection, so
                // every area must be folded into one rule's AppliesTo+AdditionalRanges, mirroring
                // CreateSetSelectedRangeDataValidationCommand's non-sweep apply path. Using only the
                // single active SelectedRange here would silently drop the non-primary areas.
                var sheetRanges = selectedRanges
                    .Select(range => RemapRangeToSheet(range, sheetId))
                    .ToArray();
                matches.Add(new SetDataValidationCommand(
                    sheetId,
                    rule.CloneWithNewIdentity(sheetRanges[0], sheetRanges.Skip(1))));
            }

            commands.AddRange(matches);
        }

        var command = ToCommand("Data Validation", commands);
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return WorkbookDataValidationMutationResult.FromEditResult(result, mutated: false);

        ApplySuccessfulWorkbookMetadataResult(activeSheetId);
        return WorkbookDataValidationMutationResult.FromEditResult(result, mutated: true);
    }

    public WorkbookCellEditResult PasteLinkFromClipboardAtActiveCell(
        string? text,
        bool transpose = false,
        bool keepSourceColumnWidths = false)
    {
        if (_workbookClipboardSession.Content is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _workbookClipboardSession.Clear();
            return new WorkbookCellEditResult(
                false,
                "Paste Link requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var sourceSheet = Workbook.GetSheet(internalClipboard.SourceRange.Start.Sheet);
        if (sourceSheet is null)
        {
            return new WorkbookCellEditResult(
                false,
                "Paste Link source sheet was not found.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        // Tile the linked formulas across the whole selected destination when it is a whole
        // multiple of the copied source range, matching the Values/Formulas/Formats/All paste
        // tiling behavior (PasteCommandFactory.CreateInternalPasteCommand /
        // GetSinglePasteDestinationRange) instead of only ever filling the selection's top-left
        // cell (R36-commands-paste-special-4-2).
        var destinationRange = GetSinglePasteDestinationRange(destination);
        var command = CreatePasteLinkCommand(
            internalClipboard,
            sourceSheet.Name,
            destination,
            destinationRange,
            transpose,
            keepSourceColumnWidths);

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        var pasteSize = GetPasteDimensions(internalClipboard.SourceRange, transpose);
        if (destinationRange.RowCount > pasteSize.RowCount || destinationRange.ColCount > pasteSize.ColCount)
        {
            pasteSize = (
                Math.Max(pasteSize.RowCount, destinationRange.RowCount),
                Math.Max(pasteSize.ColCount, destinationRange.ColCount));
        }

        SelectPastedRange(destination, pasteSize.RowCount, pasteSize.ColCount);
        return result;
    }

    public WorkbookCellEditResult PastePictureFromClipboardAtActiveCell(
        string? text,
        bool linkedPicture = false)
    {
        if (_workbookClipboardSession.Content is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _workbookClipboardSession.Clear();
            return new WorkbookCellEditResult(
                false,
                linkedPicture
                    ? "Paste Linked Picture requires copied FreeX cells."
                    : "Paste Picture requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

        var sourceSheet = linkedPicture
            ? Workbook.GetSheet(internalClipboard.SourceRange.Start.Sheet)
            : null;
        if (linkedPicture && sourceSheet is null)
        {
            return new WorkbookCellEditResult(
                false,
                "Paste Linked Picture source sheet was not found.",
                [],
                RecalcReport: null);
        }

        var destination = ActiveCell;
        var sourceCells = internalClipboard.PictureCells;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateGroupedSheetCommand(
                linkedPicture ? "Paste Linked Picture" : "Paste Picture",
                sheetId => new PasteRangeAsPictureCommand(
                    sheetId,
                    internalClipboard.SourceRange,
                    sourceCells,
                    RemapAddressToSheet(destination, sheetId),
                    linkedPicture,
                    sourceSheet?.Name)));
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        return result;
    }

    public bool ShouldPreferExternalClipboardImage(string? text)
    {
        // A non-empty text read means the OS clipboard still holds text we could paste; never prefer
        // an image over it.
        if (!WorkbookClipboardSession.ShouldPreferExternalImage(text))
            return false;

        // P45: otherwise the OS clipboard holds no text we can match against — either another app put
        // an IMAGE on it (TryGetTextAsync returns null) or it was cleared/emptied ("" ). In both cases
        // the internal snapshot we may have captured earlier is stale, so prefer the external image,
        // matching Excel and the WPF host (where Clipboard.GetText() returns "" and thus mismatches the
        // internal text). This is safe even for a transient empty read right after our own in-app copy:
        // the Avalonia caller gates on `ShouldPreferExternalClipboardImage(text) && TryPasteClipboard-
        // ImageAsync(...)`, so when no image is actually present it falls straight back to the internal/
        // text paste. Treating a null read as "unchanged" (the old behavior) instead swallowed a real
        // image-copied-in-another-app change on Linux/macOS.
        return true;
    }

    public WorkbookCellEditResult PasteClipboardImageAtActiveCell(
        IReadOnlyCollection<byte> pngBytes,
        int pixelWidth,
        int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);

        var destination = ActiveCell;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateGroupedSheetCommand(
                "Insert Picture",
                sheetId => ClipboardPictureService.CreateInsertCommand(
                    sheetId,
                    RemapAddressToSheet(destination, sheetId),
                    pngBytes,
                    pixelWidth,
                    pixelHeight)));
        if (!result.Success)
            return result;

        _workbookClipboardSession.Clear();
        ApplySuccessfulEditResult(result, destination);
        return result;
    }

    public WorkbookCellEditResult PasteExternalTextAtActiveCell(string text, bool preserveText = false) =>
        PasteExternalTextAtActiveCell(text, preserveText, default, html: null);

    /// <summary>
    /// Same as the <paramref name="preserveText"/>-only overload, but also honors Paste Special's
    /// Transpose / Skip Blanks / Operation for an EXTERNAL (non-FreeX) clipboard paste, matching Excel
    /// and the WPF host's <c>PasteCommandFactory.CreateExternalTextPasteCommand</c> options overload
    /// (review P46 — this shell used to reject Paste Special entirely for external clipboard text
    /// instead of applying the selected options).
    /// </summary>
    public WorkbookCellEditResult PasteExternalTextAtActiveCell(string text, bool preserveText, PasteSpecialOptions options) =>
        PasteExternalTextAtActiveCell(text, preserveText, options, html: null);

    /// <summary>
    /// Same as the <paramref name="options"/> overload, but also accepts the OS clipboard's HTML
    /// ('text/html' / CF_HTML) payload alongside the plain-text one, when the caller has it available.
    /// When <paramref name="html"/> contains a parseable &lt;table&gt;, its actual &lt;tr&gt;/&lt;td&gt;
    /// row/column structure is preferred over the plain-text tab/newline splitter
    /// (<see cref="ClipboardSerializer.Deserialize"/>): that splitter treats every bare '\r'/'\n' as a
    /// new row, which misreads a source cell whose rendered text wraps across multiple lines (or
    /// contains a literal &lt;br&gt;) as a row break, shifting every subsequent pasted row down by one.
    /// Uses the same <see cref="HtmlClipboardTableParser"/> as the WPF host so both renderer paths
    /// preserve identical table and cell-text semantics.
    /// </summary>
    public WorkbookCellEditResult PasteExternalTextAtActiveCell(
        string text, bool preserveText, PasteSpecialOptions options, string? html)
    {
        ArgumentNullException.ThrowIfNull(text);

        var destination = ActiveCell;
        var destinationRange = GetSinglePasteDestinationRange(destination);
        IReadOnlyList<IReadOnlyList<string>> rows =
            HtmlClipboardTableParser.Parse(html) is { Count: > 0 } htmlRows
                ? htmlRows
                : ClipboardSerializer.Deserialize(text).Select(static row => (IReadOnlyList<string>)row).ToList();
        var sourceRowCount = (ulong)rows.Count;
        var sourceColCount = rows.Count == 0 ? 0UL : (ulong)rows.Max(static row => row.Count);
        var pasteRowCount = options.Transpose ? sourceColCount : sourceRowCount;
        var pasteColCount = options.Transpose ? sourceRowCount : sourceColCount;
        var command = CreateExternalTextPasteCommand(destinationRange, rows, preserveText, options);
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        SelectPastedRange(
            destination,
            Math.Max(pasteRowCount, destinationRange.RowCount),
            Math.Max(pasteColCount, destinationRange.ColCount));
        return result;
    }

    public WorkbookCellEditResult ClearSelectedRangeContents()
    {
        // Built via the shared SelectionStyleCommandPlanner.CreateRangeCommand choke point (rather
        // than the single-range private CreateRangeCommand) so that Delete/Clear Contents clears
        // every disjoint area of a Ctrl+click multi-area selection, matching Excel and the WPF
        // host's TryExecuteRepeatableCurrentSelectionRangesCommand (R127-cellscmds-multiarea-style-1).
        var preservedRange = SelectedRange;
        var preservedRanges = SelectedRanges;
        var preservedActiveCell = ActiveCell;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            SelectionStyleCommandPlanner.CreateRangeCommand(
                CurrentGroupedEditSheetIds(),
                GetSelectionSizingRanges(),
                static (sheetId, sheetRange) => new ClearContentsCommand(sheetId, sheetRange),
                "Clear Contents"));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, preservedRange);
        if (preservedRanges.Count > 1)
            SelectRanges(preservedRange, preservedRanges, preservedActiveCell);
        CancelPendingCutAfterMutatingEdit();
        return result;
    }

    /// <summary>
    /// R75-commands-clear-delete-4-1: Backspace on a (possibly multi-cell) selection clears ONLY the
    /// active cell -- unlike Delete/Clear Contents (<see cref="ClearSelectedRangeContents"/>), which
    /// clears the whole selection rectangle. Matches Excel: Backspace is never a bulk-clear
    /// operation. Deliberately skips <see cref="ApplySuccessfulRangeEditResult"/> (which would
    /// collapse SelectedRange down to the single cleared cell via SetSingleSelectedRange) so the
    /// caller's existing multi-cell selection shape survives -- only the active cell's content is
    /// blanked before the caller enters inline edit on it.
    /// </summary>
    public WorkbookCellEditResult ClearActiveCellContents()
    {
        var address = ActiveCell;
        var range = new GridRange(address, address);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                range,
                "Clear Contents",
                static (sheetId, sheetRange) => new ClearContentsCommand(sheetId, sheetRange)));
        if (!result.Success)
            return result;

        FormulaEditAddress = null;
        RefreshLinkedPicturesForEditedCells(result);
        MarkDirty();
        RefreshViewport();
        CancelPendingCutAfterMutatingEdit();
        return result;
    }

    public WorkbookCellEditResult ClearSelectedRangeAll()
    {
        // Built via GetSelectionSizingRanges()/CreateClearAllCommand's multi-range overload (rather
        // than the single-range CreateClearAllCommand(SelectedRange)) so that Home>Clear>Clear All
        // clears every disjoint area of a Ctrl+click multi-area selection, matching Excel and the WPF
        // host's TryExecuteRepeatableCurrentSelectionRangesCommand (R128-cellscmds-multiarea-clear-2).
        var preservedRange = SelectedRange;
        var preservedRanges = SelectedRanges;
        var preservedActiveCell = ActiveCell;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateClearAllCommand(GetSelectionSizingRanges()));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, preservedRange);
        if (preservedRanges.Count > 1)
            SelectRanges(preservedRange, preservedRanges, preservedActiveCell);
        return result;
    }

    public WorkbookDataValidationMutationResult ClearSelectedRangeDataValidation()
    {
        var command = CreateClearSelectedRangeDataValidationCommand();
        if (command is null)
            return WorkbookDataValidationMutationResult.NoMutation();

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return WorkbookDataValidationMutationResult.FromEditResult(result, mutated: false);

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return WorkbookDataValidationMutationResult.FromEditResult(result, mutated: true);
    }

    public WorkbookCellEditResult ClearSelectedRangeFormats()
    {
        // Routed through ExecuteRepeatableEditCommand (rather than the generic
        // ApplySelectedRangeStyle(StyleDiff) used by plain style toggles) because Clear Formats must
        // also drop conditional-formatting rules, which a bare StyleDiff apply cannot express -- see
        // CreateClearFormatsCommand. The factory re-reads GetSelectionSizingRanges() each time it
        // runs, matching ApplySelectedRangeStyle's F4/Repeat Last Action semantics, and (via the
        // multi-range overload) clears every disjoint area of a Ctrl+click multi-area selection,
        // matching Excel and the WPF host's TryExecuteRepeatableCurrentSelectionRangesCommand
        // (R128-cellscmds-multiarea-clear-2).
        var preservedRange = SelectedRange;
        var preservedRanges = SelectedRanges;
        var preservedActiveCell = ActiveCell;
        var result = _cellEditService.ExecuteRepeatableEditCommand(
            Workbook,
            () => CreateClearFormatsCommand(GetSelectionSizingRanges()));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, preservedRange);
        if (preservedRanges.Count > 1)
            SelectRanges(preservedRange, preservedRanges, preservedActiveCell);
        return result;
    }

    public WorkbookCellEditResult ClearSelectedRangeComments()
    {
        // Built via the shared SelectionStyleCommandPlanner.CreateRangeCommand choke point (rather
        // than the single-range private CreateRangeCommand) so that Clear Comments and Notes clears
        // every disjoint area of a Ctrl+click multi-area selection, matching Excel and the WPF host's
        // TryExecuteRepeatableCurrentSelectionRangesCommand (R128-cellscmds-multiarea-clear-2).
        var preservedRange = SelectedRange;
        var preservedRanges = SelectedRanges;
        var preservedActiveCell = ActiveCell;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            SelectionStyleCommandPlanner.CreateRangeCommand(
                CurrentGroupedEditSheetIds(),
                GetSelectionSizingRanges(),
                static (sheetId, sheetRange) => new ClearCommentsCommand(sheetId, sheetRange),
                "Clear Comments and Notes"));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, preservedRange);
        if (preservedRanges.Count > 1)
            SelectRanges(preservedRange, preservedRanges, preservedActiveCell);
        return result;
    }

    /// <summary>Set (or replace) the legacy note on the active cell.</summary>
    public WorkbookCellEditResult SetActiveCellNote(string text)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetCommentCommand(ActiveSheet.Id, ActiveCell, text));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    /// <summary>Add (or replace) a threaded comment on the active cell.</summary>
    public WorkbookCellEditResult SetActiveCellThreadedComment(string text)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetThreadedCommentCommand(ActiveSheet.Id, ActiveCell, text));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    /// <summary>Current legacy note text on the active cell, or <c>null</c> when there is none.</summary>
    public string? GetActiveCellNote() =>
        ActiveSheet.Comments.TryGetValue(ActiveCell, out var note) ? note : null;

    /// <summary>Current root text of the active cell threaded comment, or <c>null</c> when there is none.</summary>
    public string? GetActiveCellThreadedCommentText() =>
        ActiveSheet.ThreadedComments.TryGetValue(ActiveCell, out var comment) ? comment.Text : null;

    /// <summary>Whether the active cell's threaded comment exists and is currently resolved.</summary>
    public bool IsActiveCellThreadedCommentResolved() =>
        ActiveSheet.ThreadedComments.TryGetValue(ActiveCell, out var comment) && comment.IsResolved;

    /// <summary>Replace the root text of the active cell's existing threaded comment.</summary>
    public WorkbookCellEditResult EditActiveCellThreadedComment(string text)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new UpdateThreadedCommentTextCommand(ActiveSheet.Id, ActiveCell, text));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    /// <summary>Toggle the resolved state of the active cell's existing threaded comment.</summary>
    public WorkbookCellEditResult SetActiveCellThreadedCommentResolved(bool resolved)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new ResolveThreadedCommentCommand(ActiveSheet.Id, ActiveCell, resolved));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult ClearSelectedRangeHyperlinks()
    {
        // Built via the shared SelectionStyleCommandPlanner.CreateRangeCommand choke point (rather
        // than the single-range private CreateRangeCommand) so that the right-click "Remove
        // Hyperlink" item clears every disjoint area of a Ctrl+click multi-area selection, matching
        // Excel and the WPF host's TryExecuteRepeatableCurrentSelectionRangesCommand
        // (R128-cellscmds-multiarea-clear-2).
        var preservedRange = SelectedRange;
        var preservedRanges = SelectedRanges;
        var preservedActiveCell = ActiveCell;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            SelectionStyleCommandPlanner.CreateRangeCommand(
                CurrentGroupedEditSheetIds(),
                GetSelectionSizingRanges(),
                static (sheetId, sheetRange) => new ClearHyperlinksCommand(sheetId, sheetRange),
                "Clear Hyperlinks"));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, preservedRange);
        if (preservedRanges.Count > 1)
            SelectRanges(preservedRange, preservedRanges, preservedActiveCell);
        return result;
    }

    /// <summary>
    /// Excel's Home&gt;Clear&gt;Remove Hyperlinks (and the equivalent right-click Clear submenu entry)
    /// strips the cell's hyperlink AND its blue/underline formatting -- unlike right-click's top-level
    /// "Remove Hyperlink" item (<see cref="ClearSelectedRangeHyperlinks"/>), which keeps the formatting.
    /// </summary>
    public WorkbookCellEditResult RemoveSelectedRangeHyperlinks()
    {
        // Built via the shared SelectionStyleCommandPlanner.CreateRangeCommand choke point (rather
        // than the single-range private CreateRangeCommand) so that Home>Clear>Clear Hyperlinks (the
        // ribbon-wired entry point -- see MainWindow.cs's "Clear Hyperlinks" menu/flyout wiring, which
        // calls this method) clears every disjoint area of a Ctrl+click multi-area selection, matching
        // Excel and the WPF host's ClearHyperlinksMenuItem_Click/
        // TryExecuteRepeatableCurrentSelectionRangesCommand (R128-cellscmds-multiarea-clear-2).
        var preservedRange = SelectedRange;
        var preservedRanges = SelectedRanges;
        var preservedActiveCell = ActiveCell;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            SelectionStyleCommandPlanner.CreateRangeCommand(
                CurrentGroupedEditSheetIds(),
                GetSelectionSizingRanges(),
                static (sheetId, sheetRange) => new RemoveHyperlinksCommand(sheetId, sheetRange),
                "Remove Hyperlinks"));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, preservedRange);
        if (preservedRanges.Count > 1)
            SelectRanges(preservedRange, preservedRanges, preservedActiveCell);
        return result;
    }

    public HyperlinkDialogPrefill GetSelectedRangeHyperlinkDialogPrefill() =>
        HyperlinkDialogPrefill.FromCell(ActiveSheet, SelectedRange.Start);

    public WorkbookCellEditResult SetSelectedRangeHyperlink(HyperlinkDialogPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var range = SelectedRange;
        var metadata = new HyperlinkMetadata(plan.LinkType, plan.ScreenTip, plan.Bookmark);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateSetHyperlinkCommand(range, plan.Target, plan.DisplayText, metadata));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    /// <summary>
    /// R127C-fillcmds-multiarea-gate-2: mirrors <see cref="FillSelectedRange"/>'s own multi-area
    /// resolution (<see cref="SelectionStyleCommandPlanner.ResolveRanges"/>) instead of checking
    /// only the single "active" <see cref="SelectedRange"/>. On a Ctrl+click multi-area selection
    /// where the active area is too small to fill (e.g. a single cell) but a disjoint sibling area
    /// qualifies, the execution path (FillSelectedRange) already fills the sibling area correctly --
    /// this predicate must agree, or every ribbon/menu consumer that gates on it (the Avalonia Fill
    /// Cells split-button and its Down/Right/Up/Left/Series flyout items) renders the control wrongly
    /// disabled even though invoking Fill would succeed.
    /// </summary>
    public bool CanFillSelectedRange(FillCellsDirection direction)
    {
        var areas = SelectionStyleCommandPlanner.ResolveRanges(SelectedRange, SelectedRanges);
        if (areas.Count == 0)
            areas = [SelectedRange];
        return areas.Any(area => CanFill(area, direction));
    }

    private static bool CanFill(GridRange range, FillCellsDirection direction) =>
        direction switch
        {
            FillCellsDirection.Down or FillCellsDirection.Up => range.RowCount > 1,
            FillCellsDirection.Right or FillCellsDirection.Left => range.ColCount > 1,
            _ => false
        };

    public bool CanSortSelectedRange => SelectedRange.RowCount > 1;

    /// <summary>True when the active sheet currently has an AutoFilter (filter dropdowns) enabled.</summary>
    public bool ActiveSheetHasAutoFilter => ActiveSheet.AutoFilter is not null;

    /// <summary>
    /// Toggles the active sheet's AutoFilter over the effective range: the existing AutoFilter range when one
    /// is set (to disable it), the current region around a single-cell selection, or the selected range.
    /// </summary>
    public WorkbookCellEditResult ToggleSelectedRangeAutoFilter() =>
        ExecuteReviewCommand(new ToggleWorksheetAutoFilterCommand(
            ActiveSheet.Id,
            AutoFilterToggleRangePlanner.Create(ActiveSheet, SelectedRange)));

    /// <summary>
    /// R127-services-sort-multiarea-1: real Excel refuses Sort outright on a Ctrl+click multi-area
    /// selection ("This operation is not allowed on multiple selections. Select a single range and
    /// click the command again."). Both SortSelectedRange overloads previously read only
    /// SelectedRange, so a Sort silently reordered rows in the active area alone while every other
    /// selected area was left completely untouched and unwarned -- worse than a no-op if the areas
    /// held related data the user expected to stay row-aligned. Mirrors the WPF host's identical
    /// refusal (MainWindow.DataFilterCommands.TryRejectMultiAreaSort) and this class's own
    /// CreateMultiRangeClipboardError refusal for multi-area Copy/Cut/Paste Special.
    /// </summary>
    private bool TryCreateMultiAreaSortRejection(out WorkbookCellEditResult rejection)
    {
        if (GetCurrentSelectedRanges().Count <= 1)
        {
            rejection = default!;
            return false;
        }

        rejection = new WorkbookCellEditResult(false, CreateMultiRangeClipboardError("Sort"), [], RecalcReport: null);
        return true;
    }

    public WorkbookCellEditResult SortSelectedRange(bool ascending)
    {
        if (TryCreateMultiAreaSortRejection(out var multiAreaRejection))
            return multiAreaRejection;

        if (!CanSortSelectedRange)
        {
            return new WorkbookCellEditResult(
                false,
                "Select at least two rows to sort.",
                [],
                RecalcReport: null);
        }

        var range = SelectedRange;
        var sortPlan = QuickSortRangePlanner.Create(ActiveSheet, range, ActiveCell);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                sortPlan.Range,
                "Sort",
                (sheetId, sheetRange) => new SortCommand(
                    sheetId,
                    sheetRange,
                    sortPlan.SortByColOffset,
                    ascending)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SortSelectedRange(IReadOnlyList<CoreSortKey> sortKeys, SortOptions options, bool hasHeaders)
    {
        ArgumentNullException.ThrowIfNull(sortKeys);
        ArgumentNullException.ThrowIfNull(options);

        return SortSelectedRange(SortDialogPlanner.CreateCommandPlan(sortKeys, options, hasHeaders));
    }

    public WorkbookCellEditResult SortSelectedRange(SortDialogCommandPlan sortPlan)
    {
        ArgumentNullException.ThrowIfNull(sortPlan);

        if (TryCreateMultiAreaSortRejection(out var multiAreaRejection))
            return multiAreaRejection;

        if (!CanSortSelectedRange)
        {
            return new WorkbookCellEditResult(
                false,
                "Select at least two rows to sort.",
                [],
                RecalcReport: null);
        }

        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                range,
                "Sort",
                sortPlan.CreateCommand));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    /// <summary>
    /// Fill Down/Up/Left/Right on a Ctrl+click multi-area selection fills EVERY disjoint area
    /// independently from its own edge, not just the "active" area <see cref="SelectedRange"/>
    /// exposes (R127-fillcmds-multiarea-1, mirrors the WPF host's ExecuteFillCells and this
    /// session's own R124/R126 multi-area Group/Ungroup and Row Height/Column Width fixes for the
    /// same <see cref="SelectionStyleCommandPlanner.ResolveRanges"/> choke point). Areas too small
    /// to fill in the requested direction (e.g. a single-row area for Fill Down) are skipped
    /// rather than failing the whole multi-area operation -- Excel simply leaves an undersized
    /// area alone instead of erroring out the whole fill. When NO area qualifies (including the
    /// ordinary single-area case), this reports the same "must include at least one target cell"
    /// failure FillCellsCommand itself would have reported -- SelectionStyleCommandPlanner.
    /// CreateRangeCommand degrades an empty range list to a silent no-op composite, which would
    /// otherwise turn today's error into a false "success", regressing the plain single-area case.
    /// </summary>
    public WorkbookCellEditResult FillSelectedRange(FillCellsDirection direction)
    {
        var range = SelectedRange;
        var areas = SelectionStyleCommandPlanner.ResolveRanges(SelectedRange, SelectedRanges);
        if (areas.Count == 0)
            areas = [range];
        areas = areas.Where(area => CanFill(area, direction)).ToList();

        if (areas.Count == 0)
            return new WorkbookCellEditResult(
                false,
                "The fill range must include at least one target cell.",
                [],
                RecalcReport: null);

        var command = SelectionStyleCommandPlanner.CreateRangeCommand(
            CurrentGroupedEditSheetIds(),
            areas,
            (sheetId, sheetRange) => new FillCellsCommand(sheetId, sheetRange, direction),
            WorksheetCommandPresentationCatalog.DescribeFill(direction).CommandTitle);

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    /// <summary>
    /// Fills <paramref name="fillRange"/> by continuing/repeating the series in
    /// <paramref name="sourceRange"/> (numeric/date linear-fit trend, list series, or formula
    /// offset), matching Excel's fill-handle drag behavior. Used by the fill-handle drag gesture,
    /// which - unlike keyboard/menu Fill Down/Up/Left/Right (<see cref="FillSelectedRange"/>) -
    /// must run full series detection (<see cref="AutofillCommand"/>) rather than a verbatim
    /// edge-cell copy (<see cref="FillCellsCommand"/>), mirroring the WPF host's
    /// <c>OnAutofillRequested</c>.
    /// </summary>
    public WorkbookCellEditResult AutofillDragRange(GridRange sourceRange, GridRange fillRange, bool ctrlHeld = false)
    {
        if (!sourceRange.Start.Sheet.Equals(ActiveSheet.Id) ||
            !sourceRange.End.Sheet.Equals(ActiveSheet.Id) ||
            !fillRange.Start.Sheet.Equals(ActiveSheet.Id) ||
            !fillRange.End.Sheet.Equals(ActiveSheet.Id))
        {
            return new WorkbookCellEditResult(false, "Autofill source and fill range must be on the active sheet.", [], RecalcReport: null);
        }

        var completedSelection = FreeX.App.Presentation.GridInteraction.GridAutofillPlanner.CalculateCompletedSelectionRange(sourceRange, fillRange);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new AutofillCommand(ActiveSheet.Id, sourceRange, fillRange, ctrlHeld));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, completedSelection);
        return result;
    }

    public WorkbookCellEditResult MoveSelectedRangeTo(GridRange sourceRange, GridRange targetRange)
    {
        if (!sourceRange.Start.Sheet.Equals(ActiveSheet.Id) ||
            !sourceRange.End.Sheet.Equals(ActiveSheet.Id) ||
            !targetRange.Start.Sheet.Equals(ActiveSheet.Id) ||
            !targetRange.End.Sheet.Equals(ActiveSheet.Id))
        {
            return new WorkbookCellEditResult(false, "Move source and destination must be on the active sheet.", [], RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new MoveRangeCommand(ActiveSheet.Id, sourceRange, targetRange.Start));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, targetRange);
        return result;
    }

    public WorkbookCellEditResult FlashFillSelectedRange()
    {
        var range = SelectedRange;
        var commands = new List<IWorkbookCommand>();
        var hasExamples = false;
        foreach (var sheetId in CurrentGroupedEditSheetIds())
        {
            var sheet = Workbook.GetSheet(sheetId)!;
            var sheetRange = RemapRangeToSheet(range, sheetId);
            var plan = FlashFillRangePlanner.Plan(sheet, sheetRange);
            hasExamples |= FlashFillRangePlanner.HasExamples(sheet, plan);
            if (FlashFillRangePlanner.HasFillTargets(sheet, plan))
                commands.Add(plan.CreateCommand(sheetId));
        }

        if (commands.Count == 0)
        {
            if (!hasExamples)
            {
                return new WorkbookCellEditResult(
                    false,
                    "No examples found. Type at least one value in the fill column.",
                    [],
                    RecalcReport: null);
            }

            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            ToCommand("Flash Fill", commands));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public bool CaptureFormatPainterSource(bool persistent = false)
    {
        _formatPainterSourceSheetId = ActiveSheet.Id;
        _formatPainterSourceRange = SelectedRange;
        _formatPainterPersistent = persistent;
        return true;
    }

    public void CancelFormatPainter()
    {
        _formatPainterSourceSheetId = null;
        _formatPainterSourceRange = null;
        _formatPainterPersistent = false;
    }

    public WorkbookCellEditResult ApplyFormatPainterToSelectedRange()
    {
        if (_formatPainterSourceSheetId is not { } sourceSheetId ||
            _formatPainterSourceRange is not { } sourceRange)
        {
            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);
        }

        var sourceSheet = Workbook.GetSheet(sourceSheetId);
        if (sourceSheet is null)
        {
            CancelFormatPainter();
            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);
        }

        var targetRange = SelectedRange;
        var targetRanges = GetCurrentSelectedRanges();
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateFormatPainterCommand(sourceSheet, sourceRange, targetRanges));
        if (!result.Success)
        {
            if (!_formatPainterPersistent)
                CancelFormatPainter();
            return result;
        }

        ApplySuccessfulRangeEditResult(result, targetRange);
        if (!_formatPainterPersistent)
            CancelFormatPainter();
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeBold(bool enabled) =>
        ApplySelectedRangeStyle(new StyleDiff(Bold: enabled));

    public WorkbookCellEditResult SetSelectedRangeItalic(bool enabled) =>
        ApplySelectedRangeStyle(new StyleDiff(Italic: enabled));

    public WorkbookCellEditResult SetSelectedRangeUnderline(bool enabled) =>
        ApplySelectedRangeStyle(CreateUnderlineStyleDiff(enabled));

    public WorkbookCellEditResult SetSelectedRangeStrikethrough(bool enabled) =>
        ApplySelectedRangeStyle(CreateStrikethroughStyleDiff(enabled));

    public WorkbookCellEditResult SetSelectedRangeDoubleUnderline(bool enabled) =>
        ApplySelectedRangeStyle(CreateDoubleUnderlineStyleDiff(enabled));

    public WorkbookCellEditResult SetSelectedRangeHorizontalAlignment(HorizontalAlignment alignment) =>
        ApplySelectedRangeStyle(new StyleDiff(HAlign: alignment));

    public WorkbookCellEditResult SetSelectedRangeVerticalAlignment(VerticalAlignment alignment) =>
        ApplySelectedRangeStyle(new StyleDiff(VAlign: alignment));

    public WorkbookCellEditResult SetSelectedRangeWrapText(bool enabled)
    {
        // Not routed through ApplySelectedRangeStyle: when enabling wrap, the style change and the
        // Excel-matching row-height auto-grow (below) must land as a single undoable/repeatable
        // operation, so both are folded into one command up front rather than applied as two
        // separate edits.
        var preservedRange = SelectedRange;
        var preservedRanges = SelectedRanges;
        var preservedActiveCell = ActiveCell;
        var result = _cellEditService.ExecuteRepeatableEditCommand(
            Workbook,
            () => CreateWrapTextCommand(enabled));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, SelectedRange);
        if (preservedRanges.Count > 1)
            SelectRanges(preservedRange, preservedRanges, preservedActiveCell);
        return result;
    }

    /// <summary>
    /// Applies WrapText to every disjoint area of the current selection via the shared
    /// SelectionStyleCommandPlanner choke point (R127-cellscmds-multiarea-style-1), same as
    /// <see cref="ApplySelectedRangeStyle"/>. The per-area row-height auto-grow is planned once per
    /// area (a row shared by two disjoint areas is grown against whichever area's plan runs last).
    /// </summary>
    private IWorkbookCommand CreateWrapTextCommand(bool enabled)
    {
        var ranges = GetSelectionSizingRanges();
        var commands = new List<IWorkbookCommand>
        {
            SelectionStyleCommandPlanner.CreateApplyStyleCommand(
                CurrentGroupedEditSheetIds(),
                ranges,
                new StyleDiff(WrapText: enabled),
                "Wrap Text"),
        };
        if (enabled)
        {
            foreach (var range in ranges)
                commands.AddRange(CreateWrapTextGrowthCommands(range));
        }

        return ToCommand("Wrap Text", commands);
    }

    /// <summary>
    /// Turning on Wrap Text auto-grows each affected row to fit the now-wrapped content, matching
    /// Excel's "row grows unless you've manually resized it" behavior. Reuses the exact same
    /// content-based estimate (RowColumnSizingPlanner/AutoFitSizingService) as the explicit "AutoFit
    /// Row Height" command — but since this runs before the WrapText style diff above has been
    /// applied, the display-text lookup is wrapped to report WrapText=true for the cells being
    /// toggled on. Only ever grows a row: any row whose estimate doesn't exceed its current height
    /// (including a row a user previously resized taller by hand) is left untouched, matching Excel
    /// never shrinking a row just from toggling wrap on.
    /// </summary>
    private IReadOnlyList<IWorkbookCommand> CreateWrapTextGrowthCommands(GridRange range)
    {
        var sheet = ActiveSheet;
        var plans = Ribbon.RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            range,
            sheet.GetUsedRange(),
            (row, col) => GetAutoFitDisplayTextForPendingWrap(row, col, range),
            sheet.DefaultRowHeight);

        return plans
            .Where(plan => plan.Size > (sheet.RowHeights.TryGetValue(plan.Index, out var currentHeight) ? currentHeight : sheet.DefaultRowHeight))
            .Select(plan => (IWorkbookCommand)new SetRowHeightCommand(sheet.Id, plan.Index, plan.Index, plan.Size))
            .ToList();
    }

    private AutoFitCellText? GetAutoFitDisplayTextForPendingWrap(uint row, uint col, GridRange pendingWrapRange)
    {
        if (GetAutoFitDisplayText(ActiveSheet, row, col) is not { } cellText)
            return null;

        return pendingWrapRange.Contains(new CellAddress(ActiveSheet.Id, row, col))
            ? cellText with { WrapText = true }
            : cellText;
    }

    public WorkbookCellEditResult IncreaseSelectedRangeIndent() =>
        SetSelectedRangeIndentLevel(Math.Min(15, SelectedRangeStartIndentLevel + 1));

    public WorkbookCellEditResult DecreaseSelectedRangeIndent() =>
        SetSelectedRangeIndentLevel(Math.Max(0, SelectedRangeStartIndentLevel - 1));

    public WorkbookCellEditResult SetSelectedRangeIndentLevel(int indentLevel)
        => ApplySelectedRangeStyle(new StyleDiff(IndentLevel: Math.Clamp(indentLevel, 0, 15)));

    public WorkbookCellEditResult SetSelectedRangeNumberFormat(string numberFormat)
    {
        ArgumentNullException.ThrowIfNull(numberFormat);

        return ApplySelectedRangeStyle(new StyleDiff(NumberFormat: numberFormat));
    }

    public WorkbookCellEditResult SetSelectedRangeTextRotation(int textRotation)
        => ApplySelectedRangeStyle(new StyleDiff(TextRotation: textRotation));

    public WorkbookCellEditResult SetSelectedRangeCellStylePreset(CellStylePreset preset)
    {
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(preset, Workbook.Theme);
        return ApplySelectedRangeStyle(diff);
    }

    // R128-services-multiarea-compactformat-1: a Ctrl+click multi-area selection (SelectedRanges) must
    // have the Border-preset gallery, Format Cells dialog apply, and Lock/Unlock Cell toggle -- the three
    // Avalonia entry points that all funnel through this shared method -- act on EVERY disjoint area, not
    // just the active SelectedRange, matching Excel and the WPF host's own ApplyRangeBorderPreset /
    // ApplyFormatCellsDialogResult (which both enumerate GetCurrentSelectionRanges()). Routed through the
    // same GetSelectionSizingRanges()/SelectionStyleCommandPlanner choke point R127 already gave the
    // sibling ApplySelectedRangeStyle (R127-cellscmds-multiarea-style-1): style/border-preset/font-size/
    // merge commands are built per area and combined into one composite so undo/redo and the recalc pass
    // still see a single atomic edit.
    public WorkbookCellEditResult ApplySelectedRangeCompactFormat(
        StyleDiff diff,
        CellBorderPreset? borderPreset,
        BorderStyle borderStyle = BorderStyle.Thin,
        CellColor? borderColor = null,
        bool? mergeCells = null,
        MergeCellContentResolution mergeContentResolution = MergeCellContentResolution.KeepFirstCell)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var range = SelectedRange;
        var preservedRanges = SelectedRanges;
        var preservedActiveCell = ActiveCell;
        var areas = GetSelectionSizingRanges();
        var commands = new List<IWorkbookCommand>();
        var remainingDiff = diff.FontSize is null ? diff : diff with { FontSize = null };
        var hasStyleChanges = HasStyleDiffChanges(remainingDiff);
        var fittingRowHeight = diff.FontSize is { } fontSizeForRowHeight ? GetFittingRowHeight(fontSizeForRowHeight) : 0;

        foreach (var area in areas)
        {
            if (hasStyleChanges)
                commands.Add(CreateApplyStyleCommand(area, remainingDiff));

            if (borderPreset is { } preset && HasBorderPresetChanges(area, preset, borderStyle, borderColor))
                commands.Add(CreateBorderPresetCommand(area, preset, borderStyle, borderColor));

            if (diff.FontSize is { } fontSize)
                commands.Add(CreateSetFontSizeCommand(area, fontSize, fittingRowHeight));

            if (mergeCells is { } shouldMerge)
                commands.AddRange(CreateFormatCellsMergeCommands(area, shouldMerge, mergeContentResolution));
        }

        if (commands.Count == 0)
            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new CompositeWorkbookCommand("Format Cells", commands));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        if (preservedRanges.Count > 1)
            SelectRanges(range, preservedRanges, preservedActiveCell);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeBorderPreset(CellBorderPreset preset)
    {
        var range = SelectedRange;
        if (!HasBorderPresetChanges(range, preset))
            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateBorderPresetCommand(range, preset));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    /// <summary>
    /// Applies "Draw Border" (outline edges only) to the currently selected range, using the same
    /// per-cell outline diff that <c>BorderDrawPlanner</c> uses in the WPF shell: each cell in the
    /// range gets only the edges that lie on the range boundary.
    /// </summary>
    public WorkbookCellEditResult SetSelectedRangeDrawBorder(
        BorderStyle borderStyle = BorderStyle.Thin,
        CellColor? borderColor = null)
    {
        const string label = "Draw Border";
        var range = SelectedRange;
        var color = borderColor ?? CellColor.Black;
        var targetSheetIds = CurrentGroupedEditSheetIds();

        var commands = new List<IWorkbookCommand>();
        foreach (var address in range.AllCells())
        {
            var diff = BorderShortcutService.GetOutlineBorderDiff(range, address, borderStyle, color);
            if (!BorderShortcutService.HasBorderChanges(diff))
                continue;

            var cellRange = new GridRange(address, address);
            commands.Add(targetSheetIds.Count > 1
                ? new GroupedApplyStyleCommand(targetSheetIds, cellRange, diff)
                : new ApplyStyleCommand(
                    ActiveSheet.Id,
                    RemapRangeToSheet(cellRange, ActiveSheet.Id),
                    diff));
        }

        if (commands.Count == 0)
            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);

        var command = commands.Count == 1
            ? commands[0]
            : (IWorkbookCommand)new CompositeWorkbookCommand(label, commands);

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    // R127-services-multiarea-merge-1: a Ctrl+click multi-area selection (SelectedRanges) must have
    // Merge & Center / Unmerge Cells act on EVERY disjoint area, not just the active SelectedRange --
    // Excel merges/unmerges each selected block independently. GetCurrentSelectedRanges is the same
    // SelectedRanges/SelectedRange fallback choke point the R124/R126/R127 multi-area Group/Ungroup,
    // Row Height/Column Width and style fixes already use in this class, mirroring the WPF host's
    // equivalent MainWindow.HomeFormatting.cs fix.
    public WorkbookCellEditResult MergeAndCenterSelectedRange(
        MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
    {
        var range = SelectedRange;
        var areas = GetCurrentSelectedRanges();
        var areaCommands = areas.Select(area => CreateMergeAndCenterCommand(area, contentResolution)).ToList();
        var command = CellMergePlanner.WrapCommands("Merge & Center", areaCommands);
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult UnmergeSelectedRange()
    {
        var range = SelectedRange;
        var areas = GetCurrentSelectedRanges();
        var commands = areas.SelectMany(CreateUnmergeCommands).ToList();
        if (commands.Count == 0)
            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CellMergePlanner.WrapCommands("Unmerge Cells", commands));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult IncreaseSelectedRangeDecimalPlaces() =>
        SetSelectedRangeNumberFormat(NumberFormatDecimalAdjuster.AddDecimalPlace(SelectedRangeStartNumberFormat));

    public WorkbookCellEditResult DecreaseSelectedRangeDecimalPlaces() =>
        SetSelectedRangeNumberFormat(NumberFormatDecimalAdjuster.RemoveDecimalPlace(SelectedRangeStartNumberFormat));

    public WorkbookCellEditResult IncreaseSelectedRangeFontSize() =>
        SetSelectedRangeFontSize(FontSizePlanner.Increase(SelectedRangeStartFontSize));

    public WorkbookCellEditResult DecreaseSelectedRangeFontSize() =>
        SetSelectedRangeFontSize(FontSizePlanner.Decrease(SelectedRangeStartFontSize));

    public WorkbookCellEditResult SetSelectedRangeFontSize(double fontSize)
    {
        var range = SelectedRange;
        var rowHeight = GetFittingRowHeight(fontSize);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateSetFontSizeCommand(range, fontSize, rowHeight));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SetSelectedRangeFontColor(CellColor fontColor) =>
        ApplySelectedRangeStyle(new StyleDiff(FontColor: fontColor));

    /// <summary>Applies a font family (typeface) to the selection. A blank name is a no-op success.</summary>
    public WorkbookCellEditResult SetSelectedRangeFontName(string fontName) =>
        string.IsNullOrWhiteSpace(fontName)
            ? new WorkbookCellEditResult(true, null, [], null)
            : ApplySelectedRangeStyle(new StyleDiff(FontName: fontName.Trim()));

    public WorkbookCellEditResult SetSelectedRangeFillColor(CellColor fillColor) =>
        ApplySelectedRangeStyle(new StyleDiff(FillColor: fillColor));

    public WorkbookCellEditResult ClearSelectedRangeFill() =>
        ApplySelectedRangeStyle(new StyleDiff(ClearFill: true));

    public WorkbookCellEditResult UndoLastEdit()
    {
        var sheetIdsBefore = CaptureSheetIds();
        var hiddenStatesBefore = CaptureSheetHiddenStates();
        var result = _cellEditService.UndoLastEdit(Workbook);
        if (!result.Success)
            return result;

        ApplySuccessfulHistoryResult(result, sheetIdsBefore, hiddenStatesBefore);
        // R126-services-clipboard-formats-undo-1: Undo is exactly the kind of mutating edit
        // CancelPendingCutAfterMutatingEdit already exists to guard against (see its own doc
        // comment / R66-services-clipboard-formats-6-2) -- it can revert a cell inside a still-
        // pending Cut's source range, and without this a subsequent Paste would silently MOVE (and
        // blank out) that range using content the user just explicitly undid away from it. Mirrors
        // CommitCellText/ClearSelectedRangeContents/ClearActiveCellContents above, which all call
        // this immediately after a successful mutation.
        CancelPendingCutAfterMutatingEdit();
        // ApplySuccessfulHistoryResult always ends by calling MarkDirty(); restore the clean state
        // when the undo stack has returned to the last save point (WPF host parity — see
        // TryMarkCleanIfAtSavePoint).
        TryMarkCleanIfAtSavePoint();
        return result;
    }

    public WorkbookCellEditResult RedoLastEdit()
    {
        var sheetIdsBefore = CaptureSheetIds();
        var hiddenStatesBefore = CaptureSheetHiddenStates();
        var result = _cellEditService.RedoLastEdit(Workbook);
        if (!result.Success)
            return result;

        ApplySuccessfulHistoryResult(result, sheetIdsBefore, hiddenStatesBefore);
        // R126-services-clipboard-formats-undo-1: see the matching comment in UndoLastEdit() above.
        CancelPendingCutAfterMutatingEdit();
        // Same rationale as UndoLastEdit(): restore the clean state when redo returns the stack to
        // the save point (e.g. undo past the save point then redo back to it).
        TryMarkCleanIfAtSavePoint();
        return result;
    }

    public bool CanSaveCurrentSource(out FileSaveTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
            return false;

        return TryResolveSaveTarget(CurrentFilePath, out target, out _);
    }

    public bool TryResolveOpenTarget(string path, out WorkbookOpenTarget? target, out string message) =>
        TryResolveOpenTarget(path, fileAccessIdentity: null, out target, out message);

    public bool TryResolveOpenTarget(
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity,
        out WorkbookOpenTarget? target,
        out string message) =>
        WorkbookOpenTargetPlanner.TryCreateOpenTarget(
            _adapters,
            path,
            fileAccessIdentity,
            out target,
            out message);

    public bool TryResolveSaveTarget(string path, out FileSaveTarget? target, out string message)
    {
        target = null;
        if (!FileSavePlanner.TryResolveExistingPath(path, _adapters, out var resolvedTarget) ||
            resolvedTarget is null)
        {
            message = "Unsupported save format.";
            return false;
        }

        if (!CanWriteTarget(resolvedTarget.Path, out message))
            return false;

        target = resolvedTarget;
        message = "";
        return true;
    }

    public void MarkSaved(string path, WorkbookFileAccessIdentity? fileAccessIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var resolvedIdentity = ResolveSavedFileAccessIdentity(path, fileAccessIdentity);
        CurrentFilePath = path;
        CurrentFileAccessIdentity = resolvedIdentity;
        CurrentXlsxFeatureReport = null;
        Workbook.Name = Path.GetFileName(path);
        RecordUndoSavePoint();
        NotifyWorkbookChanged();
    }

    /// <summary>
    /// Marks the workbook saved only when no edits arrived during an async save.
    /// Applies file-context (path, name) unconditionally when the workbook reference is unchanged.
    /// </summary>
    /// <param name="generationAtSaveStart">
    ///   The <see cref="DirtyGeneration"/> value captured just before the save awaited.
    /// </param>
    /// <param name="path">The file path the workbook was written to.</param>
    /// <param name="fileAccessIdentity">Optional file-access identity for the saved file.</param>
    /// <returns>
    ///   <c>true</c> when the workbook was marked saved (no mid-save edits);
    ///   <c>false</c> when the dirty flag was preserved due to edits arriving during save.
    /// </returns>
    public bool TryMarkSavedIfNoEditsArrived(
        int generationAtSaveStart,
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity = null)
    {
        var plan = CreateSaveCompletionPlan(generationAtSaveStart, path, fileAccessIdentity);
        return ApplySaveCompletion(plan);
    }

    public SaveCompletionPlan CreateSaveCompletionPlan(
        int generationAtSaveStart,
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var resolvedIdentity = ResolveSavedFileAccessIdentity(path, fileAccessIdentity);
        return SaveCompletionPlanner.Plan(
            generationAtSaveStart,
            DirtyGeneration,
            sameWorkbook: true,
            path,
            resolvedIdentity,
            displayName: Path.GetFileName(path));
    }

    public bool ApplySaveCompletion(SaveCompletionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.MarkSaved)
            RecordUndoSavePoint();

        if (plan.ApplyFileContext && plan.FileContext is { } fileContext)
        {
            CurrentFilePath = fileContext.Path;
            CurrentFileAccessIdentity = fileContext.FileAccessIdentity;
            CurrentXlsxFeatureReport = null;
            Workbook.Name = fileContext.DisplayName;
        }

        if (plan.MarkSaved || plan.ApplyFileContext)
            NotifyWorkbookChanged();

        return plan.MarkSaved;
    }

    public string BuildSuggestedSaveAsFileName(string defaultExtension)
    {
        return WorkbookFilePickerPlanner.BuildSuggestedSaveAsFileName(
            Workbook.Name,
            DisplayName,
            defaultExtension);
    }

    public static string EnsureSaveExtension(string path, string defaultExtension)
    {
        try
        {
            return string.IsNullOrWhiteSpace(Path.GetExtension(path))
                ? path + FileFormatResolver.NormalizeExtension(defaultExtension)
                : path;
        }
        catch (ArgumentException)
        {
            return path;
        }
        catch (NotSupportedException)
        {
            return path;
        }
        catch (PathTooLongException)
        {
            return path;
        }
    }

    private void RefreshViewport()
    {
        Viewport = BuildViewport();
    }

    /// <summary>Forces a full recalculation of all formulas (Formulas ▸ Calculate Now / F9) and refreshes the view.</summary>
    public void RecalculateWorkbook()
    {
        _cellEditService.RecalculateAll(Workbook);
        _selectionStatsRevision++;
        RefreshViewport();
    }

    /// <summary>Runs Calculate Now (F9) against the dirty dependency graph.</summary>
    public void RecalculateDirtyCells()
    {
        _cellEditService.RecalculateDirty(Workbook);
        _selectionStatsRevision++;
        RefreshViewport();
    }

    /// <summary>Forces a recalculation of the active sheet's formulas (Shift+F9 / Calculate Sheet) and refreshes the view.</summary>
    public void RecalculateActiveSheet()
    {
        _cellEditService.RecalculateSheet(Workbook, ActiveSheet.Id);
        _selectionStatsRevision++;
        RefreshViewport();
    }

    /// <summary>
    /// Applies normal post-edit calculation policy to cells changed outside the command pipeline.
    /// Prefer session command APIs for undoable mutations.
    /// </summary>
    public RecalcReport? RecalculateChangedCells(IReadOnlyList<CellAddress> changedCells)
    {
        ArgumentNullException.ThrowIfNull(changedCells);

        var report = _cellEditService.RecalculateAfterChanges(Workbook, changedCells);
        if (report is null)
            return null;

        RefreshLinkedPicturesForEditedCells(
            new WorkbookCellEditResult(true, null, changedCells, report));
        _selectionStatsRevision++;
        RefreshViewport();
        return report;
    }

    /// <summary>Forces a recalculation from changed cells regardless of calculation mode.</summary>
    public RecalcReport RecalculateChangedCellsAlways(IReadOnlyList<CellAddress> changedCells)
    {
        ArgumentNullException.ThrowIfNull(changedCells);

        var report = _cellEditService.RecalculateAlways(Workbook, changedCells);
        RefreshLinkedPicturesForEditedCells(
            new WorkbookCellEditResult(true, null, changedCells, report));
        _selectionStatsRevision++;
        RefreshViewport();
        return report;
    }

    private HashSet<SheetId> CaptureSheetIds() =>
        Workbook.Sheets.Select(sheet => sheet.Id).ToHashSet();

    /// <summary>
    /// Snapshots each sheet's <see cref="Sheet.IsHidden"/> flag before an Undo/Redo so the
    /// dispatcher can tell whether the just-applied history entry was a Hide/Unhide Sheet
    /// command (see <see cref="FindSheetWithFlippedHiddenState"/>), mirroring how
    /// <see cref="CaptureSheetIds"/> lets it detect a structural add/remove.
    /// </summary>
    private Dictionary<SheetId, bool> CaptureSheetHiddenStates() =>
        Workbook.Sheets.ToDictionary(sheet => sheet.Id, sheet => sheet.IsHidden);

    private void ApplySuccessfulHistoryResult(
        WorkbookCellEditResult result,
        IReadOnlySet<SheetId> sheetIdsBefore,
        IReadOnlyDictionary<SheetId, bool>? hiddenStatesBefore = null)
    {
        if (FindNewSheetId(sheetIdsBefore) is { } newSheetId)
        {
            ApplySuccessfulNewWorksheetResult(newSheetId);
            return;
        }

        if (result.AffectedCells.Count > 0)
        {
            // Excel restores the affected selection on Undo/Redo (e.g. undoing a Sort or Fill
            // re-selects the whole sorted/filled range), not just a single cell. Compute the
            // bounding range of every affected cell -- reported in row-major order by the
            // command, so AffectedCells[0] is the range's top-left -- and select that, mirroring
            // ApplySuccessfulRangeEditResult's forward-operation behavior. When the affected range
            // lives on a different sheet than the one currently active (e.g. the user switched
            // tabs after the command ran, with no new undo entry for that switch), Excel also
            // switches the view back to that sheet -- mirroring ApplySuccessfulEditResult's own
            // cross-sheet switch -- instead of collapsing the restored selection to a single cell.
            var boundingRange = BoundingRangeOrDefault(result.AffectedCells, ActiveCell);
            if (!ActiveSheet.Id.Equals(boundingRange.Start.Sheet))
            {
                RememberActiveWorksheetSelection();
                var selection = _sheetSelectionService.SelectSheet(Workbook, boundingRange.Start.Sheet, _groupedSheetIds);
                ActiveSheet = selection.Sheet;
                RefreshSheetTabsForActiveSheet();
            }

            ApplySuccessfulRangeEditResult(result, boundingRange);
            return;
        }

        // Undo/Redo of Hide/Unhide Sheet never adds or removes a Workbook.Sheets entry and never
        // reports AffectedCells (SetSheetHiddenCommand implements only IWorkbookCommand), so
        // without this check it would fall straight into the generic "re-select whatever is
        // already active" branch below and leave the view on whatever sheet was active going in.
        // Excel always re-activates the sheet whose visibility just changed: it switches to the
        // sheet that just became visible again (undoing a Hide, or redoing an Unhide), and
        // switches away to a visible survivor when a sheet just became hidden again (redoing a
        // Hide, or undoing an Unhide). Detect that flip here and activate accordingly.
        if (hiddenStatesBefore is not null &&
            FindSheetWithFlippedHiddenState(hiddenStatesBefore) is { } flippedSheetId)
        {
            var flippedSheet = Workbook.GetSheet(flippedSheetId);
            if (flippedSheet is { IsHidden: false })
            {
                // The sheet just became visible (Hide undone, or Unhide redone) — Excel selects it.
                ApplySuccessfulWorkbookStructureResult(flippedSheetId);
                return;
            }

            // The sheet just became hidden again (Hide redone, or Unhide undone) — it can no
            // longer host the view, so fall back to a visible survivor, mirroring HideActiveSheet's
            // own forward-path selection.
            var survivorIndex = FindSheetIndex(flippedSheetId, notFoundIndex: -1);
            var preferredSheetId = survivorIndex >= 0
                ? FindPreferredVisibleSheetIdAfterHidden(survivorIndex, flippedSheetId)
                : null;
            ApplySuccessfulWorkbookStructureResult(preferredSheetId ?? ActiveSheet.Id);
            return;
        }

        if (Workbook.GetSheet(ActiveSheet.Id) is not null)
        {
            ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
            return;
        }

        ApplySuccessfulWorkbookStructureResult(ActiveSheet.Id);
    }

    /// <summary>
    /// Finds the single sheet whose <see cref="Sheet.IsHidden"/> flag differs from the pre-Undo/Redo
    /// snapshot — i.e. the sheet a Hide/Unhide Sheet command's Apply/Revert just touched. Returns
    /// <c>null</c> when no sheet's hidden flag changed (the ordinary case for every other command).
    /// </summary>
    private SheetId? FindSheetWithFlippedHiddenState(IReadOnlyDictionary<SheetId, bool> hiddenStatesBefore)
    {
        foreach (var sheet in Workbook.Sheets)
        {
            if (hiddenStatesBefore.TryGetValue(sheet.Id, out var wasHidden) && wasHidden != sheet.IsHidden)
                return sheet.Id;
        }

        return null;
    }

    private SheetId? FindNewSheetId(IReadOnlySet<SheetId> sheetIdsBefore)
    {
        foreach (var sheet in Workbook.Sheets)
        {
            if (!sheetIdsBefore.Contains(sheet.Id))
                return sheet.Id;
        }

        return null;
    }

    private SheetId? FindPreferredSheetIdAfterRemoval(int removedIndex, SheetId removedSheetId)
    {
        for (var index = removedIndex + 1; index < Workbook.Sheets.Count; index++)
        {
            var sheet = Workbook.Sheets[index];
            if (sheet.Id != removedSheetId)
                return sheet.Id;
        }

        for (var index = removedIndex - 1; index >= 0; index--)
        {
            var sheet = Workbook.Sheets[index];
            if (sheet.Id != removedSheetId)
                return sheet.Id;
        }

        return null;
    }

    private SheetId? FindPreferredVisibleSheetIdAfterHidden(int hiddenIndex, SheetId hiddenSheetId)
    {
        for (var index = hiddenIndex + 1; index < Workbook.Sheets.Count; index++)
        {
            var sheet = Workbook.Sheets[index];
            if (sheet.Id != hiddenSheetId && !sheet.IsHidden && !sheet.IsVeryHidden)
                return sheet.Id;
        }

        for (var index = hiddenIndex - 1; index >= 0; index--)
        {
            var sheet = Workbook.Sheets[index];
            if (sheet.Id != hiddenSheetId && !sheet.IsHidden && !sheet.IsVeryHidden)
                return sheet.Id;
        }

        return null;
    }

    private IReadOnlyList<SheetId> GetSelectableSheetIds()
    {
        var visible = Workbook.Sheets
            .Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden)
            .Select(sheet => sheet.Id)
            .ToList();

        return visible.Count > 0
            ? visible
            : Workbook.Sheets.Select(sheet => sheet.Id).ToList();
    }

    private void SelectSingleSheetGroup(SheetId sheetId) =>
        UpdateGroupedSheetsForTabSelection(sheetId, selectRange: false, toggle: false);

    private void UpdateGroupedSheetsForTabSelection(SheetId sheetId, bool selectRange, bool toggle)
    {
        var selectableSheetIds = GetSelectableSheetIds();
        IReadOnlyList<SheetId> selectedSheetIds;

        if (selectRange && _sheetGroupAnchor.HasValue)
        {
            selectedSheetIds = SheetGroupSelectionService.SelectRange(
                selectableSheetIds,
                _sheetGroupAnchor.Value,
                sheetId);
        }
        else if (toggle)
        {
            selectedSheetIds = SheetGroupSelectionService.Toggle(sheetId, _groupedSheetIds);
            _sheetGroupAnchor = sheetId;
        }
        else
        {
            selectedSheetIds = SheetGroupSelectionService.SelectSingle(sheetId);
            _sheetGroupAnchor = sheetId;
        }

        SetGroupedSheetIds(selectedSheetIds, sheetId);
    }

    private bool SetGroupedSheetIds(IEnumerable<SheetId> sheetIds, SheetId fallbackSheetId)
    {
        var previous = _groupedSheetIds.ToHashSet();
        var selectableSheetIds = GetSelectableSheetIds().ToHashSet();
        _groupedSheetIds.Clear();

        foreach (var sheetId in sheetIds)
        {
            if (selectableSheetIds.Contains(sheetId))
                _groupedSheetIds.Add(sheetId);
        }

        if (_groupedSheetIds.Count == 0 || !_groupedSheetIds.Contains(fallbackSheetId))
        {
            _groupedSheetIds.Clear();
            if (selectableSheetIds.Contains(fallbackSheetId))
                _groupedSheetIds.Add(fallbackSheetId);
            else if (selectableSheetIds.Count > 0)
                _groupedSheetIds.Add(selectableSheetIds.First());
        }

        return !previous.SetEquals(_groupedSheetIds);
    }

    private void RefreshSheetTabsForActiveSheet()
    {
        SetGroupedSheetIds(_groupedSheetIds.ToArray(), ActiveSheet.Id);
        var selection = _sheetSelectionService.SelectSheet(
            Workbook,
            ActiveSheet.Id,
            IsWorkbookGrouped ? _groupedSheetIds : null);
        ActiveSheet = selection.Sheet;
        SheetTabs = selection.Tabs;
    }

    private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds()
    {
        if (!IsWorkbookGrouped)
            return [ActiveSheet.Id];

        var groupedVisibleSheetIds = GetSelectableSheetIds()
            .Where(_groupedSheetIds.Contains)
            .ToList();
        if (groupedVisibleSheetIds.Count <= 1 || !groupedVisibleSheetIds.Contains(ActiveSheet.Id))
            return [ActiveSheet.Id];

        return [ActiveSheet.Id, .. groupedVisibleSheetIds.Where(sheetId => sheetId != ActiveSheet.Id)];
    }

    private IWorkbookCommand CreateEditCellsCommand(IReadOnlyList<(CellAddress Address, Cell NewCell)> edits)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, ActiveSheet.Id, edits)
            : new EditCellsCommand(ActiveSheet.Id, edits);
    }

    private IWorkbookCommand CreateApplyStyleCommand(GridRange range, StyleDiff diff)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedApplyStyleCommand(targetSheetIds, range, diff)
            : new ApplyStyleCommand(ActiveSheet.Id, range, diff);
    }

    private IWorkbookCommand CreateBorderPresetCommand(
        GridRange range,
        CellBorderPreset preset,
        BorderStyle borderStyle = BorderStyle.Thin,
        CellColor? borderColor = null)
    {
        if (!CellBorderPresetPlanner.RequiresPerCellPlanning(preset))
            return CreateApplyStyleCommand(range, CellBorderPresetPlanner.Plan(preset, range, range.Start, borderStyle, borderColor));

        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>();
        foreach (var address in range.AllCells())
        {
            var diff = CellBorderPresetPlanner.Plan(preset, range, address, borderStyle, borderColor);
            if (!BorderShortcutService.HasBorderChanges(diff))
                continue;

            var sourceRange = new GridRange(address, address);
            commands.Add(targetSheetIds.Count > 1
                ? new GroupedApplyStyleCommand(targetSheetIds, sourceRange, diff)
                : new ApplyStyleCommand(
                    ActiveSheet.Id,
                    RemapRangeToSheet(sourceRange, ActiveSheet.Id),
                    diff));
        }

        return ToCommand(CellBorderPresetPlanner.GetDisplayName(preset), commands);
    }

    private IWorkbookCommand CreateMergeAndCenterCommand(
        GridRange range,
        MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count * 2);
        foreach (var sheetId in targetSheetIds)
        {
            var sheet = Workbook.GetSheet(sheetId);
            var sheetRange = RemapRangeToSheet(range, sheetId);
            commands.AddRange(CellMergePlanner.CreateMergeAndCenterCommands(
                sheet,
                sheetId,
                sheetRange,
                contentResolution));
        }

        return ToCommand("Merge & Center", commands);
    }

    private IReadOnlyList<IWorkbookCommand> CreateFormatCellsMergeCommands(
        GridRange range,
        bool mergeCells,
        MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>();
        foreach (var sheetId in targetSheetIds)
        {
            var sheet = Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            var sheetRange = RemapRangeToSheet(range, sheetId);
            commands.AddRange(mergeCells && contentResolution == MergeCellContentResolution.ConcatenateAllCells
                ? CellMergePlanner.CreateMergeAndCenterCommands(
                    sheet,
                    sheetId,
                    sheetRange,
                    contentResolution).Where(command => command is not ApplyStyleCommand)
                : CellMergePlanner.CreateMergeCommands(
                    sheet,
                    sheetId,
                    sheetRange,
                    mergeCells));
        }

        return commands;
    }

    private IWorkbookCommand CreateFormatPainterCommand(
        Sheet sourceSheet,
        GridRange sourceRange,
        IReadOnlyList<GridRange> targetRanges)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        return SelectionStyleCommandPlanner.CreateRangeCommand(
            targetSheetIds,
            targetRanges,
            (sheetId, sheetTargetRange) => FormatPainterCommandFactory.Create(
                Workbook,
                sourceSheet,
                sourceRange,
                sheetTargetRange),
            "Format Painter");
    }

    private IWorkbookCommand CreateClearAllCommand(GridRange range)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetRange = RemapRangeToSheet(range, sheetId);
            commands.Add(new CompositeWorkbookCommand(
                "Clear All",
                [
                    new ClearContentsCommand(sheetId, sheetRange),
                    new ApplyStyleCommand(sheetId, sheetRange, CellStyleDiffPlanner.ClearFormatsDiff()),
                    new ClearConditionalFormatsCommand(sheetId, sheetRange),
                    new ClearDataValidationCommand(sheetId, sheetRange),
                    new ClearCommentsCommand(sheetId, sheetRange),
                    new ClearHyperlinksCommand(sheetId, sheetRange)
                ]));
        }

        return ToCommand("Clear All", commands);
    }

    /// <summary>
    /// Multi-area counterpart of <see cref="CreateClearAllCommand(GridRange)"/>: builds one composite
    /// per disjoint Ctrl+click area (each itself grouped-sheet-aware via the single-range overload)
    /// so Home&gt;Clear&gt;Clear All clears every area of a multi-area selection, matching Excel and the
    /// WPF host's TryExecuteRepeatableCurrentSelectionRangesCommand (R128-cellscmds-multiarea-clear-2).
    /// </summary>
    private IWorkbookCommand CreateClearAllCommand(IReadOnlyList<GridRange> ranges) =>
        ToCommand("Clear All", ranges.Select(CreateClearAllCommand).ToList());

    /// <summary>
    /// Home&gt;Clear&gt;Clear Formats' command factory. Matching Excel (and this session's own
    /// <see cref="CreateClearAllCommand"/>), clearing formats also removes any conditional-formatting
    /// rules on the selection -- CF is itself a form of formatting, so a plain style-only
    /// <c>ApplyStyleCommand(ClearFormatsDiff)</c> left stale CF rules behind
    /// (R66-commands-clear-delete-6-1). Mirrors <see cref="CreateClearAllCommand"/>'s composite minus
    /// the contents/validation/comments/hyperlinks clears that Clear All (but not Clear Formats) also
    /// performs.
    /// </summary>
    private IWorkbookCommand CreateClearFormatsCommand(GridRange range)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetRange = RemapRangeToSheet(range, sheetId);
            commands.Add(new CompositeWorkbookCommand(
                "Clear Formats",
                [
                    new ApplyStyleCommand(sheetId, sheetRange, CellStyleDiffPlanner.ClearFormatsDiff()),
                    new ClearConditionalFormatsCommand(sheetId, sheetRange)
                ]));
        }

        return ToCommand("Clear Formats", commands);
    }

    /// <summary>
    /// Multi-area counterpart of <see cref="CreateClearFormatsCommand(GridRange)"/> -- see
    /// <see cref="CreateClearAllCommand(IReadOnlyList{GridRange})"/> (R128-cellscmds-multiarea-clear-2).
    /// </summary>
    private IWorkbookCommand CreateClearFormatsCommand(IReadOnlyList<GridRange> ranges) =>
        ToCommand("Clear Formats", ranges.Select(CreateClearFormatsCommand).ToList());

    private IWorkbookCommand CreateSetHyperlinkCommand(
        GridRange range,
        string target,
        string displayText,
        HyperlinkMetadata metadata)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            var address = RemapRangeToSheet(range, sheetId).Start;
            commands.Add(new SetHyperlinkCommand(
                sheetId,
                address,
                target,
                displayText,
                metadata));
        }

        return ToCommand("Insert Hyperlink", commands);
    }

    private IReadOnlyList<IWorkbookCommand> CreateUnmergeCommands(GridRange range)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>();
        foreach (var sheetId in targetSheetIds)
        {
            var sheet = Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            commands.AddRange(CellMergePlanner.CreateUnmergeCommands(sheet, sheetId, RemapRangeToSheet(range, sheetId)));
        }

        return commands;
    }

    private IWorkbookCommand CreateSetFontSizeCommand(GridRange range, double fontSize, double rowHeight)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count * 2);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetRange = RemapRangeToSheet(range, sheetId);
            commands.Add(new ApplyStyleCommand(sheetId, sheetRange, new StyleDiff(FontSize: fontSize)));
            commands.Add(new SetRowHeightCommand(sheetId, sheetRange.Start.Row, sheetRange.End.Row, rowHeight));
        }

        return ToCommand("Set Font Size", commands);
    }

    private IWorkbookCommand CreateExternalTextPasteCommand(
        GridRange destinationRange,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool preserveText,
        PasteSpecialOptions options = default)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = targetSheetIds
            .Select(sheetId => PasteCommandFactory.CreateExternalTextPasteCommand(
                sheetId,
                RemapRangeToSheet(destinationRange, sheetId),
                rows,
                preserveText,
                options))
            .ToList();
        return ToCommand("Paste", commands);
    }

    private IWorkbookCommand CreateInternalPasteCommand(
        WorkbookClipboardSnapshot clipboard,
        CellAddress destination,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths) =>
        CreateInternalPasteCommand(
            clipboard,
            new GridRange(destination, destination),
            mode,
            options,
            keepSourceColumnWidths);

    private IWorkbookCommand CreateInternalPasteCommand(
        WorkbookClipboardSnapshot clipboard,
        GridRange destinationRange,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetDestination = RemapRangeToSheet(destinationRange, sheetId);
            var command = PasteCommandFactory.CreateInternalPasteCommand(
                Workbook,
                sheetId,
                clipboard.SourceRange,
                clipboard.Cells,
                sheetDestination,
                mode,
                options);
            if (keepSourceColumnWidths)
            {
                command = new CompositeWorkbookCommand(
                    "Paste Special",
                    [
                        command,
                        new PasteColumnWidthsCommand(sheetId, clipboard.SourceRange, sheetDestination.Start.Col)
                    ]);
            }

            commands.Add(command);
        }

        var label = mode == PasteCellsMode.All && options == default && !keepSourceColumnWidths
            ? "Paste"
            : "Paste Special";
        return ToCommand(label, commands);
    }

    private IWorkbookCommand CreateInternalPasteCommand(
        WorkbookClipboardSnapshot clipboard,
        IReadOnlyList<CellAddress> destinations,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths)
    {
        var commands = destinations
            .Select(destination => CreateInternalPasteCommand(
                clipboard,
                destination,
                mode,
                options,
                keepSourceColumnWidths))
            .ToList();
        var label = mode == PasteCellsMode.All && options == default && !keepSourceColumnWidths
            ? "Paste"
            : "Paste Special";
        return ToCommand(label, commands);
    }

    private IWorkbookCommand CreatePasteLinkCommand(
        WorkbookClipboardSnapshot clipboard,
        string sourceSheetName,
        CellAddress destination,
        GridRange destinationRange,
        bool transpose,
        bool keepSourceColumnWidths)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetDestination = RemapAddressToSheet(destination, sheetId);
            var sheetDestinationRange = RemapRangeToSheet(destinationRange, sheetId);
            var linkedCells = PasteLinkService.CreateLinkedCells(
                clipboard.SourceRange,
                sheetDestination,
                sheetDestinationRange,
                sourceSheetName,
                transpose);
            IWorkbookCommand command = new EditCellsCommand(sheetId, linkedCells);
            if (keepSourceColumnWidths)
            {
                command = new CompositeWorkbookCommand(
                    "Paste Link",
                    [
                        command,
                        new PasteColumnWidthsCommand(
                            sheetId,
                            clipboard.SourceRange,
                            sheetDestination.Col,
                            sheetDestinationRange.ColCount)
                    ]);
            }

            commands.Add(command);
        }

        return ToCommand("Paste Link", commands);
    }

    private IWorkbookCommand CreateRangeCommand(
        GridRange range,
        string title,
        Func<SheetId, GridRange, IWorkbookCommand> createCommand)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = targetSheetIds
            .Select(sheetId => createCommand(sheetId, RemapRangeToSheet(range, sheetId)))
            .ToList();
        return ToCommand(title, commands);
    }

    private IWorkbookCommand? CreateSetSelectedRangeDataValidationCommand(DataValidation rule)
    {
        var selectedRanges = GetCurrentSelectedRanges();
        var commands = new List<IWorkbookCommand>();
        foreach (var sheetId in CurrentGroupedEditSheetIds())
        {
            var sheet = Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            var sheetRanges = selectedRanges
                .Select(range => RemapRangeToSheet(range, sheetId))
                .ToArray();
            var sheetRule = rule.CloneWithNewIdentity(sheetRanges[0], sheetRanges.Skip(1));
            if (WouldSetDataValidationMutate(sheet, sheetRule))
                commands.Add(new SetDataValidationCommand(sheetId, sheetRule));
        }

        return commands.Count == 0
            ? null
            : ToCommand("Data Validation", commands);
    }

    private IWorkbookCommand? CreateClearSelectedRangeDataValidationCommand()
    {
        var selectedRanges = GetCurrentSelectedRanges();
        var commands = new List<IWorkbookCommand>();
        foreach (var sheetId in CurrentGroupedEditSheetIds())
        {
            var sheet = Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            foreach (var range in selectedRanges)
            {
                var sheetRange = RemapRangeToSheet(range, sheetId);
                if (HasDataValidationOverlapping(sheet, sheetRange))
                    commands.Add(new ClearDataValidationCommand(sheetId, sheetRange));
            }
        }

        return commands.Count == 0
            ? null
            : ToCommand("Clear Data Validation", commands);
    }

    private IWorkbookCommand CreateGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = targetSheetIds
            .Select(createCommand)
            .ToList();
        return ToCommand(title, commands);
    }

    private WorkbookCellEditResult ApplySelectedRangeStyle(StyleDiff diff)
    {
        // Routed through ExecuteRepeatableEditCommand (rather than plain ExecuteEditCommand) so
        // that F4 / Repeat Last Action (RepeatLastAction) can replay this style change against a
        // newly-selected range later, matching the WPF host's TryExecuteRepeatableApplyStyle. The
        // factory re-reads SelectedRange/SelectedRanges each time it runs (via GetSelectionSizingRanges)
        // rather than closing over the ranges captured here, since a repeat invocation targets
        // whatever is selected at that time.
        //
        // Built via the shared SelectionStyleCommandPlanner.CreateApplyStyleCommand choke point
        // (rather than the single-range private CreateApplyStyleCommand) so that every disjoint area
        // of a Ctrl+click multi-area selection gets the style, matching the WPF host's
        // TryExecuteRepeatableApplyStyle and the R126 row/column-sizing fix
        // (R127-cellscmds-multiarea-style-1).
        var preservedRange = SelectedRange;
        var preservedRanges = SelectedRanges;
        var preservedActiveCell = ActiveCell;
        var result = _cellEditService.ExecuteRepeatableEditCommand(
            Workbook,
            () => SelectionStyleCommandPlanner.CreateApplyStyleCommand(
                CurrentGroupedEditSheetIds(),
                GetSelectionSizingRanges(),
                diff,
                "Apply Style"));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, SelectedRange);
        if (preservedRanges.Count > 1)
            SelectRanges(preservedRange, preservedRanges, preservedActiveCell);
        return result;
    }

    /// <summary>
    /// Replays the last repeatable command (F4 / Repeat Last Action), matching Excel and the WPF
    /// host's ExecuteRepeatLast. Applies to whatever range/cell is currently selected.
    /// </summary>
    public WorkbookCellEditResult RepeatLastAction()
    {
        var range = SelectedRange;
        var result = _cellEditService.RepeatLastEdit(Workbook);
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    private static IWorkbookCommand ToCommand(string title, IReadOnlyList<IWorkbookCommand> commands) =>
        commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand(title, commands);

    private static WorkbookCellEditResult? ValidateScenarioManagerPlan(
        ScenarioManagerPlan plan,
        ScenarioManagerOperation expectedOperation)
    {
        if (plan.Operation != expectedOperation)
            return FailedScenarioManagerResult("Scenario Manager plan operation does not match the requested action.");

        return plan.IsReady
            ? null
            : FailedScenarioManagerResult(plan.StatusText);
    }

    private static WorkbookCellEditResult FailedScenarioManagerResult(string errorMessage) =>
        new(false, errorMessage, [], RecalcReport: null);

    private static bool HasStyleDiffChanges(StyleDiff diff) =>
        diff != EmptyStyleDiff;

    private IReadOnlyList<GridRange> GetCurrentSelectedRanges() =>
        SelectedRanges.Count == 0
            ? [SelectedRange]
            : SelectedRanges;

    private static bool WouldSetDataValidationMutate(Sheet sheet, DataValidation rule)
    {
        var existing = FindMatchingDataValidationRule(sheet, rule);
        return existing is null || !existing.HasSameDefinition(rule, includeNativeMetadata: true);
    }

    private static DataValidation? FindMatchingDataValidationRule(Sheet sheet, DataValidation rule)
    {
        foreach (var candidate in sheet.DataValidations)
        {
            if (candidate.Id == rule.Id || candidate.AppliesTo == rule.AppliesTo)
                return candidate;
        }

        return null;
    }

    private static bool HasDataValidationOverlapping(Sheet sheet, GridRange range)
    {
        foreach (var rule in sheet.DataValidations)
        {
            if (DataValidationOverlaps(rule, range))
                return true;
        }

        return false;
    }

    private static bool DataValidationOverlaps(DataValidation rule, GridRange range)
    {
        if (rule.AppliesTo.Overlaps(range))
            return true;

        foreach (var ruleRange in rule.AdditionalRanges)
        {
            if (ruleRange.Overlaps(range))
                return true;
        }

        return false;
    }

    private static double GetFittingRowHeight(double fontSize) =>
        Math.Min(AutoFitSizingService.MaximumRowHeight, FontSizePlanner.EstimateFittingRowHeight(fontSize));

    private static bool HasBorderPresetChanges(
        GridRange range,
        CellBorderPreset preset,
        BorderStyle borderStyle = BorderStyle.Thin,
        CellColor? borderColor = null)
    {
        if (!CellBorderPresetPlanner.RequiresPerCellPlanning(preset))
            return true;

        return range
            .AllCells()
            .Any(address => BorderShortcutService.HasBorderChanges(CellBorderPresetPlanner.Plan(preset, range, address, borderStyle, borderColor)));
    }

    private static GridRange RemapRangeToSheet(GridRange range, SheetId sheetId) =>
        new(
            RemapAddressToSheet(range.Start, sheetId),
            RemapAddressToSheet(range.End, sheetId));

    private static CellAddress RemapAddressToSheet(CellAddress address, SheetId sheetId) =>
        new(sheetId, address.Row, address.Col);

    private static GridRange GetAdvancedFilterSelectedRange(AdvancedFilterPlan plan) =>
        plan is
        {
            OutputMode: AdvancedFilterOutputMode.CopyToAnotherLocation,
            CopyToRange: { } copyToRange
        }
            ? copyToRange
            : plan.ListRange;

    private static GridRange GetForecastSheetSelectedRange(SheetId sheetId, ForecastSheetPlan plan)
    {
        var historicalDataRows = plan.InputExpectation?.HistoricalDataRowCount;
        if (historicalDataRows is null && plan.SourceRange is { } sourceRange && sourceRange.RowCount > 0)
            historicalDataRows = sourceRange.RowCount - 1;

        var lastRow = 1u + (historicalDataRows ?? 0u) + plan.ForecastPeriods;
        return new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, lastRow, 5));
    }

    private void ApplySuccessfulForecastSheetResult(
        WorkbookCellEditResult result,
        IReadOnlySet<SheetId> sheetIdsBefore,
        ForecastSheetPlan plan)
    {
        if (FindNewSheetId(sheetIdsBefore) is not { } forecastSheetId)
        {
            ApplySuccessfulHistoryResult(result, sheetIdsBefore);
            return;
        }

        ApplySuccessfulWorkbookStructureRangeResult(
            forecastSheetId,
            GetForecastSheetSelectedRange(forecastSheetId, plan));
    }

    private void ApplySuccessfulNewWorksheetResult(SheetId preferredSheetId)
    {
        if (Workbook.GetSheet(preferredSheetId) is { } sheet)
        {
            sheet.ResetViewStateToA1();
            _worksheetSelections.Remove(preferredSheetId);
        }

        ApplySuccessfulWorkbookStructureResult(preferredSheetId, resetSelectionToA1: true);
    }

    private void ApplySuccessfulWorkbookStructureResult(SheetId preferredSheetId) =>
        ApplySuccessfulWorkbookStructureResult(preferredSheetId, resetSelectionToA1: false);

    private void ApplySuccessfulWorkbookStructureResult(SheetId preferredSheetId, bool resetSelectionToA1)
    {
        RememberActiveWorksheetSelection();
        var selection = _sheetSelectionService.SelectSheet(Workbook, preferredSheetId);
        ActiveSheet = selection.Sheet;
        SelectSingleSheetGroup(ActiveSheet.Id);
        RefreshSheetTabsForActiveSheet();
        if (resetSelectionToA1)
        {
            ActiveSheet.ResetViewStateToA1();
            _worksheetSelections.Remove(ActiveSheet.Id);
            ActiveCell = new CellAddress(ActiveSheet.Id, 1, 1);
            SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
        }
        else if (!TryRestoreActiveWorksheetSelection())
        {
            ActiveCell = GetInitialActiveCell(ActiveSheet);
            ActiveSheet.ActiveRow = ActiveCell.Row;
            ActiveSheet.ActiveCol = ActiveCell.Col;
            SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
        }

        FormulaEditAddress = null;
        MarkDirty();
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulWorkbookStructureRangeResult(SheetId preferredSheetId, GridRange selectedRange)
    {
        RememberActiveWorksheetSelection();
        var selection = _sheetSelectionService.SelectSheet(Workbook, preferredSheetId);
        ActiveSheet = selection.Sheet;
        SelectSingleSheetGroup(ActiveSheet.Id);
        RefreshSheetTabsForActiveSheet();
        ActiveCell = selectedRange.Start;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        SetSingleSelectedRange(selectedRange);
        FormulaEditAddress = null;
        MarkDirty();
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulWorkbookMetadataResult(SheetId preferredSheetId)
    {
        var selection = _sheetSelectionService.SelectSheet(Workbook, preferredSheetId, _groupedSheetIds);
        ActiveSheet = selection.Sheet;
        RefreshSheetTabsForActiveSheet();
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        FormulaEditAddress = null;
        MarkDirty();
        _selectionStatsRevision++;
        // This method is the single choke point for every sheet-metadata command's forward apply
        // (SetWorksheetViewMode, SetZoomPercent, SetShowGridlines, SetShowHeadings, SetShowFormulas,
        // SetFreezePanes, ...) AND for Undo/Redo of any of them (via ApplySuccessfulHistoryResult),
        // so it is also the right place to drop this view's cached values for the affected sheet --
        // forcing the next read to re-seed from the (possibly just-reverted) shared Sheet fields
        // instead of returning a stale value. Each forward-apply setter immediately reseeds its own
        // entry/entries afterward (see their remarks). MUST run before RefreshViewport()/
        // EnsureActiveCellVisible() below (R87-order-guard-window-state-sweep-3): both rebuild
        // this view's ViewportModel (the latter's EnsureCellVisible can trigger a second, nested
        // RefreshViewport of its own), and now that ViewportService actually consumes
        // GetEffectiveFrozenRows/Cols/SplitRow/Col (via ViewportRequest's FrozenRowsOverride/
        // FrozenColsOverride/SplitOverride), rebuilding BEFORE the stale entry is dropped would bake
        // the pre-Undo/Redo cached value into the rendered viewport, leaving it visibly stuck one
        // step behind even though the shared Sheet field itself already reverted correctly.
        InvalidateAllPerViewOverridesForSheet(ActiveSheet.Id);
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    /// <summary>
    /// Single choke point that drops every per-view cached override (zoom, view mode, gridlines,
    /// headings, formulas, frozen rows/cols, and split row/col) for the given sheet, forcing the
    /// next read of each to re-seed from the (possibly just-written) shared Sheet fields instead of
    /// returning a stale cached value. Used by both <see cref="ApplySuccessfulWorkbookMetadataResult"/>
    /// (the dedicated setters' and Undo/Redo's forward-apply choke point) and
    /// <see cref="ApplySuccessfulEditResult"/> (the generic ExecuteReviewCommand path used by commands
    /// like ApplyCustomViewCommand and SetSplitPanesCommand that write sheet-view fields directly
    /// without going through a dedicated setter) so that no current or future command path reaching
    /// either choke point can leave a per-view cache stale (R88-window-seed-order-guard-sweep-1).
    /// </summary>
    private void InvalidateAllPerViewOverridesForSheet(SheetId sheetId)
    {
        _viewZoomOverrides.Remove(sheetId);
        _viewModeOverrides.Remove(sheetId);
        _viewShowGridlinesOverrides.Remove(sheetId);
        _viewShowHeadingsOverrides.Remove(sheetId);
        _viewShowFormulasOverrides.Remove(sheetId);
        _viewFrozenRowsOverrides.Remove(sheetId);
        _viewFrozenColsOverrides.Remove(sheetId);
        _viewSplitRowOverrides.Remove(sheetId);
        _viewSplitColOverrides.Remove(sheetId);
    }

    private void MarkDirty()
    {
        _documentState.MarkDirty();
        NotifyWorkbookChanged();
    }

    private void NotifyWorkbookChanged() => WorkbookChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Captures the undo stack's current depth/version as the "clean" save point.</summary>
    private void RecordUndoSavePoint()
    {
        _documentState.MarkSavedAtUndoDepth(
            _cellEditService.GetUndoStackDepth(Workbook.Id),
            _cellEditService.GetUndoStackVersion(Workbook.Id));
    }

    /// <summary>
    /// If the undo stack has returned to the recorded save point (matching both depth and, when
    /// recorded, version), asks the shared document state to clear <see cref="IsDirty"/> and returns
    /// <c>true</c>. Called after Undo/Redo, which routes through <see cref="MarkDirty"/> via
    /// <see cref="ApplySuccessfulHistoryResult"/>. Leaves <see cref="IsDirty"/> untouched when no
    /// save point was recorded or the stack has not returned to it.
    /// </summary>
    public bool TryMarkCleanIfAtSavePoint()
    {
        var currentUndoDepth = _cellEditService.GetUndoStackDepth(Workbook.Id);
        var wasDirty = IsDirty;
        var isAtSavePoint = _documentState.TryMarkCleanIfAtSavePoint(
            currentUndoDepth,
            _cellEditService.GetUndoStackVersion(Workbook.Id));
        if (isAtSavePoint && wasDirty)
            NotifyWorkbookChanged();
        return isAtSavePoint;
    }

    /// <summary>
    /// Forces this session's dirty/modified state after a crash-recovery snapshot has been
    /// loaded into it, so the host shows the modified indicator and prompts the user to save
    /// rather than silently discarding the recovered data. Mirrors the WPF host's
    /// <c>MarkWorkbookDirtyForRecovery</c>/<c>MarkWorkbookDirty</c> path, which reuses this same
    /// document-state dirty-marking call for edits.
    /// </summary>
    public void MarkDirtyForRecovery() => MarkDirty();

    /// <summary>
    /// Records a mutation performed by a renderer that has not yet moved its command execution
    /// into <see cref="WorkbookSession"/>. This is the migration boundary for the WPF host;
    /// portable and Avalonia commands already call <see cref="MarkDirty"/> internally.
    /// </summary>
    public void MarkDirtyFromHost() => MarkDirty();

    /// <summary>
    /// Captures the current command-history position as the clean save point. Renderers should
    /// call this only after a completed save or after adopting a freshly opened/new workbook.
    /// </summary>
    public void MarkSavedFromHost()
    {
        RecordUndoSavePoint();
        NotifyWorkbookChanged();
    }

    /// <summary>
    /// Associates a path with the current document without changing its dirty state. Recovery
    /// and transitional renderer workflows use this when the file identity changes separately
    /// from save completion.
    /// </summary>
    public void SetCurrentFilePathFromHost(string? path)
    {
        CurrentFilePath = path;
        NotifyWorkbookChanged();
    }

    private static CellAddress FirstAffectedCellOrDefault(
        IReadOnlyList<CellAddress> affectedCells,
        CellAddress fallbackAddress) =>
        affectedCells.Count == 0 ? fallbackAddress : affectedCells[0];

    /// <summary>
    /// Computes the bounding <see cref="GridRange"/> covering every affected cell (all on the same
    /// sheet, per <see cref="CommandOutcome.AffectedCells"/> contract), so Undo/Redo can restore the
    /// full affected selection instead of collapsing to a single cell. Falls back to a degenerate
    /// range at <paramref name="fallbackAddress"/> when there are no affected cells.
    /// </summary>
    private static GridRange BoundingRangeOrDefault(
        IReadOnlyList<CellAddress> affectedCells,
        CellAddress fallbackAddress)
    {
        if (affectedCells.Count == 0)
            return new GridRange(fallbackAddress, fallbackAddress);

        var sheet = affectedCells[0].Sheet;
        var minRow = affectedCells[0].Row;
        var maxRow = affectedCells[0].Row;
        var minCol = affectedCells[0].Col;
        var maxCol = affectedCells[0].Col;

        for (var i = 1; i < affectedCells.Count; i++)
        {
            var cell = affectedCells[i];
            if (!cell.Sheet.Equals(sheet))
                continue;

            if (cell.Row < minRow) minRow = cell.Row;
            if (cell.Row > maxRow) maxRow = cell.Row;
            if (cell.Col < minCol) minCol = cell.Col;
            if (cell.Col > maxCol) maxCol = cell.Col;
        }

        return new GridRange(new CellAddress(sheet, minRow, minCol), new CellAddress(sheet, maxRow, maxCol));
    }

    private void ApplySuccessfulEditResult(WorkbookCellEditResult result, CellAddress fallbackAddress)
    {
        var address = FirstAffectedCellOrDefault(result.AffectedCells, fallbackAddress);
        if (!ActiveSheet.Id.Equals(address.Sheet))
        {
            RememberActiveWorksheetSelection();
            var selection = _sheetSelectionService.SelectSheet(Workbook, address.Sheet, _groupedSheetIds);
            ActiveSheet = selection.Sheet;
            RefreshSheetTabsForActiveSheet();
        }

        ActiveCell = address;
        ActiveSheet.ActiveRow = address.Row;
        ActiveSheet.ActiveCol = address.Col;
        SetSingleSelectedRange(new GridRange(address, address));
        FormulaEditAddress = null;
        RefreshLinkedPicturesForEditedCells(result);
        MarkDirty();
        _selectionStatsRevision++;
        // R87-order-guard-window-state-sweep-2 / R88-window-seed-order-guard-sweep-1: this is the
        // forward-apply choke point for every command reached only via the generic
        // ExecuteReviewCommand path that writes sheet-view fields directly instead of going through a
        // dedicated setter -- e.g. SetSplitPanesCommand (Split has no dedicated setter like
        // SetFreezePanes) and ApplyCustomViewCommand (writes zoom/gridlines/headings/formulas/view
        // mode/frozen/split all at once via CustomViewStatePlanner.ApplyState). Drop ALL of this
        // view's per-view override snapshots for the affected sheet here so the next read of any of
        // them falls back to the live value the command just wrote to the shared Sheet fields,
        // instead of returning this view's own stale pre-apply snapshot.
        InvalidateAllPerViewOverridesForSheet(ActiveSheet.Id);
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    /// <summary>
    /// Excel's Paste Special &gt; Linked Picture (our Copy-then-Paste-Linked-Picture) keeps drawing a
    /// live snapshot of its source range: any edit to a cell inside that range must refresh the
    /// picture's rendered content immediately, not just on structural row/column shifts. Those
    /// shifts already refresh the snapshot via RowColumnShiftHelpers.RefreshLinkedPictureSnapshot
    /// (ShiftPictures, gated on the range's coordinates having moved), but a plain value/format edit
    /// that leaves LinkedSourceRange's coordinates unchanged never goes through that path — nothing
    /// else in the app touches a linked picture's cached Cells after paste. Called from every
    /// successful edit-result apply so it covers direct edits, fill, paste, etc. uniformly.
    /// </summary>
    private void RefreshLinkedPicturesForEditedCells(WorkbookCellEditResult result)
    {
        HashSet<CellAddress>? editedCells = null;
        foreach (var cell in result.AffectedCells)
            (editedCells ??= []).Add(cell);
        if (result.RecalcReport is { } recalcReport)
        {
            foreach (var cell in recalcReport.RecalculatedCells)
                (editedCells ??= []).Add(cell);
        }
        if (editedCells is null || editedCells.Count == 0)
            return;

        foreach (var sheet in Workbook.Sheets)
        {
            if (sheet.Pictures.Count == 0)
                continue;

            foreach (var picture in sheet.Pictures)
            {
                if (!picture.IsLinkedToSourceRange || picture.LinkedSourceRange is not { } sourceRange)
                    continue;

                var sourceSheet = Workbook.GetSheet(sourceRange.Start.Sheet);
                if (sourceSheet is null)
                    continue;

                var touched = false;
                foreach (var edited in editedCells)
                {
                    if (edited.Sheet.Equals(sourceRange.Start.Sheet) &&
                        edited.Row >= sourceRange.Start.Row && edited.Row <= sourceRange.End.Row &&
                        edited.Col >= sourceRange.Start.Col && edited.Col <= sourceRange.End.Col)
                    {
                        touched = true;
                        break;
                    }
                }
                if (!touched)
                    continue;

                RefreshLinkedPictureCells(picture, sourceSheet, sourceRange);
            }
        }
    }

    /// <summary>
    /// Rebuilds a linked picture's cached cell snapshot from the live contents of its source range.
    /// Mirrors RowColumnShiftHelpers.RefreshLinkedPictureSnapshot (Core.Commands, private to that
    /// file) so both refresh paths render identical content for the same source range.
    /// </summary>
    private void RefreshLinkedPictureCells(PictureModel picture, Sheet sourceSheet, GridRange sourceRange)
    {
        picture.SourceRowCount = sourceRange.RowCount;
        picture.SourceColumnCount = sourceRange.ColCount;

        picture.Cells.Clear();
        for (var row = sourceRange.Start.Row; row <= sourceRange.End.Row; row++)
        {
            for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
            {
                var cell = sourceSheet.GetCell(row, col);
                var styleId = cell?.StyleId
                    ?? sourceSheet.GetStyleOnly(row, col)
                    ?? StyleId.Default;
                var style = Workbook.GetStyle(styleId);
                var value = cell?.Value ?? BlankValue.Instance;

                picture.Cells.Add(new PictureCellSnapshot(
                    row - sourceRange.Start.Row,
                    col - sourceRange.Start.Col,
                    FormatPictureCellText(value, style.NumberFormat),
                    style.Clone(),
                    value is NumberValue or DateTimeValue));
            }
        }
    }

    private void ApplySuccessfulRangeEditResult(WorkbookCellEditResult result, GridRange selectedRange)
    {
        ActiveCell = selectedRange.Start;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        SetSingleSelectedRange(selectedRange);
        FormulaEditAddress = null;
        RefreshLinkedPicturesForEditedCells(result);
        MarkDirty();
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulSelectionEditResult(
        WorkbookCellEditResult result,
        GridRange primaryRange,
        IReadOnlyList<GridRange> selectedRanges,
        CellAddress activeCell)
    {
        ActiveCell = primaryRange.Contains(activeCell) ? activeCell : primaryRange.Start;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        SetSelectedRanges(primaryRange, selectedRanges);
        FormulaEditAddress = null;
        RefreshLinkedPicturesForEditedCells(result);
        MarkDirty();
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulPreservedSelectionCommandResult(
        WorkbookCellEditResult result,
        IReadOnlySet<SheetId> sheetIdsBefore,
        IReadOnlyDictionary<SheetId, bool> hiddenStatesBefore)
    {
        if (!result.Success || result.IsNoOp)
            return;

        var sheetStructureChanged = !sheetIdsBefore.SetEquals(CaptureSheetIds()) ||
            hiddenStatesBefore.Any(pair =>
                Workbook.GetSheet(pair.Key) is { } sheet && sheet.IsHidden != pair.Value);
        if (sheetStructureChanged || Workbook.GetSheet(ActiveSheet.Id) is null)
        {
            ApplySuccessfulHistoryResult(result, sheetIdsBefore, hiddenStatesBefore);
            return;
        }

        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        FormulaEditAddress = null;
        RefreshLinkedPicturesForEditedCells(result);
        MarkDirty();
        _selectionStatsRevision++;
        InvalidateAllPerViewOverridesForSheet(ActiveSheet.Id);
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private WorkbookNavigationResult NavigateToRange(GridRange range)
    {
        if (!range.Start.Sheet.Equals(range.End.Sheet))
            return WorkbookNavigationResult.Failed("Reference must be on one sheet.");

        var targetSheet = Workbook.GetSheet(range.Start.Sheet);
        if (targetSheet is null)
            return WorkbookNavigationResult.Failed("Reference sheet was not found.");
        if (targetSheet.IsHidden || targetSheet.IsVeryHidden)
            return WorkbookNavigationResult.Failed("Reference sheet is hidden.");

        if (!ActiveSheet.Id.Equals(range.Start.Sheet))
            SelectSheet(range.Start.Sheet);
        if (!ActiveSheet.Id.Equals(range.Start.Sheet))
            return WorkbookNavigationResult.Failed("Reference sheet could not be selected.");

        SelectRange(range);
        return WorkbookNavigationResult.Selected(range);
    }

    private WorkbookFindAllMatch CreateFindAllMatch(FindResult result)
    {
        var sheet = Workbook.GetSheet(result.Address.Sheet);
        var cell = sheet?.GetCell(result.Address);
        return new WorkbookFindAllMatch(
            Workbook.Name,
            sheet?.Name ?? "",
            FindNameForAddress(result.Address),
            result.Address,
            result.Address.ToA1(),
            result.MatchedText,
            cell?.HasFormula == true ? cell.FormulaText ?? "" : "");
    }

    private string FindNameForAddress(CellAddress address)
    {
        string? bestName = null;
        var bestCellCount = 0L;
        foreach (var (name, range) in Workbook.NamedRanges)
        {
            if (!range.Contains(address))
                continue;

            var cellCount = range.CellCount;
            if (bestName is null ||
                cellCount < bestCellCount ||
                (cellCount == bestCellCount &&
                 string.Compare(name, bestName, StringComparison.OrdinalIgnoreCase) < 0))
            {
                bestName = name;
                bestCellCount = cellCount;
            }
        }

        return bestName ?? "";
    }

    private int FindSheetIndex(SheetId sheetId, int notFoundIndex = int.MaxValue)
    {
        for (var index = 0; index < Workbook.Sheets.Count; index++)
        {
            if (Workbook.Sheets[index].Id.Equals(sheetId))
                return index;
        }

        return notFoundIndex;
    }

    private SheetId? ResolveSheetIdByName(string sheetName) =>
        Workbook.GetSheet(sheetName)?.Id;

    private WorkbookNavigationResult GoToReviewNavigationPlan(ReviewNavigationPlan plan) =>
        plan is { Success: true, Target: { } target }
            ? GoToCell(target)
            : WorkbookNavigationResult.Failed(plan.ErrorMessage ?? "Review target was not found.");

    public WorkbookCellEditResult SetFreezePanes(uint frozenRows, uint frozenCols)
    {
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetFreezePanesCommand(ActiveSheet.Id, frozenRows, frozenCols));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        // Reseed this view's own caches with the values just applied -- see _viewFrozenRowsOverrides
        // remarks (mirrors SetZoomPercent's reseed of _viewZoomOverrides).
        _viewFrozenRowsOverrides[ActiveSheet.Id] = frozenRows;
        _viewFrozenColsOverrides[ActiveSheet.Id] = frozenCols;
        return result;
    }

    private WorkbookCellEditResult SetWorksheetViewOptions(bool showGridlines, bool showHeadings, bool showRulers)
    {
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetWorksheetViewOptionsCommand(ActiveSheet.Id, showGridlines, showHeadings, showRulers));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        // Reseed this view's own caches with the values just applied -- see _viewShowGridlinesOverrides
        // remarks (mirrors SetZoomPercent's reseed of _viewZoomOverrides). ShowRulers has no per-view
        // override (not part of this sweep), so it keeps reading the shared Sheet field directly.
        _viewShowGridlinesOverrides[ActiveSheet.Id] = showGridlines;
        _viewShowHeadingsOverrides[ActiveSheet.Id] = showHeadings;
        return result;
    }

    private CellStyle GetCellStyle(CellAddress address)
    {
        var sheet = Workbook.GetSheet(address.Sheet);
        var styleId = sheet?.GetCell(address)?.StyleId ??
            sheet?.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return Workbook.GetStyle(styleId);
    }

    private static StyleDiff CreateUnderlineStyleDiff(bool enabled) =>
        new(Underline: enabled, Strikethrough: enabled ? false : null);

    private static StyleDiff CreateStrikethroughStyleDiff(bool enabled) =>
        new(Strikethrough: enabled, Underline: enabled ? false : null, DoubleUnderline: enabled ? false : null);

    private static StyleDiff CreateDoubleUnderlineStyleDiff(bool enabled) =>
        new(DoubleUnderline: enabled, Underline: enabled ? false : null, Strikethrough: enabled ? false : null);

    private WorkbookCellEditResult PasteInternalClipboardAtActiveCell(
        WorkbookClipboardSnapshot clipboard,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths = false)
    {
        var destination = ActiveCell;
        var expandPasteToSelectedRange = ShouldFillSelectedDestinationRange(clipboard.IsCut, options);
        var destinationRange = expandPasteToSelectedRange
            ? GetSinglePasteDestinationRange(destination)
            : new GridRange(destination, destination);

        IWorkbookCommand command;
        if (TryCreateCutMoveCommand(clipboard, destination, mode, options, keepSourceColumnWidths, out var moveCommand))
        {
            // Excel cut+paste is a MOVE: the moved formulas keep their own references unchanged,
            // while OTHER formulas that pointed at the cut cells are rewritten to follow the move.
            // That is exactly MoveRangeCommand/MoveRangeOp semantics, so route cut+paste through it
            // instead of the copy-paste-and-clear combo (which would incorrectly rewrite the moved
            // formulas' own references and never fix up references from other cells).
            command = moveCommand;
        }
        else
        {
            command = CreateInternalPasteCommand(
                clipboard,
                destinationRange,
                mode,
                options,
                keepSourceColumnWidths);

            if (ShouldClearCutSourceAfterPaste(clipboard, destination, mode, options, keepSourceColumnWidths))
            {
                command = new CompositeWorkbookCommand(
                    "Cut and Paste",
                    [
                        command,
                        // R38-commands-cut-move-2-3: mark this as the tail end of a Cut so a
                        // merged source cell gets unmerged (not just cleared) when the cut can't
                        // be routed through MoveRangeCommand (e.g. a cross-sheet destination).
                        new ClearContentsCommand(clipboard.SourceRange.Start.Sheet, clipboard.SourceRange, isCutSource: true)
                    ]);
            }
        }

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        var pasteSize = GetPasteDimensions(clipboard.SourceRange, options.Transpose);
        if (expandPasteToSelectedRange)
        {
            pasteSize = (
                Math.Max(pasteSize.RowCount, destinationRange.RowCount),
                Math.Max(pasteSize.ColCount, destinationRange.ColCount));
        }

        // Excel always lands the selection (and the Name Box/formula bar/viewport) on the
        // pasted DESTINATION range, never on the source of a cut. MoveRangeCommand's
        // AffectedCells lists the source cell before the destination, so anchoring on
        // result.AffectedCells here (via ApplySuccessfulEditResult) would leave ActiveCell
        // pointing at the now-blank source cell for cut+paste moves. Anchor explicitly on
        // the destination range instead.
        var pastedRange = TryGetRectangleEnd(destination, pasteSize.RowCount, pasteSize.ColCount, out var pastedEnd)
            ? new GridRange(destination, pastedEnd)
            : new GridRange(destination, destination);
        ApplySuccessfulRangeEditResult(result, pastedRange);

        if (clipboard.IsCut)
        {
            _workbookClipboardSession.CompletePaste(clipboard);
            // R132-clipboard-cut-move-os-invalidation: signal the completed Cut+Paste MOVE back to
            // the host shell so it can invalidate the real OS clipboard (mirrors the WPF host's
            // InvalidateOsClipboardAfterCutMove, called from this exact same IsCut branch of its
            // own ExecutePaste). Without this, a later Ctrl+V falls through to the
            // external-clipboard path (since the workbook clipboard session is now empty) and re-pastes the OS
            // clipboard's still-stale cut payload a second time.
            result = result with { ClipboardCutMoveCompleted = true };
        }
        return result;
    }

    private WorkbookCellEditResult PasteInternalClipboardToSelectedRanges(
        WorkbookClipboardSnapshot clipboard,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths)
    {
        var selectedRanges = GetCurrentSelectedRanges();
        var pasteSize = GetPasteDimensions(clipboard.SourceRange, options.Transpose);
        if (clipboard.IsCut ||
            !SelectedRangesMatchPasteSize(selectedRanges, pasteSize.RowCount, pasteSize.ColCount))
        {
            return new WorkbookCellEditResult(
                false,
                CreateMultiRangeClipboardError("Paste Special"),
                [],
                RecalcReport: null);
        }

        var command = CreateInternalPasteCommand(
            clipboard,
            selectedRanges.Select(range => range.Start).ToArray(),
            mode,
            options,
            keepSourceColumnWidths);
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    private string SerializeMultiRangeCopy(MultiRangeCopyLayout layout)
    {
        var lookup = new Dictionary<(uint Row, uint Col), DisplayCell>(Viewport.Cells.Count);
        foreach (var cell in Viewport.Cells)
            lookup[(cell.Row, cell.Col)] = cell;

        var areas = layout.OrderedAreas;
        var sheet = areas[0].Start.Sheet;

        uint blockRows;
        uint blockColumns;
        if (layout.Orientation == MultiRangeCopyOrientation.SideBySideColumns)
        {
            blockRows = areas[0].End.Row - areas[0].Start.Row + 1;
            blockColumns = 0;
            foreach (var area in areas)
                blockColumns += area.End.Col - area.Start.Col + 1;
        }
        else
        {
            blockColumns = areas[0].End.Col - areas[0].Start.Col + 1;
            blockRows = 0;
            foreach (var area in areas)
                blockRows += area.End.Row - area.Start.Row + 1;
        }

        var blockCells = new List<DisplayCell>(checked((int)((long)blockRows * blockColumns)));
        for (uint blockRow = 0; blockRow < blockRows; blockRow++)
        {
            for (uint blockColumn = 0; blockColumn < blockColumns; blockColumn++)
            {
                var (sourceRow, sourceColumn) = MapBlockToSource(layout, blockRow, blockColumn);
                if (lookup.TryGetValue((sourceRow, sourceColumn), out var displayCell))
                    blockCells.Add(displayCell with { Row = blockRow, Col = blockColumn });
                else
                    blockCells.Add(new DisplayCell(blockRow, blockColumn, null, string.Empty, null, default, null));
            }
        }

        var blockViewport = Viewport with { Cells = blockCells };
        var blockRange = new GridRange(
            new CellAddress(sheet, 0, 0),
            new CellAddress(sheet, blockRows - 1, blockColumns - 1));
        return ClipboardSerializer.Serialize(blockViewport, blockRange);
    }

    private static (uint Row, uint Col) MapBlockToSource(
        MultiRangeCopyLayout layout,
        uint blockRow,
        uint blockColumn)
    {
        var areas = layout.OrderedAreas;
        if (layout.Orientation == MultiRangeCopyOrientation.SideBySideColumns)
        {
            var sourceRow = areas[0].Start.Row + blockRow;
            uint cursor = 0;
            foreach (var area in areas)
            {
                var width = area.End.Col - area.Start.Col + 1;
                if (blockColumn < cursor + width)
                    return (sourceRow, area.Start.Col + (blockColumn - cursor));
                cursor += width;
            }

            return (sourceRow, areas[^1].End.Col);
        }

        var sourceColumn = areas[0].Start.Col + blockColumn;
        uint rowCursor = 0;
        foreach (var area in areas)
        {
            var height = area.End.Row - area.Start.Row + 1;
            if (blockRow < rowCursor + height)
                return (area.Start.Row + (blockRow - rowCursor), sourceColumn);
            rowCursor += height;
        }

        return (areas[^1].End.Row, sourceColumn);
    }

    private WorkbookClipboardSnapshot CaptureInternalClipboard(GridRange range, string text, bool isCut, ViewportModel viewport)
    {
        var sheet = Workbook.GetSheet(range.Start.Sheet);
        var cells = new List<(CellAddress Source, Cell Cell)>();
        var pictureCells = CapturePictureCells(range, sheet, viewport);
        foreach (var address in range.AllCells())
        {
            var cell = sheet?.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance);
            cells.Add((address, cell));
        }

        return new WorkbookClipboardSnapshot(range, cells, pictureCells, text, isCut);
    }

    private List<(CellAddress Source, PictureCellSnapshot Snapshot)> CapturePictureCells(
        GridRange range, Sheet? sheet, ViewportModel viewport)
    {
        var displayCells = new Dictionary<(uint Row, uint Col), DisplayCell>(viewport.Cells.Count);
        foreach (var cell in viewport.Cells)
            displayCells[(cell.Row, cell.Col)] = cell;

        var result = new List<(CellAddress, PictureCellSnapshot)>();
        foreach (var address in range.AllCells())
        {
            if (displayCells.TryGetValue((address.Row, address.Col), out var displayCell))
            {
                result.Add((
                    address,
                    new PictureCellSnapshot(
                        address.Row - range.Start.Row,
                        address.Col - range.Start.Col,
                        displayCell.DisplayText,
                        displayCell.Style?.Clone(),
                        displayCell.RawValue is NumberValue or DateTimeValue)));
                continue;
            }

            var cell = sheet?.GetCell(address);
            var fallbackStyleId = cell?.StyleId
                ?? sheet?.GetStyleOnly(address.Row, address.Col)
                ?? StyleId.Default;
            result.Add((
                address,
                new PictureCellSnapshot(
                    address.Row - range.Start.Row,
                    address.Col - range.Start.Col,
                    FormatPictureCellText(cell?.Value ?? BlankValue.Instance, Workbook.GetStyle(fallbackStyleId).NumberFormat),
                    null,
                    cell?.Value is NumberValue or DateTimeValue)));
        }

        return result;
    }

    private bool TryCreateMultiRangeClipboardEditResult(
        string operation,
        out WorkbookCellEditResult result)
    {
        if (SelectedRanges.Count <= 1)
        {
            result = new WorkbookCellEditResult(true, null, [], RecalcReport: null);
            return false;
        }

        result = new WorkbookCellEditResult(
            false,
            CreateMultiRangeClipboardError(operation),
            [],
            RecalcReport: null);
        return true;
    }

    private static string CreateMultiRangeClipboardError(string operation) =>
        operation + MultiRangeClipboardErrorSuffix;

    private bool TryCreateCutMoveCommand(
        WorkbookClipboardSnapshot clipboard,
        CellAddress destination,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths,
        out IWorkbookCommand command)
    {
        command = null!;
        if (!clipboard.IsCut || keepSourceColumnWidths)
            return false;

        // Excel's Cut is always a MOVE: the moved formulas keep their own references unchanged
        // and any OTHER formula that pointed at the cut cells is rewritten to follow the move.
        // The plain "Paste" gesture (no Paste Special mode/options) gets that fixup via a straight
        // MoveRangeCommand. A handful of other Paste Special variants are simple enough to express
        // the same way: perform the real move (so references are fixed up both ways) and then
        // finish each moved cell per the paste mode's own content rule
        // (R20-paste-special-operations-1, Backlog-paste-special-cut-routing-part-a):
        //   - Values (mode == Values, no Transpose/Operation/SkipBlanks): collapse to the computed
        //     value, keeping the destination's own pre-paste style.
        //   - Formulas (mode == Formulas, no Transpose/Operation/SkipBlanks): keep the moved
        //     formula/value untouched, but restore the destination's own pre-paste style.
        //   - Formulas and Number Formats (mode == All, ContentKind ==
        //     FormulasAndNumberFormats, no Transpose/Operation/SkipBlanks): same as Formulas, but
        //     the destination's style keeps the MOVED (source) cell's number format merged in.
        // Any other Paste Special mode/option (All/AllExceptBorders-style content kinds, Transpose,
        // Operations, Skip Blanks, Formats-only) still falls back to the legacy copy+clear
        // behaviour below, which does not fix up references either direction.
        var isPlainPaste = mode == PasteCellsMode.All && options == default;
        var isPasteSpecialValuesOnly = mode == PasteCellsMode.Values && options == default;
        var isPasteSpecialFormulasOnly = mode == PasteCellsMode.Formulas && options == default;
        var isPasteSpecialFormulasAndNumberFormats =
            mode == PasteCellsMode.All &&
            options.ContentKind == PasteSpecialContentKind.FormulasAndNumberFormats &&
            !options.Transpose &&
            options.Operation == PasteSpecialOperation.None &&
            !options.SkipBlanks;
        if (!isPlainPaste &&
            !isPasteSpecialValuesOnly &&
            !isPasteSpecialFormulasOnly &&
            !isPasteSpecialFormulasAndNumberFormats)
        {
            return false;
        }

        // Grouped multi-sheet editing can't be expressed as a single move, so fall back regardless
        // of destination sheet.
        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (targetSheetIds.Count != 1 || !targetSheetIds[0].Equals(clipboard.SourceRange.Start.Sheet))
            return false;

        // R38-commands-cut-move-2-1: MoveRangeCommand now supports a cross-sheet destination for
        // the plain-paste case (isPlainPaste below) -- a real Excel Cut+Paste across sheets is
        // still a MOVE, not a copy-and-clear, so routing it through MoveRangeCommand is what keeps
        // the moved formula's own references pointing at exactly what they pointed at before (with
        // an explicit sheet qualifier added only where the reference stays behind on the source
        // sheet) and fixes up any OTHER formula in the workbook that referenced a moved cell.
        // The Paste Special variants below (Values / Formulas / Formulas-and-Number-Formats) go
        // through CutPasteMoveCommand, which still only supports a same-sheet move, so those still
        // fall back to the legacy copy+clear path when the destination is on a different sheet.
        if (!isPlainPaste && !destination.Sheet.Equals(clipboard.SourceRange.Start.Sheet))
            return false;

        if (isPlainPaste)
        {
            command = new MoveRangeCommand(clipboard.SourceRange.Start.Sheet, clipboard.SourceRange, destination);
            return true;
        }

        var finalizeKind = isPasteSpecialValuesOnly
            ? CutPasteFinalizeKind.Values
            : isPasteSpecialFormulasAndNumberFormats
                ? CutPasteFinalizeKind.FormulasAndNumberFormat
                : CutPasteFinalizeKind.Formulas;
        command = new CutPasteMoveCommand(
            clipboard.SourceRange.Start.Sheet, clipboard.SourceRange, destination, finalizeKind);
        return true;
    }

    /// <summary>
    /// How <see cref="CutPasteMoveCommand"/> finishes each cell after the underlying
    /// <see cref="MoveRangeCommand"/> has relocated it and fixed up formula references both ways.
    /// </summary>
    private enum CutPasteFinalizeKind
    {
        /// <summary>Paste Special &gt; Values: collapse to the computed value, keep the destination's own style.</summary>
        Values,

        /// <summary>Paste Special &gt; Formulas: keep the moved formula/value, restore the destination's own style.</summary>
        Formulas,

        /// <summary>Paste Special &gt; Formulas and Number Formats: like Formulas, but merge the moved cell's number format into the destination's style.</summary>
        FormulasAndNumberFormat
    }

    /// <summary>
    /// R20-paste-special-operations-1 / Backlog-paste-special-cut-routing-part-a: routes a Cut +
    /// non-default Paste Special (Values / Formulas / Formulas-and-Number-Formats) through the same
    /// move-based reference fixup as a plain Cut + Paste (see <see cref="TryCreateCutMoveCommand"/>),
    /// so that (a) the moved formula's own references are left untouched by the move (rather than
    /// mis-rewritten as a relative-copy offset) and (b) any OTHER formula that referenced the cut
    /// cells is rewritten to follow the move — both of which the legacy copy-paste-and-clear path
    /// gets wrong for every non-default Paste Special invocation. It delegates the actual cell
    /// relocation (and the accompanying formula/comment/hyperlink/sparkline/named-range fixups) to
    /// the real <see cref="MoveRangeCommand"/>, then finalizes each moved cell per
    /// <see cref="CutPasteFinalizeKind"/>: the destination always keeps its own pre-paste style
    /// (merged with the moved cell's number format for FormulasAndNumberFormat), and Values drops
    /// the formula while Formulas/FormulasAndNumberFormat keep it as the move produced it.
    /// </summary>
    private sealed class CutPasteMoveCommand : IWorkbookCommand, IAffectedCellsCommand
    {
        private readonly SheetId _sheetId;
        private readonly GridRange _sourceRange;
        private readonly CellAddress _destination;
        private readonly CutPasteFinalizeKind _finalizeKind;
        private readonly MoveRangeCommand _moveCommand;

        public string Label => "Paste Special";

        public IReadOnlyList<CellAddress> AffectedCells => _moveCommand.AffectedCells;

        public CutPasteMoveCommand(
            SheetId sheetId, GridRange sourceRange, CellAddress destination, CutPasteFinalizeKind finalizeKind)
        {
            _sheetId = sheetId;
            _sourceRange = sourceRange;
            _destination = destination;
            _finalizeKind = finalizeKind;
            _moveCommand = new MoveRangeCommand(sheetId, sourceRange, destination);
        }

        public CommandOutcome Apply(ICommandContext ctx)
        {
            var sheet = ctx.GetSheet(_sheetId);
            var rowDelta = (long)_destination.Row - _sourceRange.Start.Row;
            var colDelta = (long)_destination.Col - _sourceRange.Start.Col;

            // Capture each destination cell's PRE-paste style before the move overwrites it: every
            // routed Paste Special variant here keeps the destination's own existing formatting,
            // unlike a plain move/paste which brings the source's style along.
            var originalDestinationStyles = new Dictionary<CellAddress, StyleId>();
            foreach (var source in _sourceRange.AllCells())
            {
                var target = new CellAddress(
                    _destination.Sheet,
                    checked((uint)(source.Row + rowDelta)),
                    checked((uint)(source.Col + colDelta)));
                originalDestinationStyles[target] =
                    sheet.GetCell(target)?.StyleId ?? sheet.GetStyleOnly(target.Row, target.Col) ?? StyleId.Default;
            }

            var outcome = _moveCommand.Apply(ctx);
            if (!outcome.Success)
                return outcome;

            foreach (var (target, styleId) in originalDestinationStyles)
            {
                var movedCell = sheet.GetCell(target);
                Cell finalCell;
                StyleId finalStyleId;
                switch (_finalizeKind)
                {
                    case CutPasteFinalizeKind.Formulas:
                        finalCell = movedCell?.Clone() ?? Cell.FromValue(BlankValue.Instance);
                        finalStyleId = styleId;
                        break;

                    case CutPasteFinalizeKind.FormulasAndNumberFormat:
                        finalCell = movedCell?.Clone() ?? Cell.FromValue(BlankValue.Instance);
                        finalStyleId = MergeNumberFormat(ctx.Workbook, styleId, movedCell?.StyleId ?? styleId);
                        break;

                    default: // Values
                        finalCell = Cell.FromValue(movedCell?.Value ?? BlankValue.Instance);
                        finalStyleId = styleId;
                        break;
                }

                finalCell.StyleId = finalStyleId;
                sheet.SetCell(target, finalCell);
            }

            return outcome;
        }

        private static StyleId MergeNumberFormat(Workbook workbook, StyleId destinationStyleId, StyleId sourceStyleId)
        {
            var style = workbook.GetStyle(destinationStyleId).Clone();
            style.NumberFormat = workbook.GetStyle(sourceStyleId).NumberFormat;
            return workbook.RegisterStyle(style);
        }

        public void Revert(ICommandContext ctx) => _moveCommand.Revert(ctx);
    }

    private static bool ShouldClearCutSourceAfterPaste(
        WorkbookClipboardSnapshot clipboard,
        CellAddress destination,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths)
    {
        if (!clipboard.IsCut || mode == PasteCellsMode.Formats || keepSourceColumnWidths)
            return false;

        var rowCount = options.Transpose ? clipboard.SourceRange.ColCount : clipboard.SourceRange.RowCount;
        var colCount = options.Transpose ? clipboard.SourceRange.RowCount : clipboard.SourceRange.ColCount;

        if (!TryGetRectangleEnd(
                destination,
                rowCount,
                colCount,
                out var pastedEnd))
        {
            return false;
        }

        return !clipboard.SourceRange.Overlaps(new GridRange(destination, pastedEnd));
    }

    // An arithmetic Operation (Add/Subtract/Multiply/Divide) must still tile across a larger
    // selected destination just like a plain paste — Excel applies the operation cell-by-cell
    // to every destination cell, tiling the (possibly 1-cell) clipboard source across the whole
    // selection, not just the anchor cell (R16-paste-special-matrix-1). The same is true for
    // "All merging conditional formats": Core.Commands' PasteCommandFactory tiles its copied
    // values/formats exactly like every other Paste Special content kind
    // (R25-clipboard-paste-remaining-2) — the caller must expand the destination for this content
    // kind too, or that tiling code path is unreachable from the real paste flow (R99-clipboard-
    // paste-merge-cf-tile).
    private static bool ShouldFillSelectedDestinationRange(bool isCut, PasteSpecialOptions options) =>
        !isCut;

    private GridRange GetSinglePasteDestinationRange(CellAddress destination) =>
        SelectedRanges.Count <= 1
            ? SelectedRange
            : new GridRange(destination, destination);

    private static (ulong RowCount, ulong ColCount) GetPasteDimensions(GridRange sourceRange, bool transpose) =>
        transpose
            ? (sourceRange.ColCount, sourceRange.RowCount)
            : (sourceRange.RowCount, sourceRange.ColCount);

    private static bool SelectedRangesMatchPasteSize(
        IReadOnlyList<GridRange> selectedRanges,
        ulong rowCount,
        ulong colCount) =>
        selectedRanges.All(range => range.RowCount == rowCount && range.ColCount == colCount);

    private void SelectPastedRange(CellAddress start, ulong rowCount, ulong colCount)
    {
        if (rowCount == 0 || colCount == 0)
            return;

        if (!TryGetRectangleEnd(start, rowCount, colCount, out var end))
            return;

        SetSingleSelectedRange(new GridRange(start, end));
    }

    private void EnsureActiveCellVisible() => EnsureCellVisible(ActiveCell);

    /// <summary>
    /// Scrolls the minimum amount needed to bring <paramref name="cell"/> into the scrollable viewport.
    /// Callers pass the cell the user is navigating toward: the active cell for a plain move, but the
    /// moving CURSOR end for a drag / shift-extend (see <see cref="SelectAnchoredRange"/>) where the
    /// anchor stays put while the viewport follows the cursor -- matching Excel and the WPF host.
    /// </summary>
    private void EnsureCellVisible(CellAddress cell)
    {
        var changed = false;
        if (TryGetScrollableRowRange(out var firstRow, out var lastRow) &&
            !IsFrozenRow(cell.Row) &&
            (cell.Row < firstRow || cell.Row > lastRow))
        {
            var topRow = CalculateScrollOrigin(
                cell.Row,
                firstRow,
                lastRow,
                GetViewTopRow(),
                CellAddress.MaxRow);
            SetViewViewportOrigin(topRow, GetViewLeftCol());
            changed = true;
        }

        if (TryGetScrollableColumnRange(out var firstCol, out var lastCol) &&
            !IsFrozenColumn(cell.Col) &&
            (cell.Col < firstCol || cell.Col > lastCol))
        {
            var leftCol = CalculateScrollOrigin(
                cell.Col,
                firstCol,
                lastCol,
                GetViewLeftCol(),
                CellAddress.MaxCol);
            SetViewViewportOrigin(GetViewTopRow(), leftCol);
            changed = true;
        }

        if (changed)
            RefreshViewport();
    }

    private bool TryGetScrollableRowRange(out uint firstRow, out uint lastRow)
    {
        var frozenRows = GetEffectiveFrozenRows();
        firstRow = 1;
        lastRow = 1;
        var found = false;
        foreach (var metric in Viewport.RowMetrics)
        {
            if (metric.Row <= frozenRows)
                continue;

            if (!found)
            {
                firstRow = metric.Row;
                lastRow = metric.Row;
                found = true;
            }
            else
            {
                lastRow = metric.Row;
            }
        }

        return found;
    }

    private bool TryGetScrollableColumnRange(out uint firstCol, out uint lastCol)
    {
        var frozenCols = GetEffectiveFrozenCols();
        firstCol = 1;
        lastCol = 1;
        var found = false;
        foreach (var metric in Viewport.ColMetrics)
        {
            if (metric.Col <= frozenCols)
                continue;

            if (!found)
            {
                firstCol = metric.Col;
                lastCol = metric.Col;
                found = true;
            }
            else
            {
                lastCol = metric.Col;
            }
        }

        return found;
    }

    private bool IsFrozenRow(uint row)
    {
        var frozenRows = GetEffectiveFrozenRows();
        return frozenRows > 0 && row <= frozenRows;
    }

    private bool IsFrozenColumn(uint col)
    {
        var frozenCols = GetEffectiveFrozenCols();
        return frozenCols > 0 && col <= frozenCols;
    }

    private uint GetScrollableRowStart() =>
        Math.Min(CellAddress.MaxRow, Math.Max(1, GetEffectiveFrozenRows() + 1));

    private uint GetScrollableColumnStart() =>
        Math.Min(CellAddress.MaxCol, Math.Max(1, GetEffectiveFrozenCols() + 1));

    /// <summary>
    /// This view's effective frozen-row count (see <see cref="_viewFrozenRowsOverrides"/> remarks):
    /// a pure peek at this view's own snapshot (seeded up front by <see cref="SeedViewSplitAndFrozenOverrides"/>,
    /// kept in sync by every Freeze-Panes-changing command apply/undo/redo), falling back to the shared
    /// <see cref="Sheet.FrozenRows"/> only when this view has no snapshot at all -- so a sibling view's
    /// Freeze Panes change never retroactively changes what this view scrolls/renders around, and merely
    /// reading this (e.g. from <see cref="BuildViewport"/> during an unrelated <see cref="RefreshViewport"/>)
    /// never itself freezes in a stale value. Public (R87) so hosts/consumers outside this class (e.g.
    /// the Avalonia shell, the Core.Calc viewport pipeline) can read this view's own per-window
    /// frozen-row count instead of falling back to the shared <see cref="Sheet.FrozenRows"/> field directly.
    /// </summary>
    public uint GetEffectiveFrozenRows() =>
        _viewFrozenRowsOverrides.TryGetValue(ActiveSheet.Id, out var frozenRows) ? frozenRows : ActiveSheet.FrozenRows;

    /// <summary>
    /// This view's effective frozen-column count. See <see cref="GetEffectiveFrozenRows"/>.
    /// </summary>
    public uint GetEffectiveFrozenCols() =>
        _viewFrozenColsOverrides.TryGetValue(ActiveSheet.Id, out var frozenCols) ? frozenCols : ActiveSheet.FrozenCols;

    private static uint CalculateScrollOrigin(
        uint active,
        uint firstVisible,
        uint lastVisible,
        uint currentOrigin,
        uint max)
    {
        if (active < firstVisible)
            return active;

        if (active > lastVisible)
            return Offset(currentOrigin, checked((int)(active - lastVisible)), max);

        return currentOrigin;
    }

    private static uint Offset(uint value, int delta, uint max)
    {
        var candidate = (long)value + delta;
        return (uint)Math.Clamp(candidate, 1, max);
    }

    private static bool IsValidAddress(CellAddress address) =>
        address.Row is >= 1 and <= CellAddress.MaxRow &&
        address.Col is >= 1 and <= CellAddress.MaxCol;

    private static bool TryGetRectangleEnd(
        CellAddress start,
        ulong rowCount,
        ulong colCount,
        out CellAddress end)
    {
        end = default;
        if (!IsValidAddress(start))
            return false;

        try
        {
            var endRow = checked((ulong)start.Row + rowCount - 1UL);
            var endCol = checked((ulong)start.Col + colCount - 1UL);
            if (endRow > CellAddress.MaxRow || endCol > CellAddress.MaxCol)
                return false;

            end = new CellAddress(start.Sheet, (uint)endRow, (uint)endCol);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static double NormalizeViewportDimension(double value, double fallback)
    {
        if (!double.IsFinite(value) || value <= 0)
            return Math.Max(1, Math.Ceiling(fallback));

        return Math.Max(1, Math.Ceiling(value));
    }

    /// <summary>
    /// Renders a linked picture's cell text using the source cell's own number format, so a linked
    /// picture keeps showing the formatted value (e.g. "$1,234.50") on every refresh, exactly as it
    /// did at the moment it was pasted (Excel camera parity; see R14-camera-linked-picture-2). A raw
    /// <c>ToString(CultureInfo.CurrentCulture)</c> would silently strip currency/percent/date/custom
    /// formats after the first source-cell edit.
    /// </summary>
    private string FormatPictureCellText(ScalarValue value, string numberFormat) =>
        FreeX.Core.Formula.NumberFormatter.Format(value, numberFormat, Workbook.Uses1904DateSystem);

    private static IReadOnlyList<FileFormatDescriptor> BuildFormats(
        IReadOnlyList<IFileAdapter> adapters,
        Func<FileFormatDescriptor, bool> predicate) =>
        adapters
            .SelectMany(adapter => adapter.Formats)
            .Where(predicate)
            .GroupBy(format => FileFormatResolver.NormalizeExtension(format.Extension), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private uint GetViewTopRow() =>
        _viewViewportOrigins.TryGetValue(ActiveSheet.Id, out var origin)
            ? origin.TopRow
            : ActiveSheet.ViewTopRow ?? GetScrollableRowStart();

    private uint GetViewLeftCol() =>
        _viewViewportOrigins.TryGetValue(ActiveSheet.Id, out var origin)
            ? origin.LeftCol
            : ActiveSheet.ViewLeftCol ?? GetScrollableColumnStart();

    private void SetViewViewportOrigin(uint topRow, uint leftCol)
    {
        _viewViewportOrigins[ActiveSheet.Id] = (topRow, leftCol);
        if (_sharedDocumentStateOwner is null)
        {
            ActiveSheet.ViewTopRow = topRow;
            ActiveSheet.ViewLeftCol = leftCol;
        }
    }

    private ViewportModel BuildViewport() =>
        _viewportService.GetViewport(
            Workbook,
            ActiveSheet.Id,
            new ViewportRequest(
                GetViewTopRow(),
                GetViewLeftCol(),
                AvailableHeight: _viewportHeight,
                AvailableWidth: _viewportWidth,
                IncludeObjects: _includeObjects,
                SplitPaneOffsets: GetSplitPaneOffsetsForActiveSheet(),
                FrozenRowsOverride: GetEffectiveFrozenRows(),
                FrozenColsOverride: GetEffectiveFrozenCols(),
                SplitOverride: new SplitPaneStateOverride(GetEffectiveSplitRow(), GetEffectiveSplitCol())));

    /// <summary>
    /// Returns the TopRight/BottomLeft independent-scroll offsets for the active sheet, if it has
    /// an active Window ▸ Split and any offsets were recorded; discards (and forgets) stale
    /// entries left over from a since-removed split so <see cref="_splitPaneViewportOffsets"/>
    /// never grows unbounded. See <see cref="ScrollSplitPaneTopRight"/>/
    /// <see cref="ScrollSplitPaneBottomLeft"/> for how offsets are recorded.
    /// </summary>
    private SplitPaneViewportOffsets? GetSplitPaneOffsetsForActiveSheet()
    {
        if (ActiveSheet.SplitRow is null && ActiveSheet.SplitColumn is null)
        {
            _splitPaneViewportOffsets.Remove(ActiveSheet.Id);
            return null;
        }

        return _splitPaneViewportOffsets.TryGetValue(ActiveSheet.Id, out var offsets) ? offsets : null;
    }

    private bool CanWriteTarget(string path, out string message)
    {
        if (IsXlsxPath(path) && CurrentXlsxFeatureReport?.HasUnsupportedFeatures == true)
        {
            message = "Save As FreeX Workbook to avoid dropping unsupported XLSX features.";
            return false;
        }

        message = "";
        return true;
    }

    private static bool IsXlsxPath(string path) =>
        string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);

    private static CellAddress GetInitialActiveCell(Sheet sheet) =>
        new(sheet.Id, Math.Max(1, sheet.ActiveRow ?? 1), Math.Max(1, sheet.ActiveCol ?? 1));

    private static string FormatStartupStatus(StartupWorkbookLoadResult source)
    {
        var status = source.Status;
        if (source.OpenedAsTemplate)
            status += " Opened as template.";
        if (source.FeatureReport?.HasUnsupportedFeatures == true)
            status += " Unsupported XLSX features detected.";
        if (source.LoadWarnings is { Count: > 0 } warnings)
            status += $" {warnings.Count} load warning{(warnings.Count == 1 ? "" : "s")}.";

        return status;
    }

    private static WorkbookFileAccessIdentity? ResolveCurrentFileAccessIdentity(StartupWorkbookLoadResult source)
    {
        if (source.OpenedAsTemplate)
            return null;

        if (source.SourceFileAccessIdentity is not null)
            return source.SourceFileAccessIdentity;

        return string.IsNullOrWhiteSpace(source.SourcePath)
            ? null
            : WorkbookFileAccessIdentity.FromLocalPath(source.SourcePath);
    }

    private WorkbookFileAccessIdentity ResolveSavedFileAccessIdentity(
        string savedPath,
        WorkbookFileAccessIdentity? fileAccessIdentity)
    {
        if (fileAccessIdentity is not null)
            return WorkbookOpenTargetPlanner.ResolveFileAccessIdentity(savedPath, fileAccessIdentity);

        if (CurrentFileAccessIdentity is not null &&
            CurrentFilePath is not null &&
            PlatformPathIdentityComparer.Current.Equals(CurrentFilePath, savedPath) &&
            CurrentFileAccessIdentity.TryWithLocalPath(savedPath, out var retainedIdentity) &&
            retainedIdentity is not null)
        {
            return retainedIdentity;
        }

        return WorkbookFileAccessIdentity.FromLocalPath(savedPath);
    }
}

public sealed record WorkbookClipboardTextResult(
    bool Success,
    string? Text,
    string? ErrorMessage,
    ViewportModel? Viewport = null,
    string? ClipboardMarker = null)
{
    /// <summary>
    /// Succeeds with the full-range viewport (see <c>WorkbookSession.BuildFullRangeViewportForClipboard</c>)
    /// the text was serialized from, so callers building a CF_HTML fragment for the same copy/cut
    /// (e.g. the Avalonia shell's clipboard handler) render off the same complete range instead of
    /// re-reading the on-screen-only <see cref="WorkbookSession.Viewport"/> and truncating any part of
    /// the selection that is scrolled out of view (R14-clipboard-formats-deep-1).
    /// </summary>
    public static WorkbookClipboardTextResult Succeeded(
        string text,
        ViewportModel viewport,
        string? clipboardMarker = null) =>
        new(true, text, null, viewport, clipboardMarker);

    public static WorkbookClipboardTextResult Succeeded(string text) =>
        new(true, text, null);

    public static WorkbookClipboardTextResult Failed(string errorMessage) =>
        new(false, null, errorMessage);
}
