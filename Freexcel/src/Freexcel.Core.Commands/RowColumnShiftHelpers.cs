using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class RowColumnShiftHelpers
{
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

    internal static Dictionary<string, NamedRangeSnapshot> CaptureNamedRanges(Workbook workbook) =>
        workbook.NamedRanges.ToDictionary(
            pair => pair.Key,
            pair => new NamedRangeSnapshot(
                pair.Value,
                workbook.TryGetNamedRangeMetadata(pair.Key, out var metadata) ? metadata : NamedRangeMetadata.WorkbookScope),
            StringComparer.OrdinalIgnoreCase);

    internal static void RestoreNamedRanges(Workbook workbook, Dictionary<string, NamedRangeSnapshot>? snapshot)
    {
        if (snapshot is null)
            return;

        workbook.NamedRanges.Clear();
        workbook.NamedRangeMetadataByName.Clear();
        foreach (var (name, namedRange) in snapshot)
            workbook.DefineNamedRange(name, namedRange.Range, namedRange.Metadata);
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
            if (shifted is null) workbook.RemoveNamedRange(name);
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
            if (shifted is null) workbook.RemoveNamedRange(name);
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

        // Overlapping range: compute the surviving portion.
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

        // Overlapping range: compute the surviving portion.
        var newStartCol = range.Start.Col < start ? range.Start.Col : start;
        var newEndCol = range.End.Col > end ? range.End.Col - count : start - 1;
        if (newStartCol == start && newEndCol < start)
            return null;   // range was entirely within the deleted columns
        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet, range.End.Row, newEndCol));
    }
}
