using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStylesheetSchemaNormalizer
{
    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ExtensionAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "uri" };
    private static readonly IReadOnlySet<string> DifferentialStylesAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "count" };
    private static readonly IReadOnlySet<string> DifferentialStyleChildren =
        new HashSet<string>(StringComparer.Ordinal) { "font", "numFmt", "fill", "alignment", "border", "protection", "extLst" };
    private static readonly IReadOnlySet<string> DifferentialFontChildren =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "b",
            "i",
            "strike",
            "condense",
            "extend",
            "outline",
            "shadow",
            "u",
            "vertAlign",
            "sz",
            "color",
            "name",
            "charset",
            "family",
            "scheme"
        };
    private static readonly IReadOnlySet<string> ValAttribute =
        new HashSet<string>(StringComparer.Ordinal) { "val" };
    private static readonly IReadOnlySet<string> ColorAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "auto", "indexed", "rgb", "theme", "tint" };
    private static readonly IReadOnlySet<string> NumFmtAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "numFmtId", "formatCode" };
    private static readonly IReadOnlySet<string> AlignmentAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "horizontal",
            "vertical",
            "textRotation",
            "wrapText",
            "indent",
            "relativeIndent",
            "justifyLastLine",
            "shrinkToFit",
            "readingOrder"
        };
    private static readonly IReadOnlySet<string> BorderAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "diagonalUp", "diagonalDown", "outline" };
    private static readonly IReadOnlySet<string> ProtectionAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "locked", "hidden" };
    private static readonly IReadOnlySet<string> TableStylesAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "count", "defaultTableStyle", "defaultPivotStyle" };
    private static readonly IReadOnlySet<string> TableStyleAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "name", "pivot", "table", "count" };
    private static readonly IReadOnlySet<string> TableStyleElementAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "type", "size", "dxfId" };
    private static readonly IReadOnlySet<string> TableStyleElementTypes =
        new HashSet<string>(StringComparer.Ordinal)
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
            "lastTotalCell",
            "firstSubtotalColumn",
            "secondSubtotalColumn",
            "thirdSubtotalColumn",
            "blankRow",
            "firstSubtotalRow",
            "secondSubtotalRow",
            "thirdSubtotalRow",
            "pageFieldLabels",
            "pageFieldValues"
        };

    public static void Normalize(Stream xlsxStream)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Normalize(archive);
    }

    public static void Normalize(ZipArchive archive)
    {
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        if (stylesEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
        if (!NormalizeStylesheet(stylesXml, workbookNs))
            return;

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
    }

    internal static bool NormalizeStylesheet(XDocument stylesXml, XNamespace workbookNs)
    {
        var root = stylesXml.Root;
        if (root is null)
            return false;

        var changed = false;
        if (NormalizeStylesheetChildOrder(root, workbookNs))
            changed = true;

        if (root.Element(workbookNs + "colors") is { } colors &&
            NormalizeColorsChildOrder(colors, workbookNs))
        {
            changed = true;
        }

        foreach (var font in root.Element(workbookNs + "fonts")?.Elements(workbookNs + "font") ?? [])
        {
            if (NormalizeRegularFont(font, workbookNs))
                changed = true;
        }

        if (root.Element(workbookNs + "dxfs") is { } differentialStyles &&
            NormalizeDifferentialStyles(differentialStyles, workbookNs))
        {
            changed = true;
        }

        if (root.Element(workbookNs + "tableStyles") is { } tableStyles &&
            NormalizeTableStyles(tableStyles, workbookNs))
        {
            changed = true;
        }

        changed |= NormalizeStylesheetExtensionLists(root, workbookNs);
        return changed;
    }

    internal static bool NormalizeDifferentialStyles(XElement differentialStyles, XNamespace workbookNs)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(differentialStyles, DifferentialStylesAttributes);
        changed |= RemoveUnexpectedChildren(differentialStyles, workbookNs + "dxf");

        foreach (var dxf in differentialStyles.Elements(workbookNs + "dxf"))
            changed |= NormalizeDifferentialStyle(dxf, workbookNs);

        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
            differentialStyles,
            "count",
            differentialStyles.Elements(workbookNs + "dxf").Count().ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static bool NormalizeDifferentialStyle(XElement dxf, XNamespace workbookNs)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(dxf, EmptyAttributes);
        changed |= RemoveUnexpectedChildren(dxf, DifferentialStyleChildren, workbookNs);
        changed |= RemoveDuplicateChildren(dxf, DifferentialStyleChildren, workbookNs);

        foreach (var child in dxf.Elements().ToList())
            changed |= NormalizeDifferentialStyleChild(child, workbookNs);

        changed |= XlsxAdvancedConditionalFormatWriter.NormalizeDifferentialStyleOrder(dxf, workbookNs);
        return changed;
    }

    private static bool NormalizeDifferentialStyleChild(XElement child, XNamespace workbookNs)
    {
        return child.Name.LocalName switch
        {
            "font" => NormalizeDifferentialFont(child, workbookNs),
            "numFmt" => XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(child, NumFmtAttributes),
            "fill" => XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(child, EmptyAttributes),
            "alignment" => XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(child, AlignmentAttributes),
            "border" => XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(child, BorderAttributes),
            "protection" => XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(child, ProtectionAttributes),
            "extLst" => NormalizeExtensionListElement(child, workbookNs),
            _ => false
        };
    }

    internal static bool NormalizeStylesheetExtensionLists(XElement stylesheetRoot, XNamespace workbookNs)
    {
        var changed = false;
        var keptExtensionList = false;
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extensionList in stylesheetRoot.Elements(workbookNs + "extLst").ToList())
        {
            if (keptExtensionList)
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeExtensionListElement(extensionList, workbookNs, seenUris);
            if (ShouldRemoveExtensionListElement(extensionList, workbookNs))
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            keptExtensionList = true;
        }

        return changed;
    }

    private static bool NormalizeExtensionListElement(XElement extensionList, XNamespace workbookNs)
    {
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        var changed = NormalizeExtensionListElement(extensionList, workbookNs, seenUris);
        if (ShouldRemoveExtensionListElement(extensionList, workbookNs))
        {
            extensionList.Remove();
            return true;
        }

        return changed;
    }

    private static bool NormalizeExtensionListElement(
        XElement extensionList,
        XNamespace workbookNs,
        HashSet<string> seenUris)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(extensionList, EmptyAttributes);
        changed |= RemoveUnexpectedChildren(extensionList, workbookNs + "ext");

        foreach (var extension in extensionList.Elements(workbookNs + "ext").ToList())
        {
            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(extension, ExtensionAttributes);
            changed |= NormalizeExtensionUri(extension);
            var uri = extension.Attribute("uri")?.Value;
            if (string.IsNullOrWhiteSpace(uri) || !seenUris.Add(uri))
            {
                extension.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool ShouldRemoveExtensionListElement(XElement extensionList, XNamespace workbookNs) =>
        !extensionList.Elements(workbookNs + "ext").Any();

    private static bool NormalizeExtensionUri(XElement extension)
    {
        var attribute = extension.Attribute("uri");
        var trimmed = attribute?.Value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(extension, "uri", trimmed);
    }

    private static bool NormalizeDifferentialFont(XElement font, XNamespace workbookNs)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(font, EmptyAttributes);
        changed |= RemoveUnexpectedChildren(font, DifferentialFontChildren, workbookNs);
        changed |= RemoveDuplicateChildren(font, DifferentialFontChildren, workbookNs);

        foreach (var child in font.Elements())
            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(
                child,
                DifferentialFontChildAttributes(child.Name.LocalName));

        return changed;
    }

    private static IReadOnlySet<string> DifferentialFontChildAttributes(string localName) =>
        localName switch
        {
            "color" => ColorAttributes,
            "b" or "i" or "strike" or "condense" or "extend" or "outline" or "shadow" or "u" or
                "vertAlign" or "sz" or "name" or "charset" or "family" or "scheme" => ValAttribute,
            _ => EmptyAttributes
        };

    internal static bool NormalizeTableStyles(XElement tableStyles, XNamespace workbookNs)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(tableStyles, TableStylesAttributes);
        changed |= RemoveUnexpectedChildren(tableStyles, workbookNs + "tableStyle");

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableStyle in tableStyles.Elements(workbookNs + "tableStyle").ToList())
        {
            var name = tableStyle.Attribute("name")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || !seenNames.Add(name))
            {
                tableStyle.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(tableStyle, "name", name);
            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(tableStyle, TableStyleAttributes);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(tableStyle, "pivot", XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(tableStyle, "table", XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);
            changed |= RemoveUnexpectedChildren(tableStyle, workbookNs + "tableStyleElement");

            foreach (var tableStyleElement in tableStyle.Elements(workbookNs + "tableStyleElement").ToList())
                changed |= NormalizeTableStyleElement(tableStyleElement);

            changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
                tableStyle,
                "count",
                tableStyle.Elements(workbookNs + "tableStyleElement").Count().ToString(CultureInfo.InvariantCulture));
        }

        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
            tableStyles,
            "count",
            tableStyles.Elements(workbookNs + "tableStyle").Count().ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static bool NormalizeTableStyleElement(XElement tableStyleElement)
    {
        var type = tableStyleElement.Attribute("type")?.Value.Trim();
        if (type is null || !TableStyleElementTypes.Contains(type))
        {
            tableStyleElement.Remove();
            return true;
        }

        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(tableStyleElement, "type", type);
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(
            tableStyleElement,
            TableStyleElementAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(tableStyleElement, "dxfId", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(tableStyleElement, "size", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(tableStyleElement);
        return changed;
    }

    private static bool NormalizeStylesheetChildOrder(XElement root, XNamespace workbookNs)
    {
        var orderedChildren = root.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => StylesheetChildOrder(item.Element, workbookNs))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || root.Elements().SequenceEqual(orderedChildren))
            return false;

        root.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int StylesheetChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "numFmts" ? 0 :
        element.Name == workbookNs + "fonts" ? 1 :
        element.Name == workbookNs + "fills" ? 2 :
        element.Name == workbookNs + "borders" ? 3 :
        element.Name == workbookNs + "cellStyleXfs" ? 4 :
        element.Name == workbookNs + "cellXfs" ? 5 :
        element.Name == workbookNs + "cellStyles" ? 6 :
        element.Name == workbookNs + "dxfs" ? 7 :
        element.Name == workbookNs + "tableStyles" ? 8 :
        element.Name == workbookNs + "colors" ? 9 :
        element.Name == workbookNs + "extLst" ? 100 :
        90;

    private static bool NormalizeColorsChildOrder(XElement colors, XNamespace workbookNs)
    {
        var orderedChildren = colors.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => ColorsChildOrder(item.Element, workbookNs))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || colors.Elements().SequenceEqual(orderedChildren))
            return false;

        colors.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int ColorsChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "indexedColors" ? 0 :
        element.Name == workbookNs + "mruColors" ? 1 :
        90;

    private static bool NormalizeRegularFont(XElement font, XNamespace workbookNs)
    {
        var changed = XlsxFontNameSanitizer.SanitizeValAttribute(font.Element(workbookNs + "name"));
        var orderedChildren = font.Elements()
            .OrderBy(element => RegularFontChildOrder(element, workbookNs))
            .ToList();
        if (orderedChildren.Count == 0)
            return changed;

        if (font.Elements().Select(element => element.Name).SequenceEqual(orderedChildren.Select(element => element.Name)))
            return changed;

        font.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int RegularFontChildOrder(XElement element, XNamespace workbookNs) =>
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

    private static bool RemoveUnexpectedChildren(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().ToList())
        {
            if (child.Name == allowedChildName)
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveUnexpectedChildren(
        XElement element,
        IReadOnlySet<string> allowedLocalNames,
        XNamespace allowedNamespace)
    {
        var changed = false;
        foreach (var child in element.Elements().ToList())
        {
            if (child.Name.Namespace == allowedNamespace && allowedLocalNames.Contains(child.Name.LocalName))
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveDuplicateChildren(
        XElement element,
        IReadOnlySet<string> singletonLocalNames,
        XNamespace singletonNamespace)
    {
        var changed = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in element.Elements().ToList())
        {
            if (child.Name.Namespace != singletonNamespace ||
                !singletonLocalNames.Contains(child.Name.LocalName) ||
                seen.Add(child.Name.LocalName))
            {
                continue;
            }

            child.Remove();
            changed = true;
        }

        return changed;
    }

}
