using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static void WritePageFields(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        // H10: a field added purely to carry a slicer/timeline's filter for a field the user never
        // dragged into the Filters area must still filter rows (via MatchesFieldSelections, which
        // reads pivotTable.PageFields directly) but must NOT render a Filters-area box — Excel doesn't
        // show one for a slicer-only filter on an unplaced field. See PivotFieldModel.IsUnplacedFilterField.
        var pageFields = pivotTable.PageFields.Where(field => !field.IsUnplacedFilterField).ToList();
        if (pageFields.Count == 0)
            return;

        var start = pivotTable.TargetRange.Start;
        var wrap = Math.Max(0, pivotTable.PageWrap);
        for (var index = 0; index < pageFields.Count; index++)
        {
            var (rowOffset, colPairOffset) = GetPageFieldOffset(index, pageFields.Count, wrap, pivotTable.PageOverThenDown);
            var field = pageFields[index];
            SetPivotCell(
                sheet,
                new CellAddress(sheet.Id, start.Row + rowOffset, start.Col + colPairOffset),
                new TextValue(headers[field.SourceFieldIndex]));
            SetPivotCell(
                sheet,
                new CellAddress(sheet.Id, start.Row + rowOffset, start.Col + colPairOffset + 1),
                new TextValue(GetPageFieldSelectionText(field)));
        }
    }

    private static (uint RowOffset, uint ColPairOffset) GetPageFieldOffset(
        int index,
        int pageFieldCount,
        int wrap,
        bool overThenDown)
    {
        if (overThenDown)
        {
            var fieldsPerRow = wrap <= 0 ? pageFieldCount : wrap;
            return ((uint)(index / fieldsPerRow), (uint)((index % fieldsPerRow) * 2));
        }

        var rowsPerColumn = wrap <= 0 ? pageFieldCount : wrap;
        return ((uint)(index % rowsPerColumn), (uint)((index / rowsPerColumn) * 2));
    }

    private static string GetPageFieldSelectionText(PivotFieldModel field)
    {
        if (field.SelectedItems is { Count: > 0 })
            return field.SelectedItems.Count == 1 ? field.SelectedItems[0] : "(Multiple Items)";

        return string.IsNullOrWhiteSpace(field.SelectedItem) ? "(All)" : field.SelectedItem;
    }
}
