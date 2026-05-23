using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class CommandGuards
{
    public static CommandOutcome? RejectIfProtected(Sheet sheet)
    {
        return sheet.IsProtected
            ? new CommandOutcome(false, "The sheet is protected.")
            : null;
    }

    public static CommandOutcome? RejectIfProtectedWithoutPermission(
        Sheet sheet,
        SheetProtectionPermission permission)
    {
        if (!sheet.IsProtected)
            return null;

        return sheet.ProtectionPermissions.Contains(permission)
            ? null
            : new CommandOutcome(false, "The sheet is protected.");
    }

    public static CommandOutcome? RejectIfWorkbookStructureProtected(Workbook workbook)
    {
        return workbook.IsStructureProtected
            ? new CommandOutcome(false, "The workbook structure is protected.")
            : null;
    }

    public static bool CanEditCell(Workbook workbook, Sheet sheet, CellAddress address)
    {
        if (!sheet.IsProtected)
            return true;

        if (sheet.AllowEditRanges.Any(range => range.Contains(address)))
            return true;

        var current = sheet.GetCell(address);
        var style = workbook.GetStyle(current?.StyleId ?? StyleId.Default);
        return !style.Locked;
    }
}
