using System.Xml.Linq;

namespace Free.Shared.Opc;

internal static class XlsxNativeXmlMerger
{
    /// <summary>
    /// Maximum element nesting merged. The source tree is XML from a user-opened workbook, and this
    /// merge recurses one call frame per level. <see cref="SecureXmlReaderSettings"/> caps total
    /// document size but neither it nor <c>XDocument</c> bounds nesting depth, so without this a
    /// crafted part could drive recursion past the thread stack — and StackOverflowException is
    /// uncatchable, terminating the process rather than failing the save. Real parts nest a handful
    /// of levels; anything past this is not content worth preserving.
    /// </summary>
    private const int MaxMergeDepth = 256;

    public static bool MergeElementNativeAttributesAndChildren(XElement sourceElement, XElement targetElement)
        => MergeElementNativeAttributesAndChildren(sourceElement, targetElement, []);

    public static bool MergeElementNativeAttributesAndChildren(
        XElement sourceElement,
        XElement targetElement,
        IReadOnlyCollection<XName> modeledAttributeNames)
        => MergeElementNativeAttributesAndChildren(sourceElement, targetElement, modeledAttributeNames, 0);

    private static bool MergeElementNativeAttributesAndChildren(
        XElement sourceElement,
        XElement targetElement,
        IReadOnlyCollection<XName> modeledAttributeNames,
        int depth)
    {
        if (depth > MaxMergeDepth)
            return false;

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
            .GroupBy(GetElementIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var sourceChild in sourceElement.Elements())
        {
            var key = GetElementIdentityKey(sourceChild);
            if (existingChildrenByKey.TryGetValue(key, out var targetChild))
            {
                // Children are merged with no modeled-attribute exclusions, as before: the exclusion
                // list applies only to the element the caller named.
                if (MergeElementNativeAttributesAndChildren(sourceChild, targetChild, [], depth + 1))
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
        => MergeExtensionList(sourceExtensionList, targetRoot, workbookNs, null);

    public static bool MergeExtensionList(
        XElement? sourceExtensionList,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlyDictionary<string, string>? relationshipIdMap)
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
            targetRoot.Add(CloneWithReboundRelationshipIds(sourceExtensionList, relationshipIdMap));
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

            targetExtensionList.Add(CloneWithReboundRelationshipIds(sourceExtension, relationshipIdMap));
            if (!string.IsNullOrWhiteSpace(uri))
                existingUris.Add(uri);
            changed = true;
        }

        return changed;
    }

    private static XElement CloneWithReboundRelationshipIds(
        XElement sourceElement,
        IReadOnlyDictionary<string, string>? relationshipIdMap)
    {
        var clone = new XElement(sourceElement);
        if (relationshipIdMap is null || relationshipIdMap.Count == 0)
            return clone;

        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        foreach (var attribute in clone.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.Namespace == relNs))
        {
            if (relationshipIdMap.TryGetValue(attribute.Value, out var replacementId))
                attribute.Value = replacementId;
        }

        return clone;
    }

    internal static string GetElementIdentityKey(XElement element)
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
