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
        theme
            .WithColor(WorkbookThemeColorSlot.Dark1, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Dark1))
            .WithColor(WorkbookThemeColorSlot.Light1, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Light1))
            .WithColor(WorkbookThemeColorSlot.Dark2, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Dark2))
            .WithColor(WorkbookThemeColorSlot.Light2, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Light2))
            .WithColor(WorkbookThemeColorSlot.Accent1, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1))
            .WithColor(WorkbookThemeColorSlot.Accent2, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent2))
            .WithColor(WorkbookThemeColorSlot.Accent3, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent3))
            .WithColor(WorkbookThemeColorSlot.Accent4, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent4))
            .WithColor(WorkbookThemeColorSlot.Accent5, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent5))
            .WithColor(WorkbookThemeColorSlot.Accent6, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent6))
            .WithColor(WorkbookThemeColorSlot.Hyperlink, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Hyperlink))
            .WithColor(WorkbookThemeColorSlot.FollowedHyperlink, WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.FollowedHyperlink));

    public static WorkbookTheme ApplyColorfulColors(WorkbookTheme theme) =>
        theme
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(21, 96, 130))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(233, 113, 50))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(25, 107, 36))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(15, 158, 213))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(160, 43, 147))
            .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(78, 167, 46));

    public static WorkbookTheme ApplyGrayscaleColors(WorkbookTheme theme) =>
        theme
            .WithColor(WorkbookThemeColorSlot.Dark1, new CellColor(0, 0, 0))
            .WithColor(WorkbookThemeColorSlot.Light1, new CellColor(255, 255, 255))
            .WithColor(WorkbookThemeColorSlot.Dark2, new CellColor(64, 64, 64))
            .WithColor(WorkbookThemeColorSlot.Light2, new CellColor(230, 230, 230))
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(89, 89, 89))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(127, 127, 127))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(166, 166, 166))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(191, 191, 191))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(217, 217, 217))
            .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(242, 242, 242));
}
