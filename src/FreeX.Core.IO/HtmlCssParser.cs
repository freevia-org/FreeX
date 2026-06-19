using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Parses the compact inline-CSS subset that <see cref="HtmlTableWriter"/> emits on each cell back into a
/// <see cref="CellStyle"/>. This is the exact inverse of the writer's <c>BuildCss</c> mapping, so an
/// xlsx→html→xlsx round-trip recovers the styling HTML can carry:
/// <list type="bullet">
///   <item><c>font-weight:bold</c> → <see cref="CellStyle.Bold"/></item>
///   <item><c>font-style:italic</c> → <see cref="CellStyle.Italic"/></item>
///   <item><c>text-decoration:underline</c> → <see cref="CellStyle.Underline"/></item>
///   <item><c>font-family:'name'</c> → <see cref="CellStyle.FontName"/></item>
///   <item><c>font-size:Npt</c> → <see cref="CellStyle.FontSize"/></item>
///   <item><c>color:#rrggbb</c> → <see cref="CellStyle.FontColor"/></item>
///   <item><c>background-color:#rrggbb</c> → solid <see cref="CellStyle.FillColor"/></item>
///   <item><c>text-align:left|center|right|justify</c> → <see cref="CellStyle.HorizontalAlignment"/></item>
///   <item><c>border-{edge}:Wpx style #rrggbb</c> → the matching <see cref="CellBorder"/></item>
/// </list>
/// HTML carries only resolved/approximated styling, so the mapping is intentionally lossy: theme colors
/// arrive as concrete RGB, a double underline collapses to single, and a pattern fill arrives as a flat
/// solid color. Anything not in the subset above is ignored. Returns <c>null</c> when the style string is
/// empty or carries nothing the parser recognizes (so the cell keeps the default style).
/// </summary>
internal static class HtmlCssParser
{
    public static CellStyle? Parse(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
            return null;

        var style = new CellStyle();
        bool any = false;

        foreach (var (prop, value) in EnumerateDeclarations(css))
        {
            switch (prop)
            {
                case "font-weight":
                    if (value.Equals("bold", StringComparison.OrdinalIgnoreCase)) { style.Bold = true; any = true; }
                    break;
                case "font-style":
                    if (value.Equals("italic", StringComparison.OrdinalIgnoreCase)) { style.Italic = true; any = true; }
                    break;
                case "text-decoration":
                    if (value.Contains("underline", StringComparison.OrdinalIgnoreCase)) { style.Underline = true; any = true; }
                    break;
                case "font-family":
                    var name = Unquote(value);
                    if (name.Length > 0) { style.FontName = name; style.FontScheme = CellFontScheme.None; any = true; }
                    break;
                case "font-size":
                    if (TryParseSize(value, out var size)) { style.FontSize = size; any = true; }
                    break;
                case "color":
                    if (TryParseColor(value, out var fg)) { style.FontColor = fg; any = true; }
                    break;
                case "background-color":
                    if (TryParseColor(value, out var bg))
                    {
                        style.FillColor = bg;
                        style.FillPatternStyle = CellFillPatternStyle.Solid;
                        any = true;
                    }
                    break;
                case "text-align":
                    var align = ParseAlign(value);
                    if (align is { } a) { style.HorizontalAlignment = a; any = true; }
                    break;
                case "border-top":
                    if (TryParseBorder(value, out var bt)) { style.BorderTop = bt; any = true; }
                    break;
                case "border-right":
                    if (TryParseBorder(value, out var br)) { style.BorderRight = br; any = true; }
                    break;
                case "border-bottom":
                    if (TryParseBorder(value, out var bb)) { style.BorderBottom = bb; any = true; }
                    break;
                case "border-left":
                    if (TryParseBorder(value, out var bl)) { style.BorderLeft = bl; any = true; }
                    break;
            }
        }

        return any ? style : null;
    }

    // ---- declaration scanning ---------------------------------------------------------------------

    /// <summary>Split a CSS declaration block ("a:b;c:d") into lowercased (property, value) pairs.</summary>
    private static IEnumerable<(string Prop, string Value)> EnumerateDeclarations(string css)
    {
        foreach (var part in css.Split(';'))
        {
            var decl = part.Trim();
            if (decl.Length == 0)
                continue;
            int colon = decl.IndexOf(':');
            if (colon <= 0 || colon == decl.Length - 1)
                continue;
            var prop = decl[..colon].Trim().ToLowerInvariant();
            var value = decl[(colon + 1)..].Trim();
            yield return (prop, value);
        }
    }

    // ---- value parsers ----------------------------------------------------------------------------

    private static HorizontalAlignment? ParseAlign(string value) => value.ToLowerInvariant() switch
    {
        "left" => HorizontalAlignment.Left,
        "center" => HorizontalAlignment.Center,
        "right" => HorizontalAlignment.Right,
        "justify" => HorizontalAlignment.Justify,
        _ => null,
    };

    private static string Unquote(string value)
    {
        var v = value.Trim();
        if (v.Length >= 2 && (v[0] == '\'' || v[0] == '"') && v[^1] == v[0])
            v = v[1..^1];
        return v.Trim();
    }

    private static bool TryParseSize(string value, out double size)
    {
        size = 0;
        var v = value.Trim();
        if (v.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            v = v[..^2];
        return double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out size) && size > 0;
    }

    private static bool TryParseColor(string value, out CellColor color)
    {
        color = CellColor.Black;
        var v = value.Trim();
        if (v.Length == 7 && v[0] == '#')
        {
            if (byte.TryParse(v.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
                byte.TryParse(v.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
                byte.TryParse(v.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                color = new CellColor(r, g, b);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Parse a shorthand <c>border-{edge}</c> value of the form "&lt;width&gt; &lt;line&gt; &lt;#color&gt;",
    /// inverting the writer's width/line → BorderStyle quantization.
    /// </summary>
    private static bool TryParseBorder(string value, out CellBorder border)
    {
        border = default;
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return false;

        string? widthTok = null;
        string? lineTok = null;
        CellColor color = CellColor.Black;
        foreach (var tok in tokens)
        {
            if (tok.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                widthTok = tok;
            else if (tok.StartsWith('#') && TryParseColor(tok, out var c))
                color = c;
            else
                lineTok = tok.ToLowerInvariant();
        }

        var line = lineTok ?? "solid";
        if (line == "none" || line == "hidden")
            return false;

        int widthPx = 1;
        if (widthTok is not null && int.TryParse(widthTok.AsSpan(0, widthTok.Length - 2), out var w))
            widthPx = w;

        var bstyle = line switch
        {
            "dashed" => BorderStyle.Dashed,
            "dotted" => BorderStyle.Dotted,
            "double" => BorderStyle.Double,
            // solid: distinguish thin/medium/thick by emitted pixel width (1/2/3).
            _ => widthPx >= 3 ? BorderStyle.Thick : widthPx == 2 ? BorderStyle.Medium : BorderStyle.Thin,
        };

        border = new CellBorder(bstyle, color);
        return true;
    }
}
