using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Maps the shared ribbon's Text Direction choices to DrawingML body orientation values.</summary>
public static class TextVerticalTypeOptionParser
{
    public static bool TryParse(string? value, out TextVerticalType verticalType)
    {
        verticalType = TextVerticalType.Horizontal;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var setting = value.Trim();
        if (setting.Equals("Horizontal", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            return true;

        if (setting.Equals("Rotate 90 degrees", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("Vertical", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("vert", StringComparison.OrdinalIgnoreCase))
        {
            verticalType = TextVerticalType.Vertical;
            return true;
        }

        if (setting.Equals("Rotate 270 degrees", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("Vertical 270", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("vert270", StringComparison.OrdinalIgnoreCase))
        {
            verticalType = TextVerticalType.Vertical270;
            return true;
        }

        if (setting.Equals("East Asian vertical", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("eaVert", StringComparison.OrdinalIgnoreCase))
        {
            verticalType = TextVerticalType.EastAsianVertical;
            return true;
        }

        if (setting.Equals("WordArt vertical", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("wordArtVert", StringComparison.OrdinalIgnoreCase))
        {
            verticalType = TextVerticalType.WordArtVertical;
            return true;
        }

        if (setting.Equals("WordArt vertical RTL", StringComparison.OrdinalIgnoreCase) ||
            setting.Equals("wordArtVertRtl", StringComparison.OrdinalIgnoreCase))
        {
            verticalType = TextVerticalType.WordArtVerticalRtl;
            return true;
        }

        return false;
    }
}
