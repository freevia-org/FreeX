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
        var visibleRange = GetVisiblePivotRange(pivotTable);
        if (pivotTable.RowFields.Count <= 1 ||
            visibleRange.Start.Sheet != sheet.Id)
        {
            return;
        }

        if (pivotTable.ReportLayout != PivotReportLayout.Compact)
        {
            AddNonCompactAdornments(sheet, pivotTable, visibleRange, adornments);
            return;
        }

        var labelCol = visibleRange.Start.Col;
        var dataStartRow = visibleRange.Start.Row + (uint)Math.Max(1, pivotTable.FirstDataRow);
        if (dataStartRow > visibleRange.End.Row)
            return;

        for (var row = dataStartRow; row <= visibleRange.End.Row; row++)
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

    private static void AddNonCompactAdornments(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange visibleRange,
        List<PivotRowLabelAdornment> adornments)
    {
        if (!pivotTable.ShowExpandCollapseButtons)
            return;

        var labelStartCol = visibleRange.Start.Col;
        var dataStartRow = visibleRange.Start.Row + (uint)Math.Max(1, pivotTable.FirstDataRow);
        if (dataStartRow > visibleRange.End.Row)
            return;

        var parentFieldCount = Math.Max(0, pivotTable.RowFields.Count - 1);
        for (var row = dataStartRow; row <= visibleRange.End.Row; row++)
        for (var level = 0; level < parentFieldCount; level++)
        {
            var labelCol = labelStartCol + (uint)level;
            var address = new CellAddress(sheet.Id, row, labelCol);
            if (!TryGetRowLabel(sheet, pivotTable, address, out _))
            {
                continue;
            }

            var hasPreviousPeer = HasSamePrefixOnPreviousRow(sheet, pivotTable, visibleRange, row, labelStartCol, level);
            var hasNextPeer = HasSamePrefixOnNextRow(sheet, pivotTable, visibleRange, row, labelStartCol, level);
            var hasChildRows = HasChildRowsBeforeNextPeer(sheet, pivotTable, visibleRange, row, labelStartCol, level);
            if (!hasPreviousPeer && !hasNextPeer && !hasChildRows)
                continue;

            adornments.Add(new PivotRowLabelAdornment(
                address,
                IndentLevel: 0,
                ShowExpandCollapseButton: !hasPreviousPeer && (hasNextPeer || hasChildRows),
                IsExpanded: true));
        }
    }

    private static bool HasChildRowsBeforeNextPeer(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange visibleRange,
        uint row,
        uint labelStartCol,
        int level)
    {
        if (level + 1 >= pivotTable.RowFields.Count)
            return false;

        var labelCol = labelStartCol + (uint)level;
        var childCol = labelCol + 1;
        for (var nextRow = row + 1; nextRow <= visibleRange.End.Row; nextRow++)
        {
            if (TryGetRowLabel(sheet, pivotTable, new CellAddress(sheet.Id, nextRow, labelCol), out _))
                return false;

            if (TryGetRowLabel(sheet, pivotTable, new CellAddress(sheet.Id, nextRow, childCol), out _))
                return true;
        }

        return false;
    }

    private static bool HasSamePrefixOnPreviousRow(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange visibleRange,
        uint row,
        uint labelStartCol,
        int level)
    {
        if (row <= visibleRange.Start.Row)
            return false;

        return HasSamePrefix(sheet, pivotTable, row, row - 1, labelStartCol, level);
    }

    private static bool HasSamePrefixOnNextRow(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange visibleRange,
        uint row,
        uint labelStartCol,
        int level)
    {
        if (row >= visibleRange.End.Row)
            return false;

        return HasSamePrefix(sheet, pivotTable, row, row + 1, labelStartCol, level);
    }

    private static bool HasSamePrefix(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint row,
        uint otherRow,
        uint labelStartCol,
        int level)
    {
        for (var offset = 0; offset <= level; offset++)
        {
            var col = labelStartCol + (uint)offset;
            if (!TryGetRowLabel(sheet, pivotTable, new CellAddress(sheet.Id, row, col), out var text) ||
                !TryGetRowLabel(sheet, pivotTable, new CellAddress(sheet.Id, otherRow, col), out var otherText) ||
                !string.Equals(text, otherText, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
        }

        return true;
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

    private static GridRange GetVisiblePivotRange(PivotTableModel pivotTable) =>
        pivotTable.LastRenderedRange is { } lastRenderedRange &&
        lastRenderedRange.Start.Sheet == pivotTable.TargetRange.Start.Sheet
            ? lastRenderedRange
            : pivotTable.TargetRange;

    private static string GrandTotalCaption(PivotTableModel pivotTable) =>
        string.IsNullOrWhiteSpace(pivotTable.GrandTotalCaption)
            ? "Grand Total"
            : pivotTable.GrandTotalCaption!;
}
