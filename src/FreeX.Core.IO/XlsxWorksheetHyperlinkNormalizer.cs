using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetHyperlinkNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> HyperlinkAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ref",
            "location",
            "tooltip",
            "display"
        };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var hyperlinkContainers = worksheetRoot.Elements(WorksheetNs + "hyperlinks").ToList();
        if (hyperlinkContainers.Count == 0)
            return false;

        var changed = false;
        var hyperlinks = hyperlinkContainers[0];
        foreach (var duplicate in hyperlinkContainers.Skip(1))
        {
            hyperlinks.Add(duplicate.Elements(WorksheetNs + "hyperlink").Select(hyperlink => new XElement(hyperlink)));
            duplicate.Remove();
            changed = true;
        }

        changed |= NormalizeElement(hyperlinks);
        if (!hyperlinks.Elements(WorksheetNs + "hyperlink").Any())
        {
            hyperlinks.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement hyperlinks)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(hyperlinks, EmptyAttributes);
        changed |= RemoveUnexpectedChildren(hyperlinks, WorksheetNs + "hyperlink");

        var seenRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hyperlink in hyperlinks.Elements(WorksheetNs + "hyperlink").ToList())
        {
            var normalizedRef = NormalizeReference(hyperlink.Attribute("ref")?.Value);
            if (normalizedRef is null)
            {
                hyperlink.Remove();
                changed = true;
                continue;
            }

            changed |= RemoveUnknownHyperlinkAttributes(hyperlink);
            changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(hyperlink, "ref", normalizedRef);
            changed |= NormalizeRelationshipId(hyperlink);
            changed |= NormalizeLocation(hyperlink);
            if (!HasTarget(hyperlink) || !seenRefs.Add(normalizedRef))
            {
                hyperlink.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(hyperlink);
        }

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool HasTarget(XElement hyperlink) =>
        !string.IsNullOrWhiteSpace(hyperlink.Attribute(RelationshipNs + "id")?.Value) ||
        !string.IsNullOrWhiteSpace(hyperlink.Attribute("location")?.Value);

    private static bool NormalizeRelationshipId(XElement hyperlink)
    {
        var relationshipId = hyperlink.Attribute(RelationshipNs + "id");
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

    private static bool NormalizeLocation(XElement hyperlink)
    {
        var location = hyperlink.Attribute("location");
        if (location is null)
            return false;

        var normalized = location.Value.Trim();
        if (normalized.Length == 0)
        {
            location.Remove();
            return true;
        }

        if (string.Equals(location.Value, normalized, StringComparison.Ordinal))
            return false;

        location.Value = normalized;
        return true;
    }

    private static string? NormalizeReference(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains(' ', StringComparison.Ordinal))
            return null;

        var parts = trimmed.Split(':');
        var sheet = SheetId.New();
        if (parts.Length == 1)
        {
            return CellAddress.TryParse(parts[0], sheet, out var address)
                ? address.ToA1()
                : null;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            var range = new GridRange(start, end);
            return range.Start == range.End
                ? range.Start.ToA1()
                : $"{range.Start.ToA1()}:{range.End.ToA1()}";
        }

        return null;
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

    private static bool RemoveUnknownHyperlinkAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && HyperlinkAttributes.Contains(attribute.Name.LocalName)) ||
                attribute.Name == RelationshipNs + "id")
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
