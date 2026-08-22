namespace Free.Shared.Theme;

/// <summary>
/// The semantic color roles that drive all chrome surfaces in a FreeFamily app.
/// Seeded from FreeX's ThemeResources.xaml palette; other apps provide alternate values.
/// </summary>
public sealed record ThemeColors(
    ThemeColor Accent,
    ThemeColor AccentDark,
    ThemeColor AccentSoft,
    ThemeColor AccentPressed,
    ThemeColor TitleBar,
    ThemeColor TitleBarForeground,
    ThemeColor TitleBarHover,
    ThemeColor TitleBarPressed,
    ThemeColor TitleBarDisabled,
    ThemeColor TitleBarButtonBorder,
    ThemeColor RibbonButtonHover,
    ThemeColor RibbonInlineDivider,
    ThemeColor Text,
    ThemeColor MutedText,
    ThemeColor SubtleText,
    ThemeColor RibbonSurface,
    ThemeColor ChromeSurface,
    ThemeColor SheetSurface,
    ThemeColor StatusSurface,
    ThemeColor StatusForeground,
    ThemeColor BackstageSidebar,
    ThemeColor BackstageHover,
    ThemeColor BackstageSelected,
    ThemeColor BackstageSeparator,
    ThemeColor BackstageLink,
    ThemeColor Border,
    ThemeColor BorderStrong,
    ThemeColor Danger,
    ThemeColor White);
