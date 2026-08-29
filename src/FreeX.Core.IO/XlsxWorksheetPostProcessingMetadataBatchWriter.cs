using System.IO;
using System.Xml.Linq;
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

        using (var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap))
        {
            SaveWorksheetElementMetadata(session, workbook);
        }

        xlsxStream.Position = 0;
        XlsxWorksheetSingleXmlCellMapper.Save(xlsxStream, workbook, worksheetPathMap);
    }

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using (var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap))
        {
            SaveWorksheetElementMetadata(session, workbook);
            if (HasModeledPrinterAttributes(workbook))
                XlsxWorksheetPageSetupMetadataWriter.Save(session, workbook);
        }

        xlsxStream.Position = 0;
        XlsxWorksheetSingleXmlCellMapper.Save(xlsxStream, workbook, worksheetPathMap);
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

        // R170-freex-autofilter-sort-F2: allocate any missing colour-sort dxfs before writing
        // sortState XML, so the sortCondition can reference the freshly allocated dxfId -- same
        // ordering SaveAutoFilters uses for XlsxAutoFilterColorFilterDxfWriter above.
        if (XlsxSortStateColorDxfWriter.HasUnallocatedSortColors(workbook))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XlsxSortStateColorDxfWriter.Save(session.Archive, workbook, workbookNs);
        }

        XlsxWorksheetSortStateMapper.Save(session, workbook);
        XlsxWorksheetAdditionalViewMapper.Save(session, workbook);
        XlsxWorksheetDataConsolidationMapper.Save(session, workbook);
    }
}
