using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class AdvancedFilterCommand : IWorkbookCommand
{
    public const uint MaxListRows = 100_000;
    public const uint MaxListColumns = 256;
    public const long MaxListCells = 2_000_000;
    public const uint MaxCriteriaRows = 1_000;
    public const uint MaxCriteriaColumns = 64;
    public const long MaxCriteriaCells = 64_000;
    public const ulong MaxCopyOutputCells = 1_000_000;

    public const string ListRangeTooLargeMessage =
        "Advanced Filter list range is too large. Select a bounded list range with at most 100,000 rows, 256 columns, and 2,000,000 cells.";

    public const string CriteriaRangeTooLargeMessage =
        "Advanced Filter criteria range is too large. Select a bounded criteria range with at most 1,000 rows, 64 columns, and 64,000 cells.";

    public const string CopyOutputTooLargeMessage =
        "Advanced Filter copy output is too large. Select a smaller list range or copy fewer columns.";

    private readonly GridRange _listRange;
    private readonly GridRange _criteriaRange;
    private readonly CellAddress? _copyTo;
    private readonly GridRange? _copyToRange;
    private readonly bool _uniqueRecordsOnly;
    private HashSet<uint>? _previousFilterHiddenRows;
    private List<(CellAddress Address, Cell? OldCell)>? _copySnapshot;

    public string Label => "Advanced Filter";

    public AdvancedFilterCommand(
        GridRange ListRange,
        GridRange CriteriaRange,
        CellAddress? CopyTo,
        bool UniqueRecordsOnly,
        GridRange? CopyToRange = null)
    {
        _listRange = ListRange;
        _criteriaRange = CriteriaRange;
        _copyTo = CopyTo;
        _copyToRange = CopyToRange;
        _uniqueRecordsOnly = UniqueRecordsOnly;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (ValidateRangeShape(_listRange, _criteriaRange, _copyTo, _copyToRange) is { } invalidRange)
            return invalidRange;

        var sheet = ctx.Workbook.GetSheet(_listRange.Start.Sheet);
        if (sheet is null)
            return new CommandOutcome(false, "Advanced Filter list range must belong to this workbook.");

        var criteriaSheet = ctx.Workbook.GetSheet(_criteriaRange.Start.Sheet);
        if (criteriaSheet is null)
            return new CommandOutcome(false, "Advanced Filter criteria range must belong to this workbook.");

        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.UseAutoFilter) is { } protectedOutcome)
            return protectedOutcome;

        var headers = AdvancedFilterPlanBuilder.BuildHeaderMap(sheet, _listRange);
        var criteria = AdvancedFilterPlanBuilder.BuildCriteriaRows(criteriaSheet, _criteriaRange, headers, _listRange);
        if (criteria.Error is not null)
            return new CommandOutcome(false, criteria.Error);
        if (criteria.Rows.Count == 0)
            return new CommandOutcome(false, "Advanced Filter requires at least one criterion.");

        var matches = AdvancedFilterPlanBuilder.MatchingRows(sheet, _listRange, criteria.Rows, _uniqueRecordsOnly);

        _previousFilterHiddenRows = null;
        _copySnapshot = null;

        if (_copyTo is null)
        {
            _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];
            var matchedRows = new HashSet<uint>(matches);
            for (var row = _listRange.Start.Row + 1; row <= _listRange.End.Row; row++)
            {
                sheet.FilterHiddenRows.Remove(row);
                if (!matchedRows.Contains(row))
                    sheet.FilterHiddenRows.Add(row);
            }
            return new CommandOutcome(true);
        }

        if (_copyTo.Value.Sheet != sheet.Id)
            return new CommandOutcome(false, "Copy destination must be on the filtered sheet.");
        var copyPlan = CreateCopyOutputPlan(sheet, matches.Count, headers);
        if (RejectInvalidCopyOutput(copyPlan) is { } invalidCopyOutput)
            return invalidCopyOutput;
        if (GetLockedCopyDestination(ctx.Workbook, sheet, copyPlan) is { } lockedDestination)
            return lockedDestination;

        CopyMatches(sheet, matches, copyPlan);
        // Report both corners of the copy-to range, not just its top-left. Undo/Redo re-select the
        // BOUNDING range of AffectedCells, so a single cell collapsed the restored selection to the
        // destination corner while running the command forward selected the whole copy-to range --
        // undo visibly shrank the selection. Reporting the range the user nominated keeps both
        // directions on the same selection.
        return _copyToRange is { } copyToRange
            ? new CommandOutcome(true, AffectedCells: [copyToRange.Start, copyToRange.End])
            : new CommandOutcome(true, AffectedCells: [_copyTo.Value]);
    }

    public static bool IsListRangeWithinSupportedBounds(GridRange range) =>
        range.RowCount <= MaxListRows &&
        range.ColCount <= MaxListColumns &&
        range.CellCount <= MaxListCells;

    public static bool IsCriteriaRangeWithinSupportedBounds(GridRange range) =>
        range.RowCount <= MaxCriteriaRows &&
        range.ColCount <= MaxCriteriaColumns &&
        range.CellCount <= MaxCriteriaCells;

    public static CommandOutcome? ValidateRangeShape(
        GridRange listRange,
        GridRange criteriaRange,
        CellAddress? copyTo,
        GridRange? copyToRange = null)
    {
        if (listRange.Start.Sheet != listRange.End.Sheet ||
            criteriaRange.Start.Sheet != criteriaRange.End.Sheet)
            return new CommandOutcome(false, "Advanced Filter list and criteria ranges must each stay on one sheet.");

        if (!WorksheetBounds.IsValidAddress(listRange.Start) ||
            !WorksheetBounds.IsValidAddress(listRange.End))
            return new CommandOutcome(false, "Advanced Filter list range is outside the worksheet bounds.");

        if (!WorksheetBounds.IsValidAddress(criteriaRange.Start) ||
            !WorksheetBounds.IsValidAddress(criteriaRange.End))
            return new CommandOutcome(false, "Advanced Filter criteria range is outside the worksheet bounds.");

        if (listRange.RowCount < 2)
            return new CommandOutcome(false, "Advanced Filter list range must include headers and at least one data row.");

        if (criteriaRange.RowCount < 2)
            return new CommandOutcome(false, "Advanced Filter criteria range must include headers and at least one criteria row.");

        if (!IsListRangeWithinSupportedBounds(listRange))
            return new CommandOutcome(false, ListRangeTooLargeMessage);

        if (!IsCriteriaRangeWithinSupportedBounds(criteriaRange))
            return new CommandOutcome(false, CriteriaRangeTooLargeMessage);

        if (copyTo is not { } destination)
            return null;

        if (!WorksheetBounds.IsValidAddress(destination))
            return new CommandOutcome(false, "Advanced Filter copy destination is outside the worksheet bounds.");

        if (destination.Sheet != listRange.Start.Sheet)
            return new CommandOutcome(false, "Copy destination must be on the filtered sheet.");

        if (copyToRange is not { } range)
            return null;

        if (range.Start.Sheet != destination.Sheet || range.End.Sheet != destination.Sheet)
            return new CommandOutcome(false, "Advanced Filter copy-to range must be on the filtered sheet.");

        if (!WorksheetBounds.IsValidAddress(range.Start) ||
            !WorksheetBounds.IsValidAddress(range.End))
            return new CommandOutcome(false, "Advanced Filter copy-to range is outside the worksheet bounds.");

        if (range.Start.Row != range.End.Row)
            return new CommandOutcome(false, "Advanced Filter copy-to range must be one row.");

        if (range.ColCount > MaxListColumns)
            return new CommandOutcome(false, CopyOutputTooLargeMessage);

        return null;
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_listRange.Start.Sheet);
        if (_previousFilterHiddenRows is not null)
        {
            sheet.FilterHiddenRows.Clear();
            sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);
        }

        if (_copySnapshot is not null)
        {
            foreach (var (address, oldCell) in _copySnapshot)
            {
                if (oldCell is null)
                    sheet.ClearCell(address);
                else
                    sheet.SetCell(address, oldCell.Clone());
            }
        }
    }

    private void CopyMatches(Sheet sheet, IReadOnlyList<uint> rows, CopyOutputPlan plan)
    {
        _copySnapshot = plan.DestinationOverlapsSource
            ? CreateCopySnapshot(plan.RowsToReplace, plan.ClearWidth)
            : [];

        for (uint r = 0; r < plan.RowsToReplace; r++)
        {
            for (uint c = 0; c < plan.ClearWidth; c++)
            {
                var target = new CellAddress(sheet.Id, _copyTo!.Value.Row + r, _copyTo.Value.Col + c);
                if (plan.DestinationOverlapsSource || r >= plan.OutputRowCount || c >= plan.OutputWidth)
                {
                    SnapshotCopyTarget(sheet, target);
                    sheet.ClearCell(target);
                }
            }
        }

        for (uint r = 0; r < plan.OutputRowCount; r++)
        {
            for (uint c = 0; c < plan.OutputWidth; c++)
            {
                var target = new CellAddress(sheet.Id, _copyTo!.Value.Row + r, _copyTo.Value.Col + c);
                var sourceRow = r == 0 ? _listRange.Start.Row : rows[(int)r - 1];
                var sourceCol = plan.OutputColumns is null ? _listRange.Start.Col + c : plan.OutputColumns[(int)c];
                var source = new CellAddress(sheet.Id, sourceRow, sourceCol);
                var sourceCell = sheet.GetCell(source);
                if (!plan.DestinationOverlapsSource &&
                    sourceCell is not null &&
                    sheet.GetCell(target) is { } existingTarget &&
                    CellsHaveSameContent(existingTarget, sourceCell))
                {
                    continue;
                }

                var cellToCopy = sourceCell?.Clone()
                    ?? Cell.FromValue(sheet.GetValue(source.Row, source.Col));
                if (!plan.DestinationOverlapsSource &&
                    sourceCell is null &&
                    sheet.GetCell(target) is { } existingBlankTarget &&
                    CellsHaveSameContent(existingBlankTarget, cellToCopy))
                {
                    continue;
                }

                if (!plan.DestinationOverlapsSource)
                    SnapshotCopyTarget(sheet, target);

                sheet.SetCell(target, cellToCopy);
            }
        }
    }

    private void SnapshotCopyTarget(Sheet sheet, CellAddress target)
    {
        _copySnapshot!.Add((target, sheet.GetCell(target)?.Clone()));
    }

    private CommandOutcome? GetLockedCopyDestination(Workbook workbook, Sheet sheet, CopyOutputPlan plan)
    {
        if (!sheet.IsProtected)
            return null;

        for (uint r = 0; r < plan.ProtectionCheckRowsToReplace; r++)
        {
            for (uint c = 0; c < plan.ProtectionCheckWidth; c++)
            {
                var target = new CellAddress(sheet.Id, _copyTo!.Value.Row + r, _copyTo.Value.Col + c);
                if (!CommandGuards.CanEditCell(workbook, sheet, target))
                    return CommandGuards.RejectSheetProtected();
            }
        }

        return null;
    }

    private CopyOutputPlan CreateCopyOutputPlan(
        Sheet sheet,
        int outputRows,
        IReadOnlyDictionary<string, uint> headers)
    {
        var outputColumns = ResolveCopyOutputColumns(sheet, headers);
        var outputWidth = outputColumns is null ? _listRange.ColCount : (uint)outputColumns.Count;
        var clearWidth = Math.Max(_listRange.ColCount, outputWidth);
        var rowsToReplace = CountCopyRowsToReplace(sheet, outputRows, clearWidth);
        var outputRowCount = (uint)outputRows + 1;
        var destinationOverlapsSource = CopyDestinationOverlapsListRange(rowsToReplace, clearWidth);
        var protectionCheckWidth = _listRange.ColCount;
        var protectionCheckRowsToReplace = protectionCheckWidth == clearWidth
            ? rowsToReplace
            : CountCopyRowsToReplace(sheet, outputRows, protectionCheckWidth);

        return new CopyOutputPlan(
            outputColumns,
            outputWidth,
            clearWidth,
            rowsToReplace,
            outputRowCount,
            destinationOverlapsSource,
            protectionCheckWidth,
            protectionCheckRowsToReplace);
    }

    private uint CountCopyRowsToReplace(Sheet sheet, int outputRows, uint clearWidth)
    {
        if (_copyTo is null)
            return 0;

        return Math.Max((uint)outputRows + 1, CountExistingDestinationRows(sheet, clearWidth));
    }

    private bool CopyDestinationOverlapsListRange(uint rowsToReplace, uint clearWidth)
    {
        if (_copyTo is null || rowsToReplace == 0 || clearWidth == 0)
            return false;

        var targetStartRow = _copyTo.Value.Row;
        var targetEndRow = (ulong)targetStartRow + rowsToReplace - 1;
        var targetStartCol = _copyTo.Value.Col;
        var targetEndCol = (ulong)targetStartCol + clearWidth - 1;

        return targetStartRow <= _listRange.End.Row &&
               targetEndRow >= _listRange.Start.Row &&
               targetStartCol <= _listRange.End.Col &&
               targetEndCol >= _listRange.Start.Col;
    }

    private static List<(CellAddress Address, Cell? OldCell)> CreateCopySnapshot(uint rowsToReplace, uint clearWidth)
    {
        var targetCount = (ulong)rowsToReplace * clearWidth;
        return targetCount <= int.MaxValue
            ? new List<(CellAddress Address, Cell? OldCell)>((int)targetCount)
            : [];
    }

    private CommandOutcome? RejectInvalidCopyOutput(CopyOutputPlan plan)
    {
        if (_copyTo is not { } destination)
            return null;

        if (!WorksheetBounds.TryGetRectangleEnd(destination, plan.RowsToReplace, plan.ClearWidth, out _))
            return new CommandOutcome(false, "Advanced Filter copy output would exceed the worksheet bounds.");

        if (ExceedsCellBudget(plan.RowsToReplace, plan.ClearWidth, MaxCopyOutputCells) ||
            ExceedsCellBudget(plan.OutputRowCount, plan.OutputWidth, MaxCopyOutputCells))
        {
            return new CommandOutcome(false, CopyOutputTooLargeMessage);
        }

        return null;
    }

    private static bool ExceedsCellBudget(uint rows, uint columns, ulong budget) =>
        columns != 0 && rows > budget / columns;

    private static bool CellsHaveSameContent(Cell left, Cell right)
    {
        return Equals(left.Value, right.Value) &&
               string.Equals(left.FormulaText, right.FormulaText, StringComparison.Ordinal) &&
               left.IgnoreFormulaError == right.IgnoreFormulaError &&
               left.StyleId == right.StyleId &&
               Equals(left.CachedAst, right.CachedAst);
    }

    private uint CountExistingDestinationRows(Sheet sheet, uint outputWidth)
    {
        if (_copyTo is null)
            return 0;

        if (sheet.GetUsedRange() is not { } usedRange || _copyTo.Value.Row > usedRange.End.Row)
            return 0;

        uint count = 0;
        for (var row = _copyTo.Value.Row; row <= usedRange.End.Row; row++)
        {
            var hasOutputCell = false;
            for (uint colOffset = 0; colOffset < outputWidth; colOffset++)
            {
                if (sheet.GetCell(row, _copyTo.Value.Col + colOffset) is null)
                    continue;

                hasOutputCell = true;
                break;
            }

            if (!hasOutputCell)
                break;

            count++;
        }

        return count;
    }

    private IReadOnlyList<uint>? ResolveCopyOutputColumns(Sheet sheet, IReadOnlyDictionary<string, uint> headers)
    {
        if (_copyToRange is not { } range || range.Start.Row != range.End.Row)
            return null;

        var selectedColumns = new List<uint>();
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var headerText = FilterValueFormatter.ToText(sheet.GetValue(range.Start.Row, col));
            if (string.IsNullOrWhiteSpace(headerText))
                return null;
            if (!headers.TryGetValue(headerText, out var sourceCol))
                return null;

            selectedColumns.Add(sourceCol);
        }

        return selectedColumns.Count == 0 ? null : selectedColumns;
    }

    private readonly record struct CopyOutputPlan(
        IReadOnlyList<uint>? OutputColumns,
        uint OutputWidth,
        uint ClearWidth,
        uint RowsToReplace,
        uint OutputRowCount,
        bool DestinationOverlapsSource,
        uint ProtectionCheckWidth,
        uint ProtectionCheckRowsToReplace);

}
