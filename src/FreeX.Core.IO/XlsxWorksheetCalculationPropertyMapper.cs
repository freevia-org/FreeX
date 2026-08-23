using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCalculationPropertyMapper
{
    public static bool ReadFullCalculationOnLoad(XElement? sheetCalcPr) =>
        XlsxXmlAttributeReader.ReadBoolAttribute(sheetCalcPr, "fullCalcOnLoad");

    public static void Save(Stream packageStream, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XlsxWorksheetPackageEditTraversal.Edit(packageStream, workbook, (session, sheet, edit) =>
        {
            var root = edit.Root;
            root.Element(workbookNs + "sheetCalcPr")?.Remove();
            if (sheet.FullCalculationOnLoad)
            {
                var sheetCalcPr = new XElement(workbookNs + "sheetCalcPr");
                sheetCalcPr.SetAttributeValue("fullCalcOnLoad", "1");
                XlsxWorksheetElementOrder.Insert(root, sheetCalcPr);
            }

            session.MarkDirty(edit);
        });
    }

}
