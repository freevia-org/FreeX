using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
        GridRange materialized,
        uint firstBodyRow,
        uint labelCol,
        uint lastRowLabelCol)
    {
        uint? spanStart = null;
        string? spanText = null;
        for (var row = firstBodyRow; row <= materialized.End.Row + 1; row++)
        {
            var text = row <= materialized.End.Row ? GetMergeableLabelText(sheet, row, labelCol) : null;
            var suppressedContinuation = text is null &&
                spanStart is not null &&
                row <= materialized.End.Row &&
                HasInnerRowLabelValue(sheet, row, labelCol, lastRowLabelCol);
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

    private static string? GetMergeableLabelText(Sheet sheet, uint row, uint col)
    {
        if (sheet.GetCell(row, col)?.Value is not TextValue text ||
            string.IsNullOrWhiteSpace(text.Value) ||
            IsPivotGrandTotalCaption(text.Value) ||
            IsPivotSubtotalCaption(text.Value))
        {
            return null;
        }

        return text.Value;
    }

    private static bool HasInnerRowLabelValue(Sheet sheet, uint row, uint labelCol, uint lastRowLabelCol)
    {
        for (var col = labelCol + 1; col <= lastRowLabelCol; col++)
        {
            if (GetMergeableLabelText(sheet, row, col) is not null)
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
            if (sheet.GetCell(row, currentCol)?.Value is not null)
                return true;
        }

        return false;
    }

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
