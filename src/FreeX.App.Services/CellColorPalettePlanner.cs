using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// A single color-picker swatch. <paramref name="ThemeColor"/> is non-null only for a swatch drawn
/// from an Accent1-6 theme column (see <see cref="CellColorPalettePlanner.ThemeAccentColumn"/>) --
/// it records which theme slot/tint the swatch resolves to, so a caller that applies it can attach
/// that same <see cref="WorkbookThemeColorReference"/> to the target style (R142-services-theme-
/// colors-1) instead of only the resolved flat RGB, keeping the link to the workbook theme alive
/// across a later theme change. Standard/Recent/Custom-spectrum swatches always carry a null
/// ThemeColor -- they are plain colors with no theme identity to preserve.
/// </summary>
public sealed record CellColorSwatch(string Hex, CellColor Color, WorkbookThemeColorReference? ThemeColor = null);

public sealed record CellColorThemeColumn(string Name, IReadOnlyList<CellColorSwatch> Shades);

public sealed record CellColorPalettePlan(IReadOnlyList<CellColorPaletteSection> Sections);

public sealed record CellColorPaletteSection(
    CellColorPaletteSectionKind Kind,
    IReadOnlyList<CellColorSwatch> Swatches,
    IReadOnlyList<CellColorThemeColumn> ThemeColumns);

public enum CellColorPaletteSectionKind
{
    Theme,
    Standard,
    Recent,
    CustomSpectrum
}

public static class CellColorPalettePlanner
{
    public const int DefaultRecentColorCapacity = 10;

    public static CellColorPalettePlan BuildMenuPlan(
        IEnumerable<CellColor>? recentColors = null,
        int recentColorCapacity = DefaultRecentColorCapacity,
        bool includeCustomSpectrum = true,
        WorkbookTheme? theme = null)
    {
        var themePalette = BuildThemePalette(theme);
        var standardSwatches = BuildStandardSwatches();
        var recentSwatches = BuildRecentSwatches(recentColors, recentColorCapacity);
        var sections = new List<CellColorPaletteSection>
        {
            new(
                CellColorPaletteSectionKind.Theme,
                themePalette.SelectMany(column => column.Shades).ToList(),
                themePalette),
            new(CellColorPaletteSectionKind.Standard, standardSwatches, [])
        };

        if (recentSwatches.Count > 0)
            sections.Add(new(CellColorPaletteSectionKind.Recent, recentSwatches, []));

        if (includeCustomSpectrum)
            sections.Add(new(CellColorPaletteSectionKind.CustomSpectrum, BuildCustomSpectrumSwatches(), []));

        return new CellColorPalettePlan(sections);
    }

    public static IReadOnlyList<CellColorSwatch> BuildDefaultSwatches(WorkbookTheme? theme = null) =>
        BuildThemePalette(theme).SelectMany(column => column.Shades)
            .Concat(BuildStandardSwatches())
            .DistinctBy(swatch => swatch.Hex)
            .ToList();

    /// <summary>
    /// Builds the Excel-style 10-column theme color gallery: the two Text/Background pairs (fixed,
    /// independent of the workbook theme) plus Accent 1-6, whose base swatch and five tint/shade rows
    /// are derived from <paramref name="theme"/> (falling back to <see cref="WorkbookTheme.Office"/> —
    /// the real default Aptos theme — when the caller has no active workbook theme to pass). Previously
    /// the Accent columns hardcoded the legacy Office 2013-2021 palette (Accent 1 = #4472C4) even though
    /// <see cref="WorkbookTheme.Office"/> has long since moved to the Aptos palette (Accent 1 = #156082),
    /// so the color picker's "theme colors" never matched the workbook's actual theme.
    /// </summary>
    public static IReadOnlyList<CellColorThemeColumn> BuildThemePalette(WorkbookTheme? theme = null)
    {
        var activeTheme = theme ?? WorkbookTheme.Office;
        return new[]
        {
            Column("Text/Background Dark 1", "#000000", "#7F7F7F", "#595959", "#3F3F3F", "#262626", "#0D0D0D"),
            Column("Text/Background Light 1", "#FFFFFF", "#F2F2F2", "#D9D9D9", "#BFBFBF", "#A6A6A6", "#808080"),
            Column("Text/Background Dark 2", "#44546A", "#D6DCE4", "#ADB9CA", "#8497B0", "#323E4F", "#222A35"),
            Column("Text/Background Light 2", "#E7E6E6", "#D0CECE", "#AEAAAA", "#757171", "#3A3838", "#171616"),
            ThemeAccentColumn("Accent 1", activeTheme, WorkbookThemeColorSlot.Accent1),
            ThemeAccentColumn("Accent 2", activeTheme, WorkbookThemeColorSlot.Accent2),
            ThemeAccentColumn("Accent 3", activeTheme, WorkbookThemeColorSlot.Accent3),
            ThemeAccentColumn("Accent 4", activeTheme, WorkbookThemeColorSlot.Accent4),
            ThemeAccentColumn("Accent 5", activeTheme, WorkbookThemeColorSlot.Accent5),
            ThemeAccentColumn("Accent 6", activeTheme, WorkbookThemeColorSlot.Accent6)
        };
    }

    /// <summary>
    /// Standard Excel theme-column shade tints for an Accent slot: the base color, then Lighter 80%/60%/
    /// 40%, then Darker 25%/50% (the same tint fractions the legacy hardcoded Accent columns used).
    /// </summary>
    private static readonly double[] ThemeAccentShadeTints = [0d, 0.8d, 0.6d, 0.4d, -0.25d, -0.5d];

    private static CellColorThemeColumn ThemeAccentColumn(string name, WorkbookTheme theme, WorkbookThemeColorSlot slot) =>
        new(
            name,
            ThemeAccentShadeTints
                .Select(tint => ThemeSwatch(theme.ResolveColor(slot, tint), new WorkbookThemeColorReference(slot, tint)))
                .ToList());

    private static CellColorSwatch ThemeSwatch(CellColor color, WorkbookThemeColorReference themeColor) =>
        new(FormatHexColor(color), color, themeColor);

    public static IReadOnlyList<CellColorSwatch> BuildStandardSwatches() =>
        new[]
        {
            Swatch("#C00000"),
            Swatch("#FF0000"),
            Swatch("#FFC000"),
            Swatch("#FFFF00"),
            Swatch("#92D050"),
            Swatch("#00B050"),
            Swatch("#00B0F0"),
            Swatch("#0070C0"),
            Swatch("#002060"),
            Swatch("#7030A0")
        };

    public static IReadOnlyList<CellColorSwatch> BuildRecentSwatches(
        IEnumerable<CellColor>? recentColors,
        int capacity = DefaultRecentColorCapacity)
    {
        if (recentColors is null || capacity <= 0)
            return [];

        var swatches = new List<CellColorSwatch>(capacity);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var color in recentColors)
        {
            var hex = FormatHexColor(color);
            if (!seen.Add(hex))
                continue;

            swatches.Add(new CellColorSwatch(hex, color));
            if (swatches.Count == capacity)
                break;
        }

        return swatches;
    }

    public static IReadOnlyList<CellColor> PromoteRecentColor(
        IEnumerable<CellColor>? recentColors,
        CellColor color,
        int capacity = DefaultRecentColorCapacity)
    {
        if (capacity <= 0)
            return [];

        var colors = new List<CellColor>(capacity);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(color);

        foreach (var recentColor in recentColors ?? [])
        {
            Add(recentColor);
            if (colors.Count == capacity)
                break;
        }

        return colors;

        void Add(CellColor candidate)
        {
            if (colors.Count == capacity)
                return;

            if (seen.Add(FormatHexColor(candidate)))
                colors.Add(candidate);
        }
    }

    public static IReadOnlyList<CellColorSwatch> BuildCustomSpectrumSwatches()
    {
        var hues = new[] { 0d, 30d, 60d, 120d, 180d, 210d, 240d, 300d };
        var saturations = new[] { 1d, 0.85d, 0.7d, 0.55d, 0.4d, 0.25d };

        return saturations
            .SelectMany(saturation => hues.Select(hue => SwatchFromHsv(hue, saturation, 1d)))
            .DistinctBy(swatch => swatch.Hex)
            .ToList();
    }

    public static CellColor ScaleColor(CellColor baseColor, double factor) =>
        new(
            ScaleColorComponent(baseColor.R, factor),
            ScaleColorComponent(baseColor.G, factor),
            ScaleColorComponent(baseColor.B, factor));

    public static bool NeedsDarkForeground(CellColor color)
    {
        var luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return luminance > 140;
    }

    public static string FormatHexColor(CellColor color) =>
        ColorInputParser.FormatHexColor(color);

    /// <summary>
    /// Parses a 6-digit RGB hex string (with or without a leading '#') into a
    /// <see cref="CellColor"/>. Shared by the WPF and Avalonia color pickers so hex
    /// parsing lives in exactly one place.
    /// </summary>
    public static bool TryParseHexColor(string? text, out CellColor color)
    {
        color = default;
        if (!ColorInputParser.TryParseHexColor(text ?? string.Empty, out var parsed) || parsed is null)
            return false;

        color = parsed.Value;
        return true;
    }

    private static CellColorSwatch Swatch(string hex)
    {
        if (!TryParseHexColor(hex, out var color))
            throw new InvalidOperationException($"Invalid swatch color '{hex}'.");

        return new CellColorSwatch(hex.ToUpperInvariant(), color);
    }

    private static CellColorThemeColumn Column(string name, params string[] shades) =>
        new(name, shades.Select(Swatch).ToList());

    private static CellColorSwatch SwatchFromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var huePrime = hue / 60d;
        var x = chroma * (1d - Math.Abs((huePrime % 2d) - 1d));
        var match = value - chroma;

        var (red, green, blue) = huePrime switch
        {
            >= 0 and < 1 => (chroma, x, 0d),
            >= 1 and < 2 => (x, chroma, 0d),
            >= 2 and < 3 => (0d, chroma, x),
            >= 3 and < 4 => (0d, x, chroma),
            >= 4 and < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        var color = new CellColor(
            ToByte(red + match),
            ToByte(green + match),
            ToByte(blue + match));

        return new CellColorSwatch(FormatHexColor(color), color);
    }

    private static byte ScaleColorComponent(byte component, double factor) =>
        (byte)Math.Clamp((int)Math.Round(component * factor), 0, 255);

    private static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value * 255d), 0, 255);
}
