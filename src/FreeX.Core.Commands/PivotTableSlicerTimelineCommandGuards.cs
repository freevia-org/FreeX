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
}
