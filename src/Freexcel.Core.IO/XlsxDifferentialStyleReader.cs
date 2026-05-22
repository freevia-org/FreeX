using Freexcel.Core.Model;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxDifferentialStyleReader
{
    public static IReadOnlyList<CellStyle> ReadAll(ZipArchive archive, XNamespace workbookNs)
    {
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        if (stylesEntry is null)
            return [];

        try
        {
            var stylesXml = LoadXml(stylesEntry);
            return stylesXml.Root?
                .Element(workbookNs + "dxfs")?
                .Elements(workbookNs + "dxf")
                .Select(dxf => Read(dxf, workbookNs))
                .ToList()
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static CellStyle Read(XElement dxf, XNamespace workbookNs)
    {
        var style = new CellStyle();
        var font = dxf.Element(workbookNs + "font");
        if (font is not null)
        {
            style.Bold = font.Element(workbookNs + "b") is not null;
            style.Italic = font.Element(workbookNs + "i") is not null;
            style.Underline = font.Element(workbookNs + "u") is not null;
            style.Strikethrough = font.Element(workbookNs + "strike") is not null;
            var verticalAlignment = font.Element(workbookNs + "vertAlign")?.Attribute("val")?.Value;
            style.Superscript = string.Equals(verticalAlignment, "superscript", StringComparison.OrdinalIgnoreCase);
            style.Subscript = string.Equals(verticalAlignment, "subscript", StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(font.Element(workbookNs + "sz")?.Attribute("val")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) &&
                IsSupportedFontSize(size))
            {
                style.FontSize = size;
            }

            var fontName = font.Element(workbookNs + "name")?.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(fontName))
                style.FontName = fontName;

            if (XlsxColorReader.TryReadCellColor(font.Element(workbookNs + "color"), out var fontColor))
                style.FontColor = fontColor;
        }

        var patternFill = dxf
            .Element(workbookNs + "fill")?
            .Element(workbookNs + "patternFill");
        if (patternFill is not null)
        {
            style.FillPatternStyle = FromPatternType(patternFill.Attribute("patternType")?.Value);
            if (XlsxColorReader.TryReadCellColor(patternFill.Element(workbookNs + "bgColor"), out var backgroundColor))
                style.FillColor = backgroundColor;
            if (XlsxColorReader.TryReadCellColor(patternFill.Element(workbookNs + "fgColor"), out var foregroundColor))
            {
                if (style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
                    style.FillColor = foregroundColor;
                else
                    style.FillPatternColor = foregroundColor;
            }
        }

        var border = dxf.Element(workbookNs + "border");
        if (border is not null)
        {
            style.BorderTop = ReadBorder(border.Element(workbookNs + "top"), workbookNs);
            style.BorderRight = ReadBorder(border.Element(workbookNs + "right"), workbookNs);
            style.BorderBottom = ReadBorder(border.Element(workbookNs + "bottom"), workbookNs);
            style.BorderLeft = ReadBorder(border.Element(workbookNs + "left"), workbookNs);
        }

        var numberFormat = dxf.Element(workbookNs + "numFmt")?.Attribute("formatCode")?.Value;
        if (!string.IsNullOrWhiteSpace(numberFormat))
            style.NumberFormat = numberFormat;

        var nativeAttributes = ReadNativeAttributes(dxf);
        if (nativeAttributes.Count > 0)
            style.NativeDifferentialAttributes = nativeAttributes;

        var nativeChildXmls = ReadNativeChildXmls(dxf, workbookNs);
        if (nativeChildXmls.Count > 0)
            style.NativeDifferentialChildXmls = nativeChildXmls;

        var nativeElementXmls = ReadNativeElementXmls(dxf, workbookNs);
        if (nativeElementXmls.Count > 0)
            style.NativeDifferentialElementXmls = nativeElementXmls;

        return style;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Dictionary<string, string> ReadNativeAttributes(XElement dxf) =>
        dxf.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0)
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);

    private static List<string> ReadNativeChildXmls(XElement dxf, XNamespace workbookNs)
    {
        var modeledChildren = ModeledChildren(workbookNs);
        return dxf.Elements()
            .Where(element => !modeledChildren.Contains(element.Name))
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();
    }

    private static Dictionary<string, string> ReadNativeElementXmls(XElement dxf, XNamespace workbookNs)
    {
        var modeledChildren = ModeledChildren(workbookNs);
        return dxf.Elements()
            .Where(element => modeledChildren.Contains(element.Name))
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().ToString(SaveOptions.DisableFormatting),
                StringComparer.Ordinal);
    }

    private static XName[] ModeledChildren(XNamespace workbookNs) =>
    [
        workbookNs + "font",
        workbookNs + "numFmt",
        workbookNs + "fill",
        workbookNs + "alignment",
        workbookNs + "border",
        workbookNs + "protection"
    ];

    private static CellBorder ReadBorder(XElement? edge, XNamespace workbookNs)
    {
        if (edge is null)
            return default;

        var style = edge.Attribute("style")?.Value switch
        {
            "thin" => BorderStyle.Thin,
            "medium" => BorderStyle.Medium,
            "thick" => BorderStyle.Thick,
            "dashed" => BorderStyle.Dashed,
            "dotted" => BorderStyle.Dotted,
            "double" => BorderStyle.Double,
            _ => BorderStyle.None
        };
        if (style == BorderStyle.None)
            return default;

        return new CellBorder(
            style,
            XlsxColorReader.TryReadCellColor(edge.Element(workbookNs + "color"), out var color) ? color : CellColor.Black);
    }

    private static bool IsSupportedFontSize(double fontSize) =>
        fontSize >= 1 && fontSize <= 409;

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
