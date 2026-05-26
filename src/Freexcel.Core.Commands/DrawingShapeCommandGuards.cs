using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class DrawingShapeCommandGuards
{
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);
}
