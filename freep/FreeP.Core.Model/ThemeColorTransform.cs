using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>
/// Applies DrawingML theme color transforms to resolved sRGB colors.
/// </summary>
public static class ThemeColorTransform
{
    /// <summary>
    /// Applies the DrawingML transform order used by scheme colors: lumMod/lumOff, then tint, then shade.
    /// </summary>
    public static SrgbColor Apply(
        SrgbColor baseColor,
        double lumMod = 1.0,
        double lumOff = 0.0,
        double tint = 1.0,
        double shade = 1.0) =>
        FromSharedColor(DrawingMlColorTransform.Apply(
            ToSharedColor(baseColor),
            lumMod,
            lumOff,
            tint,
            shade));

    /// <summary>
    /// Applies lumMod/lumOff by converting to HLS, computing L' = clamp(L * lumMod + lumOff), and converting back.
    /// </summary>
    public static SrgbColor ApplyLuminance(SrgbColor baseColor, double lumMod, double lumOff) =>
        FromSharedColor(DrawingMlColorTransform.ApplyLuminance(ToSharedColor(baseColor), lumMod, lumOff));

    /// <summary>
    /// Applies DrawingML tint, where 1.0 preserves the color and 0.0 blends fully to white.
    /// </summary>
    public static SrgbColor ApplyTint(SrgbColor baseColor, double tintFraction) =>
        FromSharedColor(DrawingMlColorTransform.ApplyTint(ToSharedColor(baseColor), tintFraction));

    /// <summary>
    /// Applies DrawingML shade, where 1.0 preserves the color and 0.0 blends fully to black.
    /// </summary>
    public static SrgbColor ApplyShade(SrgbColor baseColor, double shadeFraction) =>
        FromSharedColor(DrawingMlColorTransform.ApplyShade(ToSharedColor(baseColor), shadeFraction));

    private static DrawingMlRgbColor ToSharedColor(SrgbColor color) =>
        new(color.R, color.G, color.B);

    private static SrgbColor FromSharedColor(DrawingMlRgbColor color) =>
        new(color.R, color.G, color.B);
}
