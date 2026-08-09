namespace Free.Shared.Theme;

/// <summary>A portable semantic color resource and its canonical resource keys.</summary>
public sealed record ThemeColorResourcePlan(
    string Role,
    string BrushKey,
    string ColorKey,
    ThemeColor Value);

/// <summary>A prefix-free brush alias consumed by shared renderer resources.</summary>
public sealed record ThemeSharedBrushResourcePlan(
    string Key,
    ThemeColor Value);

/// <summary>A portable metric resource and its canonical resource key.</summary>
public sealed record ThemeMetricResourcePlan(
    string Role,
    string Key,
    double Value);

/// <summary>A portable typography resource and its canonical resource keys.</summary>
public sealed record ThemeTypographyResourcePlan(
    string Role,
    string FontFamilyKey,
    string FontSizeKey,
    string FontWeightKey,
    ThemeTypeToken Value);

/// <summary>
/// Owns the semantic resource inventory shared by the WPF and Avalonia theme materializers.
/// Platform packages remain responsible for creating native brushes, colors, fonts, and weights.
/// </summary>
public sealed record ThemeResourcePlan(
    IReadOnlyList<ThemeColorResourcePlan> Colors,
    IReadOnlyList<ThemeMetricResourcePlan> Metrics,
    IReadOnlyList<ThemeTypographyResourcePlan> Typography,
    IReadOnlyList<ThemeSharedBrushResourcePlan> SharedBrushes)
{
    public static ThemeResourcePlan Create(Theme theme, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentException.ThrowIfNullOrEmpty(keyPrefix);

        var colors = theme.Colors;
        var metrics = theme.Metrics;
        var typography = theme.Typography;

        ThemeColorResourcePlan Color(string role, ThemeColor value) =>
            new(
                role,
                $"{keyPrefix}{role}Brush",
                $"{keyPrefix}{role}Color",
                value);

        ThemeMetricResourcePlan Metric(string role, double value) =>
            new(role, $"{keyPrefix}{role}", value);

        ThemeTypographyResourcePlan Type(string role, ThemeTypeToken value) =>
            new(
                role,
                $"{keyPrefix}{role}FontFamily",
                $"{keyPrefix}{role}FontSize",
                $"{keyPrefix}{role}FontWeight",
                value);

        ThemeSharedBrushResourcePlan SharedBrush(string key, ThemeColor value) =>
            new(key, value);

        return new ThemeResourcePlan(
            Colors:
            [
                Color("Accent", colors.Accent),
                Color("AccentDark", colors.AccentDark),
                Color("AccentSoft", colors.AccentSoft),
                Color("AccentPressed", colors.AccentPressed),
                Color("TitleBar", colors.TitleBar),
                Color("TitleBarHover", colors.TitleBarHover),
                Color("TitleBarPressed", colors.TitleBarPressed),
                Color("TitleBarDisabled", colors.TitleBarDisabled),
                Color("TitleBarButtonBorder", colors.TitleBarButtonBorder),
                Color("RibbonButtonHover", colors.RibbonButtonHover),
                Color("RibbonInlineDivider", colors.RibbonInlineDivider),
                Color("Text", colors.Text),
                Color("MutedText", colors.MutedText),
                Color("SubtleText", colors.SubtleText),
                Color("RibbonSurface", colors.RibbonSurface),
                Color("ChromeSurface", colors.ChromeSurface),
                Color("SheetSurface", colors.SheetSurface),
                Color("StatusSurface", colors.StatusSurface),
                Color("Border", colors.Border),
                Color("BorderStrong", colors.BorderStrong),
                Color("Danger", colors.Danger),
                Color("White", colors.White),
            ],
            Metrics:
            [
                Metric("RibbonRowHeight", metrics.RibbonRowHeight),
                Metric("ControlHeight", metrics.ControlHeight),
                Metric("IconSize", metrics.IconSize),
                Metric("CornerRadius", metrics.CornerRadius),
                Metric("StatusBarHeight", metrics.StatusBarHeight),
                Metric("TitleBarCaptionHeight", metrics.TitleBarCaptionHeight),
            ],
            Typography:
            [
                Type("Body", typography.Body),
                Type("Caption", typography.Caption),
                Type("RibbonLabel", typography.RibbonLabel),
                Type("Heading", typography.Heading),
                Type("StatusBarText", typography.StatusBarText),
            ],
            SharedBrushes:
            [
                SharedBrush("ThemeNeutralTextBrush", colors.Text),
                SharedBrush("ThemeNeutralMutedTextBrush", colors.MutedText),
                SharedBrush("ThemeNeutralWhiteBrush", colors.White),
                SharedBrush("ThemeNeutralDangerBrush", colors.Danger),
                SharedBrush("ThemeNeutralSheetSurfaceBrush", colors.SheetSurface),
                SharedBrush("ThemeNeutralBorderBrush", colors.Border),
                SharedBrush("ThemeNeutralBorderStrongBrush", colors.BorderStrong),
                SharedBrush("ThemeAccentBrush", colors.Accent),
                SharedBrush("ThemeAccentDarkBrush", colors.AccentDark),
                SharedBrush("ThemeAccentSoftBrush", colors.AccentSoft),
                SharedBrush("ThemeAccentPressedBrush", colors.AccentPressed),
                SharedBrush("ThemeRibbonButtonHoverBrush", colors.RibbonButtonHover),
            ]);
    }
}
