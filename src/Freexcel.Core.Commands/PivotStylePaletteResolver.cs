using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class PivotStylePaletteResolver
{
    public static PivotStylePalette Resolve(string styleName)
        => Resolve(styleName, WorkbookTheme.Office);

    public static PivotStylePalette Resolve(string styleName, WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (string.IsNullOrWhiteSpace(styleName))
            return LightPalette();

        if (!ReferenceEquals(theme, WorkbookTheme.Office) &&
            TryResolveThemedPalette(styleName, theme, out var themedPalette))
        {
            return themedPalette;
        }

        return styleName switch
        {
            "PivotStyleDark4" => DarkPalette(new CellColor(68, 84, 106), new CellColor(217, 225, 242), new CellColor(180, 198, 231), new CellColor(242, 242, 242), new CellColor(142, 169, 219)),
            "PivotStyleDark7" => DarkPalette(new CellColor(31, 78, 121), new CellColor(217, 226, 243), new CellColor(184, 204, 228), new CellColor(232, 240, 248), new CellColor(149, 179, 215)),
            "PivotStyleMedium2" => MediumPalette(new CellColor(31, 78, 121), new CellColor(222, 235, 247), new CellColor(189, 215, 238), new CellColor(232, 240, 248), new CellColor(91, 155, 213)),
            "PivotStyleMedium4" => MediumPalette(new CellColor(112, 173, 71), new CellColor(226, 239, 218), new CellColor(198, 224, 180), new CellColor(235, 245, 230), new CellColor(169, 208, 142)),
            "PivotStyleMedium9" => MediumPalette(new CellColor(91, 155, 213), new CellColor(221, 235, 247), new CellColor(221, 235, 247), new CellColor(234, 243, 252), new CellColor(157, 195, 230)),
            "PivotStyleMedium10" => MediumPalette(new CellColor(237, 125, 49), new CellColor(252, 228, 214), new CellColor(248, 203, 173), new CellColor(253, 239, 230), new CellColor(244, 177, 131)),
            "PivotStyleMedium17" => MediumPalette(new CellColor(112, 48, 160), new CellColor(229, 223, 236), new CellColor(204, 192, 218), new CellColor(243, 235, 250), new CellColor(178, 161, 199)),
            _ when styleName.StartsWith("PivotStyleDark", StringComparison.OrdinalIgnoreCase) =>
                DarkPalette(new CellColor(68, 68, 68), new CellColor(217, 217, 217), new CellColor(191, 191, 191), new CellColor(242, 242, 242), new CellColor(166, 166, 166)),
            _ when styleName.StartsWith("PivotStyleMedium", StringComparison.OrdinalIgnoreCase) =>
                MediumPalette(new CellColor(91, 155, 213), new CellColor(221, 235, 247), new CellColor(221, 235, 247), new CellColor(234, 243, 252), new CellColor(157, 195, 230)),
            _ => LightPalette()
        };
    }

    private static bool TryResolveThemedPalette(string styleName, WorkbookTheme theme, out PivotStylePalette palette)
    {
        if (string.Equals(styleName, "PivotStyleMedium2", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(styleName, "PivotStyleMedium9", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(styleName, "PivotStyleDark7", StringComparison.OrdinalIgnoreCase))
        {
            palette = ThemedMediumPalette(theme, WorkbookThemeColorSlot.Accent1);
            return true;
        }

        if (string.Equals(styleName, "PivotStyleMedium10", StringComparison.OrdinalIgnoreCase))
        {
            palette = ThemedMediumPalette(theme, WorkbookThemeColorSlot.Accent2);
            return true;
        }

        if (string.Equals(styleName, "PivotStyleMedium4", StringComparison.OrdinalIgnoreCase))
        {
            palette = ThemedMediumPalette(theme, WorkbookThemeColorSlot.Accent6);
            return true;
        }

        if (string.Equals(styleName, "PivotStyleMedium17", StringComparison.OrdinalIgnoreCase))
        {
            palette = ThemedMediumPalette(theme, WorkbookThemeColorSlot.Accent5);
            return true;
        }

        if (string.Equals(styleName, "PivotStyleDark4", StringComparison.OrdinalIgnoreCase))
        {
            palette = ThemedMediumPalette(theme, WorkbookThemeColorSlot.Dark2);
            return true;
        }

        palette = LightPalette();
        return false;
    }

    private static PivotStylePalette ThemedMediumPalette(WorkbookTheme theme, WorkbookThemeColorSlot slot)
    {
        var accent = theme.ResolveColor(slot);
        return MediumPalette(
            accent,
            theme.ResolveColor(slot, 0.8),
            theme.ResolveColor(slot, 0.7),
            theme.ResolveColor(slot, 0.9),
            theme.ResolveColor(slot, 0.5));
    }

    private static PivotStylePalette LightPalette() =>
        new(
            HeaderFill: new CellColor(217, 225, 242),
            HeaderFont: CellColor.Black,
            SubtotalFill: new CellColor(234, 241, 221),
            GrandTotalFill: new CellColor(234, 241, 221),
            GrandTotalFont: CellColor.Black,
            StripeFill: new CellColor(242, 248, 238),
            Border: new CellColor(191, 191, 191));

    private static PivotStylePalette MediumPalette(
        CellColor headerFill,
        CellColor subtotalFill,
        CellColor grandTotalFill,
        CellColor stripeFill,
        CellColor border) =>
        new(headerFill, CellColor.White, subtotalFill, grandTotalFill, CellColor.Black, stripeFill, border);

    private static PivotStylePalette DarkPalette(
        CellColor headerFill,
        CellColor subtotalFill,
        CellColor grandTotalFill,
        CellColor stripeFill,
        CellColor border) =>
        new(headerFill, CellColor.White, subtotalFill, grandTotalFill, CellColor.Black, stripeFill, border);
}

internal sealed record PivotStylePalette(
    CellColor HeaderFill,
    CellColor HeaderFont,
    CellColor SubtotalFill,
    CellColor GrandTotalFill,
    CellColor GrandTotalFont,
    CellColor StripeFill,
    CellColor Border);
