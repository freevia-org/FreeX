using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

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

        if (IsOfficeRevisionAttribute(attribute))
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
                IsOfficeRevisionAttribute(attribute) ||
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
            if (attribute.IsNamespaceDeclaration ||
                IsOfficeRevisionAttribute(attribute) ||
                targetElement.Attribute(attribute.Name) is not null)
            {
                continue;
            }

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool IsOfficeRevisionAttribute(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration &&
        string.Equals(attribute.Name.LocalName, "uid", StringComparison.Ordinal) &&
        IsOfficeRevisionNamespace(attribute.Name.NamespaceName);

    // A source row's `s` style index and its companion `customFormat` flag reference the source
    // stylesheet's cellXfs index space. The full-save path rebuilds styles.xml via ClosedXML, which
    // renumbers (and usually shrinks) cellXfs, so copying the source index verbatim onto the rebuilt
    // row produces a reference into the wrong — and possibly out-of-range — index space. An
    // out-of-range index crashes FreeX's own reload (ClosedXML LoadStyle -> ElementAt). These two
    // attributes are intentionally not preserved by the row-attribute merge.
    private static bool IsStylesheetIndexRowAttribute(XAttribute attribute) =>
        attribute.Name.Namespace == XNamespace.None &&
        attribute.Name.LocalName is "s" or "customFormat";

    private static bool IsOfficeRevisionNamespace(string namespaceName) =>
        namespaceName.StartsWith("http://schemas.microsoft.com/office/spreadsheetml/", StringComparison.Ordinal) &&
        namespaceName.Contains("/revision", StringComparison.Ordinal);

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

    // "sheet"/"password" are modeled directly on Sheet.IsProtected/Sheet.ProtectionPassword, every
    // permission boolean is modeled via Sheet.ProtectionPermissions, and the modern ISO 29500 hash
    // quartet (algorithmName/hashValue/saltValue/spinCount) is carried verbatim through
    // Sheet.ProtectionMetadata (see XlsxWorksheetLayoutMetadataReader.ReadWorksheetProtectionMetadata
    // / XlsxWorksheetProtectionMetadataWriter.Save) -- all model-governed, none of it may be blindly
    // copied back from the stale pre-edit source element once a target sheetProtection element
    // already exists (that would resurrect a revoked password's modern-hash verifier alongside, or
    // instead of, a freshly-set legacy password; see
    // FreeXR11B7Tests.ProtectSheetCommand_AfterUnprotectingModernHashSheet_DropsStaleVerifierForOldPassword).
    private static readonly XName[] ModeledSheetProtectionAttributes =
    [
        "sheet",
        "password",
        "algorithmName",
        "hashValue",
        "saltValue",
        "spinCount",
        .. XlsxSheetProtectionPermissionMapper.AttributeNames.Select(name => (XName)name)
    ];

    private static bool MergeWorksheetSheetProtection(
        XElement? sourceSheetProtection,
        XElement targetRoot,
        XNamespace workbookNs,
        Sheet? sheet)
    {
        if (sourceSheetProtection is null)
            return false;

        if (sheet is { IsProtected: false })
            return false;

        var targetSheetProtection = targetRoot.Element(workbookNs + "sheetProtection");
        if (targetSheetProtection is null)
        {
            var clone = new XElement(sourceSheetProtection);
            foreach (var attributeName in ModeledSheetProtectionAttributes)
                clone.Attribute(attributeName)?.Remove();

            if (!clone.HasAttributes && !clone.HasElements)
                return false;

            targetRoot.Add(clone);
            return true;
        }

        return XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(
            sourceSheetProtection,
            targetSheetProtection,
            ModeledSheetProtectionAttributes);
    }

    private static bool MergeMissingNativeChildren(
        XElement sourceElement,
        XElement targetElement,
        Func<XElement, bool> shouldRetain)
    {
        var existingChildrenByKey = targetElement
            .Elements()
            .Where(shouldRetain)
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
