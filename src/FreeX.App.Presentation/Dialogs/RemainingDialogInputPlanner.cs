using FreeX.App.Presentation.SheetUI;

namespace FreeX.App.Presentation.Dialogs;

public sealed record ConditionalFormatThresholdDialogResult(string ThresholdText);

public sealed record RowHeightDialogResult(double Height);

public sealed record ColumnWidthDialogResult(double Width);

public static class ConditionalFormatThresholdDialogPlanner
{
    public static ConditionalFormatThresholdDialogResult CreateResult(string? thresholdText) =>
        new((thresholdText ?? "").Trim());

    public static bool TryCreateResult(string? thresholdText, out ConditionalFormatThresholdDialogResult result)
    {
        result = CreateResult(thresholdText);
        return !string.IsNullOrWhiteSpace(result.ThresholdText);
    }
}

public static class WorksheetDimensionDialogPlanner
{
    public const double DefaultRowHeight = 20;
    public const double DefaultColumnWidth = 8;
    public const double MaximumExcelRowHeight = 409.5;
    public const double MaximumExcelColumnWidth = 255;

    public static bool TryCreateRowHeightResult(string? input, out RowHeightDialogResult result)
    {
        result = new RowHeightDialogResult(DefaultRowHeight);
        if (input is null || !WorksheetSizeInputParser.TryParseSizeInRange(input, 0, MaximumExcelRowHeight, out var height))
            return false;

        result = new RowHeightDialogResult(height);
        return true;
    }

    public static bool TryCreateColumnWidthResult(string? input, out ColumnWidthDialogResult result)
    {
        result = new ColumnWidthDialogResult(DefaultColumnWidth);
        if (input is null || !WorksheetSizeInputParser.TryParseSizeInRange(input, 0, MaximumExcelColumnWidth, out var width))
            return false;

        result = new ColumnWidthDialogResult(width);
        return true;
    }
}
