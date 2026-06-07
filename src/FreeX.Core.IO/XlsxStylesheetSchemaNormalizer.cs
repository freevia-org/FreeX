using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStylesheetSchemaNormalizer
{
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

        foreach (var dxf in root.Element(workbookNs + "dxfs")?.Elements(workbookNs + "dxf") ?? [])
        {
            if (XlsxAdvancedConditionalFormatWriter.NormalizeDifferentialStyleOrder(dxf, workbookNs))
                changed = true;
        }

        if (root.Element(workbookNs + "tableStyles") is { } tableStyles &&
            NormalizeTableStyles(tableStyles, workbookNs))
        {
            changed = true;
        }

        return changed;
    }

    internal static bool NormalizeTableStyles(XElement tableStyles, XNamespace workbookNs)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(tableStyles, TableStylesAttributes);
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

            changed |= SetAttributeIfChanged(tableStyle, "name", name);
            changed |= RemoveUnknownAttributes(tableStyle, TableStyleAttributes);
            changed |= NormalizeAttribute(tableStyle, "pivot", NormalizeBoolean);
            changed |= NormalizeAttribute(tableStyle, "table", NormalizeBoolean);
            changed |= RemoveUnexpectedChildren(tableStyle, workbookNs + "tableStyleElement");

            foreach (var tableStyleElement in tableStyle.Elements(workbookNs + "tableStyleElement").ToList())
                changed |= NormalizeTableStyleElement(tableStyleElement);

            changed |= SetAttributeIfChanged(
                tableStyle,
                "count",
                tableStyle.Elements(workbookNs + "tableStyleElement").Count().ToString(CultureInfo.InvariantCulture));
        }

        changed |= SetAttributeIfChanged(
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
        changed |= SetAttributeIfChanged(tableStyleElement, "type", type);
        changed |= RemoveUnknownAttributes(tableStyleElement, TableStyleElementAttributes);
        changed |= NormalizeAttribute(tableStyleElement, "dxfId", NormalizeUnsignedInt);
        changed |= NormalizeAttribute(tableStyleElement, "size", NormalizeUnsignedInt);
        changed |= RemoveAllNodes(tableStyleElement);
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

    private static bool NormalizeAttribute(
        XElement element,
        string attributeName,
        Func<string?, string?> normalize)
    {
        var attribute = element.Attribute(attributeName);
        var normalized = normalize(attribute?.Value);
        if (normalized is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        return SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "1", StringComparison.Ordinal) ||
            string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "1";
        }

        if (string.Equals(trimmed, "0", StringComparison.Ordinal) ||
            string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "0";
        }

        return null;
    }

    private static string? NormalizeUnsignedInt(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

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

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool SetAttributeIfChanged(XElement element, string attributeName, string value)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
        return true;
    }
}
