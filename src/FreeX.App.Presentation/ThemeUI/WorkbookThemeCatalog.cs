using FreeX.Core.Model;

namespace FreeX.App.Presentation.ThemeUI;

public sealed record WorkbookThemePresetOption(string Label, Func<WorkbookTheme> CreateTheme, bool IsCustomizeAction = false);

public sealed record WorkbookThemeColorPresetOption(
    string Label,
    Func<WorkbookTheme, WorkbookTheme> ApplyColors,
    bool IsCustomizeAction = false);

public sealed record WorkbookThemeFontPresetOption(
    string Label,
    string MajorFontName,
    string MinorFontName,
    bool IsCustomizeAction = false)
{
    public WorkbookTheme ApplyFonts(WorkbookTheme theme) =>
        theme.WithFonts(MajorFontName, MinorFontName);
}

public sealed record WorkbookThemeEffectPresetOption(string Label, string EffectsName, bool IsCustomizeAction = false)
{
    public WorkbookTheme ApplyEffects(WorkbookTheme theme) =>
        theme.WithEffects(EffectsName);
}

public static class WorkbookThemeCatalog
{
    public static WorkbookThemePresetOption OfficeThemePreset { get; } =
        new("Office", () => WorkbookTheme.Office);

    public static WorkbookThemePresetOption FreeXColorfulThemePreset { get; } =
        new("FreeX Colorful", WorkbookThemeWorkflow.CreateColorfulTheme);

    public static WorkbookThemePresetOption GrayscaleThemePreset { get; } =
        new("Grayscale", WorkbookThemeWorkflow.CreateGrayscaleTheme);

    public static WorkbookThemePresetOption CustomizeThemePreset { get; } =
        new("Customize...", () => WorkbookTheme.Office, IsCustomizeAction: true);

    public static WorkbookThemeColorPresetOption OfficeColorPreset { get; } =
        new("Office", theme => WorkbookThemeWorkflow.ApplyOfficeColors(theme).WithName(theme.Name));

    public static WorkbookThemeColorPresetOption FreeXColorfulColorPreset { get; } =
        new("FreeX Colorful", theme => WorkbookThemeWorkflow.ApplyColorfulColors(theme).WithName(theme.Name));

    public static WorkbookThemeColorPresetOption GrayscaleColorPreset { get; } =
        new("Grayscale", theme => WorkbookThemeWorkflow.ApplyGrayscaleColors(theme).WithName(theme.Name));

    public static WorkbookThemeColorPresetOption CustomizeColorPreset { get; } =
        new("Customize Colors...", theme => theme, IsCustomizeAction: true);

    public static WorkbookThemeFontPresetOption OfficeFontPreset { get; } =
        new("Office", WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName);

    public static WorkbookThemeFontPresetOption ArialFontPreset { get; } =
        new("Arial", "Arial", "Arial");

    public static WorkbookThemeFontPresetOption TimesNewRomanFontPreset { get; } =
        new("Times New Roman", "Times New Roman", "Times New Roman");

    public static WorkbookThemeFontPresetOption CustomizeFontPreset { get; } =
        new("Customize Fonts...", WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName, IsCustomizeAction: true);

    public static WorkbookThemeEffectPresetOption OfficeEffectPreset { get; } =
        new("Office", WorkbookTheme.Office.EffectsName);

    public static WorkbookThemeEffectPresetOption SubtleEffectPreset { get; } =
        new("Subtle", "Subtle");

    public static WorkbookThemeEffectPresetOption RefinedEffectPreset { get; } =
        new("Refined", "Refined");

    public static WorkbookThemeEffectPresetOption CustomizeEffectPreset { get; } =
        new("Customize Effects...", WorkbookTheme.Office.EffectsName, IsCustomizeAction: true);

    public static IReadOnlyList<WorkbookThemePresetOption> ThemePresets { get; } =
    [
        OfficeThemePreset,
        FreeXColorfulThemePreset,
        GrayscaleThemePreset,
        CustomizeThemePreset
    ];

    public static IReadOnlyList<WorkbookThemeColorPresetOption> ColorPresets { get; } =
    [
        OfficeColorPreset,
        FreeXColorfulColorPreset,
        GrayscaleColorPreset,
        CustomizeColorPreset
    ];

    public static IReadOnlyList<WorkbookThemeFontPresetOption> FontPresets { get; } =
    [
        OfficeFontPreset,
        ArialFontPreset,
        TimesNewRomanFontPreset,
        CustomizeFontPreset
    ];

    public static IReadOnlyList<WorkbookThemeEffectPresetOption> EffectPresets { get; } =
    [
        OfficeEffectPreset,
        SubtleEffectPreset,
        RefinedEffectPreset,
        CustomizeEffectPreset
    ];
}
