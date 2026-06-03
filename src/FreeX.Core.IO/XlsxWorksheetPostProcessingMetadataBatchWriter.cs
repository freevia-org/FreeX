using System.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPostProcessingMetadataBatchWriter
{
    public static bool HasReplayMetadata(Sheet sheet) =>
        HasReplayWorksheetElementMetadata(sheet) ||
        XlsxWorksheetPageSetupMetadataWriter.HasModeledPrinterAttributes(sheet);

    public static bool HasWorksheetElementMetadata(Sheet sheet) =>
        HasReplayWorksheetElementMetadata(sheet) ||
        sheet.SingleXmlCells is not null;

    private static bool HasReplayWorksheetElementMetadata(Sheet sheet) =>
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
        if (HasModeledPrinterAttributes(workbook))
            XlsxWorksheetPageSetupMetadataWriter.Save(session, workbook);
    }

    private static bool HasModeledPrinterAttributes(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (XlsxWorksheetPageSetupMetadataWriter.HasModeledPrinterAttributes(sheet))
                return true;
        }

        return false;
    }

    private static void SaveWorksheetElementMetadata(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        XlsxWorksheetSmartTagMapper.Save(session, workbook);
        XlsxWorksheetSortStateMapper.Save(session, workbook);
        XlsxWorksheetAdditionalViewMapper.Save(session, workbook);
        XlsxWorksheetDataConsolidationMapper.Save(session, workbook);
        XlsxWorksheetSingleXmlCellMapper.Save(session, workbook);
    }
}
