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

        foreach (var slot in WorkbookThemeColorSlots.All)
        {
            var text = colorTextBySlot.TryGetValue(slot, out var value)
                ? value
                : string.Empty;

            try
            {
                theme = theme.WithColor(slot, WorkbookThemeDialogColorCodec.ParseColor(text ?? string.Empty));
            }
            catch (FormatException ex)
            {
                error = new WorkbookThemeDialogValidationError(slot, ex.Message);
                theme = initialTheme;
                return false;
            }
        }

        return true;
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

}

internal sealed record WorkbookThemeDialogValidationError(
    WorkbookThemeColorSlot Slot,
    string Message);
