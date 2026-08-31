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
        // freex-table-style-theme-color-1: these carry the <color theme="n"/> LINK alongside the RGB it
        // resolves to today, so a themed table style keeps following the workbook theme and round-trips
        // its link on save instead of being permanently baked at load. Mirrors the conditional-
        // formatting dxf reader (R120-cf-theme-color-1), which this path had diverged from.
        WorkbookThemeColorReference? fontThemeColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        WorkbookThemeColorReference? fillPatternThemeColor = null;

        var font = dxf.Element(workbookNs + "font");
        CellColor? fontColor = null;
        bool? bold = null;
        bool? italic = null;
        bool? underline = null;
        string? fontName = null;
        double? fontSize = null;
        if (font is not null)
        {
            if (XlsxColorReader.TryReadCellColorWithThemeReference(
                    font.Element(workbookNs + "color"), theme, indexedColors, out var readFontColor, out var readFontThemeColor))
            {
                fontColor = readFontColor;
                fontThemeColor = readFontThemeColor;
            }

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

            if (XlsxColorReader.TryReadCellColorWithThemeReference(
                    patternFill.Element(workbookNs + "fgColor"),
                    theme,
                    indexedColors,
                    out var foregroundColor,
                    out var foregroundThemeColor))
            {
                if (patternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
                {
                    fillColor = foregroundColor;
                    fillThemeColor = foregroundThemeColor;
                }
                else
                {
                    fillPatternColor = foregroundColor;
                    fillPatternThemeColor = foregroundThemeColor;
                }
            }

            if (fillColor is null &&
                XlsxColorReader.TryReadCellColorWithThemeReference(
                    patternFill.Element(workbookNs + "bgColor"),
                    theme,
                    indexedColors,
                    out var backgroundColor,
                    out var backgroundThemeColor))
            {
                fillColor = backgroundColor;
                fillThemeColor = backgroundThemeColor;
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
                FontThemeColor: fontThemeColor,
                FillColor: fillColor,
                FillThemeColor: fillThemeColor,
                FillPatternStyle: fillPatternStyle,
                FillPatternColor: fillPatternColor,
                FillPatternThemeColor: fillPatternThemeColor,
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

        var style = XlsxBorderStyleCodec.Decode(edge.Attribute("style")?.Value);
        if (style == BorderStyle.None)
            return null;

        // freex-table-style-theme-color-1: capture the <color theme="n" tint="t"/> LINK, not just the
        // RGB it happens to resolve to today, so a themed table-style edge re-resolves against a later
        // theme swap and round-trips its link on save. This mirrors what the conditional-formatting
        // dxf reader already does (R120-cf-theme-color-1); the table-style path was the odd one out.
        var hasColor = XlsxColorReader.TryReadCellColorWithThemeReference(
            edge.Element(workbookNs + "color"), theme, indexedColors, out var color, out var themeColor);
        return new CellBorder(style, hasColor ? color : CellColor.Black, hasColor ? themeColor : null);
    }

}
