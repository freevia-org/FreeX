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

    public static bool TryParseColorText(string text, out CellColor color)
    {
        color = default;
        if (TryParseHexColor(text, out var hexColor) && hexColor is { } parsedHex)
        {
            color = parsedHex;
            return true;
        }

        if (TryParseRgbColorText(text, out var rgbColor))
        {
            color = rgbColor;
            return true;
        }

        return false;
    }

    public static bool TryParseRgbColorText(string text, out CellColor color)
    {
        color = default;
        var parts = text.Trim().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        if (!byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new CellColor(r, g, b);
        return true;
    }

    public static string FormatRgbColor(CellColor color) =>
        $"{color.R},{color.G},{color.B}";

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
