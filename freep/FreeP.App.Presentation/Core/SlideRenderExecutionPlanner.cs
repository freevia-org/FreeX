namespace FreeP.App.Compositor;

/// <summary>
/// One renderer-neutral draw operation plus the transient canvas state that affects it.
/// </summary>
public readonly record struct SlideRenderExecutionCommand(
    DrawOp Operation,
    bool SuppressShapeText);

/// <summary>
/// Applies transient canvas state to compositor operations while preserving painter order.
/// Native renderers only realize the returned operations.
/// </summary>
public static class SlideRenderExecutionPlanner
{
    public static IReadOnlyList<SlideRenderExecutionCommand> Plan(
        IReadOnlyList<DrawOp> sourceOperations,
        IReadOnlyDictionary<uint, DrawOp>? transformPreviews = null,
        IReadOnlySet<uint>? suppressedShapeIds = null,
        uint? activeTextEditShapeId = null)
    {
        ArgumentNullException.ThrowIfNull(sourceOperations);

        var commands = new List<SlideRenderExecutionCommand>(sourceOperations.Count);
        foreach (var sourceOperation in sourceOperations)
        {
            var operation = ResolvePreview(sourceOperation, transformPreviews);
            if (!IsSupported(operation))
                continue;

            CanvasTransformPreviewComposer.TryGetShapeId(operation, out var shapeId);
            if (shapeId != 0 && suppressedShapeIds?.Contains(shapeId) == true)
                continue;

            commands.Add(new SlideRenderExecutionCommand(
                operation,
                SuppressShapeText: operation is DrawOp.Shape
                    && shapeId != 0
                    && shapeId == activeTextEditShapeId));
        }

        return commands;
    }

    private static DrawOp ResolvePreview(
        DrawOp sourceOperation,
        IReadOnlyDictionary<uint, DrawOp>? transformPreviews)
    {
        if (transformPreviews is not null
            && CanvasTransformPreviewComposer.TryGetShapeId(sourceOperation, out var shapeId)
            && transformPreviews.TryGetValue(shapeId, out var preview))
        {
            return preview;
        }

        return sourceOperation;
    }

    private static bool IsSupported(DrawOp operation) => operation is
        DrawOp.Background or
        DrawOp.Shape or
        DrawOp.Picture or
        DrawOp.Table or
        DrawOp.Chart;
}
