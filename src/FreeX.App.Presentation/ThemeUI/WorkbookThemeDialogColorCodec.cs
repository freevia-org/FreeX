using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.ThemeUI;

public static class WorkbookThemeDialogColorCodec
{
    public static string FormatColor(CellColor color) =>
        ColorInputParser.FormatHexColor(color);

    public static CellColor ParseColor(string text)
    {
        if (!ColorInputParser.TryParseHexColor(text, out var color) || color is not { } parsedColor)
            throw new FormatException("Enter theme colors as #RRGGBB values.");

        return parsedColor;
    }
}
