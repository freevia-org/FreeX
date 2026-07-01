namespace Free.Shared.Opc;

/// <summary>
/// Compatibility facade for callers that historically imported DrawingML units from Free.Shared.Opc.
/// The neutral implementation lives in <see cref="Free.Shared.Drawing.DrawingMlCoordinateUnits"/>.
/// </summary>
public static class DrawingMlUnits
{
    public const long EmuPerPoint = Free.Shared.Drawing.DrawingMlCoordinateUnits.EmuPerPoint;
    public const long EmuPerInch = Free.Shared.Drawing.DrawingMlCoordinateUnits.EmuPerInch;
    public const long EmuPerPixel = Free.Shared.Drawing.DrawingMlCoordinateUnits.EmuPerPixel;
    public const long AngleUnitsPerDegree = Free.Shared.Drawing.DrawingMlCoordinateUnits.AngleUnitsPerDegree;

    public static long PointsToEmu(double points) => Free.Shared.Drawing.DrawingMlCoordinateUnits.PointsToEmu(points);

    public static double EmuToPoints(double emus) => Free.Shared.Drawing.DrawingMlCoordinateUnits.EmuToPoints(emus);

    public static long PixelsToEmu(double pixels) => Free.Shared.Drawing.DrawingMlCoordinateUnits.PixelsToEmu(pixels);

    public static double EmuToPixels(double emus) => Free.Shared.Drawing.DrawingMlCoordinateUnits.EmuToPixels(emus);

    public static double EmuToPixels(string? value) => Free.Shared.Drawing.DrawingMlCoordinateUnits.EmuToPixels(value);

    public static double EmuToPoints(string? value) => Free.Shared.Drawing.DrawingMlCoordinateUnits.EmuToPoints(value);

    public static double AngleToDegrees(double angleUnits) =>
        Free.Shared.Drawing.DrawingMlCoordinateUnits.AngleToDegrees(angleUnits);

    public static double AngleToRadians(double angleUnits) =>
        Free.Shared.Drawing.DrawingMlCoordinateUnits.AngleToRadians(angleUnits);

    public static double DxaToPoints(string? value) => Free.Shared.Drawing.DrawingMlCoordinateUnits.DxaToPoints(value);

    public static int PointsToDxa(double points) => Free.Shared.Drawing.DrawingMlCoordinateUnits.PointsToDxa(points);

    public static double? HalfPointsToPoints(string? value) =>
        Free.Shared.Drawing.DrawingMlCoordinateUnits.HalfPointsToPoints(value);

    public static int PointsToHalfPoints(double points) =>
        Free.Shared.Drawing.DrawingMlCoordinateUnits.PointsToHalfPoints(points);

    public static double EighthPointsToPoints(string? value) =>
        Free.Shared.Drawing.DrawingMlCoordinateUnits.EighthPointsToPoints(value);

    public static int PointsToEighthPoints(double points) =>
        Free.Shared.Drawing.DrawingMlCoordinateUnits.PointsToEighthPoints(points);

    public static int ParseInt(string? value) => Free.Shared.Drawing.DrawingMlCoordinateUnits.ParseInt(value);
}
