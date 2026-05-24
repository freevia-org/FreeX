using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    private static bool MergeWorksheetNativeOnlyElementAttributes(
        XElement? sourceElement,
        XElement targetRoot,
        XName elementName,
        HashSet<string> modeledAttributeNames)
    {
        if (sourceElement is null)
            return false;

        var retainedAttributes = sourceElement
            .Attributes()
            .Where(attribute => IsNativeOnlyWorksheetAttribute(attribute, modeledAttributeNames))
            .Select(attribute => new XAttribute(attribute))
            .ToList();
        var retainedChildren = sourceElement
            .Elements()
            .Select(element => new XElement(element))
            .ToList();
        if (retainedAttributes.Count == 0 && retainedChildren.Count == 0)
            return false;

        var targetElement = targetRoot.Element(elementName);
        if (targetElement is null)
        {
            targetRoot.Add(new XElement(elementName, retainedAttributes, retainedChildren));
            return true;
        }

        var changed = false;
        foreach (var attribute in retainedAttributes)
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildrenByKey = targetElement
            .Elements()
            .GroupBy(ElementIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var child in retainedChildren)
        {
            var key = ElementIdentityKey(child);
            if (existingChildrenByKey.ContainsKey(key))
                continue;

            targetElement.Add(child);
            existingChildrenByKey[key] = child;
            changed = true;
        }

        return changed;
    }

    private static bool IsNativeOnlyWorksheetAttribute(XAttribute attribute, HashSet<string> modeledAttributeNames)
    {
        if (attribute.IsNamespaceDeclaration)
            return false;

        if (attribute.Name.NamespaceName.Length == 0 &&
            modeledAttributeNames.Contains(attribute.Name.LocalName))
        {
            return false;
        }

        return attribute.Name != XName.Get(
            "id",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
    }

    private static bool MergeMissingAttributes(
        XElement sourceElement,
        XElement targetElement,
        IReadOnlyCollection<string> excludedLocalNames)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                excludedLocalNames.Contains(attribute.Name.LocalName, StringComparer.Ordinal) ||
                targetElement.Attribute(attribute.Name) is not null)
            {
                continue;
            }

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool MergeMissingAttributes(XElement sourceElement, XElement targetElement)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
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

    private static bool MergeWorksheetSheetProtection(XElement? sourceSheetProtection, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetProtection is null)
            return false;

        var targetSheetProtection = targetRoot.Element(workbookNs + "sheetProtection");
        if (targetSheetProtection is null)
        {
            targetRoot.Add(new XElement(sourceSheetProtection));
            return true;
        }

        return XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(
            sourceSheetProtection,
            targetSheetProtection);
    }

    private static bool MergeMissingNativeChildren(
        XElement sourceElement,
        XElement targetElement,
        Func<XElement, bool> shouldRetain)
    {
        var existingChildrenByKey = targetElement
            .Elements()
            .GroupBy(ElementIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var changed = false;
        foreach (var sourceChild in sourceElement.Elements().Where(shouldRetain))
        {
            var key = ElementIdentityKey(sourceChild);
            if (existingChildrenByKey.TryGetValue(key, out var targetChild))
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetElement.Add(new XElement(sourceChild));
            existingChildrenByKey[key] = targetElement.Elements().Last();
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetProperties(XElement? sourceSheetProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetProperties is null)
            return false;

        var targetSheetProperties = targetRoot.Element(workbookNs + "sheetPr");
        if (targetSheetProperties is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetProperties));
            return true;
        }

        return XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(
            sourceSheetProperties,
            targetSheetProperties);
    }
}
