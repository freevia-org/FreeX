using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class PivotRowLabelAdornmentPlanner
{
    public static IReadOnlyList<PivotRowLabelAdornment> BuildAdornments(Workbook workbook, Sheet sheet)
    {
        if (sheet.PivotTables.Count == 0)
            return [];

        var adornments = new List<PivotRowLabelAdornment>();
        foreach (var pivotTable in sheet.PivotTables)
            AddAdornments(workbook, sheet, pivotTable, adornments);

        return adornments;
    }

    private static void AddAdornments(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        List<PivotRowLabelAdornment> adornments)
    {
        if (pivotTable.ReportLayout != PivotReportLayout.Compact ||
            pivotTable.RowFields.Count <= 1 ||
            pivotTable.TargetRange.Start.Sheet != sheet.Id)
        {
            return;
        }

        var labelCol = pivotTable.TargetRange.Start.Col;
        var dataStartRow = pivotTable.TargetRange.Start.Row + (uint)Math.Max(1, pivotTable.FirstDataRow);
        if (dataStartRow > pivotTable.TargetRange.End.Row)
            return;

        for (var row = dataStartRow; row <= pivotTable.TargetRange.End.Row; row++)
        {
            var address = new CellAddress(sheet.Id, row, labelCol);
            if (!TryGetRowLabel(sheet, pivotTable, address, out _))
                continue;

            var indentLevel = GetIndentLevel(workbook, sheet, address);
            var showButton = pivotTable.ShowExpandCollapseButtons &&
                indentLevel < pivotTable.RowFields.Count - 1 &&
                NextVisibleLabelIndent(workbook, sheet, pivotTable, row, labelCol) > indentLevel;

            if (!showButton)
                continue;

            adornments.Add(new PivotRowLabelAdornment(
                address,
                indentLevel,
                ShowExpandCollapseButton: true,
                IsExpanded: true));
        }
    }

    private static int NextVisibleLabelIndent(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        uint row,
        uint labelCol)
    {
        for (var nextRow = row + 1; nextRow <= pivotTable.TargetRange.End.Row; nextRow++)
        {
            var nextAddress = new CellAddress(sheet.Id, nextRow, labelCol);
            if (!TryGetRowLabel(sheet, pivotTable, nextAddress, out _))
                continue;

            return GetIndentLevel(workbook, sheet, nextAddress);
        }

        return -1;
    }

    private static bool TryGetRowLabel(
        Sheet sheet,
        PivotTableModel pivotTable,
        CellAddress address,
        out string text)
    {
        text = "";
        if (sheet.GetCell(address.Row, address.Col)?.Value is not TextValue value ||
            string.IsNullOrWhiteSpace(value.Value))
        {
            return false;
        }

        text = value.Value;
        return !string.Equals(text, GrandTotalCaption(pivotTable), StringComparison.CurrentCultureIgnoreCase);
    }

    private static int GetIndentLevel(Workbook workbook, Sheet sheet, CellAddress address)
    {
        var cell = sheet.GetCell(address.Row, address.Col);
        return cell is null
            ? 0
            : Math.Clamp(workbook.GetStyle(cell.StyleId).IndentLevel, 0, 15);
    }

    private static string GrandTotalCaption(PivotTableModel pivotTable) =>
        string.IsNullOrWhiteSpace(pivotTable.GrandTotalCaption)
            ? "Grand Total"
            : pivotTable.GrandTotalCaption!;
}
