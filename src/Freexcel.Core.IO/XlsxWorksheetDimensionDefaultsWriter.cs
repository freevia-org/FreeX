using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetDimensionDefaultsWriter
{
    public static bool HasNonDefaultDimensions(Sheet sheet) =>
        IsNonDefaultColumnWidth(sheet.DefaultColumnWidth) ||
        IsNonDefaultRowHeight(sheet.DefaultRowHeight);

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

        foreach (var sheet in workbook.Sheets.Where(HasNonDefaultDimensions))
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

            var sheetFormat = root.Element(workbookNs + "sheetFormatPr");
            if (sheetFormat is null)
            {
                sheetFormat = new XElement(workbookNs + "sheetFormatPr");
                root.AddFirst(sheetFormat);
            }

            if (IsNonDefaultColumnWidth(sheet.DefaultColumnWidth))
                sheetFormat.SetAttributeValue("defaultColWidth", FormatDouble(sheet.DefaultColumnWidth));

            if (IsNonDefaultRowHeight(sheet.DefaultRowHeight))
            {
                sheetFormat.SetAttributeValue("defaultRowHeight", FormatDouble(sheet.DefaultRowHeight * (72.0 / 96.0)));
                sheetFormat.SetAttributeValue("customHeight", "1");
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static bool IsNonDefaultColumnWidth(double value) =>
        double.IsFinite(value) && value > 0 && Math.Abs(value - 8.43) >= 0.01;

    private static bool IsNonDefaultRowHeight(double value) =>
        double.IsFinite(value) && value > 0 && Math.Abs(value - 20.0) >= 0.01;

    private static string FormatDouble(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);
}
