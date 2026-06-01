using FreeX.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Windows;

namespace FreeX.App.UI;

public static class ConditionalIconLayoutPlanner
{
    private const double ConditionalIconGutterWidth = 20;
    private const double ConditionalIconSize = 10;
    private static readonly ConcurrentDictionary<string, ConditionalIconStyleTraits> StyleTraitCache = new(StringComparer.Ordinal);

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
        var traits = ResolveStyleTraits(icon.Style);
        return traits.GlyphKind;
    }

    private static ConditionalIconStyleTraits ResolveStyleTraits(string? style)
    {
        style ??= "";
        if (StyleTraitCache.TryGetValue(style, out var cached))
            return cached;

        if (style.Contains("TrafficLights", StringComparison.OrdinalIgnoreCase) ||
            style.Contains("RedToBlack", StringComparison.OrdinalIgnoreCase))
            return CacheStyleTraits(style, ConditionalIconGlyphKind.TrafficLight);
        if (style.Contains("Signs", StringComparison.OrdinalIgnoreCase))
            return CacheStyleTraits(style, ConditionalIconGlyphKind.Sign);
        if (style.Contains("Symbols", StringComparison.OrdinalIgnoreCase))
            return CacheStyleTraits(style, ConditionalIconGlyphKind.Symbol);
        if (style.Contains("Flags", StringComparison.OrdinalIgnoreCase))
            return CacheStyleTraits(style, ConditionalIconGlyphKind.Flag);
        if (style.Contains("Rating", StringComparison.OrdinalIgnoreCase))
            return CacheStyleTraits(style, ConditionalIconGlyphKind.Rating);
        if (style.Contains("Quarters", StringComparison.OrdinalIgnoreCase))
            return CacheStyleTraits(style, ConditionalIconGlyphKind.Quarter);
        if (style.Contains("Boxes", StringComparison.OrdinalIgnoreCase))
            return CacheStyleTraits(style, ConditionalIconGlyphKind.Box);
        return CacheStyleTraits(style, ConditionalIconGlyphKind.Arrow);
    }

    private static ConditionalIconStyleTraits CacheStyleTraits(string style, ConditionalIconGlyphKind glyphKind)
    {
        var traits = new ConditionalIconStyleTraits(
            glyphKind,
            style.Contains("Gray", StringComparison.OrdinalIgnoreCase));
        return StyleTraitCache.GetOrAdd(style, traits);
    }

    public static string ResolveColor(ConditionalFormatIcon icon)
    {
        if (ResolveStyleTraits(icon.Style).IsGray)
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

    private readonly record struct ConditionalIconStyleTraits(
        ConditionalIconGlyphKind GlyphKind,
        bool IsGray);
}
