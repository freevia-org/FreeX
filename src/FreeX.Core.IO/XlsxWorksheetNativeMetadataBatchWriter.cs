using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetNativeMetadataBatchWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        XlsxWorksheetProtectionMetadataWriter.Save(session, workbook);
        XlsxWorksheetPrintOptionsMetadataWriter.Save(session, workbook);
        XlsxWorksheetDimensionMetadataWriter.Save(session, workbook);
        XlsxWorksheetSheetPropertiesMetadataWriter.Save(session, workbook);
        XlsxWorksheetPrimaryViewMetadataWriter.Save(session, workbook);
        XlsxWorksheetPageMarginsMetadataWriter.Save(session, workbook);
        XlsxWorksheetPageBreaksMetadataWriter.Save(session, workbook);
        XlsxWorksheetHeaderFooterMetadataWriter.Save(session, workbook);
    }
}
