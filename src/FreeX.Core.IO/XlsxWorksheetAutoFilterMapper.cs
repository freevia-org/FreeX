using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetAutoFilterMapper
{
    public static WorksheetAutoFilterModel? Read(XElement? autoFilter) =>
        XlsxWorksheetAutoFilterXmlMapper.Read(autoFilter);

    public static void MaterializeFilters(Sheet sheet) =>
        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap) =>
        XlsxWorksheetAutoFilterXmlMapper.Save(xlsxStream, workbook, worksheetPathMap);
}
