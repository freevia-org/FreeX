using System.Globalization;

namespace FreeP.App.Compositor;

/// <summary>Parses the shared ribbon's table-row height selection into EMUs.</summary>
public static class TableRowHeightOptionParser
{
    private const long EmuPerInch = 914400;

    public static bool TryParse(string? value, out long heightEmu)
    {
        heightEmu = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var setting = value.Trim();
        if (setting.Equals("automatic", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("default", StringComparison.OrdinalIgnoreCase))
            return true;

        var number = setting.EndsWith("in", StringComparison.OrdinalIgnoreCase)
            ? setting[..^2].Trim()
            : setting;
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var inches) ||
            double.IsNaN(inches) || double.IsInfinity(inches) || inches <= 0 || inches > 12)
            return false;

        heightEmu = checked((long)Math.Round(inches * EmuPerInch));
        return heightEmu > 0;
    }
}
