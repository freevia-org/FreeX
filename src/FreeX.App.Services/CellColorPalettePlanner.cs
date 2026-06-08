using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record CellColorSwatch(string Hex, CellColor Color);

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
        bool includeCustomSpectrum = true)
    {
        var themePalette = BuildThemePalette();
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

    public static IReadOnlyList<CellColorSwatch> BuildDefaultSwatches() =>
        BuildThemePalette().SelectMany(column => column.Shades)
            .Concat(BuildStandardSwatches())
            .DistinctBy(swatch => swatch.Hex)
            .ToList();

    public static IReadOnlyList<CellColorThemeColumn> BuildThemePalette() =>
        new[]
        {
            Column("Text/Background Dark 1", "#000000", "#7F7F7F", "#595959", "#3F3F3F", "#262626", "#0D0D0D"),
            Column("Text/Background Light 1", "#FFFFFF", "#F2F2F2", "#D9D9D9", "#BFBFBF", "#A6A6A6", "#808080"),
            Column("Text/Background Dark 2", "#44546A", "#D6DCE4", "#ADB9CA", "#8497B0", "#323E4F", "#222A35"),
            Column("Text/Background Light 2", "#E7E6E6", "#D0CECE", "#AEAAAA", "#757171", "#3A3838", "#171616"),
            Column("Accent 1", "#4472C4", "#D9E2F3", "#B4C6E7", "#8EAADB", "#2F5597", "#1F3864"),
            Column("Accent 2", "#ED7D31", "#FCE4D6", "#F8CBAD", "#F4B183", "#C55A11", "#833C0C"),
            Column("Accent 3", "#A5A5A5", "#EDEDED", "#DBDBDB", "#C9C9C9", "#7B7B7B", "#525252"),
            Column("Accent 4", "#FFC000", "#FFF2CC", "#FFE699", "#FFD966", "#BF9000", "#7F6000"),
            Column("Accent 5", "#5B9BD5", "#DDEBF7", "#BDD7EE", "#9DC3E6", "#2E75B6", "#1F4E79"),
            Column("Accent 6", "#70AD47", "#E2F0D9", "#C6E0B4", "#A9D18E", "#548235", "#375623")
        };

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
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

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

    private static bool TryParseHexColor(string text, out CellColor color)
    {
        color = default;
        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = new CellColor(red, green, blue);
        return true;
    }

    private static byte ScaleColorComponent(byte component, double factor) =>
        (byte)Math.Clamp((int)Math.Round(component * factor), 0, 255);

    private static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value * 255d), 0, 255);
}
