using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Resolves a <see cref="ThemeAwareColor"/> to a concrete <see cref="SrgbColor"/>.
/// When a <see cref="SchemeColorRef"/> is present, the color is looked up from the live theme
/// and luminance modifiers (lumMod / lumOff) are applied using HLS arithmetic.
/// When the scheme ref is absent the pre-resolved <see cref="ThemeAwareColor.Resolved"/> is used.
/// </summary>
public static class ThemeColorResolver
{
    /// <summary>
    /// Default ECMA-376 role→slot map (identity for canonical slot names; role aliases mapped to canonical slots).
    /// Used when no master clrMap is available.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ThemeColorSlot> DefaultClrMap =
        new Dictionary<string, ThemeColorSlot>(StringComparer.OrdinalIgnoreCase)
        {
            ["dk1"]      = ThemeColorSlot.Dk1,
            ["tx1"]      = ThemeColorSlot.Dk1,   // tx1 aliases dk1 by default
            ["lt1"]      = ThemeColorSlot.Lt1,
            ["bg1"]      = ThemeColorSlot.Lt1,   // bg1 aliases lt1 by default
            ["dk2"]      = ThemeColorSlot.Dk2,
            ["tx2"]      = ThemeColorSlot.Dk2,   // tx2 aliases dk2 by default
            ["lt2"]      = ThemeColorSlot.Lt2,
            ["bg2"]      = ThemeColorSlot.Lt2,   // bg2 aliases lt2 by default
            ["accent1"]  = ThemeColorSlot.Accent1,
            ["accent2"]  = ThemeColorSlot.Accent2,
            ["accent3"]  = ThemeColorSlot.Accent3,
            ["accent4"]  = ThemeColorSlot.Accent4,
            ["accent5"]  = ThemeColorSlot.Accent5,
            ["accent6"]  = ThemeColorSlot.Accent6,
            ["hlink"]    = ThemeColorSlot.HLink,
            ["folhlink"] = ThemeColorSlot.FolHLink,
        };

    /// <summary>
    /// Maps a raw OOXML role name through the effective clrMap to a <see cref="ThemeColorSlot"/>.
    /// The effective clrMap maps role names (e.g. "tx1") to canonical slot names (e.g. "lt1" or "dk1").
    /// Resolution order: effectiveClrMap[role] → canonical slot name → ThemeColorSlot enum.
    /// Falls back to the default map when no effectiveClrMap is supplied, or the role is not found.
    /// </summary>
    /// <param name="roleName">Raw OOXML val= string, e.g. "tx1", "bg1", "accent1".</param>
    /// <param name="effectiveClrMap">
    /// The master's (or slide's override) p:clrMap / a:overrideClrMapping attributes as a
    /// role→slotName dictionary (e.g. ["tx1"]="lt1"), or null to use the default mapping.
    /// </param>
    /// <param name="fallbackSlot">
    /// Slot to use when <paramref name="roleName"/> is not found in <see cref="DefaultClrMap"/>.
    /// Callers should pass <see cref="SchemeColorRef.Slot"/> so that
    /// <see cref="SchemeColorRef"/> objects created without a RoleName (e.g. in tests or by
    /// older code paths) still resolve correctly via their already-computed Slot.
    /// </param>
    public static ThemeColorSlot MapRoleToSlot(string? roleName, IReadOnlyDictionary<string, string>? effectiveClrMap,
        ThemeColorSlot fallbackSlot = ThemeColorSlot.Dk1)
    {
        // No role name means this SchemeColorRef was built programmatically with a direct Slot;
        // use the fallback slot (schemeRef.Slot) directly — no clrMap indirection needed.
        if (string.IsNullOrEmpty(roleName))
            return fallbackSlot;

        if (effectiveClrMap is not null && effectiveClrMap.TryGetValue(roleName, out var targetSlotName))
        {
            // The clrMap value is a canonical slot name (dk1/lt1/dk2/lt2/accent1-6/hlink/folHlink).
            // Map that target slot name to the enum — use the default map for canonical resolution.
            if (DefaultClrMap.TryGetValue(targetSlotName, out var mappedSlot))
                return mappedSlot;
        }
        // Fallback: use the default map directly on the role name.
        // If the role name is not in the default map (unknown future roles), use the pre-computed fallbackSlot.
        return DefaultClrMap.TryGetValue(roleName, out var defaultSlot) ? defaultSlot : fallbackSlot;
    }

    /// <summary>
    /// Resolves <paramref name="color"/> against <paramref name="theme"/>.
    /// If <paramref name="theme"/> is null the pre-resolved value is returned as-is.
    /// </summary>
    public static SrgbColor Resolve(ThemeAwareColor color, PresentationTheme? theme)
        => Resolve(color, theme, effectiveClrMap: null);

    /// <summary>
    /// Resolves <paramref name="color"/> against <paramref name="theme"/>, applying
    /// <paramref name="effectiveClrMap"/> (slide override ?? master p:clrMap) for role→slot
    /// indirection (ECMA-376 §19.3.1.20, §14.2.9).
    /// </summary>
    /// <param name="effectiveClrMap">
    /// The effective color map for this slide/master context.
    /// Slide's <c>p:clrMapOvr/a:overrideClrMapping</c> takes precedence; fall back to the
    /// master's <c>p:clrMap</c>; null = default Office mapping.
    /// </param>
    public static SrgbColor Resolve(ThemeAwareColor color, PresentationTheme? theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        if (color.SchemeColor is { } schemeRef && theme is not null)
        {
            // Apply clrMap indirection: map the raw role name through the effective clrMap to get
            // the actual theme slot.  When effectiveClrMap is null the default mapping is used
            // (tx1→Dk1, bg1→Lt1, …) which matches the existing pre-fix behavior.
            // Pass schemeRef.Slot as fallback so SchemeColorRef objects created without a RoleName
            // (e.g. in tests or older code paths that set Slot directly) resolve correctly.
            var slot = MapRoleToSlot(schemeRef.RoleName, effectiveClrMap, schemeRef.Slot);

            var baseColor = theme.ColorScheme[slot];
            var resolved = ApplyLumModOff(baseColor, schemeRef.LumMod, schemeRef.LumOff);
            if (schemeRef.Tint < 1.0)
                resolved = ApplyTint(resolved, schemeRef.Tint);
            if (schemeRef.Shade < 1.0)
                resolved = ApplyShade(resolved, schemeRef.Shade);
            return resolved;
        }
        return color.Resolved;
    }

    /// <summary>
    /// DrawingML tint: result = base * tintFraction + white * (1 - tintFraction).
    /// tintFraction=1.0 → original color; tintFraction=0.0 → white.
    /// </summary>
    private static SrgbColor ApplyTint(SrgbColor c, double tint)
    {
        if (tint >= 1.0) return c;
        if (tint <= 0.0) return new SrgbColor(255, 255, 255);
        return new SrgbColor(
            (byte)Math.Clamp(Math.Round(c.R * tint + 255.0 * (1.0 - tint)), 0, 255),
            (byte)Math.Clamp(Math.Round(c.G * tint + 255.0 * (1.0 - tint)), 0, 255),
            (byte)Math.Clamp(Math.Round(c.B * tint + 255.0 * (1.0 - tint)), 0, 255));
    }

    /// <summary>
    /// DrawingML shade: result = base * shadeFraction.
    /// shadeFraction=1.0 → original color; shadeFraction=0.0 → black.
    /// </summary>
    private static SrgbColor ApplyShade(SrgbColor c, double shade)
    {
        if (shade >= 1.0) return c;
        if (shade <= 0.0) return new SrgbColor(0, 0, 0);
        return new SrgbColor(
            (byte)Math.Clamp(Math.Round(c.R * shade), 0, 255),
            (byte)Math.Clamp(Math.Round(c.G * shade), 0, 255),
            (byte)Math.Clamp(Math.Round(c.B * shade), 0, 255));
    }

    // â”€â”€â”€ HLS luminance adjustments â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Applies lumMod and lumOff to an sRGB color by converting to HLS, computing
    ///   L' = clamp(L * lumMod + lumOff, 0, 1)
    /// and converting back.
    /// Both lumMod and lumOff are in [0, 1] (already normalized from OOXML's 100 000 scale).
    /// </summary>
    private static SrgbColor ApplyLumModOff(SrgbColor rgb, double lumMod, double lumOff)
    {
        // Fast path: identity modifiers.
        if (lumMod is 1.0 && lumOff is 0.0)
            return rgb;

        RgbToHls(rgb, out double h, out double l, out double s);

        l = Math.Clamp(l * lumMod + lumOff, 0.0, 1.0);

        return HlsToRgb(h, l, s);
    }

    /// <summary>Converts sRGB [0,255] to HLS [0,1].</summary>
    private static void RgbToHls(SrgbColor rgb, out double h, out double l, out double s)
    {
        double r = rgb.R / 255.0;
        double g = rgb.G / 255.0;
        double b = rgb.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        l = (max + min) / 2.0;

        if (delta < 1e-10)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

        if (max == r)
            h = ((g - b) / delta + (g < b ? 6.0 : 0.0)) / 6.0;
        else if (max == g)
            h = ((b - r) / delta + 2.0) / 6.0;
        else
            h = ((r - g) / delta + 4.0) / 6.0;
    }

    /// <summary>Converts HLS [0,1] to sRGB [0,255].</summary>
    private static SrgbColor HlsToRgb(double h, double l, double s)
    {
        if (s < 1e-10)
        {
            byte v = (byte)Math.Round(l * 255);
            return new SrgbColor(v, v, v);
        }

        double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p = 2.0 * l - q;

        return new SrgbColor(
            (byte)Math.Round(Hue(p, q, h + 1.0 / 3.0) * 255),
            (byte)Math.Round(Hue(p, q, h) * 255),
            (byte)Math.Round(Hue(p, q, h - 1.0 / 3.0) * 255));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1.0;
        if (t > 1) t -= 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }
}

