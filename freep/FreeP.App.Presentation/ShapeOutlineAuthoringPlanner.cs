using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>PowerPoint-style outline transparency presets for ordinary shapes.</summary>
public enum ShapeOutlineTransparencyPreset
{
    Opaque,
    Half,
    Transparent,
}

public static class ShapeOutlineAuthoringPlanner
{
    public const string OpaqueCommandId = "freep.shape.outline.opaque";
    public const string HalfCommandId = "freep.shape.outline.half-transparent";
    public const string TransparentCommandId = "freep.shape.outline.transparent";

    public static byte ResolveAlpha(ShapeOutlineTransparencyPreset preset) => preset switch
    {
        ShapeOutlineTransparencyPreset.Opaque => byte.MaxValue,
        ShapeOutlineTransparencyPreset.Half => 128,
        ShapeOutlineTransparencyPreset.Transparent => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown outline transparency preset."),
    };

    public static byte OpaqueAlpha => ResolveAlpha(ShapeOutlineTransparencyPreset.Opaque);
    public static byte HalfTransparentAlpha => ResolveAlpha(ShapeOutlineTransparencyPreset.Half);
    public static byte TransparentAlpha => ResolveAlpha(ShapeOutlineTransparencyPreset.Transparent);
}
