using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCodeNameWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XlsxWorksheetPackageEditTraversal.EditSourceMapped(
            xlsxStream,
            workbook,
            sheet => !string.IsNullOrWhiteSpace(sheet.CodeName),
            (sheet, root) =>
        {
            var sheetPr = root.Element(workbookNs + "sheetPr");
            if (sheetPr is null)
            {
                sheetPr = new XElement(workbookNs + "sheetPr");
                root.AddFirst(sheetPr);
            }

            sheetPr.SetAttributeValue("codeName", sheet.CodeName);
        });
    }
}
