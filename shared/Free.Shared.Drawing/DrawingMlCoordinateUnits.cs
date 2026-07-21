using System.Globalization;

namespace Free.Shared.Drawing;

/// <summary>
/// DrawingML / OOXML unit constants and conversions shared across model, IO, and rendering layers.
/// </summary>
public static class DrawingMlCoordinateUnits
{
    /// <summary>EMU per point. 12,700 EMU = 1 pt.</summary>
    public const long EmuPerPoint = 12700;

    /// <summary>EMU per inch. 914,400 EMU = 1 in.</summary>
    public const long EmuPerInch = 914400;

    /// <summary>EMU per DIP pixel at 96 DPI. 9,525 EMU = 1 px.</summary>
    public const long EmuPerPixel = 9525;

    /// <summary>OOXML angle units per degree.</summary>
    public const long AngleUnitsPerDegree = 60000;

    public static long PointsToEmu(double points) => (long)Math.Round(points * EmuPerPoint);

    public static double EmuToPoints(double emus) => emus / EmuPerPoint;

    public static long PixelsToEmu(double pixels) => (long)Math.Round(Math.Max(0, pixels) * EmuPerPixel);

    /// <summary>
    /// Converts pixels to EMU without clamping negative values to zero. Use this only for
    /// coordinates that are genuinely signed in the OOXML schema (e.g. absoluteAnchor's
    /// CT_Point2D pos x/y, which uses ST_AdjCoordinate/ST_Coordinate and can legitimately be
    /// negative). Non-negative-only coordinates (offsets, extents) must keep using
    /// <see cref="PixelsToEmu"/>.
    /// </summary>
    public static long PixelsToEmuSigned(double pixels) => (long)Math.Round(pixels * EmuPerPixel);

    public static double EmuToPixels(double emus) => emus / EmuPerPixel;

    public static double EmuToPixels(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var emus)
            ? EmuToPixels(emus)
            : 0;

    public static double EmuToPoints(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? EmuToPoints(v)
            : 0;

    public static double AngleToDegrees(double angleUnits) => angleUnits / AngleUnitsPerDegree;

    public static double AngleToRadians(double angleUnits) => AngleToDegrees(angleUnits) * Math.PI / 180d;

    public static double DxaToPoints(string? value) => ParseInt(value) / 20.0;

    public static int PointsToDxa(double points) => (int)Math.Round(points * 20.0);

    public static double? HalfPointsToPoints(string? value) =>
        ParseInt(value) is var v && v != 0 ? v / 2.0 : null;

    public static int PointsToHalfPoints(double points) => (int)Math.Round(points * 2.0);

    public static double EighthPointsToPoints(string? value) => ParseInt(value) / 8.0;

    public static int PointsToEighthPoints(double points) => Math.Max(1, (int)Math.Round(points * 8.0));

    public static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
