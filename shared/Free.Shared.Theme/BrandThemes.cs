namespace Free.Shared.Theme;

/// <summary>
/// Canonical brand themes for every FreeFamily app.
/// All color values are sourced from the app's existing palette and validated by unit tests.
/// </summary>
public static class BrandThemes
{
    // ── Shared neutral surfaces/text/borders ──
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

    private static readonly ThemeVisualAssets s_freeXVisualAssets = new(
        IconSetId: "freex",
        ProductGlyph: "X",
        WindowsIconFileName: "FreeX.ico",
        ScalableIconFileName: "FreeX.svg",
        MacOsIconFileName: "FreeX.icns");

    private static readonly ThemeVisualAssets s_freeWVisualAssets = new(
        IconSetId: "freew",
        ProductGlyph: "W",
        WindowsIconFileName: "FreeW.ico",
        ScalableIconFileName: "FreeW.svg",
        MacOsIconFileName: "FreeW.icns");

    private static readonly ThemeVisualAssets s_freePVisualAssets = new(
        IconSetId: "freep",
        ProductGlyph: "P",
        WindowsIconFileName: "FreeP.ico",
        ScalableIconFileName: "FreeP.svg",
        MacOsIconFileName: "FreeP.icns");

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
    /// The default title bar follows the light Office surface while alternate dark themes retain
    /// a white title-bar foreground through the dedicated semantic role.
    /// </summary>
    public static readonly Theme FreeX = new(
        Name: "FreeX",
        Colors: new ThemeColors(
            Accent:               ThemeColor.FromHex("#0F6D8C"),
            AccentDark:           ThemeColor.FromHex("#17324D"),
            AccentSoft:           ThemeColor.FromHex("#E6F6FA"),
            AccentPressed:        ThemeColor.FromHex("#CCEAF2"),
            TitleBar:             ThemeColor.FromHex("#F3F4F6"),
            TitleBarForeground:   ThemeColor.FromHex("#1F1F1F"),
            TitleBarHover:        ThemeColor.FromHex("#E2E6EA"),
            TitleBarPressed:      ThemeColor.FromHex("#D0D4D9"),
            TitleBarDisabled:     ThemeColor.FromHex("#767676"),
            TitleBarButtonBorder: ThemeColor.FromHex("#C8CCD0"),
            RibbonButtonHover:    ThemeColor.FromHex("#BEE6FD"),
            RibbonInlineDivider:  ThemeColor.FromHex("#CCCCCC"),
            Text:                 ThemeColor.FromHex("#1F1F1F"),
            MutedText:            ThemeColor.FromHex("#5F6368"),
            SubtleText:           ThemeColor.FromHex("#767676"),
            RibbonSurface:        ThemeColor.FromHex("#FFFFFF"),
            ChromeSurface:        ThemeColor.FromHex("#F7F8F8"),
            SheetSurface:         ThemeColor.FromHex("#F3F3F3"),
            StatusSurface:        ThemeColor.FromHex("#F3F4F6"),
            StatusForeground:     ThemeColor.FromHex("#1F1F1F"),
            BackstageSidebar:     ThemeColor.FromHex("#10253A"),
            BackstageHover:       ThemeColor.FromHex("#1C3A55"),
            BackstageSelected:    ThemeColor.FromHex("#24445E"),
            BackstageSeparator:   ThemeColor.FromHex("#24445E"),
            BackstageLink:        ThemeColor.FromHex("#0F6D8C"),
            Border:               ThemeColor.FromHex("#DADCE0"),
            BorderStrong:         ThemeColor.FromHex("#C8CCD0"),
            Danger:               ThemeColor.FromHex("#C42B1C"),
            White:                ThemeColor.FromHex("#FFFFFF")),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        VisualAssets: s_freeXVisualAssets);

    /// <summary>
    /// FreeW (word processor) brand theme. Amber remains the product accent, status, and Backstage
    /// identity, while the title band uses Office's neutral document-chrome treatment so the Word-like
    /// ribbon can lead directly from the caption surface without a visually competing dark strip.
    /// </summary>
    public static readonly Theme FreeW = new(
        Name: "FreeW",
        Colors: new ThemeColors(
            Accent:               ThemeColor.FromHex("#A26714"),
            AccentDark:           ThemeColor.FromHex("#4B2F12"),
            AccentSoft:           ThemeColor.FromHex("#FBF0DC"),
            AccentPressed:        ThemeColor.FromHex("#F3D8AB"),
            TitleBar:             ThemeColor.FromHex("#F3F4F6"),
            TitleBarForeground:   ThemeColor.FromHex("#1F1F1F"),
            TitleBarHover:        ThemeColor.FromHex("#E2E6EA"),
            TitleBarPressed:      ThemeColor.FromHex("#D0D4D9"),
            TitleBarDisabled:     ThemeColor.FromHex("#767676"),
            TitleBarButtonBorder: ThemeColor.FromHex("#C8CCD0"),
            RibbonButtonHover:    ThemeColor.FromHex("#F6E3C2"),
            RibbonInlineDivider:  ThemeColor.FromHex("#CCCCCC"),
            Text:                 s_text,
            MutedText:            s_mutedText,
            SubtleText:           s_subtleText,
            RibbonSurface:        s_ribbonSurf,
            ChromeSurface:        s_chromeSurf,
            SheetSurface:         s_sheetSurf,
            StatusSurface:        ThemeColor.FromHex("#4B2F12"),
            StatusForeground:     s_white,
            BackstageSidebar:     ThemeColor.FromHex("#4B2F12"),
            BackstageHover:       ThemeColor.FromHex("#A26714"),
            BackstageSelected:    ThemeColor.FromHex("#36200C"),
            BackstageSeparator:   ThemeColor.FromHex("#4B2F12"),
            BackstageLink:        ThemeColor.FromHex("#A26714"),
            Border:               s_border,
            BorderStrong:         s_borderStrong,
            Danger:               s_danger,
            White:                s_white),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        VisualAssets: s_freeWVisualAssets);

    /// <summary>
    /// FreeP (presentation) brand theme. Berry remains the product accent, status, and Backstage
    /// identity, while the title band uses Office's neutral document-chrome treatment so the
    /// PowerPoint-like ribbon starts from the same quiet caption surface as the native app.
    /// </summary>
    public static readonly Theme FreeP = new(
        Name: "FreeP",
        Colors: new ThemeColors(
            Accent:               ThemeColor.FromHex("#A23B72"),
            AccentDark:           ThemeColor.FromHex("#4E213B"),
            AccentSoft:           ThemeColor.FromHex("#F9E7F1"),
            AccentPressed:        ThemeColor.FromHex("#F1CDE0"),
            TitleBar:             ThemeColor.FromHex("#F3F4F6"),
            TitleBarForeground:   ThemeColor.FromHex("#1F1F1F"),
            TitleBarHover:        ThemeColor.FromHex("#E2E6EA"),
            TitleBarPressed:      ThemeColor.FromHex("#D0D4D9"),
            TitleBarDisabled:     ThemeColor.FromHex("#767676"),
            TitleBarButtonBorder: ThemeColor.FromHex("#C8CCD0"),
            RibbonButtonHover:    ThemeColor.FromHex("#F3D7E6"),
            RibbonInlineDivider:  ThemeColor.FromHex("#CCCCCC"),
            Text:                 s_text,
            MutedText:            s_mutedText,
            SubtleText:           s_subtleText,
            RibbonSurface:        s_ribbonSurf,
            ChromeSurface:        s_chromeSurf,
            SheetSurface:         s_sheetSurf,
            StatusSurface:        ThemeColor.FromHex("#4E213B"),
            StatusForeground:     s_white,
            BackstageSidebar:     ThemeColor.FromHex("#4E213B"),
            BackstageHover:       ThemeColor.FromHex("#A23B72"),
            BackstageSelected:    ThemeColor.FromHex("#351426"),
            BackstageSeparator:   ThemeColor.FromHex("#4E213B"),
            BackstageLink:        ThemeColor.FromHex("#A23B72"),
            Border:               s_border,
            BorderStrong:         s_borderStrong,
            Danger:               s_danger,
            White:                s_white),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        VisualAssets: s_freePVisualAssets);

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
            TitleBarForeground:   s_white,
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
            StatusForeground:     s_white,
            BackstageSidebar:     ThemeColor.FromHex("#202124"),
            BackstageHover:       ThemeColor.FromHex("#C8651B"),
            BackstageSelected:    ThemeColor.FromHex("#161719"),
            BackstageSeparator:   ThemeColor.FromHex("#202124"),
            BackstageLink:        ThemeColor.FromHex("#C8651B"),
            Border:               s_border,
            BorderStrong:         s_borderStrong,
            Danger:               s_danger,
            White:                s_white),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        VisualAssets: s_freeXVisualAssets);

    /// <summary>Dark FreeW chrome that preserves FreeW's amber identity and artwork.</summary>
    public static readonly Theme FreeWMidnight = FreeXMidnight with
    {
        Name = "FreeWMidnight",
        Colors = FreeXMidnight.Colors with
        {
            Accent = FreeW.Colors.Accent,
            AccentSoft = FreeW.Colors.AccentSoft,
            AccentPressed = FreeW.Colors.AccentPressed,
            TitleBarHover = FreeW.Colors.TitleBarHover,
            RibbonButtonHover = FreeW.Colors.RibbonButtonHover,
            BackstageHover = FreeW.Colors.BackstageHover,
            BackstageLink = FreeW.Colors.BackstageLink,
        },
        VisualAssets = s_freeWVisualAssets,
    };

    /// <summary>Dark FreeP chrome that preserves FreeP's berry identity and artwork.</summary>
    public static readonly Theme FreePMidnight = FreeXMidnight with
    {
        Name = "FreePMidnight",
        Colors = FreeXMidnight.Colors with
        {
            Accent = FreeP.Colors.Accent,
            AccentSoft = FreeP.Colors.AccentSoft,
            AccentPressed = FreeP.Colors.AccentPressed,
            TitleBarHover = FreeP.Colors.TitleBarHover,
            RibbonButtonHover = FreeP.Colors.RibbonButtonHover,
            BackstageHover = FreeP.Colors.BackstageHover,
            BackstageLink = FreeP.Colors.BackstageLink,
        },
        VisualAssets = s_freePVisualAssets,
    };
}
