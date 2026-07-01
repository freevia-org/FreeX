using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Resolves a <see cref="ThemeAwareColor"/> to a concrete <see cref="SrgbColor"/>.
/// When a <see cref="SchemeColorRef"/> is present, the color is looked up from the live theme
/// and DrawingML color transforms are applied using the shared theme color transform helper.
/// When the scheme ref is absent the pre-resolved <see cref="ThemeAwareColor.Resolved"/> is used.
/// </summary>
public static class ThemeColorResolver
{
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
    /// Slot to use when <paramref name="roleName"/> is not found in the default Office role map.
    /// Callers should pass <see cref="SchemeColorRef.Slot"/> so that
    /// <see cref="SchemeColorRef"/> objects created without a RoleName (e.g. in tests or by
    /// older code paths) still resolve correctly via their already-computed Slot.
    /// </param>
    public static ThemeColorSlot MapRoleToSlot(string? roleName, IReadOnlyDictionary<string, string>? effectiveClrMap,
        ThemeColorSlot fallbackSlot = ThemeColorSlot.Dk1)
        => ThemeColorSlotMapper.MapRoleToSlot(roleName, effectiveClrMap, fallbackSlot);

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

            return ThemeColorTransform.Apply(
                theme.ColorScheme[slot],
                schemeRef.LumMod,
                schemeRef.LumOff,
                schemeRef.Tint,
                schemeRef.Shade);
        }
        return color.Resolved;
    }
}

