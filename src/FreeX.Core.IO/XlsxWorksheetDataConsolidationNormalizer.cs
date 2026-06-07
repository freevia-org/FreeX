using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDataConsolidationNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly HashSet<string> DataConsolidateAttributes = ["function", "leftLabels", "startLabels", "topLabels", "link"];
    private static readonly HashSet<string> DataReferencesAttributes = ["count"];
    private static readonly HashSet<string> DataReferenceAttributes = ["ref", "name", "sheet"];

    private static readonly HashSet<string> ValidFunctions =
    [
        "average",
        "count",
        "countNums",
        "max",
        "min",
        "product",
        "stdDev",
        "stdDevp",
        "sum",
        "var",
        "varp"
    ];

    public static bool NormalizeElement(XElement dataConsolidate)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(dataConsolidate, DataConsolidateAttributes);
        changed |= RemoveUnexpectedChildren(dataConsolidate, WorksheetNs + "dataRefs");
        changed |= MergeDuplicateDataReferences(dataConsolidate);
        changed |= NormalizeAttribute(dataConsolidate, "function", value => NormalizeToken(value, ValidFunctions));
        changed |= NormalizeAttribute(dataConsolidate, "leftLabels", NormalizeBoolean);
        changed |= NormalizeAttribute(dataConsolidate, "startLabels", NormalizeBoolean);
        changed |= NormalizeAttribute(dataConsolidate, "topLabels", NormalizeBoolean);
        changed |= NormalizeAttribute(dataConsolidate, "link", NormalizeBoolean);

        foreach (var dataRefs in dataConsolidate.Elements(WorksheetNs + "dataRefs"))
        {
            changed |= RemoveUnknownAttributes(dataRefs, DataReferencesAttributes);
            changed |= RemoveUnexpectedChildren(dataRefs, WorksheetNs + "dataRef");
            foreach (var dataRef in dataRefs.Elements(WorksheetNs + "dataRef"))
            {
                changed |= RemoveUnknownDataReferenceAttributes(dataRef);
                changed |= NormalizeRelationshipId(dataRef);
                changed |= RemoveAllNodes(dataRef);
            }

            var count = dataRefs.Elements(WorksheetNs + "dataRef").Count().ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(dataRefs.Attribute("count")?.Value, count, StringComparison.Ordinal))
            {
                dataRefs.SetAttributeValue("count", count);
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeDuplicateDataReferences(XElement dataConsolidate)
    {
        var dataRefs = dataConsolidate.Elements(WorksheetNs + "dataRefs").ToList();
        if (dataRefs.Count <= 1)
            return false;

        var primary = dataRefs[0];
        foreach (var duplicate in dataRefs.Skip(1))
        {
            primary.Add(duplicate.Elements(WorksheetNs + "dataRef").Select(dataRef => new XElement(dataRef)));
            duplicate.Remove();
        }

        return true;
    }

    private static bool RemoveUnexpectedChildren(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().ToList())
        {
            if (child.Name == allowedChildName)
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveUnknownDataReferenceAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && DataReferenceAttributes.Contains(attribute.Name.LocalName)) ||
                attribute.Name == RelationshipNs + "id")
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeRelationshipId(XElement element)
    {
        var relationshipId = element.Attribute(RelationshipNs + "id");
        if (relationshipId is null)
            return false;

        var normalized = relationshipId.Value.Trim();
        if (normalized.Length == 0)
        {
            relationshipId.Remove();
            return true;
        }

        if (string.Equals(relationshipId.Value, normalized, StringComparison.Ordinal))
            return false;

        relationshipId.Value = normalized;
        return true;
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

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    private static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }
}
