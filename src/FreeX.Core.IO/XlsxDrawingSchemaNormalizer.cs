using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxDrawingSchemaNormalizer
{
    private static readonly XNamespace SpreadsheetDrawingNs =
        "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    public static void NormalizePackage(Stream xlsxStream)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        NormalizePackage(archive);
    }

    public static void NormalizePackage(ZipArchive archive)
    {
        foreach (var entry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/drawings/drawing", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var drawingXml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = drawingXml.Root;
            if (root is null ||
                root.Name != SpreadsheetDrawingNs + "wsDr" ||
                !EnsureUniqueDrawingObjectIds(root))
            {
                continue;
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, drawingXml);
        }
    }

    private static bool EnsureUniqueDrawingObjectIds(XElement drawingRoot)
    {
        var objectProperties = drawingRoot
            .Descendants(SpreadsheetDrawingNs + "cNvPr")
            .ToList();
        var usedIds = new HashSet<int>();
        var nextId = objectProperties
            .Select(element => TryReadPositiveInt(element.Attribute("id")?.Value, out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        if (nextId <= 0)
            nextId = 1;

        var changed = false;
        foreach (var objectProperty in objectProperties)
        {
            if (TryReadPositiveInt(objectProperty.Attribute("id")?.Value, out var id) &&
                usedIds.Add(id))
            {
                continue;
            }

            while (!usedIds.Add(nextId))
                nextId++;
            objectProperty.SetAttributeValue("id", nextId.ToString(CultureInfo.InvariantCulture));
            nextId++;
            changed = true;
        }

        return changed;
    }

    private static bool TryReadPositiveInt(string? value, out int id) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0;
}
