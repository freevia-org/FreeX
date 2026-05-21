using Freexcel.Core.Model;
using System;
using System.Windows;

namespace Freexcel.App.UI;

public static class ConditionalIconLayoutPlanner
{
    private const double ConditionalIconGutterWidth = 20;
    private const double ConditionalIconSize = 10;

    public static ConditionalIconCellLayout CalculateCellLayout(
        Rect cellRect,
        ConditionalFormatIcon icon)
    {
        var size = Math.Min(ConditionalIconSize, Math.Max(6, cellRect.Height - 6));
        var iconRect = new Rect(
            Math.Round(cellRect.Left + 4),
            Math.Round(cellRect.Top + (cellRect.Height - size) / 2),
            size,
            size);

        if (!icon.ShowValue)
            return new ConditionalIconCellLayout(iconRect, Rect.Empty, ShouldDrawText: false);

        var textLeft = Math.Min(cellRect.Right, cellRect.Left + ConditionalIconGutterWidth);
        var textRect = new Rect(
            textLeft,
            cellRect.Top,
            Math.Max(0, cellRect.Right - textLeft),
            cellRect.Height);
        return new ConditionalIconCellLayout(iconRect, textRect, ShouldDrawText: true);
    }

    public static ConditionalIconGlyphKind ResolveGlyphKind(ConditionalFormatIcon icon)
    {
        var style = icon.Style ?? "";
        if (style.Contains("TrafficLights", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("RedToBlack", StringComparison.OrdinalIgnoreCase))
            return ConditionalIconGlyphKind.TrafficLight;
        if (style.Contains("Signs", StringComparison.OrdinalIgnoreCase))
            return ConditionalIconGlyphKind.Sign;
        if (style.Contains("Symbols", StringComparison.OrdinalIgnoreCase))
            return ConditionalIconGlyphKind.Symbol;
        if (style.Contains("Flags", StringComparison.OrdinalIgnoreCase))
            return ConditionalIconGlyphKind.Flag;
        if (style.Contains("Rating", StringComparison.OrdinalIgnoreCase))
            return ConditionalIconGlyphKind.Rating;
        if (style.Contains("Quarters", StringComparison.OrdinalIgnoreCase))
            return ConditionalIconGlyphKind.Quarter;
        if (style.Contains("Boxes", StringComparison.OrdinalIgnoreCase))
            return ConditionalIconGlyphKind.Box;
        return ConditionalIconGlyphKind.Arrow;
    }

    public static string ResolveColor(ConditionalFormatIcon icon)
    {
        if (icon.Style.Contains("Gray", StringComparison.OrdinalIgnoreCase))
            return "#666666";

        var index = Math.Clamp(icon.IconIndex, 0, Math.Max(0, icon.IconCount - 1));
        return icon.IconCount switch
        {
            >= 5 => index switch
            {
                0 => "#C00000",
                1 => "#ED7D31",
                2 => "#FFC000",
                3 => "#92D050",
                _ => "#00B050"
            },
            4 => index switch
            {
                0 => "#C00000",
                1 => "#FFC000",
                2 => "#92D050",
                _ => "#00B050"
            },
            _ => index switch
            {
                0 => "#C00000",
                1 => "#FFC000",
                _ => "#00B050"
            }
        };
    }
}
