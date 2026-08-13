using Free.Shared.Drawing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>Chooses a renderer-neutral foreground color for WordArt fill material.</summary>
public static class WordArtForegroundPolicy
{
    private const double LightForegroundLuminanceThreshold = 0.42;

    public static string ResolveColorHex(WordArtStyle style, DrawingObjectFillPlan fill)
    {
        ArgumentNullException.ThrowIfNull(fill);

        if (style == WordArtStyle.GlowGold)
            return "#D8BA66";

        var backgroundHex = fill.ColorHex
            ?? fill.GradientStops.FirstOrDefault()?.ColorHex
            ?? fill.PatternBackgroundColorHex
            ?? fill.PatternForegroundColorHex;
        if (!DrawingMlRgbColor.TryParseHexRgb(backgroundHex, out var background))
            return "#FFFFFF";

        var luminance = (
            0.2126 * background.R +
            0.7152 * background.G +
            0.0722 * background.B) / 255.0;
        return luminance < LightForegroundLuminanceThreshold ? "#FFFFFF" : "#000000";
    }
}
