using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxAdvancedConditionalFormatWriter
{
    private static IReadOnlyDictionary<Guid, int> SaveDifferentialStyles(
        ZipArchive archive,
        Workbook workbook,
        XNamespace workbookNs)
    {
        var rules = new List<ConditionalFormat>();
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var conditionalFormat in sheet.ConditionalFormats)
            {
                if (conditionalFormat.FormatIfTrue is not null &&
                    XlsxAdvancedConditionalFormatMetadata.IsAdvancedConditionalFormat(conditionalFormat))
                {
                    rules.Add(conditionalFormat);
                }
            }
        }

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
            HasDifferentialFont(style)
                ? new XElement(
                    workbookNs + "font",
                    style.Bold != def.Bold ? new XElement(workbookNs + "b") : null,
                    style.Italic != def.Italic ? new XElement(workbookNs + "i") : null,
                    style.Strikethrough != def.Strikethrough ? new XElement(workbookNs + "strike") : null,
                    style.Underline != def.Underline ? new XElement(workbookNs + "u") : null,
                    style.Superscript != def.Superscript
                        ? new XElement(workbookNs + "vertAlign", new XAttribute("val", "superscript"))
                        : style.Subscript != def.Subscript
                            ? new XElement(workbookNs + "vertAlign", new XAttribute("val", "subscript"))
                            : null,
                    style.FontSize != def.FontSize && IsSupportedFontSize(style.FontSize)
                        ? new XElement(workbookNs + "sz", new XAttribute("val", style.FontSize.ToString(CultureInfo.InvariantCulture)))
                        : null,
                    style.FontColor != def.FontColor ? new XElement(workbookNs + "color", new XAttribute("rgb", ToArgb(style.FontColor))) : null,
                    style.FontName != def.FontName ? new XElement(workbookNs + "name", new XAttribute("val", style.FontName)) : null)
                : null,
            style.NumberFormat != def.NumberFormat
                ? new XElement(
                    workbookNs + "numFmt",
                    new XAttribute("numFmtId", (164 + numberFormatId).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("formatCode", style.NumberFormat))
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

        if (style.NativeDifferentialAttributes is { } attributes)
        {
            foreach (var (name, value) in attributes)
                TrySetNativeAttributeIfMissing(dxf, name, value);
        }

        if (style.NativeDifferentialChildXmls is { } childXmls)
        {
            foreach (var nativeChildXml in childXmls)
            {
                if (string.IsNullOrWhiteSpace(nativeChildXml))
                    continue;

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
        }

        NormalizeDifferentialStyleOrder(dxf, workbookNs);
        return dxf;
    }

    private static void MergeDifferentialStyleElementNativeMetadata(
        XElement dxf,
        CellStyle style,
        XNamespace workbookNs)
    {
        if (style.NativeDifferentialElementXmls is null)
            return;

        foreach (var (localName, sourceXml) in style.NativeDifferentialElementXmls)
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

    internal static bool NormalizeDifferentialStyleOrder(XElement dxf, XNamespace workbookNs)
    {
        var changed = false;
        var font = dxf.Element(workbookNs + "font");
        if (font is not null)
            changed |= NormalizeDifferentialFontOrder(font, workbookNs);

        return NormalizeDifferentialStyleChildrenOrder(dxf, workbookNs) || changed;
    }

    private static int DifferentialStyleChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "font" ? 0 :
        element.Name == workbookNs + "numFmt" ? 1 :
        element.Name == workbookNs + "fill" ? 2 :
        element.Name == workbookNs + "alignment" ? 3 :
        element.Name == workbookNs + "border" ? 4 :
        element.Name == workbookNs + "protection" ? 5 :
        element.Name == workbookNs + "extLst" ? 100 :
        90;

    private static bool NormalizeDifferentialFontOrder(XElement font, XNamespace workbookNs)
    {
        var changed = XlsxFontNameSanitizer.SanitizeValAttribute(font.Element(workbookNs + "name"));

        return NormalizeDifferentialFontChildrenOrder(font, workbookNs) || changed;
    }

    private static int DifferentialFontChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "b" ? 0 :
        element.Name == workbookNs + "i" ? 1 :
        element.Name == workbookNs + "strike" ? 2 :
        element.Name == workbookNs + "condense" ? 3 :
        element.Name == workbookNs + "extend" ? 4 :
        element.Name == workbookNs + "outline" ? 5 :
        element.Name == workbookNs + "shadow" ? 6 :
        element.Name == workbookNs + "u" ? 7 :
        element.Name == workbookNs + "vertAlign" ? 8 :
        element.Name == workbookNs + "sz" ? 9 :
        element.Name == workbookNs + "color" ? 10 :
        element.Name == workbookNs + "name" ? 11 :
        element.Name == workbookNs + "charset" ? 12 :
        element.Name == workbookNs + "family" ? 13 :
        element.Name == workbookNs + "scheme" ? 14 :
        90;

    private static bool NormalizeDifferentialStyleChildrenOrder(XElement dxf, XNamespace workbookNs)
    {
        var previousOrder = int.MinValue;
        foreach (var child in dxf.Elements())
        {
            var order = DifferentialStyleChildOrder(child, workbookNs);
            if (order < previousOrder)
            {
                var children = CopyChildElements(dxf);
                StableSortDifferentialStyleChildren(children, workbookNs);
                dxf.ReplaceNodes(children);
                return true;
            }

            previousOrder = order;
        }

        return false;
    }

    private static bool NormalizeDifferentialFontChildrenOrder(XElement font, XNamespace workbookNs)
    {
        var previousOrder = int.MinValue;
        foreach (var child in font.Elements())
        {
            var order = DifferentialFontChildOrder(child, workbookNs);
            if (order < previousOrder)
            {
                var children = CopyChildElements(font);
                StableSortDifferentialFontChildren(children, workbookNs);
                font.ReplaceNodes(children);
                return true;
            }

            previousOrder = order;
        }

        return false;
    }

    private static List<XElement> CopyChildElements(XElement parent)
    {
        var children = new List<XElement>();
        foreach (var child in parent.Elements())
            children.Add(child);

        return children;
    }

    private static void StableSortDifferentialStyleChildren(List<XElement> children, XNamespace workbookNs)
    {
        for (var index = 1; index < children.Count; index++)
        {
            var current = children[index];
            var currentOrder = DifferentialStyleChildOrder(current, workbookNs);
            var insertAt = index - 1;
            while (insertAt >= 0 && DifferentialStyleChildOrder(children[insertAt], workbookNs) > currentOrder)
            {
                children[insertAt + 1] = children[insertAt];
                insertAt--;
            }

            children[insertAt + 1] = current;
        }
    }

    private static void StableSortDifferentialFontChildren(List<XElement> children, XNamespace workbookNs)
    {
        for (var index = 1; index < children.Count; index++)
        {
            var current = children[index];
            var currentOrder = DifferentialFontChildOrder(current, workbookNs);
            var insertAt = index - 1;
            while (insertAt >= 0 && DifferentialFontChildOrder(children[insertAt], workbookNs) > currentOrder)
            {
                children[insertAt + 1] = children[insertAt];
                insertAt--;
            }

            children[insertAt + 1] = current;
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
