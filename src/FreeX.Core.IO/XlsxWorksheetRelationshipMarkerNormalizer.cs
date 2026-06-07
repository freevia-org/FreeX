using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetRelationshipMarkerNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XName[] MarkerNames =
    [
        WorksheetNs + "drawing",
        WorksheetNs + "legacyDrawing",
        WorksheetNs + "legacyDrawingHF",
        WorksheetNs + "drawingHF",
        WorksheetNs + "picture"
    ];

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

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        foreach (var markerName in MarkerNames)
            changed |= NormalizeMarkerElements(worksheetRoot, markerName);

        return changed;
    }

    private static bool NormalizeMarkerElements(XElement worksheetRoot, XName markerName)
    {
        var changed = false;
        var keptMarker = false;
        foreach (var marker in worksheetRoot.Elements(markerName).ToList())
        {
            changed |= NormalizeMarkerElement(marker);
            if (keptMarker || marker.Attribute(RelNs + "id") is null)
            {
                marker.Remove();
                changed = true;
                continue;
            }

            keptMarker = true;
        }

        return changed;
    }

    private static bool NormalizeMarkerElement(XElement marker)
    {
        var changed = false;
        foreach (var attribute in marker.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name == RelNs + "id")
                continue;

            attribute.Remove();
            changed = true;
        }

        if (marker.Nodes().Any())
        {
            marker.RemoveNodes();
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
