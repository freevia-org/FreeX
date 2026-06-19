using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

/// <summary>
/// Thin WPF adapter over the portable <see cref="ConditionalIconCellLayoutPlanner"/>: maps the shared
/// neutral geometry into the <see cref="System.Windows.Rect"/>-based <see cref="ConditionalIconCellLayout"/>
/// the desktop renderer consumes.
/// </summary>
public static class ConditionalIconLayoutPlanner
{
    public static ConditionalIconCellLayout CalculateCellLayout(
        Rect cellRect,
        ConditionalFormatIcon icon)
    {
        var layout = ConditionalIconCellLayoutPlanner.CalculateCellLayout(
            cellRect.Left,
            cellRect.Top,
            cellRect.Width,
            cellRect.Height,
            icon.ShowValue);

        var iconRect = new Rect(layout.IconLeft, layout.IconTop, layout.IconSize, layout.IconSize);

        if (!icon.ShowValue)
            return new ConditionalIconCellLayout(iconRect, Rect.Empty, ShouldDrawText: false);

        var textRect = new Rect(layout.TextLeft, cellRect.Top, layout.TextWidth, cellRect.Height);
        return new ConditionalIconCellLayout(iconRect, textRect, ShouldDrawText: layout.ShouldDrawText);
    }

    public static ConditionalIconGlyphKind ResolveGlyphKind(ConditionalFormatIcon icon) =>
        ConditionalIconGlyphResolver.ResolveGlyphKind(icon.Style);

    public static string ResolveColor(ConditionalFormatIcon icon) =>
        ConditionalIconGlyphResolver.ResolveIconColor(icon.Style, icon.IconIndex, icon.IconCount);
}
