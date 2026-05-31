using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetNativeMetadataBatchWriter
{
    public static bool HasMetadata(Sheet sheet) =>
        sheet.ProtectionMetadata is not null ||
        sheet.PrintOptionsMetadata is not null ||
        sheet.DimensionMetadata is not null ||
        sheet.SheetPropertiesMetadata is not null ||
        sheet.PrimaryViewMetadata is not null ||
        sheet.PageMarginsMetadata is not null ||
        sheet.RowPageBreaksMetadata is not null ||
        sheet.ColumnPageBreaksMetadata is not null ||
        sheet.HeaderFooterMetadata is not null;

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
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
