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

    // ── Default typography (not wired to rendering in round 1) ──
    private static readonly ThemeTypography s_defaultTypography = new(
        Body:        new ThemeTypeToken("Segoe UI",  9.0,  ThemeFontWeight.Normal),
        Caption:     new ThemeTypeToken("Segoe UI",  8.0,  ThemeFontWeight.Normal),
        RibbonLabel: new ThemeTypeToken("Segoe UI",  9.0,  ThemeFontWeight.Normal),
        Heading:     new ThemeTypeToken("Segoe UI", 14.0,  ThemeFontWeight.SemiBold));

    // ── Default metrics (not wired to rendering in round 1) ──
    private static readonly ThemeMetrics s_defaultMetrics = new(
        RibbonRowHeight: 22.0,
        ControlHeight:   24.0,
        IconSize:        16.0,
        CornerRadius:     2.0);

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
    /// FreeW (word processor) brand theme.
    /// PROVISIONAL: accent/title-bar set to FreeW's teal family; surfaces/text/borders
    /// reuse FreeX neutrals pending FreeW's own final palette review.
    /// </summary>
    public static readonly Theme FreeW = new(
        Name: "FreeW",
        Colors: new ThemeColors(
            Accent:               ThemeColor.FromHex("#107C41"),   // FreeW teal (provisional)
            AccentDark:           ThemeColor.FromHex("#0A5C32"),   // FreeW teal dark (provisional)
            AccentSoft:           ThemeColor.FromHex("#E8F5EC"),
            AccentPressed:        ThemeColor.FromHex("#CCE8D8"),
            TitleBar:             ThemeColor.FromHex("#0A5C32"),   // FreeW teal dark (provisional)
            TitleBarHover:        ThemeColor.FromHex("#107C41"),
            TitleBarPressed:      ThemeColor.FromHex("#074027"),
            TitleBarDisabled:     ThemeColor.FromHex("#8BA6B8"),
            TitleBarButtonBorder: ThemeColor.FromHex("#55FFFFFF"),
            RibbonButtonHover:    ThemeColor.FromHex("#BEEFD1"),
            Text:                 s_text,
            MutedText:            s_mutedText,
            SubtleText:           s_subtleText,
            RibbonSurface:        s_ribbonSurf,
            ChromeSurface:        s_chromeSurf,
            SheetSurface:         s_sheetSurf,
            StatusSurface:        ThemeColor.FromHex("#0A5C32"),
            Border:               s_border,
            BorderStrong:         s_borderStrong,
            Danger:               s_danger,
            White:                s_white),
        Typography: s_defaultTypography,
        Metrics:    s_defaultMetrics,
        IconSetId:  "freew");

    /// <summary>
    /// FreeP (presentation) brand theme.
    /// PROVISIONAL: accent/title-bar set to FreeP's brick family (#B7472A); surfaces/text/borders
    /// reuse FreeX neutrals pending FreeP's own final palette review.
    /// </summary>
    public static readonly Theme FreeP = new(
        Name: "FreeP",
        Colors: new ThemeColors(
            Accent:               ThemeColor.FromHex("#B7472A"),   // FreeP brick (provisional)
            AccentDark:           ThemeColor.FromHex("#8C3520"),   // FreeP brick dark (provisional)
            AccentSoft:           ThemeColor.FromHex("#F9EAE6"),
            AccentPressed:        ThemeColor.FromHex("#F2D2CB"),
            TitleBar:             ThemeColor.FromHex("#8C3520"),
            TitleBarHover:        ThemeColor.FromHex("#B7472A"),
            TitleBarPressed:      ThemeColor.FromHex("#6A2718"),
            TitleBarDisabled:     ThemeColor.FromHex("#8BA6B8"),
            TitleBarButtonBorder: ThemeColor.FromHex("#55FFFFFF"),
            RibbonButtonHover:    ThemeColor.FromHex("#FDDDD6"),
            Text:                 s_text,
            MutedText:            s_mutedText,
            SubtleText:           s_subtleText,
            RibbonSurface:        s_ribbonSurf,
            ChromeSurface:        s_chromeSurf,
            SheetSurface:         s_sheetSurf,
            StatusSurface:        ThemeColor.FromHex("#8C3520"),
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
