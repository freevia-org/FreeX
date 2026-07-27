using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Parses the shared ribbon's active-cell inset selection.</summary>
public static class TableCellInsetOptionParser
{
    public static bool TryParse(
        string? value,
        out TableCellInsetSide side,
        out double? insetPt)
    {
        side = default;
        insetPt = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryParseSide(parts[0], out side))
            return false;

        var setting = parts[1].Trim();
        if (setting.Equals("automatic", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("default", StringComparison.OrdinalIgnoreCase))
            return true;

        var number = setting.EndsWith("pt", StringComparison.OrdinalIgnoreCase)
            ? setting[..^2].Trim()
            : setting;
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0 || parsed > 72)
            return false;

        insetPt = parsed;
        return true;
    }

    private static bool TryParseSide(string value, out TableCellInsetSide side)
    {
        side = value.Trim().ToLowerInvariant() switch
        {
            "all" => TableCellInsetSide.All,
            "left" => TableCellInsetSide.Left,
            "right" => TableCellInsetSide.Right,
            "top" => TableCellInsetSide.Top,
            "bottom" => TableCellInsetSide.Bottom,
            _ => default,
        };

        return value.Trim().ToLowerInvariant() is "all" or "left" or "right" or "top" or "bottom";
    }
}
