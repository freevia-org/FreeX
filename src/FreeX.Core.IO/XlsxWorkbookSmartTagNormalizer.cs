using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Normalizer for workbook.xml smart-tag elements.
/// <c>smartTagPr</c> behavior is declared in <see cref="XlsxWorkbookLeafElementSchemas"/>
/// and driven by the generic normalizer.
/// <c>smartTagTypes</c> retains its dedicated implementation because it has child elements
/// (<c>smartTagType</c>) requiring per-child attribute normalization and pruning of entries
/// missing required <c>namespaceUri</c> and <c>name</c> attributes.
/// </summary>
internal static class XlsxWorkbookSmartTagNormalizer
{
    private static readonly XlsxWorkbookLeafElementSchema SmartTagPrSchema =
        XlsxWorkbookLeafElementSchemas.ByLocalName["smartTagPr"];

    private static readonly HashSet<string> EmptyAttributes = [];

    private static readonly HashSet<string> SmartTagTypeAttributes =
    [
        "namespaceUri",
        "name",
        "url"
    ];

    public static bool NormalizeSmartTagPropertiesElement(XElement smartTagPr) =>
        XlsxWorkbookLeafElementNormalizer.Normalize(smartTagPr, SmartTagPrSchema);

    public static bool NormalizeSmartTagTypesElement(XElement smartTagTypes)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(smartTagTypes, EmptyAttributes);

        foreach (var child in smartTagTypes.Elements().ToList())
        {
            if (child.Name.LocalName != "smartTagType" ||
                child.Name.NamespaceName != smartTagTypes.Name.NamespaceName)
            {
                child.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeSmartTagType(child);
            if (child.Attribute("namespaceUri") is null || child.Attribute("name") is null)
            {
                child.Remove();
                changed = true;
            }
        }

        return changed;
    }

    public static bool ShouldRemoveSmartTagTypesElement(XElement smartTagTypes) =>
        !smartTagTypes
            .Elements(smartTagTypes.Name.Namespace + "smartTagType")
            .Any();

    private static bool NormalizeSmartTagType(XElement smartTagType)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(smartTagType, SmartTagTypeAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(smartTagType);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(smartTagType, "namespaceUri", NormalizeNonEmptyText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(smartTagType, "name", NormalizeNonEmptyText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(smartTagType, "url", XlsxXmlNormalizationHelpers.NormalizeOptionalText);
        return changed;
    }

    private static string? NormalizeNonEmptyText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

}
