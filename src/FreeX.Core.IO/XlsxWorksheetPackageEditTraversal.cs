using System.IO.Compression;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPackageEditTraversal
{
    public static void Edit(
        Stream packageStream,
        Workbook workbook,
        Action<XlsxWorksheetXmlEditSession, Sheet, XlsxWorksheetXmlEdit> editWorksheet)
    {
        XlsxWorkbookWorksheetPathMap? worksheetPathMap;
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);

        Edit(packageStream, workbook, worksheetPathMap, editWorksheet);
    }

    public static void Edit(
        Stream packageStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap,
        Action<XlsxWorksheetXmlEditSession, Sheet, XlsxWorksheetXmlEdit> editWorksheet)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(packageStream, worksheetPathMap);
        Edit(session, workbook, editWorksheet);
    }

    public static void Edit(
        XlsxWorksheetXmlEditSession session,
        Workbook workbook,
        Action<XlsxWorksheetXmlEditSession, Sheet, XlsxWorksheetXmlEdit> editWorksheet)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (session.TryGetWorksheet(sheet, out var edit))
                editWorksheet(session, sheet, edit);
        }
    }
}
