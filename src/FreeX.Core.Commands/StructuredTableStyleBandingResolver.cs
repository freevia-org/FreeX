using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Resolves an Excel built-in table style name (e.g. <c>TableStyleMedium2</c>) plus the workbook
/// theme into the banding colors (header fill/font, alternating row stripes) that Excel applies
/// dynamically.  This is the single source of truth shared by the table-style gallery (swatches +
/// creation), and the load-time materializer that paints loaded tables so they look like Excel.
/// Keeping both on this resolver guarantees the gallery and the materializer agree on every color.
/// </summary>
public static class StructuredTableStyleBandingResolver
{
    private static readonly WorkbookThemeColorSlot[] AccentSlots =
    [
        WorkbookThemeColorSlot.Accent1,
        WorkbookThemeColorSlot.Accent2,
        WorkbookThemeColorSlot.Accent3,
        WorkbookThemeColorSlot.Accent4,
        WorkbookThemeColorSlot.Accent5,
        WorkbookThemeColorSlot.Accent6
    ];

    // Per-family fixed-palette accents used when the workbook carries the default Office theme (the
    // theme-aware path below is preferred whenever the workbook overrides accent colors).  These
    // mirror Excel's built-in TableStyle{Light|Medium|Dark}N swatches.
    private static readonly (CellColor Header, CellColor Band, CellColor Font)[] LightAccents =
    [
        (new CellColor(217, 217, 217), new CellColor(242, 242, 242), CellColor.Black),
        (new CellColor(91, 155, 213), new CellColor(221, 235, 247), CellColor.White),
        (new CellColor(237, 125, 49), new CellColor(252, 228, 214), CellColor.White),
        (new CellColor(165, 165, 165), new CellColor(237, 237, 237), CellColor.White),
        (new CellColor(255, 192, 0), new CellColor(255, 242, 204), CellColor.Black),
        (new CellColor(68, 114, 196), new CellColor(217, 225, 242), CellColor.White),
        (new CellColor(112, 173, 71), new CellColor(226, 239, 218), CellColor.White)
    ];

    private static readonly (CellColor Header, CellColor Band, CellColor Font)[] MediumAccents =
    [
        (new CellColor(31, 78, 121), new CellColor(222, 235, 247), CellColor.White),
        (new CellColor(31, 115, 70), new CellColor(226, 239, 218), CellColor.White),
        (new CellColor(91, 155, 213), new CellColor(221, 235, 247), CellColor.White),
        (new CellColor(112, 48, 160), new CellColor(229, 224, 236), CellColor.White),
        (new CellColor(192, 80, 77), new CellColor(242, 220, 219), CellColor.White),
        (new CellColor(128, 100, 162), new CellColor(235, 229, 241), CellColor.White),
        (new CellColor(75, 172, 198), new CellColor(218, 238, 243), CellColor.White)
    ];

    private static readonly (CellColor Header, CellColor Band, CellColor Font)[] DarkAccents =
    [
        (new CellColor(54, 54, 54), new CellColor(68, 68, 68), CellColor.White),
        (new CellColor(31, 78, 121), new CellColor(41, 92, 135), CellColor.White),
        (new CellColor(0, 97, 0), new CellColor(0, 125, 0), CellColor.White),
        (new CellColor(91, 44, 111), new CellColor(112, 48, 160), CellColor.White),
        (new CellColor(128, 55, 52), new CellColor(160, 64, 61), CellColor.White),
        (new CellColor(68, 84, 106), new CellColor(84, 105, 132), CellColor.White),
        (new CellColor(45, 90, 39), new CellColor(60, 110, 51), CellColor.White)
    ];

    /// <summary>Resolves the banding for <paramref name="styleName"/> against the Office theme.</summary>
    public static StructuredTableStyleBanding Resolve(string? styleName) =>
        Resolve(styleName, WorkbookTheme.Office);

    /// <summary>
    /// Resolves the banding for <paramref name="styleName"/> against <paramref name="theme"/>.
    /// Prefers the theme-aware accent resolution (so a workbook that overrides accent colors gets
    /// matching table colors) and falls back to Excel's fixed built-in swatch palette.  Unknown or
    /// empty names default to a neutral light banding so the table still renders sensibly.
    /// </summary>
    public static StructuredTableStyleBanding Resolve(string? styleName, WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (string.IsNullOrWhiteSpace(styleName))
            return DefaultLightBanding();

        // Excel's built-in "Light" table styles 8-14 are the black-header variants: a solid black
        // (Text1/dark1) header row with white bold text and an unbanded white body.  They are NOT
        // accent-tinted like Light 1-7, and they precede the themed-accent Light 16-21 range, so they
        // must be intercepted before the accent-palette fallback would map them to a grey/accent header.
        if (TryResolveBlackHeaderLightStyle(styleName))
            return BlackHeaderLightBanding();

        if (ResolveThemeBanding(styleName, theme) is { } themed)
            return themed;

        if (TryResolveFamily(styleName, out var family, out var index))
            return ResolveFromAccentPalette(family, index);

        return DefaultLightBanding();
    }

    private enum StyleFamily
    {
        Light,
        Medium,
        Dark
    }

    private static StructuredTableStyleBanding ResolveFromAccentPalette(StyleFamily family, int index)
    {
        var (accents, useDarkRows) = family switch
        {
            StyleFamily.Light => (LightAccents, false),
            StyleFamily.Dark => (DarkAccents, true),
            _ => (MediumAccents, false)
        };

        var accent = accents[(index - 1) % accents.Length];
        // Once a family cycles back through its accent tuples (e.g. Dark8 reuses Dark1's accent),
        // progressively tint the fill so later styles in the cycle stay visually distinct instead of
        // rendering byte-identical to their first-cycle counterpart.
        var cycle = (index - 1) / accents.Length;
        var evenFill = useDarkRows ? Darken(accent.Band, 18 + cycle * 8) : CellColor.White;
        var oddFill = Lighten(accent.Band, cycle * 8);

        // Border color: only the Medium family uses interior thin borders (a midpoint tint ≈ 0.5 of the
        // accent, sitting between the solid header and the banded row fill).  Light and Dark families
        // match Excel's behavior: Light has no interior borders, and Dark uses color contrast from the
        // alternating band fills instead of an explicit border.
        CellColor? border = family == StyleFamily.Medium
            ? MidpointColor(accent.Header, accent.Band)
            : null;

        return new StructuredTableStyleBanding(accent.Header, oddFill, evenFill, accent.Font, Border: border);
    }

    private static bool TryResolveFamily(string styleName, out StyleFamily family, out int index)
    {
        family = default;
        index = 0;
        if (TryParseFamilyIndex(styleName, "TableStyleLight", out index))
        {
            family = StyleFamily.Light;
            return true;
        }

        if (TryParseFamilyIndex(styleName, "TableStyleMedium", out index))
        {
            family = StyleFamily.Medium;
            return true;
        }

        if (TryParseFamilyIndex(styleName, "TableStyleDark", out index))
        {
            family = StyleFamily.Dark;
            return true;
        }

        return false;
    }

    private static bool TryParseFamilyIndex(string styleName, string prefix, out int index)
    {
        index = 0;
        if (!styleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(styleName[prefix.Length..], out var parsed) ||
            parsed < 1)
        {
            return false;
        }

        index = parsed;
        return true;
    }

    private static StructuredTableStyleBanding? ResolveThemeBanding(string styleName, WorkbookTheme theme)
    {
        if (ReferenceEquals(theme, WorkbookTheme.Office))
            return null;

        if (TryResolveMediumThemeSlot(styleName, out var mediumSlot))
            return CreateThemedMediumBanding(theme, mediumSlot);

        if (TryResolveLightThemeSlot(styleName, out var lightSlot))
            return CreateThemedLightBanding(theme, lightSlot);

        return null;
    }

    private static bool TryResolveMediumThemeSlot(string styleName, out WorkbookThemeColorSlot slot) =>
        TryResolveSequentialAccentStyle(styleName, "TableStyleMedium", firstThemedIndex: 2, out slot);

    private static bool TryResolveLightThemeSlot(string styleName, out WorkbookThemeColorSlot slot) =>
        TryResolveSequentialAccentStyle(styleName, "TableStyleLight", firstThemedIndex: 16, out slot);

    private static bool TryResolveSequentialAccentStyle(
        string styleName,
        string prefix,
        int firstThemedIndex,
        out WorkbookThemeColorSlot slot)
    {
        slot = default;
        if (!styleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(styleName[prefix.Length..], out var index))
        {
            return false;
        }

        var offset = index - firstThemedIndex;
        if (offset < 0 || offset >= AccentSlots.Length)
            return false;

        slot = AccentSlots[offset];
        return true;
    }

    private static StructuredTableStyleBanding CreateThemedMediumBanding(
        WorkbookTheme theme,
        WorkbookThemeColorSlot slot) =>
        new(
            HeaderFill: theme.ResolveColor(slot),
            OddRowFill: theme.ResolveColor(slot, 0.8),
            EvenRowFill: CellColor.White,
            HeaderFontColor: CellColor.White,
            Border: theme.ResolveColor(slot, 0.5));

    private static StructuredTableStyleBanding CreateThemedLightBanding(
        WorkbookTheme theme,
        WorkbookThemeColorSlot slot) =>
        new(
            HeaderFill: theme.ResolveColor(slot, 0.8),
            OddRowFill: theme.ResolveColor(slot, 0.95),
            EvenRowFill: CellColor.White,
            HeaderFontColor: CellColor.Black,
            // Light has no interior borders (see the matching fixed-palette comment in
            // ResolveFromAccentPalette), regardless of which theme it is resolved against.
            Border: null);

    /// <summary>
    /// True for the black-header "Light" family (TableStyleLight 8-14), which Excel renders with a solid
    /// black header row rather than an accent-tinted one.
    /// </summary>
    private static bool TryResolveBlackHeaderLightStyle(string styleName) =>
        TryParseFamilyIndex(styleName, "TableStyleLight", out var index) && index is >= 8 and <= 14;

    private static StructuredTableStyleBanding BlackHeaderLightBanding() =>
        new(
            HeaderFill: CellColor.Black,
            OddRowFill: CellColor.White,
            EvenRowFill: CellColor.White,
            HeaderFontColor: CellColor.White);

    private static StructuredTableStyleBanding DefaultLightBanding() =>
        new(
            HeaderFill: new CellColor(217, 217, 217),
            OddRowFill: new CellColor(242, 242, 242),
            EvenRowFill: CellColor.White,
            HeaderFontColor: CellColor.Black);

    private static CellColor Lighten(CellColor color, int amount) =>
        new(
            ClampColor(color.R + amount),
            ClampColor(color.G + amount),
            ClampColor(color.B + amount));

    private static CellColor Darken(CellColor color, int amount) =>
        new(
            ClampColor(color.R - amount),
            ClampColor(color.G - amount),
            ClampColor(color.B - amount));

    /// <summary>
    /// Returns the midpoint color between <paramref name="a"/> and <paramref name="b"/>, used to
    /// approximate a ~tint-0.5 border color from the fixed-palette accent/band pair when a theme is not
    /// available (the theme-aware path uses <c>theme.ResolveColor(slot, 0.5)</c> directly).
    /// </summary>
    private static CellColor MidpointColor(CellColor a, CellColor b) =>
        new(
            (byte)((a.R + b.R) / 2),
            (byte)((a.G + b.G) / 2),
            (byte)((a.B + b.B) / 2));

    private static byte ClampColor(int value) => (byte)Math.Clamp(value, 0, 255);
}
