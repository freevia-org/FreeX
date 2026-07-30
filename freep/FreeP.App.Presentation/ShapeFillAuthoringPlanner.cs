using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>PowerPoint-style fill transparency presets for ordinary shapes.</summary>
public enum ShapeFillTransparencyPreset
{
    Opaque,
    Half,
    Transparent,
}

public static class ShapeFillAuthoringPlanner
{
    public const string OpaqueCommandId = "freep.shape.fill.opaque";
    public const string HalfCommandId = "freep.shape.fill.half-transparent";
    public const string TransparentCommandId = "freep.shape.fill.transparent";

    public static byte ResolveAlpha(ShapeFillTransparencyPreset preset) => preset switch
    {
        ShapeFillTransparencyPreset.Opaque => byte.MaxValue,
        ShapeFillTransparencyPreset.Half => 128,
        ShapeFillTransparencyPreset.Transparent => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown fill transparency preset."),
    };

    public static byte OpaqueAlpha => ResolveAlpha(ShapeFillTransparencyPreset.Opaque);
    public static byte HalfTransparentAlpha => ResolveAlpha(ShapeFillTransparencyPreset.Half);
    public static byte TransparentAlpha => ResolveAlpha(ShapeFillTransparencyPreset.Transparent);
}
