using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record TableStyleGalleryOption(
    string Label,
    string StyleName,
    StructuredTableStyleBanding Banding);

public static class TableStyleGalleryPlanner
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

    public static IReadOnlyList<TableStyleGalleryOption> GetOptions() =>
        GetOptions(WorkbookTheme.Office);

    public static IReadOnlyList<TableStyleGalleryOption> GetOptions(WorkbookTheme theme) =>
    [
        ..CreateLightStyles(theme),
        ..CreateMediumStyles(theme),
        ..CreateDarkStyles(theme)
    ];

    public static TableStyleGalleryOption GetOption(int index)
        => GetOption(index, WorkbookTheme.Office);

    public static TableStyleGalleryOption GetOption(int index, WorkbookTheme theme)
    {
        var options = GetOptions(theme);
        return options[Math.Clamp(index, 0, options.Count - 1)];
    }

    public static bool TryGetOption(string? styleName, out TableStyleGalleryOption option)
        => TryGetOption(styleName, WorkbookTheme.Office, out option);

    public static bool TryGetOption(string? styleName, WorkbookTheme theme, out TableStyleGalleryOption option)
    {
        option = null!;
        if (string.IsNullOrWhiteSpace(styleName))
            return false;

        option = GetOptions(theme).FirstOrDefault(candidate =>
            string.Equals(candidate.StyleName, styleName, StringComparison.OrdinalIgnoreCase))!;
        return option is not null;
    }

    private static IEnumerable<TableStyleGalleryOption> CreateLightStyles(WorkbookTheme theme)
    {
        var accents = new[]
        {
            (new CellColor(217, 217, 217), new CellColor(242, 242, 242), CellColor.Black),
            (new CellColor(91, 155, 213), new CellColor(221, 235, 247), CellColor.White),
            (new CellColor(237, 125, 49), new CellColor(252, 228, 214), CellColor.White),
            (new CellColor(165, 165, 165), new CellColor(237, 237, 237), CellColor.White),
            (new CellColor(255, 192, 0), new CellColor(255, 242, 204), CellColor.Black),
            (new CellColor(68, 114, 196), new CellColor(217, 225, 242), CellColor.White),
            (new CellColor(112, 173, 71), new CellColor(226, 239, 218), CellColor.White)
        };

        return CreateStyleGroup("Light", 21, accents, useDarkRows: false, theme);
    }

    private static IEnumerable<TableStyleGalleryOption> CreateMediumStyles(WorkbookTheme theme)
    {
        var accents = new[]
        {
            (new CellColor(31, 78, 121), new CellColor(222, 235, 247), CellColor.White),
            (new CellColor(31, 115, 70), new CellColor(226, 239, 218), CellColor.White),
            (new CellColor(91, 155, 213), new CellColor(221, 235, 247), CellColor.White),
            (new CellColor(112, 48, 160), new CellColor(229, 224, 236), CellColor.White),
            (new CellColor(192, 80, 77), new CellColor(242, 220, 219), CellColor.White),
            (new CellColor(128, 100, 162), new CellColor(235, 229, 241), CellColor.White),
            (new CellColor(75, 172, 198), new CellColor(218, 238, 243), CellColor.White)
        };

        return CreateStyleGroup("Medium", 28, accents, useDarkRows: false, theme);
    }

    private static IEnumerable<TableStyleGalleryOption> CreateDarkStyles(WorkbookTheme theme)
    {
        var accents = new[]
        {
            (new CellColor(54, 54, 54), new CellColor(68, 68, 68), CellColor.White),
            (new CellColor(31, 78, 121), new CellColor(41, 92, 135), CellColor.White),
            (new CellColor(0, 97, 0), new CellColor(0, 125, 0), CellColor.White),
            (new CellColor(91, 44, 111), new CellColor(112, 48, 160), CellColor.White),
            (new CellColor(128, 55, 52), new CellColor(160, 64, 61), CellColor.White),
            (new CellColor(68, 84, 106), new CellColor(84, 105, 132), CellColor.White)
        };

        return CreateStyleGroup("Dark", 11, accents, useDarkRows: true, theme);
    }

    private static IEnumerable<TableStyleGalleryOption> CreateStyleGroup(
        string family,
        int count,
        IReadOnlyList<(CellColor Header, CellColor Band, CellColor Font)> accents,
        bool useDarkRows,
        WorkbookTheme theme)
    {
        for (var index = 1; index <= count; index++)
        {
            var accent = accents[(index - 1) % accents.Count];
            var evenFill = useDarkRows
                ? Darken(accent.Band, 18)
                : CellColor.White;
            var oddFill = useDarkRows
                ? accent.Band
                : Lighten(accent.Band, ((index - 1) / accents.Count) * 8);

            var styleName = $"TableStyle{family}{index}";
            var banding = ResolveThemeBanding(styleName, theme) ??
                new StructuredTableStyleBanding(accent.Header, oddFill, evenFill, accent.Font);
            yield return new TableStyleGalleryOption($"{family} {index}", styleName, banding);
        }
    }

    private static StructuredTableStyleBanding? ResolveThemeBanding(string styleName, WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
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
            HeaderFontColor: CellColor.White);

    private static StructuredTableStyleBanding CreateThemedLightBanding(
        WorkbookTheme theme,
        WorkbookThemeColorSlot slot) =>
        new(
            HeaderFill: theme.ResolveColor(slot, 0.8),
            OddRowFill: theme.ResolveColor(slot, 0.95),
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

    private static byte ClampColor(int value) => (byte)Math.Clamp(value, 0, 255);
}
