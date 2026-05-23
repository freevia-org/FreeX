using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetPhoneticPropertyMapper
{
    public static WorksheetPhoneticProperties? Read(XElement? phoneticPr)
    {
        if (phoneticPr is null)
            return null;

        var fontId = phoneticPr.Attribute("fontId")?.Value;
        var type = phoneticPr.Attribute("type")?.Value;
        var alignment = phoneticPr.Attribute("alignment")?.Value;
        return string.IsNullOrWhiteSpace(fontId) && string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(alignment)
            ? null
            : new WorksheetPhoneticProperties(
                string.IsNullOrWhiteSpace(fontId) ? null : fontId,
                string.IsNullOrWhiteSpace(type) ? null : type,
                string.IsNullOrWhiteSpace(alignment) ? null : alignment);
    }

    public static void Save(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var sheetPaths = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in workbook.Sheets)
        {
            if (!sheetPaths.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(workbookNs + "phoneticPr")?.Remove();
            if (sheet.PhoneticProperties is null)
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
                continue;
            }

            var phoneticPr = new XElement(workbookNs + "phoneticPr");
            if (!string.IsNullOrWhiteSpace(sheet.PhoneticProperties.FontId))
                phoneticPr.SetAttributeValue("fontId", sheet.PhoneticProperties.FontId);
            if (!string.IsNullOrWhiteSpace(sheet.PhoneticProperties.Type))
                phoneticPr.SetAttributeValue("type", sheet.PhoneticProperties.Type);
            if (!string.IsNullOrWhiteSpace(sheet.PhoneticProperties.Alignment))
                phoneticPr.SetAttributeValue("alignment", sheet.PhoneticProperties.Alignment);

            if (phoneticPr.HasAttributes)
                InsertPhoneticPropertyInOrder(root, workbookNs, phoneticPr);
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static void InsertPhoneticPropertyInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement phoneticPr)
    {
        string[] laterWorksheetElements =
        [
            "conditionalFormatting",
            "dataValidations",
            "hyperlinks",
            "printOptions",
            "pageMargins",
            "pageSetup",
            "headerFooter",
            "rowBreaks",
            "colBreaks",
            "customProperties",
            "cellWatches",
            "ignoredErrors",
            "smartTags",
            "drawing",
            "legacyDrawing",
            "legacyDrawingHF",
            "picture",
            "oleObjects",
            "controls",
            "webPublishItems",
            "tableParts",
            "extLst"
        ];

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(phoneticPr);
        else
            insertionPoint.AddBeforeSelf(phoneticPr);
    }
}
