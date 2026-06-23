using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public static class DrawingObjectCommandPlanner
{
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
            "Resize Object",
            [
                BuildMoveCommand(sheetId, kind, objectId, anchor),
                BuildResizeCommand(sheetId, kind, objectId, width, height, flipHorizontal, flipVertical)
            ]);

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

    public static SelectionPaneObjectKind ToSelectionPaneObjectKind(DrawingObjectTargetKind kind) =>
        DrawingObjectKindMapper.ToSelectionPaneObjectKind(kind);

    public static DrawingObjectTargetKind? ToDrawingObjectTargetKind(SelectionPaneObjectKind kind) =>
        DrawingObjectKindMapper.ToDrawingObjectTargetKind(kind);

    private static DrawingObjectTargetKind RequireDrawingObjectTargetKind(SelectionPaneObjectKind kind) =>
        ToDrawingObjectTargetKind(kind) ??
        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Drawing object kind is not supported.");
}
