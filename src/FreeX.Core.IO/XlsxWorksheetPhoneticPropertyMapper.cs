using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XlsxWorksheetPackageEditTraversal.Edit(packageStream, workbook, (session, sheet, edit) =>
        {
            var root = edit.Root;
            root.Element(workbookNs + "phoneticPr")?.Remove();
            if (sheet.PhoneticProperties is not null)
            {
                var phoneticPr = new XElement(workbookNs + "phoneticPr");
                if (!string.IsNullOrWhiteSpace(sheet.PhoneticProperties.FontId))
                    phoneticPr.SetAttributeValue("fontId", sheet.PhoneticProperties.FontId);
                if (!string.IsNullOrWhiteSpace(sheet.PhoneticProperties.Type))
                    phoneticPr.SetAttributeValue("type", sheet.PhoneticProperties.Type);
                if (!string.IsNullOrWhiteSpace(sheet.PhoneticProperties.Alignment))
                    phoneticPr.SetAttributeValue("alignment", sheet.PhoneticProperties.Alignment);

                XlsxWorksheetPhoneticPropertyNormalizer.NormalizeElement(phoneticPr);
                if (phoneticPr.HasAttributes)
                    XlsxWorksheetElementOrder.Insert(root, phoneticPr);
            }

            session.MarkDirty(edit);
        });
    }

}
