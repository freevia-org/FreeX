using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal sealed record SubtotalPlan(
    IReadOnlyList<SubtotalInsertionPlan> GroupRows,
    SubtotalInsertionPlan GrandTotalRow,
    IReadOnlyList<uint> PageBreakRows);

internal readonly record struct SubtotalInsertionPlan(
    uint InsertRow,
    string Label,
    uint FormulaStartRow,
    uint FormulaEndRow);

internal static class SubtotalPlanBuilder
{
    public static SubtotalPlan Build(
        Sheet sheet,
        GridRange range,
        uint groupByColumnOffset,
        bool pageBreakBetweenGroups,
        bool summaryBelowData)
    {
        var groups = GetGroups(sheet, range, groupByColumnOffset);
        return summaryBelowData
            ? BuildSummaryBelowPlan(range, groups, pageBreakBetweenGroups)
            : BuildSummaryAbovePlan(range, groups, pageBreakBetweenGroups);
    }

    public static string BuildSubtotalFormula(int functionNumber, uint column, uint formulaStartRow, uint formulaEndRow)
    {
        var subtotalColumnName = CellAddress.NumberToColumnName(column);
        return $"SUBTOTAL({functionNumber},{subtotalColumnName}{formulaStartRow}:{subtotalColumnName}{formulaEndRow})";
    }

    private static SubtotalPlan BuildSummaryBelowPlan(
        GridRange range,
        IReadOnlyList<GroupSpan> groups,
        bool pageBreakBetweenGroups)
    {
        var groupRows = new List<SubtotalInsertionPlan>(groups.Count);
        var pageBreakRows = pageBreakBetweenGroups
            ? new List<uint>(Math.Max(0, groups.Count - 1))
            : [];

        for (var index = groups.Count - 1; index >= 0; index--)
        {
            var group = groups[index];
            groupRows.Add(new SubtotalInsertionPlan(
                group.EndRow + 1,
                $"{group.Label} Total",
                group.StartRow,
                group.EndRow));

            if (pageBreakBetweenGroups && index < groups.Count - 1)
            {
                var subsequentInsertions = (uint)index;
                pageBreakRows.Add(group.EndRow + 2 + subsequentInsertions);
            }
        }

        uint grandTotalRow = range.End.Row + (uint)groups.Count + 1;
        var grandTotal = new SubtotalInsertionPlan(
            grandTotalRow,
            "Grand Total",
            range.Start.Row + 1,
            grandTotalRow - 1);

        return new SubtotalPlan(groupRows, grandTotal, pageBreakRows);
    }

    private static SubtotalPlan BuildSummaryAbovePlan(
        GridRange range,
        IReadOnlyList<GroupSpan> groups,
        bool pageBreakBetweenGroups)
    {
        var groupRows = new List<SubtotalInsertionPlan>(groups.Count);
        for (var index = groups.Count - 1; index >= 0; index--)
        {
            var group = groups[index];
            groupRows.Add(new SubtotalInsertionPlan(
                group.StartRow,
                $"{group.Label} Total",
                group.StartRow + 1,
                group.EndRow + 1));
        }

        uint summaryRow = range.Start.Row + 1;
        uint summaryEndRow = range.End.Row + (uint)groups.Count + 1;
        var grandTotal = new SubtotalInsertionPlan(
            summaryRow,
            "Grand Total",
            summaryRow + 1,
            summaryEndRow);

        var pageBreakRows = pageBreakBetweenGroups
            ? new List<uint>(Math.Max(0, groups.Count - 1))
            : [];
        if (pageBreakBetweenGroups)
        {
            for (var index = 1; index < groups.Count; index++)
                pageBreakRows.Add(groups[index].StartRow + (uint)index + 1);
        }

        return new SubtotalPlan(groupRows, grandTotal, pageBreakRows);
    }

    private static List<GroupSpan> GetGroups(Sheet sheet, GridRange range, uint groupByColumnOffset)
    {
        var groupColumn = range.Start.Col + groupByColumnOffset;
        var groups = new List<GroupSpan>();
        var groupStart = range.Start.Row + 1;

        // Nested Subtotal (Data > Subtotal run a second time over a range that still contains a
        // prior pass's subtotal/grand-total rows, "Replace current subtotals" unchecked) must not
        // treat those leftover rows as ordinary data rows: ApplyInsertAndEdit only wrote the PRIOR
        // pass's group-by column into them, so THIS pass's group-by column is blank there, and a
        // naive scan sees each one as its own distinct one-row group (its "" label differs from the
        // real labels on either side) -- fragmenting a single contiguous group into many spurious
        // extras. Treat every such row as a transparent continuation of whichever real-data group
        // precedes it instead, so this pass's new subtotal lands after the LAST prior subtotal row
        // in the group (matching Excel), not after each one individually. The one exception is the
        // final row of the range: that row is always either the last real data row (first pass) or
        // the previous pass's own grand-total row (a later pass) -- never part of any group -- so it
        // is excluded from the scan entirely; this pass computes its own grand total independently.
        var existingSubtotalRows = new HashSet<uint>(SubtotalRowFinder.Find(sheet, range.Start.Sheet, range));
        var scanEnd = range.End.Row > groupStart && existingSubtotalRows.Contains(range.End.Row)
            ? range.End.Row - 1
            : range.End.Row;

        var currentLabel = FormatLabel(GetGroupValue(sheet, range, groupStart, groupColumn));

        for (uint row = groupStart + 1; row <= scanEnd; row++)
        {
            if (existingSubtotalRows.Contains(row))
                continue;

            var label = FormatLabel(GetGroupValue(sheet, range, row, groupColumn));
            if (label == currentLabel)
                continue;

            groups.Add(new GroupSpan(currentLabel, groupStart, row - 1));
            groupStart = row;
            currentLabel = label;
        }

        groups.Add(new GroupSpan(currentLabel, groupStart, scanEnd));
        return groups;
    }

    private static ScalarValue GetGroupValue(Sheet sheet, GridRange range, uint row, uint groupColumn)
    {
        var address = new CellAddress(range.Start.Sheet, row, groupColumn);
        if (sheet.GetMergeRegion(address) is { } merge &&
            merge.Start.Sheet == range.Start.Sheet &&
            merge.Start.Col == groupColumn &&
            merge.Start.Row >= range.Start.Row + 1 &&
            merge.Start.Row <= range.End.Row)
        {
            return sheet.GetValue(merge.Start.Row, merge.Start.Col);
        }

        return sheet.GetValue(row, groupColumn);
    }

    private static string FormatLabel(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        _ => ""
    };

    private readonly record struct GroupSpan(string Label, uint StartRow, uint EndRow);
}
