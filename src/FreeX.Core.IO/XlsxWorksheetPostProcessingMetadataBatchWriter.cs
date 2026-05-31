using System.IO;
using System.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPostProcessingMetadataBatchWriter
{
    public static bool HasReplayMetadata(Sheet sheet) =>
        HasWorksheetElementMetadata(sheet) ||
        XlsxWorksheetPageSetupMetadataWriter.HasModeledPrinterAttributes(sheet);

    public static bool HasWorksheetElementMetadata(Sheet sheet) =>
        sheet.SmartTags is not null ||
        sheet.SortState is not null ||
        sheet.AdditionalViews is not null ||
        sheet.DataConsolidation is not null;

    public static void SaveWorksheetElementMetadata(
        Stream xlsxStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        SaveWorksheetElementMetadata(session, workbook);
    }

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        SaveWorksheetElementMetadata(session, workbook);
        if (workbook.Sheets.Any(XlsxWorksheetPageSetupMetadataWriter.HasModeledPrinterAttributes))
            XlsxWorksheetPageSetupMetadataWriter.Save(session, workbook);
    }

    private static void SaveWorksheetElementMetadata(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        XlsxWorksheetSmartTagMapper.Save(session, workbook);
        XlsxWorksheetSortStateMapper.Save(session, workbook);
        XlsxWorksheetAdditionalViewMapper.Save(session, workbook);
        XlsxWorksheetDataConsolidationMapper.Save(session, workbook);
    }
}
