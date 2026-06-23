using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public static class DrawingObjectKindMapper
{
    public static SelectionPaneObjectKind ToSelectionPaneObjectKind(DrawingObjectTargetKind kind) =>
        kind switch
        {
            DrawingObjectTargetKind.Picture => SelectionPaneObjectKind.Picture,
            DrawingObjectTargetKind.Shape => SelectionPaneObjectKind.Shape,
            DrawingObjectTargetKind.TextBox => SelectionPaneObjectKind.TextBox,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing object kind is not supported.")
        };

    public static DrawingObjectTargetKind? ToDrawingObjectTargetKind(SelectionPaneObjectKind kind) =>
        kind switch
        {
            SelectionPaneObjectKind.Picture => DrawingObjectTargetKind.Picture,
            SelectionPaneObjectKind.Shape => DrawingObjectTargetKind.Shape,
            SelectionPaneObjectKind.TextBox => DrawingObjectTargetKind.TextBox,
            _ => null
        };
}
