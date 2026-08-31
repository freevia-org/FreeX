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
        var reservedIds = objectProperties
            .Select(element => TryReadPositiveInt(element.Attribute("id")?.Value, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();
        var nextId = FindNextAvailableId(reservedIds);

        var changed = false;
        foreach (var objectProperty in objectProperties)
        {
            if (TryReadPositiveInt(objectProperty.Attribute("id")?.Value, out var id) &&
                usedIds.Add(id))
            {
                continue;
            }

            usedIds.Add(nextId);
            objectProperty.SetAttributeValue("id", nextId.ToString(CultureInfo.InvariantCulture));
            reservedIds.Add(nextId);
            nextId = FindNextAvailableId(reservedIds);
            changed = true;
        }

        return changed;
    }

    private static bool TryReadPositiveInt(string? value, out int id) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0;

    private static int FindNextAvailableId(IReadOnlySet<int> reservedIds)
    {
        if (reservedIds.Count == 0)
            return 1;

        var maximum = reservedIds.Max();
        if (maximum < int.MaxValue)
            return maximum + 1;

        // A producer-controlled id of Int32.MaxValue used to wrap `maximum + 1` negative. If the
        // drawing also contained a duplicate or invalid id, normalization then emitted another
        // invalid cNvPr id and downstream package readers could terminate the open workflow.
        // Reuse the first positive gap instead; a finite XML document always has one.
        for (var candidate = 1; candidate < int.MaxValue; candidate++)
        {
            if (!reservedIds.Contains(candidate))
                return candidate;
        }

        throw new InvalidDataException("The drawing has exhausted all positive object identifiers.");
    }
}
