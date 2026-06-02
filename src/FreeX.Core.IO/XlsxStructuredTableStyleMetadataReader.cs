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

    public static List<StructuredTableStyleModel> Load(XDocument? stylesXml)
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
                if (!appliesToTables || appliesToPivotTables)
                    continue;

                var style = new StructuredTableStyleModel
                {
                    Name = name,
                    AppliesToTables = true,
                    AppliesToPivotTables = false,
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
                            ? ReadDifferentialStyleDiff(differentialStyles[dxfId.Value], workbookNs)
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

    private static StyleDiff? ReadDifferentialStyleDiff(XElement dxf, XNamespace workbookNs)
    {
        var font = dxf.Element(workbookNs + "font");
        CellColor? fontColor = null;
        bool? bold = null;
        if (font is not null)
        {
            if (XlsxColorReader.TryReadCellColor(font.Element(workbookNs + "color"), out var readFontColor))
                fontColor = readFontColor;

            if (font.Element(workbookNs + "b") is { } boldElement)
                bold = XlsxXmlAttributeReader.ReadBoolAttribute(boldElement, "val", defaultValue: true);
        }

        CellColor? fillColor = null;
        CellColor? fillPatternColor = null;
        CellFillPatternStyle? fillPatternStyle = null;
        var patternFill = dxf
            .Element(workbookNs + "fill")?
            .Element(workbookNs + "patternFill");
        if (patternFill is not null)
        {
            var patternStyle = FromPatternType(patternFill.Attribute("patternType")?.Value);
            if (patternStyle != CellFillPatternStyle.None)
                fillPatternStyle = patternStyle;

            if (XlsxColorReader.TryReadCellColor(patternFill.Element(workbookNs + "fgColor"), out var foregroundColor))
            {
                if (patternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
                    fillColor = foregroundColor;
                else
                    fillPatternColor = foregroundColor;
            }

            if (fillColor is null &&
                XlsxColorReader.TryReadCellColor(patternFill.Element(workbookNs + "bgColor"), out var backgroundColor))
            {
                fillColor = backgroundColor;
            }
        }

        return fontColor is null &&
               bold is null &&
               fillColor is null &&
               fillPatternColor is null &&
               fillPatternStyle is null
            ? null
            : new StyleDiff(
                Bold: bold,
                FontColor: fontColor,
                FillColor: fillColor,
                FillPatternStyle: fillPatternStyle,
                FillPatternColor: fillPatternColor);
    }

    private static CellFillPatternStyle FromPatternType(string? patternType) =>
        patternType switch
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
            _ => CellFillPatternStyle.None
        };
}
