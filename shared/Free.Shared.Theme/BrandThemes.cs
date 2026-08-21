namespace Free.Shared.Theme;

/// <summary>
/// Canonical brand themes for every FreeFamily app.
/// All color values are sourced from the app's existing palette and validated by unit tests.
/// </summary>
public static class BrandThemes
{
    // ── Shared neutral surfaces/text/borders (reused by FreeW + FreeP provisional themes) ──
    private static readonly ThemeColor s_text         = ThemeColor.FromHex("#1F1F1F");
    private static readonly ThemeColor s_mutedText    = ThemeColor.FromHex("#5F6368");
    private static readonly ThemeColor s_subtleText   = ThemeColor.FromHex("#767676");
    private static readonly ThemeColor s_ribbonSurf   = ThemeColor.FromHex("#FFFFFF");
    private static readonly ThemeColor s_chromeSurf   = ThemeColor.FromHex("#F7F8F8");
    private static readonly ThemeColor s_sheetSurf    = ThemeColor.FromHex("#F3F3F3");
    private static readonly ThemeColor s_border       = ThemeColor.FromHex("#DADCE0");
    private static readonly ThemeColor s_borderStrong = ThemeColor.FromHex("#C8CCD0");
    private static readonly ThemeColor s_danger       = ThemeColor.FromHex("#C42B1C");
    private static readonly ThemeColor s_white        = ThemeColor.FromHex("#FFFFFF");

    // ── Default typography ──
    // StatusBarText: FontSize=12, no FontFamily (inherits system default on both WPF and Avalonia —
    // MATCHED baseline captured 2026-06-24 from MainWindow.xaml:1133 + MainWindow.cs:3291).
    private static readonly ThemeTypography s_defaultTypography = new(
        Body:          new ThemeTypeToken("Segoe UI",  9.0,  ThemeFontWeight.Normal),
        Caption:       new ThemeTypeToken("Segoe UI",  8.0,  ThemeFontWeight.Normal),
        RibbonLabel:   new ThemeTypeToken("Segoe UI",  9.0,  ThemeFontWeight.Normal),
        Heading:       new ThemeTypeToken("Segoe UI", 14.0,  ThemeFontWeight.SemiBold),
        StatusBarText: new ThemeTypeToken("",         12.0,  ThemeFontWeight.Normal));

    // ── Default metrics ──
    // StatusBarHeight=28 px: MATCHED baseline 2026-06-24
    //   WPF  — Border Padding="8,3", FontSize=12, auto-height → renders as 28px (MainWindow.xaml:1119)
    //   Avalonia — Border Height=28 (MainWindow.cs:3388)
    // TitleBarCaptionHeight=34 px: WPF WindowChrome.CaptionHeight (MainWindow.xaml:25).
    //   Avalonia uses native OS title bar — value carried for documentation, not applied by Avalonia applier.
    private static readonly ThemeMetrics s_defaultMetrics = new(
        RibbonRowHeight:      22.0,
        ControlHeight:        24.0,
        IconSize:             16.0,
        CornerRadius:          2.0,
        StatusBarHeight:      28.0,
        TitleBarCaptionHeight: 34.0);

    /// <summary>
    /// FreeX (spreadsheet) brand theme.
    /// Colors are BYTE-IDENTICAL to <c>src/FreeX.App.Host/Resources/ThemeResources.xaml</c>.
    /// </summary>
    public static readonly Theme FreeX = new(
        Name: "FreeX",
        Colors: new ThemeColors(
            Accent:               ThemeColor.FromHex("#0F6D8C"),
            AccentDark:           ThemeColor.FromHex("#17324D"),
            AccentSoft:           ThemeColor.FromHex("#E6F6FA"),
            AccentPressed:        ThemeColor.FromHex("#CCEAF2"),
            TitleBar:             ThemeColor.FromHex("#17324D"),
            TitleBarHover:        ThemeColor.FromHex("#0F6D8C"),
            TitleBarPressed:      ThemeColor.FromHex("#10253A"),
            TitleBarDisabled:     ThemeColor.FromHex("#8BA6B8"),
            TitleBarButtonBorder: ThemeColor.FromHex("#55FFFFFF"),  // alpha 0x55
            RibbonButtonHover:    ThemeColor.FromHex("#BEE6FD"),
            RibbonInlineDivider:  ThemeColor.FromHex("#CCCCCC"),
            Text:                 ThemeColor.FromHex("#1F1F1F"),
            MutedText:            ThemeColor.FromHex("#5F6368"),
            SubtleText:           ThemeColor.FromHex("#767676"),
            RibbonSurface:        ThemeColor.FromHex("#FFFFFF"),
            ChromeSurface:        ThemeColor.FromHex("#F7F8F8"),
            SheetSurface:         ThemeColor.FromHex("#F3F3F3"),
            StatusSurface:        ThemeColor.FromHex("#17324D"),
            Border:               ThemeColor.FromHex("#DADCE0"),
            BorderStrong:         ThemeColor.FromHex("#C8CCD0"),
            Danger:               ThemeColor.FromHex("#C42B1C"),
            White:                ThemeColor.FromHex("#FFFFFF")),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        IconSetId:  "freex");

    /// <summary>
    /// FreeW (word processor) brand theme. The owned amber/umber palette is shared by WPF,
    /// Avalonia, packaging artwork, ribbon chrome, backstage, and status surfaces.
    /// </summary>
    public static readonly Theme FreeW = new(
        Name: "FreeW",
        Colors: new ThemeColors(
            Accent:               ThemeColor.FromHex("#A26714"),
            AccentDark:           ThemeColor.FromHex("#4B2F12"),
            AccentSoft:           ThemeColor.FromHex("#FBF0DC"),
            AccentPressed:        ThemeColor.FromHex("#F3D8AB"),
            TitleBar:             ThemeColor.FromHex("#4B2F12"),
            TitleBarHover:        ThemeColor.FromHex("#A26714"),
            TitleBarPressed:      ThemeColor.FromHex("#36200C"),
            TitleBarDisabled:     ThemeColor.FromHex("#B49A75"),
            TitleBarButtonBorder: ThemeColor.FromHex("#55FFFFFF"),
            RibbonButtonHover:    ThemeColor.FromHex("#F6E3C2"),
            RibbonInlineDivider:  ThemeColor.FromHex("#CCCCCC"),
            Text:                 s_text,
            MutedText:            s_mutedText,
            SubtleText:           s_subtleText,
            RibbonSurface:        s_ribbonSurf,
            ChromeSurface:        s_chromeSurf,
            SheetSurface:         s_sheetSurf,
            StatusSurface:        ThemeColor.FromHex("#4B2F12"),
            Border:               s_border,
            BorderStrong:         s_borderStrong,
            Danger:               s_danger,
            White:                s_white),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        IconSetId:  "freew");

    /// <summary>
    /// FreeP (presentation) brand theme. The owned berry/plum palette is shared by WPF,
    /// Avalonia, packaging artwork, ribbon chrome, backstage, and status surfaces.
    /// </summary>
    public static readonly Theme FreeP = new(
        Name: "FreeP",
        Colors: new ThemeColors(
            Accent:               ThemeColor.FromHex("#A23B72"),
            AccentDark:           ThemeColor.FromHex("#4E213B"),
            AccentSoft:           ThemeColor.FromHex("#F9E7F1"),
            AccentPressed:        ThemeColor.FromHex("#F1CDE0"),
            TitleBar:             ThemeColor.FromHex("#4E213B"),
            TitleBarHover:        ThemeColor.FromHex("#A23B72"),
            TitleBarPressed:      ThemeColor.FromHex("#351426"),
            TitleBarDisabled:     ThemeColor.FromHex("#B18A9F"),
            TitleBarButtonBorder: ThemeColor.FromHex("#55FFFFFF"),
            RibbonButtonHover:    ThemeColor.FromHex("#F3D7E6"),
            RibbonInlineDivider:  ThemeColor.FromHex("#CCCCCC"),
            Text:                 s_text,
            MutedText:            s_mutedText,
            SubtleText:           s_subtleText,
            RibbonSurface:        s_ribbonSurf,
            ChromeSurface:        s_chromeSurf,
            SheetSurface:         s_sheetSurf,
            StatusSurface:        ThemeColor.FromHex("#4E213B"),
            Border:               s_border,
            BorderStrong:         s_borderStrong,
            Danger:               s_danger,
            White:                s_white),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        IconSetId:  "freep");

    /// <summary>
    /// FreeXMidnight — demo alternate for FreeX with orange accent and near-black title bar.
    /// Setting <c>FREEX_THEME=midnight</c> at launch swaps the chrome to this palette,
    /// making the reskin visually obvious without touching any XAML.
    /// </summary>
    public static readonly Theme FreeXMidnight = new(
        Name: "FreeXMidnight",
        Colors: new ThemeColors(
            Accent:               ThemeColor.FromHex("#C8651B"),   // orange accent
            AccentDark:           ThemeColor.FromHex("#202124"),   // near-black
            AccentSoft:           ThemeColor.FromHex("#FDF0E6"),
            AccentPressed:        ThemeColor.FromHex("#F9D9BC"),
            TitleBar:             ThemeColor.FromHex("#202124"),   // near-black title bar
            TitleBarHover:        ThemeColor.FromHex("#C8651B"),
            TitleBarPressed:      ThemeColor.FromHex("#161719"),
            TitleBarDisabled:     ThemeColor.FromHex("#6E7074"),
            TitleBarButtonBorder: ThemeColor.FromHex("#55FFFFFF"),
            RibbonButtonHover:    ThemeColor.FromHex("#F9D9BC"),
            RibbonInlineDivider:  ThemeColor.FromHex("#CCCCCC"),
            Text:                 s_text,
            MutedText:            s_mutedText,
            SubtleText:           s_subtleText,
            RibbonSurface:        s_ribbonSurf,
            ChromeSurface:        ThemeColor.FromHex("#F5F5F5"),
            SheetSurface:         s_sheetSurf,
            StatusSurface:        ThemeColor.FromHex("#202124"),
            Border:               s_border,
            BorderStrong:         s_borderStrong,
            Danger:               s_danger,
            White:                s_white),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        IconSetId:  "freex");
}
