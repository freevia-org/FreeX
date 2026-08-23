using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableStyleMetadataReader
{
    private static readonly HashSet<string> SupportedSemanticElementTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "wholeTable",
        "headerRow",
        "totalRow",
        "firstColumn",
        "lastColumn",
        "firstRowStripe",
        "secondRowStripe",
        "firstColumnStripe",
        "secondColumnStripe",
        "firstHeaderCell",
        "lastHeaderCell",
        "firstTotalCell",
        "lastTotalCell"
    };

    public static List<StructuredTableStyleModel> Load(
        XDocument? stylesXml,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var result = new List<StructuredTableStyleModel>();
        try
        {
            if (stylesXml?.Root is null)
                return result;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var differentialStyles = stylesXml.Root
                .Element(workbookNs + "dxfs")?
                .Elements(workbookNs + "dxf")
                .ToList()
                ?? [];
            foreach (var styleElement in stylesXml.Root
                         .Element(workbookNs + "tableStyles")?
                         .Elements(workbookNs + "tableStyle") ?? [])
            {
                var name = styleElement.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var appliesToTables = XlsxXmlAttributeReader.ReadBoolAttribute(styleElement, "table");
                var appliesToPivotTables = XlsxXmlAttributeReader.ReadBoolAttribute(styleElement, "pivot");
                // Skip pivot-only styles (pivot="1", table="0"/absent). Load table styles and
                // dual-use styles (table="1", pivot="1") — Excel itself allows a custom style to
                // apply to both; dropping it on load causes data loss on round-trip.
                if (!appliesToTables)
                    continue;

                var style = new StructuredTableStyleModel
                {
                    Name = name,
                    AppliesToTables = true,
                    AppliesToPivotTables = appliesToPivotTables,
                    NativeXml = styleElement.ToString(SaveOptions.DisableFormatting)
                };

                foreach (var element in styleElement.Elements(workbookNs + "tableStyleElement"))
                {
                    var type = element.Attribute("type")?.Value;
                    if (string.IsNullOrWhiteSpace(type))
                        continue;

                    var dxfId = XlsxXmlAttributeReader.ReadIntAttribute(element, "dxfId");
                    style.Elements.Add(new StructuredTableStyleElementModel(
                        type,
                        dxfId,
                        XlsxXmlAttributeReader.ReadIntAttribute(element, "size"),
                        SupportedSemanticElementTypes.Contains(type) &&
                        dxfId is >= 0 &&
                        dxfId.Value < differentialStyles.Count
                            ? ReadDifferentialStyleDiff(differentialStyles[dxfId.Value], workbookNs, theme, indexedColors)
                            : null));
                }

                result.Add(style);
            }
        }
        catch
        {
            return result;
        }

        return result;
    }

    private static StyleDiff? ReadDifferentialStyleDiff(
        XElement dxf,
        XNamespace workbookNs,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var font = dxf.Element(workbookNs + "font");
        CellColor? fontColor = null;
        bool? bold = null;
        bool? italic = null;
        bool? underline = null;
        string? fontName = null;
        double? fontSize = null;
        if (font is not null)
        {
            if (XlsxColorReader.TryReadCellColor(font.Element(workbookNs + "color"), theme, indexedColors, out var readFontColor))
                fontColor = readFontColor;

            if (font.Element(workbookNs + "b") is { } boldElement)
                bold = XlsxXmlAttributeReader.ReadBoolAttribute(boldElement, "val", defaultValue: true);

            if (font.Element(workbookNs + "i") is { } italicElement)
                italic = XlsxXmlAttributeReader.ReadBoolAttribute(italicElement, "val", defaultValue: true);

            if (font.Element(workbookNs + "u") is not null)
                underline = true;

            var readFontName = font.Element(workbookNs + "name")?.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(readFontName))
                fontName = readFontName;

            if (double.TryParse(
                    font.Element(workbookNs + "sz")?.Attribute("val")?.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var readFontSize))
            {
                fontSize = readFontSize;
            }
        }

        CellColor? fillColor = null;
        CellColor? fillPatternColor = null;
        CellFillPatternStyle? fillPatternStyle = null;
        var patternFill = dxf
            .Element(workbookNs + "fill")?
            .Element(workbookNs + "patternFill");
        if (patternFill is not null)
        {
            var patternStyle = XlsxFillPatternCodec.FromToken(patternFill.Attribute("patternType")?.Value);
            if (patternStyle != CellFillPatternStyle.None)
                fillPatternStyle = patternStyle;

            if (XlsxColorReader.TryReadCellColor(patternFill.Element(workbookNs + "fgColor"), theme, indexedColors, out var foregroundColor))
            {
                if (patternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
                    fillColor = foregroundColor;
                else
                    fillPatternColor = foregroundColor;
            }

            if (fillColor is null &&
                XlsxColorReader.TryReadCellColor(patternFill.Element(workbookNs + "bgColor"), theme, indexedColors, out var backgroundColor))
            {
                fillColor = backgroundColor;
            }
        }

        CellBorder? borderTop = null;
        CellBorder? borderRight = null;
        CellBorder? borderBottom = null;
        CellBorder? borderLeft = null;
        var border = dxf.Element(workbookNs + "border");
        if (border is not null)
        {
            borderTop = ReadBorderOrNull(border.Element(workbookNs + "top"), workbookNs, theme, indexedColors);
            borderRight = ReadBorderOrNull(border.Element(workbookNs + "right"), workbookNs, theme, indexedColors);
            borderBottom = ReadBorderOrNull(border.Element(workbookNs + "bottom"), workbookNs, theme, indexedColors);
            borderLeft = ReadBorderOrNull(border.Element(workbookNs + "left"), workbookNs, theme, indexedColors);
        }

        var numberFormat = dxf.Element(workbookNs + "numFmt")?.Attribute("formatCode")?.Value;
        if (string.IsNullOrWhiteSpace(numberFormat))
            numberFormat = null;

        return fontColor is null &&
               bold is null &&
               italic is null &&
               underline is null &&
               fontName is null &&
               fontSize is null &&
               fillColor is null &&
               fillPatternColor is null &&
               fillPatternStyle is null &&
               borderTop is null &&
               borderRight is null &&
               borderBottom is null &&
               borderLeft is null &&
               numberFormat is null
            ? null
            : new StyleDiff(
                Bold: bold,
                Italic: italic,
                Underline: underline,
                FontName: fontName,
                FontSize: fontSize,
                FontColor: fontColor,
                FillColor: fillColor,
                FillPatternStyle: fillPatternStyle,
                FillPatternColor: fillPatternColor,
                NumberFormat: numberFormat,
                BorderTop: borderTop,
                BorderRight: borderRight,
                BorderBottom: borderBottom,
                BorderLeft: borderLeft);
    }

    private static CellBorder? ReadBorderOrNull(
        XElement? edge,
        XNamespace workbookNs,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        if (edge is null)
            return null;

        var style = edge.Attribute("style")?.Value switch
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
            _ => BorderStyle.None
        };
        if (style == BorderStyle.None)
            return null;

        var hasColor = XlsxColorReader.TryReadCellColor(edge.Element(workbookNs + "color"), theme, indexedColors, out var color);
        return new CellBorder(style, hasColor ? color : CellColor.Black);
    }

}
