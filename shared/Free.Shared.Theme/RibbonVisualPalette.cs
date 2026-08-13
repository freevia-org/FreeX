namespace Free.Shared.Theme;

/// <summary>
/// Framework-neutral visual roles used by the shared ribbon renderer.
/// Every value is derived from an app's <see cref="ThemeColors"/> so the renderer never owns a
/// product-specific color literal.
/// </summary>
public sealed record RibbonVisualPalette(
    ThemeColor Surface,
    ThemeColor Accent,
    ThemeColor Divider,
    ThemeColor InlineDivider,
    ThemeColor GroupLabel,
    ThemeColor Hover,
    ThemeColor HoverBorder,
    ThemeColor Checked,
    ThemeColor TabHover,
    ThemeColor TabStrip,
    ThemeColor TabText)
{
    /// <summary>
    /// Neutral fallback used by renderer-only tests and tooling. Product hosts should pass the
    /// palette derived from their active theme.
    /// </summary>
    public static RibbonVisualPalette DefaultNeutral { get; } = new(
        Surface: ThemeColor.FromHex("#FFFFFF"),
        Accent: ThemeColor.FromHex("#0F6D8C"),
        Divider: ThemeColor.FromHex("#DADCE0"),
        InlineDivider: ThemeColor.FromHex("#CCCCCC"),
        GroupLabel: ThemeColor.FromHex("#5F6368"),
        Hover: ThemeColor.FromHex("#BEE6FD"),
        HoverBorder: ThemeColor.FromHex("#C8CCD0"),
        Checked: ThemeColor.FromHex("#CCEAF2"),
        TabHover: ThemeColor.FromHex("#E6F6FA"),
        TabStrip: ThemeColor.FromHex("#F7F8F8"),
        TabText: ThemeColor.FromHex("#1F1F1F"));

    public static RibbonVisualPalette FromTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var colors = theme.Colors;
        return new RibbonVisualPalette(
            Surface: colors.RibbonSurface,
            Accent: colors.Accent,
            Divider: colors.Border,
            InlineDivider: colors.RibbonInlineDivider,
            GroupLabel: colors.MutedText,
            Hover: colors.RibbonButtonHover,
            HoverBorder: colors.BorderStrong,
            Checked: colors.AccentPressed,
            TabHover: colors.AccentSoft,
            TabStrip: colors.ChromeSurface,
            TabText: colors.Text);
    }
}
