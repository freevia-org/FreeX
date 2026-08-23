using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSheetPropertiesNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly IReadOnlySet<string> SheetPropertiesAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "syncHorizontal",
            "syncVertical",
            "syncRef",
            "transitionEvaluation",
            "transitionEntry",
            "published",
            "codeName",
            "filterMode",
            "enableFormatConditionsCalculation"
        };

    private static readonly IReadOnlySet<string> SheetPropertiesBooleanAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "syncHorizontal",
            "syncVertical",
            "transitionEvaluation",
            "transitionEntry",
            "published",
            "filterMode",
            "enableFormatConditionsCalculation"
        };

    private static readonly IReadOnlySet<XName> SheetPropertiesChildNames =
        new HashSet<XName>
        {
            WorksheetNs + "tabColor",
            WorksheetNs + "outlinePr",
            WorksheetNs + "pageSetUpPr"
        };

    private static readonly IReadOnlySet<string> ColorAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "auto", "indexed", "rgb", "theme", "tint" };

    private static readonly Regex RgbHexPattern = new(
        "^[0-9A-Fa-f]{8}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var sheetPropertiesElements = worksheetRoot.Elements(WorksheetNs + "sheetPr").ToList();
        if (sheetPropertiesElements.Count == 0)
            return false;

        var changed = false;
        var sheetProperties = sheetPropertiesElements[0];
        foreach (var duplicate in sheetPropertiesElements.Skip(1))
        {
            changed |= MergeSheetProperties(sheetProperties, duplicate);
            duplicate.Remove();
            changed = true;
        }

        changed |= NormalizeElement(sheetProperties);
        return changed;
    }

    public static bool NormalizeElement(XElement sheetProperties)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(sheetProperties, SheetPropertiesAttributes);

        foreach (var attributeName in SheetPropertiesBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetProperties, attributeName, XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetProperties, "syncRef", NormalizeReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetProperties, "codeName", XlsxXmlNormalizationHelpers.NormalizeOptionalText);

        changed |= RemoveUnexpectedChildren(sheetProperties);
        changed |= RemoveDuplicateAllowedChildren(sheetProperties);

        if (sheetProperties.Element(WorksheetNs + "tabColor") is { } tabColor)
            changed |= NormalizeTabColor(tabColor);

        changed |= XlsxWorksheetPageLayoutNormalizer.NormalizeSheetPropertiesPageLayout(sheetProperties);
        changed |= RemoveNonElementNodes(sheetProperties);
        changed |= NormalizeChildOrder(sheetProperties);
        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool MergeSheetProperties(XElement target, XElement source)
    {
        var changed = false;
        foreach (var attribute in source.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || target.Attribute(attribute.Name) is not null)
                continue;

            target.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        foreach (var child in source.Elements())
        {
            if (!SheetPropertiesChildNames.Contains(child.Name) || target.Element(child.Name) is not null)
                continue;

            target.Add(new XElement(child));
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeTabColor(XElement tabColor)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(tabColor, ColorAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(tabColor, "auto", XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(tabColor, "indexed", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(tabColor, "theme", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(tabColor, "rgb", NormalizeRgbHex);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(tabColor, "tint", NormalizeTint);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(tabColor);
        return changed;
    }

    private static bool RemoveUnexpectedChildren(XElement sheetProperties)
    {
        var changed = false;
        foreach (var child in sheetProperties.Elements().ToList())
        {
            if (SheetPropertiesChildNames.Contains(child.Name))
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveDuplicateAllowedChildren(XElement sheetProperties)
    {
        var changed = false;
        var seenChildren = new HashSet<XName>();
        foreach (var child in sheetProperties.Elements().ToList())
        {
            if (seenChildren.Add(child.Name))
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeChildOrder(XElement sheetProperties)
    {
        var orderedChildren = sheetProperties.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => SheetPropertiesChildOrder(item.Element))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || sheetProperties.Elements().SequenceEqual(orderedChildren))
            return false;

        sheetProperties.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int SheetPropertiesChildOrder(XElement element) =>
        element.Name == WorksheetNs + "tabColor" ? 0 :
        element.Name == WorksheetNs + "outlinePr" ? 1 :
        element.Name == WorksheetNs + "pageSetUpPr" ? 2 :
        90;

    private static bool RemoveNonElementNodes(XElement element)
    {
        var nodes = element.Nodes().Where(node => node is not XElement).ToList();
        if (nodes.Count == 0)
            return false;

        foreach (var node in nodes)
            node.Remove();
        return true;
    }

    private static string? NormalizeReference(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains(' ', StringComparison.Ordinal))
            return null;

        var parts = trimmed.Split(':');
        var sheet = SheetId.New();
        if (parts.Length == 1)
        {
            return CellAddress.TryParse(parts[0], sheet, out var address)
                ? address.ToA1()
                : null;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            var range = new GridRange(start, end);
            return range.Start == range.End
                ? range.Start.ToA1()
                : $"{range.Start.ToA1()}:{range.End.ToA1()}";
        }

        return null;
    }

    private static string? NormalizeRgbHex(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && RgbHexPattern.IsMatch(trimmed)
            ? trimmed.ToUpperInvariant()
            : null;
    }

    private static string? NormalizeTint(string? value)
    {
        var trimmed = value?.Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
               double.IsFinite(parsed) &&
               parsed is >= -1 and <= 1
            ? XlsxNumberFormatting.ToXmlString(parsed)
            : null;
    }

}
