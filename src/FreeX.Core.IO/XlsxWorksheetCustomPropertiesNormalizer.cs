using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCustomPropertiesNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> CustomPropertyAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "name", "id" };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var customPropertyContainers = worksheetRoot.Elements(WorksheetNs + "customProperties").ToList();
        if (customPropertyContainers.Count == 0)
            return false;

        var changed = false;
        var customProperties = customPropertyContainers[0];
        foreach (var duplicate in customPropertyContainers.Skip(1))
        {
            customProperties.Add(duplicate.Elements(WorksheetNs + "customPr").Select(property => new XElement(property)));
            duplicate.Remove();
            changed = true;
        }

        changed |= NormalizeElement(customProperties);
        if (!customProperties.Elements(WorksheetNs + "customPr").Any())
        {
            customProperties.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement customProperties)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(customProperties, EmptyAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(customProperties, WorksheetNs + "customPr");

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var customProperty in customProperties.Elements(WorksheetNs + "customPr").ToList())
        {
            var name = customProperty.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                !HasValidPropertyId(customProperty) ||
                !seenNames.Add(name))
            {
                customProperty.Remove();
                changed = true;
                continue;
            }

            changed |= RemoveUnknownCustomPropertyAttributes(customProperty);
            changed |= NormalizeLegacyId(customProperty);
            changed |= XlsxXmlNormalizationHelpers.NormalizeRelationshipId(customProperty, RelationshipNs + "id");
            changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(customProperty);
        }

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool HasValidPropertyId(XElement customProperty) =>
        IsPositiveInteger(customProperty.Attribute("id")?.Value) ||
        !string.IsNullOrWhiteSpace(customProperty.Attribute(RelationshipNs + "id")?.Value);

    private static bool IsPositiveInteger(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id) &&
        id > 0;

    private static bool NormalizeLegacyId(XElement element)
    {
        var id = element.Attribute("id");
        if (id is null)
            return false;

        if (!IsPositiveInteger(id.Value))
        {
            id.Remove();
            return true;
        }

        var normalized = int.Parse(id.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (string.Equals(id.Value, normalized, StringComparison.Ordinal))
            return false;

        id.Value = normalized;
        return true;
    }

    private static bool RemoveUnknownCustomPropertyAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && CustomPropertyAttributes.Contains(attribute.Name.LocalName)) ||
                attribute.Name == RelationshipNs + "id")
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

}
