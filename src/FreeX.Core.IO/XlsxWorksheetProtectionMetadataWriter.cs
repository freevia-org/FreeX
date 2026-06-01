using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetProtectionMetadataWriter
{
    public static bool HasProtectionState(Sheet sheet) =>
        sheet.ProtectionMetadata is not null ||
        sheet.IsProtected && !string.IsNullOrWhiteSpace(sheet.ProtectionPassword);

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets)
        {
            var metadata = sheet.ProtectionMetadata;
            var hasProtectionPassword = !string.IsNullOrWhiteSpace(sheet.ProtectionPassword);
            if (metadata is null)
            {
                if (!hasProtectionPassword)
                    continue;
            }

            if (!session.TryGetWorksheet(sheet, out var worksheetEdit))
                continue;

            var root = worksheetEdit.Root;
            if (!sheet.IsProtected)
            {
                root.Element(worksheetNs + "sheetProtection")?.Remove();
                session.MarkDirty(worksheetEdit);
                continue;
            }

            var protection = root.Element(worksheetNs + "sheetProtection");
            if (protection is null)
            {
                protection = new XElement(worksheetNs + "sheetProtection");
                InsertSheetProtection(root, worksheetNs, protection);
            }

            var hasAdvancedHash = false;
            if (metadata is not null)
            {
                var (protAttrs, protChildren) = XmlNativeBagSerializer.Deserialize(metadata.Get("sheetProtection"));
                hasAdvancedHash = protAttrs.ContainsKey("hashValue");
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
            }

            if (hasAdvancedHash)
                protection.Attribute("password")?.Remove();
            else if (hasProtectionPassword)
                protection.SetAttributeValue(
                    "password",
                    XlsxWorkbookMetadataXmlHelper.ToLegacyPasswordHash(sheet.ProtectionPassword!));

            if (sheet.IsProtected)
                protection.SetAttributeValue("sheet", "1");

            session.MarkDirty(worksheetEdit);
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
