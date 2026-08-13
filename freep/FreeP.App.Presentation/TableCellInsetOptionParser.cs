using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Resolves stable inset choices and compatible external labels.</summary>
public static class TableCellInsetOptionParser
{
    public static bool TryParse(
        object? value,
        out TableCellInsetSide side,
        out double? insetPt)
    {
        side = default;
        insetPt = null;
        if (FreePRibbonChoiceCatalog.TryResolve(
                value,
                FreePRibbonChoiceCatalog.TableCellInsetChoices,
                out FreePRibbonTableCellInsetChoiceDescriptor descriptor) &&
            IsValidInset(descriptor.InsetPt))
        {
            side = descriptor.Side;
            insetPt = descriptor.InsetPt;
            return true;
        }

        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split(':', 2, StringSplitOptions.TrimEntries);
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

    private static bool IsValidInset(double? insetPt) =>
        insetPt is null || double.IsFinite(insetPt.Value) && insetPt.Value is >= 0 and <= 72;

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
