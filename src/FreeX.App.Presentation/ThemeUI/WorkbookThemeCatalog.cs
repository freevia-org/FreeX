using FreeX.Core.Model;

namespace FreeX.App.Presentation.ThemeUI;

public sealed record WorkbookThemePresetOption(
    string Label,
    string LabelResourceKey,
    Func<WorkbookTheme> CreateTheme,
    bool IsCustomizeAction = false);

public sealed record WorkbookThemeColorPresetOption(
    string Label,
    string LabelResourceKey,
    Func<WorkbookTheme, WorkbookTheme> ApplyColors,
    bool IsCustomizeAction = false);

public sealed record WorkbookThemeFontPresetOption(
    string Label,
    string LabelResourceKey,
    string MajorFontName,
    string MinorFontName,
    bool IsCustomizeAction = false)
{
    public WorkbookTheme ApplyFonts(WorkbookTheme theme) =>
        theme.WithFonts(MajorFontName, MinorFontName);
}

public sealed record WorkbookThemeEffectPresetOption(
    string Label,
    string LabelResourceKey,
    string EffectsName,
    bool IsCustomizeAction = false)
{
    public WorkbookTheme ApplyEffects(WorkbookTheme theme) =>
        theme.WithEffects(EffectsName);
}

public static class WorkbookThemeCatalog
{
    public static WorkbookThemePresetOption OfficeThemePreset { get; } =
        new("Office", "MainWindow_Header_Office", () => WorkbookTheme.Office);

    public static WorkbookThemePresetOption FreeXColorfulThemePreset { get; } =
        new("FreeX Colorful", "MainWindow_Header_FreeXColorful", WorkbookThemeWorkflow.CreateColorfulTheme);

    public static WorkbookThemePresetOption GrayscaleThemePreset { get; } =
        new("Grayscale", "MainWindow_Header_Grayscale", WorkbookThemeWorkflow.CreateGrayscaleTheme);

    public static WorkbookThemePresetOption CustomizeThemePreset { get; } =
        new("Customize...", "MainWindow_Header_Customize", () => WorkbookTheme.Office, IsCustomizeAction: true);

    public static WorkbookThemeColorPresetOption OfficeColorPreset { get; } =
        new("Office", "MainWindow_Header_Office", theme => WorkbookThemeWorkflow.ApplyOfficeColors(theme).WithName(theme.Name));

    public static WorkbookThemeColorPresetOption FreeXColorfulColorPreset { get; } =
        new("FreeX Colorful", "MainWindow_Header_FreeXColorful", theme => WorkbookThemeWorkflow.ApplyColorfulColors(theme).WithName(theme.Name));

    public static WorkbookThemeColorPresetOption GrayscaleColorPreset { get; } =
        new("Grayscale", "MainWindow_Header_Grayscale", theme => WorkbookThemeWorkflow.ApplyGrayscaleColors(theme).WithName(theme.Name));

    public static WorkbookThemeColorPresetOption CustomizeColorPreset { get; } =
        new("Customize Colors...", "MainWindow_Header_CustomizeColors", theme => theme, IsCustomizeAction: true);

    public static WorkbookThemeFontPresetOption OfficeFontPreset { get; } =
        new("Office", "MainWindow_Header_Office", WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName);

    public static WorkbookThemeFontPresetOption ArialFontPreset { get; } =
        new("Arial", "MainWindow_Header_Arial", "Arial", "Arial");

    public static WorkbookThemeFontPresetOption TimesNewRomanFontPreset { get; } =
        new("Times New Roman", "MainWindow_Header_TimesNewRoman", "Times New Roman", "Times New Roman");

    public static WorkbookThemeFontPresetOption CustomizeFontPreset { get; } =
        new("Customize Fonts...", "MainWindow_Header_CustomizeFonts", WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName, IsCustomizeAction: true);

    public static WorkbookThemeEffectPresetOption OfficeEffectPreset { get; } =
        new("Office", "MainWindow_Header_Office", WorkbookTheme.Office.EffectsName);

    public static WorkbookThemeEffectPresetOption SubtleEffectPreset { get; } =
        new("Subtle", "MainWindow_Header_Subtle", "Subtle");

    public static WorkbookThemeEffectPresetOption RefinedEffectPreset { get; } =
        new("Refined", "MainWindow_Header_Refined", "Refined");

    public static WorkbookThemeEffectPresetOption CustomizeEffectPreset { get; } =
        new("Customize Effects...", "MainWindow_Header_CustomizeEffects", WorkbookTheme.Office.EffectsName, IsCustomizeAction: true);

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
