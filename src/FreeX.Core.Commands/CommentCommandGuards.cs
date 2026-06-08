using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class CommentCommandGuards
{
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);
}
