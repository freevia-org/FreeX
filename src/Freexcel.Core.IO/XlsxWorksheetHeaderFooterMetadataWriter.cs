using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetHeaderFooterMetadataWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.HeaderFooterMetadata is not null))
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

            var headerFooter = root.Element(worksheetNs + "headerFooter");
            if (headerFooter is null)
            {
                headerFooter = new XElement(worksheetNs + "headerFooter");
                root.Add(headerFooter);
            }

            foreach (var attribute in sheet.HeaderFooterMetadata!.NativeAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || IsModeledHeaderFooterAttribute(attribute.Key))
                    continue;

                TrySetNativeAttribute(headerFooter, attribute.Key, attribute.Value);
            }

            foreach (var childXml in sheet.HeaderFooterMetadata.NativeChildXmls)
            {
                if (string.IsNullOrWhiteSpace(childXml))
                    continue;

                try
                {
                    var child = XElement.Parse(childXml);
                    if (!IsModeledHeaderFooterElement(child.Name.LocalName))
                        headerFooter.Add(child);
                }
                catch
                {
                    // Skip malformed native payloads in authored native JSON files.
                }
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static bool IsModeledHeaderFooterAttribute(string name) =>
        name is "differentOddEven" or "differentFirst" or "scaleWithDoc" or "alignWithMargins";

    private static bool IsModeledHeaderFooterElement(string name) =>
        name is "oddHeader" or "oddFooter" or "evenHeader" or "evenFooter" or "firstHeader" or "firstFooter";

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
