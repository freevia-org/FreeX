using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static void ApplyMergedRowLabels(Workbook workbook, Sheet sheet, PivotTableModel pivotTable)
    {
        if (!pivotTable.MergeAndCenterLabels ||
            pivotTable.RowFields.Count <= 1)
        {
            return;
        }

        if (pivotTable.ReportLayout == PivotReportLayout.Compact)
        {
            MergeCompactRowLabelHeaderAcrossColumnHeaderRows(workbook, sheet, pivotTable);
            return;
        }

        var materialized = GetMaterializedOutputRange(sheet, pivotTable);
        var bodyStart = GetPivotBodyStart(pivotTable);
        var rowLabelColumnCount = RowFieldOutputColumnCount(pivotTable);
        if (rowLabelColumnCount <= 1 || materialized.End.Row <= bodyStart.Row + 1)
            return;

        for (var colOffset = 0; colOffset < rowLabelColumnCount - 1; colOffset++)
            MergeRepeatedLabelsInColumn(
                workbook,
                sheet,
                pivotTable,
                materialized,
                bodyStart.Row + 1,
                bodyStart.Col + (uint)colOffset,
                bodyStart.Col + (uint)rowLabelColumnCount - 1);

        MergeSubtotalLabelsAcrossRowFields(
            workbook,
            sheet,
            materialized,
            bodyStart.Row + 1,
            bodyStart.Col,
            bodyStart.Col + (uint)rowLabelColumnCount - 1);
    }

    private static void MergeCompactRowLabelHeaderAcrossColumnHeaderRows(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable)
    {
        if (pivotTable.ColumnFields.Count <= 1)
            return;

        var bodyStart = GetPivotBodyStart(pivotTable);
        var endRow = bodyStart.Row + (uint)pivotTable.ColumnFields.Count - 1;
        if (sheet.GetCell(bodyStart.Row, bodyStart.Col)?.Value is not TextValue text ||
            !string.Equals(text.Value, "Row Labels", StringComparison.Ordinal))
        {
            return;
        }

        for (var row = bodyStart.Row + 1; row <= endRow; row++)
        {
            if (sheet.GetCell(row, bodyStart.Col) is not null)
                return;
        }

        MergeLabelRegion(workbook, sheet, bodyStart.Row, endRow, bodyStart.Col, bodyStart.Col);
    }

    private static void MergeRepeatedLabelsInColumn(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange materialized,
        uint firstBodyRow,
        uint labelCol,
        uint lastRowLabelCol)
    {
        uint? spanStart = null;
        string? spanText = null;
        for (var row = firstBodyRow; row <= materialized.End.Row + 1; row++)
        {
            var text = row <= materialized.End.Row ? GetMergeableLabelText(sheet, pivotTable, row, labelCol) : null;
            var suppressedContinuation = text is null &&
                spanStart is not null &&
                row <= materialized.End.Row &&
                HasInnerRowLabelValue(sheet, pivotTable, row, labelCol, lastRowLabelCol);
            if (spanStart is not null &&
                !suppressedContinuation &&
                (!string.Equals(text, spanText, StringComparison.Ordinal) || text is null))
            {
                MergeLabelSpan(workbook, sheet, spanStart.Value, row - 1, labelCol);
                spanStart = null;
                spanText = null;
            }

            if (text is not null && spanStart is null)
            {
                spanStart = row;
                spanText = text;
            }
        }
    }

    private static string? GetMergeableLabelText(Sheet sheet, PivotTableModel pivotTable, uint row, uint col)
    {
        if (sheet.GetCell(row, col)?.Value is not TextValue text ||
            string.IsNullOrWhiteSpace(text.Value) ||
            IsPivotGrandTotalCaption(pivotTable, text.Value) ||
            IsPivotSubtotalCaption(text.Value))
        {
            return null;
        }

        return text.Value;
    }

    private static bool HasInnerRowLabelValue(Sheet sheet, PivotTableModel pivotTable, uint row, uint labelCol, uint lastRowLabelCol)
    {
        for (var col = labelCol + 1; col <= lastRowLabelCol; col++)
        {
            if (GetMergeableLabelText(sheet, pivotTable, row, col) is not null)
                return true;
        }

        return false;
    }

    private static void MergeSubtotalLabelsAcrossRowFields(
        Workbook workbook,
        Sheet sheet,
        GridRange materialized,
        uint firstBodyRow,
        uint firstRowLabelCol,
        uint lastRowLabelCol)
    {
        if (lastRowLabelCol <= firstRowLabelCol)
            return;

        for (var row = firstBodyRow; row <= materialized.End.Row; row++)
        {
            for (var col = firstRowLabelCol; col < lastRowLabelCol; col++)
            {
                if (sheet.GetCell(row, col)?.Value is not TextValue text ||
                    !IsPivotSubtotalCaption(text.Value) ||
                    HasRowLabelValueToRight(sheet, row, col, lastRowLabelCol))
                {
                    continue;
                }

                MergeLabelRegion(workbook, sheet, row, row, col, lastRowLabelCol);
                break;
            }
        }
    }

    private static bool HasRowLabelValueToRight(Sheet sheet, uint row, uint col, uint lastRowLabelCol)
    {
        for (var currentCol = col + 1; currentCol <= lastRowLabelCol; currentCol++)
        {
            if (sheet.GetCell(row, currentCol)?.Value is { } value &&
                !IsBlankPivotLabelValue(value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBlankPivotLabelValue(ScalarValue value) =>
        value is BlankValue ||
        value is TextValue text && string.IsNullOrWhiteSpace(text.Value);

    private static void MergeLabelSpan(Workbook workbook, Sheet sheet, uint startRow, uint endRow, uint col)
    {
        if (endRow <= startRow)
            return;

        MergeLabelRegion(workbook, sheet, startRow, endRow, col, col);
    }

    private static void MergeLabelRegion(
        Workbook workbook,
        Sheet sheet,
        uint startRow,
        uint endRow,
        uint startCol,
        uint endCol)
    {
        var region = new GridRange(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));

        // R106: unlike MergeCellsCommand (which absorbs-or-rejects overlaps before ever calling
        // AddMergedRegion), this had no overlap check at all -- two merged regions could end up
        // covering the same cell if a pre-existing merge (left over from an old, larger render
        // footprint, or a manual merge) happened to sit under this pivot-owned label area. The
        // pivot's own row-label merge always wins here: un-merge anything already overlapping it
        // first, exactly like ClearTargetRange already does for the pivot's cleared ranges.
        if (sheet.MergedRegions.Any(existing => existing.Overlaps(region)))
            sheet.ReplaceMergedRegions(sheet.MergedRegions.Where(existing => !existing.Overlaps(region)));

        sheet.AddMergedRegion(region);

        var labelCell = sheet.GetCell(startRow, startCol);
        if (labelCell is not null)
        {
            var style = workbook.GetStyle(labelCell.StyleId).Clone();
            style.HorizontalAlignment = HorizontalAlignment.Center;
            style.VerticalAlignment = VerticalAlignment.Center;
            labelCell.StyleId = workbook.RegisterStyle(style);
        }

        for (var row = startRow + 1; row <= endRow; row++)
            for (var col = startCol; col <= endCol; col++)
                sheet.ClearCell(row, col);

        for (var col = startCol + 1; col <= endCol; col++)
            sheet.ClearCell(startRow, col);
    }
}
