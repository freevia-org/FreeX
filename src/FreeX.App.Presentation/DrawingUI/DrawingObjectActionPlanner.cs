using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public sealed record DrawingObjectResourceText(string ResourceKey, object?[] Arguments)
{
    public DrawingObjectResourceText(string resourceKey)
        : this(resourceKey, [])
    {
    }
}

/// <summary>
/// Portable action labels and status text descriptors for non-chart drawing-object flows. Renderers still
/// localize and display the text; this planner owns which action/status applies to each drawing decision.
/// </summary>
public static class DrawingObjectActionPlanner
{
    public const string InsertTextBoxCommandTitle = "Insert Text Box";
    public const string InsertShapeCommandTitle = "Insert Shape";
    public const string BringForwardCommandTitle = "Bring Forward";
    public const string SendBackwardCommandTitle = "Send Backward";
    public const string ObjectFillCommandTitle = "Object Fill";
    public const string ObjectNoFillCommandTitle = "Object No Fill";
    public const string ObjectOutlineCommandTitle = "Object Outline";
    public const string ObjectSizeCommandTitle = "Object Size";
    public const string RotateObjectCommandTitle = "Rotate Object";
    public const string MoveObjectCommandTitle = "Move Object";
    public const string ResizeObjectCommandTitle = "Resize Object";
    public const string ShapeGradientCommandTitle = "Shape Gradient";
    public const string ShapeEffectsCommandTitle = "Shape Effects";
    public const string CropPictureCommandTitle = "Crop Picture";
    public const string DeleteObjectCommandTitle = "Delete";

    public static string ZOrderCommandTitle(bool forward) =>
        forward ? BringForwardCommandTitle : SendBackwardCommandTitle;

    public static string FillCommandTitle(bool hasFill) =>
        hasFill ? ObjectFillCommandTitle : ObjectNoFillCommandTitle;

    public static DrawingObjectResourceText InsertShapeSuccess(DrawingShapeKind kind, string cellReference) =>
        new("InsertLoc_InsertedShapeAt", [kind, cellReference]);

    public static DrawingObjectResourceText InsertTextBoxSuccess(string cellReference) =>
        new("InsertLoc_InsertedTextBoxAt", [cellReference]);

    public static DrawingObjectResourceText ZOrderSuccess(DrawingObjectTargetKind kind, bool forward) =>
        kind == DrawingObjectTargetKind.Picture
            ? new(forward ? "Drawing_PictureBroughtForward" : "Drawing_PictureSentBackward")
            : new(forward ? "InsertLoc_BroughtShapeForward" : "InsertLoc_SentShapeBackward");

    public static DrawingObjectResourceText ShapeFillSuccess(string colorText) =>
        new("InsertLoc_ShapeFillSet", [colorText]);

    public static DrawingObjectResourceText ShapeOutlineSuccess(string colorText) =>
        new("InsertLoc_ShapeOutlineSet", [colorText]);

    public static DrawingObjectResourceText ShapeGradientSuccess(string startColorText, string endColorText) =>
        new("ShapeGradient_Applied", [startColorText, endColorText]);

    public static DrawingObjectResourceText ShapeEffectSuccess(
        DrawingShapeEffectPreset normalizedPreset,
        string presetLabel) =>
        normalizedPreset == DrawingShapeEffectPreset.None
            ? new("ShapeEffects_Cleared")
            : new("ShapeEffects_Applied", [presetLabel]);

    public static DrawingObjectResourceText RotationSuccess(FormatPicturePlanner.RotationResult rotation) =>
        new("InsertLoc_RotatedObject", [rotation.Degrees]);

    public static DrawingObjectResourceText ResizeSuccess(ObjectSizeDialogSize size) =>
        new("InsertLoc_ResizedObject", [size.Width, size.Height]);

    public static DrawingObjectResourceText AltTextSuccess(string? altText) =>
        new(string.IsNullOrWhiteSpace(altText) ? "InsertLoc_AltTextCleared" : "InsertLoc_AltTextUpdated");
}
