using System.Globalization;

namespace Free.Shared.Opc;

/// <summary>
/// DrawingML / OOXML unit constants and conversions shared across all sibling apps.
/// <list type="bullet">
/// <item>EMU (English Metric Unit) — the native DrawingML coordinate unit: 914 400 per inch, 12 700 per point.</item>
/// <item>dxa — twentieths of a point (twips), used in WordprocessingML for paragraph/table sizes.</item>
/// <item>half-points — run font-size unit in WordprocessingML (w:sz).</item>
/// <item>eighth-points — border-width unit in WordprocessingML (w:sz on w:pBdr / w:tblBorders).</item>
/// </list>
/// All formulas are byte-identical to the originals in <c>Ooxml</c> (FreeW.Core.IO).
/// </summary>
public static class DrawingMlUnits
{
    /// <summary>EMU per point (DrawingML/ECMA-376). 12 700 EMU = 1 pt; 914 400 EMU = 1 in.</summary>
    public const long EmuPerPoint = 12700;

    /// <summary>EMU per inch (DrawingML/ECMA-376). 914 400 EMU = 1 in.</summary>
    public const long EmuPerInch = 914400;

    /// <summary>Converts a point value to the nearest integer number of EMU.</summary>
    public static long PointsToEmu(double points) => (long)Math.Round(points * EmuPerPoint);

    /// <summary>
    /// Parses an EMU string attribute and converts to points.
    /// Returns 0 when the value is null, empty, or not a valid integer.
    /// </summary>
    public static double EmuToPoints(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v / (double)EmuPerPoint
            : 0;

    /// <summary>OOXML "dxa" = twentieths of a point. Parses an integer dxa attribute and converts to points.</summary>
    public static double DxaToPoints(string? value) => ParseInt(value) / 20.0;

    /// <summary>Converts a point value to the nearest integer number of dxa (twentieths of a point).</summary>
    public static int PointsToDxa(double points) => (int)Math.Round(points * 20.0);

    /// <summary>
    /// Run font size is in half-points (w:sz). Parses the attribute and converts to points.
    /// Returns <see langword="null"/> when the value is absent or zero.
    /// </summary>
    public static double? HalfPointsToPoints(string? value) =>
        ParseInt(value) is var v && v != 0 ? v / 2.0 : null;

    /// <summary>Converts a point value to the nearest integer number of half-points.</summary>
    public static int PointsToHalfPoints(double points) => (int)Math.Round(points * 2.0);

    /// <summary>Border widths (w:sz on w:pBdr / w:tblBorders edges) are in eighths of a point.</summary>
    public static double EighthPointsToPoints(string? value) => ParseInt(value) / 8.0;

    /// <summary>Converts a point value to the nearest integer number of eighth-points (minimum 1).</summary>
    public static int PointsToEighthPoints(double points) => Math.Max(1, (int)Math.Round(points * 8.0));

    /// <summary>
    /// Parses an integer attribute value using invariant culture.
    /// Returns 0 when the value is null, empty, or not a valid integer.
    /// </summary>
    public static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
