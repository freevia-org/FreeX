using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using CoreSortKey = FreeX.Core.Commands.SortKey;

namespace FreeX.App.Services;

public sealed class WorkbookSession
{
    private sealed record InternalClipboard(
        GridRange SourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> Cells,
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
    private readonly HashSet<SheetId> _groupedSheetIds = [];
    private SheetId? _sheetGroupAnchor;
    private InternalClipboard? _internalClipboard;
    private SheetId? _formatPainterSourceSheetId;
    private GridRange? _formatPainterSourceRange;
    private bool _formatPainterPersistent;
    private double _viewportHeight;
    private double _viewportWidth;
    private ulong _selectionStatsRevision;
    private string? _lastFindText;
    private FindOptions? _lastFindOptions;
    private bool _lastFindMatchCase;
    private bool _lastFindMatchEntireCell;
    private FindResult? _lastFindResult;
    private FindResult? _lastReplaceResult;

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

    public XlsxFeatureReport? CurrentXlsxFeatureReport { get; private set; }

    public bool IsDirty { get; private set; }

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

    public WorkbookGoToSpecialResult GoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)
    {
        var matches = GoToSpecialService.Find(Workbook, ActiveSheet, SelectedRange, kind, ActiveCell, options);
        if (matches.Count == 0)
            return WorkbookGoToSpecialResult.Failed("No cells found.");

        var ranges = SelectionRangeService.CompressAddresses(matches);
        var selectedRange = ranges[0];
        SelectRanges(selectedRange, ranges);
        return WorkbookGoToSpecialResult.Selected(selectedRange, ranges, matches.Count);
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
                var sheetRange = RemapRangeToSheet(plan.ActiveRange, sheetId);
                var removeCommand = plan.CreateCommand(sheetId, sheetRange);
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

        var range = SelectedRange;
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

        ApplySuccessfulRangeEditResult(result, range);
        return result;
    }

    public WorkbookCellEditResult RemoveSelectedRangeSubtotals()
    {
        var range = SelectedRange;
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

        ApplySuccessfulEditResult(result, plan.AffectedCells.FirstOrDefault(ActiveCell));
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
                (workbook, changedCells) => _cellEditService.RecalculateIfAutomatic(workbook, changedCells)));
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

        var editsBySheet = new Dictionary<SheetId, List<(CellAddress Address, Cell NewCell)>>();
        var commands = new List<IWorkbookCommand>();
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var match in matches)
        {
            var sheet = Workbook.GetSheet(match.Address.Sheet);
            if (sheet is null)
                continue;

            if (TryCreateReplacementCellCommand(
                    sheet,
                    match.Address,
                    searchText,
                    replaceText,
                    comparison,
                    matchEntireCell,
                    effectiveOptions.LookIn,
                    out var newCell))
            {
                if (!editsBySheet.TryGetValue(match.Address.Sheet, out var edits))
                {
                    edits = [];
                    editsBySheet[match.Address.Sheet] = edits;
                }

                edits.Add((match.Address, newCell));
                continue;
            }

            if (TryCreateReplacementCommentCommand(
                    sheet,
                    match,
                    searchText,
                    replaceText,
                    comparison,
                    matchEntireCell,
                    effectiveOptions.LookIn,
                    out var command))
            {
                commands.Add(command);
            }
        }

        var replacedCount = editsBySheet.Values.Sum(static edits => edits.Count) + commands.Count;
        foreach (var (sheetId, edits) in editsBySheet)
        {
            commands.Add(new EditCellsCommand(sheetId, edits));
            if (replacementFormat is not null)
            {
                commands.AddRange(edits.Select(edit => (IWorkbookCommand)new ApplyStyleCommand(
                    sheetId,
                    new GridRange(edit.Address, edit.Address),
                    replacementFormat)));
            }
        }

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
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (sheet is null ||
            !TryCreateReplacementCommand(
                sheet,
                match,
                searchText,
                replaceText,
                comparison,
                matchEntireCell,
                effectiveOptions.LookIn,
                replacementFormat,
                out var command))
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

    private bool SelectSheet(SheetId sheetId, bool selectRange, bool toggle)
    {
        var previousSheetId = ActiveSheet.Id;
        var previousGroupedSheetIds = _groupedSheetIds.ToHashSet();
        var selection = _sheetSelectionService.SelectSheet(Workbook, sheetId);
        var sheetChanged = previousSheetId != selection.Sheet.Id;

        ActiveSheet = selection.Sheet;
        UpdateGroupedSheetsForTabSelection(ActiveSheet.Id, selectRange, toggle);
        RefreshSheetTabsForActiveSheet();
        FormulaEditAddress = null;

        if (sheetChanged)
        {
            ActiveCell = GetInitialActiveCell(ActiveSheet);
            SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
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

        ApplySuccessfulWorkbookStructureResult(Workbook.Sheets[^1].Id);
        return result;
    }

    public WorkbookCellEditResult DuplicateActiveSheet()
    {
        var sourceSheetId = ActiveSheet.Id;
        var sourceIndex = Workbook.Sheets.ToList().FindIndex(sheet => sheet.Id == sourceSheetId);
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

        var copyIndex = Math.Min(sourceIndex + 1, Workbook.Sheets.Count - 1);
        ApplySuccessfulWorkbookStructureResult(Workbook.Sheets[copyIndex].Id);
        return result;
    }

    public WorkbookCellEditResult MoveActiveSheetLeft() =>
        MoveActiveSheetBy(offset: -1);

    public WorkbookCellEditResult MoveActiveSheetRight() =>
        MoveActiveSheetBy(offset: 1);

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
        var sheetIndex = Workbook.Sheets.ToList().FindIndex(sheet => sheet.Id == sheetId);
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
        var sheetIndex = Workbook.Sheets.ToList().FindIndex(sheet => sheet.Id == sheetId);
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

        ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
        return result;
    }

    private WorkbookCellEditResult MoveActiveSheetBy(int offset)
    {
        var sheetId = ActiveSheet.Id;
        var fromIndex = Workbook.Sheets.ToList().FindIndex(sheet => sheet.Id == sheetId);
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
        SetSingleSelectedRange(new GridRange(address, address));
        FormulaEditAddress = address;
    }

    public void CancelFormulaEdit()
    {
        FormulaEditAddress = null;
    }

    public WorkbookCellEditResult CommitCellText(string text, bool useR1C1ReferenceStyle = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var address = FormulaEditAddress ?? ActiveCell;
        if (!address.Sheet.Equals(ActiveSheet.Id))
            throw new InvalidOperationException("Cell edit address must belong to the active sheet.");

        var cell = CellEntryParser.CreateCell(text, address, useR1C1ReferenceStyle);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateEditCellsCommand([(address, cell)]));

        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, address);
        return result;
    }

    public WorkbookCellEditResult InsertAutoSumFormula(string functionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        var target = SelectedRange.Start;
        var formula = AutoSumFormulaPlanner.BuildFormula(ActiveSheet, functionName, target);
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateEditCellsCommand([(target, Cell.FromFormula(formula))]));
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, target);
        SelectCell(GetNextAutoSumCell(target));
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
        if (TryCreateMultiRangeClipboardTextResult("Copy", out var result))
            return result;

        var text = ClipboardSerializer.Serialize(Viewport, SelectedRange);
        _internalClipboard = CaptureInternalClipboard(SelectedRange, text, isCut: false);
        return WorkbookClipboardTextResult.Succeeded(text);
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

        var text = ClipboardSerializer.Serialize(Viewport, SelectedRange);
        _internalClipboard = CaptureInternalClipboard(SelectedRange, text, isCut: true);
        return WorkbookClipboardTextResult.Succeeded(text);
    }

    public WorkbookCellEditResult PasteClipboardTextAtActiveCell(string? text, bool preserveText = false)
    {
        if (_internalClipboard is { } internalClipboard)
        {
            if (text is null || string.Equals(internalClipboard.Text, text, StringComparison.Ordinal))
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

        return PasteExternalTextAtActiveCell(text, preserveText);
    }

    public WorkbookCellEditResult PasteSpecialClipboardAtActiveCell(
        string? text,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths = false)
    {
        if (!Enum.IsDefined(mode))
        {
            return new WorkbookCellEditResult(
                false,
                "Paste Special mode is not supported.",
                [],
                RecalcReport: null);
        }

        if (TryCreateMultiRangeClipboardEditResult("Paste Special", out var multiRangeResult))
            return multiRangeResult;

        if (_internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal)))
        {
            _internalClipboard = null;
            return new WorkbookCellEditResult(
                false,
                "Paste Special requires copied FreeX cells.",
                [],
                RecalcReport: null);
        }

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
        var command = CreateGroupedSheetCommand(
            "Paste Column Widths",
            sheetId => new PasteColumnWidthsCommand(
                sheetId,
                internalClipboard.SourceRange,
                RemapAddressToSheet(destination, sheetId).Col));
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
        var pasteSize = GetPasteDimensions(internalClipboard.SourceRange, transpose);
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
                    RemapAddressToSheet(destination, sheetId),
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
        var pasteSize = GetPasteDimensions(internalClipboard.SourceRange, transpose);
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
                    RemapAddressToSheet(destination, sheetId),
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
        var command = CreatePasteLinkCommand(
            internalClipboard,
            sourceSheet.Name,
            destination,
            transpose,
            keepSourceColumnWidths);

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        var pasteSize = GetPasteDimensions(internalClipboard.SourceRange, transpose);
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
        var sourceCells = internalClipboard.Cells
            .Select(static cell => (cell.Source, FormatPictureCellText(cell.Cell.Value)))
            .ToList();
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
        if (!string.IsNullOrWhiteSpace(text))
            return false;

        return _internalClipboard is not { } internalClipboard ||
            (text is not null && !string.Equals(internalClipboard.Text, text, StringComparison.Ordinal));
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

    public WorkbookCellEditResult PasteExternalTextAtActiveCell(string text, bool preserveText = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        var destination = ActiveCell;
        var rows = ClipboardSerializer.Deserialize(text);
        var columnCount = rows.Length == 0 ? 0 : rows.Max(static row => row.Length);
        var command = CreateExternalTextPasteCommand(destination, rows, preserveText);
        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        SelectPastedRange(destination, (ulong)rows.Length, (ulong)columnCount);
        return result;
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

    public bool CanFillSelectedRange(FillCellsDirection direction) =>
        direction switch
        {
            FillCellsDirection.Down or FillCellsDirection.Up => SelectedRange.RowCount > 1,
            FillCellsDirection.Right or FillCellsDirection.Left => SelectedRange.ColCount > 1,
            _ => false
        };

    public bool CanSortSelectedRange => SelectedRange.RowCount > 1;

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
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateFormatPainterCommand(sourceSheet, sourceRange, targetRange));
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

    public WorkbookCellEditResult SetSelectedRangeWrapText(bool enabled) =>
        ApplySelectedRangeStyle(new StyleDiff(WrapText: enabled));

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
        bool? mergeCells = null)
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
            commands.AddRange(CreateFormatCellsMergeCommands(range, shouldMerge));

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

    public WorkbookCellEditResult MergeAndCenterSelectedRange()
    {
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateMergeAndCenterCommand(range));
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

    public WorkbookCellEditResult SetSelectedRangeFillColor(CellColor fillColor) =>
        ApplySelectedRangeStyle(new StyleDiff(FillColor: fillColor));

    public WorkbookCellEditResult ClearSelectedRangeFill() =>
        ApplySelectedRangeStyle(new StyleDiff(ClearFill: true));

    public WorkbookCellEditResult UndoLastEdit()
    {
        var sheetIdsBefore = CaptureSheetIds();
        var result = _cellEditService.UndoLastEdit(Workbook);
        if (!result.Success)
            return result;

        ApplySuccessfulHistoryResult(result, sheetIdsBefore);
        return result;
    }

    public WorkbookCellEditResult RedoLastEdit()
    {
        var sheetIdsBefore = CaptureSheetIds();
        var result = _cellEditService.RedoLastEdit(Workbook);
        if (!result.Success)
            return result;

        ApplySuccessfulHistoryResult(result, sheetIdsBefore);
        return result;
    }

    public bool CanSaveCurrentSource(out FileSaveTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
            return false;

        return TryResolveSaveTarget(CurrentFilePath, out target, out _);
    }

    public bool TryResolveOpenTarget(string path, out WorkbookOpenTarget? target, out string message)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            message = "Open requires a local file path.";
            return false;
        }

        var openPath = path.Trim();
        if (!TryGetExtension(openPath, out var extension))
        {
            message = "Unsupported file type.";
            return false;
        }

        var adapter = FileFormatResolver.FindOpenAdapter(_adapters, extension, out var format);
        if (adapter is null || format is null)
        {
            message = $"Unsupported file type: {extension}.";
            return false;
        }

        target = new WorkbookOpenTarget(openPath, adapter, extension, format);
        message = "";
        return true;
    }

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

    public void MarkSaved(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        IsDirty = false;
        CurrentFilePath = path;
        CurrentXlsxFeatureReport = null;
        Workbook.Name = Path.GetFileName(path);
    }

    public string BuildSuggestedSaveAsFileName(string defaultExtension)
    {
        var normalizedExtension = FileFormatResolver.NormalizeExtension(defaultExtension);
        var sourceName = string.IsNullOrWhiteSpace(Workbook.Name)
            ? DisplayName
            : Workbook.Name;
        var baseName = Path.GetFileNameWithoutExtension(sourceName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Workbook";

        return baseName + normalizedExtension;
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

    private HashSet<SheetId> CaptureSheetIds() =>
        Workbook.Sheets.Select(sheet => sheet.Id).ToHashSet();

    private void ApplySuccessfulHistoryResult(
        WorkbookCellEditResult result,
        IReadOnlySet<SheetId> sheetIdsBefore)
    {
        if (result.AffectedCells.Count > 0)
        {
            ApplySuccessfulEditResult(result, ActiveCell);
            return;
        }

        if (FindNewSheetId(sheetIdsBefore) is { } newSheetId)
        {
            ApplySuccessfulWorkbookStructureResult(newSheetId);
            return;
        }

        if (Workbook.GetSheet(ActiveSheet.Id) is not null)
        {
            ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
            return;
        }

        ApplySuccessfulWorkbookStructureResult(ActiveSheet.Id);
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

    private IWorkbookCommand CreateMergeAndCenterCommand(GridRange range)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count * 2);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetRange = RemapRangeToSheet(range, sheetId);
            commands.AddRange(CellMergePlanner.CreateMergeAndCenterCommands(sheetId, sheetRange));
        }

        return ToCommand("Merge & Center", commands);
    }

    private IReadOnlyList<IWorkbookCommand> CreateFormatCellsMergeCommands(GridRange range, bool mergeCells)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>();
        foreach (var sheetId in targetSheetIds)
        {
            var sheet = Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            commands.AddRange(CellMergePlanner.CreateMergeCommands(
                sheet,
                sheetId,
                RemapRangeToSheet(range, sheetId),
                mergeCells));
        }

        return commands;
    }

    private IWorkbookCommand CreateFormatPainterCommand(Sheet sourceSheet, GridRange sourceRange, GridRange targetRange)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            commands.Add(FormatPainterCommandFactory.Create(
                Workbook,
                sourceSheet,
                sourceRange,
                RemapRangeToSheet(targetRange, sheetId)));
        }

        return ToCommand("Format Painter", commands);
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
        CellAddress destination,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool preserveText)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = targetSheetIds
            .Select(sheetId => PasteCommandFactory.CreateExternalTextPasteCommand(
                sheetId,
                RemapAddressToSheet(destination, sheetId),
                rows,
                preserveText))
            .ToList();
        return ToCommand("Paste", commands);
    }

    private IWorkbookCommand CreateInternalPasteCommand(
        InternalClipboard clipboard,
        CellAddress destination,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        bool keepSourceColumnWidths)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetDestination = RemapAddressToSheet(destination, sheetId);
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
                        new PasteColumnWidthsCommand(sheetId, clipboard.SourceRange, sheetDestination.Col)
                    ]);
            }

            commands.Add(command);
        }

        var label = mode == PasteCellsMode.All && options == default && !keepSourceColumnWidths
            ? "Paste"
            : "Paste Special";
        return ToCommand(label, commands);
    }

    private IWorkbookCommand CreatePasteLinkCommand(
        InternalClipboard clipboard,
        string sourceSheetName,
        CellAddress destination,
        bool transpose,
        bool keepSourceColumnWidths)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        var commands = new List<IWorkbookCommand>(targetSheetIds.Count);
        foreach (var sheetId in targetSheetIds)
        {
            var sheetDestination = RemapAddressToSheet(destination, sheetId);
            var linkedCells = PasteLinkService.CreateLinkedCells(
                clipboard.SourceRange,
                sheetDestination,
                sourceSheetName,
                transpose);
            IWorkbookCommand command = new EditCellsCommand(sheetId, linkedCells);
            if (keepSourceColumnWidths)
            {
                command = new CompositeWorkbookCommand(
                    "Paste Link",
                    [
                        command,
                        new PasteColumnWidthsCommand(sheetId, clipboard.SourceRange, sheetDestination.Col)
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
        var range = SelectedRange;
        var result = _cellEditService.ExecuteEditCommand(
            Workbook,
            CreateApplyStyleCommand(range, diff));
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
        var existing = sheet.DataValidations.FirstOrDefault(candidate =>
            candidate.Id == rule.Id || candidate.AppliesTo == rule.AppliesTo);
        return existing is null || !DataValidationRulesEqual(existing, rule);
    }

    private static bool HasDataValidationOverlapping(Sheet sheet, GridRange range) =>
        sheet.DataValidations.Any(rule => DataValidationRanges(rule).Any(ruleRange => ruleRange.Overlaps(range)));

    private static IEnumerable<GridRange> DataValidationRanges(DataValidation rule)
    {
        yield return rule.AppliesTo;
        foreach (var range in rule.AdditionalRanges)
            yield return range;
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

    private static CellAddress GetNextAutoSumCell(CellAddress address) =>
        new(address.Sheet, address.Row < CellAddress.MaxRow ? address.Row + 1 : address.Row, address.Col);

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

    private void ApplySuccessfulWorkbookStructureResult(SheetId preferredSheetId)
    {
        var selection = _sheetSelectionService.SelectSheet(Workbook, preferredSheetId);
        ActiveSheet = selection.Sheet;
        SelectSingleSheetGroup(ActiveSheet.Id);
        RefreshSheetTabsForActiveSheet();
        ActiveCell = GetInitialActiveCell(ActiveSheet);
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        SetSingleSelectedRange(new GridRange(ActiveCell, ActiveCell));
        FormulaEditAddress = null;
        IsDirty = true;
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulWorkbookStructureRangeResult(SheetId preferredSheetId, GridRange selectedRange)
    {
        var selection = _sheetSelectionService.SelectSheet(Workbook, preferredSheetId);
        ActiveSheet = selection.Sheet;
        SelectSingleSheetGroup(ActiveSheet.Id);
        RefreshSheetTabsForActiveSheet();
        ActiveCell = selectedRange.Start;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        SetSingleSelectedRange(selectedRange);
        FormulaEditAddress = null;
        IsDirty = true;
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
        IsDirty = true;
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulEditResult(WorkbookCellEditResult result, CellAddress fallbackAddress)
    {
        var address = result.AffectedCells.FirstOrDefault(fallbackAddress);
        if (!ActiveSheet.Id.Equals(address.Sheet))
        {
            var selection = _sheetSelectionService.SelectSheet(Workbook, address.Sheet, _groupedSheetIds);
            ActiveSheet = selection.Sheet;
            RefreshSheetTabsForActiveSheet();
        }

        ActiveCell = address;
        ActiveSheet.ActiveRow = address.Row;
        ActiveSheet.ActiveCol = address.Col;
        SetSingleSelectedRange(new GridRange(address, address));
        FormulaEditAddress = null;
        IsDirty = true;
        _selectionStatsRevision++;
        RefreshViewport();
        EnsureActiveCellVisible();
    }

    private void ApplySuccessfulRangeEditResult(WorkbookCellEditResult result, GridRange selectedRange)
    {
        ActiveCell = selectedRange.Start;
        ActiveSheet.ActiveRow = ActiveCell.Row;
        ActiveSheet.ActiveCol = ActiveCell.Col;
        SetSingleSelectedRange(selectedRange);
        FormulaEditAddress = null;
        IsDirty = true;
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
        var namedRange = Workbook.NamedRanges
            .Where(pair => pair.Value.Contains(address))
            .OrderBy(pair => pair.Value.CellCount)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return string.IsNullOrEmpty(namedRange.Key) ? "" : namedRange.Key;
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

    private int FindSheetIndex(SheetId sheetId)
    {
        for (var index = 0; index < Workbook.Sheets.Count; index++)
        {
            if (Workbook.Sheets[index].Id.Equals(sheetId))
                return index;
        }

        return int.MaxValue;
    }

    private static bool TryCreateReplacementCommand(
        Sheet sheet,
        FindResult match,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        FindLookIn lookIn,
        StyleDiff? replacementFormat,
        out IWorkbookCommand command)
    {
        command = null!;
        if (TryCreateReplacementCellCommand(
                sheet,
                match.Address,
                searchText,
                replaceText,
                comparison,
                matchEntireCell,
                lookIn,
                out var newCell))
        {
            var editCommand = new EditCellsCommand(sheet.Id, [(match.Address, newCell)]);
            command = replacementFormat is null
                ? editCommand
                : new CompositeWorkbookCommand(
                    "Replace",
                    [
                        editCommand,
                        new ApplyStyleCommand(
                            sheet.Id,
                            new GridRange(match.Address, match.Address),
                            replacementFormat)
                    ]);
            return true;
        }

        return TryCreateReplacementCommentCommand(
            sheet,
            match,
            searchText,
            replaceText,
            comparison,
            matchEntireCell,
            lookIn,
            out command);
    }

    private static bool TryCreateReplacementCellCommand(
        Sheet sheet,
        CellAddress address,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        FindLookIn lookIn,
        out Cell newCell)
    {
        newCell = null!;
        var cell = sheet.GetCell(address);
        if (cell is null)
            return false;

        var currentText = lookIn switch
        {
            FindLookIn.Formulas => cell.FormulaText,
            FindLookIn.Values => cell.HasFormula ? null : GetReplaceableDisplayText(cell.Value),
            _ => null
        };
        if (currentText is null ||
            !TryCreateReplacementText(currentText, searchText, replaceText, comparison, matchEntireCell, out var newText))
            return false;

        if (lookIn == FindLookIn.Formulas)
        {
            newCell = cell.Clone();
            newCell.FormulaText = newText;
            return true;
        }

        ScalarValue newValue = double.TryParse(newText, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
            ? new NumberValue(number)
            : new TextValue(newText);

        newCell = Cell.FromValue(newValue);
        return true;
    }

    private static bool TryCreateReplacementCommentCommand(
        Sheet sheet,
        FindResult match,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        FindLookIn lookIn,
        out IWorkbookCommand command)
    {
        command = null!;
        var currentText = lookIn switch
        {
            FindLookIn.Notes when
                match.Target == FindResultTarget.Note &&
                sheet.Comments.TryGetValue(match.Address, out var note) => note,
            FindLookIn.Comments when
                match.Target == FindResultTarget.ThreadedComment &&
                sheet.ThreadedComments.TryGetValue(match.Address, out var threadedComment) => threadedComment.Text,
            FindLookIn.Comments when
                match.Target == FindResultTarget.ThreadedCommentReply &&
                match.ReplyIndex is { } replyIndex &&
                sheet.ThreadedComments.TryGetValue(match.Address, out var threadedComment) &&
                IsValidThreadedCommentReplyIndex(threadedComment, replyIndex) => threadedComment.Replies[replyIndex].Text,
            _ => null
        };
        if (currentText is null ||
            !TryCreateReplacementText(currentText, searchText, replaceText, comparison, matchEntireCell, out var newText))
            return false;

        command = lookIn switch
        {
            FindLookIn.Notes when match.Target == FindResultTarget.Note =>
                new SetCommentCommand(sheet.Id, match.Address, newText),
            FindLookIn.Comments when match.Target == FindResultTarget.ThreadedComment =>
                new UpdateThreadedCommentTextCommand(sheet.Id, match.Address, newText),
            FindLookIn.Comments when
                match.Target == FindResultTarget.ThreadedCommentReply &&
                match.ReplyIndex is { } replyIndex =>
                new UpdateThreadedCommentReplyCommand(sheet.Id, match.Address, replyIndex, newText),
            _ => null!
        };

        return command is not null;
    }

    private static bool IsValidThreadedCommentReplyIndex(ThreadedComment comment, int replyIndex) =>
        replyIndex >= 0 && replyIndex < comment.Replies.Count;

    private static bool TryCreateReplacementText(
        string currentText,
        string searchText,
        string replaceText,
        StringComparison comparison,
        bool matchEntireCell,
        out string newText)
    {
        newText = "";
        var isMatch = matchEntireCell
            ? currentText.Equals(searchText, comparison)
            : currentText.Contains(searchText, comparison);
        if (!isMatch)
            return false;

        newText = matchEntireCell
            ? replaceText
            : currentText.Replace(searchText, replaceText, comparison);
        return true;
    }

    private static string? GetReplaceableDisplayText(ScalarValue value) => value switch
    {
        BlankValue => null,
        NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
        TextValue text => text.Value,
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue dateTime => dateTime.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ErrorValue error => error.Code,
        _ => null
    };

    private SheetId? ResolveSheetIdByName(string sheetName) =>
        Workbook.Sheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))?.Id;

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
        var command = CreateInternalPasteCommand(
            clipboard,
            destination,
            mode,
            options,
            keepSourceColumnWidths);

        if (ShouldClearCutSourceAfterPaste(clipboard, destination, mode, options, keepSourceColumnWidths))
        {
            command = new CompositeWorkbookCommand(
                "Cut and Paste",
                [
                    command,
                    new ClearContentsCommand(clipboard.SourceRange.Start.Sheet, clipboard.SourceRange)
                ]);
        }

        var result = _cellEditService.ExecuteEditCommand(Workbook, command);
        if (!result.Success)
            return result;

        ApplySuccessfulEditResult(result, destination);
        var pasteSize = GetPasteDimensions(clipboard.SourceRange, options.Transpose);
        SelectPastedRange(destination, pasteSize.RowCount, pasteSize.ColCount);
        if (clipboard.IsCut)
            _internalClipboard = null;
        return result;
    }

    private InternalClipboard CaptureInternalClipboard(GridRange range, string text, bool isCut)
    {
        var sheet = Workbook.GetSheet(range.Start.Sheet);
        var cells = new List<(CellAddress Source, Cell Cell)>();
        foreach (var address in range.AllCells())
        {
            var cell = sheet?.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance);
            cells.Add((address, cell));
        }

        return new InternalClipboard(range, cells, text, isCut);
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

    private static (ulong RowCount, ulong ColCount) GetPasteDimensions(GridRange sourceRange, bool transpose) =>
        transpose
            ? (sourceRange.ColCount, sourceRange.RowCount)
            : (sourceRange.RowCount, sourceRange.ColCount);

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

    private static string FormatPictureCellText(ScalarValue value) =>
        value switch
        {
            BlankValue => "",
            NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            TextValue text => text.Value,
            ErrorValue error => error.Code,
            _ => value.ToString() ?? ""
        };

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
                IncludeObjects: _includeObjects));

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

    private static bool TryGetExtension(string path, out string extension)
    {
        try
        {
            if (path.Contains('\0', StringComparison.Ordinal) ||
                path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                extension = "";
                return false;
            }

            extension = Path.GetExtension(path) ?? "";
            return !string.IsNullOrWhiteSpace(extension);
        }
        catch (ArgumentException)
        {
            extension = "";
            return false;
        }
        catch (NotSupportedException)
        {
            extension = "";
            return false;
        }
        catch (PathTooLongException)
        {
            extension = "";
            return false;
        }
    }

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
}

public sealed record WorkbookClipboardTextResult(
    bool Success,
    string? Text,
    string? ErrorMessage)
{
    public static WorkbookClipboardTextResult Succeeded(string text) =>
        new(true, text, null);

    public static WorkbookClipboardTextResult Failed(string errorMessage) =>
        new(false, null, errorMessage);
}
