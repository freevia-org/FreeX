using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using CommandHistoryEntry = Free.Shared.Commands.CommandHistoryEntry;
using CoreSortKey = FreeX.Core.Commands.SortKey;

namespace FreeX.App.Services;

public sealed class WorkbookSession
{
    private sealed record InternalClipboard(
        GridRange SourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> Cells,
        IReadOnlyList<(CellAddress Source, PictureCellSnapshot Snapshot)> PictureCells,
        string Text,
        bool IsCut);

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

    private const double MaximumRowHeight = 409.5;
    private const string MultiRangeClipboardErrorSuffix =
        " does not support multiple selected ranges yet.";

    private static readonly StyleDiff EmptyStyleDiff = new();

    private readonly IReadOnlyList<IFileAdapter> _adapters;
    private readonly StartupWorkbookLoadResult _source;
    private readonly WorkbookCellEditService _cellEditService;
    private readonly WorkbookSheetSelectionService _sheetSelectionService;
    private readonly IViewportService _viewportService;
    private readonly bool _includeObjects;
    private readonly WorkbookSelectionStatsCache _selectionStatsCache = new();
    private readonly WorksheetSelectionStore _worksheetSelections = new();
    private readonly HashSet<SheetId> _groupedSheetIds = [];
    private SheetId? _sheetGroupAnchor;
    private InternalClipboard? _internalClipboard;
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
    private ulong _selectionStatsRevision;
    private string? _lastFindText;
    private FindOptions? _lastFindOptions;
    private bool _lastFindMatchCase;
    private bool _lastFindMatchEntireCell;
    private FindResult? _lastFindResult;
    private FindResult? _lastReplaceResult;

    /// <summary>
    /// The undo-stack depth at the time the workbook was last saved, or <c>-1</c> when no save
    /// point has been recorded yet (never saved this session). Mirrors the WPF host's
    /// <c>WorkbookDocumentState.SavedUndoDepth</c> so <see cref="UndoLastEdit"/>/<see cref="RedoLastEdit"/>
    /// can clear <see cref="IsDirty"/> when the stack returns to this depth, instead of leaving it
    /// permanently true after any edit-then-undo-to-save-point sequence.
    /// </summary>
    private int _savedUndoDepth = -1;

    /// <summary>
    /// The undo stack's monotonic version token at the last save point, or <c>null</c> when no
    /// save point has been recorded. Used alongside <see cref="_savedUndoDepth"/> as a robust
    /// identity check immune to depth-cap trim/refill aliasing (mirrors
    /// <c>WorkbookDocumentState.SavedUndoStackVersion</c>).
    /// </summary>
    private long? _savedUndoStackVersion;

    internal WorkbookSession(
        StartupWorkbookLoadResult source,
        IReadOnlyList<IFileAdapter> adapters,
        WorkbookCellEditService cellEditService,
        WorkbookSheetSelectionService sheetSelectionService,
        IViewportService viewportService,
        double viewportHeight,
        double viewportWidth,
        bool includeObjects)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(cellEditService);
        ArgumentNullException.ThrowIfNull(sheetSelectionService);
        ArgumentNullException.ThrowIfNull(viewportService);

        _source = source;
        _adapters = adapters;
        _cellEditService = cellEditService;
        _sheetSelectionService = sheetSelectionService;
        _viewportService = viewportService;
        _viewportHeight = NormalizeViewportDimension(viewportHeight, fallback: 1);
        _viewportWidth = NormalizeViewportDimension(viewportWidth, fallback: 1);
        _includeObjects = includeObjects;

        Workbook = source.Workbook;
        CurrentFilePath = source.OpenedAsTemplate ? null : source.SourcePath;
        CurrentFileAccessIdentity = ResolveCurrentFileAccessIdentity(source);
        CurrentXlsxFeatureReport = source.FeatureReport;
        OpenFormats = BuildFormats(adapters, static format => format.CanOpen);
        SaveFormats = BuildFormats(adapters, static format => format.CanSave);

        var selection = _sheetSelectionService.EnsureActiveSheet(Workbook);
        ActiveSheet = selection.Sheet;
        SheetTabs = selection.Tabs;
        SelectSingleSheetGroup(ActiveSheet.Id);
        RefreshSheetTabsForActiveSheet();
        ActiveCell = GetInitialActiveCell(ActiveSheet);
        SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
        Viewport = BuildViewport();
    }

    public Workbook Workbook { get; }

    public Sheet ActiveSheet { get; private set; }

    public ViewportModel Viewport { get; private set; }

    public double ViewportHeight => _viewportHeight;

    public double ViewportWidth => _viewportWidth;

    public CellAddress ActiveCell { get; private set; }

    public GridRange SelectedRange { get; private set; }

    public IReadOnlyList<GridRange> SelectedRanges { get; private set; } = [];

    public CellAddress? FormulaEditAddress { get; private set; }

    public IReadOnlyList<WorkbookSheetTab> SheetTabs { get; private set; }

    public bool IsWorkbookGrouped =>
        _groupedSheetIds.Contains(ActiveSheet.Id) &&
        GetSelectableSheetIds().Count(_groupedSheetIds.Contains) > 1;

    public bool IsShowingGridlines => ActiveSheet.ShowGridlines;

    public bool IsShowingHeadings => ActiveSheet.ShowHeadings;

    public bool IsShowingFormulas => ActiveSheet.ShowFormulas;

    public int ZoomPercent => ActiveSheet.ZoomPercent;

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

    public string? CurrentFilePath { get; private set; }

    public WorkbookFileAccessIdentity? CurrentFileAccessIdentity { get; private set; }

    public XlsxFeatureReport? CurrentXlsxFeatureReport { get; private set; }

    public bool IsDirty { get; private set; }

    /// <summary>
    /// Monotonically-increasing counter, incremented with every transition to dirty.
    /// The async save path captures this before awaiting and compares afterwards to detect
    /// edits that arrived mid-save — the same pattern used by <see cref="WorkbookDocumentState"/>.
    /// </summary>
    public int DirtyGeneration { get; private set; }

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

    public bool IsSelectedRangeStartBold => GetCellStyle(SelectedRange.Start).Bold;

    public bool IsSelectedRangeStartItalic => GetCellStyle(SelectedRange.Start).Italic;

    public bool IsSelectedRangeStartUnderline
    {
        get
        {
            var style = GetCellStyle(SelectedRange.Start);
            return style.Underline && !style.Strikethrough;
        }
    }

    public bool IsSelectedRangeStartStrikethrough => GetCellStyle(SelectedRange.Start).Strikethrough;

    public bool IsSelectedRangeStartDoubleUnderline => GetCellStyle(SelectedRange.Start).DoubleUnderline;

    public bool IsSelectedRangeStartWrapText => GetCellStyle(SelectedRange.Start).WrapText;

    public bool IsSelectedRangeStartLocked => GetCellStyle(SelectedRange.Start).Locked;

    public bool IsSelectedRangeMerged => CellMergePlanner.IsSelectionMerged(ActiveSheet, SelectedRange);

    public HorizontalAlignment SelectedRangeStartHorizontalAlignment =>
        GetCellStyle(SelectedRange.Start).HorizontalAlignment;

    public VerticalAlignment SelectedRangeStartVerticalAlignment =>
        GetCellStyle(SelectedRange.Start).VerticalAlignment;

    public int SelectedRangeStartIndentLevel =>
        GetCellStyle(SelectedRange.Start).IndentLevel;

    public double SelectedRangeStartFontSize =>
        GetCellStyle(SelectedRange.Start).FontSize;

    public int SelectedRangeStartTextRotation =>
        GetCellStyle(SelectedRange.Start).TextRotation;

    public CellColor SelectedRangeStartFontColor =>
        GetCellStyle(SelectedRange.Start).FontColor;

    public CellColor? SelectedRangeStartFillColor =>
        GetCellStyle(SelectedRange.Start).FillColor;

    public string SelectedRangeStartNumberFormat =>
        GetCellStyle(SelectedRange.Start).NumberFormat;

    public WorkbookSelectionStats SelectionStats =>
        _selectionStatsCache.GetOrCalculate(ActiveSheet, SelectedRanges, _selectionStatsRevision);

    public string SelectionStatsText =>
        WorkbookSelectionStatsFormatter.Format(SelectionStats);

    public string LastFindText => _lastFindText ?? "";

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

    public void SelectRanges(GridRange primaryRange, IReadOnlyList<GridRange> ranges)
    {
        ValidateSelectionRange(primaryRange, nameof(primaryRange));
        if (ranges.Count == 0)
            throw new ArgumentException("At least one selected range is required.", nameof(ranges));
        foreach (var range in ranges)
            ValidateSelectionRange(range, nameof(ranges));

        SetSelectedRanges(primaryRange, ranges);
        ActiveCell = primaryRange.Start;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        FormulaEditAddress = null;
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

    public bool CanOpenSelectedHyperlink =>
        HyperlinkNavigationPlanner.TryCreatePlan(ActiveSheet, SelectedRange.Start, CurrentFilePath, out _);

    public bool TryGetSelectedHyperlinkPlan(out HyperlinkNavigationPlan? plan) =>
        TryGetHyperlinkPlan(SelectedRange.Start, out plan);

    public bool TryGetHyperlinkPlan(CellAddress address, out HyperlinkNavigationPlan? plan) =>
        HyperlinkNavigationPlanner.TryCreatePlan(ActiveSheet, address, CurrentFilePath, out plan);

    public WorkbookNavigationResult OpenSelectedHyperlink() =>
        OpenHyperlink(SelectedRange.Start);

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
        ISet<ReviewSpellingIssueKey>? ignoredIssues = null) =>
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
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, fallbackAddress ?? ActiveCell);
        return result;
    }

    public WorkbookGoalSeekResult ExecuteGoalSeek(GoalSeekRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = _cellEditService.ExecuteGoalSeek(Workbook, request);
        if (!result.Success || result.EditResult is null)
            return result;

        ApplySuccessfulEditResult(result.EditResult, request.ChangingCell);
        return result;
    }

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
        var text = searchText ?? _lastFindText;
        if (string.IsNullOrEmpty(text))
            return WorkbookNavigationResult.Failed("Find text is required.");

        if (searchText is null && options is null)
        {
            options = _lastFindOptions;
            matchCase = _lastFindMatchCase;
            matchEntireCell = _lastFindMatchEntireCell;
        }

        var effectiveOptions = ResolveFindOptions(options, FindLookIn.Formulas);

        var sameSearch =
            string.Equals(_lastFindText, text, StringComparison.Ordinal) &&
            _lastFindOptions == effectiveOptions &&
            _lastFindMatchCase == matchCase &&
            _lastFindMatchEntireCell == matchEntireCell;

        var results = FindReplaceService.Find(Workbook, text, effectiveOptions, matchCase, matchEntireCell);
        RememberFindSearch(text, effectiveOptions, matchCase, matchEntireCell);

        if (results.Count == 0)
        {
            ClearLastFindTargets();
            return WorkbookNavigationResult.Failed($"No matches found for \"{text}\".");
        }

        var index = GetNextFindResultIndex(results, effectiveOptions.SearchOrder, sameSearch);
        var result = results[index];
        var navigation = GoToCell(result.Address);
        if (!navigation.Success)
            return navigation;

        _lastFindResult = result;
        _lastReplaceResult = null;
        return WorkbookNavigationResult.Found(
            navigation.SelectedRange!.Value,
            result.MatchedText,
            index + 1,
            results.Count);
    }

    public WorkbookFindAllResult FindAll(
        string searchText,
        FindOptions? options = null,
        bool matchCase = false,
        bool matchEntireCell = false)
    {
        ArgumentNullException.ThrowIfNull(searchText);

        if (string.IsNullOrEmpty(searchText))
            return WorkbookFindAllResult.Failed("Find text is required.");

        var effectiveOptions = ResolveFindOptions(options, FindLookIn.Formulas);

        var results = FindReplaceService.Find(Workbook, searchText, effectiveOptions, matchCase, matchEntireCell);
        RememberFindSearch(searchText, effectiveOptions, matchCase, matchEntireCell);
        ClearLastFindTargets();

        return WorkbookFindAllResult.Found(results.Select(CreateFindAllMatch).ToList());
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
        ArgumentNullException.ThrowIfNull(searchText);
        ArgumentNullException.ThrowIfNull(replaceText);

        if (string.IsNullOrEmpty(searchText))
            return WorkbookReplaceResult.Failed("Find text is required.");

        var effectiveOptions = ResolveFindOptions(options, FindLookIn.Values);
        RememberFindSearch(searchText, effectiveOptions, matchCase, matchEntireCell);
        ClearLastFindTargets();

        var matches = FindReplaceService.Find(Workbook, searchText, effectiveOptions, matchCase, matchEntireCell);
        if (matches.Count == 0)
            return WorkbookReplaceResult.Replaced(0);

        var commands = new List<IWorkbookCommand>();
        foreach (var match in matches)
        {
            var sheet = Workbook.GetSheet(match.Address.Sheet);
            if (sheet is null)
                continue;

            if (FindReplaceService.TryCreateReplacementCommand(
                    sheet,
                    match,
                    searchText,
                    replaceText,
                    matchCase,
                    matchEntireCell,
                    effectiveOptions.LookIn,
                    replacementFormat,
                    out var command,
                    workbook: Workbook))
            {
                commands.Add(command);
            }
        }

        var replacedCount = commands.Count;
        if (commands.Count == 0)
            return WorkbookReplaceResult.Replaced(0, matchCount: matches.Count);

        var selectedRange = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            ToCommand("Replace All", commands));
        if (!result.Success)
            return WorkbookReplaceResult.Failed(result.ErrorMessage ?? "Replace All failed.");

        ApplySuccessfulRangeEditResult(result, selectedRange);
        return WorkbookReplaceResult.Replaced(
            replacedCount,
            matchCount: matches.Count);
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
        ArgumentNullException.ThrowIfNull(searchText);
        ArgumentNullException.ThrowIfNull(replaceText);

        if (string.IsNullOrEmpty(searchText))
            return WorkbookReplaceResult.Failed("Find text is required.");

        var effectiveOptions = ResolveFindOptions(options, FindLookIn.Values);
        var sameSearchText =
            string.Equals(_lastFindText, searchText, StringComparison.Ordinal) &&
            _lastFindMatchCase == matchCase &&
            _lastFindMatchEntireCell == matchEntireCell;
        var sameSearch =
            sameSearchText &&
            (_lastFindOptions == effectiveOptions ||
                HasLastFindTargetAtActiveCell());

        var matches = FindReplaceService.Find(Workbook, searchText, effectiveOptions, matchCase, matchEntireCell);
        RememberFindSearch(searchText, effectiveOptions, matchCase, matchEntireCell);

        if (matches.Count == 0)
        {
            ClearLastFindTargets();
            return WorkbookReplaceResult.Replaced(0);
        }

        var index = GetReplaceTargetIndex(matches, effectiveOptions.SearchOrder, sameSearch);
        var match = matches[index];
        var navigation = GoToCell(match.Address);
        if (!navigation.Success)
            return WorkbookReplaceResult.Failed(navigation.ErrorMessage ?? "Replace failed.");

        var sheet = Workbook.GetSheet(match.Address.Sheet);
        if (sheet is null ||
            !FindReplaceService.TryCreateReplacementCommand(
                sheet,
                match,
                searchText,
                replaceText,
                matchCase,
                matchEntireCell,
                effectiveOptions.LookIn,
                replacementFormat,
                out var command,
                workbook: Workbook))
        {
            ClearLastFindTargets();
            return WorkbookReplaceResult.Replaced(
                0,
                navigation.SelectedRange,
                index + 1,
                matches.Count);
        }

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            command);
        if (!result.Success)
        {
            ClearLastFindTargets();
            return WorkbookReplaceResult.Failed(result.ErrorMessage ?? "Replace failed.");
        }

        _lastFindResult = null;
        _lastReplaceResult = match;
        ApplySuccessfulEditResult(result, match.Address);
        var replacedRange = new GridRange(match.Address, match.Address);
        return WorkbookReplaceResult.Replaced(1, replacedRange, index + 1, matches.Count);
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
        var nextTopRow = Offset(ActiveSheet.ViewTopRow ?? GetScrollableRowStart(), rowDelta, CellAddress.MaxRow);
        var nextLeftCol = Offset(ActiveSheet.ViewLeftCol ?? GetScrollableColumnStart(), colDelta, CellAddress.MaxCol);
        return SetViewportOrigin(nextTopRow, nextLeftCol);
    }

    public bool SetViewportOrigin(uint topRow, uint leftCol)
    {
        var normalizedTopRow = Math.Clamp(topRow, GetScrollableRowStart(), CellAddress.MaxRow);
        var normalizedLeftCol = Math.Clamp(leftCol, GetScrollableColumnStart(), CellAddress.MaxCol);
        var currentTopRow = ActiveSheet.ViewTopRow ?? GetScrollableRowStart();
        var currentLeftCol = ActiveSheet.ViewLeftCol ?? GetScrollableColumnStart();
        if (normalizedTopRow == currentTopRow && normalizedLeftCol == currentLeftCol)
            return false;

        ActiveSheet.ViewTopRow = normalizedTopRow;
        ActiveSheet.ViewLeftCol = normalizedLeftCol;
        RefreshViewport();
        return true;
    }

    /// <summary>
    /// True when the active sheet has a Window ▸ Split column boundary (<see cref="Sheet.SplitColumn"/>),
    /// i.e. there is a TopRight pane that can scroll independently of the main (BottomRight) pane.
    /// </summary>
    public bool HasIndependentSplitPaneTopRight => ActiveSheet.SplitColumn is not null;

    /// <summary>
    /// True when the active sheet has a Window ▸ Split row boundary (<see cref="Sheet.SplitRow"/>),
    /// i.e. there is a BottomLeft pane that can scroll independently of the main (BottomRight) pane.
    /// </summary>
    public bool HasIndependentSplitPaneBottomLeft => ActiveSheet.SplitRow is not null;

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
            : ActiveSheet.ViewLeftCol ?? GetScrollableColumnStart();

    /// <summary>Current first-visible row of the BottomLeft split-pane quadrant, defaulting to the main pane's when no independent offset has been recorded yet.</summary>
    public uint GetSplitPaneBottomLeftTopRow() =>
        _splitPaneViewportOffsets.TryGetValue(ActiveSheet.Id, out var offsets) && offsets.BottomLeftTopRow is { } topRow
            ? topRow
            : ActiveSheet.ViewTopRow ?? GetScrollableRowStart();

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
            new AddSheetCommand(WorkbookSheetNameGenerator.GenerateUniqueSheetName(Workbook)));
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

    /// <summary>Applies an explicit height (points) to every row in the selection, undoably.</summary>
    public WorkbookCellEditResult SetSelectedRowsHeight(double height) =>
        ExecuteSizingCommand(
            Ribbon.RowColumnSizingPlanner.CreateRowHeightCommand(ActiveSheet.Id, SelectedRange, height));

    /// <summary>Applies an explicit width (characters) to every column in the selection, undoably.</summary>
    public WorkbookCellEditResult SetSelectedColumnsWidth(double width) =>
        ExecuteSizingCommand(
            Ribbon.RowColumnSizingPlanner.CreateColumnWidthCommand(ActiveSheet.Id, SelectedRange, width));

    /// <summary>
    /// Sizes each selected row's height to its tallest cell content (content-based estimate via the
    /// shared AutoFitSizingService — character/line counts, not true glyph metrics). Returns a success
    /// result when there is nothing measurable (e.g. a whole-sheet selection with no used range).
    /// </summary>
    public WorkbookCellEditResult AutoFitSelectedRowHeight()
    {
        var plans = Ribbon.RowColumnSizingPlanner.PlanAutoFitRowHeights(
            ActiveSheet,
            SelectedRange,
            ActiveSheet.GetUsedRange(),
            GetAutoFitDisplayText,
            ActiveSheet.DefaultRowHeight);
        var command = Ribbon.RowColumnSizingPlanner.CreateAutoFitRowHeightCommand(ActiveSheet.Id, plans);
        return command is null ? SucceededWithoutEdit() : ExecuteSizingCommand(command);
    }

    /// <summary>
    /// Sizes each selected column's width to its widest cell content (content-based estimate via the
    /// shared AutoFitSizingService). Returns a success result when there is nothing measurable.
    /// </summary>
    public WorkbookCellEditResult AutoFitSelectedColumnWidth()
    {
        var plans = Ribbon.RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            ActiveSheet,
            SelectedRange,
            ActiveSheet.GetUsedRange(),
            GetAutoFitDisplayText,
            ActiveSheet.DefaultColumnWidth);
        var command = Ribbon.RowColumnSizingPlanner.CreateAutoFitColumnWidthCommand(ActiveSheet.Id, plans);
        return command is null ? SucceededWithoutEdit() : ExecuteSizingCommand(command);
    }

    /// <summary>
    /// Runs a row/column sizing command and restores the selection afterwards. The shared command
    /// pipeline collapses the selection to the active cell on success (it is built for cell edits),
    /// but a dimension change must leave the resized rows/columns selected (Excel parity) so a
    /// follow-up resize targets the same span.
    /// </summary>
    private WorkbookCellEditResult ExecuteSizingCommand(IWorkbookCommand command)
    {
        var preservedRange = SelectedRange;
        var result = ExecuteReviewCommand(command);
        if (result.Success)
            SelectRange(preservedRange);

        return result;
    }

    private AutoFitCellText? GetAutoFitDisplayText(uint row, uint col)
    {
        if (ActiveSheet.GetCell(row, col) is not { } cell)
            return null;

        if (ActiveSheet.ShowFormulas && cell.FormulaText is not null)
            return new AutoFitCellText("=" + cell.FormulaText);

        var style = Workbook.GetStyle(cell.StyleId);
        var text = FreeX.Core.Formula.NumberFormatter.Format(cell.Value, style.NumberFormat);
        return new AutoFitCellText(text, style.WrapText);
    }

    private static WorkbookCellEditResult SucceededWithoutEdit() =>
        new(true, null, [], RecalcReport: null);

    public WorkbookCellEditResult SetShowFormulas(bool showFormulas)
    {
        if (ActiveSheet.ShowFormulas == showFormulas)
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
        return result;
    }

    public WorkbookCellEditResult SetShowGridlines(bool showGridlines)
    {
        if (ActiveSheet.ShowGridlines == showGridlines)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        return SetWorksheetViewOptions(
            showGridlines,
            ActiveSheet.ShowHeadings,
            ActiveSheet.ShowRulers);
    }

    public WorkbookCellEditResult SetShowHeadings(bool showHeadings)
    {
        if (ActiveSheet.ShowHeadings == showHeadings)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                [],
                RecalcReport: null);
        }

        return SetWorksheetViewOptions(
            ActiveSheet.ShowGridlines,
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
            ActiveSheet.ShowGridlines,
            ActiveSheet.ShowHeadings,
            showRulers);
    }

    public WorkbookCellEditResult SetWorksheetViewMode(WorksheetViewMode viewMode)
    {
        if (ActiveSheet.ViewMode == viewMode)
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
        return result;
    }

    public WorkbookCellEditResult SetZoomPercent(int zoomPercent)
    {
        zoomPercent = Math.Clamp(
            zoomPercent,
            SetWorksheetZoomCommand.MinZoomPercent,
            SetWorksheetZoomCommand.MaxZoomPercent);
        if (ActiveSheet.ZoomPercent == zoomPercent)
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
        if (!address.Sheet.Equals(ActiveSheet.Id))
            throw new InvalidOperationException("Cell edit address must belong to the active sheet.");

        var cell = CellEntryParser.CreateCell(text, address, useR1C1ReferenceStyle);

        // Enforce Stop-style data validation rules the same way the WPF host's
        // TryCreateCellFromEntryText does, so a Stop-alert DV rule actually blocks bad entries
        // on the Avalonia shell instead of being purely decorative. Warning/Information styles
        // (DataValidationInvalidEntryAction.AskToContinue) need a user-facing prompt with
        // Yes/No/Cancel semantics that has no seam in this host-agnostic session yet, so those
        // are intentionally left to pass through unchanged for now -- only Block is enforced here.
        var blockMessage = TryGetBlockingValidationMessage(cell, address);
        if (blockMessage != null)
            return new WorkbookCellEditResult(false, blockMessage, [], RecalcReport: null);

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateEditCellsCommand([(address, cell)]));

        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, address);
        GrowRowHeightForAlreadyWrappedCellIfNeeded(address);
        return result;
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
            GetAutoFitDisplayText,
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

    /// <summary>
    /// Returns the violation message for the first applicable data-validation rule that
    /// blocks <paramref name="cell"/> at <paramref name="address"/> (a Stop-alert rule with
    /// <c>ShowErrorMessage</c> set), or null if no rule blocks the entry.
    /// </summary>
    private string? TryGetBlockingValidationMessage(Cell cell, CellAddress address)
    {
        var sheet = Workbook.GetSheet(address.Sheet);
        if (sheet == null)
            return null;

        var value = cell.HasFormula
            ? new FreeX.Core.Formula.FormulaEvaluator().Evaluate(cell.FormulaText!, sheet, Workbook, currentCell: address)
            : cell.Value;

        foreach (var dv in DataValidationService.GetApplicable(sheet, address))
        {
            var msg = DataValidationService.Validate(dv, value, sheet, address, Workbook);
            if (msg == null)
                continue;

            if (DataValidationService.GetInvalidEntryAction(dv) == DataValidationInvalidEntryAction.Block)
                return msg;

            // AskToContinue (Warning/Information) is not enforced yet -- only the first
            // violated rule matters for Excel's "first rule wins" behavior, so stop here.
            break;
        }

        return null;
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
                return WorkbookClipboardTextResult.Failed(CreateMultiRangeClipboardError("Copy"));

            var blockText = SerializeMultiRangeCopy(layout);
            // The combined block is copied as concatenated values through the text path; clear any
            // FreeX-owned single-range clipboard so paste does not reuse a stale payload whose
            // formula/format rebasing would not match the gap-collapsed block.
            _internalClipboard = null;
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
        _internalClipboard = CaptureInternalClipboard(SelectedRange, text, isCut: false, fullRangeViewport);
        return WorkbookClipboardTextResult.Succeeded(text, fullRangeViewport);
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
        if (TryCreateMultiRangeClipboardTextResult("Cut", out var result))
            return result;

        // Same rationale as TryCopySelectedRangeText: use a full-range viewport, not the on-screen
        // Viewport, so cutting a selection taller/wider than the visible area does not blank out the
        // off-screen part of the clipboard payload (R14-clipboard-formats-deep-1).
        var fullRangeViewport = BuildFullRangeViewportForClipboard(SelectedRange);
        var text = ClipboardSerializer.Serialize(fullRangeViewport, SelectedRange);
        _internalClipboard = CaptureInternalClipboard(SelectedRange, text, isCut: true, fullRangeViewport);
        return WorkbookClipboardTextResult.Succeeded(text, fullRangeViewport);
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
        string? html = null)
    {
        // Paste Special > Text / Unicode Text (preserveText: true — Excel semantics: paste the
        // clipboard's plain text only) must always go through the external-clipboard plain-text path
        // below, even right after an in-app copy where the OS clipboard text still matches the
        // internal clipboard's text. Otherwise the internal-clipboard branch below wins (its
        // text-equality check can't distinguish "explicitly asked for text" from "clipboard
        // unchanged") and silently performs a full formatted internal paste instead (review P44),
        // mirroring the WPF host's ExecutePaste externalTextAsText bypass.
        if (!preserveText && _internalClipboard is { } internalClipboard)
        {
            var pastePlan = ClipboardPastePlanner.PlanPaste(internalClipboard.Text, text, clipboardReadFailed);
            if (pastePlan == ClipboardPastePlan.ReadFailed)
            {
                // A transient OS-clipboard read failure must never be silently reinterpreted as
                // "clipboard unchanged" — that would risk pasting a stale internal copy over content
                // the user just copied elsewhere. Surface it instead of guessing, mirroring the WPF
                // host's ClipboardPastePlanner.PlanPaste guard.
                return new WorkbookCellEditResult(
                    false,
                    "The clipboard is busy. Try pasting again.",
                    [],
                    RecalcReport: null);
            }

            if (pastePlan == ClipboardPastePlan.UseInternalClipboard)
                return PasteInternalClipboardAtActiveCell(internalClipboard, PasteCellsMode.All, default);

            _internalClipboard = null;
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
                "The clipboard is busy. Try pasting again.",
                [],
                RecalcReport: null);
        }

        if (_internalClipboard is not { } internalClipboard)
        {
            // No FreeX-internal clipboard at all — fall back to an external-text Paste Special
            // instead of unconditionally rejecting, matching Excel (Paste Special on a copied
            // external TSV/CSV block still applies Transpose/Skip Blanks/Operation) and the WPF
            // host's PasteSpecialBtn_Click, which only routes to PasteSpecialAction.ExternalText
            // when _internalClipboard is null at click time (review P46 — this shell used to reject
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
            // the WPF host's ExecutePaste (which treats ClipboardPastePlanner.PlanPaste's
            // UseExternalClipboardText result as "clipboard changed externally" and falls through to
            // CreateExternalTextPasteCommand with the selected options), clear the stale internal
            // clipboard and fall back to an external-text Paste Special instead of hard-rejecting, so
            // the live external text still gets the chosen Transpose/Skip Blanks/Operation options
            // applied (review P46 corollary — the null-internal-clipboard branch above already does
            // this; this branch used to unconditionally reject instead).
            _internalClipboard = null;
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
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
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
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
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
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
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
        var commands = new List<IWorkbookCommand>();
        foreach (var sheetId in CurrentGroupedEditSheetIds())
        {
            var sheet = Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            var matches = sheet.DataValidations
                .Where(candidate => HasSameDataValidationSettings(candidate, existingRule))
                .Select(candidate => (IWorkbookCommand)new SetDataValidationCommand(
                    sheetId,
                    CloneDataValidationForRanges(rule, candidate.AppliesTo, candidate.AdditionalRanges)))
                .ToList();

            if (matches.Count == 0)
            {
                matches.Add(new SetDataValidationCommand(
                    sheetId,
                    CloneDataValidationForRanges(rule, RemapRangeToSheet(SelectedRange, sheetId), [])));
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
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
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
        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
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
        if (!string.IsNullOrWhiteSpace(text))
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

        _internalClipboard = null;
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
    /// Mirrors the WPF host's <c>MainWindow.ClipboardCommands.TryGetClipboardHtml</c> /
    /// <c>TryParseHtmlClipboardTableRows</c> preference (R39-io-external-clipboard-2-3), which the
    /// Avalonia shell's clipboard paste never received (R57-services-clipboard-formats-5-1).
    /// </summary>
    public WorkbookCellEditResult PasteExternalTextAtActiveCell(
        string text, bool preserveText, PasteSpecialOptions options, string? html)
    {
        ArgumentNullException.ThrowIfNull(text);

        var destination = ActiveCell;
        var destinationRange = GetSinglePasteDestinationRange(destination);
        IReadOnlyList<IReadOnlyList<string>> rows =
            TryParseHtmlClipboardTableRows(html) is { Count: > 0 } htmlRows
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

    /// <summary>
    /// Parses a CF_HTML clipboard payload's first &lt;table&gt; into rows of plain cell text (no
    /// per-cell styling — a lighter-weight recovery than <see cref="FreeX.Core.IO"/>'s whole-file HTML
    /// import), or <c>null</c> when <paramref name="html"/> is empty or contains no table markup. Only
    /// the &lt;tr&gt;/&lt;td&gt;/&lt;th&gt; row/column boundaries are recovered here — this is enough to
    /// stop a multi-line source cell's embedded line break from being misread as a row boundary the way
    /// the plain-text tab/newline splitter does (R57-services-clipboard-formats-5-1), mirroring the WPF
    /// host's <c>MainWindow.ClipboardCommands.TryParseHtmlClipboardTableRows</c>.
    /// </summary>
    private static List<IReadOnlyList<string>>? TryParseHtmlClipboardTableRows(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var fragment = ExtractHtmlClipboardFragment(html);
        var tableInner = ExtractFirstHtmlTableInner(fragment);
        if (tableInner is null)
            return null;

        var rows = new List<List<string>>();
        foreach (var rowInner in EnumerateHtmlElements(tableInner, "tr"))
        {
            var cells = new List<string>();
            foreach (var cellInner in EnumerateHtmlCells(rowInner))
                cells.Add(DecodeHtmlCellText(cellInner));

            if (cells.Count > 0)
                rows.Add(cells);
        }

        return rows.Count > 0 ? rows.Cast<IReadOnlyList<string>>().ToList() : null;
    }

    /// <summary>CF_HTML wraps the real markup between StartFragment/EndFragment comments after a small
    /// header; falls back to the whole payload if the markers are absent (some non-Excel producers, e.g.
    /// a browser's own copy, omit them).</summary>
    private static string ExtractHtmlClipboardFragment(string html)
    {
        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var start = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        var end = html.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
        return start >= 0 && end > start
            ? html[(start + startMarker.Length)..end]
            : html;
    }

    private static string? ExtractFirstHtmlTableInner(string html)
    {
        int i = 0;
        while (i < html.Length)
        {
            int lt = html.IndexOf('<', i);
            if (lt < 0)
                return null;
            if (string.Equals(HtmlTagNameAt(html, lt), "table", StringComparison.OrdinalIgnoreCase))
            {
                int tagEnd = html.IndexOf('>', lt);
                if (tagEnd < 0)
                    return null;
                int closeStart = FindMatchingHtmlClose(html, tagEnd + 1, "table");
                return closeStart < 0 ? html[(tagEnd + 1)..] : html[(tagEnd + 1)..closeStart];
            }
            i = lt + 1;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateHtmlElements(string html, string tag)
    {
        int i = 0;
        while (i < html.Length)
        {
            int lt = html.IndexOf('<', i);
            if (lt < 0)
                break;
            if (string.Equals(HtmlTagNameAt(html, lt), tag, StringComparison.OrdinalIgnoreCase))
            {
                int tagEnd = html.IndexOf('>', lt);
                if (tagEnd < 0)
                    break;
                int closeStart = FindMatchingHtmlClose(html, tagEnd + 1, tag);
                string inner = closeStart < 0 ? html[(tagEnd + 1)..] : html[(tagEnd + 1)..closeStart];
                yield return inner;
                i = closeStart < 0 ? html.Length : SkipHtmlClosingTag(html, closeStart);
            }
            else
            {
                i = lt + 1;
            }
        }
    }

    private static IEnumerable<string> EnumerateHtmlCells(string rowInner)
    {
        int i = 0;
        while (i < rowInner.Length)
        {
            int lt = rowInner.IndexOf('<', i);
            if (lt < 0)
                break;
            var name = HtmlTagNameAt(rowInner, lt);
            if (name is "td" or "th")
            {
                int tagEnd = rowInner.IndexOf('>', lt);
                if (tagEnd < 0)
                    break;
                int closeStart = FindMatchingHtmlClose(rowInner, tagEnd + 1, name);
                string inner = closeStart < 0 ? rowInner[(tagEnd + 1)..] : rowInner[(tagEnd + 1)..closeStart];
                yield return inner;
                i = closeStart < 0 ? rowInner.Length : SkipHtmlClosingTag(rowInner, closeStart);
            }
            else
            {
                i = lt + 1;
            }
        }
    }

    /// <summary>The element name at a '&lt;' position, or null if it isn't a start/end tag name.</summary>
    private static string? HtmlTagNameAt(string s, int ltIndex)
    {
        int i = ltIndex + 1;
        if (i < s.Length && s[i] == '/')
            i++;
        int start = i;
        while (i < s.Length && char.IsLetterOrDigit(s[i]))
            i++;
        return i > start ? s[start..i].ToLowerInvariant() : null;
    }

    /// <summary>Find the index of the matching &lt;/tag&gt;, honoring nesting. -1 if none.</summary>
    private static int FindMatchingHtmlClose(string s, int from, string tag)
    {
        int depth = 0;
        int i = from;
        while (i < s.Length)
        {
            int lt = s.IndexOf('<', i);
            if (lt < 0)
                return -1;
            bool isClose = lt + 1 < s.Length && s[lt + 1] == '/';
            var name = HtmlTagNameAt(s, lt);
            if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase))
            {
                if (isClose)
                {
                    if (depth == 0)
                        return lt;
                    depth--;
                }
                else if (!IsHtmlSelfClosing(s, lt))
                {
                    depth++;
                }
            }
            i = lt + 1;
        }
        return -1;
    }

    private static bool IsHtmlSelfClosing(string s, int lt)
    {
        int gt = s.IndexOf('>', lt);
        return gt > lt && s[gt - 1] == '/';
    }

    private static int SkipHtmlClosingTag(string s, int closeStart)
    {
        int gt = s.IndexOf('>', closeStart);
        return gt < 0 ? s.Length : gt + 1;
    }

    /// <summary>Strips a &lt;td&gt;/&lt;th&gt; cell's inner HTML down to its plain text, turning
    /// &lt;br&gt; into a newline (so a genuinely multi-line cell still round-trips as one pasted cell,
    /// matching Excel's own within-cell wrap semantics) and HTML-decoding entities.</summary>
    private static string DecodeHtmlCellText(string innerHtml)
    {
        var sb = new System.Text.StringBuilder(innerHtml.Length);
        int i = 0;
        while (i < innerHtml.Length)
        {
            char c = innerHtml[i];
            if (c == '<')
            {
                var name = HtmlTagNameAt(innerHtml, i);
                int gt = innerHtml.IndexOf('>', i);
                if (gt < 0)
                    break;
                if (name is "br")
                    sb.Append('\n');
                i = gt + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return System.Net.WebUtility.HtmlDecode(sb.ToString()).Trim();
    }

    public WorkbookCellEditResult ClearSelectedRangeContents()
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                range,
                "Clear Contents",
                static (sheetId, sheetRange) => new ClearContentsCommand(sheetId, sheetRange)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult ClearSelectedRangeAll()
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateClearAllCommand(range));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
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

    public WorkbookCellEditResult ClearSelectedRangeFormats() =>
        ApplySelectedRangeStyle(CellStyleDiffPlanner.ClearFormatsDiff());

    public WorkbookCellEditResult ClearSelectedRangeComments()
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                range,
                "Clear Comments and Notes",
                static (sheetId, sheetRange) => new ClearCommentsCommand(sheetId, sheetRange)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
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
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                range,
                "Clear Hyperlinks",
                static (sheetId, sheetRange) => new ClearHyperlinksCommand(sheetId, sheetRange)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    /// <summary>
    /// Excel's Home&gt;Clear&gt;Remove Hyperlinks (and the equivalent right-click Clear submenu entry)
    /// strips the cell's hyperlink AND its blue/underline formatting -- unlike right-click's top-level
    /// "Remove Hyperlink" item (<see cref="ClearSelectedRangeHyperlinks"/>), which keeps the formatting.
    /// </summary>
    public WorkbookCellEditResult RemoveSelectedRangeHyperlinks()
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                range,
                "Remove Hyperlinks",
                static (sheetId, sheetRange) => new RemoveHyperlinksCommand(sheetId, sheetRange)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
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

    public bool CanFillSelectedRange(FillCellsDirection direction) =>
        direction switch
        {
            FillCellsDirection.Down or FillCellsDirection.Up => SelectedRange.RowCount > 1,
            FillCellsDirection.Right or FillCellsDirection.Left => SelectedRange.ColCount > 1,
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

    public WorkbookCellEditResult SortSelectedRange(bool ascending)
    {
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
                (sheetId, sheetRange) => new SortCommand(sheetId, sheetRange, sortByColOffset: 0, ascending)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult SortSelectedRange(IReadOnlyList<CoreSortKey> sortKeys, SortOptions options, bool hasHeaders)
    {
        ArgumentNullException.ThrowIfNull(sortKeys);
        ArgumentNullException.ThrowIfNull(options);

        if (!CanSortSelectedRange)
        {
            return new WorkbookCellEditResult(
                false,
                "Select at least two rows to sort.",
                [],
                RecalcReport: null);
        }

        var range = SelectedRange;
        var sortRange = options.LeftToRight
            ? range
            : SortDialogPlanner.ExcludeHeaderRow(range, hasHeaders);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                sortRange,
                "Sort",
                (sheetId, sheetRange) => new SortCommand(sheetId, sheetRange, sortKeys, options)));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult FillSelectedRange(FillCellsDirection direction)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateRangeCommand(
                range,
                GetFillCellsTitle(direction),
                (sheetId, sheetRange) => new FillCellsCommand(sheetId, sheetRange, direction)));
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
        var result = _cellEditService.ExecuteRepeatableEditCommand(
            Workbook,
            () => CreateWrapTextCommand(enabled));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, SelectedRange);
        return result;
    }

    private IWorkbookCommand CreateWrapTextCommand(bool enabled)
    {
        var range = SelectedRange;
        var commands = new List<IWorkbookCommand> { CreateApplyStyleCommand(range, new StyleDiff(WrapText: enabled)) };
        if (enabled)
            commands.AddRange(CreateWrapTextGrowthCommands(range));

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
        if (GetAutoFitDisplayText(row, col) is not { } cellText)
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
        var commands = new List<IWorkbookCommand>();
        var remainingDiff = diff.FontSize is null ? diff : diff with { FontSize = null };

        if (HasStyleDiffChanges(remainingDiff))
            commands.Add(CreateApplyStyleCommand(range, remainingDiff));

        if (borderPreset is { } preset && HasBorderPresetChanges(range, preset, borderStyle, borderColor))
            commands.Add(CreateBorderPresetCommand(range, preset, borderStyle, borderColor));

        if (diff.FontSize is { } fontSize)
            commands.Add(CreateSetFontSizeCommand(range, fontSize, GetFittingRowHeight(fontSize)));

        if (mergeCells is { } shouldMerge)
            commands.AddRange(CreateFormatCellsMergeCommands(range, shouldMerge, mergeContentResolution));

        if (commands.Count == 0)
            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new CompositeWorkbookCommand("Format Cells", commands));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
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

    public WorkbookCellEditResult MergeAndCenterSelectedRange(
        MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateMergeAndCenterCommand(range, contentResolution));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult UnmergeSelectedRange()
    {
        var range = SelectedRange;
        var commands = CreateUnmergeCommands(range);
        if (commands.Count == 0)
            return new WorkbookCellEditResult(true, null, [], RecalcReport: null);

        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            ToCommand("Unmerge Cells", commands));
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
        IsDirty = false;
        CurrentFilePath = path;
        CurrentFileAccessIdentity = resolvedIdentity;
        CurrentXlsxFeatureReport = null;
        Workbook.Name = Path.GetFileName(path);
        RecordUndoSavePoint();
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
        {
            IsDirty = false;
            RecordUndoSavePoint();
        }

        if (plan.ApplyFileContext && plan.FileContext is { } fileContext)
        {
            CurrentFilePath = fileContext.Path;
            CurrentFileAccessIdentity = fileContext.FileAccessIdentity;
            CurrentXlsxFeatureReport = null;
            Workbook.Name = fileContext.DisplayName;
        }

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
        RefreshViewport();
    }

    /// <summary>Forces a recalculation of the active sheet's formulas (Shift+F9 / Calculate Sheet) and refreshes the view.</summary>
    public void RecalculateActiveSheet()
    {
        _cellEditService.RecalculateSheet(Workbook, ActiveSheet.Id);
        RefreshViewport();
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
            // ApplySuccessfulRangeEditResult's forward-operation behavior.
            var boundingRange = BoundingRangeOrDefault(result.AffectedCells, ActiveCell);
            if (ActiveSheet.Id.Equals(boundingRange.Start.Sheet))
            {
                ApplySuccessfulRangeEditResult(result, boundingRange);
            }
            else
            {
                ApplySuccessfulEditResult(result, ActiveCell);
            }
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
        InternalClipboard clipboard,
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
        InternalClipboard clipboard,
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
        InternalClipboard clipboard,
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
        InternalClipboard clipboard,
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
            var sheetRule = CloneDataValidationForRanges(rule, sheetRanges[0], sheetRanges.Skip(1));
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
        // factory re-reads SelectedRange each time it runs rather than closing over the range
        // captured here, since a repeat invocation targets whatever is selected at that time.
        var result = _cellEditService.ExecuteRepeatableEditCommand(
            Workbook,
            () => CreateApplyStyleCommand(SelectedRange, diff));
        if (!result.Success)
            return result;

        ApplySuccessfulRangeEditResult(result, SelectedRange);
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
        return existing is null || !DataValidationRulesEqual(existing, rule);
    }

    private static bool HasSameDataValidationSettings(DataValidation left, DataValidation right) =>
        left.Type == right.Type &&
        left.Operator == right.Operator &&
        string.Equals(left.Formula1, right.Formula1, StringComparison.Ordinal) &&
        string.Equals(left.Formula2, right.Formula2, StringComparison.Ordinal) &&
        left.AllowBlank == right.AllowBlank &&
        left.ShowDropdown == right.ShowDropdown &&
        left.AlertStyle == right.AlertStyle &&
        left.ShowInputMessage == right.ShowInputMessage &&
        left.ShowErrorMessage == right.ShowErrorMessage &&
        string.Equals(left.ErrorTitle, right.ErrorTitle, StringComparison.Ordinal) &&
        string.Equals(left.ErrorMessage, right.ErrorMessage, StringComparison.Ordinal) &&
        string.Equals(left.PromptTitle, right.PromptTitle, StringComparison.Ordinal) &&
        string.Equals(left.PromptMessage, right.PromptMessage, StringComparison.Ordinal);

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

    private static DataValidation CloneDataValidationForRanges(
        DataValidation source,
        GridRange appliesTo,
        IEnumerable<GridRange> additionalRanges)
    {
        var clone = new DataValidation
        {
            AppliesTo = appliesTo,
            Type = source.Type,
            Operator = source.Operator,
            Formula1 = source.Formula1,
            Formula2 = source.Formula2,
            AllowBlank = source.AllowBlank,
            ShowDropdown = source.ShowDropdown,
            AlertStyle = source.AlertStyle,
            ShowInputMessage = source.ShowInputMessage,
            ShowErrorMessage = source.ShowErrorMessage,
            ErrorTitle = source.ErrorTitle,
            ErrorMessage = source.ErrorMessage,
            PromptTitle = source.PromptTitle,
            PromptMessage = source.PromptMessage,
            NativeAttributes = source.NativeAttributes,
            NativeChildXmls = source.NativeChildXmls,
            NativeContainerAttributes = source.NativeContainerAttributes,
            NativeContainerChildXmls = source.NativeContainerChildXmls
        };
        clone.AdditionalRanges.AddRange(additionalRanges);
        return clone;
    }

    private static bool DataValidationRulesEqual(DataValidation left, DataValidation right) =>
        left.AppliesTo == right.AppliesTo &&
        left.AdditionalRanges.SequenceEqual(right.AdditionalRanges) &&
        left.Type == right.Type &&
        left.Operator == right.Operator &&
        string.Equals(left.Formula1, right.Formula1, StringComparison.Ordinal) &&
        string.Equals(left.Formula2, right.Formula2, StringComparison.Ordinal) &&
        left.AllowBlank == right.AllowBlank &&
        left.ShowDropdown == right.ShowDropdown &&
        left.AlertStyle == right.AlertStyle &&
        left.ShowInputMessage == right.ShowInputMessage &&
        left.ShowErrorMessage == right.ShowErrorMessage &&
        string.Equals(left.ErrorTitle, right.ErrorTitle, StringComparison.Ordinal) &&
        string.Equals(left.ErrorMessage, right.ErrorMessage, StringComparison.Ordinal) &&
        string.Equals(left.PromptTitle, right.PromptTitle, StringComparison.Ordinal) &&
        string.Equals(left.PromptMessage, right.PromptMessage, StringComparison.Ordinal) &&
        DictionaryEquals(left.NativeAttributes, right.NativeAttributes) &&
        SequenceEquals(left.NativeChildXmls, right.NativeChildXmls) &&
        DictionaryEquals(left.NativeContainerAttributes, right.NativeContainerAttributes) &&
        SequenceEquals(left.NativeContainerChildXmls, right.NativeContainerChildXmls);

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var rightValue) ||
                !string.Equals(value, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequenceEquals(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static double GetFittingRowHeight(double fontSize) =>
        Math.Min(MaximumRowHeight, FontSizePlanner.EstimateFittingRowHeight(fontSize));

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

    private static string GetFillCellsTitle(FillCellsDirection direction) =>
        direction switch
        {
            FillCellsDirection.Down => "Fill Down",
            FillCellsDirection.Right => "Fill Right",
            FillCellsDirection.Up => "Fill Up",
            FillCellsDirection.Left => "Fill Left",
            _ => "Fill"
        };

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
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void MarkDirty()
    {
        IsDirty = true;
        DirtyGeneration++;
    }

    /// <summary>Captures the undo stack's current depth/version as the "clean" save point.</summary>
    private void RecordUndoSavePoint()
    {
        _savedUndoDepth = _cellEditService.GetUndoStackDepth(Workbook.Id);
        _savedUndoStackVersion = _cellEditService.GetUndoStackVersion(Workbook.Id);
    }

    /// <summary>
    /// If the undo stack has returned to the recorded save point (matching both depth and, when
    /// recorded, version), clears <see cref="IsDirty"/> and returns <c>true</c>. Called after
    /// Undo/Redo — which unconditionally routes through <see cref="MarkDirty"/> via
    /// <see cref="ApplySuccessfulHistoryResult"/> — to restore the clean state the WPF host already
    /// restores via <c>WorkbookDocumentState.TryMarkCleanIfAtSavePoint</c>. Leaves
    /// <see cref="IsDirty"/> untouched (i.e. still <c>true</c>) when no save point was recorded or
    /// the stack has not returned to it.
    /// </summary>
    private bool TryMarkCleanIfAtSavePoint()
    {
        if (_savedUndoDepth < 0)
            return false;

        var currentUndoDepth = _cellEditService.GetUndoStackDepth(Workbook.Id);
        if (currentUndoDepth != _savedUndoDepth)
            return false;

        if (_savedUndoStackVersion is { } savedVersion &&
            _cellEditService.GetUndoStackVersion(Workbook.Id) != savedVersion)
            return false;

        IsDirty = false;
        return true;
    }

    /// <summary>
    /// Forces this session's dirty/modified state after a crash-recovery snapshot has been
    /// loaded into it, so the host shows the modified indicator and prompts the user to save
    /// rather than silently discarding the recovered data. Mirrors the WPF host's
    /// <c>MarkWorkbookDirtyForRecovery</c>/<c>MarkWorkbookDirty</c> path, which reuses this same
    /// document-state dirty-marking call for edits.
    /// </summary>
    public void MarkDirtyForRecovery() => MarkDirty();

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

    private int GetNextFindResultIndex(
        IReadOnlyList<FindResult> results,
        FindSearchOrder searchOrder,
        bool sameSearch)
    {
        if (sameSearch && _lastFindResult is { } lastResult)
        {
            var lastIndex = FindResultIndex(results, lastResult);
            if (lastIndex >= 0)
                return (lastIndex + 1) % results.Count;
        }

        return FindFirstResultAfterActiveCell(results, searchOrder);
    }

    private int GetReplaceTargetIndex(
        IReadOnlyList<FindResult> results,
        FindSearchOrder searchOrder,
        bool sameSearch)
    {
        if (sameSearch &&
            _lastFindResult is { } lastFindResult &&
            ActiveCell.Equals(lastFindResult.Address))
        {
            var lastIndex = FindResultIndex(results, lastFindResult);
            if (lastIndex >= 0)
                return lastIndex;
        }

        if (sameSearch &&
            _lastReplaceResult is { } lastReplaceResult &&
            ActiveCell.Equals(lastReplaceResult.Address))
        {
            var nextSameCellIndex = FindNextResultIndexAtSameAddress(results, lastReplaceResult);
            if (nextSameCellIndex >= 0)
                return nextSameCellIndex;
        }

        return FindFirstResultAfterActiveCell(results, searchOrder);
    }

    private bool HasLastFindTargetAtActiveCell() =>
        (_lastFindResult is { } lastFindResult && ActiveCell.Equals(lastFindResult.Address)) ||
        (_lastReplaceResult is { } lastReplaceResult && ActiveCell.Equals(lastReplaceResult.Address));

    private FindOptions CreateActiveSheetFindOptions(FindLookIn lookIn) =>
        new(
            Within: FindWithin.Sheet,
            CurrentSheetId: ActiveSheet.Id,
            SearchOrder: FindSearchOrder.ByRows,
            LookIn: lookIn);

    private FindOptions ResolveFindOptions(FindOptions? options, FindLookIn defaultLookIn)
    {
        var effectiveOptions = options ?? CreateActiveSheetFindOptions(defaultLookIn);
        if (effectiveOptions.Within == FindWithin.Sheet && effectiveOptions.CurrentSheetId is null)
            effectiveOptions = effectiveOptions with { CurrentSheetId = ActiveSheet.Id };
        return effectiveOptions;
    }

    private void RememberFindSearch(
        string searchText,
        FindOptions options,
        bool matchCase,
        bool matchEntireCell)
    {
        _lastFindText = searchText;
        _lastFindOptions = options;
        _lastFindMatchCase = matchCase;
        _lastFindMatchEntireCell = matchEntireCell;
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

    private int FindFirstResultAfterActiveCell(IReadOnlyList<FindResult> results, FindSearchOrder searchOrder)
    {
        for (var index = 0; index < results.Count; index++)
        {
            if (CompareFindOrder(results[index].Address, ActiveCell, searchOrder) > 0)
                return index;
        }

        return 0;
    }

    private static int FindResultIndex(IReadOnlyList<FindResult> results, FindResult result)
    {
        for (var index = 0; index < results.Count; index++)
        {
            if (IsSameFindTarget(results[index], result))
                return index;
        }

        return -1;
    }

    private static int FindNextResultIndexAtSameAddress(IReadOnlyList<FindResult> results, FindResult previous)
    {
        var previousIndex = FindResultIndex(results, previous);
        if (previousIndex >= 0)
            return (previousIndex + 1) % results.Count;

        for (var index = 0; index < results.Count; index++)
        {
            if (results[index].Address.Equals(previous.Address) &&
                CompareFindTargetOrder(results[index], previous) > 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSameFindTarget(FindResult left, FindResult right) =>
        left.Address.Equals(right.Address) &&
        left.Target == right.Target &&
        left.ReplyIndex == right.ReplyIndex;

    private static int CompareFindTargetOrder(FindResult left, FindResult right)
    {
        var targetComparison = GetFindTargetOrder(left).CompareTo(GetFindTargetOrder(right));
        return targetComparison != 0
            ? targetComparison
            : Nullable.Compare(left.ReplyIndex, right.ReplyIndex);
    }

    private static int GetFindTargetOrder(FindResult result) =>
        result.Target switch
        {
            FindResultTarget.ThreadedComment => 0,
            FindResultTarget.ThreadedCommentReply => 1 + Math.Max(0, result.ReplyIndex ?? 0),
            _ => 0
        };

    private void ClearLastFindTargets()
    {
        _lastFindResult = null;
        _lastReplaceResult = null;
    }

    private int CompareFindOrder(CellAddress left, CellAddress right, FindSearchOrder searchOrder)
    {
        var leftSheetIndex = FindSheetIndex(left.Sheet);
        var rightSheetIndex = FindSheetIndex(right.Sheet);
        var sheetComparison = leftSheetIndex.CompareTo(rightSheetIndex);
        if (sheetComparison != 0)
            return sheetComparison;

        if (searchOrder == FindSearchOrder.ByColumns)
        {
            var colComparison = left.Col.CompareTo(right.Col);
            return colComparison != 0 ? colComparison : left.Row.CompareTo(right.Row);
        }

        var rowComparison = left.Row.CompareTo(right.Row);
        return rowComparison != 0 ? rowComparison : left.Col.CompareTo(right.Col);
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

    private WorkbookCellEditResult SetFreezePanes(uint frozenRows, uint frozenCols)
    {
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            new SetFreezePanesCommand(ActiveSheet.Id, frozenRows, frozenCols));
        if (!result.Success)
            return result;

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
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
        InternalClipboard clipboard,
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
            _internalClipboard = null;
        return result;
    }

    private WorkbookCellEditResult PasteInternalClipboardToSelectedRanges(
        InternalClipboard clipboard,
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

    private InternalClipboard CaptureInternalClipboard(GridRange range, string text, bool isCut, ViewportModel viewport)
    {
        var sheet = Workbook.GetSheet(range.Start.Sheet);
        var cells = new List<(CellAddress Source, Cell Cell)>();
        var pictureCells = CapturePictureCells(range, sheet, viewport);
        foreach (var address in range.AllCells())
        {
            var cell = sheet?.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance);
            cells.Add((address, cell));
        }

        return new InternalClipboard(range, cells, pictureCells, text, isCut);
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

    private bool TryCreateMultiRangeClipboardTextResult(
        string operation,
        out WorkbookClipboardTextResult result)
    {
        if (SelectedRanges.Count <= 1)
        {
            result = WorkbookClipboardTextResult.Succeeded(string.Empty);
            return false;
        }

        result = WorkbookClipboardTextResult.Failed(CreateMultiRangeClipboardError(operation));
        return true;
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
        InternalClipboard clipboard,
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
        InternalClipboard clipboard,
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

    private static bool ShouldFillSelectedDestinationRange(bool isCut, PasteSpecialOptions options) =>
        !isCut &&
        // An arithmetic Operation (Add/Subtract/Multiply/Divide) must still tile across a larger
        // selected destination just like a plain paste — Excel applies the operation cell-by-cell
        // to every destination cell, tiling the (possibly 1-cell) clipboard source across the whole
        // selection, not just the anchor cell (R16-paste-special-matrix-1).
        options.ContentKind != PasteSpecialContentKind.AllMergingConditionalFormats;

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

    private void EnsureActiveCellVisible()
    {
        var changed = false;
        if (TryGetScrollableRowRange(out var firstRow, out var lastRow) &&
            !IsFrozenRow(ActiveCell.Row) &&
            (ActiveCell.Row < firstRow || ActiveCell.Row > lastRow))
        {
            ActiveSheet.ViewTopRow = CalculateScrollOrigin(
                ActiveCell.Row,
                firstRow,
                lastRow,
                ActiveSheet.ViewTopRow ?? GetScrollableRowStart(),
                CellAddress.MaxRow);
            changed = true;
        }

        if (TryGetScrollableColumnRange(out var firstCol, out var lastCol) &&
            !IsFrozenColumn(ActiveCell.Col) &&
            (ActiveCell.Col < firstCol || ActiveCell.Col > lastCol))
        {
            ActiveSheet.ViewLeftCol = CalculateScrollOrigin(
                ActiveCell.Col,
                firstCol,
                lastCol,
                ActiveSheet.ViewLeftCol ?? GetScrollableColumnStart(),
                CellAddress.MaxCol);
            changed = true;
        }

        if (changed)
            RefreshViewport();
    }

    private bool TryGetScrollableRowRange(out uint firstRow, out uint lastRow)
    {
        var frozenRows = ActiveSheet.FrozenRows;
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
        var frozenCols = ActiveSheet.FrozenCols;
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

    private bool IsFrozenRow(uint row) =>
        ActiveSheet.FrozenRows > 0 && row <= ActiveSheet.FrozenRows;

    private bool IsFrozenColumn(uint col) =>
        ActiveSheet.FrozenCols > 0 && col <= ActiveSheet.FrozenCols;

    private uint GetScrollableRowStart() =>
        Math.Min(CellAddress.MaxRow, Math.Max(1, ActiveSheet.FrozenRows + 1));

    private uint GetScrollableColumnStart() =>
        Math.Min(CellAddress.MaxCol, Math.Max(1, ActiveSheet.FrozenCols + 1));

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

    private ViewportModel BuildViewport() =>
        _viewportService.GetViewport(
            Workbook,
            ActiveSheet.Id,
            new ViewportRequest(
                ActiveSheet.ViewTopRow ?? 1,
                ActiveSheet.ViewLeftCol ?? 1,
                AvailableHeight: _viewportHeight,
                AvailableWidth: _viewportWidth,
                IncludeObjects: _includeObjects,
                SplitPaneOffsets: GetSplitPaneOffsetsForActiveSheet()));

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
    ViewportModel? Viewport = null)
{
    /// <summary>
    /// Succeeds with the full-range viewport (see <c>WorkbookSession.BuildFullRangeViewportForClipboard</c>)
    /// the text was serialized from, so callers building a CF_HTML fragment for the same copy/cut
    /// (e.g. the Avalonia shell's clipboard handler) render off the same complete range instead of
    /// re-reading the on-screen-only <see cref="WorkbookSession.Viewport"/> and truncating any part of
    /// the selection that is scrolled out of view (R14-clipboard-formats-deep-1).
    /// </summary>
    public static WorkbookClipboardTextResult Succeeded(string text, ViewportModel viewport) =>
        new(true, text, null, viewport);

    public static WorkbookClipboardTextResult Succeeded(string text) =>
        new(true, text, null);

    public static WorkbookClipboardTextResult Failed(string errorMessage) =>
        new(false, null, errorMessage);
}
