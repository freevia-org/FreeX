using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Resolves stable border choices and compatible external labels into model primitives.</summary>
public static class TableCellBorderOptionParser
{
    public static bool TryParse(
        object? value,
        out TableCellBorderSide side,
        out ShapeOutline? outline)
    {
        side = default;
        outline = null;
        if (FreePRibbonChoiceCatalog.TryResolve(
                value,
                FreePRibbonChoiceCatalog.TableCellBorderChoices,
                out FreePRibbonTableCellBorderChoiceDescriptor descriptor))
        {
            side = descriptor.Side;
            outline = descriptor.Outline;
            return true;
        }

        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryParseSide(parts[0], out side))
            return false;

        var style = parts[1].Trim();
        if (style.Equals("automatic", StringComparison.OrdinalIgnoreCase) ||
            style.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            style.Equals("default", StringComparison.OrdinalIgnoreCase))
            return true;

        if (style.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            outline = ShapeOutline.None.Instance;
            return true;
        }

        if (style.Equals("black 0.5pt", StringComparison.OrdinalIgnoreCase))
        {
            outline = new ShapeOutline.Visible(ThemeAwareColor.Black, 0.5);
            return true;
        }

        if (style.Equals("black 1pt", StringComparison.OrdinalIgnoreCase))
        {
            outline = new ShapeOutline.Visible(ThemeAwareColor.Black, 1.0);
            return true;
        }

        return false;
    }

    private static bool TryParseSide(string value, out TableCellBorderSide side)
    {
        side = value.Trim().ToLowerInvariant() switch
        {
            "left" => TableCellBorderSide.Left,
            "right" => TableCellBorderSide.Right,
            "top" => TableCellBorderSide.Top,
            "bottom" => TableCellBorderSide.Bottom,
            "diagonaldown" or "diagonal-down" or "tl-to-br" => TableCellBorderSide.DiagonalDown,
            "diagonalup" or "diagonal-up" or "bl-to-tr" => TableCellBorderSide.DiagonalUp,
            _ => default,
        };

        return value.Trim().ToLowerInvariant() is "left" or "right" or "top" or "bottom"
            or "diagonaldown" or "diagonal-down" or "tl-to-br"
            or "diagonalup" or "diagonal-up" or "bl-to-tr";
    }
}
