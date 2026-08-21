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
            Border:               ThemeColor.FromHex("#DADCE0"),
            BorderStrong:         ThemeColor.FromHex("#C8CCD0"),
            Danger:               ThemeColor.FromHex("#C42B1C"),
            White:                ThemeColor.FromHex("#FFFFFF")),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        IconSetId:  "freex");

    /// <summary>
    /// FreeW (word processor) brand theme.
    /// Colors are BYTE-IDENTICAL to FreeW's current chrome:
    ///   <c>freew/FreeW.App.Host/MainWindow.cs</c> (title-bar <c>#17324D</c> / badge <c>#0F6D8C</c>),
    ///   <c>freew/FreeW.App.Host/Ribbon/FreeWRibbonResources.xaml</c> (ribbon brushes),
    ///   <c>shared/Free.Shared.Shell.Wpf/SisterBackstageTheme.cs</c> (backstage sidebar/link).
    ///
    /// <para>
    /// NOTE — internal inconsistency (intentionally preserved, not unified):
    ///   The FreeW title bar / ribbon chrome uses the FreeX navy family (<c>Accent=#0F6D8C</c>,
    ///   <c>TitleBar=#17324D</c>).  The backstage link accent (<see cref="SisterBackstageTheme.FreeW"/>
    ///   <c>LinkColor</c>) is sourced from this <see cref="Accent"/> role and therefore also
    ///   <c>#0F6D8C</c> — both areas share the same token but were conceptually separate
    ///   (chrome blue vs backstage teal).  Round 5 captures this as-is; future rounds may
    ///   introduce a dedicated backstage-accent role.
    /// </para>
    /// </summary>
    public static readonly Theme FreeW = new(
        Name: "FreeW",
        Colors: new ThemeColors(
            // ── Accent family: sourced from MainWindow.cs BadgeColor (#0F6D8C) and
            //   FreeWRibbonResources.xaml FreeXAccentBrush (#0F6D8C). ──
            Accent:               ThemeColor.FromHex("#0F6D8C"),
            AccentDark:           ThemeColor.FromHex("#17324D"),
            AccentSoft:           ThemeColor.FromHex("#E6F6FA"),   // FreeWRibbonResources.xaml FreeXAccentSoftBrush
            AccentPressed:        ThemeColor.FromHex("#CFEAF1"),   // FreeWRibbonResources.xaml FreeXAccentPressedBrush
            // ── Title bar: sourced from MainWindow.cs TitleBarColor (#17324D). ──
            TitleBar:             ThemeColor.FromHex("#17324D"),
            TitleBarForeground:   s_white,
            TitleBarHover:        ThemeColor.FromHex("#0F6D8C"),
            TitleBarPressed:      ThemeColor.FromHex("#10253A"),
            TitleBarDisabled:     ThemeColor.FromHex("#8BA6B8"),
            TitleBarButtonBorder: ThemeColor.FromHex("#55FFFFFF"),
            // ── Ribbon hover: FreeWRibbonResources.xaml FreeXRibbonButtonHoverBrush (#E6F6FA). ──
            RibbonButtonHover:    ThemeColor.FromHex("#E6F6FA"),
            RibbonInlineDivider:  ThemeColor.FromHex("#CCCCCC"),
            Text:                 s_text,
            MutedText:            s_mutedText,
            SubtleText:           s_subtleText,
            RibbonSurface:        s_ribbonSurf,
            ChromeSurface:        s_chromeSurf,
            // ── Sheet/workspace surface: MainWindow.cs Background (#F3F3F3). ──
            SheetSurface:         s_sheetSurf,
            // ── Status bar: MainWindow.cs BuildStatusBar surface (#17324D). ──
            StatusSurface:        ThemeColor.FromHex("#17324D"),
            StatusForeground:     s_white,
            Border:               s_border,
            BorderStrong:         s_borderStrong,
            Danger:               s_danger,
            White:                s_white),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        IconSetId:  "freew");

    /// <summary>
    /// FreeP (presentation) brand theme.
    /// Colors are BYTE-IDENTICAL to FreeP's current chrome:
    ///   <c>freep/FreeP.App.Host/MainWindow.cs</c>
    ///     TitleBarColor = <c>#B7472A</c> (ChromeOptions, line 24)
    ///     BadgeColor    = <c>#8F3721</c> (ChromeOptions, line 25)
    ///     FileTabAccent = <c>#B7472A</c> (RibbonShellBuilder, line 237)
    ///     FileTabHover  = <c>#8F3721</c> (RibbonShellBuilder, line 238)
    ///     StatusBar surface = <c>#B7472A</c> (BuildStatusBar, line 192)
    ///   <c>shared/Free.Shared.Shell.Wpf/SisterBackstageTheme.cs</c>
    ///     FreeP.LinkColor = <c>#B7472A</c> (now routed through Accent token — byte-identical).
    /// Surfaces/text/borders reuse FreeX neutrals (FreeP scaffold has no distinct neutral chrome).
    /// </summary>
    public static readonly Theme FreeP = new(
        Name: "FreeP",
        Colors: new ThemeColors(
            // ── Accent family: brick primary sourced from TitleBarColor/FileTabAccent/StatusBar/LinkColor ──
            Accent:               ThemeColor.FromHex("#B7472A"),   // FreeP brick primary
            // ── AccentDark: sourced from BadgeColor / FileTabHover (#8F3721) ──
            AccentDark:           ThemeColor.FromHex("#8F3721"),
            AccentSoft:           ThemeColor.FromHex("#F9EAE6"),
            AccentPressed:        ThemeColor.FromHex("#F2D2CB"),
            // ── TitleBar: sourced from MainWindow.cs ChromeOptions.TitleBarColor (#B7472A) ──
            TitleBar:             ThemeColor.FromHex("#B7472A"),
            TitleBarForeground:   s_white,
            TitleBarHover:        ThemeColor.FromHex("#C95A3D"),   // BackstageAccent.Hover (#C95A3D)
            TitleBarPressed:      ThemeColor.FromHex("#8F3721"),   // BackstageAccent.Selected (#8F3721)
            TitleBarDisabled:     ThemeColor.FromHex("#8BA6B8"),
            TitleBarButtonBorder: ThemeColor.FromHex("#55FFFFFF"),
            RibbonButtonHover:    ThemeColor.FromHex("#FDDDD6"),
            RibbonInlineDivider:  ThemeColor.FromHex("#CCCCCC"),
            Text:                 s_text,
            MutedText:            s_mutedText,
            SubtleText:           s_subtleText,
            RibbonSurface:        s_ribbonSurf,
            ChromeSurface:        s_chromeSurf,
            SheetSurface:         s_sheetSurf,
            // ── StatusSurface: sourced from MainWindow.cs BuildStatusBar SolidColorBrush (#B7472A) ──
            StatusSurface:        ThemeColor.FromHex("#B7472A"),
            StatusForeground:     s_white,
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
            Border:               s_border,
            BorderStrong:         s_borderStrong,
            Danger:               s_danger,
            White:                s_white),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        IconSetId:  "freex");
}
