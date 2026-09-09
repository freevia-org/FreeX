using System.Globalization;

namespace FreeX.Core.IO;

/// <summary>
/// Converts picture crop ratios to and from DrawingML <c>srcRect</c> percentage units.
/// </summary>
internal static class XlsxSourceRectangleRatioCodec
{
    private const double PercentageUnitsPerRatio = 100000d;

    public static double Parse(string? value)
    {
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            // r577: Math.Clamp below bounds Infinity but PROPAGATES NaN, so the clamp alone let a
            // srcRect percentage of "NaN" become a NaN crop ratio on the picture. 0 is this
            // method's existing "unreadable" result: no crop.
            !double.IsFinite(parsed))
        {
            return 0;
        }

        // Negative values are valid outward crops. Mirror Excel's positive and negative bounds.
        return Math.Clamp(parsed / PercentageUnitsPerRatio, -1, 1);
    }

    public static string Format(double ratio) =>
        ((int)Math.Round(Math.Clamp(ratio, -1, 1) * PercentageUnitsPerRatio))
        .ToString(CultureInfo.InvariantCulture);
}
