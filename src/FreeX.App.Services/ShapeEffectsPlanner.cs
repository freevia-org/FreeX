using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Portable, UI-free equivalent of the shape-effects dialog policy: the ordered list of effect presets the dialog
/// offers, each carrying localization resource keys for its label and description, plus normalization for stored
/// presets that are no longer supported. Shells resolve text and build their own dialog chrome.
/// </summary>
public static class ShapeEffectsPlanner
{
    /// <summary>One selectable effect preset and the resource keys describing it.</summary>
    public sealed record ShapeEffectOption(
        DrawingShapeEffectPreset Preset,
        string LabelKey,
        string DescriptionKey);

    /// <summary>The dialog plan: the offered options and the currently-selected preset.</summary>
    public sealed record ShapeEffectsPlan(
        IReadOnlyList<ShapeEffectOption> Options,
        DrawingShapeEffectPreset SelectedPreset);

    public static ShapeEffectsPlan CreatePlan(DrawingShapeEffectPreset currentPreset) =>
        new(CreateOptions(), NormalizePreset(currentPreset));

    /// <summary>Maps an undefined / unsupported stored preset back to <see cref="DrawingShapeEffectPreset.None"/>.</summary>
    public static DrawingShapeEffectPreset NormalizePreset(DrawingShapeEffectPreset preset) =>
        Enum.IsDefined(preset) ? preset : DrawingShapeEffectPreset.None;

    /// <summary>The ordered shadow / glow / reflection / soft-edges / bevel / 3-D presets the dialog offers.</summary>
    public static IReadOnlyList<ShapeEffectOption> CreateOptions() =>
    [
        new(DrawingShapeEffectPreset.None, "ShapeEffects_None", "ShapeEffects_NoneDescription"),
        new(DrawingShapeEffectPreset.Shadow, "ShapeEffects_Shadow", "ShapeEffects_ShadowDescription"),
        new(DrawingShapeEffectPreset.InnerShadow, "ShapeEffects_InnerShadow", "ShapeEffects_InnerShadowDescription"),
        new(DrawingShapeEffectPreset.Reflection, "ShapeEffects_Reflection", "ShapeEffects_ReflectionDescription"),
        new(DrawingShapeEffectPreset.Glow, "ShapeEffects_Glow", "ShapeEffects_GlowDescription"),
        new(DrawingShapeEffectPreset.SoftEdges, "ShapeEffects_SoftEdges", "ShapeEffects_SoftEdgesDescription"),
        new(DrawingShapeEffectPreset.Bevel, "ShapeEffects_Bevel", "ShapeEffects_BevelDescription"),
        new(DrawingShapeEffectPreset.ThreeDRotation, "ShapeEffects_ThreeDRotation", "ShapeEffects_ThreeDRotationDescription"),
    ];

    /// <summary>Index of <paramref name="preset"/> within <see cref="CreateOptions"/> (0 when not found).</summary>
    public static int FindOptionIndex(IReadOnlyList<ShapeEffectOption> options, DrawingShapeEffectPreset preset)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = NormalizePreset(preset);
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Preset == normalized)
                return i;
        }

        return 0;
    }

    public static SetDrawingShapeEffectCommand BuildCommand(
        SheetId sheetId,
        Guid shapeId,
        DrawingShapeEffectPreset preset) =>
        new(sheetId, shapeId, NormalizePreset(preset));
}
