using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Maps a FreeX <see cref="CellBorder"/> to an ODF <c>fo:border</c> value (<c>&lt;width&gt; &lt;style&gt;
/// &lt;color&gt;</c>, e.g. <c>0.5pt solid #000000</c>) and back. ODF has fewer named line styles than
/// Excel, so the structural mapping is coarse — exact recovery of the Excel <see cref="BorderStyle"/> is
/// driven by the per-edge <c>freex-border-*</c> hint emitted alongside (this class only builds the
/// rendering value).
/// </summary>
internal static class OdsBorder
{
    public static string ToOdf(CellBorder border)
    {
        var (width, line) = border.Style switch
        {
            BorderStyle.Thin => ("0.5pt", "solid"),
            BorderStyle.Medium => ("1pt", "solid"),
            BorderStyle.Thick => ("2pt", "solid"),
            BorderStyle.Dashed => ("0.5pt", "dashed"),
            BorderStyle.Dotted => ("0.5pt", "dotted"),
            BorderStyle.Double => ("1.5pt", "double"),
            _ => ("0.5pt", "solid"),
        };
        return $"{width} {line} {OdsStyleRegistry.HexColor(border.Color)}";
    }

    /// <summary>Parses the verbatim per-edge hint "<c>&lt;styleInt&gt;:#RRGGBB</c>" back into a border.</summary>
    public static CellBorder FromHint(string hint)
    {
        var colon = hint.IndexOf(':');
        if (colon < 0)
            return default;
        if (!int.TryParse(hint.AsSpan(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out var styleInt))
            return default;
        var style = (BorderStyle)styleInt;
        var color = ParseHexColor(hint[(colon + 1)..]);
        return new CellBorder(style, color);
    }

    /// <summary>Best-effort parse of an ODF <c>fo:border</c> value when no FreeX hint is present.</summary>
    public static CellBorder FromOdf(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var style = BorderStyle.Thin;
        var color = CellColor.Black;
        foreach (var part in parts)
        {
            if (part.StartsWith('#'))
                color = ParseHexColor(part);
            else if (part is "dashed")
                style = BorderStyle.Dashed;
            else if (part is "dotted")
                style = BorderStyle.Dotted;
            else if (part is "double")
                style = BorderStyle.Double;
            else if (part.EndsWith("pt", StringComparison.Ordinal) &&
                     double.TryParse(part.AsSpan(0, part.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var pt))
            {
                style = pt >= 2 ? BorderStyle.Thick : pt >= 1 ? BorderStyle.Medium : BorderStyle.Thin;
            }
        }
        return new CellBorder(style, color);
    }

    internal static CellColor ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6)
            return CellColor.Black;

        // The length check does not make the characters hex: an ODS style with fo:color="#GGGGGG"
        // is six characters and would throw FormatException out of the middle of the load. Every
        // other adapter here parses hostile file data with TryParse and falls back rather than
        // throwing a raw exception type; this was the outlier.
        if (!byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return CellColor.Black;

        return new CellColor(r, g, b);
    }
}
