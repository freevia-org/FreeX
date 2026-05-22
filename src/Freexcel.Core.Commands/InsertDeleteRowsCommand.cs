using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Inserts <paramref name="count"/> blank rows before <paramref name="beforeRow"/>.</summary>
public sealed class InsertRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _beforeRow;
    private readonly uint _count;
    private List<(CellAddress Addr, Cell Cell)>? _movedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private Dictionary<uint, double>? _rowHeightSnapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo)>? _conditionalFormatSnapshot;
    private Dictionary<string, GridRange>? _namedRangeSnapshot;
    private GridRange? _printAreaSnapshot;
    private List<uint>? _rowPageBreakSnapshot;
    private List<GridRange>? _chartSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];

    public string Label => $"Insert {_count} Row(s)";

    public InsertRowsCommand(SheetId sheetId, uint beforeRow, uint count = 1)
    {
        _sheetId   = sheetId;
        _beforeRow = beforeRow;
        _count     = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var maxOccupied = sheet.EnumerateCells()
            .Where(p => p.Address.Row >= _beforeRow)
            .Select(p => p.Address.Row)
            .DefaultIfEmpty(0u)
            .Max();
        if (maxOccupied > 0 && maxOccupied + _count > Model.CellAddress.MaxRow)
            return new CommandOutcome(false,
                ErrorMessage: $"Cannot insert {_count} row(s): data would be pushed past the last row ({Model.CellAddress.MaxRow}).");

        _movedSnapshot = sheet.EnumerateCells()
            .Where(p => p.Address.Row >= _beforeRow)
            .Select(p => (p.Address, p.Cell.Clone()))
            .ToList();

        foreach (var (addr, _) in _movedSnapshot.OrderByDescending(p => p.Addr.Row))
            sheet.ClearCell(addr);

        foreach (var (addr, cell) in _movedSnapshot)
            sheet.SetCell(new CellAddress(addr.Sheet, addr.Row + _count, addr.Col), cell.Clone());

        var hiddenToShift = sheet.HiddenRows.Where(r => r >= _beforeRow).ToList();
        foreach (var r in hiddenToShift) sheet.HiddenRows.Remove(r);
        foreach (var r in hiddenToShift) sheet.HiddenRows.Add(r + _count);

        var filterHiddenToShift = sheet.FilterHiddenRows.Where(r => r >= _beforeRow).ToList();
        foreach (var r in filterHiddenToShift) sheet.FilterHiddenRows.Remove(r);
        foreach (var r in filterHiddenToShift) sheet.FilterHiddenRows.Add(r + _count);

        _rowHeightSnapshot = new Dictionary<uint, double>(sheet.RowHeights);
        ShiftIndexesUp(sheet.RowHeights, _beforeRow, _count);

        _commentSnapshot = new Dictionary<CellAddress, string>(sheet.Comments);
        ShiftCommentRowsUp(sheet.Comments, _beforeRow, _count);
        _threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>(sheet.ThreadedComments);
        ShiftCommentRowsUp(sheet.ThreadedComments, _beforeRow, _count);
        _hyperlinkSnapshot = new Dictionary<CellAddress, string>(sheet.Hyperlinks);
        ShiftCommentRowsUp(sheet.Hyperlinks, _beforeRow, _count);
        _hyperlinkMetadataSnapshot = new Dictionary<CellAddress, HyperlinkMetadata>(sheet.HyperlinkMetadata);
        ShiftCommentRowsUp(sheet.HyperlinkMetadata, _beforeRow, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = CaptureRuleRanges(sheet);
        ShiftRuleRowsUp(sheet, _beforeRow, _count);
        _namedRangeSnapshot = CaptureNamedRanges(ctx.Workbook);
        ShiftNamedRangeRowsUp(ctx.Workbook, _sheetId, _beforeRow, _count);
        _printAreaSnapshot = sheet.PrintArea;
        ShiftPrintAreaRowsUp(sheet, _beforeRow, _count);
        _rowPageBreakSnapshot = sheet.RowPageBreaks.ToList();
        ShiftSortedSetUp(sheet.RowPageBreaks, _beforeRow, _count);
        _chartSnapshot = CaptureChartDataRanges(sheet);
        ShiftChartRowsUp(sheet, _sheetId, _beforeRow, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        var shiftedMerges = sheet.MergedRegions.Select(m =>
        {
            if (m.Start.Row >= _beforeRow)
                return new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row + _count, m.Start.Col),
                    new CellAddress(m.End.Sheet,   m.End.Row   + _count, m.End.Col));
            if (m.End.Row >= _beforeRow)
                return new GridRange(
                    m.Start,
                    new CellAddress(m.End.Sheet, m.End.Row + _count, m.End.Col));
            return m;
        }).ToList();
        sheet.ReplaceMergedRegions(shiftedMerges);

        _formulaSnapshot.Clear();
        RewriteAllFormulas(ctx.Workbook, new InsertRowsOp(sheet.Name, _beforeRow, _count), _formulaSnapshot);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_movedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var (addr, _) in _movedSnapshot)
            sheet.ClearCell(new CellAddress(addr.Sheet, addr.Row + _count, addr.Col));

        foreach (var (addr, cell) in _movedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        var shifted = sheet.HiddenRows.Where(r => r >= _beforeRow + _count).ToList();
        foreach (var r in shifted) sheet.HiddenRows.Remove(r);
        foreach (var r in shifted) sheet.HiddenRows.Add(r - _count);

        var filterShifted = sheet.FilterHiddenRows.Where(r => r >= _beforeRow + _count).ToList();
        foreach (var r in filterShifted) sheet.FilterHiddenRows.Remove(r);
        foreach (var r in filterShifted) sheet.FilterHiddenRows.Add(r - _count);

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        RestoreDictionary(sheet.RowHeights, _rowHeightSnapshot);
        RestoreDictionary(sheet.Comments, _commentSnapshot);
        RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        RestoreRuleRanges(_dataValidationSnapshot, _conditionalFormatSnapshot);
        RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        sheet.PrintArea = _printAreaSnapshot;
        RestoreSortedSet(sheet.RowPageBreaks, _rowPageBreakSnapshot);
        RestoreChartDataRanges(sheet, _chartSnapshot);
    }

    internal static void RewriteAllFormulas(
        Workbook workbook, RewriteOperation op, Dictionary<CellAddress, string> snapshot)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (addr, cell) in sheet.EnumerateCells())
            {
                if (cell.FormulaText is null) continue;
                var rewritten = FormulaRewriter.Rewrite(cell.FormulaText, op, sheet.Name);
                if (rewritten is null) continue;
                snapshot[addr] = cell.FormulaText;
                cell.FormulaText = rewritten;
            }
        }
    }

    internal static void RestoreFormulas(
        Workbook workbook, Dictionary<CellAddress, string> snapshot)
    {
        foreach (var (addr, original) in snapshot)
        {
            var s = workbook.GetSheet(addr.Sheet);
            var cell = s?.GetCell(addr.Row, addr.Col);
            if (cell is not null)
                cell.FormulaText = original;
        }
        snapshot.Clear();
    }

    internal static void ShiftIndexesUp(Dictionary<uint, double> values, uint start, uint count)
    {
        var shifted = values
            .Where(p => p.Key >= start)
            .OrderByDescending(p => p.Key)
            .ToList();

        foreach (var (key, _) in shifted)
            values.Remove(key);
        foreach (var (key, value) in shifted)
            values[key + count] = value;
    }

    internal static void ShiftIndexesDown(Dictionary<uint, double> values, uint start, uint count)
    {
        var end = start + count - 1;
        var shifted = values
            .Where(p => p.Key > end)
            .OrderBy(p => p.Key)
            .ToList();
        var removed = values.Keys.Where(key => key >= start && key <= end).ToList();

        foreach (var key in removed)
            values.Remove(key);
        foreach (var (key, _) in shifted)
            values.Remove(key);
        foreach (var (key, value) in shifted)
            values[key - count] = value;
    }

    internal static void ShiftSortedSetUp(SortedSet<uint> values, uint start, uint count)
    {
        var shifted = values.Where(value => value >= start).OrderByDescending(value => value).ToList();
        foreach (var value in shifted)
            values.Remove(value);
        foreach (var value in shifted)
            values.Add(value + count);
    }

    internal static void ShiftSortedSetDown(SortedSet<uint> values, uint start, uint count)
    {
        var end = start + count - 1;
        var removed = values.Where(value => value >= start && value <= end).ToList();
        var shifted = values.Where(value => value > end).OrderBy(value => value).ToList();

        foreach (var value in removed)
            values.Remove(value);
        foreach (var value in shifted)
            values.Remove(value);
        foreach (var value in shifted)
            values.Add(value - count);
    }

    internal static void RestoreSortedSet(SortedSet<uint> target, IReadOnlyCollection<uint>? snapshot)
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var value in snapshot)
            target.Add(value);
    }

    internal static void RestoreDictionary(Dictionary<uint, double> target, Dictionary<uint, double>? snapshot)
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var (key, value) in snapshot)
            target[key] = value;
    }

    internal static void RestoreSet(HashSet<uint> target, HashSet<uint>? snapshot)
    {
        if (snapshot is null)
            return;

        target.Clear();
        target.UnionWith(snapshot);
    }

    internal static void RestoreDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        Dictionary<TKey, TValue>? snapshot)
        where TKey : notnull
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var (key, value) in snapshot)
            target[key] = value;
    }

    internal static void ShiftCommentRowsUp<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var shifted = comments
            .Where(p => p.Key.Row >= start)
            .OrderByDescending(p => p.Key.Row)
            .ToList();

        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row + count, addr.Col)] = comment;
    }

    internal static void ShiftCommentRowsDown<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var end = start + count - 1;
        var removed = comments.Keys.Where(addr => addr.Row >= start && addr.Row <= end).ToList();
        var shifted = comments
            .Where(p => p.Key.Row > end)
            .OrderBy(p => p.Key.Row)
            .ToList();

        foreach (var addr in removed)
            comments.Remove(addr);
        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row - count, addr.Col)] = comment;
    }

    internal static void ShiftCommentColumnsUp<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var shifted = comments
            .Where(p => p.Key.Col >= start)
            .OrderByDescending(p => p.Key.Col)
            .ToList();

        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row, addr.Col + count)] = comment;
    }

    internal static void ShiftCommentColumnsDown<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var end = start + count - 1;
        var removed = comments.Keys.Where(addr => addr.Col >= start && addr.Col <= end).ToList();
        var shifted = comments
            .Where(p => p.Key.Col > end)
            .OrderBy(p => p.Key.Col)
            .ToList();

        foreach (var addr in removed)
            comments.Remove(addr);
        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row, addr.Col - count)] = comment;
    }

    internal static (
        List<(DataValidation Rule, GridRange AppliesTo)> DataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo)> ConditionalFormats)
        CaptureRuleRanges(Sheet sheet)
    {
        return (
            sheet.DataValidations.Select(rule => (rule, rule.AppliesTo)).ToList(),
            sheet.ConditionalFormats.Select(rule => (rule, rule.AppliesTo)).ToList());
    }

    internal static void RestoreRuleRanges(
        List<(DataValidation Rule, GridRange AppliesTo)>? dataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo)>? conditionalFormats)
    {
        if (dataValidations is not null)
            foreach (var (rule, appliesTo) in dataValidations)
                rule.AppliesTo = appliesTo;

        if (conditionalFormats is not null)
            foreach (var (rule, appliesTo) in conditionalFormats)
                rule.AppliesTo = appliesTo;
    }

    // Full rebuild variant: used when rules may have been removed (e.g. DeleteRows/DeleteColumns).
    internal static void RestoreRuleRanges(
        Sheet sheet,
        List<(DataValidation Rule, GridRange AppliesTo)>? dataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo)>? conditionalFormats)
    {
        if (dataValidations is not null)
        {
            sheet.DataValidations.Clear();
            foreach (var (rule, appliesTo) in dataValidations)
            {
                rule.AppliesTo = appliesTo;
                sheet.DataValidations.Add(rule);
            }
        }
        if (conditionalFormats is not null)
        {
            sheet.ConditionalFormats.Clear();
            foreach (var (rule, appliesTo) in conditionalFormats)
            {
                rule.AppliesTo = appliesTo;
                sheet.ConditionalFormats.Add(rule);
            }
        }
    }

    internal static void ShiftRuleRowsUp(Sheet sheet, uint start, uint count)
    {
        foreach (var rule in sheet.DataValidations)
            rule.AppliesTo = ShiftRangeRowsUp(rule.AppliesTo, start, count);
        foreach (var rule in sheet.ConditionalFormats)
            rule.AppliesTo = ShiftRangeRowsUp(rule.AppliesTo, start, count);
    }

    internal static void ShiftRuleRowsDown(Sheet sheet, uint start, uint count)
    {
        for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeRowsDown(sheet.DataValidations[i].AppliesTo, start, count);
            if (shifted is null) sheet.DataValidations.RemoveAt(i);
            else sheet.DataValidations[i].AppliesTo = shifted.Value;
        }
        for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeRowsDown(sheet.ConditionalFormats[i].AppliesTo, start, count);
            if (shifted is null) sheet.ConditionalFormats.RemoveAt(i);
            else sheet.ConditionalFormats[i].AppliesTo = shifted.Value;
        }
    }

    internal static void ShiftRuleColumnsUp(Sheet sheet, uint start, uint count)
    {
        foreach (var rule in sheet.DataValidations)
            rule.AppliesTo = ShiftRangeColumnsUp(rule.AppliesTo, start, count);
        foreach (var rule in sheet.ConditionalFormats)
            rule.AppliesTo = ShiftRangeColumnsUp(rule.AppliesTo, start, count);
    }

    internal static void ShiftRuleColumnsDown(Sheet sheet, uint start, uint count)
    {
        for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeColumnsDown(sheet.DataValidations[i].AppliesTo, start, count);
            if (shifted is null) sheet.DataValidations.RemoveAt(i);
            else sheet.DataValidations[i].AppliesTo = shifted.Value;
        }
        for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeColumnsDown(sheet.ConditionalFormats[i].AppliesTo, start, count);
            if (shifted is null) sheet.ConditionalFormats.RemoveAt(i);
            else sheet.ConditionalFormats[i].AppliesTo = shifted.Value;
        }
    }

    internal static Dictionary<string, GridRange> CaptureNamedRanges(Workbook workbook) =>
        workbook.NamedRanges.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    internal static void RestoreNamedRanges(Workbook workbook, Dictionary<string, GridRange>? snapshot)
    {
        if (snapshot is null)
            return;

        workbook.NamedRanges.Clear();
        foreach (var (name, range) in snapshot)
            workbook.NamedRanges[name] = range;
    }

    internal static void ShiftNamedRangeRowsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
                workbook.NamedRanges[name] = ShiftRangeRowsUp(range, start, count);
        }
    }

    internal static void ShiftNamedRangeRowsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeRowsDown(range, start, count);
            if (shifted is null) workbook.NamedRanges.Remove(name);
            else workbook.NamedRanges[name] = shifted.Value;
        }
    }

    internal static void ShiftNamedRangeColumnsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
                workbook.NamedRanges[name] = ShiftRangeColumnsUp(range, start, count);
        }
    }

    internal static void ShiftNamedRangeColumnsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeColumnsDown(range, start, count);
            if (shifted is null) workbook.NamedRanges.Remove(name);
            else workbook.NamedRanges[name] = shifted.Value;
        }
    }

    internal static void ShiftPrintAreaRowsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeRowsUp(printArea, start, count);
    }

    internal static void ShiftPrintAreaRowsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeRowsDown(printArea, start, count);  // null clears the print area
    }

    internal static void ShiftPrintAreaColumnsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeColumnsUp(printArea, start, count);
    }

    internal static void ShiftPrintAreaColumnsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeColumnsDown(printArea, start, count);  // null clears the print area
    }

    // ── Chart data range helpers ──────────────────────────────────────────────

    internal static List<GridRange> CaptureChartDataRanges(Sheet sheet) =>
        sheet.Charts.Select(c => c.DataRange).ToList();

    internal static void RestoreChartDataRanges(Sheet sheet, List<GridRange>? snapshot)
    {
        if (snapshot is null) return;
        for (int i = 0; i < sheet.Charts.Count && i < snapshot.Count; i++)
            sheet.Charts[i].DataRange = snapshot[i];
    }

    internal static void ShiftChartRowsUp(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeRowsUp(chart.DataRange, start, count);
    }

    internal static void ShiftChartRowsDown(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeRowsDown(chart.DataRange, start, count) ?? chart.DataRange;
    }

    internal static void ShiftChartColumnsUp(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeColumnsUp(chart.DataRange, start, count);
    }

    internal static void ShiftChartColumnsDown(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeColumnsDown(chart.DataRange, start, count) ?? chart.DataRange;
    }

    private static GridRange ShiftRangeRowsUp(GridRange range, uint start, uint count)
    {
        if (range.End.Row < start)
            return range;

        var newStartRow = range.Start.Row >= start ? range.Start.Row + count : range.Start.Row;
        var newEndRow = range.End.Row + count;
        return new GridRange(
            new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
            new CellAddress(range.End.Sheet, newEndRow, range.End.Col));
    }

    private static GridRange? ShiftRangeRowsDown(GridRange range, uint start, uint count)
    {
        var end = start + count - 1;
        if (range.End.Row < start)
            return range;    // entirely above: unchanged
        if (range.Start.Row > end)
        {
            return new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row - count, range.Start.Col),
                new CellAddress(range.End.Sheet, range.End.Row - count, range.End.Col));
        }

        // overlapping — compute surviving portion
        var newStartRow = range.Start.Row < start ? range.Start.Row : start;
        // If the range end is inside the deletion zone, the last surviving row is start-1.
        // If entirely within the deletion zone (start == newStartRow), nothing survives.
        var newEndRow = range.End.Row > end ? range.End.Row - count : start - 1;
        if (newStartRow == start && newEndRow < start)
            return null;   // range was entirely within the deleted rows
        return new GridRange(
            new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
            new CellAddress(range.End.Sheet, newEndRow, range.End.Col));
    }

    private static GridRange ShiftRangeColumnsUp(GridRange range, uint start, uint count)
    {
        if (range.End.Col < start)
            return range;

        var newStartCol = range.Start.Col >= start ? range.Start.Col + count : range.Start.Col;
        var newEndCol = range.End.Col + count;
        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet, range.End.Row, newEndCol));
    }

    private static GridRange? ShiftRangeColumnsDown(GridRange range, uint start, uint count)
    {
        var end = start + count - 1;
        if (range.End.Col < start)
            return range;    // entirely left: unchanged
        if (range.Start.Col > end)
        {
            return new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col - count),
                new CellAddress(range.End.Sheet, range.End.Row, range.End.Col - count));
        }

        // overlapping — compute surviving portion
        var newStartCol = range.Start.Col < start ? range.Start.Col : start;
        var newEndCol = range.End.Col > end ? range.End.Col - count : start - 1;
        if (newStartCol == start && newEndCol < start)
            return null;   // range was entirely within the deleted columns
        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet, range.End.Row, newEndCol));
    }
}

/// <summary>Deletes <paramref name="count"/> rows starting at <paramref name="startRow"/>.</summary>
public sealed class DeleteRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _startRow;
    private readonly uint _count;
    private List<(CellAddress Addr, Cell Cell)>? _deletedSnapshot;
    private List<(CellAddress Addr, Cell Cell)>? _shiftedSnapshot;
    private List<GridRange>? _mergeSnapshot;
    private Dictionary<uint, double>? _rowHeightSnapshot;
    private HashSet<uint>? _hiddenRowsSnapshot;
    private HashSet<uint>? _filterHiddenRowsSnapshot;
    private Dictionary<CellAddress, string>? _commentSnapshot;
    private Dictionary<CellAddress, ThreadedComment>? _threadedCommentSnapshot;
    private Dictionary<CellAddress, string>? _hyperlinkSnapshot;
    private Dictionary<CellAddress, HyperlinkMetadata>? _hyperlinkMetadataSnapshot;
    private List<(DataValidation Rule, GridRange AppliesTo)>? _dataValidationSnapshot;
    private List<(ConditionalFormat Rule, GridRange AppliesTo)>? _conditionalFormatSnapshot;
    private Dictionary<string, GridRange>? _namedRangeSnapshot;
    private GridRange? _printAreaSnapshot;
    private List<uint>? _rowPageBreakSnapshot;
    private List<GridRange>? _chartSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];

    public string Label => $"Delete {_count} Row(s)";

    public DeleteRowsCommand(SheetId sheetId, uint startRow, uint count = 1)
    {
        _sheetId  = sheetId;
        _startRow = startRow;
        _count    = count;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        uint endRow = _startRow + _count - 1;

        _deletedSnapshot = sheet.EnumerateCells()
            .Where(p => p.Address.Row >= _startRow && p.Address.Row <= endRow)
            .Select(p => (p.Address, p.Cell.Clone()))
            .ToList();
        _shiftedSnapshot = sheet.EnumerateCells()
            .Where(p => p.Address.Row > endRow)
            .Select(p => (p.Address, p.Cell.Clone()))
            .ToList();

        foreach (var (addr, _) in _deletedSnapshot)
            sheet.ClearCell(addr);

        foreach (var (addr, _) in _shiftedSnapshot.OrderBy(p => p.Addr.Row))
            sheet.ClearCell(addr);
        foreach (var (addr, cell) in _shiftedSnapshot)
            sheet.SetCell(new CellAddress(addr.Sheet, addr.Row - _count, addr.Col), cell.Clone());

        var inRangeHidden = sheet.HiddenRows.Where(r => r >= _startRow && r <= endRow).ToList();
        var belowHidden   = sheet.HiddenRows.Where(r => r > endRow).ToList();
        _hiddenRowsSnapshot = [.. sheet.HiddenRows];
        foreach (var r in inRangeHidden) sheet.HiddenRows.Remove(r);
        foreach (var r in belowHidden) { sheet.HiddenRows.Remove(r); sheet.HiddenRows.Add(r - _count); }

        var inRangeFilterHidden = sheet.FilterHiddenRows.Where(r => r >= _startRow && r <= endRow).ToList();
        var belowFilterHidden = sheet.FilterHiddenRows.Where(r => r > endRow).ToList();
        _filterHiddenRowsSnapshot = [.. sheet.FilterHiddenRows];
        foreach (var r in inRangeFilterHidden) sheet.FilterHiddenRows.Remove(r);
        foreach (var r in belowFilterHidden) { sheet.FilterHiddenRows.Remove(r); sheet.FilterHiddenRows.Add(r - _count); }

        _rowHeightSnapshot = new Dictionary<uint, double>(sheet.RowHeights);
        InsertRowsCommand.ShiftIndexesDown(sheet.RowHeights, _startRow, _count);

        _commentSnapshot = new Dictionary<CellAddress, string>(sheet.Comments);
        InsertRowsCommand.ShiftCommentRowsDown(sheet.Comments, _startRow, _count);
        _threadedCommentSnapshot = new Dictionary<CellAddress, ThreadedComment>(sheet.ThreadedComments);
        InsertRowsCommand.ShiftCommentRowsDown(sheet.ThreadedComments, _startRow, _count);
        _hyperlinkSnapshot = new Dictionary<CellAddress, string>(sheet.Hyperlinks);
        InsertRowsCommand.ShiftCommentRowsDown(sheet.Hyperlinks, _startRow, _count);
        _hyperlinkMetadataSnapshot = new Dictionary<CellAddress, HyperlinkMetadata>(sheet.HyperlinkMetadata);
        InsertRowsCommand.ShiftCommentRowsDown(sheet.HyperlinkMetadata, _startRow, _count);

        (_dataValidationSnapshot, _conditionalFormatSnapshot) = InsertRowsCommand.CaptureRuleRanges(sheet);
        InsertRowsCommand.ShiftRuleRowsDown(sheet, _startRow, _count);
        _namedRangeSnapshot = InsertRowsCommand.CaptureNamedRanges(ctx.Workbook);
        InsertRowsCommand.ShiftNamedRangeRowsDown(ctx.Workbook, _sheetId, _startRow, _count);
        _printAreaSnapshot = sheet.PrintArea;
        InsertRowsCommand.ShiftPrintAreaRowsDown(sheet, _startRow, _count);
        _rowPageBreakSnapshot = sheet.RowPageBreaks.ToList();
        InsertRowsCommand.ShiftSortedSetDown(sheet.RowPageBreaks, _startRow, _count);
        _chartSnapshot = InsertRowsCommand.CaptureChartDataRanges(sheet);
        InsertRowsCommand.ShiftChartRowsDown(sheet, _sheetId, _startRow, _count);

        _mergeSnapshot = sheet.MergedRegions.ToList();
        var adjustedMerges = new List<GridRange>();
        foreach (var m in sheet.MergedRegions)
        {
            if (m.End.Row < _startRow)
            {
                adjustedMerges.Add(m); // entirely above
            }
            else if (m.Start.Row > endRow)
            {
                // entirely below — shift up
                adjustedMerges.Add(new GridRange(
                    new CellAddress(m.Start.Sheet, m.Start.Row - _count, m.Start.Col),
                    new CellAddress(m.End.Sheet,   m.End.Row   - _count, m.End.Col)));
            }
            else
            {
                // overlapping — shrink
                uint newStart = m.Start.Row < _startRow ? m.Start.Row : _startRow;
                uint newEnd   = m.End.Row   > endRow    ? m.End.Row - _count
                              : _startRow > 1           ? _startRow - 1 : 0;
                if (newEnd > 0 && newEnd >= newStart)
                {
                    adjustedMerges.Add(new GridRange(
                        new CellAddress(m.Start.Sheet, newStart, m.Start.Col),
                        new CellAddress(m.End.Sheet,   newEnd,   m.End.Col)));
                }
                // if newEnd < newStart the merge was entirely deleted — drop it
            }
        }
        sheet.ReplaceMergedRegions(adjustedMerges);

        _formulaSnapshot.Clear();
        InsertRowsCommand.RewriteAllFormulas(
            ctx.Workbook, new DeleteRowsOp(sheet.Name, _startRow, _count), _formulaSnapshot);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_deletedSnapshot is null || _shiftedSnapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        InsertRowsCommand.RestoreFormulas(ctx.Workbook, _formulaSnapshot);

        foreach (var (addr, _) in _shiftedSnapshot)
            sheet.ClearCell(new CellAddress(addr.Sheet, addr.Row - _count, addr.Col));

        foreach (var (addr, cell) in _shiftedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        foreach (var (addr, cell) in _deletedSnapshot)
            sheet.SetCell(addr, cell.Clone());

        if (_mergeSnapshot is not null)
            sheet.ReplaceMergedRegions(_mergeSnapshot);

        InsertRowsCommand.RestoreDictionary(sheet.RowHeights, _rowHeightSnapshot);
        InsertRowsCommand.RestoreSet(sheet.HiddenRows, _hiddenRowsSnapshot);
        InsertRowsCommand.RestoreSet(sheet.FilterHiddenRows, _filterHiddenRowsSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.Comments, _commentSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.ThreadedComments, _threadedCommentSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.Hyperlinks, _hyperlinkSnapshot);
        InsertRowsCommand.RestoreDictionary(sheet.HyperlinkMetadata, _hyperlinkMetadataSnapshot);
        // Full-rebuild overload: rules removed during deletion must be re-added here.
        InsertRowsCommand.RestoreRuleRanges(sheet, _dataValidationSnapshot, _conditionalFormatSnapshot);
        InsertRowsCommand.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        sheet.PrintArea = _printAreaSnapshot;
        InsertRowsCommand.RestoreSortedSet(sheet.RowPageBreaks, _rowPageBreakSnapshot);
        InsertRowsCommand.RestoreChartDataRanges(sheet, _chartSnapshot);
    }
}
