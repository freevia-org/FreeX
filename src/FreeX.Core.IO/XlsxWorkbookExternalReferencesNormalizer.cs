using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookExternalReferencesNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <param name="reservedRelationshipIdsForOrdinalPreservation">
    /// R99-io-external-references-patch-normalizer-1: when non-null, an externalReference whose
    /// r:id is blank, missing, or a duplicate is NOT removed. Excel's '[n]' bracket-index formula
    /// syntax addresses external references by their fixed 1-based position in workbook.xml's
    /// &lt;externalReference&gt; list -- dropping an earlier slot silently renumbers every later
    /// externalReference down by one, so a formula like '[3]Sheet1'!A1 that correctly addressed the
    /// third external workbook would start addressing whatever became the new third entry. Instead,
    /// the slot is reserved: a freshly minted r:id guaranteed unused (both within this document and
    /// within the caller-supplied reserved-id pool, typically every id already used in
    /// xl/_rels/workbook.xml.rels) is assigned and the element is deliberately left unbacked by any
    /// Relationship -- mirroring the placeholder logic in XlsxExternalLinkReferencePreserver (the
    /// full ClosedXML-rebuild save path) and XlsxExternalLinkMetadataReader (the read path). Pass
    /// null (the default) to keep the legacy remove-the-element behavior, used by callers that feed
    /// a disposable in-memory copy (e.g. the pre-ClosedXML-load sanitizer) where a separate mechanism
    /// already re-injects the placeholders into the real output.
    /// </param>
    public static bool NormalizeWorkbookRoot(
        XElement workbookRoot,
        XNamespace workbookNs,
        IReadOnlyCollection<string>? reservedRelationshipIdsForOrdinalPreservation = null)
    {
        var changed = false;
        var keptExternalReferences = false;
        var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mintPool = reservedRelationshipIdsForOrdinalPreservation is null
            ? null
            : new HashSet<string>(reservedRelationshipIdsForOrdinalPreservation, StringComparer.OrdinalIgnoreCase);
        foreach (var externalReferences in workbookRoot.Elements(workbookNs + "externalReferences").ToList())
        {
            if (keptExternalReferences)
            {
                externalReferences.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeExternalReferencesElement(externalReferences, seenRelationshipIds, mintPool);
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
        return NormalizeExternalReferencesElement(externalReferences, seenRelationshipIds, mintPool: null);
    }

    public static bool ShouldRemoveExternalReferencesElement(XElement externalReferences) =>
        !externalReferences.Elements(WorkbookNs + "externalReference").Any();

    private static bool NormalizeExternalReferencesElement(
        XElement externalReferences,
        HashSet<string> seenRelationshipIds,
        HashSet<string>? mintPool)
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
                if (mintPool is not null)
                {
                    var placeholderId = MintPlaceholderRelationshipId(mintPool);
                    externalReference.SetAttributeValue(RelationshipNs + "id", placeholderId);
                    seenRelationshipIds.Add(placeholderId);
                }
                else
                {
                    externalReference.Remove();
                }

                changed = true;
            }
            else
            {
                mintPool?.Add(relationshipId!);
            }
        }

        return changed;
    }

    private static string MintPlaceholderRelationshipId(HashSet<string> reservedIds)
    {
        var index = 1;
        var candidate = $"rId{index}";
        while (!reservedIds.Add(candidate))
        {
            index++;
            candidate = $"rId{index}";
        }

        return candidate;
    }

    private static bool NormalizeExternalReferenceElement(XElement externalReference)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(externalReference, RelationshipNs + "id");
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(externalReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeRelationshipId(externalReference, RelationshipNs + "id");
        return changed;
    }

}
