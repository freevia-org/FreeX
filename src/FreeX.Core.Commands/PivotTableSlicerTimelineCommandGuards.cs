using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class PivotTableSlicerTimelineCommandGuards
{
    private const string ConnectedPivotTableNotFoundMessage = "Connected PivotTable was not found.";
    private const string ConnectedPivotTableFieldNotFoundMessage = "Connected PivotTable field was not found.";

    public static CommandOutcome ConnectedPivotTableNotFound() =>
        new(false, ConnectedPivotTableNotFoundMessage);

    public static CommandOutcome ConnectedPivotTableFieldNotFound() =>
        new(false, ConnectedPivotTableFieldNotFoundMessage);

    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    /// <summary>
    /// Checks <see cref="SheetProtectionPermission.UsePivotTableReports"/> against BOTH the sheet
    /// hosting the connected PivotTable (<paramref name="pivotSheet"/>) AND the sheet the
    /// slicer/timeline widget's own drawing anchor lives on (<paramref name="sourceSheetName"/>).
    /// A slicer/timeline is conventionally co-located with its PivotTable, but nothing enforces
    /// that: when placed on a different, protected sheet, clicking the widget must still be
    /// blocked — Excel gates object interaction per the sheet the object itself sits on, not just
    /// the sheet the data it filters happens to live on.
    /// </summary>
    public static CommandOutcome? RejectIfEitherSheetProtected(
        Workbook workbook,
        Sheet pivotSheet,
        string? sourceSheetName)
    {
        if (CommandGuards.RejectIfProtectedWithoutPermission(pivotSheet, SheetProtectionPermission.UsePivotTableReports) is { } pivotOutcome)
            return pivotOutcome;

        if (string.IsNullOrWhiteSpace(sourceSheetName))
            return null;

        var widgetSheet = workbook.GetSheet(sourceSheetName);
        if (widgetSheet is null || ReferenceEquals(widgetSheet, pivotSheet))
            return null;

        return CommandGuards.RejectIfProtectedWithoutPermission(widgetSheet, SheetProtectionPermission.UsePivotTableReports);
    }
}
