using System.Globalization;

namespace FreeX.Core.IO;

internal static class XlsxChartScalarReader
{
    /// <summary>
    /// r575: IsFinite as well. This is the shared helper behind 68 chart-reader call sites, all
    /// reading attributes out of a chart part: manual-layout X/Y/W/H (where a chart element is
    /// positioned), the chart's page margins, and trendline forward/backward periods. All are
    /// geometry and all are file-controlled.
    ///
    /// <para>Returning null is the contract this helper already has for an attribute it cannot
    /// read, and every caller treats null as "not specified", so an unusable value falls back to
    /// the automatic layout rather than positioning an element at infinity.</para>
    /// </summary>
    public static double? ReadOptionalDouble(string? value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            && double.IsFinite(result))
            return result;

        return null;
    }

    public static int? ReadOptionalInt(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return result;

        return null;
    }

    public static bool? ReadOptionalBool(string? value)
    {
        if (value is null)
            return null;

        return IsTrue(value);
    }

    public static bool IsTrue(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
