using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static void ApplyMergedRowLabels(Sheet sheet, PivotTableModel pivotTable)
    {
        if (!pivotTable.MergeAndCenterLabels ||
            pivotTable.ReportLayout == PivotReportLayout.Compact ||
            pivotTable.RowFields.Count <= 1)
        {
            return;
        }

        var materialized = GetMaterializedOutputRange(sheet, pivotTable);
        var bodyStart = GetPivotBodyStart(pivotTable);
        var rowLabelColumnCount = RowFieldOutputColumnCount(pivotTable);
        if (rowLabelColumnCount <= 1 || materialized.End.Row <= bodyStart.Row + 1)
            return;

        for (var colOffset = 0; colOffset < rowLabelColumnCount - 1; colOffset++)
            MergeRepeatedLabelsInColumn(
                sheet,
                materialized,
                bodyStart.Row + 1,
                bodyStart.Col + (uint)colOffset,
                bodyStart.Col + (uint)rowLabelColumnCount - 1);
    }

    private static void MergeRepeatedLabelsInColumn(
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
                MergeLabelSpan(sheet, spanStart.Value, row - 1, labelCol);
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

    private static void MergeLabelSpan(Sheet sheet, uint startRow, uint endRow, uint col)
    {
        if (endRow <= startRow)
            return;

        var region = new GridRange(
            new CellAddress(sheet.Id, startRow, col),
            new CellAddress(sheet.Id, endRow, col));
        sheet.AddMergedRegion(region);

        for (var row = startRow + 1; row <= endRow; row++)
            sheet.ClearCell(row, col);
    }
}
