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

        foreach (var slot in WorkbookThemeColorSlots)
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

    private static readonly WorkbookThemeColorSlot[] WorkbookThemeColorSlots =
    [
        WorkbookThemeColorSlot.Dark1,
        WorkbookThemeColorSlot.Light1,
        WorkbookThemeColorSlot.Dark2,
        WorkbookThemeColorSlot.Light2,
        WorkbookThemeColorSlot.Accent1,
        WorkbookThemeColorSlot.Accent2,
        WorkbookThemeColorSlot.Accent3,
        WorkbookThemeColorSlot.Accent4,
        WorkbookThemeColorSlot.Accent5,
        WorkbookThemeColorSlot.Accent6,
        WorkbookThemeColorSlot.Hyperlink,
        WorkbookThemeColorSlot.FollowedHyperlink
    ];
}

internal sealed record WorkbookThemeDialogValidationError(
    WorkbookThemeColorSlot Slot,
    string Message);
