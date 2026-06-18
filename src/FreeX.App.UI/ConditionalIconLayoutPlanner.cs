using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;
using System;
using System.Windows;

namespace FreeX.App.UI;

public static class ConditionalIconLayoutPlanner
{
    private const double ConditionalIconGutterWidth = 20;
    private const double ConditionalIconSize = 10;

    public static ConditionalIconCellLayout CalculateCellLayout(
        Rect cellRect,
        ConditionalFormatIcon icon)
    {
        var size = Math.Min(
            ConditionalIconSize,
            Math.Min(
                Math.Max(0, cellRect.Width - 8),
                Math.Max(0, cellRect.Height - 6)));
        var iconLeft = Math.Clamp(cellRect.Left + 4, cellRect.Left, cellRect.Right - size);
        var iconRect = new Rect(
            Math.Round(iconLeft),
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
        return new ConditionalIconCellLayout(
            iconRect,
            textRect,
            ShouldDrawText: textRect.Width > 0 && textRect.Height > 0);
    }

    public static ConditionalIconGlyphKind ResolveGlyphKind(ConditionalFormatIcon icon) =>
        ConditionalIconGlyphResolver.ResolveGlyphKind(icon.Style);

    public static string ResolveColor(ConditionalFormatIcon icon) =>
        ConditionalIconGlyphResolver.ResolveIconColor(icon.Style, icon.IconIndex, icon.IconCount);
}
