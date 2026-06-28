using FreeX.Core.Model;
using SharedColorInputParser = FreeX.App.Presentation.ColorInputParser;

namespace FreeX.App.Host;

public static class ColorInputParser
{
    public static bool TryParseOptionalHexColor(string text, out CellColor? color) =>
        SharedColorInputParser.TryParseOptionalHexColor(text, out color);

    public static bool TryParseColorText(string text, out CellColor color) =>
        SharedColorInputParser.TryParseColorText(text, out color);

    public static bool TryParseRgbColorText(string text, out CellColor color) =>
        SharedColorInputParser.TryParseRgbColorText(text, out color);

    public static string FormatRgbColor(CellColor color) =>
        SharedColorInputParser.FormatRgbColor(color);

    public static bool TryParseHexColor(string text, out CellColor? color) =>
        SharedColorInputParser.TryParseHexColor(text, out color);

    public static string FormatHexColor(CellColor color) =>
        SharedColorInputParser.FormatHexColor(color);
}
