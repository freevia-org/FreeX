using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    private const string DefaultGrandTotalCaption = "Grand Total";

    private static string GrandTotalCaption(PivotTableModel pivotTable) =>
        string.IsNullOrWhiteSpace(pivotTable.GrandTotalCaption)
            ? DefaultGrandTotalCaption
            : pivotTable.GrandTotalCaption.Trim();

    private static string GrandTotalCaption(PivotTableModel pivotTable, PivotDataFieldModel dataField, bool singleDataField)
    {
        var caption = GrandTotalCaption(pivotTable);
        return singleDataField ? caption : $"{caption} {dataField.Name}";
    }

    private static bool IsPivotGrandTotalCaption(PivotTableModel pivotTable, string value)
    {
        var caption = GrandTotalCaption(pivotTable);
        if (string.Equals(value, caption, StringComparison.OrdinalIgnoreCase))
            return true;

        return pivotTable.DataFields.Any(dataField =>
            string.Equals(value, $"{caption} {dataField.Name}", StringComparison.OrdinalIgnoreCase));
    }
}
