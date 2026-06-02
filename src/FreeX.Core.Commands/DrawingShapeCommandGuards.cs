using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class DrawingShapeCommandGuards
{
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    public static CommandOutcome TryMoveZOrder(
        Sheet sheet,
        Guid shapeId,
        int direction,
        out IReadOnlyList<DrawingObjectZOrderEntry>? previousOrder,
        out bool hadExplicitOrder)
    {
        previousOrder = null;
        hadExplicitOrder = false;
        var entry = new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shapeId);
        if (!DrawingObjectZOrder.ContainsObject(sheet, entry))
            return new CommandOutcome(false, "Drawing shape was not found.");

        var normalizedOrder = DrawingObjectZOrder.GetNormalizedOrder(sheet);
        var index = FindIndex(normalizedOrder, entry);
        if (index < 0)
            return new CommandOutcome(false, "Drawing shape was not found.");

        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= normalizedOrder.Count)
            return new CommandOutcome(true);

        hadExplicitOrder = sheet.DrawingObjectZOrder.Count > 0;
        previousOrder = sheet.DrawingObjectZOrder.ToList();
        sheet.DrawingObjectZOrder.Clear();
        sheet.DrawingObjectZOrder.AddRange(normalizedOrder);
        SwapZOrder(sheet, index, targetIndex);
        var shape = sheet.DrawingShapes.First(item => item.Id == shapeId);
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public static void RestoreZOrder(
        Sheet sheet,
        IReadOnlyList<DrawingObjectZOrderEntry> previousOrder,
        bool hadExplicitOrder)
    {
        sheet.DrawingObjectZOrder.Clear();
        if (hadExplicitOrder)
            sheet.DrawingObjectZOrder.AddRange(previousOrder);
    }

    private static void SwapZOrder(Sheet sheet, int fromIndex, int toIndex)
    {
        (sheet.DrawingObjectZOrder[fromIndex], sheet.DrawingObjectZOrder[toIndex]) =
            (sheet.DrawingObjectZOrder[toIndex], sheet.DrawingObjectZOrder[fromIndex]);
    }

    private static int FindIndex(
        IReadOnlyList<DrawingObjectZOrderEntry> order,
        DrawingObjectZOrderEntry entry)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (order[index] == entry)
                return index;
        }

        return -1;
    }
}
