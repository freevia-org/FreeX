using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public enum ShapeShadowPreset
{
    None,
    Subtle,
    Offset,
}

public enum ShapeGlowPreset
{
    None,
    Subtle,
    Strong,
}

public enum ShapeSoftEdgePreset
{
    None,
    Subtle,
    Strong,
}

/// <summary>PowerPoint-style shape shadow presets backed by the shared effect model.</summary>
public static class ShapeEffectAuthoringPlanner
{
    public const string NoneCommandId = "freep.shape.shadow.none";
    public const string SubtleCommandId = "freep.shape.shadow.subtle";
    public const string OffsetCommandId = "freep.shape.shadow.offset";
    public const string GlowNoneCommandId = "freep.shape.glow.none";
    public const string GlowSubtleCommandId = "freep.shape.glow.subtle";
    public const string GlowStrongCommandId = "freep.shape.glow.strong";
    public const string SoftEdgeNoneCommandId = "freep.shape.soft-edge.none";
    public const string SoftEdgeSubtleCommandId = "freep.shape.soft-edge.subtle";
    public const string SoftEdgeStrongCommandId = "freep.shape.soft-edge.strong";

    public static ShapeShadowValues None() => Resolve(ShapeShadowPreset.None);
    public static ShapeShadowValues Subtle() => Resolve(ShapeShadowPreset.Subtle);
    public static ShapeShadowValues Offset() => Resolve(ShapeShadowPreset.Offset);
    public static ShapeGlowValues GlowNone() => ResolveGlow(ShapeGlowPreset.None);
    public static ShapeGlowValues GlowSubtle() => ResolveGlow(ShapeGlowPreset.Subtle);
    public static ShapeGlowValues GlowStrong() => ResolveGlow(ShapeGlowPreset.Strong);
    public static ShapeSoftEdgeValues SoftEdgeNone() => ResolveSoftEdge(ShapeSoftEdgePreset.None);
    public static ShapeSoftEdgeValues SoftEdgeSubtle() => ResolveSoftEdge(ShapeSoftEdgePreset.Subtle);
    public static ShapeSoftEdgeValues SoftEdgeStrong() => ResolveSoftEdge(ShapeSoftEdgePreset.Strong);

    public static ShapeShadowValues Resolve(ShapeShadowPreset preset) => preset switch
    {
        ShapeShadowPreset.None => ShapeShadowValues.None,
        ShapeShadowPreset.Subtle => Create(alpha: 0x55, blurPt: 4, distancePt: 2),
        ShapeShadowPreset.Offset => Create(alpha: 0x80, blurPt: 6, distancePt: 4),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown shape shadow preset."),
    };

    public static ShapeGlowValues ResolveGlow(ShapeGlowPreset preset) => preset switch
    {
        ShapeGlowPreset.None => ShapeGlowValues.None,
        ShapeGlowPreset.Subtle => CreateGlow(alpha: 0x66, radiusPt: 4),
        ShapeGlowPreset.Strong => CreateGlow(alpha: 0xA0, radiusPt: 8),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown shape glow preset."),
    };

    public static ShapeSoftEdgeValues ResolveSoftEdge(ShapeSoftEdgePreset preset) => preset switch
    {
        ShapeSoftEdgePreset.None => ShapeSoftEdgeValues.None,
        ShapeSoftEdgePreset.Subtle => new(true, PointsToEmu(4)),
        ShapeSoftEdgePreset.Strong => new(true, PointsToEmu(8)),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown shape soft-edge preset."),
    };

    private static ShapeShadowValues Create(byte alpha, double blurPt, double distancePt) => new(
        Enabled: true,
        Color: SrgbColor.Black,
        Alpha: alpha,
        BlurRadEmu: PointsToEmu(blurPt),
        DistEmu: PointsToEmu(distancePt),
        DirDeg: 45);

    private static ShapeGlowValues CreateGlow(byte alpha, double radiusPt) => new(
        Enabled: true,
        Color: new SrgbColor(0xFF, 0xC0, 0x00),
        Alpha: alpha,
        RadiusEmu: PointsToEmu(radiusPt));

    private static long PointsToEmu(double points) =>
        checked((long)Math.Round(points / 72.0 * DrawingMlCoordinateUnits.EmuPerInch));
}
