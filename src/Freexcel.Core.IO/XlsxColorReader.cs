using Freexcel.Core.Model;
using System.Globalization;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

public static class XlsxColorReader
{
    public static bool TryParseHexColor(string? text, out CellColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().TrimStart('#');
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

    public static bool TryReadRgbColor(XElement? element, out RgbColor color)
    {
        color = default;
        var rgb = element?.Attribute("rgb")?.Value;
        if (string.IsNullOrWhiteSpace(rgb))
            return false;

        var normalized = NormalizeRgbAttribute(rgb);
        if (!TryParseHexColor(normalized, out var cellColor))
            return false;

        color = RgbColor.FromCellColor(cellColor);
        return true;
    }

    public static bool TryReadCellColor(XElement? element, out CellColor color)
    {
        color = default;
        var rgb = element?.Attribute("rgb")?.Value;
        if (string.IsNullOrWhiteSpace(rgb))
            return false;

        return TryParseHexColor(NormalizeRgbAttribute(rgb), out color);
    }

    private static string NormalizeRgbAttribute(string rgb)
    {
        var normalized = rgb.Trim().TrimStart('#');
        return normalized.Length == 8
            ? normalized[2..]
            : normalized;
    }
}
