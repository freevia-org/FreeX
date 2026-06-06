using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCellWatchesNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> CellWatchAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "r" };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var cellWatchContainers = worksheetRoot.Elements(WorksheetNs + "cellWatches").ToList();
        if (cellWatchContainers.Count == 0)
            return false;

        var changed = false;
        var keptContainer = false;
        foreach (var cellWatches in cellWatchContainers)
        {
            if (keptContainer)
            {
                cellWatches.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeElement(cellWatches);
            if (!cellWatches.Elements(WorksheetNs + "cellWatch").Any())
            {
                cellWatches.Remove();
                changed = true;
                continue;
            }

            keptContainer = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement cellWatches)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(cellWatches, EmptyAttributes);
        changed |= RemoveUnexpectedChildren(cellWatches, WorksheetNs + "cellWatch");

        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cellWatch in cellWatches.Elements(WorksheetNs + "cellWatch").ToList())
        {
            var normalizedReference = NormalizeCellReference(cellWatch.Attribute("r")?.Value);
            if (normalizedReference is null || !seenReferences.Add(normalizedReference))
            {
                cellWatch.Remove();
                changed = true;
                continue;
            }

            changed |= RemoveUnknownAttributes(cellWatch, CellWatchAttributes);
            changed |= SetAttributeIfChanged(cellWatch, "r", normalizedReference);
            changed |= RemoveAllChildren(cellWatch);
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

    private static bool RemoveAllChildren(XElement element)
    {
        if (!element.HasElements)
            return false;

        element.Elements().Remove();
        return true;
    }

    private static bool SetAttributeIfChanged(XElement element, string attributeName, string value)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static string? NormalizeCellReference(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && CellAddress.TryParse(trimmed, SheetId.New(), out var address)
            ? address.ToA1()
            : null;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
