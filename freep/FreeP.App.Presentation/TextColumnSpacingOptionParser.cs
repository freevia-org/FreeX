using System.Globalization;

namespace FreeP.App.Compositor;

/// <summary>Maps text-column spacing ribbon choices to DrawingML EMU.</summary>
public static class TextColumnSpacingOptionParser
{
    private const double EmuPerPoint = 12_700;

    public static bool TryParse(string? value, out long spacingEmu)
    {
        spacingEmu = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var setting = value.Trim();
        if (setting.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            setting = setting[..^2].Trim();

        if (!double.TryParse(setting, NumberStyles.Float, CultureInfo.InvariantCulture, out var points)
            || !double.IsFinite(points)
            || points < 0
            || points > 144)
            return false;

        spacingEmu = checked((long)Math.Round(points * EmuPerPoint, MidpointRounding.AwayFromZero));
        return true;
    }
}
