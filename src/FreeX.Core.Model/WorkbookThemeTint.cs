namespace FreeX.Core.Model;

internal static class WorkbookThemeTint
{
    private const double NeutralTintThreshold = 0.000001d;

    public static CellColor Apply(CellColor color, double tint)
    {
        if (Math.Abs(tint) < NeutralTintThreshold)
            return color;

        // Excel resolves theme tint via HSL-luminance modulation (lumMod/lumOff), not the
        // DrawingML shape-fill <a:tint> linear-RGB-toward-white blend. This mirrors the values
        // XlsxDrawingColorTint.ApplyTo writes: positive tint -> lumMod=1-tint, lumOff=tint;
        // negative tint -> lumMod=1+tint. Keeping read/write symmetric round-trips correctly.
        var transformed = tint < 0
            ? DrawingMlColorTransform.ApplyLuminance(ToSharedColor(color), 1.0 + tint, 0.0)
            : DrawingMlColorTransform.ApplyLuminance(ToSharedColor(color), 1.0 - tint, tint);

        return new CellColor(transformed.R, transformed.G, transformed.B);
    }

    private static DrawingMlRgbColor ToSharedColor(CellColor color) =>
        new(color.R, color.G, color.B);
}
