using FreeX.App.Presentation.SheetUI;

namespace FreeX.App.Presentation.Dialogs;

public sealed record ConditionalFormatThresholdDialogResult(string ThresholdText);

public sealed record RowHeightDialogResult(double Height);

public sealed record ColumnWidthDialogResult(double Width);

public enum WorksheetDimensionKind
{
    RowHeight,
    ColumnWidth,
}

public readonly record struct WorksheetDimensionDialogResult(double Value);

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

    public static bool TryCreateResult(
        WorksheetDimensionKind kind,
        string? input,
        out WorksheetDimensionDialogResult result)
    {
        var defaultValue = kind switch
        {
            WorksheetDimensionKind.RowHeight => DefaultRowHeight,
            WorksheetDimensionKind.ColumnWidth => DefaultColumnWidth,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var maximum = kind == WorksheetDimensionKind.RowHeight
            ? MaximumExcelRowHeight
            : MaximumExcelColumnWidth;
        result = new WorksheetDimensionDialogResult(defaultValue);
        if (input is null || !WorksheetSizeInputParser.TryParseSizeInRange(input, 0, maximum, out var value))
            return false;

        result = new WorksheetDimensionDialogResult(value);
        return true;
    }

    public static bool TryCreateRowHeightResult(string? input, out RowHeightDialogResult result)
    {
        result = new RowHeightDialogResult(DefaultRowHeight);
        if (!TryCreateResult(WorksheetDimensionKind.RowHeight, input, out var parsed))
            return false;

        result = new RowHeightDialogResult(parsed.Value);
        return true;
    }

    public static bool TryCreateColumnWidthResult(string? input, out ColumnWidthDialogResult result)
    {
        result = new ColumnWidthDialogResult(DefaultColumnWidth);
        if (!TryCreateResult(WorksheetDimensionKind.ColumnWidth, input, out var parsed))
            return false;

        result = new ColumnWidthDialogResult(parsed.Value);
        return true;
    }
}
