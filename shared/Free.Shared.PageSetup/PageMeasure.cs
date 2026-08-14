namespace Free.Shared.PageSetup;

/// <summary>The physical length units the paired page-setup dialogs work in.</summary>
public enum PageMeasureUnit
{
    Inch,
    Point,
    Centimetre,
    Millimetre,
}

/// <summary>
/// Page-geometry unit conversion shared by every app's page-setup surface. FreeX stores page
/// geometry in inches, FreeW in points, and the paper catalog is authored in millimetres, so all
/// three meet here instead of each app carrying its own constants.
/// </summary>
public static class PageMeasure
{
    public const double PointsPerInch = 72.0;
    public const double MillimetresPerInch = 25.4;
    public const double CentimetresPerInch = 2.54;
    public const double PointsPerMillimetre = PointsPerInch / MillimetresPerInch;

    public static double InchesToPoints(double inches) => inches * PointsPerInch;
    public static double PointsToInches(double points) => points / PointsPerInch;

    public static double InchesToMillimetres(double inches) => inches * MillimetresPerInch;
    public static double MillimetresToInches(double millimetres) => millimetres / MillimetresPerInch;

    public static double InchesToCentimetres(double inches) => inches * CentimetresPerInch;
    public static double CentimetresToInches(double centimetres) => centimetres / CentimetresPerInch;

    public static double MillimetresToPoints(double millimetres) => millimetres * PointsPerMillimetre;
    public static double PointsToMillimetres(double points) => points / PointsPerMillimetre;

    public static double CentimetresToPoints(double centimetres) => MillimetresToPoints(centimetres * 10.0);
    public static double PointsToCentimetres(double points) => PointsToMillimetres(points) / 10.0;

    /// <summary>Converts <paramref name="value"/> between any two supported units.</summary>
    public static double Convert(double value, PageMeasureUnit from, PageMeasureUnit to)
    {
        if (from == to)
            return value;

        var inches = from switch
        {
            PageMeasureUnit.Inch => value,
            PageMeasureUnit.Point => PointsToInches(value),
            PageMeasureUnit.Centimetre => CentimetresToInches(value),
            PageMeasureUnit.Millimetre => MillimetresToInches(value),
            _ => throw new ArgumentOutOfRangeException(nameof(from)),
        };

        return to switch
        {
            PageMeasureUnit.Inch => inches,
            PageMeasureUnit.Point => InchesToPoints(inches),
            PageMeasureUnit.Centimetre => InchesToCentimetres(inches),
            PageMeasureUnit.Millimetre => InchesToMillimetres(inches),
            _ => throw new ArgumentOutOfRangeException(nameof(to)),
        };
    }

    /// <summary>
    /// Converts and then rounds away from zero to <paramref name="digits"/> decimals. The paper
    /// catalog uses this so each app's historical rounded literals (FreeX: inches at two decimals,
    /// FreeW: points at one decimal) are reproduced from the one canonical table.
    /// </summary>
    public static double ConvertRounded(double value, PageMeasureUnit from, PageMeasureUnit to, int digits) =>
        Math.Round(Convert(value, from, to), digits, MidpointRounding.AwayFromZero);
}
