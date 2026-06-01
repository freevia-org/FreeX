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

    private readonly record struct GroupSpan(string Label, uint StartRow, uint EndRow);
}
