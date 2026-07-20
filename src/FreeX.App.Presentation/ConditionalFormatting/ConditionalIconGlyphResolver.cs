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
    /// <summary>
    /// Graduated rating bars (4Rating / 5Rating): N filled bar columns of M, left-aligned in the
    /// icon rect. The lowest bucket (index 0) is the worst (all bars empty) and the highest is best
    /// (all bars filled). Distinct from <see cref="Star"/> which draws a five-pointed star glyph.
    /// </summary>
    Rating,
    /// <summary>
    /// Five-pointed star with partial fill (3Stars / 5Stars / x14 Stars). The fill fraction is
    /// proportional to the bucket index: bucket 0 = empty outline, bucket count-1 = fully filled.
    /// Intermediate buckets clip the fill horizontally so the left portion is filled and the right
    /// is transparent, matching Excel's partial-star appearance.
    /// </summary>
    Star,
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
    /// True for the alternate icon-set variant within a style family that has two visually distinct
    /// forms sharing the same <see cref="ConditionalIconGlyphKind"/> -- "3 Traffic Lights (Rimmed)"
    /// (style <c>"3TrafficLights2"</c>, vs the default Unrimmed <c>"3TrafficLights1"</c>) and
    /// "3 Symbols (Uncircled)" (style <c>"3Symbols2"</c>, vs the default Circled <c>"3Symbols"</c>) are
    /// the two real Excel gallery presets this distinguishes (R54-render-cf-icon-databar-4-2;
    /// see ConditionalFormatPresetGalleryPlanner.cs / ConditionalFormatPresetFactory.cs). Both members
    /// of a pair otherwise resolve to the exact same glyph kind, so a caller that wants to actually draw
    /// them differently (a rim/bezel ring around the traffic light, no circular backdrop behind the
    /// symbol mark) passes this flag on to <see cref="ConditionalIconGlyphGeometry.Build"/>.
    /// </summary>
    public static bool IsAlternateGlyphVariant(string? style) => ResolveStyleTraits(style).IsAlternateVariant;

    /// <summary>
    /// The fixed gold fill color used for all buckets of a star icon set. Excel renders all star
    /// buckets in the same gold color; the fill fraction (controlled by bucket index) varies how
    /// much of the star outline is filled rather than the hue.
    /// </summary>
    public const string StarGoldHex = "#FFC000";

    /// <summary>
    /// Resolve the icon fill color (hex, e.g. <c>"#C00000"</c>) for a bucket, including the
    /// gray-style override used by the <c>*Gray*</c> icon sets and the fixed-gold override for
    /// star icon sets.
    /// </summary>
    public static string ResolveIconColor(string? style, int index, int count)
    {
        var traits = ResolveStyleTraits(style);
        if (traits.IsGray)
            return "#666666";
        // Star icon sets use a single gold fill regardless of bucket; the fill fraction is what varies.
        if (traits.IsStar)
            return StarGoldHex;

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
        // x14-extension icon sets
        if (style.Contains("Stars", StringComparison.OrdinalIgnoreCase))
            return CacheStyleTraits(style, ConditionalIconGlyphKind.Star);
        if (style.Contains("Triangles", StringComparison.OrdinalIgnoreCase))
            return CacheStyleTraits(style, ConditionalIconGlyphKind.Arrow);
        return CacheStyleTraits(style, ConditionalIconGlyphKind.Arrow);
    }

    private static StyleTraits CacheStyleTraits(string style, ConditionalIconGlyphKind glyphKind)
    {
        var traits = new StyleTraits(
            glyphKind,
            style.Contains("Gray", StringComparison.OrdinalIgnoreCase),
            glyphKind == ConditionalIconGlyphKind.Star,
            // "3TrafficLights2" (Rimmed) / "3Symbols2" (Uncircled) are the only real style names in
            // their respective families with a trailing "2"; the default/unsuffixed member of each
            // pair ("3TrafficLights1", "3Symbols") is the primary variant.
            IsAlternateVariant: style.EndsWith("2", StringComparison.Ordinal));
        return StyleTraitCache.GetOrAdd(style, traits);
    }

    private readonly record struct StyleTraits(
        ConditionalIconGlyphKind GlyphKind,
        bool IsGray,
        bool IsStar = false,
        bool IsAlternateVariant = false);
}
