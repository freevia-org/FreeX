using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public static class DrawingObjectRenderPlanner
{
    public static IReadOnlyList<DrawingObjectRenderPlan> Plan(ViewportModel viewport)
    {
        if (viewport.DrawingObjects is not { Count: > 0 })
            return [];

        var plans = new DrawingObjectRenderPlan[viewport.DrawingObjects.Count];
        for (var i = 0; i < viewport.DrawingObjects.Count; i++)
            plans[i] = Plan(viewport.DrawingObjects[i]);

        return plans;
    }

    public static DrawingObjectRenderPlan Plan(DrawingObjectBounds drawingObject) =>
        drawingObject.Kind switch
        {
            SelectionPaneObjectKind.Shape => PlanShape(drawingObject),
            SelectionPaneObjectKind.Picture => PlanPicture(drawingObject),
            SelectionPaneObjectKind.TextBox => new DrawingObjectRenderPlan(
                drawingObject,
                DrawingObjectRenderPrimitiveKind.TextBox),
            _ => Fallback(drawingObject, "Unsupported drawing object kind.")
        };

    private static DrawingObjectRenderPlan PlanShape(DrawingObjectBounds drawingObject) =>
        drawingObject.ShapeKind is DrawingShapeKind.Rectangle or DrawingShapeKind.Ellipse or DrawingShapeKind.Line
            ? new DrawingObjectRenderPlan(drawingObject, DrawingObjectRenderPrimitiveKind.Shape)
            : Fallback(drawingObject, "Unsupported drawing shape kind.");

    private static DrawingObjectRenderPlan PlanPicture(DrawingObjectBounds drawingObject) =>
        drawingObject.PictureKind switch
        {
            PictureKind.Image => PlanImagePicture(drawingObject),
            PictureKind.CellRangeSnapshot => PlanCellRangeSnapshot(drawingObject),
            _ => Fallback(drawingObject, "Unsupported picture kind.")
        };

    private static DrawingObjectRenderPlan PlanImagePicture(DrawingObjectBounds drawingObject)
    {
        if (drawingObject.ImageBytes is not { Length: > 0 })
            return Fallback(drawingObject, "Image picture has no embedded image bytes.");

        if (!HasCrop(drawingObject))
            return new DrawingObjectRenderPlan(drawingObject, DrawingObjectRenderPrimitiveKind.Image);

        return new DrawingObjectRenderPlan(
            drawingObject,
            DrawingObjectRenderPrimitiveKind.CroppedImage,
            new DrawingPictureCrop(
                drawingObject.CropLeft,
                drawingObject.CropTop,
                drawingObject.CropRight,
                drawingObject.CropBottom));
    }

    private static DrawingObjectRenderPlan PlanCellRangeSnapshot(DrawingObjectBounds drawingObject) =>
        new(
            drawingObject,
            DrawingObjectRenderPrimitiveKind.CellRangeSnapshot,
            PictureGrid: new DrawingPictureGrid(
                Math.Max(1, drawingObject.SourceRowCount),
                Math.Max(1, drawingObject.SourceColumnCount),
                drawingObject.PictureCells ?? []));

    private static DrawingObjectRenderPlan Fallback(DrawingObjectBounds drawingObject, string reason) =>
        new(
            drawingObject,
            DrawingObjectRenderPrimitiveKind.BoundsFallback,
            FallbackReason: reason);

    private static bool HasCrop(DrawingObjectBounds drawingObject) =>
        drawingObject.CropLeft > 0 ||
        drawingObject.CropTop > 0 ||
        drawingObject.CropRight > 0 ||
        drawingObject.CropBottom > 0;
}
