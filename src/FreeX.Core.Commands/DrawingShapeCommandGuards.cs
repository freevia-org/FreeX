using System.Diagnostics.CodeAnalysis;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class DrawingShapeCommandGuards
{
    private const string DrawingShapeNotFoundMessage = "Drawing shape was not found.";
    private const string InvalidDrawingShapeSizeMessage = "Shape size must be positive.";

    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    /// <summary>
    /// Same sheet-level "Edit objects" protection check as <see cref="RejectIfEditObjectsBlocked(Sheet)"/>,
    /// but layers in the per-shape <see cref="DrawingShapeModel.Locked"/> flag: an author-unlocked shape
    /// (<c>Locked == false</c>) stays movable/resizable even while the sheet is protected with "Edit
    /// objects" blocked, matching Excel's per-object Locked checkbox. A locked shape (the default) is
    /// rejected exactly like the sheet-only overload.
    /// </summary>
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet, DrawingShapeModel shape) =>
        shape.Locked ? RejectIfEditObjectsBlocked(sheet) : null;

    public static CommandOutcome? RejectInvalidSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0
            ? null
            : new CommandOutcome(false, InvalidDrawingShapeSizeMessage);

    public static bool TryFindShape(
        Sheet sheet,
        Guid shapeId,
        [NotNullWhen(true)] out DrawingShapeModel? shape)
    {
        foreach (var candidate in sheet.DrawingShapes)
        {
            if (candidate.Id == shapeId)
            {
                shape = candidate;
                return true;
            }
        }

        shape = null;
        return false;
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
        var index = IndexOfZOrderEntry(normalizedOrder, entry);
        if (index < 0)
            return DrawingShapeNotFound();

        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= normalizedOrder.Count)
            return new CommandOutcome(true, IsNoOp: true); // already at the front/back

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

    private static int IndexOfZOrderEntry(
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
