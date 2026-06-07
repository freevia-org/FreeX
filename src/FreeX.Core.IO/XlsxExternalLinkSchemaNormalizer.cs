using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxExternalLinkSchemaNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XName[] LinkPayloadNames =
    [
        WorkbookNs + "externalBook",
        WorkbookNs + "ddeLink",
        WorkbookNs + "oleLink"
    ];

    public static void NormalizePackage(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(IsExternalLinkXmlEntry).ToList())
        {
            var document = XlsxPackageXmlEditor.LoadXml(entry);
            var root = document.Root;
            if (root is null || root.Name != WorkbookNs + "externalLink")
                continue;

            if (NormalizeExternalLinkRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    public static bool NormalizeExternalLinkRoot(XElement externalLink)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(externalLink, []);

        var keptPayload = false;
        var keptExtensionList = false;
        foreach (var child in externalLink.Elements().ToList())
        {
            if (LinkPayloadNames.Contains(child.Name))
            {
                if (keptPayload)
                {
                    child.Remove();
                    changed = true;
                    continue;
                }

                changed |= NormalizeLinkPayloadElement(child);
                if (ShouldRemoveLinkPayloadElement(child))
                {
                    child.Remove();
                    changed = true;
                    continue;
                }

                keptPayload = true;
                continue;
            }

            if (child.Name == WorkbookNs + "extLst")
            {
                if (keptExtensionList)
                {
                    child.Remove();
                    changed = true;
                    continue;
                }

                keptExtensionList = true;
                continue;
            }

            child.Remove();
            changed = true;
        }

        changed |= NormalizeChildOrder(externalLink, ExternalLinkChildOrder);
        return changed;
    }

    private static bool NormalizeLinkPayloadElement(XElement payload) =>
        payload.Name == WorkbookNs + "externalBook"
            ? NormalizeExternalBookElement(payload)
            : false;

    private static bool ShouldRemoveLinkPayloadElement(XElement payload) =>
        payload.Name == WorkbookNs + "externalBook" &&
        string.IsNullOrWhiteSpace(payload.Attribute(RelationshipNs + "id")?.Value);

    private static bool NormalizeExternalBookElement(XElement externalBook)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(externalBook, [RelationshipNs + "id"]);
        changed |= NormalizeRelationshipId(externalBook);

        foreach (var child in externalBook.Elements().ToList())
        {
            if (child.Name == WorkbookNs + "sheetNames")
            {
                changed |= NormalizeSheetNamesElement(child);
                if (!child.Elements(WorkbookNs + "sheetName").Any())
                {
                    child.Remove();
                    changed = true;
                }
                continue;
            }

            if (child.Name == WorkbookNs + "definedNames")
            {
                changed |= NormalizeDefinedNamesElement(child);
                if (!child.Elements(WorkbookNs + "definedName").Any())
                {
                    child.Remove();
                    changed = true;
                }
                continue;
            }

            if (child.Name == WorkbookNs + "sheetDataSet")
                continue;

            child.Remove();
            changed = true;
        }

        changed |= NormalizeChildOrder(externalBook, ExternalBookChildOrder);
        return changed;
    }

    private static bool NormalizeSheetNamesElement(XElement sheetNames)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(sheetNames, []);
        foreach (var child in sheetNames.Elements().ToList())
        {
            if (child.Name != WorkbookNs + "sheetName")
            {
                child.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeSheetNameElement(child);
            if (string.IsNullOrWhiteSpace(child.Attribute("val")?.Value))
            {
                child.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeSheetNameElement(XElement sheetName)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(sheetName, [XName.Get("val")]);
        changed |= NormalizeOptionalTextAttribute(sheetName, "val");
        changed |= RemoveAllNodes(sheetName);
        return changed;
    }

    private static bool NormalizeDefinedNamesElement(XElement definedNames)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(definedNames, []);
        foreach (var child in definedNames.Elements().ToList())
        {
            if (child.Name != WorkbookNs + "definedName")
            {
                child.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeDefinedNameElement(child);
            if (string.IsNullOrWhiteSpace(child.Attribute("name")?.Value))
            {
                child.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeDefinedNameElement(XElement definedName)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(definedName, [XName.Get("name"), XName.Get("refersTo"), XName.Get("sheetId")]);
        changed |= NormalizeOptionalTextAttribute(definedName, "name");
        changed |= NormalizeOptionalTextAttribute(definedName, "refersTo");
        changed |= NormalizeAttribute(definedName, "sheetId", NormalizeUnsignedIntOrNull);
        changed |= RemoveAllNodes(definedName);
        return changed;
    }

    private static bool NormalizeRelationshipId(XElement element)
    {
        var attribute = element.Attribute(RelationshipNs + "id");
        var trimmed = attribute?.Value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, trimmed, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(RelationshipNs + "id", trimmed);
        return true;
    }

    private static bool NormalizeOptionalTextAttribute(XElement element, string attributeName)
    {
        var attribute = element.Attribute(attributeName);
        var trimmed = attribute?.Value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(element, attributeName, trimmed);
    }

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

        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool RemoveUnknownAttributes(XElement element, params XName[] allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration || allowedNames.Contains(attribute.Name))
                continue;

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
        return true;
    }

    private static bool NormalizeChildOrder(XElement parent, Func<XElement, int> orderSelector)
    {
        var orderedChildren = parent.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => orderSelector(item.Element))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || parent.Elements().SequenceEqual(orderedChildren))
            return false;

        parent.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int ExternalLinkChildOrder(XElement child) =>
        LinkPayloadNames.Contains(child.Name) ? 0 :
        child.Name == WorkbookNs + "extLst" ? 100 :
        90;

    private static int ExternalBookChildOrder(XElement child) =>
        child.Name == WorkbookNs + "sheetNames" ? 0 :
        child.Name == WorkbookNs + "definedNames" ? 1 :
        child.Name == WorkbookNs + "sheetDataSet" ? 2 :
        90;

    private static bool IsExternalLinkXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
