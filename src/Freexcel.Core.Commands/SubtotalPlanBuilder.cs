using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal sealed record SubtotalPlan(
    IReadOnlyList<SubtotalInsertionPlan> GroupRows,
    SubtotalInsertionPlan GrandTotalRow,
    IReadOnlyList<uint> PageBreakRows);

internal sealed record SubtotalInsertionPlan(
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
        var orderedGroups = groups.OrderByDescending(g => g.EndRow).ToList();
        var groupRows = new List<SubtotalInsertionPlan>(orderedGroups.Count);
        var pendingBreaks = new List<uint>();

        foreach (var group in orderedGroups)
        {
            groupRows.Add(new SubtotalInsertionPlan(
                group.EndRow + 1,
                $"{group.Label} Total",
                group.StartRow,
                group.EndRow));

            if (pageBreakBetweenGroups && group.EndRow < range.End.Row)
                pendingBreaks.Add(group.EndRow);
        }

        var pageBreakRows = new List<uint>(pendingBreaks.Count);
        foreach (var sourceEndRow in pendingBreaks)
        {
            uint subsequentInsertions = (uint)orderedGroups.Count(g => g.EndRow < sourceEndRow);
            pageBreakRows.Add(sourceEndRow + 2 + subsequentInsertions);
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
        var orderedGroups = groups.OrderByDescending(g => g.StartRow).ToList();
        var groupRows = orderedGroups
            .Select(group => new SubtotalInsertionPlan(
                group.StartRow,
                $"{group.Label} Total",
                group.StartRow + 1,
                group.EndRow + 1))
            .ToList();

        uint summaryRow = range.Start.Row + 1;
        uint summaryEndRow = range.End.Row + (uint)groups.Count + 1;
        var grandTotal = new SubtotalInsertionPlan(
            summaryRow,
            "Grand Total",
            summaryRow + 1,
            summaryEndRow);

        var pageBreakRows = pageBreakBetweenGroups
            ? groups
                .OrderBy(g => g.StartRow)
                .Skip(1)
                .Select((group, index) => group.StartRow + (uint)index + 2)
                .ToList()
            : [];

        return new SubtotalPlan(groupRows, grandTotal, pageBreakRows);
    }

    private static List<GroupSpan> GetGroups(Sheet sheet, GridRange range, uint groupByColumnOffset)
    {
        var groupColumn = range.Start.Col + groupByColumnOffset;
        var groups = new List<GroupSpan>();
        var groupStart = range.Start.Row + 1;
        var currentLabel = FormatLabel(sheet.GetValue(groupStart, groupColumn));

        for (uint row = groupStart + 1; row <= range.End.Row; row++)
        {
            var label = FormatLabel(sheet.GetValue(row, groupColumn));
            if (label == currentLabel)
                continue;

            groups.Add(new GroupSpan(currentLabel, groupStart, row - 1));
            groupStart = row;
            currentLabel = label;
        }

        groups.Add(new GroupSpan(currentLabel, groupStart, range.End.Row));
        return groups;
    }

    private static string FormatLabel(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        _ => ""
    };

    private sealed record GroupSpan(string Label, uint StartRow, uint EndRow);
}
