using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxAdvancedConditionalFormatWriter
{
    private static IReadOnlyDictionary<Guid, int> SaveDifferentialStyles(
        ZipArchive archive,
        Workbook workbook,
        XNamespace workbookNs)
    {
        var rules = workbook.Sheets
            .SelectMany(sheet => sheet.ConditionalFormats)
            .Where(cf => IsAdvancedConditionalFormat(cf) && cf.FormatIfTrue is not null)
            .ToList();
        if (rules.Count == 0)
            return new Dictionary<Guid, int>();

        var stylesEntry = archive.GetEntry("xl/styles.xml");
        var stylesXml = stylesEntry is not null
            ? XlsxPackageXmlEditor.LoadXml(stylesEntry)
            : new XDocument(new XElement(workbookNs + "styleSheet"));
        var root = stylesXml.Root;
        if (root is null)
            return new Dictionary<Guid, int>();

        var dxfs = root.Element(workbookNs + "dxfs");
        if (dxfs is null)
        {
            dxfs = new XElement(workbookNs + "dxfs");
            root.Add(dxfs);
        }

        var result = new Dictionary<Guid, int>();
        var nextIndex = dxfs.Elements(workbookNs + "dxf").Count();
        foreach (var rule in rules)
        {
            if (rule.FormatIfTrue is null)
                continue;

            result[rule.Id] = nextIndex++;
            dxfs.Add(ToDifferentialStyleXml(rule.FormatIfTrue, workbookNs, nextIndex));
        }

        dxfs.SetAttributeValue("count", dxfs.Elements(workbookNs + "dxf").Count().ToString(CultureInfo.InvariantCulture));
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
        return result;
    }

    private static XElement ToDifferentialStyleXml(CellStyle style, XNamespace workbookNs, int numberFormatId)
    {
        var def = CellStyle.Default;
        var dxf = new XElement(
            workbookNs + "dxf",
            style.NumberFormat != def.NumberFormat
                ? new XElement(
                    workbookNs + "numFmt",
                    new XAttribute("numFmtId", (164 + numberFormatId).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("formatCode", style.NumberFormat))
                : null,
            HasDifferentialFont(style)
                ? new XElement(
                    workbookNs + "font",
                    style.Bold != def.Bold ? new XElement(workbookNs + "b") : null,
                    style.Italic != def.Italic ? new XElement(workbookNs + "i") : null,
                    style.Underline != def.Underline ? new XElement(workbookNs + "u") : null,
                    style.Strikethrough != def.Strikethrough ? new XElement(workbookNs + "strike") : null,
                    style.Superscript != def.Superscript
                        ? new XElement(workbookNs + "vertAlign", new XAttribute("val", "superscript"))
                        : style.Subscript != def.Subscript
                            ? new XElement(workbookNs + "vertAlign", new XAttribute("val", "subscript"))
                            : null,
                    style.FontColor != def.FontColor ? new XElement(workbookNs + "color", new XAttribute("rgb", ToArgb(style.FontColor))) : null,
                    style.FontSize != def.FontSize && IsSupportedFontSize(style.FontSize)
                        ? new XElement(workbookNs + "sz", new XAttribute("val", style.FontSize.ToString(CultureInfo.InvariantCulture)))
                        : null,
                    style.FontName != def.FontName ? new XElement(workbookNs + "name", new XAttribute("val", style.FontName)) : null)
                : null,
            HasDifferentialFill(style)
                ? new XElement(
                    workbookNs + "fill",
                    new XElement(
                        workbookNs + "patternFill",
                        new XAttribute("patternType", ToPatternType(style.FillPatternStyle)),
                        style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid
                            ? style.FillColor is { } fill
                                ? new XElement(workbookNs + "fgColor", new XAttribute("rgb", ToArgb(fill)))
                                : null
                            : style.FillPatternColor is { } pattern
                                ? new XElement(workbookNs + "fgColor", new XAttribute("rgb", ToArgb(pattern)))
                                : null,
                        style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid
                            ? new XElement(workbookNs + "bgColor", new XAttribute("indexed", "64"))
                            : style.FillColor is { } background
                                ? new XElement(workbookNs + "bgColor", new XAttribute("rgb", ToArgb(background)))
                                : new XElement(workbookNs + "bgColor", new XAttribute("indexed", "64"))))
                : null,
            HasDifferentialBorder(style)
                ? new XElement(
                    workbookNs + "border",
                    ToDifferentialBorderXml("left", style.BorderLeft, workbookNs),
                    ToDifferentialBorderXml("right", style.BorderRight, workbookNs),
                    ToDifferentialBorderXml("top", style.BorderTop, workbookNs),
                    ToDifferentialBorderXml("bottom", style.BorderBottom, workbookNs))
                : null);

        MergeDifferentialStyleElementNativeMetadata(dxf, style, workbookNs);

        foreach (var (name, value) in style.NativeDifferentialAttributes ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && dxf.Attribute(name) is null)
                dxf.SetAttributeValue(name, value);
        }

        foreach (var nativeChildXml in (style.NativeDifferentialChildXmls ?? []).Where(xml => !string.IsNullOrWhiteSpace(xml)))
        {
            try
            {
                var nativeChild = XElement.Parse(nativeChildXml);
                if (nativeChild.Name.Namespace == workbookNs &&
                    nativeChild.Name.LocalName is not "font" and not "numFmt" and not "fill" and not "alignment" and not "border" and not "protection")
                {
                    dxf.Add(nativeChild);
                }
            }
            catch
            {
                // Ignore malformed native differential-style payloads from older saves.
            }
        }

        return dxf;
    }

    private static void MergeDifferentialStyleElementNativeMetadata(
        XElement dxf,
        CellStyle style,
        XNamespace workbookNs)
    {
        foreach (var (localName, sourceXml) in style.NativeDifferentialElementXmls ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(localName) || string.IsNullOrWhiteSpace(sourceXml))
                continue;

            try
            {
                var sourceElement = XElement.Parse(sourceXml);
                if (sourceElement.Name.Namespace != workbookNs || !IsModeledDifferentialStyleElement(sourceElement.Name.LocalName))
                    continue;

                var targetElement = dxf.Element(workbookNs + localName);
                if (targetElement is null)
                    dxf.Add(sourceElement);
                else
                    XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceElement, targetElement);
            }
            catch
            {
                // Ignore malformed nested dxf metadata from older saves.
            }
        }
    }

    private static bool IsModeledDifferentialStyleElement(string localName) =>
        localName is "font" or "numFmt" or "fill" or "alignment" or "border" or "protection";

    private static bool HasDifferentialFont(CellStyle style)
    {
        var def = CellStyle.Default;
        return style.Bold != def.Bold ||
            style.Italic != def.Italic ||
            style.Underline != def.Underline ||
            style.Strikethrough != def.Strikethrough ||
            style.Superscript != def.Superscript ||
            style.Subscript != def.Subscript ||
            style.FontColor != def.FontColor ||
            style.FontSize != def.FontSize ||
            style.FontName != def.FontName;
    }

    private static bool HasDifferentialBorder(CellStyle style) =>
        style.BorderLeft.Style != BorderStyle.None ||
        style.BorderRight.Style != BorderStyle.None ||
        style.BorderTop.Style != BorderStyle.None ||
        style.BorderBottom.Style != BorderStyle.None;

    private static bool HasDifferentialFill(CellStyle style) =>
        style.FillColor is not null ||
        style.FillPatternStyle != CellFillPatternStyle.None ||
        style.FillPatternColor is not null;

    private static XElement ToDifferentialBorderXml(string edgeName, CellBorder border, XNamespace workbookNs)
    {
        var element = new XElement(workbookNs + edgeName);
        if (border.Style != BorderStyle.None)
        {
            element.SetAttributeValue("style", ToDifferentialBorderStyle(border.Style));
            element.Add(new XElement(workbookNs + "color", new XAttribute("rgb", ToArgb(border.Color))));
        }

        return element;
    }

    private static string ToDifferentialBorderStyle(BorderStyle style) =>
        style switch
        {
            BorderStyle.Thin => "thin",
            BorderStyle.Medium => "medium",
            BorderStyle.Thick => "thick",
            BorderStyle.Dashed => "dashed",
            BorderStyle.Dotted => "dotted",
            BorderStyle.Double => "double",
            _ => "none"
        };
}
