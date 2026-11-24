using Free.Shared.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public enum DrawingObjectNudgeDirection
{
    Up,
    Down,
    Left,
    Right
}

public readonly record struct DrawingObjectNudgePlan(
    SelectionPaneObjectKind Kind,
    Guid ObjectId,
    double DeltaX,
    double DeltaY);

/// <summary>
/// Resolves renderer-neutral arrow-key nudge eligibility and movement deltas. Native shells only
/// translate their key and modifier enums before executing the resulting object command.
/// </summary>
public static class DrawingObjectNudgePlanner
{
    public const double StandardStep = 3.0;
    public const double FineStep = 1.0;

    public static bool TryPlan(
        DrawingObjectNudgeDirection? direction,
        FreeKeyModifiers modifiers,
        SelectionPaneObjectKind? selectedKind,
        Guid? selectedObjectId,
        out DrawingObjectNudgePlan plan)
    {
        plan = default;
        if (direction is null ||
            modifiers is not (FreeKeyModifiers.None or FreeKeyModifiers.Control) ||
            selectedKind is not (SelectionPaneObjectKind.Picture or
                SelectionPaneObjectKind.Shape or
                SelectionPaneObjectKind.TextBox or
                SelectionPaneObjectKind.Chart) ||
            selectedObjectId is not { } objectId ||
            objectId == Guid.Empty)
        {
            return false;
        }

        var step = modifiers == FreeKeyModifiers.Control ? FineStep : StandardStep;
        var (deltaX, deltaY) = direction.Value switch
        {
            DrawingObjectNudgeDirection.Up => (0.0, -step),
            DrawingObjectNudgeDirection.Down => (0.0, step),
            DrawingObjectNudgeDirection.Left => (-step, 0.0),
            DrawingObjectNudgeDirection.Right => (step, 0.0),
            _ => (0.0, 0.0)
        };

        plan = new DrawingObjectNudgePlan(selectedKind.Value, objectId, deltaX, deltaY);
        return true;
    }
}
