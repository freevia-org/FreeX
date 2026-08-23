using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetAutoFilterMapper
{
    public static WorksheetAutoFilterModel? Read(
        XElement? autoFilter,
        IReadOnlyList<CellStyle>? differentialStyles = null) =>
        XlsxWorksheetAutoFilterXmlMapper.Read(autoFilter, differentialStyles);

    public static void MaterializeFilters(Sheet sheet) =>
        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap) =>
        XlsxWorksheetAutoFilterXmlMapper.Save(xlsxStream, workbook, worksheetPathMap);

    internal static void Save(
        XlsxWorksheetXmlEditSession session,
        Workbook workbook,
        IReadOnlyDictionary<(SheetId, int), int>? colorFilterDxfIds = null,
        bool removeMissingAutoFilters = false) =>
        XlsxWorksheetAutoFilterXmlMapper.Save(
            session,
            workbook,
            colorFilterDxfIds,
            removeMissingAutoFilters);
}
