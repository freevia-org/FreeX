using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookExternalReferencesNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static bool NormalizeWorkbookRoot(XElement workbookRoot, XNamespace workbookNs)
    {
        var changed = false;
        var keptExternalReferences = false;
        var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var externalReferences in workbookRoot.Elements(workbookNs + "externalReferences").ToList())
        {
            if (keptExternalReferences)
            {
                externalReferences.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeExternalReferencesElement(externalReferences, seenRelationshipIds);
            if (ShouldRemoveExternalReferencesElement(externalReferences))
            {
                externalReferences.Remove();
                changed = true;
                continue;
            }

            keptExternalReferences = true;
        }

        return changed;
    }

    public static bool NormalizeExternalReferencesElement(XElement externalReferences)
    {
        var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return NormalizeExternalReferencesElement(externalReferences, seenRelationshipIds);
    }

    public static bool ShouldRemoveExternalReferencesElement(XElement externalReferences) =>
        !externalReferences.Elements(WorkbookNs + "externalReference").Any();

    private static bool NormalizeExternalReferencesElement(
        XElement externalReferences,
        HashSet<string> seenRelationshipIds)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(externalReferences, Array.Empty<XName>());
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(externalReferences, WorkbookNs + "externalReference");

        foreach (var externalReference in externalReferences.Elements(WorkbookNs + "externalReference").ToList())
        {
            changed |= NormalizeExternalReferenceElement(externalReference);
            var relationshipId = externalReference.Attribute(RelationshipNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId) || !seenRelationshipIds.Add(relationshipId))
            {
                externalReference.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeExternalReferenceElement(XElement externalReference)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(externalReference, RelationshipNs + "id");
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(externalReference);
        changed |= NormalizeRelationshipId(externalReference);
        return changed;
    }

    private static bool NormalizeRelationshipId(XElement externalReference)
    {
        var attribute = externalReference.Attribute(RelationshipNs + "id");
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

        externalReference.SetAttributeValue(RelationshipNs + "id", trimmed);
        return true;
    }
}
