using System.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSourceIndependentMetadataBatchWriter
{
    public static bool HasMetadata(Sheet sheet) =>
        sheet.AutoFilter is not null ||
        XlsxDataValidationNativeMetadataMapper.HasNativeMetadata(sheet) ||
        XlsxWorksheetPageBreaksMetadataWriter.HasModeledBreaksOrMetadata(sheet) ||
        XlsxWorksheetNativeMetadataBatchWriter.HasMetadata(sheet);

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        var hasAutoFilter = false;
        var hasDataValidationNativeMetadata = false;
        var hasWorksheetPageBreaks = false;
        var hasWorksheetNativeMetadata = false;
        foreach (var sheet in workbook.Sheets)
        {
            hasAutoFilter |= sheet.AutoFilter is not null;
            hasDataValidationNativeMetadata |= XlsxDataValidationNativeMetadataMapper.HasNativeMetadata(sheet);
            hasWorksheetPageBreaks |= XlsxWorksheetPageBreaksMetadataWriter.HasModeledBreaksOrMetadata(sheet);
            hasWorksheetNativeMetadata |= XlsxWorksheetNativeMetadataBatchWriter.HasMetadata(sheet);
            if (hasAutoFilter && hasDataValidationNativeMetadata && hasWorksheetPageBreaks && hasWorksheetNativeMetadata)
                break;
        }

        if (!hasAutoFilter && !hasDataValidationNativeMetadata && !hasWorksheetPageBreaks && !hasWorksheetNativeMetadata)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        if (hasAutoFilter)
            XlsxWorksheetAutoFilterMapper.Save(session, workbook);
        if (hasDataValidationNativeMetadata)
            XlsxDataValidationNativeMetadataMapper.Save(session, workbook);
        if (hasWorksheetNativeMetadata)
            XlsxWorksheetNativeMetadataBatchWriter.Save(session, workbook);
        else if (hasWorksheetPageBreaks)
            XlsxWorksheetPageBreaksMetadataWriter.Save(session, workbook);
    }
}
