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
        changed |= RemoveUnknownAttributes(pivotCaches, allowCacheId: false, allowRelationshipId: false);
        changed |= RemoveUnexpectedChildElements(pivotCaches, WorkbookNs + "pivotCache");

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
        changed |= RemoveUnknownAttributes(pivotCache, allowCacheId: true, allowRelationshipId: true);
        changed |= RemoveAllNodes(pivotCache);
        changed |= NormalizeAttribute(pivotCache, "cacheId", NormalizeNonNegativeIntTextOrNull);
        changed |= NormalizeRelationshipId(pivotCache);
        return changed;
    }

    private static bool RemoveUnknownAttributes(
        XElement element,
        bool allowCacheId,
        bool allowRelationshipId)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (allowCacheId && attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "cacheId") ||
                (allowRelationshipId && attribute.Name == RelationshipNs + "id"))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveUnexpectedChildElements(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().Where(child => child.Name != allowedChildName).ToList())
        {
            child.Remove();
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

        if (attribute is not null && string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, normalized);
        return true;
    }

    private static bool NormalizeRelationshipId(XElement pivotCache)
    {
        var attribute = pivotCache.Attribute(RelationshipNs + "id");
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

        pivotCache.SetAttributeValue(RelationshipNs + "id", trimmed);
        return true;
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
