namespace Free.Shared.Theme;

/// <summary>Font weight semantic for theme tokens.</summary>
public enum ThemeFontWeight { Normal, SemiBold, Bold }

/// <summary>A single typography token: family, size, weight.</summary>
public sealed record ThemeTypeToken(string FontFamily, double SizePt, ThemeFontWeight Weight);

/// <summary>The four typography roles in a theme.</summary>
public sealed record ThemeTypography(
    ThemeTypeToken Body,
    ThemeTypeToken Caption,
    ThemeTypeToken RibbonLabel,
    ThemeTypeToken Heading);
