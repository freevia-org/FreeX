namespace FreeX.Core.Model;

internal static class WorkbookThemeTint
{
    private const double NeutralTintThreshold = 0.000001d;

    public static CellColor Apply(CellColor color, double tint)
    {
        if (Math.Abs(tint) < NeutralTintThreshold)
            return color;

        var transformed = tint < 0
            ? DrawingMlColorTransform.ApplyLuminance(ToSharedColor(color), 1.0 + tint, 0.0)
            : DrawingMlColorTransform.ApplyTint(ToSharedColor(color), 1.0 - tint);

        return new CellColor(transformed.R, transformed.G, transformed.B);
    }

    private static DrawingMlRgbColor ToSharedColor(CellColor color) =>
        new(color.R, color.G, color.B);
}
