using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public enum ShapeShadowPreset
{
    None,
    Subtle,
    Offset,
}

/// <summary>PowerPoint-style shape shadow presets backed by the shared effect model.</summary>
public static class ShapeEffectAuthoringPlanner
{
    public const string NoneCommandId = "freep.shape.shadow.none";
    public const string SubtleCommandId = "freep.shape.shadow.subtle";
    public const string OffsetCommandId = "freep.shape.shadow.offset";

    public static ShapeShadowValues None() => Resolve(ShapeShadowPreset.None);
    public static ShapeShadowValues Subtle() => Resolve(ShapeShadowPreset.Subtle);
    public static ShapeShadowValues Offset() => Resolve(ShapeShadowPreset.Offset);

    public static ShapeShadowValues Resolve(ShapeShadowPreset preset) => preset switch
    {
        ShapeShadowPreset.None => ShapeShadowValues.None,
        ShapeShadowPreset.Subtle => Create(alpha: 0x55, blurPt: 4, distancePt: 2),
        ShapeShadowPreset.Offset => Create(alpha: 0x80, blurPt: 6, distancePt: 4),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown shape shadow preset."),
    };

    private static ShapeShadowValues Create(byte alpha, double blurPt, double distancePt) => new(
        Enabled: true,
        Color: SrgbColor.Black,
        Alpha: alpha,
        BlurRadEmu: PointsToEmu(blurPt),
        DistEmu: PointsToEmu(distancePt),
        DirDeg: 45);

    private static long PointsToEmu(double points) =>
        checked((long)Math.Round(points / 72.0 * DrawingMlCoordinateUnits.EmuPerInch));
}
