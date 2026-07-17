using FreeX.Core.Model;

namespace FreeX.App.Presentation.ThemeUI;

public static class WorkbookThemeDialogPlanner
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
                var parsedColor = WorkbookThemeDialogColorCodec.ParseColor(ReadColorText(colorTextBySlot, slot));

                // Only patch the slots the user actually changed. WithColor always rewrites the
                // slot's native XML as a fresh <a:srgbClr>, so calling it for every slot on every
                // Save (even ones whose dialog text round-tripped straight back from the theme's
                // existing baked RGB) would bake untouched "Automatic" sysClr slots (dk1/lt1) --
                // and any lumMod/lumOff/tint transform on any slot -- into plain literal colors,
                // which real Excel's own Customize Colors dialog never does.
                if (parsedColor == theme.GetColor(slot))
                    continue;

                themedPalette = themedPalette.WithColor(slot, parsedColor);
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

public sealed record WorkbookThemeDialogValidationError(
    WorkbookThemeColorSlot Slot,
    string Message);
