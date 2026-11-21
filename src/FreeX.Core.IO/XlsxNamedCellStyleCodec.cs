using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Reads and writes user-defined named cell styles (<c>styles.xml</c> <c>cellStyleXfs</c> /
/// <c>cellStyles</c>) so <see cref="Workbook.CustomCellStyles"/> round-trips through .xlsx and
/// Excel-authored custom styles surface in the model. Built-in styles (those carrying a
/// <c>builtinId</c>, e.g. Normal/Good/Bad/Neutral) are never imported or re-emitted here: ClosedXML
/// plus <see cref="XlsxStylesheetMetadataPreserver"/> already carry them, and treating them as
/// custom would duplicate the gallery's built-ins.
/// </summary>
internal static class XlsxNamedCellStyleCodec
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ---------------------------------------------------------------- read

    public static List<CustomCellStyle> Read(
        XDocument? stylesXml,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var root = stylesXml?.Root;
        return root is null ? [] : ReadRoot(root, theme, indexedColors);
    }

    private static List<CustomCellStyle> ReadRoot(
        XElement root,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var result = new List<CustomCellStyle>();

        var cellStyles = root.Element(Ns + "cellStyles");
        var styleXfs = root.Element(Ns + "cellStyleXfs")?.Elements(Ns + "xf").ToList();
        if (cellStyles is null || styleXfs is null)
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cellStyle in cellStyles.Elements(Ns + "cellStyle"))
        {
            if (cellStyle.Attribute("builtinId") is not null)
                continue;

            var name = cellStyle.Attribute("name")?.Value?.Trim();
            // Non-Excel producers sometimes omit builtinId on the built-in entries (e.g. a bare
            // <cellStyle name="Normal" xfId="0"/>); the name alone identifies those.
            if (string.IsNullOrEmpty(name) || IsBuiltInStyleName(name) || name.Length > CustomCellStyle.MaxNameLength || !seen.Add(name))
                continue;

            if (!TryParseInt(cellStyle.Attribute("xfId")?.Value, out var xfId) || xfId < 0 || xfId >= styleXfs.Count)
                continue;

            result.Add(ReadStyle(root, styleXfs[xfId], name, theme, indexedColors));
        }

        return result;
    }

    private static readonly HashSet<string> BuiltInStyleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Normal", "Good", "Bad", "Neutral", "Calculation", "Check Cell", "Explanatory Text", "Input",
        "Linked Cell", "Note", "Output", "Warning Text", "Heading 1", "Heading 2", "Heading 3", "Heading 4",
        "Title", "Total", "Comma", "Comma [0]", "Currency", "Currency [0]", "Percent", "Hyperlink",
        "Followed Hyperlink", "Accent1", "Accent2", "Accent3", "Accent4", "Accent5", "Accent6",
        "20% - Accent1", "20% - Accent2", "20% - Accent3", "20% - Accent4", "20% - Accent5", "20% - Accent6",
        "40% - Accent1", "40% - Accent2", "40% - Accent3", "40% - Accent4", "40% - Accent5", "40% - Accent6",
        "60% - Accent1", "60% - Accent2", "60% - Accent3", "60% - Accent4", "60% - Accent5", "60% - Accent6",
    };

    /// <summary>True for Excel's built-in cell style names, which the gallery already owns.</summary>
    internal static bool IsBuiltInStyleName(string name) => BuiltInStyleNames.Contains(name);

    private static CustomCellStyle ReadStyle(
        XElement root,
        XElement xf,
        string name,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var alignment = xf.Element(Ns + "alignment");
        var protection = xf.Element(Ns + "protection");
        var parts = CustomCellStyleParts.None;
        if (ReadApply(xf, "applyNumberFormat", defaultValue: true)) parts |= CustomCellStyleParts.NumberFormat;
        if (ReadApply(xf, "applyFont", defaultValue: true)) parts |= CustomCellStyleParts.Font;
        if (ReadApply(xf, "applyFill", defaultValue: true)) parts |= CustomCellStyleParts.Fill;
        if (ReadApply(xf, "applyBorder", defaultValue: true)) parts |= CustomCellStyleParts.Border;
        if (ReadApply(xf, "applyAlignment", defaultValue: alignment is not null)) parts |= CustomCellStyleParts.Alignment;
        if (ReadApply(xf, "applyProtection", defaultValue: protection is not null)) parts |= CustomCellStyleParts.Protection;

        var style = new CellStyle();
        if (parts.HasFlag(CustomCellStyleParts.NumberFormat))
            style.NumberFormat = ReadNumberFormat(root, xf);
        if (parts.HasFlag(CustomCellStyleParts.Font) &&
            ElementAt(root, "fonts", "font", xf.Attribute("fontId")?.Value) is { } font)
            ReadFont(font, style, theme, indexedColors);
        if (parts.HasFlag(CustomCellStyleParts.Fill) &&
            ElementAt(root, "fills", "fill", xf.Attribute("fillId")?.Value) is { } fill)
            ReadFill(fill, style, theme, indexedColors);
        if (parts.HasFlag(CustomCellStyleParts.Border) &&
            ElementAt(root, "borders", "border", xf.Attribute("borderId")?.Value) is { } border)
            ReadBorder(border, style, theme, indexedColors);
        if (parts.HasFlag(CustomCellStyleParts.Alignment) && alignment is not null)
            ReadAlignment(alignment, style);
        if (parts.HasFlag(CustomCellStyleParts.Protection) && protection is not null)
        {
            style.Locked = ReadBool(protection.Attribute("locked")?.Value, true);
            style.Hidden = ReadBool(protection.Attribute("hidden")?.Value, false);
        }

        return new CustomCellStyle(name, style, parts);
    }

    private static bool ReadApply(XElement xf, string attribute, bool defaultValue) =>
        xf.Attribute(attribute) is { } attr ? ReadBool(attr.Value, defaultValue) : defaultValue;

    private static bool ReadBool(string? value, bool defaultValue) => value switch
    {
        "1" or "true" => true,
        "0" or "false" => false,
        _ => defaultValue,
    };

    private static XElement? ElementAt(XElement root, string collection, string item, string? idText)
    {
        if (!TryParseInt(idText, out var id) || id < 0)
            return null;
        return root.Element(Ns + collection)?.Elements(Ns + item).ElementAtOrDefault(id);
    }

    private static bool TryParseInt(string? text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static string ReadNumberFormat(XElement root, XElement xf)
    {
        if (!TryParseInt(xf.Attribute("numFmtId")?.Value, out var id))
            return "General";

        var custom = root.Element(Ns + "numFmts")?.Elements(Ns + "numFmt")
            .FirstOrDefault(e => TryParseInt(e.Attribute("numFmtId")?.Value, out var candidate) && candidate == id)
            ?.Attribute("formatCode")?.Value;
        if (!string.IsNullOrEmpty(custom))
            return custom;

        return BuiltInNumberFormatCatalog.TryResolveFormatCode(id, out var code) ? code : "General";
    }

    private static void ReadFont(XElement font, CellStyle style, WorkbookTheme theme, WorkbookIndexedColorPalette indexedColors)
    {
        style.Bold = IsOn(font.Element(Ns + "b"));
        style.Italic = IsOn(font.Element(Ns + "i"));
        style.Strikethrough = IsOn(font.Element(Ns + "strike"));
        if (font.Element(Ns + "u") is { } underline)
        {
            var kind = underline.Attribute("val")?.Value ?? "single";
            style.Underline = kind != "none";
            style.DoubleUnderline = kind is "double" or "doubleAccounting";
        }

        switch (font.Element(Ns + "vertAlign")?.Attribute("val")?.Value)
        {
            case "superscript": style.Superscript = true; break;
            case "subscript": style.Subscript = true; break;
        }

        if (double.TryParse(font.Element(Ns + "sz")?.Attribute("val")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) &&
            size > 0 && double.IsFinite(size))
            style.FontSize = size;
        if (font.Element(Ns + "name")?.Attribute("val")?.Value is { Length: > 0 } fontName)
            style.FontName = fontName;
        if (TryParseInt(font.Element(Ns + "family")?.Attribute("val")?.Value, out var family))
            style.FontFamily = family;
        if (TryParseInt(font.Element(Ns + "charset")?.Attribute("val")?.Value, out var charset))
            style.Charset = charset;
        style.FontScheme = font.Element(Ns + "scheme")?.Attribute("val")?.Value switch
        {
            "minor" => CellFontScheme.Minor,
            "major" => CellFontScheme.Major,
            _ => CellFontScheme.None,
        };

        if (XlsxColorReader.TryReadCellColorWithThemeReference(font.Element(Ns + "color"), theme, indexedColors, out var color, out var themeRef))
        {
            style.FontColor = color;
            style.FontThemeColor = themeRef;
        }
    }

    private static bool IsOn(XElement? element) =>
        element is not null && ReadBool(element.Attribute("val")?.Value, true);

    private static void ReadFill(XElement fill, CellStyle style, WorkbookTheme theme, WorkbookIndexedColorPalette indexedColors)
    {
        var pattern = fill.Element(Ns + "patternFill");
        if (pattern is null)
            return; // gradient fills are not modelled for named styles

        var patternStyle = ParsePatternType(pattern.Attribute("patternType")?.Value);
        style.FillPatternStyle = patternStyle;
        if (patternStyle == CellFillPatternStyle.None)
            return;

        var fg = pattern.Element(Ns + "fgColor");
        var bg = pattern.Element(Ns + "bgColor");
        if (patternStyle == CellFillPatternStyle.Solid)
        {
            if (XlsxColorReader.TryReadCellColorWithThemeReference(fg, theme, indexedColors, out var solid, out var solidTheme))
            {
                style.FillColor = solid;
                style.FillThemeColor = solidTheme;
            }

            return;
        }

        if (XlsxColorReader.TryReadCellColorWithThemeReference(bg, theme, indexedColors, out var back, out var backTheme))
        {
            style.FillColor = back;
            style.FillThemeColor = backTheme;
        }

        if (XlsxColorReader.TryReadCellColorWithThemeReference(fg, theme, indexedColors, out var front, out var frontTheme))
        {
            style.FillPatternColor = front;
            style.FillPatternThemeColor = frontTheme;
        }
    }

    private static void ReadBorder(XElement border, CellStyle style, WorkbookTheme theme, WorkbookIndexedColorPalette indexedColors)
    {
        style.BorderTop = ReadEdge(border.Element(Ns + "top"), theme, indexedColors);
        style.BorderRight = ReadEdge(border.Element(Ns + "right") ?? border.Element(Ns + "end"), theme, indexedColors);
        style.BorderBottom = ReadEdge(border.Element(Ns + "bottom"), theme, indexedColors);
        style.BorderLeft = ReadEdge(border.Element(Ns + "left") ?? border.Element(Ns + "start"), theme, indexedColors);
        var diagonal = ReadEdge(border.Element(Ns + "diagonal"), theme, indexedColors);
        if (ReadBool(border.Attribute("diagonalDown")?.Value, false))
            style.BorderDiagonalDown = diagonal;
        if (ReadBool(border.Attribute("diagonalUp")?.Value, false))
            style.BorderDiagonalUp = diagonal;
    }

    private static CellBorder ReadEdge(XElement? edge, WorkbookTheme theme, WorkbookIndexedColorPalette indexedColors)
    {
        var borderStyle = ParseBorderStyle(edge?.Attribute("style")?.Value);
        if (borderStyle == BorderStyle.None)
            return default;

        if (XlsxColorReader.TryReadCellColorWithThemeReference(edge!.Element(Ns + "color"), theme, indexedColors, out var color, out var themeRef))
            return new CellBorder(borderStyle, color, themeRef);
        return new CellBorder(borderStyle, CellColor.Black);
    }

    private static void ReadAlignment(XElement alignment, CellStyle style)
    {
        style.HorizontalAlignment = alignment.Attribute("horizontal")?.Value switch
        {
            "left" => HorizontalAlignment.Left,
            "center" or "centerContinuous" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            "justify" => HorizontalAlignment.Justify,
            "distributed" => HorizontalAlignment.Distributed,
            "fill" => HorizontalAlignment.Fill,
            _ => HorizontalAlignment.General,
        };
        style.VerticalAlignment = alignment.Attribute("vertical")?.Value switch
        {
            "top" => VerticalAlignment.Top,
            "center" => VerticalAlignment.Center,
            "justify" => VerticalAlignment.Justify,
            "distributed" => VerticalAlignment.Distributed,
            _ => VerticalAlignment.Bottom,
        };
        style.WrapText = ReadBool(alignment.Attribute("wrapText")?.Value, false);
        style.ShrinkToFit = ReadBool(alignment.Attribute("shrinkToFit")?.Value, false);
        if (TryParseInt(alignment.Attribute("indent")?.Value, out var indent))
            style.IndentLevel = Math.Clamp(indent, 0, 15);
        if (TryParseInt(alignment.Attribute("textRotation")?.Value, out var rotation))
        {
            // OOXML encodes -90..-1 as 91..180.
            style.TextRotation = rotation is > 90 and <= 180 ? 90 - rotation : rotation;
            if (style.TextRotation != 255 && style.TextRotation is < -90 or > 90)
                style.TextRotation = 0;
        }

        style.ReadingOrder = alignment.Attribute("readingOrder")?.Value switch
        {
            "1" => CellReadingOrder.LeftToRight,
            "2" => CellReadingOrder.RightToLeft,
            _ => CellReadingOrder.Context,
        };
    }

    private static CellFillPatternStyle ParsePatternType(string? value) => value switch
    {
        "solid" => CellFillPatternStyle.Solid,
        "gray0625" => CellFillPatternStyle.Gray0625,
        "gray125" => CellFillPatternStyle.Gray125,
        "lightGray" => CellFillPatternStyle.LightGray,
        "mediumGray" => CellFillPatternStyle.MediumGray,
        "darkGray" => CellFillPatternStyle.DarkGray,
        "lightHorizontal" => CellFillPatternStyle.LightHorizontal,
        "lightVertical" => CellFillPatternStyle.LightVertical,
        "lightDown" => CellFillPatternStyle.LightDown,
        "lightUp" => CellFillPatternStyle.LightUp,
        "lightGrid" => CellFillPatternStyle.LightGrid,
        "lightTrellis" => CellFillPatternStyle.LightTrellis,
        "darkHorizontal" => CellFillPatternStyle.DarkHorizontal,
        "darkVertical" => CellFillPatternStyle.DarkVertical,
        "darkDown" => CellFillPatternStyle.DarkDown,
        "darkUp" => CellFillPatternStyle.DarkUp,
        "darkGrid" => CellFillPatternStyle.DarkGrid,
        "darkTrellis" => CellFillPatternStyle.DarkTrellis,
        _ => CellFillPatternStyle.None,
    };

    private static string ToPatternType(CellFillPatternStyle style) => style switch
    {
        CellFillPatternStyle.Solid => "solid",
        CellFillPatternStyle.Gray0625 => "gray0625",
        CellFillPatternStyle.Gray125 => "gray125",
        CellFillPatternStyle.LightGray => "lightGray",
        CellFillPatternStyle.MediumGray => "mediumGray",
        CellFillPatternStyle.DarkGray => "darkGray",
        CellFillPatternStyle.LightHorizontal => "lightHorizontal",
        CellFillPatternStyle.LightVertical => "lightVertical",
        CellFillPatternStyle.LightDown => "lightDown",
        CellFillPatternStyle.LightUp => "lightUp",
        CellFillPatternStyle.LightGrid => "lightGrid",
        CellFillPatternStyle.LightTrellis => "lightTrellis",
        CellFillPatternStyle.DarkHorizontal => "darkHorizontal",
        CellFillPatternStyle.DarkVertical => "darkVertical",
        CellFillPatternStyle.DarkDown => "darkDown",
        CellFillPatternStyle.DarkUp => "darkUp",
        CellFillPatternStyle.DarkGrid => "darkGrid",
        CellFillPatternStyle.DarkTrellis => "darkTrellis",
        _ => "none",
    };

    private static BorderStyle ParseBorderStyle(string? value) => value switch
    {
        "thin" => BorderStyle.Thin,
        "medium" => BorderStyle.Medium,
        "thick" => BorderStyle.Thick,
        "dashed" => BorderStyle.Dashed,
        "dotted" => BorderStyle.Dotted,
        "double" => BorderStyle.Double,
        "hair" => BorderStyle.Hair,
        "slantDashDot" => BorderStyle.SlantDashDot,
        "mediumDashed" => BorderStyle.MediumDashed,
        "dashDot" => BorderStyle.DashDot,
        "mediumDashDot" => BorderStyle.MediumDashDot,
        "dashDotDot" => BorderStyle.DashDotDot,
        "mediumDashDotDot" => BorderStyle.MediumDashDotDot,
        _ => BorderStyle.None,
    };

    private static string ToBorderStyleName(BorderStyle style) => style switch
    {
        BorderStyle.Thin => "thin",
        BorderStyle.Medium => "medium",
        BorderStyle.Thick => "thick",
        BorderStyle.Dashed => "dashed",
        BorderStyle.Dotted => "dotted",
        BorderStyle.Double => "double",
        BorderStyle.Hair => "hair",
        BorderStyle.SlantDashDot => "slantDashDot",
        BorderStyle.MediumDashed => "mediumDashed",
        BorderStyle.DashDot => "dashDot",
        BorderStyle.MediumDashDot => "mediumDashDot",
        BorderStyle.DashDotDot => "dashDotDot",
        BorderStyle.MediumDashDotDot => "mediumDashDotDot",
        _ => "none",
    };

    // ---------------------------------------------------------------- write

    /// <summary>
    /// Masks <paramref name="style"/> down to the groups in <paramref name="parts"/>; every other
    /// attribute is reset to its default. Equal masked styles describe the same named style.
    /// </summary>
    internal static CellStyle Mask(CellStyle style, CustomCellStyleParts parts)
    {
        var result = new CellStyle();
        if (parts.HasFlag(CustomCellStyleParts.NumberFormat))
            result.NumberFormat = style.NumberFormat;
        if (parts.HasFlag(CustomCellStyleParts.Alignment))
        {
            result.HorizontalAlignment = style.HorizontalAlignment;
            result.VerticalAlignment = style.VerticalAlignment;
            result.ReadingOrder = style.ReadingOrder;
            result.WrapText = style.WrapText;
            result.ShrinkToFit = style.ShrinkToFit;
            result.IndentLevel = style.IndentLevel;
            result.TextRotation = style.TextRotation;
        }

        if (parts.HasFlag(CustomCellStyleParts.Font))
        {
            result.FontName = style.FontName;
            result.FontSize = style.FontSize;
            result.Bold = style.Bold;
            result.Italic = style.Italic;
            result.Underline = style.Underline;
            result.DoubleUnderline = style.DoubleUnderline;
            result.Strikethrough = style.Strikethrough;
            result.Superscript = style.Superscript;
            result.Subscript = style.Subscript;
            result.FontColor = style.FontColor;
            result.FontThemeColor = style.FontThemeColor;
            result.FontScheme = style.FontScheme;
            result.Charset = style.Charset;
            result.FontFamily = style.FontFamily;
        }

        if (parts.HasFlag(CustomCellStyleParts.Border))
        {
            result.BorderTop = style.BorderTop;
            result.BorderRight = style.BorderRight;
            result.BorderBottom = style.BorderBottom;
            result.BorderLeft = style.BorderLeft;
            result.BorderDiagonalDown = style.BorderDiagonalDown;
            result.BorderDiagonalUp = style.BorderDiagonalUp;
        }

        if (parts.HasFlag(CustomCellStyleParts.Fill))
        {
            result.FillColor = style.FillColor;
            result.FillThemeColor = style.FillThemeColor;
            result.FillPatternStyle = style.FillPatternStyle;
            result.FillPatternColor = style.FillPatternColor;
            result.FillPatternThemeColor = style.FillPatternThemeColor;
        }

        if (parts.HasFlag(CustomCellStyleParts.Protection))
        {
            result.Locked = style.Locked;
            result.Hidden = style.Hidden;
        }

        return result;
    }

    public static void Save(Stream packageStream, Workbook workbook)
    {
        if (workbook.CustomCellStyles.Count == 0)
            return;

        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        if (archive.GetEntry("xl/styles.xml") is not { } stylesEntry)
            return;

        var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
        if (Apply(stylesXml, workbook))
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
    }

    internal static bool Apply(XDocument stylesXml, Workbook workbook)
    {
        var root = stylesXml.Root;
        if (root is null)
            return false;

        var cellStyles = root.Element(Ns + "cellStyles");
        var styleXfs = root.Element(Ns + "cellStyleXfs");
        if (cellStyles is null || styleXfs is null)
            return false;

        var existing = ReadExistingByName(root, workbook);
        var changed = false;
        foreach (var custom in workbook.CustomCellStyles)
        {
            var name = custom.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > CustomCellStyle.MaxNameLength)
                continue;
            if (IsBuiltInStyleName(name))
                continue; // never shadow a built-in with a same-named custom definition

            var wanted = Mask(custom.Style, custom.Parts);
            var cellStyle = cellStyles.Elements(Ns + "cellStyle")
                .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
            if (cellStyle is not null)
            {
                // Never overwrite a built-in (Normal, Good, ...) with a same-named custom definition.
                if (cellStyle.Attribute("builtinId") is not null)
                    continue;
                if (existing.TryGetValue(name, out var current) &&
                    current.Parts == custom.Parts &&
                    current.Style.Equals(wanted))
                    continue;
            }

            var xf = BuildStyleXf(root, wanted, custom.Parts);
            if (cellStyle is not null &&
                TryParseInt(cellStyle.Attribute("xfId")?.Value, out var existingXfId) &&
                styleXfs.Elements(Ns + "xf").ElementAtOrDefault(existingXfId) is { } existingXf)
            {
                existingXf.ReplaceWith(xf);
            }
            else
            {
                styleXfs.Add(xf);
                var newId = styleXfs.Elements(Ns + "xf").Count() - 1;
                var element = new XElement(
                    Ns + "cellStyle",
                    new XAttribute("name", name),
                    new XAttribute("xfId", newId.ToString(CultureInfo.InvariantCulture)));
                if (cellStyle is not null)
                    cellStyle.ReplaceWith(element);
                else
                    cellStyles.Add(element);
            }

            changed = true;
        }

        if (!changed)
            return false;

        foreach (var (collection, item) in new[] { ("fonts", "font"), ("fills", "fill"), ("borders", "border"), ("numFmts", "numFmt"), ("cellStyleXfs", "xf"), ("cellStyles", "cellStyle") })
        {
            var element = root.Element(Ns + collection);
            element?.SetAttributeValue("count", element.Elements(Ns + item).Count().ToString(CultureInfo.InvariantCulture));
        }

        return true;
    }

    private static Dictionary<string, CustomCellStyle> ReadExistingByName(XElement root, Workbook workbook)
    {
        var map = new Dictionary<string, CustomCellStyle>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in ReadRoot(root, workbook.Theme, workbook.IndexedColors))
            map[style.Name] = style with { Style = Mask(style.Style, style.Parts) };
        return map;
    }

    private static XElement BuildStyleXf(XElement root, CellStyle style, CustomCellStyleParts parts)
    {
        var fontId = parts.HasFlag(CustomCellStyleParts.Font) ? AppendItem(root, "fonts", BuildFont(style)) : 0;
        var fillId = parts.HasFlag(CustomCellStyleParts.Fill) ? AppendItem(root, "fills", BuildFill(style)) : 0;
        var borderId = parts.HasFlag(CustomCellStyleParts.Border) ? AppendItem(root, "borders", BuildBorder(style)) : 0;
        var numFmtId = parts.HasFlag(CustomCellStyleParts.NumberFormat) ? ResolveNumberFormatId(root, style.NumberFormat) : 0;

        var xf = new XElement(
            Ns + "xf",
            new XAttribute("numFmtId", Int(numFmtId)),
            new XAttribute("fontId", Int(fontId)),
            new XAttribute("fillId", Int(fillId)),
            new XAttribute("borderId", Int(borderId)),
            new XAttribute("applyNumberFormat", Flag(parts, CustomCellStyleParts.NumberFormat)),
            new XAttribute("applyFont", Flag(parts, CustomCellStyleParts.Font)),
            new XAttribute("applyFill", Flag(parts, CustomCellStyleParts.Fill)),
            new XAttribute("applyBorder", Flag(parts, CustomCellStyleParts.Border)),
            new XAttribute("applyAlignment", Flag(parts, CustomCellStyleParts.Alignment)),
            new XAttribute("applyProtection", Flag(parts, CustomCellStyleParts.Protection)));

        if (parts.HasFlag(CustomCellStyleParts.Alignment) && BuildAlignment(style) is { } alignment)
            xf.Add(alignment);
        if (parts.HasFlag(CustomCellStyleParts.Protection) && (!style.Locked || style.Hidden))
        {
            var protection = new XElement(Ns + "protection");
            if (!style.Locked)
                protection.SetAttributeValue("locked", "0");
            if (style.Hidden)
                protection.SetAttributeValue("hidden", "1");
            xf.Add(protection);
        }

        return xf;
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Flag(CustomCellStyleParts parts, CustomCellStyleParts flag) => parts.HasFlag(flag) ? "1" : "0";

    private static int AppendItem(XElement root, string collection, XElement item)
    {
        var container = root.Element(Ns + collection);
        if (container is null)
        {
            container = new XElement(Ns + collection);
            // CT_Stylesheet order: numFmts, fonts, fills, borders, cellStyleXfs, ... -- fonts/fills/borders
            // always exist in ClosedXML output; keep this defensive path schema-ordered anyway.
            var next = root.Element(Ns + "cellStyleXfs");
            if (next is not null)
                next.AddBeforeSelf(container);
            else
                root.AddFirst(container);
        }

        container.Add(item);
        return container.Elements().Count() - 1;
    }

    private static int ResolveNumberFormatId(XElement root, string formatCode)
    {
        if (string.IsNullOrEmpty(formatCode) || string.Equals(formatCode, "General", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (BuiltInNumberFormatCatalog.TryResolveNumberFormatIdForCode(formatCode, out var builtin) && builtin is { } builtinId)
            return builtinId;

        var numFmts = root.Element(Ns + "numFmts");
        if (numFmts is null)
        {
            numFmts = new XElement(Ns + "numFmts");
            root.AddFirst(numFmts);
        }

        var maxId = 163;
        foreach (var numFmt in numFmts.Elements(Ns + "numFmt"))
        {
            if (!TryParseInt(numFmt.Attribute("numFmtId")?.Value, out var id))
                continue;
            if (string.Equals(numFmt.Attribute("formatCode")?.Value, formatCode, StringComparison.Ordinal))
                return id;
            maxId = Math.Max(maxId, id);
        }

        var newId = maxId + 1;
        numFmts.Add(new XElement(
            Ns + "numFmt",
            new XAttribute("numFmtId", Int(newId)),
            new XAttribute("formatCode", formatCode)));
        return newId;
    }

    private static XElement BuildFont(CellStyle style)
    {
        var font = new XElement(Ns + "font");
        if (style.Bold) font.Add(new XElement(Ns + "b"));
        if (style.Italic) font.Add(new XElement(Ns + "i"));
        if (style.Strikethrough) font.Add(new XElement(Ns + "strike"));
        if (style.DoubleUnderline)
            font.Add(new XElement(Ns + "u", new XAttribute("val", "double")));
        else if (style.Underline)
            font.Add(new XElement(Ns + "u"));
        if (style.Superscript)
            font.Add(new XElement(Ns + "vertAlign", new XAttribute("val", "superscript")));
        else if (style.Subscript)
            font.Add(new XElement(Ns + "vertAlign", new XAttribute("val", "subscript")));
        font.Add(new XElement(Ns + "sz", new XAttribute("val", style.FontSize.ToString("0.##", CultureInfo.InvariantCulture))));
        font.Add(BuildColor("color", style.FontColor, style.FontThemeColor));
        font.Add(new XElement(Ns + "name", new XAttribute("val", style.FontName)));
        if (style.FontFamily != 2)
            font.Add(new XElement(Ns + "family", new XAttribute("val", Int(style.FontFamily))));
        if (style.Charset != 1)
            font.Add(new XElement(Ns + "charset", new XAttribute("val", Int(style.Charset))));
        if (style.FontScheme != CellFontScheme.None)
            font.Add(new XElement(Ns + "scheme", new XAttribute("val", style.FontScheme == CellFontScheme.Major ? "major" : "minor")));
        return font;
    }

    private static XElement BuildColor(string elementName, CellColor color, WorkbookThemeColorReference? themeColor)
    {
        if (themeColor is not { } link)
            return new XElement(Ns + elementName, new XAttribute("rgb", $"FF{color.R:X2}{color.G:X2}{color.B:X2}"));

        var element = new XElement(
            Ns + elementName,
            new XAttribute("theme", Int(XlsxColorReader.ThemeColorIndex(link.Slot))));
        if (Math.Abs(link.Tint) >= 0.000001)
            element.SetAttributeValue("tint", link.Tint.ToString("G17", CultureInfo.InvariantCulture));
        return element;
    }

    private static XElement BuildFill(CellStyle style)
    {
        var pattern = style.FillPatternStyle;
        if (pattern == CellFillPatternStyle.None && (style.FillColor is not null || style.FillThemeColor is not null))
            pattern = CellFillPatternStyle.Solid;

        var patternFill = new XElement(Ns + "patternFill", new XAttribute("patternType", ToPatternType(pattern)));
        if (pattern == CellFillPatternStyle.Solid)
        {
            if (style.FillThemeColor is not null || style.FillColor is not null)
                patternFill.Add(BuildColor("fgColor", style.FillColor ?? CellColor.White, style.FillThemeColor));
            patternFill.Add(new XElement(Ns + "bgColor", new XAttribute("indexed", "64")));
        }
        else if (pattern != CellFillPatternStyle.None)
        {
            if (style.FillPatternThemeColor is not null || style.FillPatternColor is not null)
                patternFill.Add(BuildColor("fgColor", style.FillPatternColor ?? CellColor.Black, style.FillPatternThemeColor));
            if (style.FillThemeColor is not null || style.FillColor is not null)
                patternFill.Add(BuildColor("bgColor", style.FillColor ?? CellColor.White, style.FillThemeColor));
        }

        return new XElement(Ns + "fill", patternFill);
    }

    private static XElement BuildBorder(CellStyle style)
    {
        var diagonalDown = style.BorderDiagonalDown.Style != BorderStyle.None;
        var diagonalUp = style.BorderDiagonalUp.Style != BorderStyle.None;
        var border = new XElement(Ns + "border");
        if (diagonalDown) border.SetAttributeValue("diagonalDown", "1");
        if (diagonalUp) border.SetAttributeValue("diagonalUp", "1");
        border.Add(
            BuildEdge("left", style.BorderLeft),
            BuildEdge("right", style.BorderRight),
            BuildEdge("top", style.BorderTop),
            BuildEdge("bottom", style.BorderBottom),
            BuildEdge("diagonal", diagonalDown ? style.BorderDiagonalDown : style.BorderDiagonalUp));
        return border;
    }

    private static XElement BuildEdge(string name, CellBorder edge)
    {
        var element = new XElement(Ns + name);
        if (edge.Style == BorderStyle.None)
            return element;

        element.SetAttributeValue("style", ToBorderStyleName(edge.Style));
        element.Add(BuildColor("color", edge.Color, edge.ThemeColor));
        return element;
    }

    private static XElement? BuildAlignment(CellStyle style)
    {
        var alignment = new XElement(Ns + "alignment");
        var horizontal = style.HorizontalAlignment switch
        {
            HorizontalAlignment.Left => "left",
            HorizontalAlignment.Center => "center",
            HorizontalAlignment.Right => "right",
            HorizontalAlignment.Justify => "justify",
            HorizontalAlignment.Distributed => "distributed",
            HorizontalAlignment.Fill => "fill",
            _ => null,
        };
        if (horizontal is not null) alignment.SetAttributeValue("horizontal", horizontal);
        var vertical = style.VerticalAlignment switch
        {
            VerticalAlignment.Top => "top",
            VerticalAlignment.Center => "center",
            VerticalAlignment.Justify => "justify",
            VerticalAlignment.Distributed => "distributed",
            _ => null,
        };
        if (vertical is not null) alignment.SetAttributeValue("vertical", vertical);
        if (style.TextRotation != 0)
            alignment.SetAttributeValue("textRotation", Int(style.TextRotation is < 0 and >= -90 ? 90 - style.TextRotation : style.TextRotation));
        if (style.WrapText) alignment.SetAttributeValue("wrapText", "1");
        if (style.ShrinkToFit) alignment.SetAttributeValue("shrinkToFit", "1");
        if (style.IndentLevel > 0) alignment.SetAttributeValue("indent", Int(style.IndentLevel));
        if (style.ReadingOrder != CellReadingOrder.Context)
            alignment.SetAttributeValue("readingOrder", style.ReadingOrder == CellReadingOrder.LeftToRight ? "1" : "2");
        return alignment.HasAttributes ? alignment : null;
    }
}
