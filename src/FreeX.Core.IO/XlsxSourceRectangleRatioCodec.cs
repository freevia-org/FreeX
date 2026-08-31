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
                out var parsed))
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
