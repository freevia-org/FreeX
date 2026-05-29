using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetProtectionMetadataWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.ProtectionMetadata is not null))
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

            if (!sheet.IsProtected)
            {
                root.Element(worksheetNs + "sheetProtection")?.Remove();
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
                continue;
            }

            var protection = root.Element(worksheetNs + "sheetProtection");
            if (protection is null)
            {
                protection = new XElement(worksheetNs + "sheetProtection");
                InsertSheetProtection(root, worksheetNs, protection);
            }

            var (protAttrs, protChildren) = XmlNativeBagSerializer.Deserialize(sheet.ProtectionMetadata!.Get("sheetProtection"));
            foreach (var attribute in protAttrs)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) ||
                    string.Equals(attribute.Key, "sheet", StringComparison.Ordinal) ||
                    string.Equals(attribute.Key, "password", StringComparison.Ordinal))
                {
                    continue;
                }

                TrySetNativeAttribute(protection, attribute.Key, attribute.Value);
            }

            protection.Elements().Remove();
            foreach (var childXml in protChildren)
            {
                if (string.IsNullOrWhiteSpace(childXml))
                    continue;

                try
                {
                    protection.Add(XElement.Parse(childXml));
                }
                catch
                {
                    // Skip malformed native payloads in authored native JSON files.
                }
            }

            if (sheet.IsProtected)
                protection.SetAttributeValue("sheet", "1");

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static void InsertSheetProtection(XElement root, XNamespace worksheetNs, XElement protection)
    {
        var protectedRanges = root.Element(worksheetNs + "protectedRanges");
        if (protectedRanges is not null)
        {
            protectedRanges.AddBeforeSelf(protection);
            return;
        }

        var sheetData = root.Element(worksheetNs + "sheetData");
        if (sheetData is not null)
        {
            sheetData.AddAfterSelf(protection);
            return;
        }

        root.Add(protection);
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
