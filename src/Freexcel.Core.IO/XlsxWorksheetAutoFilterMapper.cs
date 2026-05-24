using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetAutoFilterMapper
{
    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.AutoFilter is not null))
        {
            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(worksheetNs + "autoFilter")?.Remove();
            if (ToAutoFilterXml(sheet.AutoFilter, worksheetNs) is { } autoFilter)
                InsertAutoFilter(root, worksheetNs, autoFilter);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static XElement? ToAutoFilterXml(WorksheetAutoFilterModel? autoFilter, XNamespace worksheetNs)
    {
        if (autoFilter is null)
            return null;

        if (!string.IsNullOrWhiteSpace(autoFilter.NativeXml))
        {
            try
            {
                var element = XElement.Parse(autoFilter.NativeXml);
                return element.Name == worksheetNs + "autoFilter" ? element : null;
            }
            catch
            {
                // Fall back to a range-only AutoFilter when legacy native JSON contains malformed XML.
            }
        }

        return string.IsNullOrWhiteSpace(autoFilter.Reference)
            ? null
            : new XElement(worksheetNs + "autoFilter", new XAttribute("ref", autoFilter.Reference));
    }

    private static void InsertAutoFilter(XElement root, XNamespace worksheetNs, XElement autoFilter)
    {
        var insertionPoint = root.Element(worksheetNs + "mergeCells") ??
            root.Element(worksheetNs + "phoneticPr") ??
            root.Element(worksheetNs + "conditionalFormatting") ??
            root.Element(worksheetNs + "dataValidations") ??
            root.Element(worksheetNs + "hyperlinks") ??
            root.Element(worksheetNs + "printOptions") ??
            root.Element(worksheetNs + "pageMargins") ??
            root.Element(worksheetNs + "pageSetup") ??
            root.Element(worksheetNs + "headerFooter");
        if (insertionPoint is not null)
            insertionPoint.AddAfterSelf(autoFilter);
        else
            root.Add(autoFilter);
    }
}
