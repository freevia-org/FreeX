using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ColorInputParser
{
    public static bool TryParseOptionalHexColor(string text, out CellColor? color)
    {
        color = null;
        var normalized = text.Trim();
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryParseHexColor(normalized, out color);
    }

    public static bool TryParseHexColor(string text, out CellColor? color)
    {
        color = null;
        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(normalized[2..4], NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(normalized[4..6], NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = new CellColor(r, g, b);
        return true;
    }

    public static string FormatHexColor(CellColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
