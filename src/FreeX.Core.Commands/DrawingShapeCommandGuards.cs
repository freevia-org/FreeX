using System.Diagnostics.CodeAnalysis;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class DrawingShapeCommandGuards
{
    private const string DrawingShapeNotFoundMessage = "Drawing shape was not found.";
    private const string InvalidDrawingShapeSizeMessage = "Shape size must be positive.";

    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    public static CommandOutcome? RejectInvalidSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0
            ? null
            : new CommandOutcome(false, InvalidDrawingShapeSizeMessage);

    public static bool TryFindShape(
        Sheet sheet,
        Guid shapeId,
        [NotNullWhen(true)] out DrawingShapeModel? shape)
    {
        shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == shapeId);
        return shape is not null;
    }

    public static CommandOutcome DrawingShapeNotFound() =>
        new(false, DrawingShapeNotFoundMessage);

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
            return DrawingShapeNotFound();

        var normalizedOrder = DrawingObjectZOrder.GetNormalizedOrder(sheet);
        var index = FindIndex(normalizedOrder, entry);
        if (index < 0)
            return DrawingShapeNotFound();

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
