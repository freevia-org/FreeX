using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public enum WorksheetFilterMutationKind
{
    ApplyFilter,
    ClearFilter,
    SortAscending,
    SortDescending,
    SortByColor
}

public enum WorksheetFilterMutationError
{
    None,
    InvalidCriteria,
    SelectionRequired,
    UnsupportedColor
}

public sealed class WorksheetFilterMutationPlan
{
    private readonly Func<GridRange, IWorkbookCommand>? _createCommand;

    internal WorksheetFilterMutationPlan(
        SheetId sheetId,
        GridRange range,
        uint columnOffset,
        WorksheetFilterMutationKind kind,
        string historyLabel,
        Func<GridRange, IWorkbookCommand> createCommand)
    {
        SheetId = sheetId;
        Range = range;
        ColumnOffset = columnOffset;
        Kind = kind;
        HistoryLabel = historyLabel;
        _createCommand = createCommand;
    }

    internal WorksheetFilterMutationPlan(
        SheetId sheetId,
        GridRange range,
        uint columnOffset,
        WorksheetFilterMutationError error,
        FilterPromptPlanError promptError = FilterPromptPlanError.None)
    {
        SheetId = sheetId;
        Range = range;
        ColumnOffset = columnOffset;
        Kind = WorksheetFilterMutationKind.ApplyFilter;
        HistoryLabel = "Filter";
        Error = error;
        PromptError = promptError;
    }

    public SheetId SheetId { get; }
    public GridRange Range { get; }
    public uint ColumnOffset { get; }
    public WorksheetFilterMutationKind Kind { get; }
    public string HistoryLabel { get; }
    public WorksheetFilterMutationError Error { get; }
    public FilterPromptPlanError PromptError { get; }
    public bool Success => Error == WorksheetFilterMutationError.None;

    public IWorkbookCommand CreateCommand() => CreateCommand(Range);

    public IWorkbookCommand CreateCommand(GridRange range) =>
        _createCommand?.Invoke(range) ??
        throw new InvalidOperationException("A failed worksheet filter plan has no command.");
}

public sealed record WorksheetFilterClearPlan(
    GridRange Range,
    IWorkbookCommand Command,
    int DefinitionCount);

public sealed record WorksheetFilterReapplyPlan(
    GridRange Range,
    IReadOnlyList<IWorkbookCommand> Commands)
{
    public int DefinitionCount => Commands.Count;

    public IWorkbookCommand CreateCommand(string label) =>
        Commands.Count == 1
            ? Commands[0]
            : new CompositeWorkbookCommand(label, Commands);
}

/// <summary>
/// Owns the selection aftermath shared by worksheet AutoFilter commands. WPF expands a single
/// selected header cell back to the filter range after applying, clearing, or reapplying a filter,
/// while preserving every other selection (including multi-area selections). Keeping the predicate
/// portable prevents either renderer from approximating that policy independently.
/// </summary>
public static class WorksheetFilterSelectionPlanner
{
    public static bool ShouldExpandHeaderCell(GridRange selectedRange, GridRange filterRange) =>
        selectedRange != filterRange &&
        selectedRange.RowCount == 1 &&
        selectedRange.ColCount == 1 &&
        selectedRange.Start.Row == filterRange.Start.Row &&
        filterRange.Contains(selectedRange.Start);
}

/// <summary>
/// Owns portable AutoFilter and Advanced Filter workflow decisions shared by the WPF and Avalonia
/// shells. Renderers provide dialog results and execute the returned Core commands; this session
/// retains live criteria needed for Reapply when a structured-table criterion is only persisted as
/// native XML, and reconstructs ordinary worksheet criteria from durable model metadata.
/// </summary>
public sealed class WorksheetFilterWorkflowSession
{
    private readonly Dictionary<uint, WorksheetFilterMutationPlan> _activeColumnPlans = [];
    private GridRange? _activeAutoFilterRange;
    private AdvancedFilterReapplyState? _lastInPlaceAdvancedFilter;

    public WorksheetFilterMutationPlan PlanDialogResult(
        Sheet sheet,
        GridRange range,
        uint columnOffset,
        AutoFilterDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(result);

        var sheetId = sheet.Id;

        if (result.Action == AutoFilterDialogAction.ClearFilter)
        {
            return CreatePlan(
                sheetId,
                range,
                columnOffset,
                WorksheetFilterMutationKind.ClearFilter,
                "Clear Filter",
                currentRange => new FilterCommand(sheetId, currentRange, columnOffset, []));
        }

        if (result.SortDirection != AutoFilterSortDirection.None)
        {
            var ascending = result.SortDirection == AutoFilterSortDirection.Ascending;
            return CreatePlan(
                sheetId,
                range,
                columnOffset,
                ascending
                    ? WorksheetFilterMutationKind.SortAscending
                    : WorksheetFilterMutationKind.SortDescending,
                "Sort",
                currentRange => new SortCommand(
                    sheetId,
                    ExcludeAutoFilterHeaderAndTotalsRow(sheet, currentRange),
                    columnOffset,
                    ascending));
        }

        if (result.SortByColorFilter is { } sortByColor)
        {
            if (sortByColor.Color is not { } color)
                return Invalid(sheetId, range, columnOffset, WorksheetFilterMutationError.UnsupportedColor);

            return CreatePlan(
                sheetId,
                range,
                columnOffset,
                WorksheetFilterMutationKind.SortByColor,
                "Sort by Color",
                currentRange => AutoFilterDropdownMenuPlanner.CreateSortByColorCommand(
                    sheetId,
                    ExcludeAutoFilterHeaderAndTotalsRow(sheet, currentRange),
                    columnOffset,
                    new AutoFilterColorOption(string.Empty, sortByColor.Kind, color)));
        }

        if (result.ColorFilter is { } colorFilter)
        {
            var factory = CreateColorFilterFactory(sheetId, columnOffset, colorFilter);
            if (factory is null)
                return Invalid(sheetId, range, columnOffset, WorksheetFilterMutationError.UnsupportedColor);

            var label = colorFilter.Kind switch
            {
                AutoFilterColorFilterKind.FontColor => "Filter by Font Color",
                AutoFilterColorFilterKind.NoFill => "Filter by No Fill",
                _ => "Filter by Cell Color"
            };
            return CreatePlan(
                sheetId,
                range,
                columnOffset,
                WorksheetFilterMutationKind.ApplyFilter,
                label,
                factory);
        }

        if (!string.IsNullOrWhiteSpace(result.CriteriaText))
        {
            if (!FilterPromptPlanner.TryPlan(result.CriteriaText, out var promptPlan, out var promptError) ||
                promptPlan is null)
            {
                return Invalid(
                    sheetId,
                    range,
                    columnOffset,
                    WorksheetFilterMutationError.InvalidCriteria,
                    promptError);
            }

            return CreatePlan(
                sheetId,
                range,
                columnOffset,
                WorksheetFilterMutationKind.ApplyFilter,
                "Filter",
                currentRange => promptPlan.CreateCommand(sheetId, currentRange, columnOffset));
        }

        if (result.SelectedValues.Count == 0)
            return Invalid(sheetId, range, columnOffset, WorksheetFilterMutationError.SelectionRequired);

        return PlanAllowedValues(sheetId, range, columnOffset, result.SelectedValues);
    }

    public WorksheetFilterMutationPlan PlanAllowedValues(
        SheetId sheetId,
        GridRange range,
        uint columnOffset,
        IReadOnlyList<string> allowedValues) =>
        CreatePlan(
            sheetId,
            range,
            columnOffset,
            allowedValues.Count == 0
                ? WorksheetFilterMutationKind.ClearFilter
                : WorksheetFilterMutationKind.ApplyFilter,
            allowedValues.Count == 0 ? "Clear Filter" : "Filter",
            currentRange => new FilterCommand(sheetId, currentRange, columnOffset, allowedValues));

    public void RecordSuccessfulMutation(WorksheetFilterMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.Success)
            throw new ArgumentException("Only successful filter plans can be recorded.", nameof(plan));

        var absoluteColumn = plan.Range.Start.Col + plan.ColumnOffset;
        switch (plan.Kind)
        {
            case WorksheetFilterMutationKind.ApplyFilter:
                if (_activeAutoFilterRange is not { } activeRange || activeRange != plan.Range)
                {
                    _activeColumnPlans.Clear();
                    _activeAutoFilterRange = plan.Range;
                }
                _activeColumnPlans[absoluteColumn] = plan;
                break;

            case WorksheetFilterMutationKind.ClearFilter:
                _activeColumnPlans.Remove(absoluteColumn);
                if (_activeColumnPlans.Count == 0)
                    _activeAutoFilterRange = null;
                break;
        }
    }

    public void ResetAutoFilterState()
    {
        _activeAutoFilterRange = null;
        _activeColumnPlans.Clear();
    }

    public void RememberAdvancedFilter(
        GridRange listRange,
        GridRange criteriaRange,
        bool filterInPlace,
        bool uniqueRecordsOnly)
    {
        var state = AdvancedFilterReapplyPlanner.CreateState(
            listRange,
            criteriaRange,
            filterInPlace,
            uniqueRecordsOnly);
        if (state is not null)
            _lastInPlaceAdvancedFilter = state;
    }

    public WorksheetFilterClearPlan CreateClearAllPlan(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var offsets = CollectActiveColumnOffsets(sheet, range);
        if (offsets.Count == 0)
            offsets.Add(0);

        var commands = offsets
            .Select(offset => (IWorkbookCommand)new FilterCommand(sheet.Id, range, offset, []))
            .ToList();
        var command = commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand("Clear Filter", commands);
        return new WorksheetFilterClearPlan(range, command, offsets.Count);
    }

    public void RecordSuccessfulClearAll(WorksheetFilterClearPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_activeAutoFilterRange is { } activeRange && activeRange == plan.Range)
            ResetAutoFilterState();
    }

    public WorksheetFilterReapplyPlan? CreateReapplyPlan(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var commands = new List<IWorkbookCommand>();
        if (_lastInPlaceAdvancedFilter is { } advanced && advanced.ListRange.Start.Sheet == sheet.Id)
            commands.Add(AdvancedFilterReapplyPlanner.CreatePlan(advanced).CreateCommand());

        GridRange? autoFilterRange = null;
        if (AutoFilterRangeResolver.TryGetEffectiveAutoFilterRange(sheet, out var resolvedRange))
        {
            autoFilterRange = resolvedRange;
            var commandsByColumn = ReconstructPersistedCommands(sheet, resolvedRange);
            if (_activeAutoFilterRange is { } activeRange && activeRange == resolvedRange)
            {
                foreach (var (absoluteColumn, plan) in _activeColumnPlans)
                {
                    if (absoluteColumn < resolvedRange.Start.Col || absoluteColumn > resolvedRange.End.Col)
                        continue;

                    commandsByColumn[absoluteColumn] = plan.CreateCommand(resolvedRange);
                }
            }

            commands.AddRange(commandsByColumn.Values);
        }

        if (commands.Count == 0)
            return null;

        var range = autoFilterRange ?? _lastInPlaceAdvancedFilter!.ListRange;
        return new WorksheetFilterReapplyPlan(range, commands);
    }

    private static WorksheetFilterMutationPlan CreatePlan(
        SheetId sheetId,
        GridRange range,
        uint columnOffset,
        WorksheetFilterMutationKind kind,
        string historyLabel,
        Func<GridRange, IWorkbookCommand> createCommand) =>
        new(sheetId, range, columnOffset, kind, historyLabel, createCommand);

    private static WorksheetFilterMutationPlan Invalid(
        SheetId sheetId,
        GridRange range,
        uint columnOffset,
        WorksheetFilterMutationError error,
        FilterPromptPlanError promptError = FilterPromptPlanError.None) =>
        new(sheetId, range, columnOffset, error, promptError);

    private static Func<GridRange, IWorkbookCommand>? CreateColorFilterFactory(
        SheetId sheetId,
        uint columnOffset,
        AutoFilterColorFilter colorFilter) =>
        colorFilter.Kind switch
        {
            AutoFilterColorFilterKind.FontColor when colorFilter.Color is { } fontColor =>
                range => new CellFontColorFilterCommand(sheetId, range, columnOffset, fontColor),
            AutoFilterColorFilterKind.NoFill =>
                range => new CellNoFillColorFilterCommand(sheetId, range, columnOffset),
            AutoFilterColorFilterKind.CellFillColor when colorFilter.Color is { } fillColor =>
                range => new CellFillColorFilterCommand(sheetId, range, columnOffset, fillColor),
            _ => null
        };

    /// <summary>
    /// Trims the header row from the front of an AutoFilter dropdown's Sort/Sort-by-Color range, and
    /// -- when <paramref name="range"/> is exactly a structured table's own <c>Range</c> with its
    /// Totals Row shown -- trims the Totals Row from the back too, via the same
    /// <see cref="AutoFilterRangeResolver.GetFilterableLastRow"/> bound the interactive filter
    /// commands (FilterCommand, TopBottomFilterCommand, AverageFilterCommand, FilterConditionCommand)
    /// already use. Without this, range.End.Row IS the Totals Row for such a table (see
    /// SetStructuredTableTotalsRowCommand), and sorting from the dropdown would shuffle the row
    /// holding the table's SUBTOTAL formula into the data body while sorting a normal row's content
    /// into the Totals Row position.
    /// </summary>
    private static GridRange ExcludeAutoFilterHeaderAndTotalsRow(Sheet sheet, GridRange range)
    {
        if (range.RowCount <= 1)
            return range;

        // Resolve the last filterable row against the ORIGINAL range -- GetFilterableLastRow only
        // recognizes a table's Totals Row when the range passed in still equals that table's own
        // Range exactly, which stops being true the moment the header row is trimmed off the front.
        var lastDataRow = AutoFilterRangeResolver.GetFilterableLastRow(sheet, range);
        var endRow = lastDataRow < range.Start.Row + 1 ? range.Start.Row + 1 : lastDataRow;

        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row + 1, range.Start.Col),
            new CellAddress(range.Start.Sheet, endRow, range.End.Col));
    }

    private static List<uint> CollectActiveColumnOffsets(Sheet sheet, GridRange range)
    {
        var absoluteColumns = new HashSet<uint>(sheet.ActiveValueFilterColumns.Keys);
        absoluteColumns.UnionWith(sheet.ColumnFilterOwnedRows.Keys);

        if (sheet.AutoFilter is { } autoFilter)
        {
            foreach (var column in autoFilter.FilterColumns)
            {
                if (column.ColumnId >= 0)
                    absoluteColumns.Add(range.Start.Col + (uint)column.ColumnId);
            }
        }

        foreach (var table in sheet.StructuredTables)
        {
            if (!table.HasAutoFilter || table.Range != range)
                continue;

            foreach (var column in table.FilterColumns)
            {
                if (column.ColumnId >= 0)
                    absoluteColumns.Add(range.Start.Col + (uint)column.ColumnId);
            }
        }

        return absoluteColumns
            .Where(column => column >= range.Start.Col && column <= range.End.Col)
            .OrderBy(column => column)
            .Select(column => column - range.Start.Col)
            .ToList();
    }

    private static SortedDictionary<uint, IWorkbookCommand> ReconstructPersistedCommands(
        Sheet sheet,
        GridRange range)
    {
        var commands = new SortedDictionary<uint, IWorkbookCommand>();
        if (AutoFilterRangeResolver.TryGetWorksheetAutoFilterRange(sheet, out var worksheetRange) &&
            worksheetRange == range &&
            sheet.AutoFilter is { } autoFilter)
        {
            foreach (var column in autoFilter.FilterColumns)
            {
                if (TryCreatePersistedCommand(sheet.Id, range, column) is { } command)
                    commands[range.Start.Col + (uint)column.ColumnId] = command;
            }
            return commands;
        }

        foreach (var table in sheet.StructuredTables)
        {
            if (!table.HasAutoFilter || table.Range != range)
                continue;

            foreach (var column in table.FilterColumns)
            {
                if (TryCreatePersistedCommand(sheet.Id, range, column) is { } command)
                    commands[range.Start.Col + (uint)column.ColumnId] = command;
            }
            break;
        }

        return commands;
    }

    private static IWorkbookCommand? TryCreatePersistedCommand(
        SheetId sheetId,
        GridRange range,
        WorksheetAutoFilterColumnModel column)
    {
        if (column.ColumnId < 0 || (uint)column.ColumnId >= range.ColCount)
            return null;

        var columnOffset = (uint)column.ColumnId;
        if (column.Values.Count > 0 || column.IncludeBlank)
            return new FilterCommand(sheetId, range, columnOffset, IncludeBlank(column.Values, column.IncludeBlank));

        if (column.Top10 is { } top10)
        {
            var count = ClampToUInt(top10.Value ?? 10);
            return top10.Percent
                ? TopBottomFilterCommand.Percent(sheetId, range, columnOffset, count, top10.Top)
                : new TopBottomFilterCommand(sheetId, range, columnOffset, count, top10.Top);
        }

        if (column.DynamicFilter is { } dynamicFilter && TryGetAverageDirection(dynamicFilter.Type, out var above))
            return new AverageFilterCommand(sheetId, range, columnOffset, above);

        if (column.ColorFilter is { } colorFilter)
            return TryCreatePersistedColorCommand(sheetId, range, columnOffset, colorFilter);

        if (CustomFilterModelReconstructor.Reconstruct(column.CustomFilters, column.CustomFiltersAnd) is { } criterion)
            return new FilterConditionCommand(sheetId, range, columnOffset, criterion);

        return null;
    }

    private static IWorkbookCommand? TryCreatePersistedCommand(
        SheetId sheetId,
        GridRange range,
        StructuredTableFilterColumnModel column)
    {
        if (column.ColumnId < 0 || (uint)column.ColumnId >= range.ColCount)
            return null;

        var columnOffset = (uint)column.ColumnId;
        if (column.Values.Count > 0 || column.IncludeBlank)
            return new FilterCommand(sheetId, range, columnOffset, IncludeBlank(column.Values, column.IncludeBlank));

        if (column.ColorFilter is { } colorFilter)
            return TryCreatePersistedColorCommand(sheetId, range, columnOffset, colorFilter);

        var worksheetFilters = column.CustomFilters
            .Select(filter => new WorksheetAutoFilterCustomFilterModel(
                filter.Operator,
                filter.Value,
                filter.NativeAttributes))
            .ToList();
        if (CustomFilterModelReconstructor.Reconstruct(worksheetFilters, column.CustomFiltersAnd) is { } criterion)
            return new FilterConditionCommand(sheetId, range, columnOffset, criterion);

        return null;
    }

    private static IWorkbookCommand? TryCreatePersistedColorCommand(
        SheetId sheetId,
        GridRange range,
        uint columnOffset,
        WorksheetAutoFilterColorFilterModel colorFilter)
    {
        if (colorFilter.CellColor)
        {
            return colorFilter.Color is { } fillColor
                ? new CellFillColorFilterCommand(sheetId, range, columnOffset, fillColor)
                : new CellNoFillColorFilterCommand(sheetId, range, columnOffset);
        }

        return colorFilter.Color is { } fontColor
            ? new CellFontColorFilterCommand(sheetId, range, columnOffset, fontColor)
            : null;
    }

    private static IReadOnlyList<string> IncludeBlank(IReadOnlyList<string> values, bool includeBlank) =>
        includeBlank && !values.Contains("")
            ? [.. values, ""]
            : values;

    private static bool TryGetAverageDirection(string? type, out bool above)
    {
        above = true;
        if (string.Equals(type, "aboveAverage", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.Equals(type, "belowAverage", StringComparison.OrdinalIgnoreCase))
            return false;

        above = false;
        return true;
    }

    private static uint ClampToUInt(double value) =>
        value <= 0
            ? 0
            : value >= uint.MaxValue
                ? uint.MaxValue
                : (uint)value;
}
