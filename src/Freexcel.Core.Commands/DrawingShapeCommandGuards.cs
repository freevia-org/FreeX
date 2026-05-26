using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class DrawingShapeCommandGuards
{
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    public static CommandOutcome TryMoveZOrder(
        Sheet sheet,
        Guid shapeId,
        int direction,
        out int fromIndex,
        out int toIndex)
    {
        fromIndex = -1;
        toIndex = -1;

        var index = sheet.DrawingShapes.FindIndex(shape => shape.Id == shapeId);
        if (index < 0)
            return new CommandOutcome(false, "Drawing shape was not found.");

        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= sheet.DrawingShapes.Count)
            return new CommandOutcome(true);

        fromIndex = index;
        toIndex = targetIndex;
        SwapZOrder(sheet, fromIndex, toIndex);
        return new CommandOutcome(true, AffectedCells: [sheet.DrawingShapes[toIndex].Anchor]);
    }

    public static void SwapZOrder(Sheet sheet, int fromIndex, int toIndex)
    {
        (sheet.DrawingShapes[fromIndex], sheet.DrawingShapes[toIndex]) =
            (sheet.DrawingShapes[toIndex], sheet.DrawingShapes[fromIndex]);
    }
}
