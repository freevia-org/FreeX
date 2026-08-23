using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookPivotCachesNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static bool NormalizeWorkbookRoot(XElement workbookRoot, XNamespace workbookNs)
    {
        var changed = false;
        var keptPivotCaches = false;
        var seenCacheIds = new HashSet<int>();
        var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pivotCaches in workbookRoot.Elements(workbookNs + "pivotCaches").ToList())
        {
            if (keptPivotCaches)
            {
                pivotCaches.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizePivotCachesElement(pivotCaches, seenCacheIds, seenRelationshipIds);
            if (ShouldRemovePivotCachesElement(pivotCaches))
            {
                pivotCaches.Remove();
                changed = true;
                continue;
            }

            keptPivotCaches = true;
        }

        return changed;
    }

    public static bool NormalizePivotCachesElement(XElement pivotCaches)
    {
        var seenCacheIds = new HashSet<int>();
        var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return NormalizePivotCachesElement(pivotCaches, seenCacheIds, seenRelationshipIds);
    }

    public static bool ShouldRemovePivotCachesElement(XElement pivotCaches) =>
        !pivotCaches.Elements(WorkbookNs + "pivotCache").Any();

    private static bool NormalizePivotCachesElement(
        XElement pivotCaches,
        HashSet<int> seenCacheIds,
        HashSet<string> seenRelationshipIds)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(pivotCaches, Array.Empty<XName>());
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(pivotCaches, WorkbookNs + "pivotCache");

        foreach (var pivotCache in pivotCaches.Elements(WorkbookNs + "pivotCache").ToList())
        {
            changed |= NormalizePivotCacheElement(pivotCache);
            var cacheId = NormalizeNonNegativeIntOrNull(pivotCache.Attribute("cacheId")?.Value);
            var relationshipId = pivotCache.Attribute(RelationshipNs + "id")?.Value;
            if (cacheId is null ||
                string.IsNullOrWhiteSpace(relationshipId) ||
                !seenCacheIds.Add(cacheId.Value) ||
                !seenRelationshipIds.Add(relationshipId))
            {
                pivotCache.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizePivotCacheElement(XElement pivotCache)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(
            pivotCache,
            XName.Get("cacheId"),
            RelationshipNs + "id");
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(pivotCache);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pivotCache, "cacheId", NormalizeNonNegativeIntTextOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeRelationshipId(pivotCache, RelationshipNs + "id");
        return changed;
    }

    private static string? NormalizeNonNegativeIntTextOrNull(string? value) =>
        NormalizeNonNegativeIntOrNull(value)?.ToString(CultureInfo.InvariantCulture);

    private static int? NormalizeNonNegativeIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }
}
