using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Reads the VML drawing for a worksheet and returns the 1-based (row, col) addresses of note
/// shapes that carry an <c>&lt;x:Visible/&gt;</c> element in their ClientData — i.e. notes
/// whose comment box is pinned open ("Show Comment" in Excel).
/// </summary>
internal static class XlsxWorksheetCommentVisibilityReader
{
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    /// <summary>
    /// Returns the 1-based (row, col) addresses of notes whose VML ClientData contains
    /// <c>&lt;x:Visible/&gt;</c>. Returns an empty list if there is no VML or none are pinned.
    /// </summary>
    public static IReadOnlyList<(uint Row, uint Col)> Read(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml,
        XNamespace worksheetNs)
    {
        try
        {
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            // Resolve the VML drawing relationship.
            var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
            var relsEntry = archive.GetEntry(relsPath);
            if (relsEntry is null)
                return [];

            XDocument relsXml;
            try
            {
                relsXml = OpcXml.LoadXml(relsEntry);
            }
            catch
            {
                return [];
            }

            var vmlRelId = worksheetXml.Root
                ?.Element(worksheetNs + "legacyDrawing")
                ?.Attribute("{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id")
                ?.Value;
            if (string.IsNullOrWhiteSpace(vmlRelId))
                return [];

            var vmlTarget = relsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(rel =>
                    string.Equals(rel.Attribute("Id")?.Value, vmlRelId, StringComparison.Ordinal) &&
                    string.Equals(rel.Attribute("Type")?.Value, VmlDrawingRelationshipType, StringComparison.OrdinalIgnoreCase))
                ?.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(vmlTarget))
                return [];

            var vmlPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, vmlTarget);
            var vmlEntry = archive.GetEntry(vmlPath);
            if (vmlEntry is null)
                return [];

            XDocument vml;
            try
            {
                vml = OpcXml.LoadXml(vmlEntry);
            }
            catch
            {
                return [];
            }

            if (vml.Root is null)
                return [];

            List<(uint Row, uint Col)>? result = null;
            foreach (var shape in vml.Root.Elements(VmlNs + "shape"))
            {
                var clientData = shape.Elements(ExcelVmlNs + "ClientData")
                    .FirstOrDefault(cd => string.Equals(
                        cd.Attribute("ObjectType")?.Value, "Note",
                        StringComparison.OrdinalIgnoreCase));
                if (clientData is null)
                    continue;

                // Check for <x:Visible/> element.
                var hasVisible = clientData.Element(ExcelVmlNs + "Visible") is not null;
                if (!hasVisible)
                    continue;

                var rowText = clientData.Element(ExcelVmlNs + "Row")?.Value;
                var colText = clientData.Element(ExcelVmlNs + "Column")?.Value;
                if (!uint.TryParse(rowText, out var row0) || !uint.TryParse(colText, out var col0))
                    continue;

                // VML uses 0-based; CellAddress uses 1-based.
                result ??= [];
                result.Add((row0 + 1, col0 + 1));
            }

            return result ?? [];
        }
        catch
        {
            return [];
        }
    }
}
