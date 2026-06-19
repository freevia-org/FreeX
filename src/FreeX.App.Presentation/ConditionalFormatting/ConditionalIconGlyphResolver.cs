using System;
using System.Collections.Concurrent;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Framework-neutral classification of a conditional-format icon-set glyph. Shared by the desktop
/// renderer and the cross-platform renderer so both draw the same shapes.
/// </summary>
public enum ConditionalIconGlyphKind
{
    Arrow,
    TrafficLight,
    Sign,
    Symbol,
    Flag,
    Rating,
    Quarter,
    Box,
}

/// <summary>
/// Portable, single-source mapping from an icon-set <em>style name</em> (and the resolved bucket
/// index/count) to the glyph kind and fill color a renderer should draw. Pure decision logic with no
/// UI-framework dependencies, so it can be unit-tested and reused across hosts. This is the source
/// of truth previously inlined in the desktop <c>ConditionalIconLayoutPlanner</c> and re-declared in
/// the cross-platform <c>ConditionalFormatCellRenderPlanner</c>.
/// </summary>
public static class ConditionalIconGlyphResolver
{
    private static readonly ConcurrentDictionary<string, StyleTraits> StyleTraitCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve the glyph kind for an icon-set style name (e.g. <c>"3TrafficLights1"</c>).
    /// </summary>
    public static ConditionalIconGlyphKind ResolveGlyphKind(string? style) =>
        ResolveStyleTraits(style).GlyphKind;

    /// <summary>
    /// Resolve the icon fill color (hex, e.g. <c>"#C00000"</c>) for a bucket, including the
    /// gray-style override used by the <c>*Gray*</c> icon sets.
    /// </summary>
    public static string ResolveIconColor(string? style, int index, int count)
    {
        if (ResolveStyleTraits(style).IsGray)
            return "#666666";

        var clamped = Math.Clamp(index, 0, Math.Max(0, count - 1));
        return count switch
        {
            >= 5 => clamped switch
            {
                0 => "#C00000",
                1 => "#ED7D31",
                2 => "#FFC000",
                3 => "#92D050",
                _ => "#00B050",
            },
            4 => clamped switch
            {
                0 => "#C00000",
                1 => "#FFC000",
                2 => "#92D050",
                _ => "#00B050",
            },
            _ => clamped switch
            {
                0 => "#C00000",
                1 => "#FFC000",
                _ => "#00B050",
            },
        };
    }

    private static StyleTraits ResolveStyleTraits(string? style)
    {
        style ??= string.Empty;
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

    private static StyleTraits CacheStyleTraits(string style, ConditionalIconGlyphKind glyphKind)
    {
        var traits = new StyleTraits(
            glyphKind,
            style.Contains("Gray", StringComparison.OrdinalIgnoreCase));
        return StyleTraitCache.GetOrAdd(style, traits);
    }

    private readonly record struct StyleTraits(
        ConditionalIconGlyphKind GlyphKind,
        bool IsGray);
}
