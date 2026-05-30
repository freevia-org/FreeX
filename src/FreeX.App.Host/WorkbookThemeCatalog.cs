using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record WorkbookThemePresetOption(string Label, Func<WorkbookTheme> CreateTheme, bool IsCustomizeAction = false);

internal sealed record WorkbookThemeColorPresetOption(
    string Label,
    Func<WorkbookTheme, WorkbookTheme> ApplyColors,
    bool IsCustomizeAction = false);

internal sealed record WorkbookThemeFontPresetOption(
    string Label,
    string MajorFontName,
    string MinorFontName,
    bool IsCustomizeAction = false);

internal sealed record WorkbookThemeEffectPresetOption(string Label, string EffectsName, bool IsCustomizeAction = false);

internal static class WorkbookThemeCatalog
{
    public static IReadOnlyList<WorkbookThemePresetOption> ThemePresets { get; } =
    [
        new("Office", () => WorkbookTheme.Office),
        new("FreeX Colorful", WorkbookThemeWorkflow.CreateColorfulTheme),
        new("Grayscale", WorkbookThemeWorkflow.CreateGrayscaleTheme),
        new("Customize...", () => WorkbookTheme.Office, IsCustomizeAction: true)
    ];

    public static IReadOnlyList<WorkbookThemeColorPresetOption> ColorPresets { get; } =
    [
        new("Office", theme => WorkbookThemeWorkflow.ApplyOfficeColors(theme).WithName(theme.Name)),
        new("FreeX Colorful", theme => WorkbookThemeWorkflow.ApplyColorfulColors(theme).WithName(theme.Name)),
        new("Grayscale", theme => WorkbookThemeWorkflow.ApplyGrayscaleColors(theme).WithName(theme.Name)),
        new("Customize Colors...", theme => theme, IsCustomizeAction: true)
    ];

    public static IReadOnlyList<WorkbookThemeFontPresetOption> FontPresets { get; } =
    [
        new("Office", WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName),
        new("Arial", "Arial", "Arial"),
        new("Times New Roman", "Times New Roman", "Times New Roman"),
        new("Customize Fonts...", WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName, IsCustomizeAction: true)
    ];

    public static IReadOnlyList<WorkbookThemeEffectPresetOption> EffectPresets { get; } =
    [
        new("Office", WorkbookTheme.Office.EffectsName),
        new("Subtle", "Subtle"),
        new("Refined", "Refined"),
        new("Customize Effects...", WorkbookTheme.Office.EffectsName, IsCustomizeAction: true)
    ];
}
