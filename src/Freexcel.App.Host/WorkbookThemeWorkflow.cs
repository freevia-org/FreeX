using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class WorkbookThemeWorkflow
{
    public static WorkbookTheme CreateColorfulTheme() =>
        ApplyColorfulColors(WorkbookTheme.Office)
            .WithName("Freexcel Colorful")
            .WithFonts("Aptos Display", "Aptos")
            .WithEffects("Office");

    public static WorkbookTheme CreateGrayscaleTheme() =>
        ApplyGrayscaleColors(WorkbookTheme.Office)
            .WithName("Grayscale")
            .WithFonts("Aptos Display", "Aptos")
            .WithEffects("Office");

    public static WorkbookTheme CreateCustomTheme(
        WorkbookTheme baseTheme,
        string name,
        string majorFontName,
        string minorFontName,
        string effectsName) =>
        baseTheme
            .WithName(name)
            .WithFonts(majorFontName, minorFontName)
            .WithEffects(effectsName);

    public static WorkbookTheme ApplyOfficeColors(WorkbookTheme theme) =>
        ApplyColors(
            theme,
            WorkbookThemeColorSlots.All.ToDictionary(
                slot => slot,
                slot => WorkbookTheme.Office.GetColor(slot)));

    public static WorkbookTheme ApplyColorfulColors(WorkbookTheme theme) =>
        ApplyColors(
            theme,
            new Dictionary<WorkbookThemeColorSlot, CellColor>
            {
                [WorkbookThemeColorSlot.Accent1] = new(21, 96, 130),
                [WorkbookThemeColorSlot.Accent2] = new(233, 113, 50),
                [WorkbookThemeColorSlot.Accent3] = new(25, 107, 36),
                [WorkbookThemeColorSlot.Accent4] = new(15, 158, 213),
                [WorkbookThemeColorSlot.Accent5] = new(160, 43, 147),
                [WorkbookThemeColorSlot.Accent6] = new(78, 167, 46)
            });

    public static WorkbookTheme ApplyGrayscaleColors(WorkbookTheme theme) =>
        ApplyColors(
            theme,
            new Dictionary<WorkbookThemeColorSlot, CellColor>
            {
                [WorkbookThemeColorSlot.Dark1] = new(0, 0, 0),
                [WorkbookThemeColorSlot.Light1] = new(255, 255, 255),
                [WorkbookThemeColorSlot.Dark2] = new(64, 64, 64),
                [WorkbookThemeColorSlot.Light2] = new(230, 230, 230),
                [WorkbookThemeColorSlot.Accent1] = new(89, 89, 89),
                [WorkbookThemeColorSlot.Accent2] = new(127, 127, 127),
                [WorkbookThemeColorSlot.Accent3] = new(166, 166, 166),
                [WorkbookThemeColorSlot.Accent4] = new(191, 191, 191),
                [WorkbookThemeColorSlot.Accent5] = new(217, 217, 217),
                [WorkbookThemeColorSlot.Accent6] = new(242, 242, 242)
            });

    private static WorkbookTheme ApplyColors(
        WorkbookTheme theme,
        IReadOnlyDictionary<WorkbookThemeColorSlot, CellColor> colors)
    {
        foreach (var (slot, color) in colors)
            theme = theme.WithColor(slot, color);

        return theme;
    }
}
