using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Presentation.DrawingUI;

public enum DrawingObjectContextualRibbonCommand
{
    CropPicture,
    ShapeGradient,
    ShapeEffects
}

public enum DrawingObjectContextualCommandAction
{
    FormatPicture,
    PictureCropMenuHint,
    CropPicture,
    ResetPictureCrop,
    BringForward,
    SendBackward,
    SelectionPane,
    RotateObject,
    ResizeObject,
    EditAltText,
    ShapeFill,
    ShapeOutline,
    ShapeGradient,
    ShapeEffectsDialog,
    ShapeEffectPreset
}

public sealed record DrawingObjectContextualCommandSpec(
    string CommandId,
    DrawingObjectContextualCommandAction Action,
    DrawingShapeEffectPreset? EffectPreset = null);

public sealed record DrawingObjectContextualRibbonPlan(
    bool ShapeFormatVisible,
    bool PictureFormatVisible,
    bool CropPictureEnabled,
    bool ShapeGradientEnabled,
    bool ShapeEffectsEnabled)
{
    public static DrawingObjectContextualRibbonPlan None { get; } = new(
        ShapeFormatVisible: false,
        PictureFormatVisible: false,
        CropPictureEnabled: false,
        ShapeGradientEnabled: false,
        ShapeEffectsEnabled: false);

    public bool IsEnabled(DrawingObjectContextualRibbonCommand command) =>
        command switch
        {
            DrawingObjectContextualRibbonCommand.CropPicture => CropPictureEnabled,
            DrawingObjectContextualRibbonCommand.ShapeGradient => ShapeGradientEnabled,
            DrawingObjectContextualRibbonCommand.ShapeEffects => ShapeEffectsEnabled,
            _ => false
        };
}

public static class DrawingObjectContextualRibbonPlanner
{
    public const string ChartContextKey = "chart.selected";
    public const string PictureContextKey = "picture.selected";
    public const string ShapeContextKey = "shape.selected";
    public const string TableContextKey = "table.active";
    public const string PivotContextKey = "pivot.active";

    public const string ShapeGradientCommandName = "Shape Gradient";
    public const string ShapeEffectsCommandName = "Shape Effects";
    public const string CropPictureCommandName = "Crop Picture";

    public static IReadOnlyList<DrawingObjectContextualCommandSpec> CreatePictureShapeCommandSpecs() =>
    [
        new("Format Picture", DrawingObjectContextualCommandAction.FormatPicture),
        new("Crop Picture", DrawingObjectContextualCommandAction.PictureCropMenuHint),
        new("Crop", DrawingObjectContextualCommandAction.CropPicture),
        new("Reset Crop", DrawingObjectContextualCommandAction.ResetPictureCrop),
        new("Bring Forward", DrawingObjectContextualCommandAction.BringForward),
        new("Send Backward", DrawingObjectContextualCommandAction.SendBackward),
        new(FreeXRibbonCommandIds.DrawingSelectionPane, DrawingObjectContextualCommandAction.SelectionPane),
        new("Rotate Object", DrawingObjectContextualCommandAction.RotateObject),
        new("Object Size", DrawingObjectContextualCommandAction.ResizeObject),
        new("Alt Text", DrawingObjectContextualCommandAction.EditAltText),
        new("Shape Fill", DrawingObjectContextualCommandAction.ShapeFill),
        new("Object Outline", DrawingObjectContextualCommandAction.ShapeOutline),
        new(ShapeGradientCommandName, DrawingObjectContextualCommandAction.ShapeGradient),
        new(ShapeEffectsCommandName, DrawingObjectContextualCommandAction.ShapeEffectsDialog),
        new("No Effect", DrawingObjectContextualCommandAction.ShapeEffectPreset, DrawingShapeEffectPreset.None),
        new("Shadow", DrawingObjectContextualCommandAction.ShapeEffectPreset, DrawingShapeEffectPreset.Shadow),
        new("Inner Shadow", DrawingObjectContextualCommandAction.ShapeEffectPreset, DrawingShapeEffectPreset.InnerShadow),
        new("Reflection", DrawingObjectContextualCommandAction.ShapeEffectPreset, DrawingShapeEffectPreset.Reflection),
        new("Glow", DrawingObjectContextualCommandAction.ShapeEffectPreset, DrawingShapeEffectPreset.Glow),
        new("Soft Edges", DrawingObjectContextualCommandAction.ShapeEffectPreset, DrawingShapeEffectPreset.SoftEdges),
        new("Bevel", DrawingObjectContextualCommandAction.ShapeEffectPreset, DrawingShapeEffectPreset.Bevel),
        new("3-D Rotation", DrawingObjectContextualCommandAction.ShapeEffectPreset, DrawingShapeEffectPreset.ThreeDRotation),
    ];

    public static DrawingObjectContextualRibbonPlan Build(
        Sheet? sheet,
        SelectionPaneObjectKind? selectedKind,
        Guid? selectedObjectId) =>
        Build(sheet, selectedAnchor: null, selectedKind, selectedObjectId);

    public static DrawingObjectContextualRibbonPlan Build(
        Sheet? sheet,
        CellAddress? selectedAnchor,
        SelectionPaneObjectKind? selectedKind,
        Guid? selectedObjectId)
    {
        if (selectedKind is null || selectedObjectId is not { } objectId || objectId == Guid.Empty)
            return DrawingObjectContextualRibbonPlan.None;

        var selectedTarget = DrawingTargetResolver.GetTargetDrawingObject(
            sheet,
            selectedAnchor,
            selectedKind,
            objectId,
            includePictures: true,
            allowFallback: false);
        var picture = DrawingTargetResolver.ResolveSelectedPicture(sheet, selectedKind, objectId).Target;

        return Build(selectedTarget, picture);
    }

    public static DrawingObjectContextualRibbonPlan Build(
        DrawingObjectTarget? selectedTarget,
        PictureModel? selectedPicture)
    {
        var selectedShape = selectedTarget?.Kind == DrawingObjectTargetKind.Shape;
        var shapeVisible = selectedTarget?.Kind is DrawingObjectTargetKind.Shape or DrawingObjectTargetKind.TextBox;
        var pictureVisible = selectedPicture is not null;

        return new DrawingObjectContextualRibbonPlan(
            ShapeFormatVisible: shapeVisible,
            PictureFormatVisible: pictureVisible,
            CropPictureEnabled: selectedPicture?.Kind == PictureKind.Image,
            ShapeGradientEnabled: selectedShape,
            ShapeEffectsEnabled: selectedShape);
    }

    public static string ResolveActivationKey(SelectionPaneObjectKind kind) =>
        kind switch
        {
            SelectionPaneObjectKind.Chart => ChartContextKey,
            SelectionPaneObjectKind.Picture => PictureContextKey,
            SelectionPaneObjectKind.Shape => ShapeContextKey,
            SelectionPaneObjectKind.TextBox => ShapeContextKey,
            _ => ShapeContextKey
        };
}
