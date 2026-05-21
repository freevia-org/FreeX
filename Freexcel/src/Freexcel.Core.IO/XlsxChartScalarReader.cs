using System.Globalization;

namespace Freexcel.Core.IO;

internal static class XlsxChartScalarReader
{
    public static double? ReadOptionalDouble(string? value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
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
