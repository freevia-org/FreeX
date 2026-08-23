using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Post-processing normalizer for fresh (no-source-package) saves: ensures that every VML
/// note shape's <c>&lt;x:Visible/&gt;</c> element correctly reflects the model's
/// <see cref="Sheet.ShownComments"/> set. Without this, ClosedXML's default VML template may
/// emit <c>&lt;x:Visible/&gt;</c> for all notes regardless of pin state.
/// </summary>
internal static class XlsxLegacyCommentVisibilityNormalizer
{
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    /// <summary>
    /// Scans all worksheet VML drawing parts and rewrites <c>&lt;x:Visible/&gt;</c> elements
    /// so they exactly match the <see cref="Sheet.ShownComments"/> set.
    /// </summary>
    public static void NormalizePackage(Stream packageStream, Workbook workbook)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || workbookRelsEntry is null)
            return;

        XDocument workbookXml;
        try
        {
            workbookXml = OpcXml.LoadXml(workbookEntry);
        }
        catch { return; }

        var relTargets = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var sheetName = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrEmpty(sheetName) || string.IsNullOrEmpty(relId))
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            var sheet = workbook.GetSheet(sheetName!);
            if (sheet is null || sheet.Comments.Count == 0)
                continue;

            NormalizeWorksheetVml(archive, worksheetPath, workbookNs, packageRelNs, sheet);
        }
    }

    private static void NormalizeWorksheetVml(
        ZipArchive archive,
        string worksheetPath,
        XNamespace workbookNs,
        XNamespace packageRelNs,
        Sheet sheet)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        // Find the legacyDrawing rel id.
        XDocument worksheetXml;
        try
        {
            worksheetXml = OpcXml.LoadXml(worksheetEntry);
        }
        catch { return; }

        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var vmlRelId = worksheetXml.Root
            ?.Element(workbookNs + "legacyDrawing")
            ?.Attribute(relNs + "id")
            ?.Value;
        if (string.IsNullOrWhiteSpace(vmlRelId))
            return;

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return;

        XDocument relsXml;
        try
        {
            relsXml = OpcXml.LoadXml(relsEntry);
        }
        catch { return; }

        var vmlTarget = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(rel =>
                string.Equals(rel.Attribute("Id")?.Value, vmlRelId, StringComparison.Ordinal) &&
                string.Equals(rel.Attribute("Type")?.Value, VmlDrawingRelationshipType, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(vmlTarget))
            return;

        var vmlPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, vmlTarget!);
        var vmlEntry = archive.GetEntry(vmlPath);
        if (vmlEntry is null)
            return;

        XDocument vml;
        try
        {
            vml = OpcXml.LoadXml(vmlEntry);
        }
        catch { return; }

        if (vml.Root is null)
            return;

        var modified = false;
        foreach (var shape in vml.Root.Elements(VmlNs + "shape"))
        {
            var clientData = shape.Elements(ExcelVmlNs + "ClientData")
                .FirstOrDefault(cd => string.Equals(
                    cd.Attribute("ObjectType")?.Value, "Note",
                    StringComparison.OrdinalIgnoreCase));
            if (clientData is null)
                continue;

            var rowText = clientData.Element(ExcelVmlNs + "Row")?.Value;
            var colText = clientData.Element(ExcelVmlNs + "Column")?.Value;
            if (!uint.TryParse(rowText, out var row0) || !uint.TryParse(colText, out var col0))
                continue;

            // VML uses 0-based; SheetId from the sheet model.
            var address = new CellAddress(sheet.Id, row0 + 1, col0 + 1);
            var isPinned = sheet.ShownComments.Contains(address);

            var visibleElement = clientData.Element(ExcelVmlNs + "Visible");
            if (isPinned && visibleElement is null)
            {
                clientData.Add(new XElement(ExcelVmlNs + "Visible"));
                modified = true;
            }
            else if (!isPinned && visibleElement is not null)
            {
                visibleElement.Remove();
                modified = true;
            }

            if (XlsxVmlStylePolicy.SetVisibility(shape, isPinned))
                modified = true;
        }

        if (modified)
            XlsxPackageXmlEditor.ReplaceXml(archive, vmlPath, vml);
    }

    /// <summary>
    /// Rewrites (or appends) the <c>visibility:</c> CSS property inside the VML shape's
    /// <c>style</c> attribute so it matches <paramref name="isPinned"/> — <c>visible</c> when
    /// pinned, <c>hidden</c> otherwise — without disturbing any other CSS properties already
    /// present (position, margins, size, z-index, etc). Real Excel treats this CSS property as
    /// the shape's actual paint state, so the ClientData <c>&lt;x:Visible/&gt;</c> flag alone is
    /// not sufficient (see the sibling fix in <see cref="XlsxLegacyCommentPreserver"/>).
    /// </summary>
    /// <returns><see langword="true"/> if the style attribute was changed.</returns>
}
