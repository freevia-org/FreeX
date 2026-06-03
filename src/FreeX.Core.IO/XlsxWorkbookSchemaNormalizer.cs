using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookSchemaNormalizer
{
    public static void Normalize(Stream xlsxStream)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Normalize(archive);
    }

    public static void Normalize(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        NormalizeWorksheets(archive, workbookNs);

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        if (!NormalizeWorkbook(workbookXml, workbookNs))
            return;

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void NormalizeWorksheets(ZipArchive archive, XNamespace workbookNs)
    {
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var worksheetPaths = archive.Entries
            .Select(entry => entry.FullName)
            .Where(path => path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                           path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var worksheetPath in worksheetPaths)
        {
            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var changed = NormalizeWorksheet(worksheetXml, workbookNs);
            changed |= NormalizeLegacyDrawingHeaderFooterRelationship(
                archive,
                worksheetPath,
                worksheetXml,
                workbookNs,
                relNs,
                packageRelNs);
            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private const string VmlDrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    private static bool NormalizeLegacyDrawingHeaderFooterRelationship(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var legacyDrawingHeaderFooter = worksheetXml.Root?.Element(workbookNs + "legacyDrawingHF");
        if (legacyDrawingHeaderFooter is null)
            return false;

        var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (relsEntry is null)
            return false;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var vmlRelationshipId = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(relationship => string.Equals(
                relationship.Attribute("Type")?.Value,
                VmlDrawingRelationshipType,
                StringComparison.OrdinalIgnoreCase))
            .Select(relationship => relationship.Attribute("Id")?.Value)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (string.IsNullOrWhiteSpace(vmlRelationshipId) ||
            string.Equals(legacyDrawingHeaderFooter.Attribute(relNs + "id")?.Value, vmlRelationshipId, StringComparison.Ordinal))
        {
            return false;
        }

        worksheetXml.Root?.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        legacyDrawingHeaderFooter.SetAttributeValue(relNs + "id", vmlRelationshipId);
        return true;
    }

    internal static bool NormalizeWorkbook(XDocument workbookXml, XNamespace workbookNs)
    {
        var root = workbookXml.Root;
        if (root is null)
            return false;

        var orderedChildren = root.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => WorkbookChildOrder(item.Element, workbookNs))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || root.Elements().SequenceEqual(orderedChildren))
            return false;

        root.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int WorkbookChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "revisionPtr" ? 0 :
        element.Name == workbookNs + "fileVersion" ? 1 :
        element.Name == workbookNs + "fileSharing" ? 2 :
        element.Name == workbookNs + "workbookPr" ? 3 :
        element.Name == workbookNs + "workbookProtection" ? 4 :
        element.Name == workbookNs + "bookViews" ? 5 :
        element.Name == workbookNs + "sheets" ? 6 :
        element.Name == workbookNs + "functionGroups" ? 7 :
        element.Name == workbookNs + "externalReferences" ? 8 :
        element.Name == workbookNs + "definedNames" ? 9 :
        element.Name == workbookNs + "calcPr" ? 10 :
        element.Name == workbookNs + "oleSize" ? 11 :
        element.Name == workbookNs + "customWorkbookViews" ? 12 :
        element.Name == workbookNs + "pivotCaches" ? 13 :
        element.Name == workbookNs + "smartTagPr" ? 14 :
        element.Name == workbookNs + "smartTagTypes" ? 15 :
        element.Name == workbookNs + "webPublishing" ? 16 :
        element.Name == workbookNs + "fileRecoveryPr" ? 17 :
        element.Name == workbookNs + "webPublishObjects" ? 18 :
        element.Name == workbookNs + "extLst" ? 100 :
        90;

    internal static bool NormalizeWorksheet(XDocument worksheetXml, XNamespace workbookNs)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return false;

        var orderedChildren = root.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => WorksheetChildOrder(item.Element, workbookNs))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || root.Elements().SequenceEqual(orderedChildren))
            return false;

        root.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int WorksheetChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "sheetPr" ? 0 :
        element.Name == workbookNs + "dimension" ? 1 :
        element.Name == workbookNs + "sheetViews" ? 2 :
        element.Name == workbookNs + "sheetFormatPr" ? 3 :
        element.Name == workbookNs + "cols" ? 4 :
        element.Name == workbookNs + "sheetData" ? 5 :
        element.Name == workbookNs + "sheetCalcPr" ? 6 :
        element.Name == workbookNs + "sheetProtection" ? 7 :
        element.Name == workbookNs + "protectedRanges" ? 8 :
        element.Name == workbookNs + "scenarios" ? 9 :
        element.Name == workbookNs + "autoFilter" ? 10 :
        element.Name == workbookNs + "sortState" ? 11 :
        element.Name == workbookNs + "dataConsolidate" ? 12 :
        element.Name == workbookNs + "customSheetViews" ? 13 :
        element.Name == workbookNs + "mergeCells" ? 14 :
        element.Name == workbookNs + "phoneticPr" ? 15 :
        element.Name == workbookNs + "conditionalFormatting" ? 16 :
        element.Name == workbookNs + "dataValidations" ? 17 :
        element.Name == workbookNs + "hyperlinks" ? 18 :
        element.Name == workbookNs + "printOptions" ? 19 :
        element.Name == workbookNs + "pageMargins" ? 20 :
        element.Name == workbookNs + "pageSetup" ? 21 :
        element.Name == workbookNs + "headerFooter" ? 22 :
        element.Name == workbookNs + "rowBreaks" ? 23 :
        element.Name == workbookNs + "colBreaks" ? 24 :
        element.Name == workbookNs + "customProperties" ? 25 :
        element.Name == workbookNs + "cellWatches" ? 26 :
        element.Name == workbookNs + "ignoredErrors" ? 27 :
        element.Name == workbookNs + "singleXmlCells" ? 28 :
        element.Name == workbookNs + "smartTags" ? 29 :
        element.Name == workbookNs + "drawing" ? 30 :
        element.Name == workbookNs + "legacyDrawing" ? 31 :
        element.Name == workbookNs + "legacyDrawingHF" ? 32 :
        element.Name == workbookNs + "drawingHF" ? 33 :
        element.Name == workbookNs + "picture" ? 34 :
        element.Name == workbookNs + "oleObjects" ? 35 :
        element.Name == workbookNs + "controls" ? 36 :
        element.Name == workbookNs + "webPublishItems" ? 37 :
        element.Name == workbookNs + "tableParts" ? 38 :
        element.Name == workbookNs + "extLst" ? 100 :
        90;
}
