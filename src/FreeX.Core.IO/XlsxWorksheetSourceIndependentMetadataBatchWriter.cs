using System.IO;
using System.Xml.Linq;
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
        // Excel keeps sheetView/@tabSelected in lockstep with bookViews/@activeTab on every save,
        // regardless of what other worksheet features are in use. XlsxWorkbookMetadataWriter already
        // writes activeTab unconditionally whenever workbook.ActiveSheetIndex is set (see
        // XlsxWorkbookMetadataWriter.HasPostProcessingMetadata); the per-sheet tabSelected sync in
        // XlsxWorksheetPrimaryViewMetadataWriter must run under that exact same condition, or a
        // workbook with no other native worksheet metadata (e.g. any brand-new/never-loaded-from-xlsx
        // workbook, or a loaded one where the user merely switched sheets) never gets its tabSelected
        // repointed and can permanently disagree with activeTab.
        var hasWorksheetNativeMetadata = workbook.ActiveSheetIndex is not null;
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
            SaveAutoFilters(session, workbook, removeMissingAutoFilters: false);
        if (hasDataValidationNativeMetadata)
            XlsxDataValidationNativeMetadataMapper.Save(session, workbook);
        if (hasWorksheetNativeMetadata)
            XlsxWorksheetNativeMetadataBatchWriter.Save(session, workbook);
        else if (hasWorksheetPageBreaks)
            XlsxWorksheetPageBreaksMetadataWriter.Save(session, workbook);
    }

    public static void SaveAutoFilters(
        Stream xlsxStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        SaveAutoFilters(session, workbook, removeMissingAutoFilters: true);
    }

    private static void SaveAutoFilters(
        XlsxWorksheetXmlEditSession session,
        Workbook workbook,
        bool removeMissingAutoFilters)
    {
        // Allocate any missing colour-filter dxfs before writing AutoFilter XML so the
        // filterColumn can reference the freshly allocated dxfId.
        IReadOnlyDictionary<(SheetId, int), int>? colorFilterDxfIds = null;
        if (XlsxAutoFilterColorFilterDxfWriter.HasUnallocatedColorFilters(workbook))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            colorFilterDxfIds = XlsxAutoFilterColorFilterDxfWriter.Save(session.Archive, workbook, workbookNs);
        }

        XlsxWorksheetAutoFilterMapper.Save(
            session,
            workbook,
            colorFilterDxfIds,
            removeMissingAutoFilters);
    }
}
