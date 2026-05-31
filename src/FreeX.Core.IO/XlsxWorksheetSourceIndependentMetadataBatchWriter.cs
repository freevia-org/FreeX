using System.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSourceIndependentMetadataBatchWriter
{
    public static bool HasMetadata(Sheet sheet) =>
        sheet.AutoFilter is not null ||
        XlsxWorksheetNativeMetadataBatchWriter.HasMetadata(sheet);

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        XlsxWorksheetAutoFilterMapper.Save(session, workbook);
        XlsxWorksheetNativeMetadataBatchWriter.Save(session, workbook);
    }
}
