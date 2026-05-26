using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetPageMarginsMetadataWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.PageMarginsMetadata is not null))
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

            var pageMargins = root.Element(worksheetNs + "pageMargins");
            if (pageMargins is null)
            {
                pageMargins = new XElement(worksheetNs + "pageMargins");
                InsertPageMargins(root, worksheetNs, pageMargins);
            }

            foreach (var attribute in sheet.PageMarginsMetadata!.NativeAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || IsModeledPageMarginsAttribute(attribute.Key))
                    continue;

                TrySetNativeAttribute(pageMargins, attribute.Key, attribute.Value);
            }

            if (sheet.PageMarginsMetadata.NativeChildXmls.Count > 0)
            {
                pageMargins.Elements().Remove();
                foreach (var childXml in sheet.PageMarginsMetadata.NativeChildXmls)
                {
                    if (string.IsNullOrWhiteSpace(childXml))
                        continue;

                    try
                    {
                        pageMargins.Add(XElement.Parse(childXml));
                    }
                    catch
                    {
                        // Skip malformed native payloads in authored native JSON files.
                    }
                }
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static bool IsModeledPageMarginsAttribute(string name) =>
        name is "left" or "right" or "top" or "bottom" or "header" or "footer";

    private static void InsertPageMargins(XElement root, XNamespace worksheetNs, XElement pageMargins)
    {
        var pageSetup = root.Element(worksheetNs + "pageSetup");
        if (pageSetup is not null)
        {
            pageSetup.AddBeforeSelf(pageMargins);
            return;
        }

        var printOptions = root.Element(worksheetNs + "printOptions");
        if (printOptions is not null)
        {
            printOptions.AddAfterSelf(pageMargins);
            return;
        }

        root.Add(pageMargins);
    }

    private static bool TrySetNativeAttribute(XElement element, string name, string value)
    {
        try
        {
            element.SetAttributeValue(XName.Get(name), value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
