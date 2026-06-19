using System.Globalization;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// The small, hand-rolled CSS↔FreeW-formatting bridge shared by <see cref="HtmlFileAdapter"/> (and, through
/// it, <see cref="MhtmlFileAdapter"/>). The design doc (§5.7) deliberately rejects AngleSharp.Css's pre-1.0
/// cascade in favour of parsing inline <c>style="…"</c> declarations plus the hand-picked property subset the
/// FreeW model can actually represent: <c>font-weight</c>, <c>font-style</c>, <c>text-decoration</c>,
/// <c>color</c>, <c>text-align</c>, <c>font-size</c> (px/pt) and <c>font-family</c>. Everything else is
/// ignored on read (interchange, not fidelity) and never emitted on write.
///
/// <para>Both directions live here so the writer and reader agree on one mapping and the corpus tests can
/// exercise it directly.</para>
/// </summary>
internal static class HtmlCssFormatting
{
    /// <summary>Splits a <c>style="…"</c> attribute into a lower-cased property→value map (last wins).</summary>
    public static Dictionary<string, string> ParseDeclarations(string? style)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style))
            return map;

        foreach (var decl in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = decl.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = decl[..colon].Trim().ToLowerInvariant();
            var value = decl[(colon + 1)..].Trim();
            if (name.Length > 0 && value.Length > 0)
                map[name] = value;
        }

        return map;
    }

    // ---- READ: CSS declarations → model formatting --------------------------------------------------

    /// <summary>
    /// Applies the recognised inline-style declarations of one element to a run-formatting accumulator,
    /// returning the updated formatting. Unknown properties are left untouched so they inherit.
    /// </summary>
    public static RunFormatting ApplyToRun(RunFormatting current, IReadOnlyDictionary<string, string> decl)
    {
        var result = current;

        if (decl.TryGetValue("font-weight", out var weight))
        {
            var w = weight.Trim().ToLowerInvariant();
            if (w is "bold" or "bolder" || (int.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 600))
                result = result with { Bold = true };
            else if (w is "normal" or "lighter")
                result = result with { Bold = false };
        }

        if (decl.TryGetValue("font-style", out var fontStyle))
        {
            var s = fontStyle.Trim().ToLowerInvariant();
            if (s is "italic" or "oblique")
                result = result with { Italic = true };
            else if (s == "normal")
                result = result with { Italic = false };
        }

        if (decl.TryGetValue("text-decoration", out var decoration) ||
            decl.TryGetValue("text-decoration-line", out decoration))
        {
            var d = decoration.ToLowerInvariant();
            if (d.Contains("underline"))
                result = result with { Underline = true };
            if (d.Contains("line-through"))
                result = result with { Strikethrough = true };
            if (d.Contains("none"))
                result = result with { Underline = false, Strikethrough = false };
        }

        if (decl.TryGetValue("color", out var color) && TryParseColor(color, out var hex))
            result = result with { ColorHex = hex };

        if (decl.TryGetValue("background-color", out var bg) && TryParseColor(bg, out var bgHex))
            result = result with { HighlightColorHex = bgHex };

        if (decl.TryGetValue("font-size", out var size) && TryParseLengthPt(size, out var pt))
            result = result with { FontSizePt = pt };

        if (decl.TryGetValue("font-family", out var family))
        {
            var first = family.Split(',')[0].Trim().Trim('"', '\'');
            if (first.Length > 0)
                result = result with { FontFamily = first };
        }

        if (decl.TryGetValue("vertical-align", out var valign))
        {
            var v = valign.Trim().ToLowerInvariant();
            if (v == "super")
                result = result with { VerticalAlign = VerticalAlign.Superscript };
            else if (v == "sub")
                result = result with { VerticalAlign = VerticalAlign.Subscript };
        }

        return result;
    }

    /// <summary>Reads a <c>text-align</c> declaration into a <see cref="TextAlignment"/>, or null if absent/unknown.</summary>
    public static TextAlignment? ReadAlignment(IReadOnlyDictionary<string, string> decl)
    {
        if (!decl.TryGetValue("text-align", out var align))
            return null;
        return align.Trim().ToLowerInvariant() switch
        {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.Right,
            "justify" => TextAlignment.Justify,
            "left" or "start" => TextAlignment.Left,
            _ => null
        };
    }

    // ---- WRITE: model formatting → CSS declarations -------------------------------------------------

    /// <summary>
    /// Builds the inline-style declaration string for a run's formatting, or an empty string when nothing
    /// maps. Bold/italic/underline/strike are emitted as their CSS properties (the writer also wraps the run
    /// in the matching semantic tags), and colour/size/family/super-sub are emitted as the property subset.
    /// </summary>
    public static string RunStyle(RunFormatting f)
    {
        var sb = new StringBuilder();

        if (f.ColorHex is { Length: > 0 } color)
            Append(sb, "color", NormalizeHex(color));
        if (f.HighlightColorHex is { Length: > 0 } bg)
            Append(sb, "background-color", NormalizeHex(bg));
        if (f.FontSizePt is { } pt && pt > 0)
            Append(sb, "font-size", FormatPt(pt) + "pt");
        if (f.FontFamily is { Length: > 0 } family)
            Append(sb, "font-family", family.Contains(' ') ? "'" + family + "'" : family);

        return sb.ToString();
    }

    /// <summary>Builds the inline-style declaration string for paragraph alignment, or empty when left/default.</summary>
    public static string ParagraphStyle(ParagraphFormatting f)
    {
        var sb = new StringBuilder();
        var align = f.Alignment switch
        {
            TextAlignment.Center => "center",
            TextAlignment.Right => "right",
            TextAlignment.Justify => "justify",
            _ => null
        };
        if (align is not null)
            Append(sb, "text-align", align);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string name, string value)
    {
        if (sb.Length > 0)
            sb.Append(' ');
        sb.Append(name).Append(": ").Append(value).Append(';');
    }

    // ---- Length & colour parsing -------------------------------------------------------------------

    /// <summary>
    /// Parses a CSS length into points. Supports <c>pt</c> directly and <c>px</c> via the CSS 96dpi
    /// convention (1px = 0.75pt). Bare numbers are treated as px. Returns false for unsupported units
    /// (em/%/rem/etc.) so the caller leaves the size inherited.
    /// </summary>
    public static bool TryParseLengthPt(string? value, out double pt)
    {
        pt = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var v = value.Trim().ToLowerInvariant();

        string number;
        double factor;
        if (v.EndsWith("pt", StringComparison.Ordinal))
        {
            number = v[..^2];
            factor = 1.0;
        }
        else if (v.EndsWith("px", StringComparison.Ordinal))
        {
            number = v[..^2];
            factor = 0.75; // 96px/in ÷ 72pt/in
        }
        else if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            number = v;
            factor = 0.75;
        }
        else
        {
            return false;
        }

        if (!double.TryParse(number.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
            return false;
        pt = raw * factor;
        return true;
    }

    /// <summary>
    /// Parses a CSS colour into an upper-case <c>#RRGGBB</c> string. Supports <c>#rgb</c>/<c>#rrggbb</c>,
    /// <c>rgb(r,g,b)</c>/<c>rgb(r g b)</c>, and the handful of named colours Word's HTML actually emits.
    /// Returns false for unsupported forms (hsl(), rgba alpha, currentColor, …).
    /// </summary>
    public static bool TryParseColor(string? value, out string hex)
    {
        hex = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var v = value.Trim();

        if (v.StartsWith('#'))
        {
            var body = v[1..];
            if (body.Length == 3 && body.All(Uri.IsHexDigit))
            {
                hex = $"#{body[0]}{body[0]}{body[1]}{body[1]}{body[2]}{body[2]}".ToUpperInvariant();
                return true;
            }
            if (body.Length == 6 && body.All(Uri.IsHexDigit))
            {
                hex = "#" + body.ToUpperInvariant();
                return true;
            }
            return false;
        }

        if (v.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && v.EndsWith(')'))
        {
            var inner = v[4..^1];
            var parts = inner.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 3 &&
                byte.TryParse(parts[0], out var r) &&
                byte.TryParse(parts[1], out var g) &&
                byte.TryParse(parts[2], out var b))
            {
                hex = $"#{r:X2}{g:X2}{b:X2}";
                return true;
            }
            return false;
        }

        if (NamedColors.TryGetValue(v, out var named))
        {
            hex = named;
            return true;
        }

        return false;
    }

    private static string NormalizeHex(string color) =>
        TryParseColor(color, out var hex) ? hex : (color.StartsWith('#') ? color : "#" + color);

    private static string FormatPt(double pt) =>
        pt.ToString("0.##", CultureInfo.InvariantCulture);

    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "#000000",
        ["white"] = "#FFFFFF",
        ["red"] = "#FF0000",
        ["green"] = "#008000",
        ["lime"] = "#00FF00",
        ["blue"] = "#0000FF",
        ["yellow"] = "#FFFF00",
        ["cyan"] = "#00FFFF",
        ["aqua"] = "#00FFFF",
        ["magenta"] = "#FF00FF",
        ["fuchsia"] = "#FF00FF",
        ["gray"] = "#808080",
        ["grey"] = "#808080",
        ["silver"] = "#C0C0C0",
        ["maroon"] = "#800000",
        ["olive"] = "#808000",
        ["navy"] = "#000080",
        ["purple"] = "#800080",
        ["teal"] = "#008080",
        ["orange"] = "#FFA500",
    };
}
