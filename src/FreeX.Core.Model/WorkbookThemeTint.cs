namespace FreeX.Core.Model;

internal static class WorkbookThemeTint
{
    private const double NeutralTintThreshold = 0.000001d;

    public static CellColor Apply(CellColor color, double tint)
    {
        if (Math.Abs(tint) < NeutralTintThreshold)
            return color;

        return new CellColor(
            Apply(color.R, tint),
            Apply(color.G, tint),
            Apply(color.B, tint));
    }

    private static byte Apply(byte channel, double tint)
    {
        var value = tint < 0
            ? channel * (1.0 + tint)
            : channel + ((255 - channel) * tint);
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }
}
