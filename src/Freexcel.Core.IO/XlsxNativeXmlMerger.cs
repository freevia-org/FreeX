using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxNativeXmlMerger
{
    public static bool MergeElementNativeAttributesAndChildren(XElement sourceElement, XElement targetElement)
        => MergeElementNativeAttributesAndChildren(sourceElement, targetElement, []);

    public static bool MergeElementNativeAttributesAndChildren(
        XElement sourceElement,
        XElement targetElement,
        IReadOnlyCollection<XName> modeledAttributeNames)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (modeledAttributeNames.Contains(attribute.Name))
                continue;

            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildrenByKey = targetElement
            .Elements()
            .GroupBy(ElementIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var sourceChild in sourceElement.Elements())
        {
            var key = ElementIdentityKey(sourceChild);
            if (existingChildrenByKey.TryGetValue(key, out var targetChild))
            {
                if (MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetElement.Add(new XElement(sourceChild));
            existingChildrenByKey[key] = targetElement.Elements().Last();
            changed = true;
        }

        return changed;
    }

    public static bool MergeExtensionList(XElement? sourceExtensionList, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceExtensionList is null)
            return false;

        var sourceExtensions = sourceExtensionList
            .Elements(workbookNs + "ext")
            .ToList();
        if (sourceExtensions.Count == 0)
            return false;

        var targetExtensionList = targetRoot.Element(workbookNs + "extLst");
        if (targetExtensionList is null)
        {
            targetRoot.Add(new XElement(sourceExtensionList));
            return true;
        }

        var existingUris = targetExtensionList
            .Elements(workbookNs + "ext")
            .Select(extension => extension.Attribute("uri")?.Value)
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceExtension in sourceExtensions)
        {
            var uri = sourceExtension.Attribute("uri")?.Value;
            if (!string.IsNullOrWhiteSpace(uri) && existingUris.Contains(uri))
                continue;

            targetExtensionList.Add(new XElement(sourceExtension));
            if (!string.IsNullOrWhiteSpace(uri))
                existingUris.Add(uri);
            changed = true;
        }

        return changed;
    }

    private static string ElementIdentityKey(XElement element)
    {
        var address = element.Attribute("pane")?.Value
            ?? element.Attribute("sqref")?.Value
            ?? element.Attribute("ref")?.Value
            ?? element.Attribute("r")?.Value
            ?? element.Attribute("activeCell")?.Value
            ?? element.Attribute("name")?.Value
            ?? element.Attribute("id")?.Value
            ?? element.Attribute("uid")?.Value
            ?? element.Attribute("uri")?.Value
            ?? string.Empty;
        return $"{element.Name}\u001f{address}";
    }
}
