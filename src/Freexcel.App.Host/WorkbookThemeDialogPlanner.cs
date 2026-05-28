using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class WorkbookThemeDialogPlanner
{
    public static bool TryCreateTheme(
        WorkbookTheme initialTheme,
        string? name,
        string? headingFont,
        string? bodyFont,
        string? effects,
        IReadOnlyDictionary<WorkbookThemeColorSlot, string> colorTextBySlot,
        out WorkbookTheme theme,
        out WorkbookThemeDialogValidationError? error)
    {
        theme = WorkbookThemeWorkflow.CreateCustomTheme(
            initialTheme,
            name ?? string.Empty,
            headingFont ?? string.Empty,
            bodyFont ?? string.Empty,
            effects ?? string.Empty);
        error = null;

        if (TryApplyThemeColors(theme, colorTextBySlot, out var themedPalette, out error))
        {
            theme = themedPalette;
            return true;
        }

        theme = initialTheme;
        return false;
    }

    public static CellColor PreviewColorOrBlack(string? text)
    {
        try
        {
            return WorkbookThemeDialogColorCodec.ParseColor(text ?? string.Empty);
        }
        catch (FormatException)
        {
            return new CellColor(0, 0, 0);
        }
    }

    private static bool TryApplyThemeColors(
        WorkbookTheme theme,
        IReadOnlyDictionary<WorkbookThemeColorSlot, string> colorTextBySlot,
        out WorkbookTheme themedPalette,
        out WorkbookThemeDialogValidationError? error)
    {
        themedPalette = theme;
        error = null;

        foreach (var slot in WorkbookThemeColorSlots.All)
        {
            try
            {
                themedPalette = themedPalette.WithColor(
                    slot,
                    WorkbookThemeDialogColorCodec.ParseColor(ReadColorText(colorTextBySlot, slot)));
            }
            catch (FormatException ex)
            {
                error = new WorkbookThemeDialogValidationError(slot, ex.Message);
                themedPalette = theme;
                return false;
            }
        }

        return true;
    }

    private static string ReadColorText(
        IReadOnlyDictionary<WorkbookThemeColorSlot, string> colorTextBySlot,
        WorkbookThemeColorSlot slot) =>
        colorTextBySlot.TryGetValue(slot, out var value)
            ? value ?? string.Empty
            : string.Empty;
}

internal sealed record WorkbookThemeDialogValidationError(
    WorkbookThemeColorSlot Slot,
    string Message);
