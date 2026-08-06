using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation.DrawingInteraction;

namespace FreeX.App.Presentation.DrawingUI;

public static class DrawingObjectCommandPlanner
{
    public static IWorkbookCommand? BuildDragCommitCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        ObjectDragCommitPlan plan) =>
        plan.Kind switch
        {
            ObjectDragCommitKind.Move => BuildMoveCommand(sheetId, kind, objectId, plan.Anchor!.Value),
            ObjectDragCommitKind.Resize => BuildResizeCommand(
                sheetId,
                kind,
                objectId,
                plan.Width,
                plan.Height,
                plan.FlipHorizontal,
                plan.FlipVertical),
            ObjectDragCommitKind.ResizeWithAnchor => BuildResizeWithAnchorCommand(
                sheetId,
                kind,
                objectId,
                plan.Anchor!.Value,
                plan.Width,
                plan.Height,
                plan.FlipHorizontal,
                plan.FlipVertical),
            ObjectDragCommitKind.Rotate => BuildRotateCommand(
                sheetId,
                kind,
                objectId,
                plan.RotationDegrees),
            _ => null,
        };

    public static IWorkbookCommand BuildMoveCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        CellAddress anchor)
    {
        var targetAnchor = new CellAddress(sheetId, anchor.Row, anchor.Col);
        return kind switch
        {
            DrawingObjectTargetKind.Picture => new RepositionPictureCommand(sheetId, objectId, targetAnchor),
            DrawingObjectTargetKind.Shape => new RepositionShapeCommand(sheetId, objectId, targetAnchor),
            DrawingObjectTargetKind.TextBox => new RepositionTextBoxCommand(sheetId, objectId, targetAnchor),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing object kind is not supported.")
        };
    }

    public static IWorkbookCommand BuildResizeCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        double width,
        double height,
        bool? flipHorizontal = null,
        bool? flipVertical = null) =>
        kind switch
        {
            DrawingObjectTargetKind.Picture => new ResizePictureCommand(sheetId, objectId, width, height, flipHorizontal, flipVertical),
            DrawingObjectTargetKind.Shape => new ResizeDrawingShapeCommand(sheetId, objectId, width, height, flipHorizontal, flipVertical),
            DrawingObjectTargetKind.TextBox => new ResizeTextBoxCommand(sheetId, objectId, width, height, flipHorizontal, flipVertical),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing object kind is not supported.")
        };

    public static IWorkbookCommand BuildResizeCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        double width,
        double height,
        bool? flipHorizontal = null,
        bool? flipVertical = null) =>
        BuildResizeCommand(
            sheetId,
            RequireDrawingObjectTargetKind(kind),
            objectId,
            width,
            height,
            flipHorizontal,
            flipVertical);

    public static IWorkbookCommand BuildResizeWithAnchorCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        CellAddress anchor,
        double width,
        double height,
        bool? flipHorizontal = null,
        bool? flipVertical = null) =>
        new CompositeWorkbookCommand(
            DrawingObjectActionPlanner.ResizeObjectCommandTitle,
            [
                BuildMoveCommand(sheetId, kind, objectId, anchor),
                BuildResizeCommand(sheetId, kind, objectId, width, height, flipHorizontal, flipVertical)
            ]);

    // R129-model-drawing-nudge-1: arrow-key nudge entry point, shared by both shells. Takes
    // SelectionPaneObjectKind directly (not DrawingObjectTargetKind) since a chart can be nudged
    // just like a picture/shape/text box -- see NudgeChartCommand.
    public static IWorkbookCommand BuildNudgeCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        double deltaX,
        double deltaY) =>
        kind switch
        {
            SelectionPaneObjectKind.Picture => new NudgePictureCommand(sheetId, objectId, deltaX, deltaY),
            SelectionPaneObjectKind.Shape => new NudgeDrawingShapeCommand(sheetId, objectId, deltaX, deltaY),
            SelectionPaneObjectKind.TextBox => new NudgeTextBoxCommand(sheetId, objectId, deltaX, deltaY),
            SelectionPaneObjectKind.Chart => new NudgeChartCommand(sheetId, objectId, deltaX, deltaY),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing object kind is not supported.")
        };

    public static IWorkbookCommand BuildRotateCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        double degrees) =>
        new SetDrawingObjectRotationCommand(sheetId, DrawingObjectKindMapper.ToSelectionPaneObjectKind(kind), objectId, degrees);

    public static IWorkbookCommand BuildRotateCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        double degrees) =>
        BuildRotateCommand(sheetId, RequireDrawingObjectTargetKind(kind), objectId, degrees);

    public static IWorkbookCommand BuildZOrderCommand(
        SheetId sheetId,
        DrawingObjectZOrderTarget target,
        bool forward) =>
        new MoveSelectionPaneObjectCommand(sheetId, target.Kind, target.Id, forward);

    public static IWorkbookCommand BuildZOrderCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        bool forward) =>
        new MoveSelectionPaneObjectCommand(sheetId, kind, objectId, forward);

    public static IWorkbookCommand BuildAltTextCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        string? altText) =>
        kind switch
        {
            DrawingObjectTargetKind.Picture => new SetPictureAltTextCommand(sheetId, objectId, altText),
            DrawingObjectTargetKind.Shape => new SetDrawingShapeAltTextCommand(sheetId, objectId, altText),
            DrawingObjectTargetKind.TextBox => new SetTextBoxAltTextCommand(sheetId, objectId, altText),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing object kind is not supported.")
        };

    public static IWorkbookCommand BuildAltTextCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        string? altText) =>
        BuildAltTextCommand(sheetId, RequireDrawingObjectTargetKind(kind), objectId, altText);

    public static IWorkbookCommand BuildFillColorCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        CellColor? fillColor)
    {
        var hasFill = fillColor is not null;
        return kind switch
        {
            DrawingObjectTargetKind.Shape => new SetDrawingShapeColorsCommand(
                sheetId,
                objectId,
                fillColor,
                null,
                updateFill: true,
                updateOutline: false,
                hasFill: hasFill),
            DrawingObjectTargetKind.TextBox => new SetTextBoxColorsCommand(
                sheetId,
                objectId,
                fillColor,
                null,
                updateFill: true,
                updateOutline: false,
                hasFill: hasFill),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing object fill is not supported.")
        };
    }

    public static IWorkbookCommand BuildFillColorCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        CellColor? fillColor) =>
        BuildFillColorCommand(sheetId, RequireDrawingObjectTargetKind(kind), objectId, fillColor);

    public static IWorkbookCommand BuildOutlineColorCommand(
        SheetId sheetId,
        DrawingObjectTargetKind kind,
        Guid objectId,
        CellColor? outlineColor) =>
        kind switch
        {
            DrawingObjectTargetKind.Shape => new SetDrawingShapeColorsCommand(
                sheetId,
                objectId,
                null,
                outlineColor,
                updateFill: false,
                updateOutline: true),
            DrawingObjectTargetKind.TextBox => new SetTextBoxColorsCommand(
                sheetId,
                objectId,
                null,
                outlineColor,
                updateFill: false,
                updateOutline: true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing object outline is not supported.")
        };

    public static IWorkbookCommand BuildOutlineColorCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId,
        CellColor? outlineColor) =>
        BuildOutlineColorCommand(sheetId, RequireDrawingObjectTargetKind(kind), objectId, outlineColor);

    // R121-model-drawing-delete-1: the delete key / context-menu / Selection Pane "Delete" entry
    // point, shared by both shells (MainWindow.cs on WPF, MainWindow on Avalonia). Takes
    // SelectionPaneObjectKind directly (not DrawingObjectTargetKind, which has no Chart case) since
    // a chart is just as deletable as a picture/shape/text box.
    public static IWorkbookCommand BuildDeleteCommand(
        SheetId sheetId,
        SelectionPaneObjectKind kind,
        Guid objectId) =>
        new DeleteDrawingObjectCommand(sheetId, kind, objectId);

    public static SelectionPaneObjectKind ToSelectionPaneObjectKind(DrawingObjectTargetKind kind) =>
        DrawingObjectKindMapper.ToSelectionPaneObjectKind(kind);

    public static DrawingObjectTargetKind? ToDrawingObjectTargetKind(SelectionPaneObjectKind kind) =>
        DrawingObjectKindMapper.ToDrawingObjectTargetKind(kind);

    private static DrawingObjectTargetKind RequireDrawingObjectTargetKind(SelectionPaneObjectKind kind) =>
        ToDrawingObjectTargetKind(kind) ??
        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing object kind is not supported.");
}
