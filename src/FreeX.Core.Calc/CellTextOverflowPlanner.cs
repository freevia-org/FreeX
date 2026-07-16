using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public static class CellTextOverflowPlanner
{
    public static bool CanOverflowCellText(
        CellStyle? style,
        ScalarValue? rawValue,
        string? displayText,
        GridRange? merge)
    {
        var horizontalAlignment = style?.HorizontalAlignment ?? HorizontalAlignment.General;
        return !string.IsNullOrEmpty(displayText) &&
            style?.WrapText != true &&
            style?.ShrinkToFit != true &&
            !CellTextOrientationLayoutPlanner.HasTextOrientation(style?.TextRotation ?? 0) &&
            rawValue is not NumberValue and not DateTimeValue &&
            !merge.HasValue &&
            horizontalAlignment is HorizontalAlignment.Left or
                HorizontalAlignment.General or
                HorizontalAlignment.Right or
                HorizontalAlignment.Center;
    }

    public static bool IsOverflowOccupied(
        DisplayCell cell,
        CellAddress? editingCell,
        GridRange? merge = null)
    {
        if (editingCell is { } address && address.Row == cell.Row && address.Col == cell.Col)
            return true;

        if (merge is not null)
            return true;

        return !string.IsNullOrEmpty(cell.DisplayText) ||
            cell.ConditionalIcon is not null ||
            cell.ConditionalDataBar is not null ||
            cell.Formula is not null ||
            cell.RawValue is not null and not BlankValue;
    }
}
