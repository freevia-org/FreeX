using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Wpf;
using FreeX.Core.Calc;
using FreeX.App.Presentation.Consolidate;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const double ScreenshotTourCaptureHeight = 300;
    private const string ScreenshotTourTableName = "TourTable";
    private const string ScreenshotTourPivotTableName = "TourPivotTable";
    private const string ScreenshotTourChartName = "TourChart";
    private const string RibbonScreenshotTourManifestFileName = "ribbon_screenshot_tour_manifest.json";
    private const string AutoFilterFlyoutTourManifestFileName = "autofilter_flyout_tour_manifest.json";
    private const string AutoFilterFlyoutTourCaptureFileName = "freex_table_autofilter_dropdown";
    private const string HomeNumberFormatDropdownTourManifestFileName = "home_number_format_dropdown_tour_manifest.json";
    private const string HomeNumberFormatDropdownTourCaptureFileName = "freex_dropdown_home_number_format_opened";
    private const string HomeAlignmentNumberTourManifestFileName = "home_alignment_number_tour_manifest.json";
    private const string HomeAlignmentNumberTourOutputDirectoryName = "home-alignment-number-tour";
    private const string HomeBordersDropdownTourManifestFileName = "home_borders_dropdown_tour_manifest.json";
    private const string HomeBordersDropdownTourCaptureFileName = "freex_dropdown_home_borders_opened";
    private const string HomeFontColorsTourManifestFileName = "home_font_colors_tour_manifest.json";
    private const string HomeFontColorsTourOutputDirectoryName = "home-font-colors-tour";
    private const string HomeStylesConditionalFormattingTourManifestFileName = "home_styles_conditional_formatting_tour_manifest.json";
    private const string HomeStylesConditionalFormattingTourOutputDirectoryName = "home-styles-cf-tour";
    private const string HomeClipboardCellsEditingTourManifestFileName = "home_clipboard_cells_editing_tour_manifest.json";
    private const string HomeClipboardCellsEditingTourOutputDirectoryName = "home-clipboard-cells-editing-tour";
    private const string HomeSubmittedWorkflowsTourManifestFileName = "home_submitted_workflows_tour_manifest.json";
    private const string HomeSubmittedWorkflowsTourOutputDirectoryName = "home-submitted-workflows-tour";
    private const string HomeStylePersistenceTourManifestFileName = "home_style_persistence_tour_manifest.json";
    private const string HomeStylePersistenceTourOutputDirectoryName = "home-style-persistence-tour";
    private const string HomeStylePersistenceTourSavedWorkbookFileName = "freex_home_style_persistence_saved.fxl";
    private const string RibbonOverflowKeytipTourManifestFileName = "ribbon_overflow_keytip_tour_manifest.json";
    private const string RibbonOverflowKeytipTourOutputDirectoryName = "ribbon-overflow-keytip-tour";
    private const string WorksheetContextMenuTourManifestFileName = "worksheet_context_menu_tour_manifest.json";
    private const string WorksheetContextMenuTourCaptureFileName = "freex_context_menu_worksheet_cell_opened";
    private const string WorksheetContextTargetsTourOutputDirectoryName = "worksheet-context-targets-tour";
    private const string WorksheetContextSubmittedTourOutputDirectoryName = "worksheet-context-submitted-tour";
    private const string WorksheetContextSubmittedTourSavedWorkbookFileName = "freex_worksheet_context_submitted_saved.fxl";
    private const string KeyTipOverlayTourManifestFileName = "keytip_overlay_tour_manifest.json";
    private const string PrintPreviewTourManifestFileName = "print_preview_tour_manifest.json";
    private const string BackstageRecentExportShareTourManifestFileName = "backstage_recent_export_share_tour_manifest.json";
    private const string BackstageRecentExportShareTourOutputDirectoryName = "backstage-recent-export-share-tour";
    private const string BackstageRecentExportShareTourSavedWorkbookFileName = "freex_backstage_share_ready_saved.xlsx";
    private const string OptionsAccountTourManifestFileName = "options_account_tour_manifest.json";
    private const string OptionsAccountTourOutputDirectoryName = "options-account-tour";
    private const string HelpAboutLegalTourManifestFileName = "help_about_legal_tour_manifest.json";
    private const string HelpAboutLegalTourOutputDirectoryName = "help-about-legal-tour";
    private const string QatUndoRedoTourManifestFileName = "qat_undo_redo_tour_manifest.json";
    private const string QatUndoRedoTourOutputDirectoryName = "qat-undo-redo-tour";
    private const string SheetTabTourManifestFileName = "sheet_tabs_tour_manifest.json";
    private const string SheetTabTourOutputDirectoryName = "sheet-tabs-tour";
    private const string SheetTabWorkflowsTourManifestFileName = "sheet_tab_workflows_tour_manifest.json";
    private const string SheetTabWorkflowsTourOutputDirectoryName = "sheet-tab-workflows-tour";
    private const string SheetTabWorkflowsTourSavedWorkbookFileName = "freex_sheet_tab_workflows_persisted.xlsx";
    private const string TitlebarWindowChromeTourManifestFileName = "titlebar_window_chrome_tour_manifest.json";
    private const string TitlebarWindowChromeTourOutputDirectoryName = "titlebar-window-chrome-tour";
    private const string TitlebarWindowChromeTourSavedWorkbookFileName = "freex_titlebar_renamed_workbook.xlsx";
    private const string FormulaBarNameBoxTourManifestFileName = "formula_bar_name_box_tour_manifest.json";
    private const string FormulaBarNameBoxTourOutputDirectoryName = "formula-bar-name-box-tour";
    private const string GridSelectionEditingTourManifestFileName = "grid_selection_editing_tour_manifest.json";
    private const string GridSelectionEditingTourOutputDirectoryName = "grid-selection-editing-tour";
    private const string StatusFooterTourManifestFileName = "status_footer_tour_manifest.json";
    private const string StatusFooterTourOutputDirectoryName = "status-footer-tour";
    private const string StatusFooterInteractionsTourManifestFileName = "status_footer_interactions_tour_manifest.json";
    private const string StatusFooterInteractionsTourOutputDirectoryName = "status-footer-interactions-tour";
    private const string InsertObjectsLinksTourManifestFileName = "insert_objects_links_tour_manifest.json";
    private const string InsertObjectsLinksTourOutputDirectoryName = "insert-objects-links-tour";
    private const string InsertObjectPersistenceTourManifestFileName = "insert_object_persistence_tour_manifest.json";
    private const string InsertObjectPersistenceTourOutputDirectoryName = "insert-object-persistence-tour";
    private const string InsertObjectPersistenceTourSavedWorkbookFileName = "freex_insert_object_persistence_saved.fxl";
    private const string DataToolsDialogsTourManifestFileName = "data_tools_dialogs_tour_manifest.json";
    private const string DataToolsDialogsTourOutputDirectoryName = "data-tools-dialogs-tour";
    private const string DataSortFilterOutlineTourManifestFileName = "data_sort_filter_outline_tour_manifest.json";
    private const string DataSortFilterOutlineTourOutputDirectoryName = "data-sort-filter-outline-tour";
    private const string DataSubmittedWorkflowsTourManifestFileName = "data_submitted_workflows_tour_manifest.json";
    private const string DataSubmittedWorkflowsTourOutputDirectoryName = "data-submitted-workflows-tour";
    private const string DataWhatIfWorkflowsTourManifestFileName = "data_what_if_workflows_tour_manifest.json";
    private const string DataWhatIfWorkflowsTourOutputDirectoryName = "data-what-if-workflows-tour";
    private const string FileIoImportSmokeTourManifestFileName = "file_io_import_smoke_tour_manifest.json";
    private const string FileIoImportSmokeTourOutputDirectoryName = "file-io-import-smoke-tour";
    private const string FileBackstageWorkflowsTourManifestFileName = "file_backstage_workflows_tour_manifest.json";
    private const string FileBackstageWorkflowsTourOutputDirectoryName = "file-backstage-workflows-tour";
    private const string InsertTablesChartsTourManifestFileName = "insert_tables_charts_tour_manifest.json";
    private const string InsertTablesChartsTourOutputDirectoryName = "insert-tables-charts-tour";
    private const string TableWorkflowsTourManifestFileName = "table_workflows_tour_manifest.json";
    private const string TableWorkflowsTourOutputDirectoryName = "table-workflows-tour";
    private const string TableWorkflowsTourSavedWorkbookFileName = "freex_table_workflows_saved.xlsx";
    private const string ChartDataLayoutTourManifestFileName = "chart_data_layout_tour_manifest.json";
    private const string ChartDataLayoutTourOutputDirectoryName = "chart-data-layout-tour";
    private const string ChartPersistenceRenderTourManifestFileName = "chart_persistence_render_tour_manifest.json";
    private const string ChartPersistenceRenderTourOutputDirectoryName = "chart-persistence-render-tour";
    private const string ChartPersistenceRenderTourSavedWorkbookFileName = "freex_chart_persistence_render_saved.fxl";
    private const string ChartObjectSelectionTourManifestFileName = "chart_object_selection_tour_manifest.json";
    private const string ChartObjectSelectionTourOutputDirectoryName = "chart-object-selection-tour";
    private const string ChartObjectSelectionTourSavedWorkbookFileName = "freex_chart_object_selection_saved.fxl";
    private const string PivotFieldListContextTourManifestFileName = "pivot_field_list_context_tour_manifest.json";
    private const string PivotFieldListContextTourOutputDirectoryName = "pivot-field-list-context-tour";
    private const string PivotOptionsSlicerTourManifestFileName = "pivot_options_slicer_tour_manifest.json";
    private const string PivotOptionsSlicerTourOutputDirectoryName = "pivot-options-slicer-tour";
    private const string PivotAdvancedWorkflowsTourManifestFileName = "pivot_advanced_workflows_tour_manifest.json";
    private const string PivotAdvancedWorkflowsTourOutputDirectoryName = "pivot-advanced-workflows-tour";
    private const string PivotAdvancedWorkflowsTourSavedWorkbookFileName = "freex_pivot_advanced_workflows_saved.xlsx";
    private const string ViewPanesZoomTourManifestFileName = "view_panes_zoom_tour_manifest.json";
    private const string ViewPanesZoomTourOutputDirectoryName = "view-panes-zoom-tour";
    private const string ViewPanesZoomTourCustomViewName = "View Panes Zoom Tour";
    private const string ViewWorkflowsTourManifestFileName = "view_workflows_tour_manifest.json";
    private const string ViewWorkflowsTourOutputDirectoryName = "view-workflows-tour";
    private const string ViewWorkflowsTourSavedWorkbookFileName = "freex_view_workflows_saved.fxl";
    private const string ViewWorkflowsTourCustomViewName = "View Workflow Submitted";
    private const string PageLayoutSetupTourManifestFileName = "page_layout_setup_tour_manifest.json";
    private const string PageLayoutSetupTourOutputDirectoryName = "page-layout-setup-tour";
    private const string PageLayoutOutputTourManifestFileName = "page_layout_output_tour_manifest.json";
    private const string PageLayoutOutputTourOutputDirectoryName = "page-layout-output-tour";
    private const string DrawObjectFormattingTourManifestFileName = "draw_object_formatting_tour_manifest.json";
    private const string DrawObjectFormattingTourOutputDirectoryName = "draw-object-formatting-tour";
    private const string DrawObjectPersistenceTourManifestFileName = "draw_object_persistence_tour_manifest.json";
    private const string DrawObjectPersistenceTourOutputDirectoryName = "draw-object-persistence-tour";
    private const string DrawObjectPersistenceTourSavedWorkbookFileName = "freex_draw_object_persistence_saved.fxl";
    private const string FormulaDiagnosticsTourManifestFileName = "formula_diagnostics_tour_manifest.json";
    private const string FormulaDiagnosticsTourOutputDirectoryName = "formula-diagnostics-tour";
    private const string FormulaAuthoringNamesTourManifestFileName = "formula_authoring_names_tour_manifest.json";
    private const string FormulaAuthoringNamesTourOutputDirectoryName = "formula-authoring-names-tour";
    private const string FormulaSubmittedPersistenceTourManifestFileName = "formula_submitted_persistence_tour_manifest.json";
    private const string FormulaSubmittedPersistenceTourOutputDirectoryName = "formula-submitted-persistence-tour";
    private const string FormulaSubmittedPersistenceTourSavedWorkbookFileName = "freex_formula_submitted_persistence_saved.fxl";
    private const string ReviewCommentsProtectionTourManifestFileName = "review_comments_protection_tour_manifest.json";
    private const string ReviewCommentsProtectionTourOutputDirectoryName = "review-comments-protection-tour";
    private const string ReviewProtectionMatrixTourManifestFileName = "review_protection_matrix_tour_manifest.json";
    private const string ReviewProtectionMatrixTourOutputDirectoryName = "review-protection-matrix-tour";
    private const string ScreenshotTourAllowBackgroundRenderEnvVar = "FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER";
    private const string ScreenshotTourOutputSubdirectoryEnvVar = "FREEX_SS_TOUR_OUTPUT_SUBDIR";

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Activated by FREEX_SS_TOUR=1 env var.  Output lands in <repo-root>/screenshots/.
    private async void TryStartScreenshotTour()
    {
        try
        {
        var ribbonBurstTour = Environment.GetEnvironmentVariable("FREEX_SS_TOUR_BURST") == "1";
        var ribbonTour = ribbonBurstTour || Environment.GetEnvironmentVariable("FREEX_SS_TOUR") == "1";
        var backstageTour = Environment.GetEnvironmentVariable("FREEX_BACKSTAGE_TOUR") == "1";
        var autoFilterFlyoutTour = Environment.GetEnvironmentVariable("FREEX_AUTOFILTER_FLYOUT_TOUR") == "1";
        var homeNumberFormatDropdownTour = Environment.GetEnvironmentVariable("FREEX_HOME_NUMBER_FORMAT_DROPDOWN_TOUR") == "1";
        var homeAlignmentNumberTour = Environment.GetEnvironmentVariable("FREEX_HOME_ALIGNMENT_NUMBER_TOUR") == "1";
        var homeBordersDropdownTour = Environment.GetEnvironmentVariable("FREEX_HOME_BORDERS_DROPDOWN_TOUR") == "1";
        var homeFontColorsTour = Environment.GetEnvironmentVariable("FREEX_HOME_FONT_COLORS_TOUR") == "1";
        var homeStylesConditionalFormattingTour = Environment.GetEnvironmentVariable("FREEX_HOME_STYLES_CF_TOUR") == "1";
        var homeClipboardCellsEditingTour = Environment.GetEnvironmentVariable("FREEX_HOME_CLIPBOARD_CELLS_EDITING_TOUR") == "1";
        var homeSubmittedWorkflowsTour = Environment.GetEnvironmentVariable("FREEX_HOME_SUBMITTED_WORKFLOWS_TOUR") == "1";
        var homeStylePersistenceTour = Environment.GetEnvironmentVariable("FREEX_HOME_STYLE_PERSISTENCE_TOUR") == "1";
        var ribbonOverflowKeytipTour = Environment.GetEnvironmentVariable("FREEX_RIBBON_OVERFLOW_KEYTIP_TOUR") == "1";
        var worksheetContextMenuTour = Environment.GetEnvironmentVariable("FREEX_WORKSHEET_CONTEXT_MENU_TOUR") == "1";
        var worksheetContextTargetsTour = Environment.GetEnvironmentVariable("FREEX_WORKSHEET_CONTEXT_TARGETS_TOUR") == "1";
        var worksheetContextSubmittedTour = Environment.GetEnvironmentVariable("FREEX_WORKSHEET_CONTEXT_SUBMITTED_TOUR") == "1";
        var keyTipOverlayTour = Environment.GetEnvironmentVariable("FREEX_KEYTIP_OVERLAY_TOUR") == "1";
        var printPreviewTour = Environment.GetEnvironmentVariable("FREEX_PRINT_PREVIEW_TOUR") == "1";
        var backstageRecentExportShareTour = Environment.GetEnvironmentVariable("FREEX_BACKSTAGE_RECENT_EXPORT_SHARE_TOUR") == "1";
        var optionsAccountTour = Environment.GetEnvironmentVariable("FREEX_OPTIONS_ACCOUNT_TOUR") == "1";
        var helpAboutLegalTour = Environment.GetEnvironmentVariable("FREEX_HELP_ABOUT_LEGAL_TOUR") == "1";
        var qatUndoRedoTour = Environment.GetEnvironmentVariable("FREEX_QAT_UNDO_REDO_TOUR") == "1";
        var titlebarWindowChromeTour = Environment.GetEnvironmentVariable("FREEX_TITLEBAR_WINDOW_CHROME_TOUR") == "1";
        var formulaBarNameBoxTour = Environment.GetEnvironmentVariable("FREEX_FORMULA_BAR_NAME_BOX_TOUR") == "1";
        var gridSelectionEditingTour = Environment.GetEnvironmentVariable("FREEX_GRID_SELECTION_EDITING_TOUR") == "1";
        var statusFooterTour = Environment.GetEnvironmentVariable("FREEX_STATUS_FOOTER_TOUR") == "1";
        var statusFooterInteractionsTour = statusFooterTour ||
            Environment.GetEnvironmentVariable("FREEX_STATUS_FOOTER_INTERACTIONS_TOUR") == "1";
        var insertObjectsLinksTour = Environment.GetEnvironmentVariable("FREEX_INSERT_OBJECTS_LINKS_TOUR") == "1";
        var insertObjectPersistenceTour = Environment.GetEnvironmentVariable("FREEX_INSERT_OBJECT_PERSISTENCE_TOUR") == "1";
        var dataToolsDialogsTour = Environment.GetEnvironmentVariable("FREEX_DATA_TOOLS_DIALOGS_TOUR") == "1";
        var dataSortFilterOutlineTour = Environment.GetEnvironmentVariable("FREEX_DATA_SORT_FILTER_OUTLINE_TOUR") == "1";
        var dataSubmittedWorkflowsTour = Environment.GetEnvironmentVariable("FREEX_DATA_SUBMITTED_WORKFLOWS_TOUR") == "1";
        var dataWhatIfWorkflowsTour = Environment.GetEnvironmentVariable("FREEX_DATA_WHAT_IF_WORKFLOWS_TOUR") == "1";
        var fileIoImportSmokeTour = Environment.GetEnvironmentVariable("FREEX_FILE_IO_IMPORT_SMOKE_TOUR") == "1";
        var fileBackstageWorkflowsTour = Environment.GetEnvironmentVariable("FREEX_FILE_BACKSTAGE_WORKFLOWS_TOUR") == "1";
        var insertTablesChartsTour = Environment.GetEnvironmentVariable("FREEX_INSERT_TABLES_CHARTS_TOUR") == "1";
        var tableWorkflowsTour = Environment.GetEnvironmentVariable("FREEX_TABLE_WORKFLOWS_TOUR") == "1";
        var chartDataLayoutTour = Environment.GetEnvironmentVariable("FREEX_CHART_DATA_LAYOUT_TOUR") == "1";
        var chartPersistenceRenderTour = Environment.GetEnvironmentVariable("FREEX_CHART_PERSISTENCE_RENDER_TOUR") == "1";
        var chartObjectSelectionTour = Environment.GetEnvironmentVariable("FREEX_CHART_OBJECT_SELECTION_TOUR") == "1";
        var pivotFieldListContextTour = Environment.GetEnvironmentVariable("FREEX_PIVOT_FIELD_LIST_CONTEXT_TOUR") == "1";
        var pivotOptionsSlicerTour = Environment.GetEnvironmentVariable("FREEX_PIVOT_OPTIONS_SLICER_TOUR") == "1";
        var pivotAdvancedWorkflowsTour = Environment.GetEnvironmentVariable("FREEX_PIVOT_ADVANCED_WORKFLOWS_TOUR") == "1";
        var viewPanesZoomTour = Environment.GetEnvironmentVariable("FREEX_VIEW_PANES_ZOOM_TOUR") == "1";
        var viewWorkflowsTour = Environment.GetEnvironmentVariable("FREEX_VIEW_WORKFLOWS_TOUR") == "1";
        var pageLayoutSetupTour = Environment.GetEnvironmentVariable("FREEX_PAGE_LAYOUT_SETUP_TOUR") == "1";
        var pageLayoutOutputTour = Environment.GetEnvironmentVariable("FREEX_PAGE_LAYOUT_OUTPUT_TOUR") == "1";
        var drawObjectFormattingTour = Environment.GetEnvironmentVariable("FREEX_DRAW_OBJECT_FORMATTING_TOUR") == "1";
        var drawObjectPersistenceTour = Environment.GetEnvironmentVariable("FREEX_DRAW_OBJECT_PERSISTENCE_TOUR") == "1";
        var formulaDiagnosticsTour = Environment.GetEnvironmentVariable("FREEX_FORMULA_DIAGNOSTICS_TOUR") == "1";
        var formulaAuthoringNamesTour = Environment.GetEnvironmentVariable("FREEX_FORMULA_AUTHORING_NAMES_TOUR") == "1";
        var formulaSubmittedPersistenceTour = Environment.GetEnvironmentVariable("FREEX_FORMULA_SUBMITTED_PERSISTENCE_TOUR") == "1";
        var reviewCommentsProtectionTour = Environment.GetEnvironmentVariable("FREEX_REVIEW_COMMENTS_PROTECTION_TOUR") == "1";
        var reviewProtectionMatrixTour = Environment.GetEnvironmentVariable("FREEX_REVIEW_PROTECTION_MATRIX_TOUR") == "1";
        var reviewStatsShareTour = Environment.GetEnvironmentVariable("FREEX_REVIEW_STATS_SHARE_TOUR") == "1";
        if (!ribbonTour && !backstageTour && !autoFilterFlyoutTour && !homeNumberFormatDropdownTour && !homeAlignmentNumberTour && !homeBordersDropdownTour && !homeFontColorsTour && !homeStylesConditionalFormattingTour && !homeClipboardCellsEditingTour && !homeSubmittedWorkflowsTour && !homeStylePersistenceTour && !ribbonOverflowKeytipTour && !worksheetContextMenuTour && !worksheetContextTargetsTour && !worksheetContextSubmittedTour && !keyTipOverlayTour && !printPreviewTour && !backstageRecentExportShareTour && !optionsAccountTour && !helpAboutLegalTour && !qatUndoRedoTour && !titlebarWindowChromeTour && !statusFooterTour && !statusFooterInteractionsTour && !formulaBarNameBoxTour && !gridSelectionEditingTour && !insertObjectsLinksTour && !insertObjectPersistenceTour && !dataToolsDialogsTour && !dataSortFilterOutlineTour && !dataSubmittedWorkflowsTour && !dataWhatIfWorkflowsTour && !fileIoImportSmokeTour && !fileBackstageWorkflowsTour && !insertTablesChartsTour && !tableWorkflowsTour && !chartDataLayoutTour && !chartPersistenceRenderTour && !chartObjectSelectionTour && !pivotFieldListContextTour && !pivotOptionsSlicerTour && !pivotAdvancedWorkflowsTour && !viewPanesZoomTour && !viewWorkflowsTour && !pageLayoutSetupTour && !pageLayoutOutputTour && !drawObjectFormattingTour && !drawObjectPersistenceTour && !formulaDiagnosticsTour && !formulaAuthoringNamesTour && !formulaSubmittedPersistenceTour && !reviewCommentsProtectionTour && !reviewProtectionMatrixTour && !reviewStatsShareTour)
            return;

        var ribbonPlan = ribbonTour
            ? RibbonScreenshotTourPlanner.CreatePlan(
                Environment.GetEnvironmentVariable("FREEX_SS_TOUR_TABS"),
                Environment.GetEnvironmentVariable("FREEX_SS_TOUR_WIDTHS"),
                ribbonBurstTour,
                Environment.GetEnvironmentVariable("FREEX_SS_TOUR_CONTEXT"))
            : null;

        var screenshotsRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots"));
        var outputDir = ResolveScreenshotTourOutputDirectory(
            screenshotsRoot,
            Environment.GetEnvironmentVariable(ScreenshotTourOutputSubdirectoryEnvVar));
        Directory.CreateDirectory(outputDir);
        await RunScreenshotTourAsync(outputDir, ribbonPlan, backstageTour, autoFilterFlyoutTour, homeNumberFormatDropdownTour, homeAlignmentNumberTour, homeBordersDropdownTour, homeFontColorsTour, homeStylesConditionalFormattingTour, homeClipboardCellsEditingTour, homeSubmittedWorkflowsTour, homeStylePersistenceTour, ribbonOverflowKeytipTour, worksheetContextMenuTour, worksheetContextTargetsTour, worksheetContextSubmittedTour, keyTipOverlayTour, printPreviewTour, backstageRecentExportShareTour, optionsAccountTour, helpAboutLegalTour, qatUndoRedoTour, titlebarWindowChromeTour, statusFooterTour, statusFooterInteractionsTour, formulaBarNameBoxTour, gridSelectionEditingTour, insertObjectsLinksTour, insertObjectPersistenceTour, dataToolsDialogsTour, dataSortFilterOutlineTour, dataSubmittedWorkflowsTour, dataWhatIfWorkflowsTour, fileIoImportSmokeTour, fileBackstageWorkflowsTour, insertTablesChartsTour, tableWorkflowsTour, chartDataLayoutTour, chartPersistenceRenderTour, chartObjectSelectionTour, pivotFieldListContextTour, pivotOptionsSlicerTour, pivotAdvancedWorkflowsTour, viewPanesZoomTour, viewWorkflowsTour, pageLayoutSetupTour, pageLayoutOutputTour, drawObjectFormattingTour, drawObjectPersistenceTour, formulaDiagnosticsTour, formulaAuthoringNamesTour, formulaSubmittedPersistenceTour, reviewCommentsProtectionTour, reviewProtectionMatrixTour, reviewStatsShareTour);
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("screenshot_tour_failed", new Dictionary<string, string?>
            {
                ["reason"] = ex.GetType().Name,
                ["message"] = ex.Message
            });
        }
    }

    private static string ResolveScreenshotTourOutputDirectory(string screenshotsRoot, string? requestedSubdirectory)
    {
        if (string.IsNullOrWhiteSpace(requestedSubdirectory))
            return screenshotsRoot;

        if (Path.IsPathRooted(requestedSubdirectory))
            throw new InvalidOperationException($"{ScreenshotTourOutputSubdirectoryEnvVar} must be a relative path under screenshots.");

        var root = Path.GetFullPath(screenshotsRoot);
        var resolved = Path.GetFullPath(Path.Combine(root, requestedSubdirectory));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{ScreenshotTourOutputSubdirectoryEnvVar} must stay under screenshots.");

        return resolved;
    }

    private async Task RunScreenshotTourAsync(
        string outputDir,
        RibbonScreenshotTourPlan? ribbonPlan,
        bool backstageTour,
        bool autoFilterFlyoutTour,
        bool homeNumberFormatDropdownTour,
        bool homeAlignmentNumberTour,
        bool homeBordersDropdownTour,
        bool homeFontColorsTour,
        bool homeStylesConditionalFormattingTour,
        bool homeClipboardCellsEditingTour,
        bool homeSubmittedWorkflowsTour,
        bool homeStylePersistenceTour,
        bool ribbonOverflowKeytipTour,
        bool worksheetContextMenuTour,
        bool worksheetContextTargetsTour,
        bool worksheetContextSubmittedTour,
        bool keyTipOverlayTour,
        bool printPreviewTour,
        bool backstageRecentExportShareTour,
        bool optionsAccountTour,
        bool helpAboutLegalTour,
        bool qatUndoRedoTour,
        bool titlebarWindowChromeTour,
        bool statusFooterTour,
        bool statusFooterInteractionsTour,
        bool formulaBarNameBoxTour,
        bool gridSelectionEditingTour,
        bool insertObjectsLinksTour,
        bool insertObjectPersistenceTour,
        bool dataToolsDialogsTour,
        bool dataSortFilterOutlineTour,
        bool dataSubmittedWorkflowsTour,
        bool dataWhatIfWorkflowsTour,
        bool fileIoImportSmokeTour,
        bool fileBackstageWorkflowsTour,
        bool insertTablesChartsTour,
        bool tableWorkflowsTour,
        bool chartDataLayoutTour,
        bool chartPersistenceRenderTour,
        bool chartObjectSelectionTour,
        bool pivotFieldListContextTour,
        bool pivotOptionsSlicerTour,
        bool pivotAdvancedWorkflowsTour,
        bool viewPanesZoomTour,
        bool viewWorkflowsTour,
        bool pageLayoutSetupTour,
        bool pageLayoutOutputTour,
        bool drawObjectFormattingTour,
        bool drawObjectPersistenceTour,
        bool formulaDiagnosticsTour,
        bool formulaAuthoringNamesTour,
        bool formulaSubmittedPersistenceTour,
        bool reviewCommentsProtectionTour,
        bool reviewProtectionMatrixTour,
        bool reviewStatsShareTour)
    {
        if (ribbonPlan is not null)
            await CaptureRibbonTourAsync(outputDir, ribbonPlan);

        if (backstageTour)
            await CaptureBackstageAsync(outputDir);

        if (autoFilterFlyoutTour)
            await CaptureAutoFilterFlyoutTourAsync(Path.Combine(outputDir, "autofilter-flyout-tour"));

        if (homeNumberFormatDropdownTour)
            await CaptureHomeNumberFormatDropdownTourAsync(Path.Combine(outputDir, "home-number-format-dropdown-tour"));

        if (homeAlignmentNumberTour)
            await CaptureHomeAlignmentNumberTourAsync(Path.Combine(outputDir, HomeAlignmentNumberTourOutputDirectoryName));

        if (homeBordersDropdownTour)
            await CaptureHomeBordersDropdownTourAsync(Path.Combine(outputDir, "home-borders-dropdown-tour"));

        if (homeFontColorsTour)
            await CaptureHomeFontColorsTourAsync(Path.Combine(outputDir, HomeFontColorsTourOutputDirectoryName));

        if (homeStylesConditionalFormattingTour)
            await CaptureHomeStylesConditionalFormattingTourAsync(Path.Combine(outputDir, HomeStylesConditionalFormattingTourOutputDirectoryName));

        if (homeClipboardCellsEditingTour)
            await CaptureHomeClipboardCellsEditingTourAsync(Path.Combine(outputDir, HomeClipboardCellsEditingTourOutputDirectoryName));

        if (homeSubmittedWorkflowsTour)
            await CaptureHomeSubmittedWorkflowsTourAsync(Path.Combine(outputDir, HomeSubmittedWorkflowsTourOutputDirectoryName));

        if (homeStylePersistenceTour)
            await CaptureHomeStylePersistenceTourAsync(Path.Combine(outputDir, HomeStylePersistenceTourOutputDirectoryName));

        if (ribbonOverflowKeytipTour)
            await CaptureRibbonOverflowKeytipTourAsync(Path.Combine(outputDir, RibbonOverflowKeytipTourOutputDirectoryName));

        if (worksheetContextMenuTour)
            await CaptureWorksheetContextMenuTourAsync(Path.Combine(outputDir, "worksheet-context-menu-tour"));
        if (worksheetContextTargetsTour)
            await CaptureWorksheetContextTargetsTourAsync(Path.Combine(outputDir, WorksheetContextTargetsTourOutputDirectoryName));
        if (worksheetContextSubmittedTour)
            await CaptureWorksheetContextSubmittedTourAsync(Path.Combine(outputDir, WorksheetContextSubmittedTourOutputDirectoryName));

        if (keyTipOverlayTour)
            await CaptureKeyTipOverlayTourAsync(Path.Combine(outputDir, "keytip-overlay-tour"));

        if (printPreviewTour)
            await CapturePrintPreviewTourAsync(Path.Combine(outputDir, "print-preview-tour"));

        if (backstageRecentExportShareTour)
            await CaptureBackstageRecentExportShareTourAsync(Path.Combine(outputDir, BackstageRecentExportShareTourOutputDirectoryName));

        if (optionsAccountTour)
            await CaptureOptionsAccountTourAsync(Path.Combine(outputDir, OptionsAccountTourOutputDirectoryName));

        if (helpAboutLegalTour)
            await CaptureHelpAboutLegalTourAsync(Path.Combine(outputDir, HelpAboutLegalTourOutputDirectoryName));

        if (qatUndoRedoTour)
            await CaptureQatUndoRedoTourAsync(Path.Combine(outputDir, QatUndoRedoTourOutputDirectoryName));

        if (titlebarWindowChromeTour)
            await CaptureTitlebarWindowChromeTourAsync(Path.Combine(outputDir, TitlebarWindowChromeTourOutputDirectoryName));
        if (statusFooterTour)
            await CaptureStatusFooterTourAsync(Path.Combine(outputDir, StatusFooterTourOutputDirectoryName));

        if (statusFooterInteractionsTour)
            await CaptureStatusFooterInteractionsTourAsync(Path.Combine(outputDir, StatusFooterInteractionsTourOutputDirectoryName));

        if (formulaBarNameBoxTour)
            await CaptureFormulaBarNameBoxTourAsync(Path.Combine(outputDir, FormulaBarNameBoxTourOutputDirectoryName));

        if (gridSelectionEditingTour)
            await CaptureGridSelectionEditingTourAsync(Path.Combine(outputDir, GridSelectionEditingTourOutputDirectoryName));

        if (insertObjectsLinksTour)
            await CaptureInsertObjectsLinksTourAsync(Path.Combine(outputDir, InsertObjectsLinksTourOutputDirectoryName));
        if (insertObjectPersistenceTour)
            await CaptureInsertObjectPersistenceTourAsync(Path.Combine(outputDir, InsertObjectPersistenceTourOutputDirectoryName));
        if (dataToolsDialogsTour)
            await CaptureDataToolsDialogsTourAsync(Path.Combine(outputDir, DataToolsDialogsTourOutputDirectoryName));
        if (dataSortFilterOutlineTour)
            await CaptureDataSortFilterOutlineTourAsync(Path.Combine(outputDir, DataSortFilterOutlineTourOutputDirectoryName));
        if (dataSubmittedWorkflowsTour)
            await CaptureDataSubmittedWorkflowsTourAsync(Path.Combine(outputDir, DataSubmittedWorkflowsTourOutputDirectoryName));
        if (dataWhatIfWorkflowsTour)
            await CaptureDataWhatIfWorkflowsTourAsync(Path.Combine(outputDir, DataWhatIfWorkflowsTourOutputDirectoryName));
        if (fileIoImportSmokeTour)
            await CaptureFileIoImportSmokeTourAsync(Path.Combine(outputDir, FileIoImportSmokeTourOutputDirectoryName));
        if (fileBackstageWorkflowsTour)
            await CaptureFileBackstageWorkflowsTourAsync(Path.Combine(outputDir, FileBackstageWorkflowsTourOutputDirectoryName));
        if (insertTablesChartsTour)
            await CaptureInsertTablesChartsTourAsync(Path.Combine(outputDir, InsertTablesChartsTourOutputDirectoryName));
        if (tableWorkflowsTour)
            await CaptureTableWorkflowsTourAsync(Path.Combine(outputDir, TableWorkflowsTourOutputDirectoryName));
        if (chartDataLayoutTour)
            await CaptureChartDataLayoutTourAsync(Path.Combine(outputDir, ChartDataLayoutTourOutputDirectoryName));
        if (chartPersistenceRenderTour)
            await CaptureChartPersistenceRenderTourAsync(Path.Combine(outputDir, ChartPersistenceRenderTourOutputDirectoryName));
        if (chartObjectSelectionTour)
            await CaptureChartObjectSelectionTourAsync(Path.Combine(outputDir, ChartObjectSelectionTourOutputDirectoryName));
        if (pivotFieldListContextTour)
            await CapturePivotFieldListContextTourAsync(Path.Combine(outputDir, PivotFieldListContextTourOutputDirectoryName));
        if (pivotOptionsSlicerTour)
            await CapturePivotOptionsSlicerTourAsync(Path.Combine(outputDir, PivotOptionsSlicerTourOutputDirectoryName));
        if (pivotAdvancedWorkflowsTour)
            await CapturePivotAdvancedWorkflowsTourAsync(Path.Combine(outputDir, PivotAdvancedWorkflowsTourOutputDirectoryName));
        if (viewPanesZoomTour)
            await CaptureViewPanesZoomTourAsync(Path.Combine(outputDir, ViewPanesZoomTourOutputDirectoryName));
        if (viewWorkflowsTour)
            await CaptureViewWorkflowsTourAsync(Path.Combine(outputDir, ViewWorkflowsTourOutputDirectoryName));
        if (pageLayoutSetupTour)
            await CapturePageLayoutSetupTourAsync(Path.Combine(outputDir, PageLayoutSetupTourOutputDirectoryName));
        if (pageLayoutOutputTour)
            await CapturePageLayoutOutputTourAsync(Path.Combine(outputDir, PageLayoutOutputTourOutputDirectoryName));
        if (drawObjectFormattingTour)
            await CaptureDrawObjectFormattingTourAsync(Path.Combine(outputDir, DrawObjectFormattingTourOutputDirectoryName));
        if (drawObjectPersistenceTour)
            await CaptureDrawObjectPersistenceTourAsync(Path.Combine(outputDir, DrawObjectPersistenceTourOutputDirectoryName));
        if (formulaDiagnosticsTour)
            await CaptureFormulaDiagnosticsTourAsync(Path.Combine(outputDir, FormulaDiagnosticsTourOutputDirectoryName));

        if (formulaAuthoringNamesTour)
            await CaptureFormulaAuthoringNamesTourAsync(Path.Combine(outputDir, FormulaAuthoringNamesTourOutputDirectoryName));

        if (formulaSubmittedPersistenceTour)
            await CaptureFormulaSubmittedPersistenceTourAsync(Path.Combine(outputDir, FormulaSubmittedPersistenceTourOutputDirectoryName));

        if (reviewCommentsProtectionTour)
            await CaptureReviewCommentsProtectionTourAsync(Path.Combine(outputDir, ReviewCommentsProtectionTourOutputDirectoryName));

        if (reviewProtectionMatrixTour)
            await CaptureReviewProtectionMatrixTourAsync(Path.Combine(outputDir, ReviewProtectionMatrixTourOutputDirectoryName));

        if (reviewStatsShareTour)
            await CaptureReviewStatsShareTourAsync(Path.Combine(outputDir, ReviewStatsShareTourOutputDirectoryName));

        _suppressClosePrompt = true;
        Application.Current.Shutdown();
    }

    private async Task CaptureBackstageAsync(string outputDir)
    {
        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(800);

        ShowStartScreen();
        UpdateLayout();
        await Task.Delay(350);
        UpdateLayout();

        await CaptureCurrentWindowAsync(outputDir, "backstage_home", 760);
    }

    private async Task CaptureAutoFilterFlyoutTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteAutoFilterFlyoutTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var headerCell = EnsureAutoFilterFlyoutTourContext();
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);

        if (_workbook.GetSheet(_currentSheetId) is not { } sheet ||
            CreateAutoFilterFlyoutDialog(sheet, headerCell, null, out var plan) is not { } dialog ||
            plan is null)
        {
            throw new InvalidOperationException("AutoFilter flyout tour could not create the live AutoFilter flyout.");
        }

        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await Task.Delay(350);
            dialog.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureElementAsync(dialog, outputDir, AutoFilterFlyoutTourCaptureFileName);
            ValidateAutoFilterFlyoutTourEvidence(outputDir);
            await WriteAutoFilterFlyoutTourManifestAsync(outputDir, dialog, plan);
        }
        catch
        {
            DeleteAutoFilterFlyoutTourEvidence(outputDir);
            throw;
        }
        finally
        {
            dialog.Close();
        }
    }

    private CellAddress EnsureAutoFilterFlyoutTourContext()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
        {
            foreach (var candidate in _workbook.Sheets)
            {
                sheet = candidate;
                break;
            }
        }

        if (sheet is null)
            throw new InvalidOperationException("AutoFilter flyout tour requires an active worksheet.");

        _currentSheetId = sheet.Id;

        var headers = new[] { "score", "name", "date", "note" };
        object?[][] rows =
        [
            [1d, "North", "2026-06-01", "alpha"],
            [2d, "South", "2026-06-02", "beta"],
            [3d, "East", "2026-06-03", "gamma"],
            [4d, "West", "2026-06-04", "delta"],
            [null, "Blank score", "2026-06-05", "blank"]
        ];

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        foreach (var address in range.AllCells())
            sheet.ClearCell(address);

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                switch (rows[row][col])
                {
                    case double number:
                        sheet.SetCell(address, new NumberValue(number));
                        break;
                    case string text:
                        sheet.SetCell(address, new TextValue(text));
                        break;
                    case null:
                        sheet.ClearCell(address);
                        break;
                }
            }
        }

        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenRows.Clear();
        _filterWorkflowSession.ResetAutoFilterState();

        var headerCell = range.Start;
        SetActiveCell(headerCell);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(headerCell, headerCell);
            SheetGrid.SelectedRanges = null;
        }

        return headerCell;
    }

    private static void DeleteAutoFilterFlyoutTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            $"{AutoFilterFlyoutTourCaptureFileName}.png",
            AutoFilterFlyoutTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateAutoFilterFlyoutTourEvidence(string outputDir)
    {
        var path = Path.Combine(outputDir, $"{AutoFilterFlyoutTourCaptureFileName}.png");
        if (!File.Exists(path))
            throw new InvalidOperationException("AutoFilter flyout tour did not create the planned FreeX dropdown capture.");
    }

    private async Task CaptureHomeNumberFormatDropdownTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeNumberFormatDropdownTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var homeTab = RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home");
        SelectRibbonTourTab(homeTab);
        var numberFormatBox = FindRenderedRibbonControl("Number Format") as ComboBox
            ?? throw new InvalidOperationException("Home number format dropdown tour could not locate the rendered Number Format combo.");
        numberFormatBox.SelectedIndex = HomeNumberFormatDropdownPlanner.DefaultSelectionIndex;
        numberFormatBox.Focus();
        numberFormatBox.ApplyTemplate();
        numberFormatBox.IsDropDownOpen = true;
        numberFormatBox.UpdateLayout();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(350);
        numberFormatBox.UpdateLayout();

        try
        {
            var popupChild = FindOpenPopupChild(numberFormatBox)
                ?? throw new InvalidOperationException("Home number format dropdown tour could not locate the open ComboBox popup.");

            await CaptureElementAsync(popupChild, outputDir, HomeNumberFormatDropdownTourCaptureFileName);
            ValidateHomeNumberFormatDropdownTourEvidence(outputDir);
            await WriteHomeNumberFormatDropdownTourManifestAsync(outputDir, popupChild);
        }
        catch
        {
            DeleteHomeNumberFormatDropdownTourEvidence(outputDir);
            throw;
        }
        finally
        {
            numberFormatBox.IsDropDownOpen = false;
        }
    }

    private static FrameworkElement? FindOpenPopupChild(DependencyObject root)
    {
        if (root is Popup { IsOpen: true, Child: FrameworkElement child })
            return child;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var candidate = VisualTreeHelper.GetChild(root, i);
            var match = FindOpenPopupChild(candidate);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static void DeleteHomeNumberFormatDropdownTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            $"{HomeNumberFormatDropdownTourCaptureFileName}.png",
            HomeNumberFormatDropdownTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateHomeNumberFormatDropdownTourEvidence(string outputDir)
    {
        var path = Path.Combine(outputDir, $"{HomeNumberFormatDropdownTourCaptureFileName}.png");
        if (!File.Exists(path))
            throw new InvalidOperationException("Home number format dropdown tour did not create the planned FreeX dropdown capture.");
    }

    private async Task CaptureHomeAlignmentNumberTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeAlignmentNumberTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureHomeAlignmentNumberTourContext();
        var captures = new List<HomeAlignmentNumberTourManifestCapture>();
        FormatCellsDialog? alignmentDialog = null;
        FormatCellsDialog? numberDialog = null;

        try
        {
            captures.Add(await CaptureHomeAlignmentNumberWindowStateAsync(
                outputDir,
                "alignment-grid",
                "freex_home_alignment_grid_commands",
                "window-full",
                "Home Alignment group focused with rendered left/center/right, top/middle/bottom, wrap, indent, rotation, and merged-center worksheet examples."));

            var orientationButton = FindRenderedRibbonControl("Orientation") as Button
                ?? throw new InvalidOperationException("Home alignment/number tour could not locate the rendered Orientation button.");
            OpenRibbonContextMenu(orientationButton, orientationButton.ContextMenu!);
            orientationButton.ContextMenu!.UpdateLayout();
            await Task.Delay(350);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureElementAsync(orientationButton.ContextMenu!, outputDir, "freex_home_alignment_orientation_menu_opened");
            captures.Add(CreateHomeAlignmentNumberTourCapture(
                "orientation-menu-opened",
                "freex_home_alignment_orientation_menu_opened",
                "orientation-menu",
                "RenderTargetBitmap-context-menu",
                orientationButton.ContextMenu!.ActualWidth,
                orientationButton.ContextMenu!.ActualHeight,
                "Production Orientation menu opened from the Home Alignment group."));
            orientationButton.ContextMenu!.IsOpen = false;

            SetSelectionRange(context.NumberRange, context.NumberRange.Start);
            RefreshToolbar();
            UpdateLayout();
            await Task.Delay(250);
            captures.Add(await CaptureHomeAlignmentNumberWindowStateAsync(
                outputDir,
                "number-format-grid",
                "freex_home_number_format_grid_commands",
                "window-full",
                "Home Number group focused with rendered Accounting, Percent, Short Date, and custom number format examples."));

            alignmentDialog = new FormatCellsDialog(
                new CellStyle
                {
                    HorizontalAlignment = FreeX.Core.Model.HorizontalAlignment.Distributed,
                    VerticalAlignment = FreeX.Core.Model.VerticalAlignment.Center,
                    WrapText = true,
                    ShrinkToFit = true,
                    IndentLevel = 2,
                    TextRotation = 45
                },
                FormatCellsDialogTab.Alignment)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            alignmentDialog.Show();
            alignmentDialog.Activate();
            alignmentDialog.UpdateLayout();
            await Task.Delay(450);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureWindowElementForScreenshotTourAsync(alignmentDialog, outputDir, "freex_home_alignment_format_cells_dialog");
            captures.Add(CreateHomeAlignmentNumberTourCapture(
                "format-cells-alignment-dialog",
                "freex_home_alignment_format_cells_dialog",
                "format-cells-dialog",
                "RenderTargetBitmap-format-cells-dialog",
                alignmentDialog.ActualWidth,
                alignmentDialog.ActualHeight,
                "Format Cells dialog opened directly to the Alignment tab with wrap, shrink, indent, rotation, and distributed alignment state."));
            alignmentDialog.Close();
            alignmentDialog = null;

            numberDialog = new FormatCellsDialog(
                new CellStyle
                {
                    NumberFormat = "[$-409]mmmm d, yyyy;@"
                },
                FormatCellsDialogTab.Number)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            numberDialog.Show();
            numberDialog.Activate();
            numberDialog.UpdateLayout();
            await Task.Delay(450);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureWindowElementForScreenshotTourAsync(numberDialog, outputDir, "freex_home_number_format_cells_dialog");
            captures.Add(CreateHomeAlignmentNumberTourCapture(
                "format-cells-number-dialog",
                "freex_home_number_format_cells_dialog",
                "format-cells-dialog",
                "RenderTargetBitmap-format-cells-dialog",
                numberDialog.ActualWidth,
                numberDialog.ActualHeight,
                "Format Cells dialog opened directly to the Number tab with a locale/custom date format scenario."));
            numberDialog.Close();
            numberDialog = null;

            ValidateHomeAlignmentNumberTourEvidence(outputDir, captures);
            await WriteHomeAlignmentNumberTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteHomeAlignmentNumberTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if ((FindRenderedRibbonControl("Orientation") as Button)?.ContextMenu is { IsOpen: true } menu)
                menu.IsOpen = false;
            alignmentDialog?.Close();
            numberDialog?.Close();
        }
    }

    private HomeAlignmentNumberTourContext EnsureHomeAlignmentNumberTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Home alignment/number tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        for (uint row = 1; row <= 9; row++)
        {
            for (uint col = 1; col <= 6; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[2] = 20;
        sheet.ColumnWidths[3] = 18;
        sheet.ColumnWidths[4] = 17;
        sheet.ColumnWidths[5] = 18;
        sheet.ColumnWidths[6] = 18;
        sheet.RowHeights[2] = 42;
        sheet.RowHeights[3] = 38;
        sheet.RowHeights[4] = 44;

        SetTourCell(sheet, 1, 1, new TextValue("Alignment"));
        SetTourCell(sheet, 1, 4, new TextValue("Number formats"));
        SetTourCell(sheet, 2, 1, new TextValue("Left / top"));
        SetTourCell(sheet, 2, 2, new TextValue("Centered with wrap text"));
        SetTourCell(sheet, 2, 3, new TextValue("Right / bottom"));
        SetTourCell(sheet, 3, 1, new TextValue("Indented text"));
        SetTourCell(sheet, 3, 2, new TextValue("Rotated"));
        SetTourCell(sheet, 4, 1, new TextValue("Merged & Centered"));
        SetTourCell(sheet, 2, 4, new NumberValue(1234.5));
        SetTourCell(sheet, 3, 4, new NumberValue(0.425));
        SetTourCell(sheet, 4, 4, new NumberValue(new DateTime(2026, 6, 10).ToOADate()));
        SetTourCell(sheet, 5, 4, new NumberValue(-1200.34));

        var headerRange = Range(sheet.Id, 1, 1, 1, 6);
        ApplyHomeAlignmentNumberTourStyle(headerRange, new StyleDiff(Bold: true, FillColor: new CellColor(217, 225, 242)));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 2, 1, 2, 1), new StyleDiff(HAlign: FreeX.Core.Model.HorizontalAlignment.Left, VAlign: FreeX.Core.Model.VerticalAlignment.Top));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 2, 2, 2, 2), new StyleDiff(HAlign: FreeX.Core.Model.HorizontalAlignment.Center, VAlign: FreeX.Core.Model.VerticalAlignment.Center, WrapText: true));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 2, 3, 2, 3), new StyleDiff(HAlign: FreeX.Core.Model.HorizontalAlignment.Right, VAlign: FreeX.Core.Model.VerticalAlignment.Bottom));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 3, 1, 3, 1), new StyleDiff(IndentLevel: 2));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 3, 2, 3, 2), new StyleDiff(TextRotation: 45));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 2, 4, 2, 4), new StyleDiff(NumberFormat: HomeNumberFormatDropdownPlanner.AccountingNumberFormatCode));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 3, 4, 3, 4), new StyleDiff(NumberFormat: "0%"));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 4, 4, 4, 4), new StyleDiff(NumberFormat: "m/d/yyyy"));
        ApplyHomeAlignmentNumberTourStyle(Range(sheet.Id, 5, 4, 5, 4), new StyleDiff(NumberFormat: "[Red]#,##0.00;[Blue]-#,##0.00;0"));

        var mergeRange = Range(sheet.Id, 4, 1, 4, 3);
        if (!TryExecuteCommand(CreateMergeAndCenterCommand(mergeRange), "Merge & Center"))
            throw new InvalidOperationException("Home alignment/number tour could not create the Merge & Center sample.");

        var alignmentRange = Range(sheet.Id, 2, 1, 4, 3);
        var numberRange = Range(sheet.Id, 2, 4, 5, 4);
        SetSelectionRange(alignmentRange, alignmentRange.Start);
        EnsureCellVisible(alignmentRange.Start);
        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateLayout();

        return new HomeAlignmentNumberTourContext(
            SheetName: sheet.Name,
            AlignmentRange: alignmentRange,
            NumberRange: numberRange,
            SampleFormats:
            [
                HomeNumberFormatDropdownPlanner.AccountingNumberFormatCode,
                "0%",
                "m/d/yyyy",
                "[Red]#,##0.00;[Blue]-#,##0.00;0"
            ]);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));

    private static void SetTourCell(Sheet sheet, uint row, uint col, ScalarValue value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

    private void ApplyHomeAlignmentNumberTourStyle(GridRange range, StyleDiff diff)
    {
        if (!TryExecuteApplyStyle(range, diff, "Apply Style"))
            throw new InvalidOperationException($"Home alignment/number tour could not apply style to {range}.");
    }

    private async Task<HomeAlignmentNumberTourManifestCapture> CaptureHomeAlignmentNumberWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string surface,
        string evidencePurpose)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateHomeAlignmentNumberTourCapture(
            state,
            fileName,
            surface,
            "RenderTargetBitmap-main-window",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidencePurpose);
    }

    private static HomeAlignmentNumberTourManifestCapture CreateHomeAlignmentNumberTourCapture(
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidencePurpose) =>
        new(
            CaptureKey: $"interactive:home-alignment-number:{state}",
            PairKey: $"interactive:home-alignment-number:{state}",
            ScenarioId: "home:alignment-number",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CounterpartFileName: $"interactive_home_alignment_number_{state.Replace('-', '_')}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            EvidencePurpose: evidencePurpose);

    private static void DeleteHomeAlignmentNumberTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            "freex_home_alignment_grid_commands.png",
            "freex_home_alignment_orientation_menu_opened.png",
            "freex_home_number_format_grid_commands.png",
            "freex_home_alignment_format_cells_dialog.png",
            "freex_home_number_format_cells_dialog.png",
            HomeAlignmentNumberTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateHomeAlignmentNumberTourEvidence(
        string outputDir,
        IReadOnlyCollection<HomeAlignmentNumberTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Home alignment/number tour did not create {capture.OutputFileName}.");
        }
    }

    private async Task CaptureHomeBordersDropdownTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeBordersDropdownTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var homeTab = RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home");
        SelectRibbonTourTab(homeTab);
        var bordersButton = FindRenderedRibbonControl("Borders") as Button
            ?? throw new InvalidOperationException("Home Borders dropdown tour could not locate the rendered Borders button.");
        bordersButton.Focus();
        bordersButton.UpdateLayout();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);

        var menu = bordersButton.ContextMenu
            ?? throw new InvalidOperationException("Home Borders dropdown tour could not locate the Borders context menu.");

        try
        {
            menu.PlacementTarget = bordersButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            menu.UpdateLayout();
            await Task.Delay(350);
            menu.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureElementAsync(menu, outputDir, HomeBordersDropdownTourCaptureFileName);
            ValidateHomeBordersDropdownTourEvidence(outputDir);
            await WriteHomeBordersDropdownTourManifestAsync(outputDir, menu);
        }
        catch
        {
            DeleteHomeBordersDropdownTourEvidence(outputDir);
            throw;
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private static void DeleteHomeBordersDropdownTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            $"{HomeBordersDropdownTourCaptureFileName}.png",
            HomeBordersDropdownTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateHomeBordersDropdownTourEvidence(string outputDir)
    {
        var path = Path.Combine(outputDir, $"{HomeBordersDropdownTourCaptureFileName}.png");
        if (!File.Exists(path))
            throw new InvalidOperationException("Home Borders dropdown tour did not create the planned FreeX dropdown capture.");
    }

    private async Task CaptureHomeFontColorsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeFontColorsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 820;
        await Task.Delay(700);

        var sampleRange = EnsureHomeFontColorsTourContext();
        var captures = new List<HomeFontColorsTourManifestCapture>();

        var fontNameBox = FindRenderedRibbonControl("Font") as ComboBox
            ?? throw new InvalidOperationException("Home font/colors tour could not locate the rendered Font combo.");
        var fontSizeBox = FindRenderedRibbonControl("Font Size") as ComboBox
            ?? throw new InvalidOperationException("Home font/colors tour could not locate the rendered Font Size combo.");
        var underlineButton = FindRenderedRibbonControl("Underline") as ButtonBase
            ?? throw new InvalidOperationException("Home font/colors tour could not locate the rendered Underline button.");
        var bordersButton = FindRenderedRibbonControl("Borders") as ButtonBase
            ?? throw new InvalidOperationException("Home font/colors tour could not locate the rendered Borders button.");

        try
        {
            var homeTab = RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home");
            SelectRibbonTourTab(homeTab);
            SetSelectionRange(sampleRange, sampleRange.Start);
            UpdateViewport();
            RefreshToolbar();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(350);

            await CaptureCurrentWindowAsync(outputDir, "freex_home_font_colors_grid_styled", 760);
            captures.Add(CreateHomeFontColorsWindowCapture(
                "styled-grid",
                "freex_home_font_colors_grid_styled",
                "Real grid render for font family/size, grow/shrink-sized rows, bold, italic, underline, double underline, strikethrough, font color, fill color, theme-backed colors, and representative borders."));

            captures.Add(await CaptureHomeFontColorsComboPopupAsync(
                outputDir,
                fontNameBox,
                "font-family-dropdown",
                "freex_home_font_family_dropdown_opened",
                "Font family dropdown opened from the production Home Font combo box."));

            captures.Add(await CaptureHomeFontColorsComboPopupAsync(
                outputDir,
                fontSizeBox,
                "font-size-dropdown",
                "freex_home_font_size_dropdown_opened",
                "Font size dropdown opened from the production Home Font Size combo box."));

            captures.Add(await CaptureHomeFontColorsMenuAsync(
                outputDir,
                underlineButton,
                "underline-menu",
                "freex_home_underline_menu_opened",
                "Underline split-menu with single and double underline choices."));

            var borderMenuCapture = await CaptureHomeFontColorsMenuAsync(
                outputDir,
                bordersButton,
                "borders-menu",
                "freex_home_borders_full_menu_opened",
                "Full Home Borders menu with presets, draw/erase commands, line color, line style, and More Borders.");
            captures.Add(borderMenuCapture);

            captures.Add(await CaptureHomeFontColorsBorderLineColorSubmenuAsync(
                outputDir,
                "freex_home_borders_line_color_submenu_opened"));

            ValidateHomeFontColorsTourEvidence(outputDir, captures);
            await WriteHomeFontColorsTourManifestAsync(outputDir, sampleRange, captures);
        }
        catch
        {
            DeleteHomeFontColorsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            fontNameBox.IsDropDownOpen = false;
            fontSizeBox.IsDropDownOpen = false;
            if (underlineButton.ContextMenu is { } underlineMenu)
                underlineMenu.IsOpen = false;
            if (bordersButton.ContextMenu is { } bordersMenu)
                bordersMenu.IsOpen = false;
        }
    }

    private GridRange EnsureHomeFontColorsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Home font/colors tour requires an active worksheet.");

        _currentSheetId = sheet.Id;

        var labels = new[]
        {
            "Calibri 11",
            "Aptos 14",
            "Grow 18",
            "Shrink 9",
            "Bold",
            "Italic",
            "Underline",
            "Double underline",
            "Strikethrough",
            "Font color",
            "Fill color",
            "Theme colors",
            "All borders",
            "Outside border",
            "Bottom double"
        };

        for (uint row = 1; row <= 5; row++)
        {
            for (uint col = 1; col <= 5; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                sheet.ClearCell(address);
                var index = (int)((row - 1) * 5 + (col - 1));
                if (index < labels.Length)
                    sheet.SetCell(address, new TextValue(labels[index]));
            }
        }

        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 1, 1), new StyleDiff(FontName: "Calibri", FontSize: 11));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 1, 2), new StyleDiff(FontName: "Aptos", FontSize: 14));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 1, 3), new StyleDiff(FontSize: FontSizePlanner.Increase(16)));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 1, 4), new StyleDiff(FontSize: FontSizePlanner.Decrease(10)));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 1, 5), new StyleDiff(Bold: true));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 2, 1), new StyleDiff(Italic: true));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 2, 2), CellStyleDiffPlanner.UnderlineDiff(true));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 2, 3), CellStyleDiffPlanner.DoubleUnderlineDiff(true));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 2, 4), CellStyleDiffPlanner.StrikethroughDiff(true));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 2, 5), new StyleDiff(FontColor: new CellColor(192, 0, 0)));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 3, 1), new StyleDiff(FillColor: new CellColor(255, 242, 204)));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 3, 2), new StyleDiff(
            FontThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
            FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, 0.6)));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 3, 3), BorderShortcutService.GetAllBorderDiff(BorderStyle.Thin, CellColor.Black));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 3, 4), new StyleDiff(
            BorderTop: new CellBorder(BorderStyle.Thick, _workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1)),
            BorderRight: new CellBorder(BorderStyle.Thick, _workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1)),
            BorderBottom: new CellBorder(BorderStyle.Thick, _workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1)),
            BorderLeft: new CellBorder(BorderStyle.Thick, _workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1))));
        ApplyHomeFontColorsTourStyle(new CellAddress(sheet.Id, 3, 5), new StyleDiff(
            BorderBottom: new CellBorder(BorderStyle.Double, _workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent2))));

        var sampleRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 5));
        SetActiveCell(sampleRange.Start);
        SetSelectionRange(sampleRange, sampleRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        return sampleRange;
    }

    private void ApplyHomeFontColorsTourStyle(CellAddress address, StyleDiff diff)
    {
        var range = new GridRange(address, address);
        if (!TryExecuteApplyStyle(range, diff, "Apply Style"))
            throw new InvalidOperationException($"Home font/colors tour could not apply style to {address}.");
    }

    private async Task<HomeFontColorsTourManifestCapture> CaptureHomeFontColorsComboPopupAsync(
        string outputDir,
        ComboBox comboBox,
        string state,
        string fileName,
        string evidencePurpose)
    {
        comboBox.Focus();
        comboBox.ApplyTemplate();
        comboBox.IsDropDownOpen = true;
        comboBox.UpdateLayout();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(350);

        try
        {
            var popupChild = FindOpenPopupChild(comboBox)
                ?? throw new InvalidOperationException($"Home font/colors tour could not locate the open {state} popup.");

            await CaptureElementAsync(popupChild, outputDir, fileName);
            return CreateHomeFontColorsElementCapture(state, fileName, evidencePurpose, popupChild.ActualWidth, popupChild.ActualHeight);
        }
        finally
        {
            comboBox.IsDropDownOpen = false;
        }
    }

    private async Task<HomeFontColorsTourManifestCapture> CaptureHomeFontColorsMenuAsync(
        string outputDir,
        ButtonBase placementTarget,
        string state,
        string fileName,
        string evidencePurpose)
    {
        var menu = placementTarget.ContextMenu
            ?? throw new InvalidOperationException($"Home font/colors tour could not locate the {state} context menu.");

        menu.PlacementTarget = placementTarget;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        menu.UpdateLayout();
        await Task.Delay(350);
        await WaitForRibbonScreenshotRenderPassAsync();

        await CaptureElementAsync(menu, outputDir, fileName);
        var capture = CreateHomeFontColorsElementCapture(state, fileName, evidencePurpose, menu.ActualWidth, menu.ActualHeight);
        menu.IsOpen = false;
        return capture;
    }

    private async Task<HomeFontColorsTourManifestCapture> CaptureHomeFontColorsBorderLineColorSubmenuAsync(string outputDir, string fileName)
    {
        var bordersButton = FindRenderedRibbonControl("Borders") as ButtonBase
            ?? throw new InvalidOperationException("Home font/colors tour could not locate the rendered Borders button.");
        var menu = bordersButton.ContextMenu
            ?? throw new InvalidOperationException("Home font/colors tour could not locate the Borders context menu.");

        menu.PlacementTarget = bordersButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        menu.UpdateLayout();
        await Task.Delay(250);

        var lineColorItem = FindMenuItemByHeader(menu.Items, UiText.Get("MainWindow_Header_LineColor"))
            ?? throw new InvalidOperationException("Home font/colors tour could not locate the Borders Line Color submenu.");
        lineColorItem.IsSubmenuOpen = true;
        lineColorItem.UpdateLayout();
        await Task.Delay(350);
        await WaitForRibbonScreenshotRenderPassAsync();

        try
        {
            var popupChild = FindOpenPopupChild(lineColorItem)
                ?? throw new InvalidOperationException("Home font/colors tour could not locate the open Borders Line Color submenu popup.");
            await CaptureElementAsync(popupChild, outputDir, fileName);
            return CreateHomeFontColorsElementCapture(
                "borders-line-color-submenu",
                fileName,
                "Borders Line Color submenu showing implemented black, gray, Accent 1, and Accent 2 theme color choices.",
                popupChild.ActualWidth,
                popupChild.ActualHeight);
        }
        finally
        {
            lineColorItem.IsSubmenuOpen = false;
            menu.IsOpen = false;
        }
    }

    private static MenuItem? FindMenuItemByHeader(ItemCollection items, string header)
    {
        foreach (var item in items)
        {
            if (item is not MenuItem menuItem)
                continue;

            if (string.Equals(menuItem.Header?.ToString(), header, StringComparison.Ordinal))
                return menuItem;

            var nested = FindMenuItemByHeader(menuItem.Items, header);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private HomeFontColorsTourManifestCapture CreateHomeFontColorsWindowCapture(string state, string fileName, string evidencePurpose)
    {
        var activeCell = SheetGrid.SelectedRange?.Start;
        var style = activeCell is { } address
            ? ResolveHomeFontColorsTourStyle(address)
            : CellStyle.Default;
        return new HomeFontColorsTourManifestCapture(
            State: state,
            FileName: $"{fileName}.png",
            CaptureKey: $"interactive:home-font-colors:{state}",
            EvidencePurpose: evidencePurpose,
            CaptureMethod: IsScreenshotTourBackgroundRenderAllowed()
                ? "RenderTargetBitmap-window-full"
                : "CopyFromScreen-window-full",
            LogicalWidth: ActualWidth,
            LogicalHeight: Math.Min(ActualHeight, 760),
            ActiveCell: activeCell?.ToString() ?? string.Empty,
            ActiveCellFontName: style.FontName,
            ActiveCellFontSize: style.FontSize,
            ActiveCellBold: style.Bold,
            ActiveCellItalic: style.Italic,
            ActiveCellUnderline: style.Underline,
            ActiveCellDoubleUnderline: style.DoubleUnderline,
            ActiveCellStrikethrough: style.Strikethrough,
            ActiveCellFontColor: FormatQatUndoRedoTourColor(style.ResolveFontColor(_workbook.Theme)),
            ActiveCellFillColor: FormatQatUndoRedoTourColor(style.ResolveFillColor(_workbook.Theme)),
            MenuHeaders: []);
    }

    private HomeFontColorsTourManifestCapture CreateHomeFontColorsElementCapture(
        string state,
        string fileName,
        string evidencePurpose,
        double width,
        double height)
    {
        return new HomeFontColorsTourManifestCapture(
            State: state,
            FileName: $"{fileName}.png",
            CaptureKey: $"interactive:home-font-colors:{state}",
            EvidencePurpose: evidencePurpose,
            CaptureMethod: "RenderTargetBitmap-wpf-element",
            LogicalWidth: width,
            LogicalHeight: height,
            ActiveCell: SheetGrid.SelectedRange?.Start.ToString() ?? string.Empty,
            ActiveCellFontName: string.Empty,
            ActiveCellFontSize: 0,
            ActiveCellBold: false,
            ActiveCellItalic: false,
            ActiveCellUnderline: false,
            ActiveCellDoubleUnderline: false,
            ActiveCellStrikethrough: false,
            ActiveCellFontColor: null,
            ActiveCellFillColor: null,
            MenuHeaders: CaptureOpenMenuHeaders());
    }

    private IReadOnlyList<string> CaptureOpenMenuHeaders()
    {
        var headers = new List<string>();
        AddMenuHeaders((FindRenderedRibbonControl("Underline") as ButtonBase)?.ContextMenu, headers);
        AddMenuHeaders((FindRenderedRibbonControl("Borders") as ButtonBase)?.ContextMenu, headers);
        return headers;
    }

    private static void AddMenuHeaders(ContextMenu? menu, List<string> headers)
    {
        if (menu is not { IsOpen: true })
            return;

        foreach (var header in menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).Where(header => !string.IsNullOrWhiteSpace(header)))
            headers.Add(header!);
    }

    private CellStyle ResolveHomeFontColorsTourStyle(CellAddress address)
    {
        var sheet = _workbook.GetSheet(address.Sheet)
            ?? throw new InvalidOperationException("Home font/colors tour could not resolve the active worksheet.");
        var cell = sheet.GetCell(address);
        return _workbook.GetStyle(cell?.StyleId ?? StyleId.Default);
    }

    private static void DeleteHomeFontColorsTourEvidence(string outputDir)
    {
        foreach (var fileName in HomeFontColorsTourExpectedFileNames().Append(HomeFontColorsTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static IReadOnlyList<string> HomeFontColorsTourExpectedFileNames() =>
    [
        "freex_home_font_colors_grid_styled.png",
        "freex_home_font_family_dropdown_opened.png",
        "freex_home_font_size_dropdown_opened.png",
        "freex_home_underline_menu_opened.png",
        "freex_home_borders_full_menu_opened.png",
        "freex_home_borders_line_color_submenu_opened.png"
    ];

    private static void ValidateHomeFontColorsTourEvidence(string outputDir, IReadOnlyList<HomeFontColorsTourManifestCapture> captures)
    {
        if (captures.Count != HomeFontColorsTourExpectedFileNames().Count)
            throw new InvalidOperationException("Home font/colors tour did not create the planned capture count.");

        foreach (var fileName in HomeFontColorsTourExpectedFileNames())
        {
            var path = Path.Combine(outputDir, fileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Home font/colors tour did not create {fileName}.");
        }
    }

    private async Task CaptureHomeStylesConditionalFormattingTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeStylesConditionalFormattingTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 820;
        await Task.Delay(700);

        var context = EnsureHomeStylesConditionalFormattingTourContext();
        var captures = new List<HomeStylesConditionalFormattingTourManifestCapture>();
        Window? openDialog = null;

        try
        {
            SelectHomeStylesConditionalFormattingRibbonTab();
            SetSelectionRange(context.ResultRange, context.ResultRange.Start);
            UpdateViewport();
            RefreshToolbar();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(350);

            await CaptureCurrentWindowAsync(outputDir, "freex_home_styles_cf_grid_result", 760);
            captures.Add(CreateHomeStylesConditionalFormattingCapture(
                "grid-result",
                "Home Styles result",
                "freex_home_styles_cf_grid_result",
                "RenderTargetBitmap-window-full",
                ActualWidth,
                Math.Min(ActualHeight, 760),
                [],
                "Grid result showing a real structured table, seeded conditional-format rules, and representative Cell Style preset cells."));

            captures.Add(await CaptureHomeStylesConditionalFormattingMenuAsync(
                outputDir,
                "Conditional Formatting",
                "conditional-formatting-menu-opened",
                "Conditional Formatting menu",
                "freex_home_styles_cf_conditional_formatting_menu_opened",
                "Production Home Conditional Formatting menu with highlight rules, top/bottom rules, data bars, color scales, icon sets, new/clear/manage rule commands."));

            captures.Add(await CaptureHomeStylesConditionalFormattingDataBarsSubmenuAsync(
                outputDir,
                "freex_home_styles_cf_data_bars_submenu_opened"));

            openDialog = new ManageConditionalFormatsDialog(
                context.Sheet,
                context.ConditionalFormatRange,
                _ => { },
                _ => { })
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureHomeStylesConditionalFormattingDialogAsync(
                openDialog,
                outputDir,
                "conditional-formatting-manager",
                "Conditional Formatting Rules Manager",
                "freex_home_styles_cf_manage_rules_dialog",
                "Conditional Formatting Rules Manager opened against the seeded score range with the real data bar and greater-than rules visible."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            captures.Add(await CaptureHomeStylesConditionalFormattingMenuAsync(
                outputDir,
                "Format as Table",
                "format-as-table-gallery-opened",
                "Format as Table gallery",
                "freex_home_styles_cf_format_as_table_gallery_opened",
                "Production Format as Table gallery populated from TableStyleGalleryPlanner with Light, Medium, and Dark style sections and theme-backed swatches."));

            captures.Add(await CaptureHomeStylesConditionalFormattingMenuAsync(
                outputDir,
                "Cell Styles",
                "cell-styles-gallery-opened",
                "Cell Styles gallery",
                "freex_home_styles_cf_cell_styles_gallery_opened",
                "Production Cell Styles menu with Normal, Good/Bad/Neutral, data/model styles, headings, note/warning/total, and accent depth presets."));

            ValidateHomeStylesConditionalFormattingTourEvidence(outputDir, captures);
            await WriteHomeStylesConditionalFormattingTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteHomeStylesConditionalFormattingTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openDialog is { IsVisible: true })
                CloseDataToolsTourDialog(openDialog);

            CloseHomeStylesConditionalFormattingMenus();
        }
    }

    private HomeStylesConditionalFormattingTourContext EnsureHomeStylesConditionalFormattingTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Home Styles/Conditional Formatting tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        sheet.StructuredTables.Clear();
        sheet.ConditionalFormats.Clear();

        for (uint row = 1; row <= 12; row++)
        {
            for (uint col = 1; col <= 7; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        var rows = new (string Region, string Owner, double Score, string Status)[]
        {
            ("North", "Ari", 1820, "Good"),
            ("South", "Bo", 940, "Watch"),
            ("East", "Cai", 1515, "Good"),
            ("West", "Dee", 1280, "Neutral"),
            ("Central", "Eli", 1688, "Good")
        };

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Owner"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Status"));
        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)(index + 2);
            var item = rows[index];
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(item.Region));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(item.Owner));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(item.Score));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new TextValue(item.Status));
        }

        sheet.SetCell(new CellAddress(sheet.Id, 8, 1), new TextValue("Cell Styles"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 2), new TextValue("Good"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 3), new TextValue("Bad"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 4), new TextValue("Neutral"));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 2), new TextValue("Pass"));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 3), new TextValue("Risk"));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 4), new TextValue("Review"));

        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        var conditionalFormatRange = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 6, 3));
        var styleRange = new GridRange(new CellAddress(sheet.Id, 8, 1), new CellAddress(sheet.Id, 9, 4));
        var resultRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 9, 4));
        const string tableStyleName = "TableStyleMedium4";

        ApplyHomeStylesConditionalFormattingStyle(
            new GridRange(new CellAddress(sheet.Id, 8, 1), new CellAddress(sheet.Id, 8, 4)),
            CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Heading2, _workbook.Theme));
        ApplyHomeStylesConditionalFormattingStyle(
            new GridRange(new CellAddress(sheet.Id, 9, 2), new CellAddress(sheet.Id, 9, 2)),
            CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Good, _workbook.Theme));
        ApplyHomeStylesConditionalFormattingStyle(
            new GridRange(new CellAddress(sheet.Id, 9, 3), new CellAddress(sheet.Id, 9, 3)),
            CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Bad, _workbook.Theme));
        ApplyHomeStylesConditionalFormattingStyle(
            new GridRange(new CellAddress(sheet.Id, 9, 4), new CellAddress(sheet.Id, 9, 4)),
            CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Neutral, _workbook.Theme));

        if (!TableStyleGalleryPlanner.TryGetOption(tableStyleName, _workbook.Theme, out var tableStyle))
            tableStyle = TableStyleGalleryPlanner.GetOption(0, _workbook.Theme);

        ExecuteHomeStylesConditionalFormattingCommand(
            new CreateStyledStructuredTableCommand(sheet.Id, tableRange, tableStyle.StyleName, firstRowHasHeaders: true, tableStyle.Banding),
            "Format as Table");

        var greaterThanRule = new ConditionalFormat
        {
            AppliesTo = conditionalFormatRange,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThanOrEqual,
            Value1 = "1600",
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FontColor = new CellColor(156, 87, 0),
                FillColor = new CellColor(255, 235, 156)
            }
        };
        ExecuteHomeStylesConditionalFormattingCommand(
            new ApplyConditionalFormatCommand(sheet.Id, greaterThanRule),
            "Conditional Formatting");

        var dataBarRule = ConditionalFormatPresetGalleryPlanner.CreateDataBarRule("SolidBlue", conditionalFormatRange)
            ?? throw new InvalidOperationException("Home Styles/Conditional Formatting tour could not create the data bar rule.");
        dataBarRule.Priority = 2;
        ExecuteHomeStylesConditionalFormattingCommand(
            new ApplyConditionalFormatCommand(sheet.Id, dataBarRule),
            "Conditional Formatting");

        SetSelectionRange(resultRange, resultRange.Start);
        EnsureCellVisible(resultRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new HomeStylesConditionalFormattingTourContext(
            sheet,
            tableRange,
            conditionalFormatRange,
            styleRange,
            resultRange,
            tableStyle.StyleName);
    }

    private void ApplyHomeStylesConditionalFormattingStyle(GridRange range, StyleDiff diff)
    {
        if (!TryExecuteApplyStyle(range, diff, "Apply Style"))
            throw new InvalidOperationException($"Home Styles/Conditional Formatting tour could not apply style to {range}.");
    }

    private void ExecuteHomeStylesConditionalFormattingCommand(IWorkbookCommand command, string title)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException($"Home Styles/Conditional Formatting tour failed to apply '{title}': {outcome.ErrorMessage}");
    }

    private void SelectHomeStylesConditionalFormattingRibbonTab()
    {
        var homeTab = RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home");
        SelectRibbonTourTab(homeTab);
    }

    private async Task<HomeStylesConditionalFormattingTourManifestCapture> CaptureHomeStylesConditionalFormattingMenuAsync(
        string outputDir,
        string commandName,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        SelectHomeStylesConditionalFormattingRibbonTab();
        if (string.Equals(commandName, "Format as Table", StringComparison.Ordinal))
            PopulateFormatTableGalleryMenu();

        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName)
            ?? throw new InvalidOperationException($"Home Styles/Conditional Formatting tour could not find '{commandName}' ribbon button.");
        var menu = button.ContextMenu
            ?? throw new InvalidOperationException($"Home Styles/Conditional Formatting tour could not find '{commandName}' context menu.");

        OpenRibbonContextMenu(button, menu);
        await Task.Delay(350);
        menu.UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        await CaptureElementAsync(menu, outputDir, fileName);
        var headers = CaptureHomeStylesConditionalFormattingMenuHeaders(menu.Items);
        var capture = CreateHomeStylesConditionalFormattingCapture(
            state,
            surface,
            fileName,
            "RenderTargetBitmap-home-styles-context-menu",
            menu.ActualWidth,
            menu.ActualHeight,
            headers,
            evidenceSummary);
        menu.IsOpen = false;
        return capture;
    }

    private async Task<HomeStylesConditionalFormattingTourManifestCapture> CaptureHomeStylesConditionalFormattingDataBarsSubmenuAsync(
        string outputDir,
        string fileName)
    {
        SelectHomeStylesConditionalFormattingRibbonTab();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, "Conditional Formatting")
            ?? throw new InvalidOperationException("Home Styles/Conditional Formatting tour could not find the Conditional Formatting ribbon button.");
        var menu = button.ContextMenu
            ?? throw new InvalidOperationException("Home Styles/Conditional Formatting tour could not find the Conditional Formatting context menu.");

        OpenRibbonContextMenu(button, menu);
        await Task.Delay(250);
        var dataBarsItem = FindMenuItemByCommandName(menu.Items, "Data Bars")
            ?? throw new InvalidOperationException("Home Styles/Conditional Formatting tour could not find the Data Bars submenu.");
        PopulateConditionalFormatDataBarGallery(dataBarsItem);
        dataBarsItem.IsSubmenuOpen = true;
        dataBarsItem.UpdateLayout();
        await Task.Delay(350);
        await WaitForRibbonScreenshotRenderPassAsync();

        try
        {
            var popupChild = FindOpenPopupChild(dataBarsItem)
                ?? throw new InvalidOperationException("Home Styles/Conditional Formatting tour could not locate the open Data Bars submenu popup.");
            await CaptureElementAsync(popupChild, outputDir, fileName);
            return CreateHomeStylesConditionalFormattingCapture(
                "data-bars-submenu-opened",
                "Conditional Formatting Data Bars submenu",
                fileName,
                "RenderTargetBitmap-home-styles-context-submenu",
                popupChild.ActualWidth,
                popupChild.ActualHeight,
                CaptureHomeStylesConditionalFormattingMenuHeaders(dataBarsItem.Items),
                "Data Bars preset gallery with gradient, solid fill, swatches, keytip metadata, and More Rules entry populated by ConditionalFormatPresetGalleryPlanner.");
        }
        finally
        {
            dataBarsItem.IsSubmenuOpen = false;
            menu.IsOpen = false;
        }
    }

    private async Task<HomeStylesConditionalFormattingTourManifestCapture> CaptureHomeStylesConditionalFormattingDialogAsync(
        Window dialog,
        string outputDir,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        return CreateHomeStylesConditionalFormattingCapture(
            state,
            surface,
            fileName,
            "RenderTargetBitmap-home-styles-dialog-window",
            dialog.ActualWidth,
            dialog.ActualHeight,
            [],
            evidenceSummary);
    }

    private HomeStylesConditionalFormattingTourManifestCapture CreateHomeStylesConditionalFormattingCapture(
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        IReadOnlyList<string> menuHeaders,
        string evidenceSummary)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return new HomeStylesConditionalFormattingTourManifestCapture(
            CaptureKey: $"interactive:home-styles-cf:{state}",
            PairKey: $"interactive:home-styles-cf:{state}",
            ScenarioId: "home-styles-cf:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SelectedRange: SheetGrid.SelectedRange?.ToString() ?? string.Empty,
            StructuredTableCount: sheet?.StructuredTables.Count ?? 0,
            ConditionalFormatRuleCount: sheet?.ConditionalFormats.Count ?? 0,
            MenuHeaders: menuHeaders,
            EvidenceSummary: evidenceSummary);
    }

    private static MenuItem? FindMenuItemByCommandName(ItemCollection items, string commandName)
    {
        foreach (var item in items)
        {
            if (item is not MenuItem menuItem)
                continue;

            if (RibbonMetadata.TryGetCommandName(menuItem, out var candidate) &&
                string.Equals(candidate, commandName, StringComparison.Ordinal))
            {
                return menuItem;
            }

            var nested = FindMenuItemByCommandName(menuItem.Items, commandName);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static IReadOnlyList<string> CaptureHomeStylesConditionalFormattingMenuHeaders(ItemCollection items)
    {
        var headers = new List<string>();
        foreach (var item in items)
        {
            if (item is not MenuItem menuItem)
                continue;

            if (RibbonMetadata.TryGetCommandName(menuItem, out var commandName))
            {
                headers.Add(commandName);
                continue;
            }

            var header = ExtractMenuHeaderText(menuItem.Header);
            if (!string.IsNullOrWhiteSpace(header))
                headers.Add(header);
        }

        return headers;
    }

    private static string ExtractMenuHeaderText(object? header)
    {
        if (header is null)
            return string.Empty;

        if (header is string text)
            return text;

        if (header is TextBlock textBlock)
            return textBlock.Text;

        if (header is Panel panel)
        {
            return string.Join(" ", panel.Children
                .OfType<TextBlock>()
                .Select(child => child.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        return header.ToString() ?? string.Empty;
    }

    private void CloseHomeStylesConditionalFormattingMenus()
    {
        foreach (var commandName in new[] { "Conditional Formatting", "Format as Table", "Cell Styles" })
        {
            var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName);
            if (button?.ContextMenu is { } menu)
                menu.IsOpen = false;
        }
    }

    private static void DeleteHomeStylesConditionalFormattingTourEvidence(string outputDir)
    {
        foreach (var fileName in HomeStylesConditionalFormattingTourExpectedFileNames().Append(HomeStylesConditionalFormattingTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static IReadOnlyList<string> HomeStylesConditionalFormattingTourExpectedFileNames() =>
    [
        "freex_home_styles_cf_grid_result.png",
        "freex_home_styles_cf_conditional_formatting_menu_opened.png",
        "freex_home_styles_cf_data_bars_submenu_opened.png",
        "freex_home_styles_cf_manage_rules_dialog.png",
        "freex_home_styles_cf_format_as_table_gallery_opened.png",
        "freex_home_styles_cf_cell_styles_gallery_opened.png"
    ];

    private static void ValidateHomeStylesConditionalFormattingTourEvidence(
        string outputDir,
        IReadOnlyList<HomeStylesConditionalFormattingTourManifestCapture> captures)
    {
        if (captures.Count != HomeStylesConditionalFormattingTourExpectedFileNames().Count)
            throw new InvalidOperationException("Home Styles/Conditional Formatting tour did not create the planned capture count.");

        foreach (var fileName in HomeStylesConditionalFormattingTourExpectedFileNames())
        {
            var path = Path.Combine(outputDir, fileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Home Styles/Conditional Formatting tour did not create {fileName}.");
        }
    }

    private async Task CaptureHomeClipboardCellsEditingTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeClipboardCellsEditingTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1320;
        Height = 820;
        await Task.Delay(700);

        var context = EnsureHomeClipboardCellsEditingTourContext();
        var captures = new List<HomeClipboardCellsEditingTourManifestCapture>();

        try
        {
            SelectHomeClipboardCellsEditingRibbonTab();
            SetSelectionRange(context.PasteTargetRange, context.PasteTargetRange.Start);
            UpdateViewport();
            RefreshToolbar();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(350);

            await CaptureCurrentWindowAsync(outputDir, "freex_home_clipboard_cells_editing_clipboard_copied_state", 760);
            captures.Add(CreateHomeClipboardCellsEditingCapture(
                "UI-CMD-HOME-CLIP-001",
                "clipboard-copied-state",
                "Home Clipboard",
                "freex_home_clipboard_cells_editing_clipboard_copied_state",
                "RenderTargetBitmap-window-full",
                ActualWidth,
                Math.Min(ActualHeight, 760),
                [],
                "Home tab with a deterministic copied source range and paste target selection visible on the grid."));

            captures.Add(await CaptureHomeClipboardCellsEditingMenuAsync(
                outputDir,
                "UI-CMD-HOME-CLIP-002",
                "paste-menu-opened",
                "Paste",
                "freex_home_clipboard_cells_editing_paste_menu_opened",
                "Paste split menu with Paste, Values, Formulas, Formatting, column widths, transpose, link, picture, linked picture, and Paste Special entries."));

            captures.Add(await CaptureHomeClipboardCellsEditingMenuAsync(
                outputDir,
                "UI-CMD-HOME-CELLS-001",
                "insert-menu-opened",
                "Insert",
                "freex_home_clipboard_cells_editing_insert_menu_opened",
                "Cells Insert menu with Insert Cells, sheet rows, sheet columns, and sheet insertion commands."));

            captures.Add(await CaptureHomeClipboardCellsEditingMenuAsync(
                outputDir,
                "UI-CMD-HOME-CELLS-002",
                "delete-menu-opened",
                "Delete",
                "freex_home_clipboard_cells_editing_delete_menu_opened",
                "Cells Delete menu with Delete Cells, sheet rows, sheet columns, and sheet deletion commands."));

            captures.Add(await CaptureHomeClipboardCellsEditingMenuAsync(
                outputDir,
                "UI-CMD-HOME-CELLS-003",
                "format-menu-opened",
                "Format",
                "freex_home_clipboard_cells_editing_format_menu_opened",
                "Cells Format menu with row/column sizing, hide/unhide, sheet actions, protection, lock-cell, and Format Cells entries."));

            captures.Add(await CaptureHomeClipboardCellsEditingMenuAsync(
                outputDir,
                "UI-CMD-HOME-EDIT-003",
                "clear-menu-opened",
                "Clear",
                "freex_home_clipboard_cells_editing_clear_menu_opened",
                "Editing Clear menu with Clear All, Formats, Contents, Comments and Notes, and Hyperlinks entries."));

            SetSelectionRange(context.SortRange, context.SortRange.Start);
            UpdateViewport();
            RefreshToolbar();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            captures.Add(await CaptureHomeClipboardCellsEditingMenuAsync(
                outputDir,
                "UI-CMD-HOME-EDIT-004",
                "sort-filter-menu-opened",
                "Sort & Filter",
                "freex_home_clipboard_cells_editing_sort_filter_menu_opened",
                "Editing Sort & Filter menu with A-to-Z, Z-to-A, Custom Sort, Filter, Clear, and Reapply entries."));

            captures.Add(await CaptureHomeClipboardCellsEditingMenuAsync(
                outputDir,
                "UI-CMD-HOME-EDIT-004",
                "find-select-menu-opened",
                "Find & Select",
                "freex_home_clipboard_cells_editing_find_select_menu_opened",
                "Editing Find & Select menu with Find, Replace, Go To, Go To Special, formulas, notes, conditional formatting, constants, data validation, objects, and selection pane entries."));

            captures.Add(await CaptureHomeClipboardCellsEditingDialogAsync(
                new CellShiftDialog(CellShiftDialogMode.Insert) { Owner = this },
                outputDir,
                "UI-CMD-HOME-CELLS-001",
                "insert-cells-dialog",
                "Insert Cells dialog",
                "freex_home_clipboard_cells_editing_insert_cells_dialog",
                "Insert Cells shift-choice dialog opened in its default Shift cells right state."));

            captures.Add(await CaptureHomeClipboardCellsEditingDialogAsync(
                new CellShiftDialog(CellShiftDialogMode.Delete) { Owner = this },
                outputDir,
                "UI-CMD-HOME-CELLS-002",
                "delete-cells-dialog",
                "Delete Cells dialog",
                "freex_home_clipboard_cells_editing_delete_cells_dialog",
                "Delete Cells shift-choice dialog opened in its default Shift cells left state."));

            captures.Add(await CaptureHomeClipboardCellsEditingDialogAsync(
                CreateHomeClipboardCellsEditingSortDialog(context),
                outputDir,
                "UI-CMD-HOME-EDIT-004",
                "custom-sort-dialog",
                "Custom Sort dialog",
                "freex_home_clipboard_cells_editing_custom_sort_dialog",
                "Custom Sort dialog for the seeded header range with sort levels, Sort On, Order, color column, options, and My data has headers controls."));

            captures.Add(await CaptureHomeClipboardCellsEditingDialogAsync(
                CreateHomeClipboardCellsEditingFindReplaceDialog(replaceMode: false),
                outputDir,
                "UI-CMD-HOME-EDIT-004",
                "find-dialog",
                "Find dialog",
                "freex_home_clipboard_cells_editing_find_dialog",
                "Find dialog opened on the Find tab with search box, options, Find Next, Find All, and results grid surfaces."));

            captures.Add(await CaptureHomeClipboardCellsEditingDialogAsync(
                CreateHomeClipboardCellsEditingFindReplaceDialog(replaceMode: true),
                outputDir,
                "UI-CMD-HOME-EDIT-004",
                "replace-dialog",
                "Replace dialog",
                "freex_home_clipboard_cells_editing_replace_dialog",
                "Find and Replace dialog opened on the Replace tab with find/replace fields, replace actions, format pickers, options, and results grid surfaces."));

            captures.Add(await CaptureHomeClipboardCellsEditingDialogAsync(
                new GoToDialog(_currentSheetId, context.GoToDefaultAddress, _workbook.NamedRanges, [context.CopySourceRange.Start.ToA1(), "HomeTourData"]) { Owner = this },
                outputDir,
                "UI-CMD-HOME-EDIT-004",
                "go-to-dialog",
                "Go To dialog",
                "freex_home_clipboard_cells_editing_go_to_dialog",
                "Go To dialog with recent references, defined name, reference box default focus/select-all behavior, Special, OK, and Cancel controls."));

            captures.Add(await CaptureHomeClipboardCellsEditingDialogAsync(
                new GoToSpecialDialog { Owner = this },
                outputDir,
                "UI-CMD-HOME-EDIT-004",
                "go-to-special-dialog",
                "Go To Special dialog",
                "freex_home_clipboard_cells_editing_go_to_special_dialog",
                "Go To Special dialog with blanks, constants, formulas, comments, current region, differences, last cell, conditional formats, objects, precedents, dependents, data validation, and visible-cells choices."));

            ValidateHomeClipboardCellsEditingTourEvidence(outputDir, captures);
            await WriteHomeClipboardCellsEditingTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteHomeClipboardCellsEditingTourEvidence(outputDir);
            throw;
        }
        finally
        {
            ClearClipboardVisualState();
            _internalClipboard = null;
        }
    }

    private HomeClipboardCellsEditingTourContext EnsureHomeClipboardCellsEditingTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Home Clipboard/Cells/Editing tour requires an active worksheet.");

        _currentSheetId = sheet.Id;

        for (uint row = 1; row <= 14; row++)
        {
            for (uint col = 1; col <= 9; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        var headers = new[] { "Region", "Rep", "Q1", "Q2", "Total", "Status" };
        for (var index = 0; index < headers.Length; index++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(index + 1)), new TextValue(headers[index]));

        var rows = new (string Region, string Rep, double Q1, double Q2, string Status)[]
        {
            ("East", "Ari", 1200, 1380, "Open"),
            ("West", "Bo", 950, 1125, "Closed"),
            ("North", "Cai", 1440, 1510, "Open"),
            ("South", "Dee", 875, 990, "Review"),
            ("East", "Eli", 1680, 1725, "Open"),
            ("West", "Fox", 1010, 1088, "Closed")
        };

        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)(index + 2);
            var item = rows[index];
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(item.Region));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(item.Rep));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(item.Q1));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(item.Q2));
            sheet.SetFormula(new CellAddress(sheet.Id, row, 5), $"C{row}+D{row}");
            sheet.SetCell(new CellAddress(sheet.Id, row, 6), new TextValue(item.Status));
        }

        sheet.SetCell(new CellAddress(sheet.Id, 9, 1), new TextValue("Paste target"));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 2), new TextValue("Copied marquee is seeded without touching the OS clipboard."));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("Find token"));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 2), new TextValue("Ari"));
        sheet.Comments[new CellAddress(sheet.Id, 4, 2)] = "Home tour note for Clear and Find Notes.";
        sheet.Hyperlinks[new CellAddress(sheet.Id, 5, 2)] = "https://example.test/freex-home-tour";

        var copySourceRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 3));
        var pasteTargetRange = new GridRange(new CellAddress(sheet.Id, 9, 4), new CellAddress(sheet.Id, 11, 6));
        var sortRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 6));
        var usedRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 11, 6));

        ApplyHomeClipboardCellsEditingTourStyle(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 6)),
            new StyleDiff(Bold: true, FillColor: new CellColor(217, 225, 242)));
        ApplyHomeClipboardCellsEditingTourStyle(
            copySourceRange,
            new StyleDiff(FillColor: new CellColor(226, 239, 218)));
        ApplyHomeClipboardCellsEditingTourStyle(
            pasteTargetRange,
            new StyleDiff(FillColor: new CellColor(255, 242, 204)));

        _workbook.DefineNamedRange("HomeTourData", sortRange);
        // R118: this bypasses the command bus (unlike the real Define Name flows in
        // MainWindow.Editing.cs/FormulaCommands.cs/NamedRangeDialog.xaml.cs), so the Name Box's
        // revision-keyed range index (MainWindow.WorkbookUiState.cs EnsureNameBoxRangeIndex) would
        // not otherwise learn this name was added until some later command bumps the revision.
        InvalidateNavigationCaches();
        SeedHomeClipboardCellsEditingInternalClipboard(sheet, copySourceRange);
        SetSelectionRange(pasteTargetRange, pasteTargetRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();

        return new HomeClipboardCellsEditingTourContext(
            sheet,
            copySourceRange,
            pasteTargetRange,
            sortRange,
            usedRange,
            pasteTargetRange.Start.ToA1());
    }

    private void ApplyHomeClipboardCellsEditingTourStyle(GridRange range, StyleDiff diff)
    {
        if (!TryExecuteApplyStyle(range, diff, "Apply Style"))
            throw new InvalidOperationException($"Home Clipboard/Cells/Editing tour could not apply style to {range}.");
    }

    private void SeedHomeClipboardCellsEditingInternalClipboard(Sheet sheet, GridRange copySourceRange)
    {
        var cells = new List<(CellAddress Source, Cell Cell)>();
        for (var row = copySourceRange.Start.Row; row <= copySourceRange.End.Row; row++)
        {
            for (var col = copySourceRange.Start.Col; col <= copySourceRange.End.Col; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                cells.Add((address, sheet.GetCell(row, col)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
            }
        }

        _internalClipboard = new InternalClipboard(
            copySourceRange,
            cells,
            [],
            "Region\tRep\tQ1\r\nEast\tAri\t1200\r\nWest\tBo\t950",
            IsCut: false);
        SheetGrid.ClipboardRange = copySourceRange;
        SheetGrid.ClipboardIsCut = false;
    }

    private void SelectHomeClipboardCellsEditingRibbonTab()
    {
        var homeTab = RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home");
        SelectRibbonTourTab(homeTab);
    }

    private async Task<HomeClipboardCellsEditingTourManifestCapture> CaptureHomeClipboardCellsEditingMenuAsync(
        string outputDir,
        string catalogCommandRow,
        string state,
        string commandName,
        string fileName,
        string evidenceSummary)
    {
        SelectHomeClipboardCellsEditingRibbonTab();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName)
            ?? throw new InvalidOperationException($"Home Clipboard/Cells/Editing tour could not find '{commandName}' ribbon button.");
        var menu = button.ContextMenu
            ?? throw new InvalidOperationException($"Home Clipboard/Cells/Editing tour could not find '{commandName}' context menu.");

        OpenRibbonContextMenu(button, menu);
        await Task.Delay(350);
        menu.UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        await CaptureElementAsync(menu, outputDir, fileName);
        var headers = new List<string>();
        AddMenuHeaders(menu, headers);
        var capture = CreateHomeClipboardCellsEditingCapture(
            catalogCommandRow,
            state,
            commandName,
            fileName,
            "RenderTargetBitmap-home-context-menu",
            menu.ActualWidth,
            menu.ActualHeight,
            headers,
            evidenceSummary);
        menu.IsOpen = false;
        return capture;
    }

    private SortDialog CreateHomeClipboardCellsEditingSortDialog(HomeClipboardCellsEditingTourContext context)
    {
        var sheet = context.Sheet;
        var dialog = new SortDialog(
            columnChoices: SortDialog.BuildColumnChoices(sheet, context.SortRange, hasHeaders: true),
            genericColumnChoices: SortDialog.BuildColumnChoices(sheet, context.SortRange, hasHeaders: false),
            rowChoices: SortDialog.BuildRowChoices(context.SortRange),
            colorChoices: SortDialog.BuildColorChoices(_workbook, sheet, context.SortRange),
            cellColorChoices: SortDialog.BuildColorChoices(_workbook, sheet, context.SortRange, SortOn.CellColor),
            fontColorChoices: SortDialog.BuildColorChoices(_workbook, sheet, context.SortRange, SortOn.FontColor),
            iconWorkbook: _workbook,
            iconSheet: sheet,
            iconRange: context.SortRange)
        {
            Owner = this
        };
        return dialog;
    }

    private FindReplaceDialog CreateHomeClipboardCellsEditingFindReplaceDialog(bool replaceMode) =>
        new(
            () => _workbook,
            _commandBus,
            NavigateToCell,
            replaceMode,
            () => _currentSheetId,
            () => SheetGrid.SelectedRange?.Start,
            RefreshAfterFindReplaceEdit)
        {
            Owner = this
        };

    private async Task<HomeClipboardCellsEditingTourManifestCapture> CaptureHomeClipboardCellsEditingDialogAsync(
        Window dialog,
        string outputDir,
        string catalogCommandRow,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        try
        {
            await ShowDataToolsTourDialogAsync(dialog);
            await CaptureElementAsync(dialog, outputDir, fileName);
            return CreateHomeClipboardCellsEditingCapture(
                catalogCommandRow,
                state,
                surface,
                fileName,
                "RenderTargetBitmap-home-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                [],
                evidenceSummary);
        }
        finally
        {
            CloseDataToolsTourDialog(dialog);
        }
    }

    private HomeClipboardCellsEditingTourManifestCapture CreateHomeClipboardCellsEditingCapture(
        string catalogCommandRow,
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        IReadOnlyList<string> menuHeaders,
        string evidenceSummary)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return new HomeClipboardCellsEditingTourManifestCapture(
            CaptureKey: $"interactive:home-clipboard-cells-editing:{state}",
            PairKey: $"interactive:home-clipboard-cells-editing:{state}",
            CatalogCommandRow: catalogCommandRow,
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SelectedRange: SheetGrid.SelectedRange?.ToString() ?? string.Empty,
            ClipboardRange: SheetGrid.ClipboardRange?.ToString() ?? string.Empty,
            ClipboardIsCut: SheetGrid.ClipboardIsCut,
            NoteCount: sheet?.Comments.Count ?? 0,
            HyperlinkCount: sheet?.Hyperlinks.Count ?? 0,
            MenuHeaders: menuHeaders,
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteHomeClipboardCellsEditingTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_home_clipboard_cells_editing_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, HomeClipboardCellsEditingTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateHomeClipboardCellsEditingTourEvidence(
        string outputDir,
        IReadOnlyList<HomeClipboardCellsEditingTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Home Clipboard/Cells/Editing tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task CaptureWorksheetContextMenuTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteWorksheetContextMenuTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var address = EnsureWorksheetContextMenuTourContext();
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);

        ContextMenu? menu = null;
        try
        {
            OnGridContextMenuRequested(address, GetKeyboardContextMenuGridPoint(address));
            await Task.Delay(350);
            menu = SheetGrid.ContextMenu
                ?? throw new InvalidOperationException("Worksheet context menu tour could not locate the open context menu.");
            menu.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureElementAsync(menu, outputDir, WorksheetContextMenuTourCaptureFileName);
            ValidateWorksheetContextMenuTourEvidence(outputDir);
            await WriteWorksheetContextMenuTourManifestAsync(outputDir, menu, address);
        }
        catch
        {
            DeleteWorksheetContextMenuTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (menu is not null)
                menu.IsOpen = false;
        }
    }

    private CellAddress EnsureWorksheetContextMenuTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Worksheet context menu tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Worksheet context menu"));
        sheet.ClearCell(new CellAddress(sheet.Id, 1, 2));
        SetActiveCell(address);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(address, address);
            SheetGrid.SelectedRanges = null;
        }

        return address;
    }

    private static void DeleteWorksheetContextMenuTourEvidence(string outputDir)
    {
        foreach (var fileName in new[]
        {
            $"{WorksheetContextMenuTourCaptureFileName}.png",
            WorksheetContextMenuTourManifestFileName
        })
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateWorksheetContextMenuTourEvidence(string outputDir)
    {
        var path = Path.Combine(outputDir, $"{WorksheetContextMenuTourCaptureFileName}.png");
        if (!File.Exists(path))
            throw new InvalidOperationException("Worksheet context menu tour did not create the planned FreeX context menu capture.");
    }

    private async Task CapturePrintPreviewTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeletePrintPreviewTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var sheet = EnsurePrintPreviewTourContext();
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);

        OpenPrintBackstage();
        UpdateLayout();
        await Task.Delay(350);
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, "freex_print_backstage_file_print_entry", 760);

        var totalPages = Math.Max(1, PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService).Pages.Count);
        var initialPreview = CreatePrintPreviewTourDialog();
        try
        {
            initialPreview.Show();
            initialPreview.Activate();
            initialPreview.UpdateLayout();
            await Task.Delay(550);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureWindowElementForScreenshotTourAsync(initialPreview, outputDir, "freex_print_preview_ctrlp_entry_opened");
        }
        finally
        {
            initialPreview.Close();
        }

        var dialog = CreatePrintPreviewTourDialog();
        var closedViaEscape = false;
        var focusReturned = false;
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await Task.Delay(550);
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_print_preview_toolbar_first_page");

            var pageNumberBox = FindDescendantByAutomationId<TextBox>(dialog, "PrintPreviewPageNumberBox");
            if (pageNumberBox is not null && totalPages > 1)
            {
                pageNumberBox.Text = totalPages.ToString(System.Globalization.CultureInfo.InvariantCulture);
                pageNumberBox.Focus();
                Keyboard.Focus(pageNumberBox);
                NavigationCommands.GoToPage.Execute(null, pageNumberBox);
                await Task.Delay(350);
                await WaitForRibbonScreenshotRenderPassAsync();
                await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_print_preview_toolbar_last_page");
            }

            var zoomBox = FindDescendantByAutomationId<ComboBox>(dialog, "PrintPreviewZoomBox");
            if (zoomBox is not null)
            {
                zoomBox.SelectedItem = UiText.Get("PrintPreview_ZoomPageWidth");
                await Task.Delay(350);
                await WaitForRibbonScreenshotRenderPassAsync();
                await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_print_preview_zoom_settings_summary");
            }

            closedViaEscape = ClosePrintPreviewTourDialogWithEscape(dialog);
            await Task.Delay(350);
            Activate();
            SsBackstagePrintNowButton.Focus();
            Keyboard.Focus(SsBackstagePrintNowButton);
            focusReturned = IsActive && Keyboard.FocusedElement == SsBackstagePrintNowButton;
            await CaptureCurrentWindowAsync(outputDir, "freex_print_preview_closed_focus_return", 760);
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
        }

        ValidatePrintPreviewTourEvidence(outputDir, totalPages);
        await WritePrintPreviewTourManifestAsync(outputDir, sheet, totalPages, closedViaEscape, focusReturned);
    }

    private PrintPreviewDialog CreatePrintPreviewTourDialog()
    {
        var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);
        var sheet = _workbook.GetSheet(_currentSheetId);
        var settings = sheet is null
            ? new PrintSettingsPlan([UiText.Get("MainWindowPrintSettings_ActiveSheet")])
            : PrintSettingsPlanner.Build(sheet, textResolver: WpfPrintSettingsTextResolver.Instance);
        return new PrintPreviewDialog(
            _workbook.Name,
            doc,
            settings,
            showMargins: () => PageMarginsBtn_Click(this, new RoutedEventArgs()),
            showPageSetup: () => PageSetupDialogBtn_Click(this, new RoutedEventArgs()),
            refreshPreviewWithSettings: BuildActiveSheetPrintPreview,
            sheetId: _currentSheetId,
            sheet: sheet,
            executeCommand: cmd => TryExecuteCommand(cmd, "Print Settings"))
        {
            Owner = this,
            Width = 2600,
            Height = 820,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
    }

    private Sheet EnsurePrintPreviewTourContext()
    {
        var sheet = _workbook.GetSheet(_currentSheetId) ?? _workbook.Sheets.FirstOrDefault();
        if (sheet is null)
            throw new InvalidOperationException("Print Preview tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.ScaleToFit = new WorksheetScaleToFit(100, 1, 0);

        for (uint row = 1; row <= 140; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(row == 1 ? "Print Preview Tour" : $"Line {row - 1:000}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(row == 1 ? "State" : $"Toolbar navigation sample {row - 1:000}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row));
        }

        var activeCell = new CellAddress(sheet.Id, 1, 1);
        SetActiveCell(activeCell);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(activeCell, activeCell);
            SheetGrid.SelectedRanges = null;
        }

        return sheet;
    }

    private static void DeletePrintPreviewTourEvidence(string outputDir)
    {
        foreach (var fileName in PrintPreviewTourExpectedFileNames(includeLastPage: true).Append(PrintPreviewTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidatePrintPreviewTourEvidence(string outputDir, int totalPages)
    {
        var missing = PrintPreviewTourExpectedFileNames(totalPages > 1)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Print Preview tour did not capture expected evidence: {string.Join(", ", missing)}.");
    }

    private static IReadOnlyList<string> PrintPreviewTourExpectedFileNames(bool includeLastPage)
    {
        var files = new List<string>
        {
            "freex_print_backstage_file_print_entry.png",
            "freex_print_preview_ctrlp_entry_opened.png",
            "freex_print_preview_toolbar_first_page.png",
            "freex_print_preview_zoom_settings_summary.png",
            "freex_print_preview_closed_focus_return.png"
        };
        if (includeLastPage)
            files.Insert(3, "freex_print_preview_toolbar_last_page.png");

        return files;
    }

    private async Task CaptureWindowElementForScreenshotTourAsync(Window window, string outputDir, string fileName)
    {
        await EnsureWindowForegroundForScreenshotTourAsync(window, $"capturing {fileName}.png");
        await CaptureElementAsync(window, outputDir, fileName);
        AssertWindowForegroundForScreenshotTour(window, $"saved {fileName}.png");
    }

    private static bool ClosePrintPreviewTourDialogWithEscape(PrintPreviewDialog dialog)
    {
        var closeButton = FindDescendantByAutomationId<Button>(dialog, "PrintPreviewCloseButton");
        if (closeButton?.IsCancel != true)
            return false;

        closeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return !dialog.IsVisible;
    }

    private async Task CaptureBackstageRecentExportShareTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteBackstageRecentExportShareTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = await EnsureBackstageRecentExportShareTourContextAsync(outputDir);
        var captures = new List<BackstageRecentExportShareTourManifestCapture>();

        ShowStartScreen();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(300);

        _backstageFrame?.FocusEntry("BackstageOpenButton");
        captures.Add(await CaptureBackstageRecentExportShareStateAsync(
            outputDir,
            "open-recent-list",
            "Backstage Home/Open recent list",
            "File > Open / Recent",
            "freex_backstage_open_recent_list",
            "Backstage Home shows seeded recent workbooks while the Open navigation command is focused; the native Open dialog is not launched.",
            "main-window"));

        SwitchToPinnedTab();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        captures.Add(await CaptureBackstageRecentExportShareStateAsync(
            outputDir,
            "open-pinned-list",
            "Backstage Home/Pinned list",
            "File > Open / Pinned",
            "freex_backstage_open_pinned_list",
            "Pinned tab shows seeded pinned workbooks with unpin/remove command surfaces.",
            "main-window"));

        ShowInfoView();
        _backstageFrame?.FocusEntry("BackstageInfoButton");
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        captures.Add(await CaptureBackstageRecentExportShareStateAsync(
            outputDir,
            "info-unsaved-status",
            "Backstage Info",
            "File > Info",
            "freex_backstage_info_unsaved_status",
            "Info view shows unsaved workbook file path, share readiness, and export readiness without launching external UI.",
            "main-window"));

        var previousFeatureReport = _currentXlsxFeatureReport;
        _currentXlsxFeatureReport = new XlsxFeatureReport(
        [
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.SmartArtDiagrams, "xl/diagrams/data1.xml")
        ]);
        var unsupportedMessage = WpfResourceKeyTextResolver.Resolve(
            DeferredCommandMessagePlanner.UnsupportedXlsxFeatureSaveWarning(_currentXlsxFeatureReport));
        var unsupportedCaptureTask = CaptureBackstageOwnedNativeDialogWhenShownAsync(
            unsupportedMessage.Title,
            outputDir,
            "freex_backstage_info_unsupported_feature_save_warning",
            "unsupported-feature:save-warning",
            "unsupported-feature-save-warning",
            "Owned unsupported XLSX feature warning",
            "File > Info / Save warning",
            "Saving an XLSX with unsupported package features opens the real FreeX-owned warning dialog before save continues.");
        _ = ConfirmUnsupportedXlsxFeatureSave();
        captures.Add(await unsupportedCaptureTask);
        _currentXlsxFeatureReport = previousFeatureReport;

        ShowStartScreen();
        _backstageFrame?.FocusEntry("BackstageExportButton");
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        captures.Add(await CaptureBackstageRecentExportShareStateAsync(
            outputDir,
            "export-entry-focused",
            "Backstage Export entry",
            "File > Export",
            "freex_backstage_export_entry_focused",
            "Export PDF/XPS navigation command is focused without opening the native Save As dialog.",
            "main-window"));

        captures.Add(await CaptureBackstageExportOptionsDialogAsync(
            outputDir,
            ExportFormat.Pdf,
            "export-options-pdf",
            "freex_backstage_export_pdf_options",
            "PDF/XPS Options dialog for PDF shows publish scope, page range, PDF-only options, quality, and open-after-publish controls."));

        captures.Add(await CaptureBackstageExportOptionsDialogAsync(
            outputDir,
            ExportFormat.Xps,
            "export-options-xps",
            "freex_backstage_export_xps_options",
            "PDF/XPS Options dialog for XPS shows PDF-only choices disabled with explanatory help text."));

        _currentFilePath = null;
        ShowStartScreen();
        ShowInfoView();
        _backstageFrame?.FocusEntry("BackstageShareButton");
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        captures.Add(await CaptureBackstageRecentExportShareStateAsync(
            outputDir,
            "share-unsaved-guard-status",
            "Backstage Share unsaved guard",
            "File > Share",
            "freex_backstage_share_unsaved_guard_status",
            "Info/share status records the unsaved-workbook guard that requires Save As before Windows Share can open.",
            "main-window"));

        var savedWorkbookPath = Path.Combine(outputDir, BackstageRecentExportShareTourSavedWorkbookFileName);
        await SaveBackstageRecentExportShareTourWorkbookAsync(savedWorkbookPath);
        ShowStartScreen();
        ShowInfoView();
        _backstageFrame?.FocusEntry("BackstageShareButton");
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        captures.Add(await CaptureBackstageRecentExportShareStateAsync(
            outputDir,
            "share-saved-ready-status",
            "Backstage Share saved-ready state",
            "File > Share",
            "freex_backstage_share_saved_ready_status",
            "Info/share status records the saved local workbook state before Windows Share; the external OS share UI is intentionally not launched.",
            "main-window"));

        // The Back arrow now lives on the shared BackstageFrame; HideStartScreen() drives the same
        // close-and-return-focus path the arrow/Esc trigger.
        HideStartScreen();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        captures.Add(await CaptureBackstageRecentExportShareStateAsync(
            outputDir,
            "back-to-workbook-focus-return",
            "Workbook focus return",
            "File > Back",
            "freex_backstage_back_to_workbook_focus_return",
            "Back exits Backstage and returns focus to the worksheet grid.",
            "main-window"));

        ValidateBackstageRecentExportShareTourEvidence(outputDir);
        await WriteBackstageRecentExportShareTourManifestAsync(outputDir, context, captures, savedWorkbookPath);
    }

    private async Task<BackstageRecentExportShareTourContext> EnsureBackstageRecentExportShareTourContextAsync(string outputDir)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Backstage recent/export/share tour requires an active worksheet.");
        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        _currentXlsxFeatureReport = null;

        var headers = new[] { "Backstage state", "Evidence", "Value" };
        var rows = new[]
        {
            new object[] { "Recent", "Open list", 3d },
            new object[] { "Pinned", "Pinned list", 2d },
            new object[] { "Export", "PDF/XPS options", 1d },
            new object[] { "Share", "Unsaved guard", 1d }
        };
        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));
        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < rows[row].Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                if (rows[row][col] is double number)
                    sheet.SetCell(address, new NumberValue(number));
                else
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
            }
        }

        var activeCell = new CellAddress(sheet.Id, 1, 1);
        SetActiveCell(activeCell);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(activeCell, activeCell);
            SheetGrid.SelectedRanges = null;
        }

        var recentDir = Path.Combine(outputDir, "recent-source-files");
        Directory.CreateDirectory(recentDir);
        var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        var recentPaths = new[]
        {
            Path.Combine(recentDir, "Freight Forecast.xlsx"),
            Path.Combine(recentDir, "Quarterly Budget.xlsx"),
            Path.Combine(recentDir, "Operations Scorecard.xlsx")
        };
        var pinnedPaths = new[]
        {
            Path.Combine(recentDir, "Pinned Investor Model.xlsx"),
            Path.Combine(recentDir, "Pinned Launch Plan.xlsx")
        };

        foreach (var path in recentPaths.Concat(pinnedPaths))
            File.WriteAllText(path, "FreeX screenshot tour recent-file placeholder");

        _recentFiles.Entries.Clear();
        _recentFiles.Entries.AddRange(recentPaths.Select((path, index) => new RecentFileEntry
        {
            Path = path,
            LastOpened = now.AddMinutes(-index - 1),
            IsPinned = false
        }));
        _recentFiles.Entries.AddRange(pinnedPaths.Select((path, index) => new RecentFileEntry
        {
            Path = path,
            LastOpened = now.AddHours(-index - 1),
            IsPinned = true
        }));

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        MarkWorkbookDirty();

        var unsavedSharePlan = WorkbookShareReadinessPlanner.CreatePlan(null, WorkbookShareSurface.WindowsShare);
        var exportReadiness = WorkbookExportReadinessPlanner.Create(_workbook, hasSelection: SheetGrid?.SelectedRange is not null);
        return new BackstageRecentExportShareTourContext(
            SheetName: sheet.Name,
            ActiveRange: SheetGrid?.SelectedRange?.ToString() ?? activeCell.ToA1(),
            RecentFileNames: recentPaths.Select(Path.GetFileName).OfType<string>().ToArray(),
            PinnedFileNames: pinnedPaths.Select(Path.GetFileName).OfType<string>().ToArray(),
            UnsavedShareStatus: WorkbookShareReadinessPlanner.FormatStatus(unsavedSharePlan),
            ExportStatus: exportReadiness.StatusText);
    }

    private async Task SaveBackstageRecentExportShareTourWorkbookAsync(string savedWorkbookPath)
    {
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);

        var adapter = FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ".xlsx", out _)
            ?? throw new InvalidOperationException("Backstage recent/export/share tour could not find an XLSX save adapter.");
        var saved = await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter));
        if (!saved)
            throw new InvalidOperationException("Backstage recent/export/share tour could not save the share-ready workbook.");
    }

    private async Task<BackstageRecentExportShareTourManifestCapture> CaptureBackstageExportOptionsDialogAsync(
        string outputDir,
        ExportFormat format,
        string state,
        string fileName,
        string evidenceSummary)
    {
        var dialog = new ExportOptionsDialog(
            SheetGrid?.SelectedRange is not null,
            _options.PdfExportLanguage,
            format)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowActivated = true
        };

        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
            var options = ExportPlanner.CreateEffectiveOptionsForFormat(ExportOptions.ExcelLikeDefault, format);
            var request = ExportPlanner.PlanExport(
                Path.Combine(outputDir, format == ExportFormat.Xps ? "tour-export.xps" : "tour-export.pdf"),
                format,
                options);
            return CreateBackstageRecentExportShareCapture(
                state,
                "Export Options dialog",
                "File > Export > PDF/XPS Options",
                fileName,
                "RenderTargetBitmap-export-options-dialog",
                evidenceSummary,
                dialog.ActualWidth,
                dialog.ActualHeight,
                WpfExportDescriptionPlanner.DescribeRequest(request));
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
        }
    }

    private async Task<BackstageRecentExportShareTourManifestCapture> CaptureBackstageRecentExportShareStateAsync(
        string outputDir,
        string state,
        string surface,
        string entryPath,
        string fileName,
        string evidenceSummary,
        string captureMethod)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateBackstageRecentExportShareCapture(
            state,
            surface,
            entryPath,
            fileName,
            $"RenderTargetBitmap-{captureMethod}",
            evidenceSummary,
            ActualWidth,
            Math.Min(ActualHeight, 760),
            null);
    }

    private BackstageRecentExportShareTourManifestCapture CreateBackstageRecentExportShareCapture(
        string state,
        string surface,
        string entryPath,
        string fileName,
        string captureMethod,
        string evidenceSummary,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string? exportRequestSummary)
    {
        var sharePlan = WorkbookShareReadinessPlanner.CreatePlan(_currentFilePath, WorkbookShareSurface.WindowsShare);
        var focusedAutomationId = Keyboard.FocusedElement is DependencyObject focusedElement
            ? AutomationProperties.GetAutomationId(focusedElement)
            : null;
        return new BackstageRecentExportShareTourManifestCapture(
            CaptureKey: $"backstage-recent-export-share:{state}",
            PairKey: $"interactive:backstage-recent-export-share:{state}",
            ScenarioId: "backstage:recent-export-share",
            State: state,
            Surface: surface,
            EntryPath: entryPath,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            FocusedElementAutomationId: focusedAutomationId,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            CurrentFilePath: _currentFilePath,
            SharePlanKind: sharePlan.Kind.ToString(),
            ShareStatus: WorkbookShareReadinessPlanner.FormatStatus(sharePlan),
            ExportStatus: WorkbookExportReadinessPlanner.Create(_workbook, hasSelection: SheetGrid?.SelectedRange is not null).StatusText,
            ExportRequestSummary: exportRequestSummary,
            EvidenceSummary: evidenceSummary);
    }

    private async Task<BackstageRecentExportShareTourManifestCapture> CaptureBackstageOwnedNativeDialogWhenShownAsync(
        string caption,
        string outputDir,
        string fileName,
        string captureKey,
        string state,
        string surface,
        string entryPath,
        string evidenceSummary)
    {
        var owner = new WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero)
            throw new InvalidOperationException("Backstage recent/export/share tour could not resolve the FreeX owner window handle.");

        var size = await Task.Run(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            IntPtr dialogHandle;
            do
            {
                dialogHandle = FindOwnedNativeWindow(owner, caption);
                if (dialogHandle != IntPtr.Zero)
                    break;

                Task.Delay(100).GetAwaiter().GetResult();
            }
            while (DateTime.UtcNow < deadline);

            if (dialogHandle == IntPtr.Zero)
                throw new InvalidOperationException($"Backstage recent/export/share tour did not find the owned native dialog '{caption}'.");

            var capturedSize = CaptureNativeWindow(dialogHandle, outputDir, fileName);
            PostMessage(dialogHandle, 0x0111, new IntPtr(7), IntPtr.Zero);
            return capturedSize;
        });

        var sharePlan = WorkbookShareReadinessPlanner.CreatePlan(_currentFilePath, WorkbookShareSurface.WindowsShare);
        return new BackstageRecentExportShareTourManifestCapture(
            CaptureKey: captureKey,
            PairKey: $"interactive:backstage-recent-export-share:{state}",
            ScenarioId: "backstage:recent-export-share",
            State: state,
            Surface: surface,
            EntryPath: entryPath,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "PrintWindow-owned-native-dialog",
            CaptureLogicalWidth: size.Width,
            CaptureLogicalHeight: size.Height,
            FocusedElementAutomationId: null,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            CurrentFilePath: _currentFilePath,
            SharePlanKind: sharePlan.Kind.ToString(),
            ShareStatus: WorkbookShareReadinessPlanner.FormatStatus(sharePlan),
            ExportStatus: WorkbookExportReadinessPlanner.Create(_workbook, hasSelection: SheetGrid?.SelectedRange is not null).StatusText,
            ExportRequestSummary: null,
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteBackstageRecentExportShareTourEvidence(string outputDir)
    {
        foreach (var fileName in BackstageRecentExportShareTourExpectedFileNames().Append(BackstageRecentExportShareTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateBackstageRecentExportShareTourEvidence(string outputDir)
    {
        var missing = BackstageRecentExportShareTourExpectedFileNames()
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Backstage recent/export/share tour did not capture expected evidence: {string.Join(", ", missing)}.");
    }

    private static IReadOnlyList<string> BackstageRecentExportShareTourExpectedFileNames() =>
    [
        "freex_backstage_open_recent_list.png",
        "freex_backstage_open_pinned_list.png",
        "freex_backstage_info_unsaved_status.png",
        "freex_backstage_info_unsupported_feature_save_warning.png",
        "freex_backstage_export_entry_focused.png",
        "freex_backstage_export_pdf_options.png",
        "freex_backstage_export_xps_options.png",
        "freex_backstage_share_unsaved_guard_status.png",
        "freex_backstage_share_saved_ready_status.png",
        "freex_backstage_back_to_workbook_focus_return.png"
    ];

    private async Task CaptureOptionsAccountTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteOptionsAccountTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1120;
        Height = 768;
        await Task.Delay(700);

        ShowStartScreen();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(350);

        _backstageFrame?.FocusEntry("BackstageAccountButton");
        await CaptureCurrentWindowAsync(outputDir, "freex_account_backstage_entry_focused", 760);

        var accountPlan = LocalAccountPlanner.Create(
            _options,
            _currentFilePath,
            _workbook.Name,
            workbook: _workbook,
            hasSelection: SheetGrid.SelectedRange is not null);
        var accountMessageCapture = CaptureOwnedNativeDialogWhenShownAsync(
            UiText.Get("DeferredCommand_LocalAccount_Title"),
            outputDir,
            "freex_account_local_account_message");
        SsAccountBtn_Click(this, new RoutedEventArgs());
        var accountMessage = await accountMessageCapture;

        Activate();
        _backstageFrame?.FocusEntry("BackstageAccountButton");
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, "freex_account_backstage_focus_return", 760);

        var optionCaptures = new List<OptionsAccountTourManifestCapture>();
        var dialog = new OptionsDialog(_options, _workbook.DisabledFormulaErrorCodes)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowActivated = true
        };
        bool categoryListFocused;
        bool closedViaCancelEquivalent;
        bool focusReturned;
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(450);

            var categories = FindDescendantByAutomationId<ListBox>(dialog, "OptionsCategoryList")
                ?? throw new InvalidOperationException("Options Account tour could not find the Options category list.");

            categories.Focus();
            Keyboard.Focus(categories);
            categoryListFocused = Keyboard.FocusedElement == categories;
            optionCaptures.Add(await CaptureOptionsDialogCategoryAsync(
                dialog,
                categories,
                outputDir,
                0,
                "options:default-category-list",
                "default-general",
                "freex_options_default_general_category_list",
                "Default Options dialog opens on General with the category list focused and OK/Cancel visible."));

            optionCaptures.Add(await CaptureOptionsDialogCategoryAsync(
                dialog,
                categories,
                outputDir,
                1,
                "options:category-navigation",
                "formulas",
                "freex_options_formulas_category_navigation",
                "Category navigation selects Formulas and shows calculation/error-checking options."));

            optionCaptures.Add(await CaptureOptionsDialogCategoryAsync(
                dialog,
                categories,
                outputDir,
                8,
                "options:category-navigation",
                "quick-access-toolbar",
                "freex_options_quick_access_toolbar_category_navigation",
                "Category navigation selects Quick Access Toolbar and shows command-list customization controls."));

            optionCaptures.Add(await CaptureOptionsDialogCategoryAsync(
                dialog,
                categories,
                outputDir,
                11,
                "options:category-navigation",
                "view",
                "freex_options_view_category_navigation",
                "Category navigation selects View and shows formula-bar view toggles."));

            closedViaCancelEquivalent = CloseOptionsTourDialogWithCancel(dialog);
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
        }

        Activate();
        ShowStartScreen();
        _backstageFrame?.FocusEntry("BackstageOptionsButton");
        focusReturned = IsActive && (_backstageFrame?.IsEntryFocused("BackstageOptionsButton") ?? false);
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, "freex_options_cancel_focus_return", 760);

        ValidateOptionsAccountTourEvidence(outputDir);
        await WriteOptionsAccountTourManifestAsync(
            outputDir,
            accountPlan,
            accountMessage,
            optionCaptures,
            categoryListFocused,
            closedViaCancelEquivalent,
            focusReturned);
    }

    private async Task<OptionsAccountTourManifestCapture> CaptureOptionsDialogCategoryAsync(
        OptionsDialog dialog,
        ListBox categories,
        string outputDir,
        int selectedIndex,
        string captureKey,
        string state,
        string fileName,
        string evidenceSummary)
    {
        if (selectedIndex < 0 || selectedIndex >= categories.Items.Count)
            throw new InvalidOperationException($"Options Account tour category index {selectedIndex} is outside the category list.");

        categories.SelectedIndex = selectedIndex;
        categories.Focus();
        Keyboard.Focus(categories);
        dialog.UpdateLayout();
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(250);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);

        var categoryName = categories.Items[selectedIndex] is ListBoxItem item
            ? item.Content?.ToString() ?? state
            : state;

        return new OptionsAccountTourManifestCapture(
            CaptureKey: captureKey,
            PairKey: $"interactive:options-account:{state}",
            ScenarioId: "options-account:options-dialog",
            State: state,
            Surface: "Options dialog",
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-options-dialog-window",
            EvidenceSummary: evidenceSummary,
            CategoryName: categoryName,
            CategoryIndex: selectedIndex,
            FocusedElementAutomationId: Keyboard.FocusedElement is DependencyObject focusedElement
                ? AutomationProperties.GetAutomationId(focusedElement)
                : null,
            CaptureLogicalWidth: dialog.ActualWidth,
            CaptureLogicalHeight: dialog.ActualHeight);
    }

    private static bool CloseOptionsTourDialogWithCancel(OptionsDialog dialog)
    {
        var cancelButton = FindDescendantByAutomationId<Button>(dialog, "OptionsCancelButton");
        if (cancelButton?.IsCancel != true)
            return false;

        dialog.Close();
        return !dialog.IsVisible;
    }

    private async Task<OptionsAccountTourManifestCapture> CaptureOwnedNativeDialogWhenShownAsync(
        string caption,
        string outputDir,
        string fileName)
    {
        var owner = new WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero)
            throw new InvalidOperationException("Options Account tour could not resolve the FreeX owner window handle.");

        return await Task.Run(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            IntPtr dialogHandle;
            do
            {
                dialogHandle = FindOwnedNativeWindow(owner, caption);
                if (dialogHandle != IntPtr.Zero)
                    break;

                Task.Delay(100).GetAwaiter().GetResult();
            }
            while (DateTime.UtcNow < deadline);

            if (dialogHandle == IntPtr.Zero)
                throw new InvalidOperationException($"Options Account tour did not find the owned native dialog '{caption}'.");

            var size = CaptureNativeWindow(dialogHandle, outputDir, fileName);
            PostMessage(dialogHandle, 0x0010, IntPtr.Zero, IntPtr.Zero);

            return new OptionsAccountTourManifestCapture(
                CaptureKey: "account:local-account-message:opened",
                PairKey: "interactive:options-account:local-account-message",
                ScenarioId: "options-account:account-message",
                State: "local-account-message",
                Surface: "Account owned native message",
                FileName: fileName,
                OutputFileName: $"{fileName}.png",
                CaptureMethod: "PrintWindow-owned-native-dialog",
                EvidenceSummary: "Account command opens the FreeX-owned local-account information message with local OS account and app build details.",
                CategoryName: null,
                CategoryIndex: null,
                FocusedElementAutomationId: null,
                CaptureLogicalWidth: size.Width,
                CaptureLogicalHeight: size.Height);
        });
    }

    private static IntPtr FindOwnedNativeWindow(IntPtr owner, string caption)
    {
        var result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || GetWindow(hWnd, 4) != owner)
                return true;

            var title = GetNativeWindowTitle(hWnd);
            if (!string.Equals(title, caption, StringComparison.CurrentCulture))
                return true;

            result = hWnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static string GetNativeWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static OptionsAccountTourNativeCaptureSize CaptureNativeWindow(IntPtr hWnd, string outputDir, string fileName)
    {
        if (!GetWindowRect(hWnd, out var rect))
            throw new InvalidOperationException($"Options Account tour could not read native window bounds for {fileName}.png.");

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var windowDc = GetWindowDC(hWnd);
        if (windowDc == IntPtr.Zero)
            throw new InvalidOperationException($"Options Account tour could not acquire native window DC for {fileName}.png.");

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(windowDc);
            bitmap = CreateCompatibleBitmap(windowDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new InvalidOperationException($"Options Account tour could not allocate native capture bitmap for {fileName}.png.");

            oldBitmap = SelectObject(memoryDc, bitmap);
            if (!PrintWindow(hWnd, memoryDc, 0))
                throw new InvalidOperationException($"Options Account tour PrintWindow failed for {fileName}.png.");

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            var path = Path.Combine(outputDir, $"{fileName}.png");
            using var stream = File.Create(path);
            encoder.Save(stream);
            return new OptionsAccountTourNativeCaptureSize(width, height);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
                SelectObject(memoryDc, oldBitmap);
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(hWnd, windowDc);
        }
    }

    private static void DeleteOptionsAccountTourEvidence(string outputDir)
    {
        foreach (var fileName in OptionsAccountTourExpectedFileNames().Append(OptionsAccountTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateOptionsAccountTourEvidence(string outputDir)
    {
        var missing = OptionsAccountTourExpectedFileNames()
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Options Account tour did not capture expected evidence: {string.Join(", ", missing)}.");
    }

    private static IReadOnlyList<string> OptionsAccountTourExpectedFileNames() =>
    [
        "freex_account_backstage_entry_focused.png",
        "freex_account_local_account_message.png",
        "freex_account_backstage_focus_return.png",
        "freex_options_default_general_category_list.png",
        "freex_options_formulas_category_navigation.png",
        "freex_options_quick_access_toolbar_category_navigation.png",
        "freex_options_view_category_navigation.png",
        "freex_options_cancel_focus_return.png"
    ];

    private async Task CaptureHelpAboutLegalTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHelpAboutLegalTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1120;
        Height = 768;
        await Task.Delay(700);

        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Help"));
        var helpOnlineButton = FindRenderedRibbonControl("Help Online")
            ?? throw new InvalidOperationException("Help/About/Legal tour could not locate the rendered Help Online control.");
        helpOnlineButton.Focus();
        Keyboard.Focus(helpOnlineButton);
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);

        var captures = new List<HelpAboutLegalTourManifestCapture>
        {
            new(
                CaptureKey: "help:ribbon-command-context",
                PairKey: "interactive:help:ribbon-command-context",
                ScenarioId: "help-about-legal:ribbon",
                State: "ribbon-command-context",
                Surface: "Help ribbon tab",
                FileName: "freex_help_ribbon_command_context",
                OutputFileName: "freex_help_ribbon_command_context.png",
                CaptureMethod: "RenderTargetBitmap-main-window-top-band",
                EntryPath: "Help tab",
                EvidenceSummary: "Help tab command context shows Help Online, Feedback, Copy Diagnostics, Check for Updates, About FreeX, and Legal Notices.",
                Url: null,
                FocusedElementAutomationId: AutomationProperties.GetAutomationId(helpOnlineButton),
                CaptureLogicalWidth: ActualWidth,
                CaptureLogicalHeight: ScreenshotTourCaptureHeight)
        };
        await CaptureCurrentWindowAsync(outputDir, "freex_help_ribbon_command_context", ScreenshotTourCaptureHeight);

        captures.Add(await CaptureGuardedExternalHelpMessageForTourAsync(
            outputDir,
            "help-online-guarded-message",
            "freex_help_online_guarded_message",
            "Help > Help Online guarded external-link warning",
            AppInfo.HelpUrl,
            UiText.Get("MainWindowMessage_HelpOnlineTitle")));

        captures.Add(await CaptureGuardedExternalHelpMessageForTourAsync(
            outputDir,
            "feedback-guarded-message",
            "freex_feedback_guarded_message",
            "Help > Feedback guarded external-link warning with diagnostics-aware issue URL",
            AppIssueReporter.CreateIssueUrl(CreateDeterministicIssueReportContextForHelpTour()),
            UiText.Get("MainWindowMessage_FeedbackTitle")));

        captures.Add(await CaptureGuardedExternalHelpMessageForTourAsync(
            outputDir,
            "updates-guarded-message",
            "freex_updates_guarded_message",
            "Help > Check for Updates guarded external-link warning",
            AppUpdateSource.CreateDefault().ReleasePageUrl,
            UiText.Get("MainWindowMessage_CheckForUpdatesTitle")));

        captures.Add(await CaptureAboutDialogForTourAsync(outputDir));
        captures.Add(await CaptureLegalNoticesDialogForTourAsync(outputDir));
        captures.Add(await CaptureHelpAboutLegalFocusReturnForTourAsync(outputDir));

        ValidateHelpAboutLegalTourEvidence(outputDir);
        await WriteHelpAboutLegalTourManifestAsync(outputDir, captures);
    }

    private async Task<HelpAboutLegalTourManifestCapture> CaptureHelpAboutLegalFocusReturnForTourAsync(string outputDir)
    {
        Activate();
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Help"));
        var helpOnlineButton = FindRenderedRibbonControl("Help Online")
            ?? throw new InvalidOperationException("Help/About/Legal tour could not locate the rendered Help Online control.");
        helpOnlineButton.Focus();
        Keyboard.Focus(helpOnlineButton);
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        await CaptureCurrentWindowAsync(outputDir, "freex_help_focus_return_status", ActualHeight);

        return new HelpAboutLegalTourManifestCapture(
            CaptureKey: "help:focus-return-status",
            PairKey: "interactive:help:focus-return-status",
            ScenarioId: "help-about-legal:focus-return",
            State: "focus-return-status",
            Surface: "Help tab focus return and status bar",
            FileName: "freex_help_focus_return_status",
            OutputFileName: "freex_help_focus_return_status.png",
            CaptureMethod: "RenderTargetBitmap-main-window-full",
            EntryPath: "Help tab after owned dialog close",
            EvidenceSummary: "Focus returns to the FreeX Help ribbon context after owned About/Legal dialogs close, with the Ready status bar still visible.",
            Url: null,
            FocusedElementAutomationId: AutomationProperties.GetAutomationId(helpOnlineButton),
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: ActualHeight);
    }

    private async Task<HelpAboutLegalTourManifestCapture> CaptureGuardedExternalHelpMessageForTourAsync(
        string outputDir,
        string state,
        string fileName,
        string evidenceSummary,
        string url,
        string title)
    {
        var messageCapture = CaptureOwnedNativeDialogWhenShownForHelpTourAsync(
            title,
            outputDir,
            fileName,
            state,
            evidenceSummary,
            url);
        ShowOwnedMessage(
            CreateExternalLinkOpenFailedMessageForHelpTour(url),
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return await messageCapture;
    }

    private async Task<HelpAboutLegalTourManifestCapture> CaptureAboutDialogForTourAsync(string outputDir)
    {
        var dialog = new AboutDialog
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowActivated = true
        };
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_about_dialog");

            return new HelpAboutLegalTourManifestCapture(
                CaptureKey: "help:about-dialog:opened",
                PairKey: "interactive:help:about-dialog:opened",
                ScenarioId: "help-about-legal:about-dialog",
                State: "about-dialog-opened",
                Surface: "About FreeX dialog",
                FileName: "freex_about_dialog",
                OutputFileName: "freex_about_dialog.png",
                CaptureMethod: "RenderTargetBitmap-about-dialog-window",
                EntryPath: "Help > About FreeX",
                EvidenceSummary: "About FreeX dialog is the production owned WPF dialog with read-only version/license text and OK close path.",
                Url: null,
                FocusedElementAutomationId: Keyboard.FocusedElement is DependencyObject focusedElement
                    ? AutomationProperties.GetAutomationId(focusedElement)
                    : null,
                CaptureLogicalWidth: dialog.ActualWidth,
                CaptureLogicalHeight: dialog.ActualHeight);
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<HelpAboutLegalTourManifestCapture> CaptureLegalNoticesDialogForTourAsync(string outputDir)
    {
        var dialog = new LegalNoticesDialog
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowActivated = true
        };
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_legal_notices_dialog");

            return new HelpAboutLegalTourManifestCapture(
                CaptureKey: "help:legal-notices-dialog:opened",
                PairKey: "interactive:help:legal-notices-dialog:opened",
                ScenarioId: "help-about-legal:legal-notices-dialog",
                State: "legal-notices-dialog-opened",
                Surface: "Legal Notices dialog",
                FileName: "freex_legal_notices_dialog",
                OutputFileName: "freex_legal_notices_dialog.png",
                CaptureMethod: "RenderTargetBitmap-legal-notices-dialog-window",
                EntryPath: "Help > Legal Notices",
                EvidenceSummary: "Legal Notices dialog is the production owned WPF dialog with packaged legal/privacy/third-party tabs and copyable read-only text.",
                Url: null,
                FocusedElementAutomationId: Keyboard.FocusedElement is DependencyObject focusedElement
                    ? AutomationProperties.GetAutomationId(focusedElement)
                    : null,
                CaptureLogicalWidth: dialog.ActualWidth,
                CaptureLogicalHeight: dialog.ActualHeight);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static string CreateExternalLinkOpenFailedMessageForHelpTour(string url)
    {
        var reason = UiText.Get("MainWindowMessage_ExternalLinkCouldNotBeOpened");
        return UiText.Format("MainWindowMessage_ExternalLinkOpenFailed", url, reason);
    }

    private static AppIssueReportContext CreateDeterministicIssueReportContextForHelpTour() =>
        new(
            AppInfo.FeedbackUrl,
            new AppDiagnosticsMetadata(
                AppInfo.VersionText,
                "visual-evidence-session",
                ".NET visual evidence runtime",
                "Windows visual evidence runner",
                "X64"),
            "visual-evidence",
            DiagnosticsEnabled: false);

    private async Task<HelpAboutLegalTourManifestCapture> CaptureOwnedNativeDialogWhenShownForHelpTourAsync(
        string caption,
        string outputDir,
        string fileName,
        string state,
        string evidenceSummary,
        string url)
    {
        var owner = new WindowInteropHelper(this).Handle;
        if (owner == IntPtr.Zero)
            throw new InvalidOperationException("Help/About/Legal tour could not resolve the FreeX owner window handle.");

        return await Task.Run(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            IntPtr dialogHandle;
            do
            {
                dialogHandle = FindOwnedNativeWindow(owner, caption);
                if (dialogHandle != IntPtr.Zero)
                    break;

                Task.Delay(100).GetAwaiter().GetResult();
            }
            while (DateTime.UtcNow < deadline);

            if (dialogHandle == IntPtr.Zero)
                throw new InvalidOperationException($"Help/About/Legal tour did not find the owned native dialog '{caption}'.");

            var size = CaptureNativeWindow(dialogHandle, outputDir, fileName);
            PostMessage(dialogHandle, 0x0010, IntPtr.Zero, IntPtr.Zero);

            return new HelpAboutLegalTourManifestCapture(
                CaptureKey: $"help:{state}",
                PairKey: $"interactive:help:{state}",
                ScenarioId: "help-about-legal:external-link-guard",
                State: state,
                Surface: "Owned guarded external-link warning",
                FileName: fileName,
                OutputFileName: $"{fileName}.png",
                CaptureMethod: "PrintWindow-owned-native-dialog",
                EntryPath: caption,
                EvidenceSummary: evidenceSummary,
                Url: url,
                FocusedElementAutomationId: null,
                CaptureLogicalWidth: size.Width,
                CaptureLogicalHeight: size.Height);
        });
    }

    private static void DeleteHelpAboutLegalTourEvidence(string outputDir)
    {
        foreach (var fileName in HelpAboutLegalTourExpectedFileNames().Append(HelpAboutLegalTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateHelpAboutLegalTourEvidence(string outputDir)
    {
        var missing = HelpAboutLegalTourExpectedFileNames()
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Help/About/Legal tour did not capture expected evidence: {string.Join(", ", missing)}.");
    }

    private static IReadOnlyList<string> HelpAboutLegalTourExpectedFileNames() =>
    [
        "freex_help_ribbon_command_context.png",
        "freex_help_online_guarded_message.png",
        "freex_feedback_guarded_message.png",
        "freex_updates_guarded_message.png",
        "freex_about_dialog.png",
        "freex_legal_notices_dialog.png",
        "freex_help_focus_return_status.png"
    ];

    private static T? FindDescendantByAutomationId<T>(DependencyObject root, string automationId)
        where T : FrameworkElement
    {
        if (root is T element && AutomationProperties.GetAutomationId(element) == automationId)
            return element;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var match = FindDescendantByAutomationId<T>(child, automationId);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static T? FindDescendantByContent<T>(DependencyObject root, string content)
        where T : ContentControl
    {
        if (root is T element && string.Equals(element.Content?.ToString(), content, StringComparison.Ordinal))
            return element;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var match = FindDescendantByContent<T>(child, content);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static Button? FindDescendantButtonByContent(DependencyObject root, string content)
    {
        if (root is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            return button;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var match = FindDescendantButtonByContent(child, content);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T element)
            return element;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var match = FindDescendant<T>(child);
            if (match is not null)
                return match;
        }

        return null;
    }

    private async Task CaptureQatUndoRedoTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteQatUndoRedoTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var address = EnsureQatUndoRedoTourContext();
        var captures = new List<QatUndoRedoTourManifestCapture>();

        try
        {
            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "fresh-disabled",
                "freex_qat_initial_disabled",
                address));

            ExecuteQatUndoRedoTourMutation(address);
            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "after-edit-undo-enabled",
                "freex_qat_after_edit_undo_enabled",
                address));

            captures.Add(await CaptureQatUndoRedoHistoryMenuAsync(
                outputDir,
                QuickAccessToolbarCommandIds.Undo,
                "undo-history-opened",
                "freex_qat_undo_history_menu_opened",
                address));

            if (!ExecuteUndo())
                throw new InvalidOperationException("QAT undo/redo tour could not execute the first Undo action.");

            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "after-one-undo-redo-enabled",
                "freex_qat_after_one_undo_redo_enabled",
                address));

            if (!ExecuteUndo())
                throw new InvalidOperationException("QAT undo/redo tour could not execute the second Undo action.");

            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "after-two-undos-redo-menu-ready",
                "freex_qat_after_two_undos_redo_menu_ready",
                address));

            captures.Add(await CaptureQatUndoRedoHistoryMenuAsync(
                outputDir,
                QuickAccessToolbarCommandIds.Redo,
                "redo-history-opened",
                "freex_qat_redo_history_menu_opened",
                address));

            if (!ExecuteRedo() || !ExecuteRedo())
                throw new InvalidOperationException("QAT undo/redo tour could not execute both Redo actions.");

            captures.Add(await CaptureQatUndoRedoWindowStateAsync(
                outputDir,
                "after-redo-restored",
                "freex_qat_after_redo_restored",
                address));

            ValidateQatUndoRedoTourEvidence(outputDir, captures);
            await WriteQatUndoRedoTourManifestAsync(outputDir, address, captures);
        }
        catch
        {
            DeleteQatUndoRedoTourEvidence(outputDir);
            throw;
        }
    }

    private CellAddress EnsureQatUndoRedoTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("QAT undo/redo tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ClearCell(address);
        sheet.ClearCell(new CellAddress(sheet.Id, 1, 2));
        sheet.ClearCell(new CellAddress(sheet.Id, 2, 1));
        SetActiveCell(address);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(address, address);
            SheetGrid.SelectedRanges = null;
            SheetGrid.Focus();
        }

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        return address;
    }

    private void ExecuteQatUndoRedoTourMutation(CellAddress address)
    {
        var edit = (address, Cell.FromValue(new TextValue("QAT undo redo proof")));
        if (!TryExecuteEditCells([edit], "Edit Cell", out var editOutcome))
            throw new InvalidOperationException(editOutcome.ErrorMessage ?? "QAT undo/redo tour cell edit failed.");

        var styleRange = new GridRange(address, address);
        var diff = new StyleDiff(FillColor: new CellColor(255, 242, 204), Bold: true);
        if (!TryExecuteApplyStyle(styleRange, diff, "Apply Style"))
            throw new InvalidOperationException("QAT undo/redo tour style mutation failed.");

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private async Task<QatUndoRedoTourManifestCapture> CaptureQatUndoRedoWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        CellAddress address)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateQatUndoRedoTourCapture(state, "window", fileName, address, "RenderTargetBitmap-window-full", ActualWidth, Math.Min(ActualHeight, 760), []);
    }

    private async Task<QatUndoRedoTourManifestCapture> CaptureQatUndoRedoHistoryMenuAsync(
        string outputDir,
        string commandId,
        string state,
        string fileName,
        CellAddress address)
    {
        var historyButton = FindName(GetQuickAccessHistoryButtonName(commandId)) as ButtonBase
            ?? throw new InvalidOperationException($"QAT undo/redo tour could not find history button for '{commandId}'.");
        var menu = CreateQuickAccessHistoryMenu(commandId, historyButton);
        try
        {
            menu.IsOpen = true;
            menu.UpdateLayout();
            await Task.Delay(350);
            menu.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureElementAsync(menu, outputDir, fileName);
            var menuHeaders = menu.Items
                .OfType<MenuItem>()
                .Select(item => item.Header?.ToString() ?? string.Empty)
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .ToArray();
            return CreateQatUndoRedoTourCapture(state, "history-menu", fileName, address, "RenderTargetBitmap-qat-history-context-menu", menu.ActualWidth, menu.ActualHeight, menuHeaders);
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private QatUndoRedoTourManifestCapture CreateQatUndoRedoTourCapture(
        string state,
        string surface,
        string fileName,
        CellAddress address,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        IReadOnlyList<string> menuHeaders)
    {
        var sheet = _workbook.GetSheet(address.Sheet);
        var cell = sheet?.GetCell(address);
        var style = cell is null ? _workbook.GetStyle(StyleId.Default) : _workbook.GetStyle(cell.StyleId);
        var undoButton = GetQuickAccessToolbarButton(QuickAccessToolbarCommandIds.Undo);
        var redoButton = GetQuickAccessToolbarButton(QuickAccessToolbarCommandIds.Redo);
        var undoHistoryButton = FindName(GetQuickAccessHistoryButtonName(QuickAccessToolbarCommandIds.Undo)) as ButtonBase;
        var redoHistoryButton = FindName(GetQuickAccessHistoryButtonName(QuickAccessToolbarCommandIds.Redo)) as ButtonBase;
        var undoHistory = GetQuickAccessHistoryEntries(QuickAccessToolbarCommandIds.Undo)
            .Select(entry => entry.Label)
            .ToArray();
        var redoHistory = GetQuickAccessHistoryEntries(QuickAccessToolbarCommandIds.Redo)
            .Select(entry => entry.Label)
            .ToArray();

        return new QatUndoRedoTourManifestCapture(
            CaptureKey: $"interactive:qat-undo-redo:{state}",
            PairKey: $"interactive:qat-undo-redo:{state}",
            ScenarioId: "qat:undo-redo",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            UndoButtonEnabled: undoButton?.IsEnabled == true,
            UndoHistoryButtonEnabled: undoHistoryButton?.IsEnabled == true,
            RedoButtonEnabled: redoButton?.IsEnabled == true,
            RedoHistoryButtonEnabled: redoHistoryButton?.IsEnabled == true,
            CanUndo: _session.CanUndo,
            CanRedo: _session.CanRedo,
            ActiveCell: address.ToA1(),
            ActiveCellText: FormatQatUndoRedoTourValue(cell?.Value),
            ActiveCellBold: style.Bold,
            ActiveCellFillColor: FormatQatUndoRedoTourColor(style.FillColor),
            StatusText: StatusReadyText.Text,
            UndoHistoryLabels: undoHistory,
            RedoHistoryLabels: redoHistory,
            MenuHeaders: menuHeaders);
    }

    private static string FormatQatUndoRedoTourValue(ScalarValue? value) =>
        value switch
        {
            null or BlankValue => string.Empty,
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.ToDateTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            RangeValue range => $"{range.RowCount}x{range.ColCount} range",
            _ => value.ToString() ?? string.Empty
        };

    private static string? FormatQatUndoRedoTourColor(CellColor? color) =>
        color is { } value ? $"#{value.R:X2}{value.G:X2}{value.B:X2}" : null;

    private static void DeleteQatUndoRedoTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_qat_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, QatUndoRedoTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateQatUndoRedoTourEvidence(string outputDir, IReadOnlyList<QatUndoRedoTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"QAT undo/redo tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private async Task CaptureTitlebarWindowChromeTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteTitlebarWindowChromeTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1100;
        Height = 768;
        await Task.Delay(700);

        var address = EnsureTitlebarWindowChromeTourContext();
        var captures = new List<TitlebarWindowChromeTourManifestCapture>();
        var savedWorkbookPath = Path.Combine(outputDir, TitlebarWindowChromeTourSavedWorkbookFileName);

        try
        {
            UpdateTitleBar();
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "unsaved-restored",
                "freex_titlebar_unsaved_restored",
                "Fresh workbook titlebar shows Book1, QAT Save/Undo/Redo, and custom minimize/maximize/close buttons in restored state."));

            ExecuteTitlebarWindowChromeTourDirtyMutation(address);
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "dirty-marker-restored",
                "freex_titlebar_dirty_marker_restored",
                "Dirty marker appears in the workbook title after a command-stack edit."));

            await SaveTitlebarWindowChromeTourWorkbookAsync(savedWorkbookPath);
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "saved-renamed-restored",
                "freex_titlebar_saved_renamed_restored",
                "Real save-to-XLSX path renames the title and clears the dirty marker in restored state."));

            WindowState = WindowState.Maximized;
            UpdateMaxRestoreButtonState();
            UpdateLayout();
            await Task.Delay(450);
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "saved-renamed-maximized",
                "freex_titlebar_saved_renamed_maximized",
                "Maximized window state shows the saved title and restore-down system-button state."));

            WindowState = WindowState.Normal;
            Width = 1100;
            Height = 768;
            UpdateMaxRestoreButtonState();
            UpdateLayout();
            await Task.Delay(450);
            captures.Add(await CaptureTitlebarWindowChromeStateAsync(
                outputDir,
                "saved-renamed-restored-after-maximize",
                "freex_titlebar_saved_renamed_restored_after_maximize",
                "Restored-after-maximize state shows the saved title and maximize system-button state."));

            ValidateTitlebarWindowChromeTourEvidence(outputDir, captures);
            await WriteTitlebarWindowChromeTourManifestAsync(outputDir, captures, savedWorkbookPath);
        }
        catch
        {
            DeleteTitlebarWindowChromeTourEvidence(outputDir);
            throw;
        }
    }

    private CellAddress EnsureTitlebarWindowChromeTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Titlebar/window chrome tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ClearCell(address);
        SetActiveCell(address);
        _workbook.Name = "Book1";
        _currentFilePath = null;
        MarkWorkbookSaved();
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(address, address);
            SheetGrid.SelectedRanges = null;
            SheetGrid.Focus();
        }

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        return address;
    }

    private void ExecuteTitlebarWindowChromeTourDirtyMutation(CellAddress address)
    {
        var edit = (address, Cell.FromValue(new TextValue("Titlebar dirty marker proof")));
        if (!TryExecuteEditCells([edit], "Edit Cell", out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? "Titlebar/window chrome tour cell edit failed.");

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private async Task SaveTitlebarWindowChromeTourWorkbookAsync(string savedWorkbookPath)
    {
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);

        var adapter = FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ".xlsx", out _)
            ?? throw new InvalidOperationException("Titlebar/window chrome tour could not find an XLSX save adapter.");

        var saved = await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter));
        if (!saved)
            throw new InvalidOperationException("Titlebar/window chrome tour could not save the renamed workbook.");

        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
    }

    private async Task<TitlebarWindowChromeTourManifestCapture> CaptureTitlebarWindowChromeStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidenceSummary)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateMaxRestoreButtonState();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 220);
        return CreateTitlebarWindowChromeTourCapture(state, fileName, evidenceSummary);
    }

    private TitlebarWindowChromeTourManifestCapture CreateTitlebarWindowChromeTourCapture(
        string state,
        string fileName,
        string evidenceSummary)
    {
        return new TitlebarWindowChromeTourManifestCapture(
            CaptureKey: $"window-chrome:titlebar:{state}",
            PairKey: $"interactive:titlebar-window-chrome:{state}",
            ScenarioId: "window-chrome:titlebar",
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-top-band",
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: Math.Min(ActualHeight, 220),
            EvidenceSummary: evidenceSummary,
            WindowState: WindowState.ToString(),
            WindowTitle: Title,
            WorkbookNameText: WorkbookNameText.Text,
            WorkbookName: _workbook.Name,
            WorkbookDirty: _workbookDirty,
            CurrentFileName: string.IsNullOrWhiteSpace(_currentFilePath) ? null : Path.GetFileName(_currentFilePath),
            TitleBarQatVisible: TitleBarQatPanel.Visibility == Visibility.Visible,
            TitleBarQatCommandIds: GetTitlebarWindowChromeVisibleQatCommandIds(),
            MinimizeButton: CreateTitlebarWindowChromeButtonState(MinimizeBtn),
            MaxRestoreButton: CreateTitlebarWindowChromeButtonState(MaxRestoreBtn),
            CloseButton: CreateTitlebarWindowChromeButtonState(CloseSysBtn),
            MaxRestoreIconKind: MaxRestoreIcon.Kind.ToString());
    }

    private IReadOnlyList<string> GetTitlebarWindowChromeVisibleQatCommandIds()
    {
        var result = new List<string>();
        foreach (var command in QuickAccessToolbarCatalog.Commands)
        {
            var button = GetQuickAccessToolbarButton(command.Id);
            if (button is { Visibility: Visibility.Visible })
                result.Add(command.Id);
        }

        return result;
    }

    private static TitlebarWindowChromeTourManifestButtonState CreateTitlebarWindowChromeButtonState(ButtonBase button)
    {
        return new TitlebarWindowChromeTourManifestButtonState(
            AutomationId: AutomationProperties.GetAutomationId(button),
            AutomationName: AutomationProperties.GetName(button),
            HelpText: AutomationProperties.GetHelpText(button),
            IsVisible: button.Visibility == Visibility.Visible,
            IsEnabled: button.IsEnabled,
            ActualWidth: button.ActualWidth,
            ActualHeight: button.ActualHeight);
    }

    private static void DeleteTitlebarWindowChromeTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_titlebar_*.png"))
            File.Delete(file);

        var savedWorkbookPath = Path.Combine(outputDir, TitlebarWindowChromeTourSavedWorkbookFileName);
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);

        var manifestPath = Path.Combine(outputDir, TitlebarWindowChromeTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private async Task CaptureFormulaBarNameBoxTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteFormulaBarNameBoxTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureFormulaBarNameBoxTourContext();
        var captures = new List<FormulaBarNameBoxTourManifestCapture>();
        InsertFunctionDialog? insertFunctionDialog = null;

        try
        {
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "initial-named-range-selection",
                "freex_formula_name_box_named_range_selected",
                "window-full",
                "Selected Sales named range displays in the Name Box, with the formula bar showing B2's content."));

            CellAddressBox.Focus();
            Keyboard.Focus(CellAddressBox);
            CellAddressBox.IsDropDownOpen = true;
            CellAddressBox.UpdateLayout();
            await Task.Delay(350);
            await WaitForRibbonScreenshotRenderPassAsync();
            var nameBoxPopup = FindOpenPopupChild(CellAddressBox)
                ?? throw new InvalidOperationException("Formula bar/name box tour could not locate the open Name Box dropdown.");
            await CaptureElementAsync(nameBoxPopup, outputDir, "freex_formula_name_box_dropdown_opened");
            captures.Add(CreateFormulaBarNameBoxCapture(
                "name-box-dropdown-opened",
                "freex_formula_name_box_dropdown_opened",
                "name-box-dropdown",
                "RenderTargetBitmap-name-box-combobox-popup",
                nameBoxPopup.ActualWidth,
                nameBoxPopup.ActualHeight,
                "Name Box dropdown lists workbook defined names including Sales."));

            CellAddressBox.SelectedItem = NameBoxDropdownPlanner
                .Build(_workbook, _currentSheetId)
                .First(item => item.Name == "Sales");
            CellAddressBox.IsDropDownOpen = false;
            await Task.Delay(250);
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "name-box-dropdown-navigation",
                "freex_formula_name_box_dropdown_navigation",
                "window-full",
                "Selecting SalesData from the Name Box dropdown navigates to B2:C3 and returns focus to the worksheet."));

            BeginFormulaBarFormulaEdit("=SUM(B2:C3)");
            FormulaBar.CaretIndex = FormulaBar.Text.Length;
            FormulaBarCancelButton.Focus();
            Keyboard.Focus(FormulaBarCancelButton);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-edit-cancel-focused",
                "freex_formula_bar_edit_mode_cancel_focused",
                "window-full",
                "Formula bar edit mode shows the draft formula with the Cancel control focused."));

            FormulaBarCancelButton_Click(FormulaBarCancelButton, new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(250);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-edit-canceled",
                "freex_formula_bar_cancel_restored_selection",
                "window-full",
                "Cancel restores the selected cell's formula bar text and worksheet focus."));

            BeginFormulaBarFormulaEdit("=SUM(B2:C3)");
            FormulaBar.CaretIndex = FormulaBar.Text.Length;
            FormulaBarEnterButton.Focus();
            Keyboard.Focus(FormulaBarEnterButton);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-edit-enter-focused",
                "freex_formula_bar_edit_mode_enter_focused",
                "window-full",
                "Formula bar edit mode shows the draft formula with the Enter control focused."));

            FormulaBarEnterButton_Click(FormulaBarEnterButton, new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(250);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-edit-enter-committed",
                "freex_formula_bar_enter_committed",
                "window-full",
                "Enter commits the formula-bar edit and returns focus to the worksheet."));

            FormulaBarFxButton.Focus();
            Keyboard.Focus(FormulaBarFxButton);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "fx-button-focused",
                "freex_formula_bar_fx_button_focused",
                "window-full",
                "Formula bar fx button is focused beside the Cancel/Enter controls."));

            insertFunctionDialog = new InsertFunctionDialog
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            insertFunctionDialog.Show();
            insertFunctionDialog.Activate();
            insertFunctionDialog.UpdateLayout();
            await Task.Delay(450);
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureWindowElementForScreenshotTourAsync(insertFunctionDialog, outputDir, "freex_formula_bar_fx_insert_function_dialog");
            captures.Add(CreateFormulaBarNameBoxCapture(
                "fx-insert-function-dialog-opened",
                "freex_formula_bar_fx_insert_function_dialog",
                "insert-function-dialog",
                "RenderTargetBitmap-insert-function-dialog",
                insertFunctionDialog.ActualWidth,
                insertFunctionDialog.ActualHeight,
                "Production Insert Function dialog shown from the formula-bar fx surface scenario."));
            insertFunctionDialog.Close();
            insertFunctionDialog = null;

            if (!_formulaBarExpanded)
                FormulaBarExpandBtn_Click(FormulaBarExpandBtn, new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(250);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-bar-expanded",
                "freex_formula_bar_expanded",
                "window-full",
                "Expanded formula bar shows the taller multiline editor and collapse chevron state."));

            FormulaBar.Focus();
            Keyboard.Focus(FormulaBar);
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "formula-bar-focus",
                "freex_formula_bar_focus",
                "window-full",
                "Formula bar accepts keyboard focus after the expand/collapse interaction."));

            CellAddressBox.Focus();
            Keyboard.Focus(CellAddressBox);
            EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);
            UpdateLayout();
            await Task.Delay(350);
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureFormulaBarNameBoxWindowStateAsync(
                outputDir,
                "name-box-focus-top-level-keytips",
                "freex_formula_keytips_from_name_box_focus",
                "window-top-band",
                "Top-level keytip overlay is visible while focus starts from the Name Box."));
            ExitRibbonKeyTipMode();

            ValidateFormulaBarNameBoxTourEvidence(outputDir, captures);
            await WriteFormulaBarNameBoxTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteFormulaBarNameBoxTourEvidence(outputDir);
            throw;
        }
        finally
        {
            ExitRibbonKeyTipMode();
            if (insertFunctionDialog is { IsVisible: true })
                insertFunctionDialog.Close();
        }
    }

    private FormulaBarNameBoxTourContext EnsureFormulaBarNameBoxTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Formula bar/name box tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Formula bar/name box tour")),
            (1, 2, new TextValue("Q1")),
            (1, 3, new TextValue("Q2")),
            (2, 1, new TextValue("North")),
            (2, 2, new NumberValue(10)),
            (2, 3, new NumberValue(15)),
            (3, 1, new TextValue("South")),
            (3, 2, new NumberValue(12)),
            (3, 3, new NumberValue(18))
        };

        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

        var namedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
        _workbook.DefineNamedRange("Sales", namedRange);
        // R118: see the matching note above HomeTourData -- this direct define bypasses the command
        // bus, so the Name Box's cached range index must be told about it explicitly.
        InvalidateNavigationCaches();
        const string nameBoxShape = "Tour Name Box Shape";
        const string nameBoxPicture = "Tour Name Box Picture";
        const string nameBoxTextBox = "Tour Name Box Text Box";
        const string nameBoxChart = "Tour Name Box Chart";
        sheet.DrawingShapes.RemoveAll(item => string.Equals(item.Name, nameBoxShape, StringComparison.Ordinal));
        sheet.Pictures.RemoveAll(item => string.Equals(item.Name, nameBoxPicture, StringComparison.Ordinal));
        sheet.TextBoxes.RemoveAll(item => string.Equals(item.Name, nameBoxTextBox, StringComparison.Ordinal));
        sheet.Charts.RemoveAll(item => string.Equals(item.Name, nameBoxChart, StringComparison.Ordinal));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000001"),
            Name = nameBoxShape,
            Anchor = new CellAddress(sheet.Id, 22, 8),
            Width = 96,
            Height = 48,
        });
        sheet.Pictures.Add(new PictureModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000002"),
            Name = nameBoxPicture,
            Anchor = new CellAddress(sheet.Id, 23, 8),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3, 4],
            ContentType = "image/png",
            Width = 96,
            Height = 48,
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000003"),
            Name = nameBoxTextBox,
            Anchor = new CellAddress(sheet.Id, 24, 8),
            Text = "Name Box tour text box",
            Width = 120,
            Height = 48,
        });
        sheet.Charts.Add(new ChartModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000004"),
            Name = nameBoxChart,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 25, 8),
                new CellAddress(sheet.Id, 26, 9)),
        });
        SetSelectionRange(namedRange, namedRange.Start);
        EnsureCellVisible(namedRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new FormulaBarNameBoxTourContext(
            SheetName: sheet.Name,
            NamedRangeName: "Sales",
            NamedRangeAddress: namedRange.ToString(),
            StartCell: namedRange.Start.ToA1(),
            ObjectNames: [nameBoxChart, nameBoxPicture, nameBoxShape, nameBoxTextBox]);
    }

    private async Task<FormulaBarNameBoxTourManifestCapture> CaptureFormulaBarNameBoxWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string surface,
        string evidenceSummary)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(150);

        var height = surface == "window-top-band" ? ScreenshotTourCaptureHeight : 760;
        await CaptureCurrentWindowAsync(outputDir, fileName, height);
        return CreateFormulaBarNameBoxCapture(
            state,
            fileName,
            surface,
            surface == "window-top-band" ? "RenderTargetBitmap-window-top-band" : "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, height),
            evidenceSummary);
    }
    private FormulaBarNameBoxTourManifestCapture CreateFormulaBarNameBoxCapture(
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary)
    {
        var selectedRange = SheetGrid.SelectedRange;
        var activeCell = selectedRange?.Start;
        var activeCellText = activeCell is { } cellAddress
            ? FormatQatUndoRedoTourValue(_workbook.GetSheet(cellAddress.Sheet)?.GetCell(cellAddress)?.Value)
            : string.Empty;
        var focusElement = Keyboard.FocusedElement as DependencyObject;

        return new FormulaBarNameBoxTourManifestCapture(
            CaptureKey: $"formula-bar-name-box:{state}",
            PairKey: $"interactive:formula-bar-name-box:{state}",
            ScenarioId: "formula-bar-name-box:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            NameBoxText: CellAddressBox.Text,
            NameBoxDropDownOpen: CellAddressBox.IsDropDownOpen,
            FormulaBarText: FormulaBar.Text,
            FormulaBarAcceptsReturn: FormulaBar.AcceptsReturn,
            FormulaBarExpanded: _formulaBarExpanded,
            SelectedRange: selectedRange?.ToString() ?? string.Empty,
            ActiveCellText: activeCellText,
            FocusedAutomationId: FormatFormulaBarNameBoxFocusedAutomationId(focusElement),
            KeyTipBadgeCount: KeyTipOverlay.Children.OfType<Border>().Count(),
            EvidenceSummary: evidenceSummary);
    }

    private static string FormatFormulaBarNameBoxFocusedAutomationId(DependencyObject? focusedElement)
    {
        if (focusedElement is null)
            return string.Empty;

        var automationId = AutomationProperties.GetAutomationId(focusedElement);
        if (!string.IsNullOrWhiteSpace(automationId))
            return automationId;

        return focusedElement.GetType().Name;
    }

    private static void DeleteFormulaBarNameBoxTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_formula_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, FormulaBarNameBoxTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private async Task CaptureGridSelectionEditingTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteGridSelectionEditingTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureGridSelectionEditingTourContext();
        var captures = new List<GridSelectionEditingTourManifestCapture>();

        try
        {
            SetActiveCell(context.SelectedCell);
            EnsureCellVisible(context.SelectedCell);
            captures.Add(await CaptureGridSelectionEditingWindowStateAsync(
                outputDir,
                "selected-cell",
                "freex_grid_selection_editing_selected_cell",
                "grid-window",
                "Single selected value cell shows the active-cell border, Name Box B2, formula bar value, and Ready/status agreement."));

            SetSelectionRange(context.SelectedRange, context.SelectedRange.Start);
            EnsureCellVisible(context.SelectedRange.Start);
            captures.Add(await CaptureGridSelectionEditingWindowStateAsync(
                outputDir,
                "selected-range",
                "freex_grid_selection_editing_selected_range",
                "grid-window",
                "Selected B2:D4 range shows the range highlight, active cell B2, Name Box B2:D4, and aggregate status stats."));

            SelectRow(context.RowSelectionIndex);
            EnsureCellVisible(new CellAddress(context.Sheet.Id, context.RowSelectionIndex, 1));
            captures.Add(await CaptureGridSelectionEditingWindowStateAsync(
                outputDir,
                "whole-row-selection",
                "freex_grid_selection_editing_whole_row",
                "grid-window",
                "Whole row 5 selection uses the row-header path and Name Box 5:5 while the grid highlights the row."));

            SelectColumn(context.ColumnSelectionIndex);
            EnsureCellVisible(new CellAddress(context.Sheet.Id, 1, context.ColumnSelectionIndex));
            captures.Add(await CaptureGridSelectionEditingWindowStateAsync(
                outputDir,
                "whole-column-selection",
                "freex_grid_selection_editing_whole_column",
                "grid-window",
                "Whole column C selection uses the column-header path and Name Box C:C while the grid highlights the column."));

            SetActiveCell(context.EditCell);
            EnsureCellVisible(context.EditCell);
            ShowInlineEditor(context.EditCell);
            if (_inlineEditor is not null)
            {
                _inlineEditor.Text = "Draft inline edit";
                _inlineEditor.CaretIndex = _inlineEditor.Text.Length;
                FormulaBar.Text = _inlineEditor.Text;
            }
            captures.Add(await CaptureGridSelectionEditingWindowStateAsync(
                outputDir,
                "inline-edit-mode",
                "freex_grid_selection_editing_inline_edit_mode",
                "grid-window",
                "Inline grid edit mode shows the active editor chrome and caret, formula bar draft text, and Edit status mode."));

            FormulaBar.Text = "Committed grid edit";
            var editCommitted = CommitEdit();
            HideInlineEditor(commit: false);
            if (!editCommitted)
                throw new InvalidOperationException("Grid selection/editing tour could not commit the seeded inline edit.");
            SetActiveCell(context.EditCell);
            EnsureCellVisible(context.EditCell);
            captures.Add(await CaptureGridSelectionEditingWindowStateAsync(
                outputDir,
                "committed-edit-value",
                "freex_grid_selection_editing_committed_value",
                "grid-window",
                "Committed inline edit updates the workbook cell, formula bar, grid cell value, and Ready status."));

            SetSelectionRange(context.FilterVisibleRange, context.FilterVisibleRange.Start);
            EnsureCellVisible(context.FilterVisibleRange.Start);
            captures.Add(await CaptureGridSelectionEditingWindowStateAsync(
                outputDir,
                "filtered-hidden-visible-rows",
                "freex_grid_selection_editing_filtered_hidden_rows",
                "grid-window",
                "Filtered row 6 and manually hidden row 8 are omitted from visible row headers while the AutoFilter range remains active."));

            SetSelectionRange(context.FillRange, context.FillRange.Start);
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Fill Down",
                    context.FillRange,
                    currentRange => new FillCellsCommand(_currentSheetId, currentRange, FillCellsDirection.Down),
                    out var fillOutcome) ||
                fillOutcome.AffectedCells is not { Count: > 0 })
            {
                throw new InvalidOperationException("Grid selection/editing tour could not execute Fill Down.");
            }
            UpdateViewport();
            captures.Add(await CaptureGridSelectionEditingWindowStateAsync(
                outputDir,
                "fill-down-result",
                "freex_grid_selection_editing_fill_down_result",
                "grid-window",
                "Fill Down result shows B10:B12 populated from the source cell while the selection remains on the fill range."));

            SetSelectionRange(context.ClearRange, context.ClearRange.Start);
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Clear Contents",
                    context.ClearRange,
                    currentRange => new ClearContentsCommand(_currentSheetId, currentRange),
                    out var clearOutcome) ||
                clearOutcome.AffectedCells is not { Count: > 0 })
            {
                throw new InvalidOperationException("Grid selection/editing tour could not execute Clear Contents.");
            }
            UpdateViewport();
            SetSelectionRange(context.ClearRange, context.ClearRange.Start);
            captures.Add(await CaptureGridSelectionEditingWindowStateAsync(
                outputDir,
                "clear-contents-result",
                "freex_grid_selection_editing_clear_contents_result",
                "grid-window",
                "Clear Contents result shows C10:C12 blanked while style/grid selection and Name Box remain coherent."));

            ValidateGridSelectionEditingTourEvidence(outputDir, captures);
            await WriteGridSelectionEditingTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteGridSelectionEditingTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (_inlineEditor?.IsVisible == true)
                HideInlineEditor(commit: false);
        }
    }

    private GridSelectionEditingTourContext EnsureGridSelectionEditingTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Grid selection/editing tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        for (uint row = 1; row <= 14; row++)
        {
            for (uint col = 1; col <= 6; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        sheet.RowHeights.Clear();
        sheet.ColumnWidths[1] = 16;
        sheet.ColumnWidths[2] = 18;
        sheet.ColumnWidths[3] = 18;
        sheet.ColumnWidths[4] = 18;
        sheet.ColumnWidths[5] = 20;
        sheet.HiddenRows.Clear();
        sheet.FilterHiddenRows.Clear();
        sheet.GroupHiddenRows.Clear();
        sheet.HiddenCols.Clear();
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:E8", null);

        SetTourCell(sheet, 1, 1, new TextValue("Region"));
        SetTourCell(sheet, 1, 2, new TextValue("Q1"));
        SetTourCell(sheet, 1, 3, new TextValue("Q2"));
        SetTourCell(sheet, 1, 4, new TextValue("Q3"));
        SetTourCell(sheet, 1, 5, new TextValue("Status"));
        SetTourCell(sheet, 2, 1, new TextValue("North"));
        SetTourCell(sheet, 2, 2, new NumberValue(14));
        SetTourCell(sheet, 2, 3, new NumberValue(18));
        SetTourCell(sheet, 2, 4, new NumberValue(21));
        SetTourCell(sheet, 2, 5, new TextValue("Open"));
        SetTourCell(sheet, 3, 1, new TextValue("South"));
        SetTourCell(sheet, 3, 2, new NumberValue(12));
        SetTourCell(sheet, 3, 3, new NumberValue(17));
        SetTourCell(sheet, 3, 4, new NumberValue(19));
        SetTourCell(sheet, 3, 5, new TextValue("Open"));
        SetTourCell(sheet, 4, 1, new TextValue("East"));
        SetTourCell(sheet, 4, 2, new NumberValue(11));
        SetTourCell(sheet, 4, 3, new NumberValue(16));
        SetTourCell(sheet, 4, 4, new NumberValue(20));
        SetTourCell(sheet, 4, 5, new TextValue("Open"));
        SetTourCell(sheet, 5, 1, new TextValue("West"));
        SetTourCell(sheet, 5, 2, new NumberValue(15));
        SetTourCell(sheet, 5, 3, new NumberValue(20));
        SetTourCell(sheet, 5, 4, new NumberValue(24));
        SetTourCell(sheet, 5, 5, new TextValue("Open"));
        SetTourCell(sheet, 6, 1, new TextValue("Filtered row"));
        SetTourCell(sheet, 6, 2, new NumberValue(99));
        SetTourCell(sheet, 6, 5, new TextValue("Closed"));
        SetTourCell(sheet, 7, 1, new TextValue("Visible after filter"));
        SetTourCell(sheet, 7, 2, new NumberValue(22));
        SetTourCell(sheet, 7, 5, new TextValue("Open"));
        SetTourCell(sheet, 8, 1, new TextValue("Hidden row"));
        SetTourCell(sheet, 8, 2, new NumberValue(77));
        SetTourCell(sheet, 8, 5, new TextValue("Open"));
        SetTourCell(sheet, 10, 1, new TextValue("Fill source"));
        SetTourCell(sheet, 10, 2, new NumberValue(42));
        SetTourCell(sheet, 10, 3, new TextValue("Clear source"));
        SetTourCell(sheet, 11, 3, new TextValue("Clear me"));
        SetTourCell(sheet, 12, 3, new TextValue("Clear me too"));

        sheet.FilterHiddenRows.Add(6);
        sheet.HiddenRows.Add(8);

        var selectedCell = new CellAddress(sheet.Id, 2, 2);
        var selectedRange = Range(sheet.Id, 2, 2, 4, 4);
        var editCell = new CellAddress(sheet.Id, 4, 5);
        var filterVisibleRange = Range(sheet.Id, 1, 1, 8, 5);
        var fillRange = Range(sheet.Id, 10, 2, 12, 2);
        var clearRange = Range(sheet.Id, 10, 3, 12, 3);

        SetActiveCell(selectedCell);
        EnsureCellVisible(selectedCell);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new GridSelectionEditingTourContext(
            Sheet: sheet,
            SelectedCell: selectedCell,
            SelectedRange: selectedRange,
            RowSelectionIndex: 5,
            ColumnSelectionIndex: 3,
            EditCell: editCell,
            FilterVisibleRange: filterVisibleRange,
            FillRange: fillRange,
            ClearRange: clearRange,
            FilterHiddenRows: sheet.FilterHiddenRows.OrderBy(row => row).Select(row => row.ToString()).ToArray(),
            ManualHiddenRows: sheet.HiddenRows.OrderBy(row => row).Select(row => row.ToString()).ToArray());
    }

    private async Task<GridSelectionEditingTourManifestCapture> CaptureGridSelectionEditingWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string surface,
        string evidenceSummary)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(150);

        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateGridSelectionEditingCapture(
            state,
            fileName,
            surface,
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary);
    }

    private GridSelectionEditingTourManifestCapture CreateGridSelectionEditingCapture(
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary)
    {
        var selectedRange = SheetGrid.SelectedRange;
        var activeCell = _selectionAnchor ?? selectedRange?.Start;
        var activeCellText = activeCell is { } address
            ? FormatQatUndoRedoTourValue(_workbook.GetSheet(address.Sheet)?.GetCell(address)?.Value)
            : string.Empty;
        var visibleRows = SheetGrid.Viewport?.RowMetrics.Select(row => row.Row.ToString()).ToArray() ?? [];

        return new GridSelectionEditingTourManifestCapture(
            CaptureKey: $"grid-selection-editing:{state}",
            PairKey: $"interactive:grid-selection-editing:{state}",
            ScenarioId: "grid-selection-editing:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SelectedRange: selectedRange?.ToString() ?? string.Empty,
            ActiveCell: activeCell?.ToA1() ?? string.Empty,
            NameBoxText: CellAddressBox.Text,
            FormulaBarText: FormulaBar.Text,
            StatusReadyText: StatusReadyText.Text,
            StatusAverageText: StatusAvgText.Text,
            StatusCountText: StatusCountText.Text,
            StatusNumericalCountText: StatusNumericalCountText.Text,
            StatusSumText: StatusSumText.Text,
            EditingCell: SheetGrid.EditingCell?.ToA1() ?? string.Empty,
            InlineEditorVisible: _inlineEditor?.IsVisible == true,
            ActiveCellText: activeCellText,
            VisibleRows: visibleRows,
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteGridSelectionEditingTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_grid_selection_editing_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, GridSelectionEditingTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateGridSelectionEditingTourEvidence(
        string outputDir,
        IReadOnlyList<GridSelectionEditingTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Grid selection/editing tour expected capture '{capture.OutputFileName}' was not written.");
        }
    }

    private async Task CaptureStatusFooterTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteStatusFooterTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 760;
        await Task.Delay(700);

        var sheet = EnsureStatusFooterTourContext();
        var captures = new List<StatusFooterTourManifestCapture>();

        try
        {
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "ready-baseline",
                "freex_status_footer_ready_baseline",
                "Ready footer with Normal view shortcut, 100% zoom text, zoom buttons, and slider visible.",
                captureFullWindow: false));

            SelectStatusFooterTourRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)));
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "selection-stats",
                "freex_status_footer_selection_stats_numeric_mixed",
                "Numeric plus text selection showing Average, Count, Numerical Count, Sum, Min, and Max footer statistics.",
                captureFullWindow: false));

            BeginFormulaBarFormulaEdit("=SUM(A1:A4)");
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "formula-edit-mode",
                "freex_status_footer_formula_edit_mode",
                "Formula edit mode with footer mode text set to Edit and the formula bar showing the in-progress formula.",
                captureFullWindow: true));
            HideInlineEditor(commit: false);
            FocusSheetGridIfNeeded();

            SetWorksheetViewMode(WorksheetViewMode.PageLayout);
            RefreshStatusBar();
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "view-shortcut-page-layout",
                "freex_status_footer_view_shortcut_page_layout",
                "Status bar view shortcut buttons with Page Layout selected.",
                captureFullWindow: false));

            SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);
            RefreshStatusBar();
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "view-shortcut-page-break-preview",
                "freex_status_footer_view_shortcut_page_break_preview",
                "Status bar view shortcut buttons with Page Break Preview selected.",
                captureFullWindow: false));

            SetWorksheetViewMode(WorksheetViewMode.Normal);
            await SetStatusFooterTourZoomAsync(10);
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "zoom-min-10-percent",
                "freex_status_footer_zoom_min_10",
                "Minimum representative zoom state with 10% footer text, slider at minimum, and visibly scaled grid.",
                captureFullWindow: true));

            await SetStatusFooterTourZoomAsync(100);
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "zoom-baseline-100-percent",
                "freex_status_footer_zoom_baseline_100",
                "Baseline zoom state with 100% footer text, midpoint slider, and normal grid scale.",
                captureFullWindow: true));

            await SetStatusFooterTourZoomAsync(400);
            captures.Add(await CaptureStatusFooterWindowStateAsync(
                outputDir,
                "zoom-max-400-percent",
                "freex_status_footer_zoom_max_400",
                "Maximum representative zoom state with 400% footer text, slider at maximum, and visibly enlarged grid.",
                captureFullWindow: true));

            ValidateStatusFooterTourEvidence(outputDir, captures);
            await WriteStatusFooterTourManifestAsync(outputDir, captures);
        }
        catch
        {
            DeleteStatusFooterTourEvidence(outputDir);
            throw;
        }
    }

    private Sheet EnsureStatusFooterTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Status/footer tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        _options.StatusBarShowCellMode = true;
        _options.StatusBarShowAverage = true;
        _options.StatusBarShowCount = true;
        _options.StatusBarShowNumericalCount = true;
        _options.StatusBarShowSum = true;
        _options.StatusBarShowMinimum = true;
        _options.StatusBarShowMaximum = true;
        _options.StatusBarShowViewShortcuts = true;
        _options.StatusBarShowZoom = true;
        _options.StatusBarShowZoomSlider = true;

        var values = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)),
            (4, 1, new NumberValue(40)),
            (1, 2, new NumberValue(5)),
            (2, 2, new NumberValue(15)),
            (3, 2, new NumberValue(25)),
            (4, 2, new NumberValue(35)),
            (1, 3, new TextValue("North")),
            (2, 3, new TextValue("South")),
            (3, 3, new TextValue("East")),
            (4, 3, new TextValue("West"))
        };

        for (uint row = 1; row <= 8; row++)
        {
            for (uint col = 1; col <= 5; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        foreach (var value in values)
            sheet.SetCell(new CellAddress(sheet.Id, value.Row, value.Col), value.Value);

        var activeCell = new CellAddress(sheet.Id, 1, 1);
        SelectStatusFooterTourRange(new GridRange(activeCell, activeCell));
        SyncZoomFromSheet(100);
        return sheet;
    }

    private void SelectStatusFooterTourRange(GridRange range)
    {
        SetActiveCell(range.Start);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = range;
            SheetGrid.SelectedRanges = null;
            SheetGrid.Focus();
        }

        var cell = _workbook.GetSheet(range.Start.Sheet)?.GetCell(range.Start);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, range.Start));
        UpdateViewport();
        RefreshStatusBar();
    }

    private async Task SetStatusFooterTourZoomAsync(int zoomPercent)
    {
        ZoomSlider.Value = StatusZoomSliderValueForPercent(zoomPercent);
        RefreshStatusBar();
        UpdateViewport();
        await Task.Delay(250);
    }

    private async Task<StatusFooterTourManifestCapture> CaptureStatusFooterWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidencePurpose,
        bool captureFullWindow)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        if (captureFullWindow)
            await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        else
            await CaptureElementAsync(StatusBarRoot, outputDir, fileName);

        return CreateStatusFooterTourCapture(state, fileName, evidencePurpose, captureFullWindow);
    }

    private StatusFooterTourManifestCapture CreateStatusFooterTourCapture(
        string state,
        string fileName,
        string evidencePurpose,
        bool captureFullWindow)
    {
        var activeRange = SheetGrid?.SelectedRange;
        var viewMode = _workbook.GetSheet(_currentSheetId)?.ViewMode ?? WorksheetViewMode.Normal;
        return new StatusFooterTourManifestCapture(
            CaptureKey: $"interactive:status-footer:{state}",
            PairKey: $"interactive:status-footer:{state}",
            ScenarioId: "status-footer:visual-evidence",
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureFullWindow
                ? "RenderTargetBitmap-window-full"
                : "RenderTargetBitmap-status-footer-element",
            EvidencePurpose: evidencePurpose,
            CaptureLogicalWidth: captureFullWindow ? ActualWidth : StatusBarRoot.ActualWidth,
            CaptureLogicalHeight: captureFullWindow ? Math.Min(ActualHeight, 760) : StatusBarRoot.ActualHeight,
            ActiveRange: activeRange?.ToString() ?? string.Empty,
            StatusModeText: StatusReadyText.Text,
            StatusModeVisible: StatusReadyText.Visibility == Visibility.Visible,
            AverageText: StatusAvgText.Text,
            CountText: StatusCountText.Text,
            NumericalCountText: StatusNumericalCountText.Text,
            SumText: StatusSumText.Text,
            MinText: StatusMinText.Text,
            MaxText: StatusMaxText.Text,
            StatsVisible: StatusStatsPanel.Visibility == Visibility.Visible,
            ViewMode: viewMode.ToString(),
            NormalViewChecked: StatusNormalViewButton.IsChecked == true,
            PageLayoutViewChecked: StatusPageLayoutViewButton.IsChecked == true,
            PageBreakPreviewChecked: StatusPageBreakPreviewButton.IsChecked == true,
            ZoomText: StatusZoomText.Text,
            ZoomSliderValue: ZoomSlider.Value,
            ZoomOutButtonEnabled: StatusZoomOutButton.IsEnabled,
            ZoomInButtonEnabled: StatusZoomInButton.IsEnabled,
            FormulaBarText: FormulaBar.Text);
    }

    private static void DeleteStatusFooterTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_status_footer_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, StatusFooterTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateTitlebarWindowChromeTourEvidence(string outputDir, IReadOnlyList<TitlebarWindowChromeTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Titlebar/window chrome tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private static void ValidateFormulaBarNameBoxTourEvidence(string outputDir, IReadOnlyList<FormulaBarNameBoxTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Formula bar/name box tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private static void ValidateStatusFooterTourEvidence(string outputDir, IReadOnlyList<StatusFooterTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Status/footer tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private async Task CaptureFormulaDiagnosticsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteFormulaDiagnosticsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 760;
        await Task.Delay(700);

        var context = EnsureFormulaDiagnosticsTourContext();
        var captures = new List<FormulaDiagnosticsTourManifestCapture>();
        ErrorCheckingDialog? errorCheckingDialog = null;
        EvaluateFormulaDialog? evaluateFormulaDialog = null;
        AddWatchDialog? addWatchDialog = null;
        WatchWindowDialog? watchWindowDialog = null;

        try
        {
            SetFormulaDiagnosticsTourSelection(context.ResultCell);
            TracePrecedentsForCell(context.ResultCell, "Trace Precedents");
            captures.Add(await CaptureFormulaDiagnosticsWindowStateAsync(
                outputDir,
                "trace-precedents-visible",
                "freex_formula_diagnostics_trace_precedents",
                "window-full",
                "Trace Precedents draws visible formula auditing arrows from A2/A3 into B2."));

            SetFormulaDiagnosticsTourSelection(context.InputCell);
            TraceDependentsBtn_Click(this, new RoutedEventArgs());
            captures.Add(await CaptureFormulaDiagnosticsWindowStateAsync(
                outputDir,
                "trace-dependents-visible",
                "freex_formula_diagnostics_trace_dependents",
                "window-full",
                "Trace Dependents adds a visible auditing arrow from A2 toward B2 without clearing the existing precedent arrows."));

            SetFormulaDiagnosticsTourSelection(context.ResultCell);
            ShowFormulasBtn_Click(this, new RoutedEventArgs());
            captures.Add(await CaptureFormulaDiagnosticsWindowStateAsync(
                outputDir,
                "show-formulas-enabled",
                "freex_formula_diagnostics_show_formulas_enabled",
                "window-full",
                "Show Formulas toggles the active sheet to display formula text such as =A2+A3 and =B2/0 in the grid."));

            ShowFormulasBtn_Click(this, new RoutedEventArgs());
            RemoveTraceArrows(kind: null, "Remove Arrows");
            captures.Add(await CaptureFormulaDiagnosticsWindowStateAsync(
                outputDir,
                "remove-arrows-cleared",
                "freex_formula_diagnostics_remove_arrows_cleared",
                "window-full",
                "Remove Arrows clears the in-memory formula trace arrows and returns the sheet to value display mode."));

            var issues = FormulaAuditingService.FindFormulaErrorIssues(_workbook, _currentSheetId, _session.CyclicCells);
            if (issues.Count == 0)
                throw new InvalidOperationException("Formula diagnostics tour expected at least one formula error issue.");

            errorCheckingDialog = new ErrorCheckingDialog(
                issues,
                address =>
                {
                    NavigateToCell(address);
                    RefreshSheetTabs();
                    UpdateViewport();
                    RefreshStatusBar();
                },
                issue => true,
                issue => TracePrecedentsForCell(issue.Address, "Trace Error"),
                issue =>
                {
                    var summary = FormulaEvaluationSummaryService.GetSummary(_workbook, issue.Address)
                        ?? throw new InvalidOperationException("Formula diagnostics tour expected an evaluation summary for the selected error issue.");
                    var stepsDialog = new EvaluateFormulaDialog(summary) { Owner = this };
                    stepsDialog.Show();
                },
                openOptions: null)
            {
                Owner = this
            };
            errorCheckingDialog.Show();
            errorCheckingDialog.Activate();
            errorCheckingDialog.UpdateLayout();
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(errorCheckingDialog, outputDir, "freex_formula_diagnostics_error_checking_dialog");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "error-checking-dialog-list",
                "freex_formula_diagnostics_error_checking_dialog",
                "error-checking-dialog",
                "RenderTargetBitmap-error-checking-dialog",
                errorCheckingDialog.ActualWidth,
                errorCheckingDialog.ActualHeight,
                "Error Checking dialog opens with the issue list, selected first error, side actions, bottom navigation, Ignore, Trace Error, Options, and Close controls."));
            errorCheckingDialog.Close();
            errorCheckingDialog = null;

            var resultSummary = FormulaEvaluationSummaryService.GetSummary(_workbook, context.ResultCell)
                ?? throw new InvalidOperationException("Formula diagnostics tour expected an evaluation summary for the result cell.");
            evaluateFormulaDialog = new EvaluateFormulaDialog(resultSummary) { Owner = this };
            evaluateFormulaDialog.Show();
            evaluateFormulaDialog.Activate();
            evaluateFormulaDialog.UpdateLayout();
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(evaluateFormulaDialog, outputDir, "freex_formula_diagnostics_evaluate_default");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "evaluate-formula-default-button",
                "freex_formula_diagnostics_evaluate_default",
                "evaluate-formula-dialog",
                "RenderTargetBitmap-evaluate-formula-dialog",
                evaluateFormulaDialog.ActualWidth,
                evaluateFormulaDialog.ActualHeight,
                "Evaluate Formula dialog opens on B2 with the Evaluate command as the focused/default command and Close as the cancel command."));

            var evaluateButton = FindDescendantButtonByContent(evaluateFormulaDialog, UiText.Get("EvaluateFormula_EvaluateButton"))
                ?? throw new InvalidOperationException("Formula diagnostics tour could not find the Evaluate Formula default button.");
            evaluateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, evaluateButton));
            await Task.Delay(250);
            evaluateFormulaDialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(evaluateFormulaDialog, outputDir, "freex_formula_diagnostics_evaluate_after_step");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "evaluate-formula-after-step",
                "freex_formula_diagnostics_evaluate_after_step",
                "evaluate-formula-dialog",
                "RenderTargetBitmap-evaluate-formula-dialog",
                evaluateFormulaDialog.ActualWidth,
                evaluateFormulaDialog.ActualHeight,
                "Evaluate advances one deterministic calculation step while preserving the Evaluate/Step In/Step Out/Restart/Close/Help command row."));
            evaluateFormulaDialog.Close();
            evaluateFormulaDialog = null;

            SetFormulaDiagnosticsTourSelection(context.ResultCell);
            addWatchDialog = new AddWatchDialog(FormatRangeReference(context.ResultCell, context.ResultCell)) { Owner = this };
            addWatchDialog.Show();
            addWatchDialog.Activate();
            addWatchDialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(addWatchDialog, outputDir, "freex_formula_diagnostics_watch_add_dialog");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "watch-window-add-dialog",
                "freex_formula_diagnostics_watch_add_dialog",
                "watch-window-add-dialog",
                "RenderTargetBitmap-add-watch-dialog",
                addWatchDialog.ActualWidth,
                addWatchDialog.ActualHeight,
                "Add Watch dialog shows the selected B2 range, Add default button, Cancel button, and stable AddWatch automation IDs."));
            addWatchDialog.Close();
            addWatchDialog = null;

            WatchWindowService.AddWatches(_workbook, new GridRange(context.ResultCell, context.ResultCell));
            WatchWindowService.AddWatches(_workbook, new GridRange(context.ErrorCell, context.ErrorCell));
            watchWindowDialog = CreateFormulaDiagnosticsWatchWindowDialog();
            watchWindowDialog.Show();
            watchWindowDialog.Activate();
            watchWindowDialog.UpdateLayout();
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(watchWindowDialog, outputDir, "freex_formula_diagnostics_watch_window_list");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "watch-window-list",
                "freex_formula_diagnostics_watch_window_list",
                "watch-window-dialog",
                "RenderTargetBitmap-watch-window-dialog",
                watchWindowDialog.ActualWidth,
                watchWindowDialog.ActualHeight,
                "Watch Window lists B2 and D2 with workbook, sheet, cell, value, and formula columns plus Add Watch, Refresh, Delete Watch, and Close controls."));

            var refreshButton = FindDescendantByAutomationId<Button>(watchWindowDialog, "WatchWindowRefreshButton")
                ?? throw new InvalidOperationException("Formula diagnostics tour could not find the Watch Window Refresh button.");
            refreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, refreshButton));
            await Task.Delay(250);
            await CaptureWindowElementForScreenshotTourAsync(watchWindowDialog, outputDir, "freex_formula_diagnostics_watch_window_after_refresh");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "watch-window-after-refresh",
                "freex_formula_diagnostics_watch_window_after_refresh",
                "watch-window-dialog",
                "RenderTargetBitmap-watch-window-dialog",
                watchWindowDialog.ActualWidth,
                watchWindowDialog.ActualHeight,
                "Refresh rehydrates the watched rows while preserving the selected watched cell when possible."));

            var watchList = FindDescendantByAutomationId<ListView>(watchWindowDialog, "WatchWindowList")
                ?? throw new InvalidOperationException("Formula diagnostics tour could not find the Watch Window list.");
            if (watchList.Items.Count > 0)
                watchList.SelectedIndex = 0;
            var deleteButton = FindDescendantByAutomationId<Button>(watchWindowDialog, "WatchWindowDeleteButton")
                ?? throw new InvalidOperationException("Formula diagnostics tour could not find the Watch Window Delete Watch button.");
            deleteButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, deleteButton));
            await Task.Delay(250);
            await CaptureWindowElementForScreenshotTourAsync(watchWindowDialog, outputDir, "freex_formula_diagnostics_watch_window_after_delete");
            captures.Add(CreateFormulaDiagnosticsCapture(
                "watch-window-after-delete",
                "freex_formula_diagnostics_watch_window_after_delete",
                "watch-window-dialog",
                "RenderTargetBitmap-watch-window-dialog",
                watchWindowDialog.ActualWidth,
                watchWindowDialog.ActualHeight,
                "Delete Watch removes the selected watched row and leaves the remaining watched formula visible."));
            watchWindowDialog.Close();
            watchWindowDialog = null;

            ValidateFormulaDiagnosticsTourEvidence(outputDir, captures);
            await WriteFormulaDiagnosticsTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteFormulaDiagnosticsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (errorCheckingDialog is { IsVisible: true })
                errorCheckingDialog.Close();
            if (evaluateFormulaDialog is { IsVisible: true })
                evaluateFormulaDialog.Close();
            if (addWatchDialog is { IsVisible: true })
                addWatchDialog.Close();
            if (watchWindowDialog is { IsVisible: true })
                watchWindowDialog.Close();

            _formulaTraceArrows.Clear();
            UpdateViewport();
        }
    }

    private FormulaDiagnosticsTourContext EnsureFormulaDiagnosticsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Formula diagnostics tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        _formulaTraceArrows.Clear();
        WatchWindowService.RemoveWatches(
            _workbook,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 8, 6)));

        for (uint row = 1; row <= 8; row++)
        {
            for (uint col = 1; col <= 6; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Input"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Result"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Error"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(8));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "A2+A3");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 4), "B2/0");
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 4), "B2+A2");

        RecalculateWorkbook();
        var resultCell = new CellAddress(sheet.Id, 2, 2);
        SetFormulaDiagnosticsTourSelection(resultCell);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new FormulaDiagnosticsTourContext(
            SheetName: sheet.Name,
            InputCell: new CellAddress(sheet.Id, 2, 1),
            ResultCell: resultCell,
            ErrorCell: new CellAddress(sheet.Id, 2, 4),
            ResultFormula: sheet.GetCell(resultCell)?.FormulaText ?? "",
            ErrorFormula: sheet.GetCell(new CellAddress(sheet.Id, 2, 4))?.FormulaText ?? "");
    }

    private void SetFormulaDiagnosticsTourSelection(CellAddress address)
    {
        var range = new GridRange(address, address);
        SetSelectionRange(range, address);
        EnsureCellVisible(address);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private WatchWindowDialog CreateFormulaDiagnosticsWatchWindowDialog() =>
        new(
            () =>
            {
                RecalculateWorkbook();
                return WatchWindowService.GetEntries(_workbook);
            },
            () => AddWatchFromSelection(showMessage: false),
            () => SheetGrid.SelectedRange is { } range
                ? FormatRangeReference(range.Start, range.End)
                : "",
            address =>
            {
                NavigateToCell(address);
                RefreshSheetTabs();
                UpdateViewport();
                RefreshStatusBar();
            },
            address =>
            {
                WatchWindowService.RemoveWatch(_workbook, address);
                UpdateViewport();
            })
        {
            Owner = this
        };

    private async Task<FormulaDiagnosticsTourManifestCapture> CaptureFormulaDiagnosticsWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string surface,
        string evidenceSummary)
    {
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(150);

        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateFormulaDiagnosticsCapture(
            state,
            fileName,
            surface,
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary);
    }

    private FormulaDiagnosticsTourManifestCapture CreateFormulaDiagnosticsCapture(
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var selectedRange = SheetGrid.SelectedRange;
        return new FormulaDiagnosticsTourManifestCapture(
            CaptureKey: $"formula-diagnostics:{state}",
            PairKey: $"interactive:formula-diagnostics:{state}",
            ScenarioId: "formula-diagnostics:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SelectedRange: selectedRange?.ToString() ?? string.Empty,
            ShowFormulas: sheet?.ShowFormulas == true,
            FormulaTraceArrowCount: _formulaTraceArrows.Count,
            WatchCount: WatchWindowService.GetEntries(_workbook).Count,
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteFormulaDiagnosticsTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_formula_diagnostics_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, FormulaDiagnosticsTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private async Task CaptureReviewCommentsProtectionTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteReviewCommentsProtectionTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 760;
        await Task.Delay(700);

        var context = EnsureReviewCommentsProtectionTourContext();
        var captures = new List<ReviewCommentsProtectionTourManifestCapture>();
        Window? openDialog = null;

        try
        {
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Review"));
            RefreshReviewCommentNoteCommandStates();
            captures.Add(await CaptureReviewCommentsProtectionWindowStateAsync(
                outputDir,
                "review-tab-supported-surfaces",
                "freex_review_comments_protection_review_tab",
                "Review tab",
                "Review tab shows supported FreeX proofing, accessibility, comments, notes, protection, and sharing controls; unsupported Thesaurus and change-history commands are not exposed."));

            openDialog = new SpellCheckDialog(context.SpellingWord, context.SpellingSuggestion) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewCommentsProtectionDialogAsync(
                openDialog,
                outputDir,
                "spell-check-dialog",
                "Spelling",
                "freex_review_spell_check_dialog",
                "Spell Check dialog shows the misspelled word, suggestion list, replacement editor, Ignore/Ignore All/Change/Change All/Add/Cancel command row, and production automation IDs."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            var accessibilityIssues = AccessibilityCheckerService.FindIssues(_workbook);
            if (accessibilityIssues.Count == 0)
                throw new InvalidOperationException("Review tour expected at least one accessibility issue.");
            openDialog = new AccessibilityCheckerDialog(accessibilityIssues) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewCommentsProtectionDialogAsync(
                openDialog,
                outputDir,
                "accessibility-checker-dialog",
                "Accessibility Checker",
                "freex_review_accessibility_checker_dialog",
                "Accessibility Checker dialog lists seeded merged-cell/default-sheet issues with Go To and Close controls."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            captures.Add(await CaptureReviewCommentsProtectionInlineThreadedCommentEditorAsync(outputDir, context));

            openDialog = new CommentListWindow(
                UiText.Get("MainWindowMessage_CommentsTitle"),
                CommentListWindow.CreateThreadedCommentItems(context.Sheet.ThreadedComments),
                NavigateToCell)
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewCommentsProtectionDialogAsync(
                openDialog,
                outputDir,
                "show-comments-list",
                "Show Comments",
                "freex_review_show_comments_list",
                "Show Comments opens the modeless threaded comments list with Cell/Text columns, Open, Close, and first-item selection."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new CommentListWindow(
                UiText.Get("MainWindow_Text_Notes"),
                CommentListWindow.CreateNoteItems(context.Sheet.Comments),
                NavigateToCell)
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewCommentsProtectionDialogAsync(
                openDialog,
                outputDir,
                "show-notes-list",
                "Show Notes",
                "freex_review_show_notes_list",
                "Show Notes opens the modeless simple notes list with Cell/Text columns, Open, Close, and first-item selection."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new PasswordProtectionDialog(
                UiText.Get("MainWindowMessage_ProtectSheetTitle"),
                UiText.Get("MainWindowMessage_OptionalPasswordLabel"))
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewCommentsProtectionDialogAsync(
                openDialog,
                outputDir,
                "protect-sheet-dialog",
                "Protect Sheet",
                "freex_review_protect_sheet_dialog",
                "Protect Sheet dialog shows the optional password field, caution text, and sheet-permission checklist."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new PasswordProtectionDialog(
                UiText.Get("MainWindowMessage_ProtectWorkbookTitle"),
                UiText.Get("MainWindowMessage_OptionalPasswordLabel"))
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewCommentsProtectionDialogAsync(
                openDialog,
                outputDir,
                "protect-workbook-dialog",
                "Protect Workbook",
                "freex_review_protect_workbook_dialog",
                "Protect Workbook dialog shows the workbook-structure optional password prompt and caution text."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new AllowEditRangeDialog(
                context.Sheet.Id,
                context.AllowEditRange.ToString(),
                context.Sheet.AllowEditRanges,
                request => { })
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewCommentsProtectionDialogAsync(
                openDialog,
                outputDir,
                "allow-edit-ranges-dialog",
                "Allow Users to Edit Ranges",
                "freex_review_allow_edit_ranges_dialog",
                "Allow Users to Edit Ranges dialog shows the existing editable range list, New/Modify/Delete actions, disabled Permissions button, range editor, and picker."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            ValidateReviewCommentsProtectionTourEvidence(outputDir, captures);
            await WriteReviewCommentsProtectionTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteReviewCommentsProtectionTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openDialog is { IsVisible: true })
                CloseDataToolsTourDialog(openDialog);
        }
    }

    private ReviewCommentsProtectionTourContext EnsureReviewCommentsProtectionTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Review comments/protection tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        for (uint row = 1; row <= 10; row++)
        {
            for (uint col = 1; col <= 7; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        sheet.Comments.Clear();
        sheet.ThreadedComments.Clear();
        sheet.AllowEditRanges.Clear();
        sheet.ReplaceMergedRegions([]);
        sheet.ColumnWidths[1] = 16;
        sheet.ColumnWidths[2] = 24;
        sheet.ColumnWidths[3] = 24;
        sheet.ColumnWidths[4] = 22;

        SetTourCell(sheet, 1, 1, new TextValue("Review tour"));
        SetTourCell(sheet, 2, 1, new TextValue("mispelled total"));
        SetTourCell(sheet, 2, 2, new TextValue("Threaded comment anchor"));
        SetTourCell(sheet, 3, 2, new TextValue("Simple note anchor"));
        SetTourCell(sheet, 4, 1, new TextValue("Merged accessibility issue"));
        SetTourCell(sheet, 6, 1, new TextValue("Editable range"));
        SetTourCell(sheet, 6, 2, new TextValue("Team-owned cells"));

        var threadedCell = new CellAddress(sheet.Id, 2, 2);
        var noteCell = new CellAddress(sheet.Id, 3, 2);
        var newThreadedCell = new CellAddress(sheet.Id, 2, 4);
        var allowEditRange = Range(sheet.Id, 6, 1, 6, 3);
        sheet.ThreadedComments[threadedCell] = new ThreadedComment("Review seeded threaded comment", "FreeX")
        {
            Replies =
            [
                new CommentReply("Follow-up reply for list evidence", "FreeX QA")
            ]
        };
        sheet.Comments[noteCell] = "Review seeded simple note.";
        sheet.AllowEditRanges.Add(allowEditRange);
        sheet.AddMergedRegion(Range(sheet.Id, 4, 1, 4, 3));

        var selection = Range(sheet.Id, 2, 1, 6, 4);
        SetSelectionRange(selection, selection.Start);
        EnsureCellVisible(selection.Start);
        RefreshReviewCommentNoteCommandStates();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateLayout();

        return new ReviewCommentsProtectionTourContext(
            Sheet: sheet,
            SpellingCell: new CellAddress(sheet.Id, 2, 1),
            SpellingWord: "mispelled",
            SpellingSuggestion: "misspelled",
            ThreadedCommentCell: threadedCell,
            NoteCell: noteCell,
            NewThreadedCommentCell: newThreadedCell,
            AllowEditRange: allowEditRange);
    }

    private async Task<ReviewCommentsProtectionTourManifestCapture> CaptureReviewCommentsProtectionWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string surface,
        string evidenceSummary)
    {
        RefreshReviewCommentNoteCommandStates();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateReviewCommentsProtectionCapture(
            state,
            surface,
            fileName,
            "RenderTargetBitmap-main-window",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary);
    }

    private async Task<ReviewCommentsProtectionTourManifestCapture> CaptureReviewCommentsProtectionInlineThreadedCommentEditorAsync(
        string outputDir,
        ReviewCommentsProtectionTourContext context)
    {
        var address = context.NewThreadedCommentCell;
        SetSelectionRange(new GridRange(address, address), address);
        EnsureCellVisible(address);
        UpdateViewport();
        RefreshReviewCommentNoteCommandStates();
        RefreshToolbar();
        UpdateLayout();

        try
        {
            if (!SheetGrid.BeginThreadedCommentInlineEdit(address, address.ToA1(), existing: null))
                throw new InvalidOperationException("Review comments/protection tour could not open the inline New Comment popup.");

            await Task.Delay(300);
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            const string fileName = "freex_review_new_threaded_comment_inline_popup";
            await CaptureCurrentWindowAsync(outputDir, fileName, 760);
            return CreateReviewCommentsProtectionCapture(
                "new-threaded-comment-inline-popup",
                "New Comment",
                fileName,
                "RenderTargetBitmap-main-window",
                ActualWidth,
                Math.Min(ActualHeight, 760),
                "New Comment opens as a FreeX-owned in-window yellow popup anchored near the selected cell, with the threaded-comment editor focused inline.");
        }
        finally
        {
            SheetGrid.HideCommentPreview();
        }
    }

    private async Task<ReviewCommentsProtectionTourManifestCapture> CaptureReviewCommentsProtectionDialogAsync(
        Window dialog,
        string outputDir,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        await WaitForDataToolsDialogRenderAsync(dialog);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        return CreateReviewCommentsProtectionCapture(
            state,
            surface,
            fileName,
            "RenderTargetBitmap-review-dialog-window",
            dialog.ActualWidth,
            dialog.ActualHeight,
            evidenceSummary);
    }

    private ReviewCommentsProtectionTourManifestCapture CreateReviewCommentsProtectionCapture(
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return new ReviewCommentsProtectionTourManifestCapture(
            CaptureKey: $"review-comments-protection:{state}",
            PairKey: $"interactive:review-comments-protection:{state}",
            ScenarioId: "review-comments-protection:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SelectedRange: SheetGrid.SelectedRange?.ToString() ?? string.Empty,
            ThreadedCommentCount: sheet?.ThreadedComments.Count ?? 0,
            NoteCount: sheet?.Comments.Count ?? 0,
            AllowEditRangeCount: sheet?.AllowEditRanges.Count ?? 0,
            AccessibilityIssueCount: AccessibilityCheckerService.FindIssues(_workbook).Count,
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteReviewCommentsProtectionTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_review_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, ReviewCommentsProtectionTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateReviewCommentsProtectionTourEvidence(
        string outputDir,
        IReadOnlyList<ReviewCommentsProtectionTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Review comments/protection tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private async Task CaptureViewPanesZoomTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteViewPanesZoomTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 760;
        await Task.Delay(700);

        var originalFormulaBarVisible = _options.ShowFormulaBar;
        var sheet = EnsureViewPanesZoomTourContext();
        var captures = new List<ViewPanesZoomTourManifestCapture>();

        try
        {
            captures.Add(await CaptureViewPanesZoomWindowStateAsync(
                outputDir,
                "view-tab-normal-baseline",
                "freex_view_panes_zoom_view_tab_normal",
                "View tab with Normal workbook view selected, Show toggles on, 100% zoom, and baseline grid geometry."));

            SetWorksheetViewMode(WorksheetViewMode.PageLayout);
            captures.Add(await CaptureViewPanesZoomWindowStateAsync(
                outputDir,
                "page-layout-ruler-on",
                "freex_view_panes_zoom_page_layout_ruler_on",
                "Page Layout workbook view with ruler toggle enabled and on."));

            SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);
            captures.Add(await CaptureViewPanesZoomWindowStateAsync(
                outputDir,
                "page-break-preview",
                "freex_view_panes_zoom_page_break_preview",
                "Page Break Preview workbook view selected through the same workbook-view command path."));

            SetWorksheetViewMode(WorksheetViewMode.PageLayout);
            SetViewPanesZoomTourShowToggles(showGridlines: false, showHeadings: false, showRulers: false);
            SetViewPanesZoomTourFormulaBarVisible(false);
            captures.Add(await CaptureViewPanesZoomWindowStateAsync(
                outputDir,
                "show-toggles-hidden",
                "freex_view_panes_zoom_show_toggles_hidden",
                "Gridlines, headings, ruler, and formula bar hidden with ribbon checkbox state visible."));

            SetViewPanesZoomTourShowToggles(showGridlines: true, showHeadings: true, showRulers: true);
            SetViewPanesZoomTourFormulaBarVisible(true);
            SetWorksheetViewMode(WorksheetViewMode.Normal);
            SelectViewPanesZoomTourRange(sheet, new GridRange(new CellAddress(sheet.Id, 4, 3), new CellAddress(sheet.Id, 4, 3)));
            FreezeAtSelectionMenuItem_Click(this, new RoutedEventArgs());
            captures.Add(await CaptureViewPanesZoomWindowStateAsync(
                outputDir,
                "freeze-panes-c4",
                "freex_view_panes_zoom_freeze_panes_c4",
                "Freeze Panes at C4, showing frozen rows and columns in the grid model and visible pane geometry."));

            SelectViewPanesZoomTourRange(sheet, new GridRange(new CellAddress(sheet.Id, 6, 5), new CellAddress(sheet.Id, 6, 5)));
            SplitViewBtn_Click(this, new RoutedEventArgs());
            captures.Add(await CaptureViewPanesZoomWindowStateAsync(
                outputDir,
                "split-panes-e6",
                "freex_view_panes_zoom_split_panes_e6",
                "Split panes at E6 after the Split command clears the previous frozen-pane state."));

            var zoomDialog = new ZoomDialog(125) { Owner = this };
            try
            {
                zoomDialog.Show();
                await Task.Delay(350);
                zoomDialog.UpdateLayout();
                await CaptureWindowElementForScreenshotTourAsync(zoomDialog, outputDir, "freex_view_panes_zoom_dialog_custom_125");
                captures.Add(CreateViewPanesZoomTourCapture(
                    "zoom-dialog-custom-125",
                    "freex_view_panes_zoom_dialog_custom_125",
                    "Zoom dialog opened with a custom 125% value selected in the production ZoomDialog surface.",
                    "RenderTargetBitmap-zoom-dialog-window"));
            }
            finally
            {
                zoomDialog.Close();
            }

            await SetViewPanesZoomTourZoomAsync(175);
            Zoom100Btn_Click(this, new RoutedEventArgs());
            captures.Add(await CaptureViewPanesZoomWindowStateAsync(
                outputDir,
                "zoom-100-command",
                "freex_view_panes_zoom_100_percent_command",
                "View ribbon 100% command resets the worksheet zoom and status zoom text to 100%."));

            SelectViewPanesZoomTourRange(sheet, new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 18, 8)));
            ZoomSelectionBtn_Click(this, new RoutedEventArgs());
            captures.Add(await CaptureViewPanesZoomWindowStateAsync(
                outputDir,
                "zoom-to-selection",
                "freex_view_panes_zoom_to_selection",
                "Zoom to Selection fits the selected A1:H18 range to the visible grid viewport."));

            if (TryExecuteCommand(new SetWorkbookWindowArrangementCommand(WorkbookWindowArrangement.Horizontal), "Arrange Windows"))
            {
                captures.Add(await CaptureViewPanesZoomWindowStateAsync(
                    outputDir,
                    "arrange-horizontal-state",
                    "freex_view_panes_zoom_arrange_horizontal_state",
                    "Arrange All command state set to Horizontal and reflected in the workbook window arrangement model."));
            }

            var arrangeButton = FindDescendantByRibbonCommandName<Button>(RibbonTabs, "Arrange All");
            if (arrangeButton?.ContextMenu is { } arrangeMenu)
            {
                OpenRibbonContextMenu(arrangeButton, arrangeMenu);
                await Task.Delay(350);
                arrangeMenu.UpdateLayout();
                await CaptureElementAsync(arrangeMenu, outputDir, "freex_view_panes_zoom_arrange_all_menu_opened");
                captures.Add(CreateViewPanesZoomTourCapture(
                    "arrange-all-menu-opened",
                    "freex_view_panes_zoom_arrange_all_menu_opened",
                    "Arrange All menu opened with live checked layout state.",
                    "RenderTargetBitmap-arrange-all-context-menu"));
                arrangeMenu.IsOpen = false;
            }

            if (_workbook.CustomViews.All(view => !string.Equals(view.Name, ViewPanesZoomTourCustomViewName, StringComparison.OrdinalIgnoreCase)))
                TryExecuteCommand(new SaveCustomViewCommand(ViewPanesZoomTourCustomViewName), "Save Custom View");

            var customViewsDialog = new CustomViewsDialog(_workbook, _commandBus) { Owner = this };
            try
            {
                customViewsDialog.Show();
                await Task.Delay(350);
                customViewsDialog.UpdateLayout();
                await CaptureWindowElementForScreenshotTourAsync(customViewsDialog, outputDir, "freex_view_panes_zoom_custom_views_dialog");
                captures.Add(CreateViewPanesZoomTourCapture(
                    "custom-views-dialog-opened",
                    "freex_view_panes_zoom_custom_views_dialog",
                    "Custom Views dialog opened with a saved tour view in the production list surface.",
                    "RenderTargetBitmap-custom-views-dialog-window"));
            }
            finally
            {
                customViewsDialog.Close();
            }

            ValidateViewPanesZoomTourEvidence(outputDir, captures);
            await WriteViewPanesZoomTourManifestAsync(outputDir, captures);
        }
        catch
        {
            DeleteViewPanesZoomTourEvidence(outputDir);
            throw;
        }
        finally
        {
            SetViewPanesZoomTourFormulaBarVisible(originalFormulaBarVisible);
        }
    }

    private Sheet EnsureViewPanesZoomTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("View panes/zoom tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        for (uint row = 1; row <= 24; row++)
        {
            for (uint col = 1; col <= 10; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (row == 1)
                    sheet.SetCell(address, new TextValue($"Metric {col}"));
                else if (col == 1)
                    sheet.SetCell(address, new TextValue($"Region {row - 1}"));
                else
                    sheet.SetCell(address, new NumberValue((row - 1) * 100 + col));
            }
        }

        _options.ShowFormulaBar = true;
        _suppressAppViewOptionSync = true;
        try
        {
            _ribbonState.SetChecked("Formula Bar", true);
        }
        finally
        {
            _suppressAppViewOptionSync = false;
        }

        FormulaBarBorder.Visibility = Visibility.Visible;
        SelectViewPanesZoomTourRange(sheet, new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 4)));
        SetWorksheetViewMode(WorksheetViewMode.Normal);
        SetViewPanesZoomTourShowToggles(showGridlines: true, showHeadings: true, showRulers: true);
        SetFreezePanes(0, 0);
        if (sheet.SplitRow is not null || sheet.SplitColumn is not null)
            SplitViewBtn_Click(this, new RoutedEventArgs());
        SyncZoomFromSheet(100);
        SelectViewRibbonTabForTour();
        UpdateViewport();
        return sheet;
    }

    private void SelectViewPanesZoomTourRange(Sheet sheet, GridRange range)
    {
        _currentSheetId = sheet.Id;
        SetActiveCell(range.Start);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = range;
            SheetGrid.SelectedRanges = null;
            SheetGrid.Focus();
        }

        var cell = sheet.GetCell(range.Start);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, range.Start));
        UpdateViewport();
        RefreshStatusBar();
    }

    private void SelectViewRibbonTabForTour()
    {
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "View"));
    }

    private void SetViewPanesZoomTourShowToggles(bool showGridlines, bool showHeadings, bool showRulers)
    {
        if (!TryExecuteGroupedSheetCommand(
                "View Show",
                sheetId => new SetWorksheetViewOptionsCommand(sheetId, showGridlines, showHeadings, showRulers)))
            return;

        UpdateViewport();
    }

    private void SetViewPanesZoomTourFormulaBarVisible(bool isVisible)
    {
        _options.ShowFormulaBar = isVisible;
        _suppressAppViewOptionSync = true;
        try
        {
            _ribbonState.SetChecked("Formula Bar", isVisible);
        }
        finally
        {
            _suppressAppViewOptionSync = false;
        }

        FormulaBarBorder.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task SetViewPanesZoomTourZoomAsync(int zoomPercent)
    {
        ZoomSlider.Value = StatusZoomSliderValueForPercent(zoomPercent);
        RefreshStatusBar();
        UpdateViewport();
        await Task.Delay(250);
    }

    private async Task<ViewPanesZoomTourManifestCapture> CaptureViewPanesZoomWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidencePurpose)
    {
        SelectViewRibbonTabForTour();
        SyncViewPanesZoomTourWorkbookViewButtons();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateViewPanesZoomTourCapture(state, fileName, evidencePurpose, "RenderTargetBitmap-window-full");
    }

    private void SyncViewPanesZoomTourWorkbookViewButtons()
    {
        var viewMode = _workbook.GetSheet(_currentSheetId)?.ViewMode ?? WorksheetViewMode.Normal;
        var state = WorksheetViewModeUiStatePlanner.Build(viewMode);
        _ribbonState.SetChecked("Normal", state.NormalChecked);
        _ribbonState.SetChecked("Page Layout", state.PageLayoutChecked);
        _ribbonState.SetChecked("Page Break Preview", state.PageBreakPreviewChecked);
    }

    private ViewPanesZoomTourManifestCapture CreateViewPanesZoomTourCapture(
        string state,
        string fileName,
        string evidencePurpose,
        string captureMethod)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return new ViewPanesZoomTourManifestCapture(
            CaptureKey: $"interactive:view-panes-zoom:{state}",
            PairKey: $"interactive:view-panes-zoom:{state}",
            ScenarioId: "view-panes-zoom:visual-evidence",
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            EvidencePurpose: evidencePurpose,
            CaptureLogicalWidth: captureMethod.Contains("window-full", StringComparison.Ordinal) ? ActualWidth : 0,
            CaptureLogicalHeight: captureMethod.Contains("window-full", StringComparison.Ordinal) ? Math.Min(ActualHeight, 760) : 0,
            SheetName: sheet?.Name ?? string.Empty,
            ActiveRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            ViewMode: (sheet?.ViewMode ?? WorksheetViewMode.Normal).ToString(),
            ShowGridlines: sheet?.ShowGridlines ?? true,
            ShowHeadings: sheet?.ShowHeadings ?? true,
            ShowRulers: sheet?.ShowRulers ?? true,
            FormulaBarVisible: FormulaBarBorder.Visibility == Visibility.Visible,
            FrozenRows: sheet?.FrozenRows ?? 0,
            FrozenCols: sheet?.FrozenCols ?? 0,
            SplitRow: sheet?.SplitRow,
            SplitColumn: sheet?.SplitColumn,
            ZoomText: StatusZoomText.Text,
            ZoomSliderValue: ZoomSlider.Value,
            WindowArrangement: _workbook.WindowArrangement.ToString(),
            CustomViewCount: _workbook.CustomViews.Count,
            ViewNormalChecked: IsRibbonCommandChecked("Normal"),
            ViewPageLayoutChecked: IsRibbonCommandChecked("Page Layout"),
            ViewPageBreakPreviewChecked: IsRibbonCommandChecked("Page Break Preview"),
            ViewGridlinesChecked: IsRibbonCommandChecked("Gridlines"),
            ViewHeadingsChecked: IsRibbonCommandChecked("Headings"),
            ViewRulerChecked: IsRibbonCommandChecked("Ruler"),
            ViewFormulaBarChecked: IsRibbonCommandChecked("Formula Bar"),
            SplitButtonChecked: IsRibbonCommandChecked("Split"));
    }

    private static void DeleteViewPanesZoomTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_view_panes_zoom_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, ViewPanesZoomTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateViewPanesZoomTourEvidence(string outputDir, IReadOnlyList<ViewPanesZoomTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"View panes/zoom tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private async Task CapturePageLayoutSetupTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeletePageLayoutSetupTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1240;
        Height = 768;
        await Task.Delay(700);

        var sheet = EnsurePageLayoutSetupTourContext();
        var captures = new List<PageLayoutSetupTourManifestCapture>();

        try
        {
            captures.Add(await CapturePageLayoutSetupWindowStateAsync(
                outputDir,
                "ribbon-baseline",
                "freex_page_layout_setup_ribbon_baseline",
                "Page Layout tab shows Themes, Page Setup, Scale to Fit, and Sheet Options groups with a seeded print area and print-title state."));

            captures.Add(await CapturePageLayoutSetupMenuAsync(
                outputDir,
                "margins-menu-opened",
                "freex_page_layout_setup_margins_menu_opened",
                "Margins",
                "Margins menu exposes Normal, Wide, Narrow, and Custom Margins choices."));

            captures.Add(await CapturePageLayoutSetupMenuAsync(
                outputDir,
                "orientation-menu-opened",
                "freex_page_layout_setup_orientation_menu_opened",
                "Page Orientation",
                "Orientation menu exposes Portrait and Landscape choices."));

            captures.Add(await CapturePageLayoutSetupMenuAsync(
                outputDir,
                "size-menu-opened",
                "freex_page_layout_setup_size_menu_opened",
                "Paper Size",
                "Size menu exposes implemented paper-size choices plus dialog-backed larger paper entries."));

            captures.Add(await CapturePageLayoutSetupMenuAsync(
                outputDir,
                "print-area-menu-opened",
                "freex_page_layout_setup_print_area_menu_opened",
                "Print Area",
                "Print Area menu exposes Set and Clear choices against the selected range."));

            captures.Add(await CapturePageLayoutSetupMenuAsync(
                outputDir,
                "breaks-menu-opened",
                "freex_page_layout_setup_breaks_menu_opened",
                "Breaks",
                "Breaks menu exposes Insert Page Break, Remove Page Break, and Reset All Page Breaks."));

            captures.Add(await CapturePageLayoutSetupMenuAsync(
                outputDir,
                "background-menu-opened",
                "freex_page_layout_setup_background_menu_opened",
                "Background",
                "Background menu exposes Choose Background and Delete Background without opening the native file picker."));

            var pageSetupDialog = new PageSetupDialog(sheet, SheetGrid.SelectedRange, null, PageSetupInitialFocusTarget.PageOrientation)
            {
                Owner = this
            };
            try
            {
                pageSetupDialog.Show();
                await Task.Delay(350);
                pageSetupDialog.UpdateLayout();
                await CaptureWindowElementForScreenshotTourAsync(pageSetupDialog, outputDir, "freex_page_layout_setup_dialog");
                captures.Add(CreatePageLayoutSetupCapture(
                    "page-setup-dialog",
                    "freex_page_layout_setup_dialog",
                    "Page Setup dialog default Page tab shows the initial orientation-focused state used by the Page Setup command.",
                    "Page Setup",
                    "RenderTargetBitmap-page-setup-dialog-window",
                    []));

                await CaptureWindowElementForScreenshotTourAsync(pageSetupDialog, outputDir, "freex_page_layout_setup_dialog_page_tab");
                captures.Add(CreatePageLayoutSetupCapture(
                    "page-setup-dialog-page-tab",
                    "freex_page_layout_setup_dialog_page_tab",
                    "Page Setup dialog Page tab shows orientation, paper size, scaling, first-page number, print quality, and Print/Preview/Options/OK/Cancel buttons.",
                    "Page Setup",
                    "RenderTargetBitmap-page-setup-dialog-window",
                    []));

                pageSetupDialog.PageSetupTabs.SelectedItem = pageSetupDialog.MarginsTab;
                await Task.Delay(250);
                pageSetupDialog.UpdateLayout();
                await CaptureWindowElementForScreenshotTourAsync(pageSetupDialog, outputDir, "freex_page_layout_setup_dialog_margins_tab");
                captures.Add(CreatePageLayoutSetupCapture(
                    "page-setup-dialog-margins-tab",
                    "freex_page_layout_setup_dialog_margins_tab",
                    "Page Setup dialog Margins tab shows left/right/top/bottom, header/footer margins, and center-on-page options.",
                    "Page Setup",
                    "RenderTargetBitmap-page-setup-dialog-window",
                    []));

                pageSetupDialog.PageSetupTabs.SelectedIndex = 2;
                await Task.Delay(250);
                pageSetupDialog.UpdateLayout();
                await CaptureWindowElementForScreenshotTourAsync(pageSetupDialog, outputDir, "freex_page_layout_setup_dialog_header_footer_tab");
                captures.Add(CreatePageLayoutSetupCapture(
                    "page-setup-dialog-header-footer-tab",
                    "freex_page_layout_setup_dialog_header_footer_tab",
                    "Page Setup dialog Header/Footer tab shows header/footer presets, preview panes, and scale/alignment options.",
                    "Page Setup",
                    "RenderTargetBitmap-page-setup-dialog-window",
                    []));

                pageSetupDialog.PageSetupTabs.SelectedItem = pageSetupDialog.SheetTab;
                await Task.Delay(250);
                pageSetupDialog.UpdateLayout();
                await CaptureWindowElementForScreenshotTourAsync(pageSetupDialog, outputDir, "freex_page_layout_setup_dialog_sheet_tab_print_titles");
                captures.Add(CreatePageLayoutSetupCapture(
                    "page-setup-dialog-sheet-tab-print-titles",
                    "freex_page_layout_setup_dialog_sheet_tab_print_titles",
                    "Page Setup dialog Sheet tab captures Print Titles fields, print area, print gridlines/headings, page order, comments, and error display options.",
                    "Page Setup",
                    "RenderTargetBitmap-page-setup-dialog-window",
                    []));
            }
            finally
            {
                pageSetupDialog.Close();
            }

            ApplyPageLayoutScaleToFit(new WorksheetScaleToFit(null, 1, 2));
            if (FindRenderedRibbonControl("Scale Width") is ComboBox tourScaleWidthBox) tourScaleWidthBox.Text = "1 page";
            if (FindRenderedRibbonControl("Scale Height") is ComboBox tourScaleHeightBox) tourScaleHeightBox.Text = "2 pages";
            if (FindRenderedRibbonControl("Scale Percent") is ComboBox tourScalePercentBox) tourScalePercentBox.Text = "85%";
            captures.Add(await CapturePageLayoutSetupWindowStateAsync(
                outputDir,
                "scale-to-fit-state",
                "freex_page_layout_setup_scale_to_fit_state",
                "Scale to Fit ribbon fields show fit-to-pages width/height and percent controls after applying the production scale command."));

            _ribbonState.SetChecked("View Gridlines", false);
            _ribbonState.SetChecked("View Headings", false);
            _ribbonState.SetChecked("Print Gridlines", true);
            _ribbonState.SetChecked("Print Headings", true);
            sheet.ShowGridlines = false;
            sheet.ShowHeadings = false;
            sheet.PrintGridlines = true;
            sheet.PrintHeadings = true;
            UpdateViewport();
            captures.Add(await CapturePageLayoutSetupWindowStateAsync(
                outputDir,
                "sheet-options-toggled",
                "freex_page_layout_setup_sheet_options_toggled",
                "Sheet Options shows display gridlines/headings off and print gridlines/headings on for the active sheet."));

            var selectionPaneDialog = new SelectionPaneDialog(CreatePageLayoutSetupSelectionPaneItems())
            {
                Owner = this
            };
            try
            {
                selectionPaneDialog.Show();
                await Task.Delay(350);
                selectionPaneDialog.UpdateLayout();
                await CaptureWindowElementForScreenshotTourAsync(selectionPaneDialog, outputDir, "freex_page_layout_setup_arrange_selection_pane_dialog");
                captures.Add(CreatePageLayoutSetupCapture(
                    "arrange-selection-pane-dialog",
                    "freex_page_layout_setup_arrange_selection_pane_dialog",
                    "Arrange group representative Selection Pane dialog shows object list, search/filter, visibility, rename, and move controls.",
                    "Selection Pane",
                    "RenderTargetBitmap-selection-pane-dialog-window",
                    []));
            }
            finally
            {
                selectionPaneDialog.Close();
            }

            ValidatePageLayoutSetupTourEvidence(outputDir, captures);
            await WritePageLayoutSetupTourManifestAsync(outputDir, captures);
        }
        catch
        {
            DeletePageLayoutSetupTourEvidence(outputDir);
            throw;
        }
    }

    private Sheet EnsurePageLayoutSetupTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Page Layout/Page Setup tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        for (uint row = 1; row <= 28; row++)
        {
            for (uint col = 1; col <= 8; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (row == 1)
                    sheet.SetCell(address, new TextValue($"Print Field {col}"));
                else if (col == 1)
                    sheet.SetCell(address, new TextValue($"Page Row {row - 1}"));
                else
                    sheet.SetCell(address, new NumberValue(row * 10 + col));
            }
        }

        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 18, 6));
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.RowPageBreaks.Clear();
        sheet.RowPageBreaks.Add(12);
        sheet.ColumnPageBreaks.Clear();
        sheet.ColumnPageBreaks.Add(5);
        sheet.ScaleToFit = new WorksheetScaleToFit(90, null, null);
        sheet.PrintGridlines = false;
        sheet.PrintHeadings = false;
        sheet.ShowGridlines = true;
        sheet.ShowHeadings = true;
        sheet.CenterHorizontallyOnPage = true;
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.PageHeader = new WorksheetHeaderFooter("", "Page Layout Tour", "");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &[Page] of &[Pages]", "");

        SelectViewPanesZoomTourRange(sheet, sheet.PrintArea.Value);
        SetWorksheetViewMode(WorksheetViewMode.PageLayout);
        SelectPageLayoutRibbonTabForTour();
        SyncPageLayoutSetupTourControls(sheet);
        UpdateViewport();
        RefreshStatusBar();
        return sheet;
    }

    private void SelectPageLayoutRibbonTabForTour()
    {
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Page Layout"));
    }

    private void SyncPageLayoutSetupTourControls(Sheet sheet)
    {
        _suppressToolbarSync = true;
        try
        {
            if (FindRenderedRibbonControl("Scale Width") is ComboBox syncScaleWidthBox)
                syncScaleWidthBox.Text = sheet.ScaleToFit.FitToPagesWide is { } wide ? $"{wide} page" : "Automatic";
            if (FindRenderedRibbonControl("Scale Height") is ComboBox syncScaleHeightBox)
                syncScaleHeightBox.Text = sheet.ScaleToFit.FitToPagesTall is { } tall ? $"{tall} page" : "Automatic";
            if (FindRenderedRibbonControl("Scale Percent") is ComboBox syncScalePercentBox)
                syncScalePercentBox.Text = $"{sheet.ScaleToFit.ScalePercent ?? 100}%";
            _ribbonState.SetChecked("View Gridlines", sheet.ShowGridlines);
            _ribbonState.SetChecked("View Headings", sheet.ShowHeadings);
            _ribbonState.SetChecked("Print Gridlines", sheet.PrintGridlines);
            _ribbonState.SetChecked("Print Headings", sheet.PrintHeadings);
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    private async Task<PageLayoutSetupTourManifestCapture> CapturePageLayoutSetupWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidencePurpose)
    {
        SelectPageLayoutRibbonTabForTour();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 768);
        return CreatePageLayoutSetupCapture(state, fileName, evidencePurpose, "Page Layout ribbon", "RenderTargetBitmap-window-full", []);
    }

    private async Task<PageLayoutSetupTourManifestCapture> CapturePageLayoutSetupMenuAsync(
        string outputDir,
        string state,
        string fileName,
        string commandName,
        string evidencePurpose)
    {
        SelectPageLayoutRibbonTabForTour();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName)
            ?? throw new InvalidOperationException($"Page Layout/Page Setup tour could not find '{commandName}' ribbon button.");
        var menu = button.ContextMenu
            ?? throw new InvalidOperationException($"Page Layout/Page Setup tour could not find '{commandName}' context menu.");

        OpenRibbonContextMenu(button, menu);
        await Task.Delay(350);
        menu.UpdateLayout();
        await CaptureElementAsync(menu, outputDir, fileName);
        var headers = new List<string>();
        AddMenuHeaders(menu, headers);
        menu.IsOpen = false;
        return CreatePageLayoutSetupCapture(state, fileName, evidencePurpose, commandName, "RenderTargetBitmap-page-layout-context-menu", headers);
    }

    private PageLayoutSetupTourManifestCapture CreatePageLayoutSetupCapture(
        string state,
        string fileName,
        string evidencePurpose,
        string surface,
        string captureMethod,
        IReadOnlyList<string> menuHeaders)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return new PageLayoutSetupTourManifestCapture(
            CaptureKey: $"interactive:page-layout-setup:{state}",
            PairKey: $"interactive:page-layout-setup:{state}",
            ScenarioId: "page-layout-setup:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureMethod.Contains("window-full", StringComparison.Ordinal) ? ActualWidth : 0,
            CaptureLogicalHeight: captureMethod.Contains("window-full", StringComparison.Ordinal) ? Math.Min(ActualHeight, 768) : 0,
            SheetName: sheet?.Name ?? string.Empty,
            ActiveRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            ViewMode: (sheet?.ViewMode ?? WorksheetViewMode.Normal).ToString(),
            PageOrientation: (sheet?.PageOrientation ?? WorksheetPageOrientation.Portrait).ToString(),
            PaperSize: (sheet?.PaperSize ?? WorksheetPaperSize.A4).ToString(),
            PrintArea: sheet?.PrintArea?.ToString() ?? string.Empty,
            PrintTitleRows: sheet?.PrintTitleRows?.ToString() ?? string.Empty,
            PrintTitleColumns: sheet?.PrintTitleColumns?.ToString() ?? string.Empty,
            RowPageBreaks: sheet?.RowPageBreaks.ToArray() ?? [],
            ColumnPageBreaks: sheet?.ColumnPageBreaks.ToArray() ?? [],
            ScaleToFit: sheet?.ScaleToFit.ToString() ?? WorksheetScaleToFit.Default.ToString(),
            ShowGridlines: sheet?.ShowGridlines ?? true,
            ShowHeadings: sheet?.ShowHeadings ?? true,
            PrintGridlines: sheet?.PrintGridlines ?? false,
            PrintHeadings: sheet?.PrintHeadings ?? false,
            ScaleWidthText: (FindRenderedRibbonControl("Scale Width") as ComboBox)?.Text ?? string.Empty,
            ScaleHeightText: (FindRenderedRibbonControl("Scale Height") as ComboBox)?.Text ?? string.Empty,
            ScalePercentText: (FindRenderedRibbonControl("Scale Percent") as ComboBox)?.Text ?? string.Empty,
            MenuHeaders: menuHeaders,
            EvidencePurpose: evidencePurpose);
    }

    private static IReadOnlyList<SelectionPaneItem> CreatePageLayoutSetupSelectionPaneItems() =>
    [
        new(SelectionPaneObjectKind.Shape, Guid.Parse("11111111-1111-1111-1111-111111111111"), "Rectangle 1", true, false, true),
        new(SelectionPaneObjectKind.TextBox, Guid.Parse("22222222-2222-2222-2222-222222222222"), "Text Box 1", true, true, true),
        new(SelectionPaneObjectKind.Picture, Guid.Parse("33333333-3333-3333-3333-333333333333"), "Picture 1", false, true, false)
    ];

    private static void DeletePageLayoutSetupTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_page_layout_setup_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, PageLayoutSetupTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidatePageLayoutSetupTourEvidence(
        string outputDir,
        IReadOnlyList<PageLayoutSetupTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Page Layout/Page Setup tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task CaptureDrawObjectFormattingTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteDrawObjectFormattingTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureDrawObjectFormattingTourContext();
        var captures = new List<DrawObjectFormattingTourManifestCapture>();

        try
        {
            captures.Add(await CaptureDrawObjectFormattingWindowStateAsync(
                outputDir,
                "draw-tab-baseline-seeded-objects",
                "freex_draw_object_formatting_draw_tab_baseline",
                "Draw tab baseline shows Arrange and Format command groups with seeded shape, picture, and text-box objects visible on the worksheet.",
                ["UI-CAT-DRAW-001", "UI-CMD-DRAW-001", "UI-CMD-DRAW-002", "UI-CMD-DRAW-003"]));

            captures.Add(await CaptureDrawObjectFormattingDialogAsync(
                outputDir,
                new ColorPickerDialog(context.Shape.FillColor, allowNoColor: true, UiText.Get("FormatCells_NoFill"))
                {
                    Owner = this,
                    Title = UiText.Get("MainWindowMessage_ObjectFillTitle")
                },
                "shape-fill-color-picker",
                "freex_draw_object_formatting_shape_fill_color_picker",
                "Shape Fill color picker opens on the standard/theme/custom palette surface and offers No Fill for the selected shape.",
                "Shape Fill",
                "RenderTargetBitmap-color-picker-dialog-window",
                ["UI-CMD-DRAW-003"]));

            captures.Add(await CaptureDrawObjectFormattingDialogAsync(
                outputDir,
                new ColorPickerDialog(context.Shape.OutlineColor, allowNoColor: false)
                {
                    Owner = this,
                    Title = UiText.Get("MainWindowMessage_ObjectOutlineTitle")
                },
                "object-outline-color-picker",
                "freex_draw_object_formatting_object_outline_color_picker",
                "Object Outline color picker opens against the selected drawing object's current outline color.",
                "Object Outline",
                "RenderTargetBitmap-color-picker-dialog-window",
                ["UI-CMD-DRAW-003"]));

            captures.Add(await CaptureDrawObjectFormattingDialogAsync(
                outputDir,
                new ShapeGradientDialog(
                    context.Shape.FillThemeColor?.Resolve(_workbook.Theme)
                        ?? context.Shape.FillColor
                        ?? DrawingShapeModel.ResolveDefaultFillColor(_workbook.Theme),
                    context.Shape.GradientFillEndColor ?? ShapeGradientPlanner.DefaultEndColor,
                    context.Shape.GetEffectiveGradientFillDirection())
                {
                    Owner = this
                },
                "shape-gradient-dialog",
                "freex_draw_object_formatting_shape_gradient_dialog",
                "Shape Gradient dialog shows start/end RGB stop inputs, color buttons, direction choices, and OK/Cancel.",
                "Shape Gradient",
                "RenderTargetBitmap-shape-gradient-dialog-window",
                ["UI-CMD-DRAW-004"]));

            captures.Add(await CaptureDrawObjectFormattingDialogAsync(
                outputDir,
                new ShapeEffectsDialog(context.Shape.GetEffectiveEffectPreset()) { Owner = this },
                "shape-effects-dialog",
                "freex_draw_object_formatting_shape_effects_dialog",
                "Shape Effects dialog shows the current effect preset selector and description text.",
                "Shape Effects",
                "RenderTargetBitmap-shape-effects-dialog-window",
                ["UI-CMD-DRAW-004"]));

            SelectDrawObjectFormattingPicture(context);
            captures.Add(await CaptureDrawObjectFormattingCropMenuAsync(outputDir));

            SelectDrawObjectFormattingShape(context);
            captures.Add(await CaptureDrawObjectFormattingDialogAsync(
                outputDir,
                new ObjectSizeDialog(context.Shape.Width, context.Shape.Height, UiText.Get("MainWindowMessage_ObjectSizeTitle")) { Owner = this },
                "object-size-dialog",
                "freex_draw_object_formatting_object_size_dialog",
                "Object Size dialog opens with the height box focused/select-all and lock-aspect-ratio visible for the selected shape.",
                "Object Size",
                "RenderTargetBitmap-object-size-dialog-window",
                ["UI-CMD-DRAW-003"]));

            foreach (var capture in await CaptureDrawObjectFormattingFormatPictureDialogAsync(outputDir, context.Picture))
                captures.Add(capture);

            captures.Add(await CaptureDrawObjectFormattingSelectionPaneAsync(outputDir, context));

            ValidateDrawObjectFormattingTourEvidence(outputDir, captures);
            await WriteDrawObjectFormattingTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteDrawObjectFormattingTourEvidence(outputDir);
            throw;
        }
    }

    private DrawObjectFormattingTourContext EnsureDrawObjectFormattingTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Draw/object formatting tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        for (uint row = 1; row <= 16; row++)
        {
            for (uint col = 1; col <= 8; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (row == 1)
                    sheet.SetCell(address, new TextValue($"Draw Field {col}"));
                else if (col == 1)
                    sheet.SetCell(address, new TextValue($"Object Row {row - 1}"));
                else
                    sheet.SetCell(address, new NumberValue(row * 100 + col));
            }
        }

        sheet.DrawingShapes.Clear();
        sheet.Pictures.Clear();
        sheet.TextBoxes.Clear();
        sheet.DrawingObjectZOrder.Clear();

        var shape = new DrawingShapeModel
        {
            Id = Guid.Parse("aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa"),
            Anchor = new CellAddress(sheet.Id, 3, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 168,
            Height = 88,
            Name = "Tour Process Shape",
            Title = "Draw tour process shape",
            AltText = "Rounded process shape for Draw object formatting evidence.",
            FillColor = new CellColor(47, 117, 181),
            OutlineColor = new CellColor(31, 78, 121),
            GradientFillEndColor = new CellColor(189, 215, 238),
            GradientFillDirection = DrawingShapeGradientDirection.Horizontal,
            EffectPreset = DrawingShapeEffectPreset.Shadow,
            RotationDegrees = 4
        };
        sheet.DrawingShapes.Add(shape);

        var picture = new PictureModel
        {
            Id = Guid.Parse("bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb"),
            Anchor = new CellAddress(sheet.Id, 5, 5),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3, 4],
            ContentType = "image/png",
            Name = "Tour Picture Logo",
            AltText = "Picture placeholder used for Draw formatting and alt text evidence.",
            Width = 176,
            Height = 96,
            RotationDegrees = 8,
            CropLeft = 0.04,
            CropTop = 0.02,
            CropRight = 0.08,
            CropBottom = 0.03
        };
        sheet.Pictures.Add(picture);

        var textBox = new FreeX.Core.Model.TextBoxModel
        {
            Id = Guid.Parse("cccccccc-3333-4333-8333-cccccccccccc"),
            Anchor = new CellAddress(sheet.Id, 8, 3),
            Text = "Text box evidence",
            Width = 210,
            Height = 76,
            Name = "Tour Text Box",
            AltText = "Text box object for Draw formatting evidence.",
            FillColor = new CellColor(255, 242, 204),
            OutlineColor = new CellColor(191, 143, 0),
            RotationDegrees = 0
        };
        sheet.TextBoxes.Add(textBox);

        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id));

        SelectDrawObjectFormattingShape(new DrawObjectFormattingTourContext(sheet, shape, picture, textBox));
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Draw"));
        UpdateViewport();
        RefreshToolbar();
        return new DrawObjectFormattingTourContext(sheet, shape, picture, textBox);
    }

    private void SelectDrawObjectFormattingShape(DrawObjectFormattingTourContext context) =>
        SelectDrawObjectFormattingObject(context.Shape.Anchor, context.Shape.Id, FreeX.App.UI.ObjectKind.Shape);

    private void SelectDrawObjectFormattingPicture(DrawObjectFormattingTourContext context) =>
        SelectDrawObjectFormattingObject(context.Picture.Anchor, context.Picture.Id, FreeX.App.UI.ObjectKind.Picture);

    private void SelectDrawObjectFormattingObject(CellAddress anchor, Guid objectId, FreeX.App.UI.ObjectKind kind)
    {
        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        if (SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(anchor, anchor);
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedObjectId = objectId;
            SheetGrid.SelectedObjectKind = kind;
        }
    }

    private async Task<DrawObjectFormattingTourManifestCapture> CaptureDrawObjectFormattingWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidencePurpose,
        IReadOnlyList<string> commandRows)
    {
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Draw"));
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateDrawObjectFormattingCapture(
            state,
            fileName,
            evidencePurpose,
            "Draw ribbon",
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            commandRows,
            []);
    }

    private async Task<DrawObjectFormattingTourManifestCapture> CaptureDrawObjectFormattingDialogAsync(
        string outputDir,
        Window dialog,
        string state,
        string fileName,
        string evidencePurpose,
        string surface,
        string captureMethod,
        IReadOnlyList<string> commandRows,
        Action<Window>? configureAfterShow = null)
    {
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            configureAfterShow?.Invoke(dialog);
            dialog.UpdateLayout();
            await Task.Delay(150);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
            return CreateDrawObjectFormattingCapture(
                state,
                fileName,
                evidencePurpose,
                surface,
                captureMethod,
                dialog.ActualWidth,
                dialog.ActualHeight,
                commandRows,
                []);
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<DrawObjectFormattingTourManifestCapture> CaptureDrawObjectFormattingCropMenuAsync(string outputDir)
    {
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Draw"));
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, "Crop Picture")
            ?? throw new InvalidOperationException("Draw/object formatting tour could not find the Crop Picture ribbon button.");
        var menu = button.ContextMenu
            ?? throw new InvalidOperationException("Draw/object formatting tour could not find the Crop Picture context menu.");

        OpenRibbonContextMenu(button, menu);
        await Task.Delay(350);
        menu.UpdateLayout();
        await CaptureElementAsync(menu, outputDir, "freex_draw_object_formatting_crop_menu_opened");
        var headers = new List<string>();
        AddMenuHeaders(menu, headers);
        menu.IsOpen = false;
        return CreateDrawObjectFormattingCapture(
            "crop-reset-crop-menu-opened",
            "freex_draw_object_formatting_crop_menu_opened",
            "Crop Picture split menu exposes Crop and Reset Crop commands for the selected picture object.",
            "Crop Picture menu",
            "RenderTargetBitmap-draw-crop-context-menu",
            0,
            0,
            ["UI-CMD-DRAW-004"],
            headers);
    }

    private async Task<IReadOnlyList<DrawObjectFormattingTourManifestCapture>> CaptureDrawObjectFormattingFormatPictureDialogAsync(
        string outputDir,
        PictureModel picture)
    {
        var dialog = new FormatPictureDialog(picture) { Owner = this };
        var captures = new List<DrawObjectFormattingTourManifestCapture>();
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_draw_object_formatting_picture_size_tab");
            captures.Add(CreateDrawObjectFormattingCapture(
                "format-picture-size-tab",
                "freex_draw_object_formatting_picture_size_tab",
                "Format Picture dialog Size tab shows width, height, rotation, lock aspect ratio, tabs, and OK/Cancel for the selected picture.",
                "Format Picture",
                "RenderTargetBitmap-format-picture-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                ["UI-CMD-DRAW-003", "UI-CMD-DRAW-004"],
                []));

            var tabs = FindDescendant<TabControl>(dialog)
                ?? throw new InvalidOperationException("Draw/object formatting tour could not find the Format Picture tab control.");
            foreach (var item in tabs.Items.OfType<TabItem>())
            {
                if (string.Equals(item.Header?.ToString(), UiText.Get("FormatPicture_AltTextTab"), StringComparison.Ordinal))
                {
                    tabs.SelectedItem = item;
                    break;
                }
            }

            await Task.Delay(250);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_draw_object_formatting_picture_alt_text_tab");
            captures.Add(CreateDrawObjectFormattingCapture(
                "format-picture-alt-text-tab",
                "freex_draw_object_formatting_picture_alt_text_tab",
                "Format Picture dialog Alt Text tab shows the seeded picture description field and dialog action buttons.",
                "Format Picture",
                "RenderTargetBitmap-format-picture-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                ["UI-CMD-DRAW-003"],
                []));
        }
        finally
        {
            dialog.Close();
        }

        return captures;
    }

    private async Task<DrawObjectFormattingTourManifestCapture> CaptureDrawObjectFormattingSelectionPaneAsync(
        string outputDir,
        DrawObjectFormattingTourContext context)
    {
        var dialog = new SelectionPaneDialog(SelectionPaneDialog.BuildItems(context.Sheet)) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            var searchBox = FindDescendantByAutomationId<TextBox>(dialog, "SelectionPaneSearchBox")
                ?? throw new InvalidOperationException("Draw/object formatting tour could not find the Selection Pane search box.");
            var renameBox = FindDescendantByAutomationId<TextBox>(dialog, "SelectionPaneRenameBox")
                ?? throw new InvalidOperationException("Draw/object formatting tour could not find the Selection Pane rename box.");
            var renameButton = FindDescendantByAutomationId<Button>(dialog, "SelectionPaneRenameButton")
                ?? throw new InvalidOperationException("Draw/object formatting tour could not find the Selection Pane rename button.");
            var toggleButton = FindDescendantByAutomationId<Button>(dialog, "SelectionPaneToggleVisibilityButton")
                ?? throw new InvalidOperationException("Draw/object formatting tour could not find the Selection Pane visibility button.");

            searchBox.Text = "Tour";
            renameBox.Text = "Tour Shape Renamed";
            renameButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            toggleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            dialog.UpdateLayout();
            await Task.Delay(250);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_draw_object_formatting_selection_pane_rename_visibility");
            return CreateDrawObjectFormattingCapture(
                "selection-pane-rename-visibility",
                "freex_draw_object_formatting_selection_pane_rename_visibility",
                "Selection Pane dialog shows search/list state, a renamed object preview, visibility toggle state, show/hide all, bring/send, OK, and Cancel.",
                "Selection Pane",
                "RenderTargetBitmap-selection-pane-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                ["UI-CMD-DRAW-002", "UI-CMD-DRAW-005"],
                []);
        }
        finally
        {
            dialog.Close();
        }
    }

    private DrawObjectFormattingTourManifestCapture CreateDrawObjectFormattingCapture(
        string state,
        string fileName,
        string evidencePurpose,
        string surface,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        IReadOnlyList<string> commandRows,
        IReadOnlyList<string> menuHeaders)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return new DrawObjectFormattingTourManifestCapture(
            CaptureKey: $"interactive:draw-object-formatting:{state}",
            PairKey: $"interactive:draw-object-formatting:{state}",
            ScenarioId: "draw-object-formatting:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SheetName: sheet?.Name ?? string.Empty,
            ActiveRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            SelectedObjectKind: SheetGrid?.SelectedObjectKind.ToString() ?? string.Empty,
            SelectedObjectName: GetDrawObjectFormattingSelectedObjectName(sheet),
            ShapeCount: sheet?.DrawingShapes.Count ?? 0,
            PictureCount: sheet?.Pictures.Count ?? 0,
            TextBoxCount: sheet?.TextBoxes.Count ?? 0,
            DrawingZOrder: sheet?.DrawingObjectZOrder.Select(entry => $"{entry.Kind}:{entry.Id:N}").ToArray() ?? [],
            CommandRows: commandRows,
            MenuHeaders: menuHeaders,
            EvidencePurpose: evidencePurpose);
    }

    private string GetDrawObjectFormattingSelectedObjectName(Sheet? sheet)
    {
        if (sheet is null || SheetGrid is null || SheetGrid.SelectedObjectId == Guid.Empty)
            return string.Empty;

        var id = SheetGrid.SelectedObjectId;
        return SheetGrid.SelectedObjectKind switch
        {
            FreeX.App.UI.ObjectKind.Picture => sheet.Pictures.FirstOrDefault(picture => picture.Id == id)?.Name ?? string.Empty,
            FreeX.App.UI.ObjectKind.Shape => sheet.DrawingShapes.FirstOrDefault(shape => shape.Id == id)?.Name ?? string.Empty,
            FreeX.App.UI.ObjectKind.TextBox => sheet.TextBoxes.FirstOrDefault(textBox => textBox.Id == id)?.Name ?? string.Empty,
            _ => string.Empty
        };
    }

    private static void DeleteDrawObjectFormattingTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_draw_object_formatting_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, DrawObjectFormattingTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateDrawObjectFormattingTourEvidence(
        string outputDir,
        IReadOnlyList<DrawObjectFormattingTourManifestCapture> captures)
    {
        if (captures.Count != 10)
            throw new InvalidOperationException($"Draw/object formatting tour expected 10 captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Draw/object formatting tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private static T? FindDescendantByRibbonCommandName<T>(DependencyObject root, string commandName)
        where T : DependencyObject
    {
        if (root is T typed &&
            RibbonMetadata.TryGetCommandName(typed, out var candidate) &&
            string.Equals(candidate, commandName, StringComparison.Ordinal))
            return typed;

        var visualCount = root is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetChildrenCount(root)
            : 0;
        for (var index = 0; index < visualCount; index++)
        {
            var match = FindDescendantByRibbonCommandName<T>(VisualTreeHelper.GetChild(root, index), commandName);
            if (match is not null)
                return match;
        }

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            var match = FindDescendantByRibbonCommandName<T>(logicalChild, commandName);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static void ValidateFormulaDiagnosticsTourEvidence(
        string outputDir,
        IReadOnlyList<FormulaDiagnosticsTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Formula diagnostics tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task CaptureFormulaAuthoringNamesTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteFormulaAuthoringNamesTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureFormulaAuthoringNamesTourContext();
        var captures = new List<FormulaAuthoringNamesTourManifestCapture>();
        InsertFunctionDialog? insertFunctionDialog = null;
        NamedRangeDialog? nameManagerDialog = null;
        NameDefinitionDialog? defineNameDialog = null;
        CreateNamesFromSelectionDialog? createFromSelectionDialog = null;
        ContextMenu? openMenu = null;

        try
        {
            SelectFormulaAuthoringNamesRibbonTabForTour();
            SetSelectionRange(context.AuthoringRange, context.AuthoringRange.Start);
            UpdateViewport();
            RefreshToolbar();
            RefreshStatusBar();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(350);

            captures.Add(await CaptureFormulaAuthoringNamesWindowStateAsync(
                outputDir,
                "formulas-tab-seeded",
                "freex_formula_authoring_names_formulas_tab",
                "Formulas ribbon baseline over seeded revenue/cost/profit cells with workbook names already defined."));

            captures.Add(await CaptureFormulaAuthoringNamesMenuAsync(
                outputDir,
                "autosum-menu-opened",
                "freex_formula_authoring_names_autosum_menu_opened",
                "AutoSum",
                "Formulas tab AutoSum split-menu showing Sum, Average, Count, Max/Min, and More Functions."));

            captures.Add(await CaptureFormulaAuthoringNamesFunctionMenuAsync(
                outputDir,
                "logical-functions-menu-opened",
                "freex_formula_authoring_names_logical_functions_menu_opened",
                "Logical Functions",
                FormulaLogicalBtn_Click,
                "Logical Functions category menu opened from the Formulas Function Library group."));

            captures.Add(await CaptureFormulaAuthoringNamesFunctionMenuAsync(
                outputDir,
                "use-in-formula-menu-opened",
                "freex_formula_authoring_names_use_in_formula_menu_opened",
                "Use in Formula",
                UseInFormulaBtn_Click,
                "Use in Formula menu listing seeded workbook defined names that can be inserted into the active formula."));

            insertFunctionDialog = new InsertFunctionDialog
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            insertFunctionDialog.Show();
            insertFunctionDialog.Activate();
            insertFunctionDialog.UpdateLayout();
            await ConfigureInsertFunctionDialogForFormulaAuthoringTourAsync(insertFunctionDialog);
            await CaptureWindowElementForScreenshotTourAsync(insertFunctionDialog, outputDir, "freex_formula_authoring_names_insert_function_lookup_xlookup");
            captures.Add(CreateFormulaAuthoringNamesCapture(
                "insert-function-lookup-xlookup",
                "freex_formula_authoring_names_insert_function_lookup_xlookup",
                "Insert Function dialog",
                "RenderTargetBitmap-insert-function-dialog",
                insertFunctionDialog.ActualWidth,
                insertFunctionDialog.ActualHeight,
                "Production Insert Function dialog with Lookup & Reference selected and XLOOKUP highlighted."));
            insertFunctionDialog.Close();
            insertFunctionDialog = null;

            nameManagerDialog = new NamedRangeDialog(_workbook, _commandBus, context.AuthoringRange)
            {
                Owner = this
            };
            nameManagerDialog.Show();
            nameManagerDialog.Activate();
            nameManagerDialog.UpdateLayout();
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(nameManagerDialog, outputDir, "freex_formula_authoring_names_name_manager_dialog");
            captures.Add(CreateFormulaAuthoringNamesCapture(
                "name-manager-dialog",
                "freex_formula_authoring_names_name_manager_dialog",
                "Name Manager dialog",
                "RenderTargetBitmap-name-manager-dialog",
                nameManagerDialog.ActualWidth,
                nameManagerDialog.ActualHeight,
                "Production Name Manager dialog showing seeded Revenue, Cost, Profit, and RegionLabels names."));
            nameManagerDialog.Close();
            nameManagerDialog = null;

            defineNameDialog = new NameDefinitionDialog(
                new NameDefinitionDialogResult(
                    "ProfitMargin",
                    "Workbook",
                    "Formula authoring tour calculated margin range.",
                    FormatFormulaAuthoringNamesRangeReference(context.Sheet, context.MarginRange)),
                GetFormulaAuthoringNamesScopeOptions(),
                isValidRange: rangeText => NamedRangeInputParser.TryParseRange(_workbook, rangeText, out _),
                validateName: _workbook.ValidateNamedRangeName)
            {
                Owner = this
            };
            defineNameDialog.Show();
            defineNameDialog.Activate();
            defineNameDialog.UpdateLayout();
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(defineNameDialog, outputDir, "freex_formula_authoring_names_define_name_dialog");
            captures.Add(CreateFormulaAuthoringNamesCapture(
                "define-name-dialog",
                "freex_formula_authoring_names_define_name_dialog",
                "Define Name dialog",
                "RenderTargetBitmap-define-name-dialog",
                defineNameDialog.ActualWidth,
                defineNameDialog.ActualHeight,
                "Production Define Name dialog with the name box focused/select-all and Refers To seeded from the selected formula range."));
            defineNameDialog.Close();
            defineNameDialog = null;

            createFromSelectionDialog = new CreateNamesFromSelectionDialog
            {
                Owner = this
            };
            createFromSelectionDialog.Show();
            createFromSelectionDialog.Activate();
            createFromSelectionDialog.UpdateLayout();
            await Task.Delay(450);
            await CaptureWindowElementForScreenshotTourAsync(createFromSelectionDialog, outputDir, "freex_formula_authoring_names_create_from_selection_dialog");
            captures.Add(CreateFormulaAuthoringNamesCapture(
                "create-from-selection-dialog",
                "freex_formula_authoring_names_create_from_selection_dialog",
                "Create from Selection dialog",
                "RenderTargetBitmap-create-from-selection-dialog",
                createFromSelectionDialog.ActualWidth,
                createFromSelectionDialog.ActualHeight,
                "Production Create from Selection dialog with Top row and Left column defaults visible."));
            createFromSelectionDialog.Close();
            createFromSelectionDialog = null;

            ValidateFormulaAuthoringNamesTourEvidence(outputDir, captures);
            await WriteFormulaAuthoringNamesTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteFormulaAuthoringNamesTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openMenu is { IsOpen: true })
                openMenu.IsOpen = false;
            if (insertFunctionDialog is { IsVisible: true })
                insertFunctionDialog.Close();
            if (nameManagerDialog is { IsVisible: true })
                nameManagerDialog.Close();
            if (defineNameDialog is { IsVisible: true })
                defineNameDialog.Close();
            if (createFromSelectionDialog is { IsVisible: true })
                createFromSelectionDialog.Close();
        }

        async Task<FormulaAuthoringNamesTourManifestCapture> CaptureFormulaAuthoringNamesMenuAsync(
            string captureOutputDir,
            string state,
            string fileName,
            string commandName,
            string evidenceSummary)
        {
            SelectFormulaAuthoringNamesRibbonTabForTour();
            var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName)
                ?? throw new InvalidOperationException($"Formula authoring/names tour could not find '{commandName}' ribbon button.");
            var menu = button.ContextMenu
                ?? throw new InvalidOperationException($"Formula authoring/names tour could not find '{commandName}' context menu.");

            OpenRibbonContextMenu(button, menu);
            openMenu = menu;
            await Task.Delay(350);
            menu.UpdateLayout();
            await CaptureElementAsync(menu, captureOutputDir, fileName);
            var headers = new List<string>();
            AddMenuHeaders(menu, headers);
            var capture = CreateFormulaAuthoringNamesCapture(
                state,
                fileName,
                $"{commandName} menu",
                "RenderTargetBitmap-formulas-context-menu",
                menu.ActualWidth,
                menu.ActualHeight,
                evidenceSummary,
                headers);
            menu.IsOpen = false;
            openMenu = null;
            return capture;
        }

        async Task<FormulaAuthoringNamesTourManifestCapture> CaptureFormulaAuthoringNamesFunctionMenuAsync(
            string captureOutputDir,
            string state,
            string fileName,
            string commandName,
            RoutedEventHandler openHandler,
            string evidenceSummary)
        {
            SelectFormulaAuthoringNamesRibbonTabForTour();
            var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName)
                ?? throw new InvalidOperationException($"Formula authoring/names tour could not find '{commandName}' ribbon button.");

            openHandler(button, new RoutedEventArgs(ButtonBase.ClickEvent, button));
            var menu = button.ContextMenu
                ?? throw new InvalidOperationException($"Formula authoring/names tour did not open the '{commandName}' context menu.");
            openMenu = menu;
            await Task.Delay(350);
            menu.UpdateLayout();
            await CaptureElementAsync(menu, captureOutputDir, fileName);
            var headers = new List<string>();
            AddMenuHeaders(menu, headers);
            var capture = CreateFormulaAuthoringNamesCapture(
                state,
                fileName,
                $"{commandName} menu",
                "RenderTargetBitmap-formulas-context-menu",
                menu.ActualWidth,
                menu.ActualHeight,
                evidenceSummary,
                headers);
            menu.IsOpen = false;
            openMenu = null;
            return capture;
        }
    }

    private FormulaAuthoringNamesTourContext EnsureFormulaAuthoringNamesTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Formula authoring/names tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        for (uint row = 1; row <= 8; row++)
        {
            for (uint col = 1; col <= 6; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        var values = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Revenue")),
            (1, 3, new TextValue("Cost")),
            (1, 4, new TextValue("Profit")),
            (1, 5, new TextValue("Margin")),
            (2, 1, new TextValue("North")),
            (3, 1, new TextValue("South")),
            (4, 1, new TextValue("East")),
            (5, 1, new TextValue("West")),
            (2, 2, new NumberValue(4200)),
            (3, 2, new NumberValue(3900)),
            (4, 2, new NumberValue(5100)),
            (5, 2, new NumberValue(4700)),
            (2, 3, new NumberValue(2600)),
            (3, 3, new NumberValue(2400)),
            (4, 3, new NumberValue(3150)),
            (5, 3, new NumberValue(2950))
        };

        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

        sheet.SetFormula(new CellAddress(sheet.Id, 2, 4), "B2-C2");
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 4), "B3-C3");
        sheet.SetFormula(new CellAddress(sheet.Id, 4, 4), "B4-C4");
        sheet.SetFormula(new CellAddress(sheet.Id, 5, 4), "B5-C5");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 5), "D2/B2");
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 5), "D3/B3");
        sheet.SetFormula(new CellAddress(sheet.Id, 4, 5), "D4/B4");
        sheet.SetFormula(new CellAddress(sheet.Id, 5, 5), "D5/B5");
        sheet.SetFormula(new CellAddress(sheet.Id, 7, 2), "SUM(Revenue)");
        sheet.SetFormula(new CellAddress(sheet.Id, 7, 4), "SUM(Profit)");

        var regionLabels = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1));
        var revenueRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2));
        var costRange = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 5, 3));
        var profitRange = new GridRange(new CellAddress(sheet.Id, 2, 4), new CellAddress(sheet.Id, 5, 4));
        var marginRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 5));
        var authoringRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 5));

        _workbook.DefineNamedRange("RegionLabels", regionLabels);
        _workbook.DefineNamedRange("Revenue", revenueRange);
        _workbook.DefineNamedRange("Cost", costRange);
        _workbook.DefineNamedRange("Profit", profitRange);
        // R118: see the matching note above HomeTourData -- these direct defines bypass the command
        // bus, so the Name Box's cached range index must be told about them explicitly.
        InvalidateNavigationCaches();

        SetSelectionRange(authoringRange, new CellAddress(sheet.Id, 2, 5));
        EnsureCellVisible(authoringRange.Start);
        RecalculateWorkbook();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new FormulaAuthoringNamesTourContext(
            Sheet: sheet,
            AuthoringRange: authoringRange,
            RevenueRange: revenueRange,
            CostRange: costRange,
            ProfitRange: profitRange,
            MarginRange: marginRange,
            DefinedNames: ["Cost", "Profit", "RegionLabels", "Revenue"],
            SummaryFormulaCell: new CellAddress(sheet.Id, 7, 2),
            ProfitFormulaCell: new CellAddress(sheet.Id, 2, 4));
    }

    private void SelectFormulaAuthoringNamesRibbonTabForTour()
    {
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Formulas"));
        UpdateLayout();
    }

    private IReadOnlyList<NamedRangeScopeOption> GetFormulaAuthoringNamesScopeOptions()
    {
        var options = new List<NamedRangeScopeOption> { new("Workbook", null) };
        options.AddRange(_workbook.Sheets.Select(sheet => new NamedRangeScopeOption(sheet.Name, sheet.Id)));
        return options;
    }

    private static string FormatFormulaAuthoringNamesRangeReference(Sheet sheet, GridRange range) =>
        $"{sheet.Name}!{range.Start.ToA1()}:{range.End.ToA1()}";

    private async Task ConfigureInsertFunctionDialogForFormulaAuthoringTourAsync(InsertFunctionDialog dialog)
    {
        await Task.Delay(250);
        var categoryBox = FindDescendant<ComboBox>(dialog)
            ?? throw new InvalidOperationException("Formula authoring/names tour could not find Insert Function category box.");
        categoryBox.SelectedItem = "Lookup & Reference";
        categoryBox.UpdateLayout();
        await Task.Delay(250);

        var functionList = FindDescendant<ListBox>(dialog)
            ?? throw new InvalidOperationException("Formula authoring/names tour could not find Insert Function function list.");
        foreach (var item in functionList.Items)
        {
            if (item is FreeX.App.Presentation.Dialogs.InsertFunctionCatalogEntry { Name: "XLOOKUP" })
            {
                functionList.SelectedItem = item;
                functionList.ScrollIntoView(item);
                break;
            }
        }

        dialog.UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
    }

    private async Task<FormulaAuthoringNamesTourManifestCapture> CaptureFormulaAuthoringNamesWindowStateAsync(
        string outputDir,
        string state,
        string fileName,
        string evidenceSummary)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 768);
        return CreateFormulaAuthoringNamesCapture(
            state,
            fileName,
            "Formulas ribbon and worksheet",
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 768),
            evidenceSummary);
    }

    private FormulaAuthoringNamesTourManifestCapture CreateFormulaAuthoringNamesCapture(
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary,
        IReadOnlyList<string>? menuHeaders = null)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return new FormulaAuthoringNamesTourManifestCapture(
            CaptureKey: $"interactive:formula-authoring-names:{state}",
            PairKey: $"interactive:formula-authoring-names:{state}",
            ScenarioId: "formula-authoring-names:visual-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SheetName: sheet?.Name ?? string.Empty,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            FormulaBarText: FormulaBar.Text,
            NameCount: _workbook.NamedRanges.Count,
            DefinedNames: _workbook.NamedRanges.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            MenuHeaders: menuHeaders ?? [],
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteFormulaAuthoringNamesTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_formula_authoring_names_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, FormulaAuthoringNamesTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateFormulaAuthoringNamesTourEvidence(
        string outputDir,
        IReadOnlyList<FormulaAuthoringNamesTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Formula authoring/names tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task CaptureDataToolsDialogsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteDataToolsDialogsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureDataToolsDialogsTourContext();
        var captures = new List<DataToolsDialogsTourManifestCapture>();
        Window? openDialog = null;

        try
        {
            openDialog = new AdvancedFilterDialog(
                _currentSheetId,
                context.RemoveDuplicatesRange.ToString(),
                ResolveSheetIdByName,
                _ => { })
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-003",
                "advanced-filter-dialog",
                "Advanced Filter",
                "freex_data_tools_advanced_filter_dialog",
                "Advanced Filter dialog shows action choices, list range, criteria range, copy-to range, unique records, and range picker buttons."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new TextToColumnsDialog(
                TextToColumnsDialog.BuildPreviewRows(context.Sheet, context.TextToColumnsRange),
                context.TextToColumnsRange.Start,
                _ => { })
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-004",
                "text-to-columns-step-1-original-data-type",
                "Text to Columns",
                "freex_data_tools_text_to_columns_step1_original_data_type",
                "Text to Columns wizard step 1 shows delimited/fixed-width original data type choices and a seeded preview."));

            ClickDataToolsDialogButton(openDialog, UiText.Get("TextToColumns_NextButton"));
            await WaitForDataToolsDialogRenderAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-004",
                "text-to-columns-step-2-delimited",
                "Text to Columns",
                "freex_data_tools_text_to_columns_step2_delimited",
                "Text to Columns wizard step 2 shows delimiter choices, text qualifier, consecutive delimiter option, and split preview."));

            SetDataToolsDialogRadio(openDialog, UiText.Get("TextToColumns_FixedWidth"));
            await WaitForDataToolsDialogRenderAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-004",
                "text-to-columns-step-2-fixed-width",
                "Text to Columns",
                "freex_data_tools_text_to_columns_step2_fixed_width",
                "Text to Columns wizard step 2 fixed-width mode shows break-position entry, ruler surface, and preview."));

            SetDataToolsDialogRadio(openDialog, UiText.Get("TextToColumns_Delimited"));
            ClickDataToolsDialogButton(openDialog, UiText.Get("TextToColumns_NextButton"));
            await WaitForDataToolsDialogRenderAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-004",
                "text-to-columns-step-3-column-format-destination",
                "Text to Columns",
                "freex_data_tools_text_to_columns_step3_column_format_destination",
                "Text to Columns wizard step 3 shows column data format choices, destination editor, advanced separators, and final preview."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new RemoveDuplicatesDialog(
                RemoveDuplicatesDialog.BuildColumnChoices(context.Sheet, context.RemoveDuplicatesRange),
                RemoveDuplicatesDialog.BuildColumnChoices(context.Sheet, context.RemoveDuplicatesRange, hasHeaders: false),
                hasHeaders: true)
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-005",
                "remove-duplicates-header-column-list",
                "Remove Duplicates",
                "freex_data_tools_remove_duplicates_headers_columns",
                "Remove Duplicates dialog shows My data has headers enabled plus header-derived column checkboxes and Select All/Unselect All controls."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = CreateDataValidationTourDialog();
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-005",
                "data-validation-settings-tab",
                "Data Validation",
                "freex_data_tools_data_validation_settings_tab",
                "Data Validation Settings tab shows list validation criteria, source editor, in-cell dropdown, ignore blank, and same-settings controls."));

            SelectDataToolsTab(openDialog, 1);
            await WaitForDataToolsDialogRenderAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-005",
                "data-validation-input-message-tab",
                "Data Validation",
                "freex_data_tools_data_validation_input_message_tab",
                "Data Validation Input Message tab shows title and message editors with show-input-message enabled."));

            SelectDataToolsTab(openDialog, 2);
            await WaitForDataToolsDialogRenderAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-005",
                "data-validation-error-alert-tab",
                "Data Validation",
                "freex_data_tools_data_validation_error_alert_tab",
                "Data Validation Error Alert tab shows alert style, title, and error message editors with show-error-alert enabled."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new GoalSeekDialog(context.Sheet.Id, context.GoalSeekSetCell, _ => { }) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            FindDescendantByAutomationId<TextBox>(openDialog, "GoalSeekToValueBox")!.Text = "5000";
            FindDescendantByAutomationId<TextBox>(openDialog, "GoalSeekChangingCellBox")!.Text = context.GoalSeekChangingCell.ToA1();
            await WaitForDataToolsDialogRenderAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-006",
                "goal-seek-dialog",
                "Goal Seek",
                "freex_data_tools_goal_seek_dialog",
                "Goal Seek dialog shows Set cell, To value, By changing cell, range picker buttons, and OK/Cancel controls."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new GoalSeekStatusDialog(new GoalSeekResult(true, 125d, 5000d, 7), 5000d) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-006",
                "goal-seek-status-dialog",
                "Goal Seek Status",
                "freex_data_tools_goal_seek_status_dialog",
                "Goal Seek Status dialog shows a converged result message with Keep Result and Restore Original Values default actions."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new ScenarioManagerDialog(_workbook, context.Sheet.Id, ResolveSheetIdByName) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-006",
                "scenario-manager-dialog",
                "Scenario Manager",
                "freex_data_tools_scenario_manager_dialog",
                "Scenario Manager dialog shows existing scenario list, add/edit fields, changing/result cells, comment, hidden/locked options, and action buttons."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new DataTableDialog(context.Sheet.Id, context.DataTableRange, _ => { }) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            FindDescendantByAutomationId<TextBox>(openDialog, "DataTableRowInputCellBox")!.Text = "E2";
            FindDescendantByAutomationId<TextBox>(openDialog, "DataTableColumnInputCellBox")!.Text = "F2";
            await WaitForDataToolsDialogRenderAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-006",
                "data-table-dialog",
                "Data Table",
                "freex_data_tools_data_table_dialog",
                "Data Table dialog shows row and column input cell editors with range picker buttons for a seeded two-variable table range."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new ConsolidateDialog(
                context.Sheet.Id,
                ConsolidateParityFixture.SourceReference,
                ConsolidateParityFixture.DestinationReference,
                _ => { }) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-006",
                "consolidate-dialog",
                "Consolidate",
                "freex_data_tools_consolidate_dialog",
                "Consolidate dialog shows source reference, all references list, destination cell, function selector, label options, and create-links option."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            openDialog = new ForecastSheetDialog(6) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureDataToolsDialogWindowAsync(
                openDialog,
                outputDir,
                "UI-CMD-DATA-006",
                "forecast-sheet-dialog",
                "Forecast Sheet",
                "freex_data_tools_forecast_sheet_dialog",
                "Forecast Sheet dialog shows forecast periods input and Create/Cancel command row."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            ValidateDataToolsDialogsTourEvidence(outputDir, captures);
            await WriteDataToolsDialogsTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteDataToolsDialogsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openDialog is { IsVisible: true })
                CloseDataToolsTourDialog(openDialog);
        }
    }

    private DataToolsDialogsTourContext EnsureDataToolsDialogsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Data tools dialogs tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Sales Rep")),
            (1, 3, new TextValue("Amount")),
            (1, 4, new TextValue("Status")),
            (2, 1, new TextValue("North")),
            (2, 2, new TextValue("Ada")),
            (2, 3, new NumberValue(4200)),
            (2, 4, new TextValue("Open")),
            (3, 1, new TextValue("South")),
            (3, 2, new TextValue("Beth")),
            (3, 3, new NumberValue(3150)),
            (3, 4, new TextValue("Closed")),
            (4, 1, new TextValue("North")),
            (4, 2, new TextValue("Ada")),
            (4, 3, new NumberValue(4200)),
            (4, 4, new TextValue("Open")),
            (6, 1, new TextValue("East,125,Open")),
            (7, 1, new TextValue("West,98,Closed")),
            (8, 1, new TextValue("North,143,Open")),
            (10, 1, new TextValue("Year")),
            (10, 2, new TextValue("Revenue")),
            (11, 1, new NumberValue(2023)),
            (11, 2, new NumberValue(1200)),
            (12, 1, new NumberValue(2024)),
            (12, 2, new NumberValue(1420)),
            (13, 1, new NumberValue(2025)),
            (13, 2, new NumberValue(1630)),
            (2, 5, new NumberValue(125)),
            (2, 6, new NumberValue(42))
        };

        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

        if (!_workbook.Scenarios.Any(scenario => string.Equals(scenario.Name, "Tour Base Case", StringComparison.Ordinal)))
        {
            _workbook.Scenarios.Add(new WorkbookScenario(
                "Tour Base Case",
                [
                    new ScenarioCellValue(new CellAddress(sheet.Id, 2, 3), new NumberValue(4200)),
                    new ScenarioCellValue(new CellAddress(sheet.Id, 3, 3), new NumberValue(3150))
                ],
                "Seeded scenario for deterministic Data Tools dialog visual evidence.",
                Hidden: false,
                Locked: true));
        }

        var removeDuplicatesRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));
        var textToColumnsRange = new GridRange(new CellAddress(sheet.Id, 6, 1), new CellAddress(sheet.Id, 8, 1));
        var dataTableRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 4));
        var consolidateSourceRange = ConsolidateParityFixture.CreateSourceRange(sheet.Id);
        SetSelectionRange(removeDuplicatesRange, removeDuplicatesRange.Start);
        EnsureCellVisible(removeDuplicatesRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new DataToolsDialogsTourContext(
            sheet,
            textToColumnsRange,
            removeDuplicatesRange,
            dataTableRange,
            consolidateSourceRange,
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 5));
    }

    private DataValidationDialog CreateDataValidationTourDialog()
    {
        var validation = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "\"North,South,West\"",
            AllowBlank = true,
            ShowDropdown = true,
            ShowInputMessage = true,
            PromptTitle = "Choose a region",
            PromptMessage = "Pick a region from the approved sales territories.",
            ShowErrorMessage = true,
            AlertStyle = DvAlertStyle.Stop,
            ErrorTitle = "Invalid region",
            ErrorMessage = "Use one of the listed region names."
        };

        return new DataValidationDialog(validation, _ => { })
        {
            Owner = this,
            SelectionSource = "$A$2:$A$4"
        };
    }

    private static async Task ShowDataToolsTourDialogAsync(Window dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Show();
        dialog.Activate();
        dialog.UpdateLayout();
        await Task.Delay(450);
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void CloseDataToolsTourDialog(Window dialog)
    {
        if (dialog.IsVisible)
            dialog.Close();
    }

    private static void ClickDataToolsDialogButton(Window dialog, string content)
    {
        var button = FindDescendantByContent<Button>(dialog, content)
            ?? throw new InvalidOperationException($"Data tools dialogs tour could not find button '{content}'.");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static void SetDataToolsDialogRadio(Window dialog, string content)
    {
        var radio = FindDescendantByContent<RadioButton>(dialog, content)
            ?? throw new InvalidOperationException($"Data tools dialogs tour could not find radio button '{content}'.");
        radio.IsChecked = true;
    }

    private static void SelectDataToolsTab(Window dialog, int index)
    {
        var tabs = FindDescendant<TabControl>(dialog)
            ?? throw new InvalidOperationException("Data tools dialogs tour could not find a tab control.");
        tabs.SelectedIndex = index;
    }

    private static async Task WaitForDataToolsDialogRenderAsync(Window dialog)
    {
        dialog.UpdateLayout();
        await Task.Delay(250);
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private async Task<DataToolsDialogsTourManifestCapture> CaptureDataToolsDialogWindowAsync(
        Window dialog,
        string outputDir,
        string commandRow,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        await WaitForDataToolsDialogRenderAsync(dialog);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        return new DataToolsDialogsTourManifestCapture(
            CaptureKey: $"data-tools-dialogs:{state}",
            PairKey: $"interactive:data-tools-dialogs:{state}",
            CatalogCommandRow: commandRow,
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-data-tools-dialog-window",
            EvidenceSummary: evidenceSummary,
            CaptureLogicalWidth: dialog.ActualWidth,
            CaptureLogicalHeight: dialog.ActualHeight);
    }

    private static void DeleteDataToolsDialogsTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_data_tools_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, DataToolsDialogsTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateDataToolsDialogsTourEvidence(string outputDir, IReadOnlyList<DataToolsDialogsTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Data tools dialogs tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private async Task CaptureDataSortFilterOutlineTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteDataSortFilterOutlineTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureDataSortFilterOutlineTourContext();
        var captures = new List<DataSortFilterOutlineTourManifestCapture>();
        Window? openWindow = null;

        try
        {
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Data"));
            await WaitForRibbonScreenshotRenderPassAsync();
            captures.Add(await CaptureDataSortFilterOutlineWindowAsync(
                outputDir,
                "UI-CAT-DATA-001",
                "data-tab-sort-filter-outline-surface",
                "Data tab",
                "freex_data_sort_filter_outline_data_tab_surface",
                "Data tab shows Get Data, Refresh All, Sort & Filter, and Outline command groups against seeded tabular data."));

            openWindow = new SortDialog(
                levels:
                [
                    new SortDialogLevel(0, true),
                    new SortDialogLevel(2, false)
                ],
                columnChoices: SortDialog.BuildColumnChoices(context.Sheet, context.TableRange, hasHeaders: true),
                genericColumnChoices: SortDialog.BuildColumnChoices(context.Sheet, context.TableRange, hasHeaders: false),
                rowChoices: SortDialog.BuildRowChoices(context.TableRange),
                colorChoices: SortDialog.BuildColorChoices(_workbook, context.Sheet, context.TableRange),
                cellColorChoices: SortDialog.BuildColorChoices(_workbook, context.Sheet, context.TableRange, SortOn.CellColor),
                fontColorChoices: SortDialog.BuildColorChoices(_workbook, context.Sheet, context.TableRange, SortOn.FontColor),
                iconWorkbook: _workbook,
                iconSheet: context.Sheet,
                iconRange: context.TableRange)
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openWindow);
            captures.Add(await CaptureDataSortFilterOutlineDialogAsync(
                openWindow,
                outputDir,
                "UI-CMD-DATA-002",
                "sort-dialog-multi-level",
                "Sort dialog",
                "freex_data_sort_filter_outline_sort_dialog",
                "Sort dialog shows header-aware column choices, two sort levels, Sort On/Order columns, level commands, Options, OK, and Cancel."));
            CloseDataToolsTourDialog(openWindow);
            openWindow = null;

            openWindow = new SortOptionsDialog(new SortDialogOptions(
                CaseSensitive: true,
                LeftToRight: true,
                FirstKeySortOrder: "Jan, Feb, Mar, Apr, May, Jun, Jul, Aug, Sep, Oct, Nov, Dec"))
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openWindow);
            captures.Add(await CaptureDataSortFilterOutlineDialogAsync(
                openWindow,
                outputDir,
                "UI-CMD-DATA-002",
                "sort-options-left-to-right",
                "Sort Options dialog",
                "freex_data_sort_filter_outline_sort_options_dialog",
                "Sort Options dialog shows Case sensitive, custom first-key sort order, and left-to-right orientation choices."));
            CloseDataToolsTourDialog(openWindow);
            openWindow = null;

            if (CreateAutoFilterFlyoutDialog(context.Sheet, context.FilterHeaderCell, null, out var autoFilterPlan) is not { } filterDialog ||
                autoFilterPlan is null)
            {
                throw new InvalidOperationException("Data sort/filter/outline tour could not create the AutoFilter flyout.");
            }

            openWindow = filterDialog;
            filterDialog.Show();
            filterDialog.Activate();
            filterDialog.UpdateLayout();
            await Task.Delay(350);
            var searchBox = FindDescendantByAutomationName<TextBox>(filterDialog, UiText.Get("AutoFilter_Search3"))
                ?? FindDescendant<TextBox>(filterDialog)
                ?? throw new InvalidOperationException("Data sort/filter/outline tour could not find the AutoFilter search box.");
            searchBox.Text = "Open";
            await WaitForDataToolsDialogRenderAsync(filterDialog);
            captures.Add(await CaptureDataSortFilterOutlineDialogAsync(
                filterDialog,
                outputDir,
                "UI-CMD-DATA-008",
                "autofilter-flyout-search-open",
                "AutoFilter flyout",
                "freex_data_sort_filter_outline_autofilter_search_open",
                "AutoFilter flyout for the Status header shows sort commands, text filters, search text 'Open', and filtered checklist values."));
            CloseDataToolsTourDialog(filterDialog);
            openWindow = null;

            openWindow = new SubtotalDialog(SubtotalDialog.BuildColumnChoices(context.Sheet, context.TableRange))
            {
                Owner = this
            };
            await ShowDataToolsTourDialogAsync(openWindow);
            captures.Add(await CaptureDataSortFilterOutlineDialogAsync(
                openWindow,
                outputDir,
                "UI-CMD-DATA-007",
                "subtotal-dialog-defaults",
                "Subtotal dialog",
                "freex_data_sort_filter_outline_subtotal_dialog",
                "Subtotal dialog shows At each change in, Use function, Add subtotal to, replace/page-break/summary options, Remove All, OK, and Cancel."));
            CloseDataToolsTourDialog(openWindow);
            openWindow = null;

            SetSelectionRange(context.OutlineRange, context.OutlineRange.Start);
            GroupRowsBtn_Click(this, new RoutedEventArgs());
            await WaitForDataSortFilterOutlineWindowAsync(context.OutlineRange.Start);
            captures.Add(await CaptureDataSortFilterOutlineWindowAsync(
                outputDir,
                "UI-CMD-DATA-007",
                "outline-group-expanded",
                "Worksheet grid",
                "freex_data_sort_filter_outline_group_expanded",
                "Worksheet grid shows seeded rows after the production Group command assigned row outline levels while detail remains expanded."));

            CollapseGroupBtn_Click(this, new RoutedEventArgs());
            await WaitForDataSortFilterOutlineWindowAsync(context.OutlineRange.Start);
            captures.Add(await CaptureDataSortFilterOutlineWindowAsync(
                outputDir,
                "UI-CMD-DATA-007",
                "outline-hide-detail-collapsed",
                "Worksheet grid",
                "freex_data_sort_filter_outline_hide_detail_collapsed",
                "Worksheet grid shows the Hide Detail command collapsed the grouped rows through GroupHiddenRows."));

            ExpandGroupBtn_Click(this, new RoutedEventArgs());
            await WaitForDataSortFilterOutlineWindowAsync(context.OutlineRange.Start);
            captures.Add(await CaptureDataSortFilterOutlineWindowAsync(
                outputDir,
                "UI-CMD-DATA-007",
                "outline-show-detail-expanded",
                "Worksheet grid",
                "freex_data_sort_filter_outline_show_detail_expanded",
                "Worksheet grid shows the Show Detail command restored the grouped rows while outline levels remain."));

            await CaptureDataSortFilterOutlineRibbonMenuAsync(
                outputDir,
                captures,
                "Group",
                "UI-CMD-DATA-007",
                "group-dropdown-open",
                "freex_data_sort_filter_outline_group_dropdown",
                "Group dropdown shows the implemented Group menu entry.");
            await CaptureDataSortFilterOutlineRibbonMenuAsync(
                outputDir,
                captures,
                "Ungroup",
                "UI-CMD-DATA-007",
                "ungroup-dropdown-open",
                "freex_data_sort_filter_outline_ungroup_dropdown",
                "Ungroup dropdown shows Ungroup and Clear Outline menu entries.");

            ValidateDataSortFilterOutlineTourEvidence(outputDir, captures);
            await WriteDataSortFilterOutlineTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteDataSortFilterOutlineTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openWindow is { IsVisible: true })
                CloseDataToolsTourDialog(openWindow);
        }
    }

    private DataSortFilterOutlineTourContext EnsureDataSortFilterOutlineTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Data sort/filter/outline tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 9, 5));
        foreach (var address in tableRange.AllCells())
            sheet.ClearCell(address);

        var rows = new (string Region, string Rep, double Amount, string Status, string Month)[]
        {
            ("North", "Ada", 4200, "Open", "Jan"),
            ("South", "Beth", 3150, "Closed", "Feb"),
            ("North", "Cora", 5100, "Open", "Mar"),
            ("East", "Drew", 2800, "Pending", "Apr"),
            ("West", "Eli", 6300, "Open", "May"),
            ("East", "Fay", 2400, "Closed", "Jun"),
            ("South", "Gus", 4700, "Open", "Jul"),
            ("West", "Hana", 3900, "Pending", "Aug")
        };

        var headers = new[] { "Region", "Rep", "Amount", "Status", "Month" };
        for (var index = 0; index < headers.Length; index++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(index + 1)), new TextValue(headers[index]));

        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)(index + 2);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(rows[index].Region));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(rows[index].Rep));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(rows[index].Amount));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new TextValue(rows[index].Status));
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue(rows[index].Month));
        }

        sheet.AutoFilter = new WorksheetAutoFilterModel(tableRange.ToString(), null);
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenRows.Clear();
        sheet.GroupHiddenRows.Clear();
        sheet.RowOutlineLevels.Clear();
        sheet.ShowOutlineSymbols = true;
        _filterWorkflowSession.ResetAutoFilterState();

        SetSelectionRange(tableRange, tableRange.Start);
        EnsureCellVisible(tableRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        var outlineRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 6, 5));
        return new DataSortFilterOutlineTourContext(
            sheet,
            tableRange,
            outlineRange,
            new CellAddress(sheet.Id, 1, 4));
    }

    private async Task WaitForDataSortFilterOutlineWindowAsync(CellAddress visibleCell)
    {
        EnsureCellVisible(visibleCell);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await Task.Delay(300);
        await WaitForRibbonScreenshotRenderPassAsync();
    }

    private async Task<DataSortFilterOutlineTourManifestCapture> CaptureDataSortFilterOutlineWindowAsync(
        string outputDir,
        string catalogRow,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return new DataSortFilterOutlineTourManifestCapture(
            CaptureKey: $"data-sort-filter-outline:{state}",
            PairKey: $"interactive:data-sort-filter-outline:{state}",
            CatalogRow: catalogRow,
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-full",
            EvidenceSummary: evidenceSummary,
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: Math.Min(ActualHeight, 760));
    }

    private async Task<DataSortFilterOutlineTourManifestCapture> CaptureDataSortFilterOutlineDialogAsync(
        Window dialog,
        string outputDir,
        string catalogRow,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        await WaitForDataToolsDialogRenderAsync(dialog);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        return new DataSortFilterOutlineTourManifestCapture(
            CaptureKey: $"data-sort-filter-outline:{state}",
            PairKey: $"interactive:data-sort-filter-outline:{state}",
            CatalogRow: catalogRow,
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-wpf-window",
            EvidenceSummary: evidenceSummary,
            CaptureLogicalWidth: dialog.ActualWidth,
            CaptureLogicalHeight: dialog.ActualHeight);
    }

    private async Task CaptureDataSortFilterOutlineRibbonMenuAsync(
        string outputDir,
        List<DataSortFilterOutlineTourManifestCapture> captures,
        string commandName,
        string catalogRow,
        string state,
        string fileName,
        string evidenceSummary)
    {
        var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName)
            ?? throw new InvalidOperationException($"Data sort/filter/outline tour could not find the {commandName} ribbon button.");
        var menu = button.ContextMenu
            ?? throw new InvalidOperationException($"Data sort/filter/outline tour could not find the {commandName} ribbon menu.");

        try
        {
            MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            await Task.Delay(350);
            menu.UpdateLayout();
            await CaptureElementAsync(menu, outputDir, fileName);
            captures.Add(new DataSortFilterOutlineTourManifestCapture(
                CaptureKey: $"data-sort-filter-outline:{state}",
                PairKey: $"interactive:data-sort-filter-outline:{state}",
                CatalogRow: catalogRow,
                State: state,
                Surface: $"{commandName} dropdown",
                FileName: fileName,
                OutputFileName: $"{fileName}.png",
                CaptureMethod: "RenderTargetBitmap-ribbon-context-menu",
                EvidenceSummary: evidenceSummary,
                CaptureLogicalWidth: menu.ActualWidth,
                CaptureLogicalHeight: menu.ActualHeight));
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private static T? FindDescendantByAutomationName<T>(DependencyObject root, string automationName)
        where T : FrameworkElement
    {
        if (root is T element && string.Equals(AutomationProperties.GetName(element), automationName, StringComparison.Ordinal))
            return element;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var match = FindDescendantByAutomationName<T>(child, automationName);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static void DeleteDataSortFilterOutlineTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_data_sort_filter_outline_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, DataSortFilterOutlineTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateDataSortFilterOutlineTourEvidence(string outputDir, IReadOnlyList<DataSortFilterOutlineTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Data sort/filter/outline tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private async Task CaptureInsertObjectsLinksTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteInsertObjectsLinksTourEvidence(outputDir);

        var captures = new List<InsertObjectsLinksTourManifestCapture>();

        try
        {
            await ApplyScreenshotTourWidthAsync(new RibbonScreenshotTourWidth("1100", 1100));
            EnsureInsertObjectsLinksTourContext();

            captures.Add(await CaptureInsertObjectsLinksDialogAsync(
                outputDir,
                new HyperlinkDialog(HyperlinkDialogParityFixture.Target, HyperlinkDialogParityFixture.DisplayText) { Owner = this },
                "freex_insert_hyperlink_dialog_address_focus",
                "hyperlink-dialog-address-focus",
                "Insert Hyperlink dialog opened with the address box as the initial focused/select-all target.",
                "RenderTargetBitmap-hyperlink-dialog-window",
                "UI-CMD-INSERT-009"));

            captures.Add(await CaptureInsertObjectsLinksDialogAsync(
                outputDir,
                new SymbolPickerDialog { Owner = this },
                "freex_insert_symbol_picker_opened",
                "symbol-picker-opened",
                "Symbol picker opened on the production Symbols tab/grid with insert/cancel actions visible.",
                "RenderTargetBitmap-symbol-picker-dialog-window",
                "UI-CMD-INSERT-009"));

            await ApplyInsertObjectsLinksTourModelEvidenceAsync();
            captures.Add(await CaptureInsertObjectsLinksWindowStateAsync(
                outputDir,
                "freex_insert_objects_grid_visuals",
                "inserted-objects-grid-visuals",
                "Worksheet visual state after applying model-backed hyperlink, rectangle shape, text box, picture placeholder, threaded comment, and note evidence.",
                "UI-CMD-INSERT-008"));

            captures.Add(await CaptureInsertObjectsLinksInlineThreadedCommentEditorAsync(outputDir));
            captures.Add(await CaptureInsertObjectsLinksInlineNoteEditorAsync(outputDir));

            ReviewShowCommentsBtn_Click(this, new RoutedEventArgs());
            if (_reviewCommentsWindow is null)
                throw new InvalidOperationException("Insert objects/links/text tour could not open the threaded comments list surface.");

            captures.Add(await CaptureInsertObjectsLinksOwnedWindowAsync(
                outputDir,
                _reviewCommentsWindow,
                "freex_insert_comments_list_surface",
                "comments-list-surface",
                "Review/Insert comments list surface showing the seeded threaded comment.",
                "RenderTargetBitmap-comment-list-window",
                "UI-CMD-INSERT-010"));
            _reviewCommentsWindow.Close();
            _reviewCommentsWindow = null;

            ReviewShowNotesBtn_Click(this, new RoutedEventArgs());
            if (_reviewNotesWindow is null)
                throw new InvalidOperationException("Insert objects/links/text tour could not open the notes list surface.");

            captures.Add(await CaptureInsertObjectsLinksOwnedWindowAsync(
                outputDir,
                _reviewNotesWindow,
                "freex_insert_notes_list_surface",
                "notes-list-surface",
                "Review/Insert notes list surface showing the seeded note.",
                "RenderTargetBitmap-note-list-window",
                "UI-CMD-INSERT-010"));
            _reviewNotesWindow.Close();
            _reviewNotesWindow = null;

            ValidateInsertObjectsLinksTourEvidence(outputDir, captures);
            await WriteInsertObjectsLinksTourManifestAsync(outputDir, captures);
        }
        catch
        {
            DeleteInsertObjectsLinksTourEvidence(outputDir);
            throw;
        }
    }

    private async Task<InsertObjectsLinksTourManifestCapture> CaptureInsertObjectsLinksDialogAsync(
        string outputDir,
        Window dialog,
        string fileName,
        string state,
        string evidenceSummary,
        string captureMethod,
        string commandRow)
    {
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
            return CreateInsertObjectsLinksTourCapture(
                state,
                fileName,
                evidenceSummary,
                captureMethod,
                dialog.ActualWidth,
                dialog.ActualHeight,
                commandRow);
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<InsertObjectsLinksTourManifestCapture> CaptureInsertObjectsLinksOwnedWindowAsync(
        string outputDir,
        Window window,
        string fileName,
        string state,
        string evidenceSummary,
        string captureMethod,
        string commandRow)
    {
        window.Activate();
        await Task.Delay(250);
        window.UpdateLayout();
        await CaptureWindowElementForScreenshotTourAsync(window, outputDir, fileName);
        return CreateInsertObjectsLinksTourCapture(
            state,
            fileName,
            evidenceSummary,
            captureMethod,
            window.ActualWidth,
            window.ActualHeight,
            commandRow);
    }

    private async Task<InsertObjectsLinksTourManifestCapture> CaptureInsertObjectsLinksWindowStateAsync(
        string outputDir,
        string fileName,
        string state,
        string evidenceSummary,
        string commandRow)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateInsertObjectsLinksTourCapture(
            state,
            fileName,
            evidenceSummary,
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            commandRow);
    }

    private async Task<InsertObjectsLinksTourManifestCapture> CaptureInsertObjectsLinksInlineThreadedCommentEditorAsync(string outputDir)
    {
        var address = new CellAddress(_currentSheetId, 6, 4);
        var sheet = _workbook.GetSheet(_currentSheetId)
            ?? throw new InvalidOperationException("Insert objects/links/text tour requires an active worksheet.");
        sheet.ThreadedComments.TryGetValue(address, out var existing);
        SetSelectionRange(new GridRange(address, address), address);
        EnsureCellVisible(address);
        UpdateViewport();
        RefreshReviewCommentNoteCommandStates();
        RefreshToolbar();
        UpdateLayout();

        try
        {
            if (!SheetGrid.BeginThreadedCommentInlineEdit(address, address.ToA1(), existing))
                throw new InvalidOperationException("Insert objects/links/text tour could not open the inline New Comment popup.");

            await Task.Delay(300);
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            const string fileName = "freex_insert_new_comment_inline_popup";
            await CaptureCurrentWindowAsync(outputDir, fileName, 760);
            return CreateInsertObjectsLinksTourCapture(
                "new-threaded-comment-inline-popup",
                fileName,
                "New Comment opens as an in-window yellow popup near D6 with the threaded-comment editor available inline.",
                "RenderTargetBitmap-window-full",
                ActualWidth,
                Math.Min(ActualHeight, 760),
                "UI-CMD-INSERT-010");
        }
        finally
        {
            SheetGrid.HideCommentPreview();
        }
    }

    private async Task<InsertObjectsLinksTourManifestCapture> CaptureInsertObjectsLinksInlineNoteEditorAsync(string outputDir)
    {
        var address = new CellAddress(_currentSheetId, 6, 5);
        var sheet = _workbook.GetSheet(_currentSheetId)
            ?? throw new InvalidOperationException("Insert objects/links/text tour requires an active worksheet.");
        sheet.Comments.TryGetValue(address, out var noteText);
        SetSelectionRange(new GridRange(address, address), address);
        EnsureCellVisible(address);
        UpdateViewport();
        RefreshReviewCommentNoteCommandStates();
        RefreshToolbar();
        UpdateLayout();

        try
        {
            if (!SheetGrid.BeginNoteInlineEdit(address, address.ToA1(), noteText ?? string.Empty))
                throw new InvalidOperationException("Insert objects/links/text tour could not open the inline New Note popup.");

            await Task.Delay(300);
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            const string fileName = "freex_insert_new_note_inline_popup";
            await CaptureCurrentWindowAsync(outputDir, fileName, 760);
            return CreateInsertObjectsLinksTourCapture(
                "new-note-inline-popup",
                fileName,
                "New Note opens as an in-window yellow popup near E6 with note text editing available inline.",
                "RenderTargetBitmap-window-full",
                ActualWidth,
                Math.Min(ActualHeight, 760),
                "UI-CMD-INSERT-010");
        }
        finally
        {
            SheetGrid.HideCommentPreview();
        }
    }

    private InsertObjectsLinksTourManifestCapture CreateInsertObjectsLinksTourCapture(
        string state,
        string fileName,
        string evidenceSummary,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string commandRow) =>
        new(
            CaptureKey: $"insert-objects-links:{state}",
            PairKey: $"interactive:insert-objects-links:{state}",
            ScenarioId: "insert:objects-links-text",
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CounterpartFileName: $"interactive_insert_objects_links_{state.Replace("-", "_", StringComparison.Ordinal)}.png",
            EvidenceSummary: evidenceSummary,
            CommandRow: commandRow,
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight);

    private void EnsureInsertObjectsLinksTourContext()
    {
        SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
        UpdateViewport();
        RefreshToolbar();
        RefreshReviewCommentNoteCommandStates();
    }

    private async Task ApplyInsertObjectsLinksTourModelEvidenceAsync()
    {
        var sheetId = _currentSheetId;
        ExecuteInsertObjectsLinksTourCommand(new SetHyperlinkCommand(
            sheetId,
            new CellAddress(sheetId, 2, 2),
            HyperlinkDialogParityFixture.Target,
            "FreeX hyperlink",
            new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage, HyperlinkDialogParityFixture.DisplayText, "")), "Insert Hyperlink");
        ExecuteInsertObjectsLinksTourCommand(
            DrawingInsertionPlanner.BuildShapeCommand(sheetId, new CellAddress(sheetId, 4, 2), DrawingShapeKind.Rectangle),
            "Insert Shape");
        ExecuteInsertObjectsLinksTourCommand(
            DrawingInsertionPlanner.BuildTextBoxCommand(sheetId, new CellAddress(sheetId, 4, 5), "Text Box evidence"),
            "Insert Text Box");
        ExecuteInsertObjectsLinksTourCommand(
            PictureInsertionPlacementPlanner.CreateInsertPictureCommand(
                sheetId,
                new CellAddress(sheetId, 8, 2),
                [1, 2, 3, 4],
                "image/png"),
            "Insert Picture");
        ExecuteInsertObjectsLinksTourCommand(
            new SetThreadedCommentCommand(sheetId, new CellAddress(sheetId, 6, 4), "Threaded comment evidence"),
            "Threaded Comment");
        ExecuteInsertObjectsLinksTourCommand(
            new SetCommentCommand(sheetId, new CellAddress(sheetId, 6, 5), "Note evidence"),
            "Comment");

        SetActiveCell(new CellAddress(sheetId, 4, 2));
        EnsureCellVisible(new CellAddress(sheetId, 8, 2));
        UpdateViewport();
        RefreshToolbar();
        RefreshReviewCommentNoteCommandStates();
        await Task.Delay(350);
    }

    private void ExecuteInsertObjectsLinksTourCommand(IWorkbookCommand command, string title)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException($"Insert objects/links/text tour failed to apply '{title}': {outcome.ErrorMessage}");
    }

    private static void DeleteInsertObjectsLinksTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, InsertObjectsLinksTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateInsertObjectsLinksTourEvidence(
        string outputDir,
        IReadOnlyList<InsertObjectsLinksTourManifestCapture> captures)
    {
        if (captures.Count != 7)
            throw new InvalidOperationException($"Insert objects/links/text tour expected 7 captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Insert objects/links/text tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task CaptureInsertTablesChartsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteInsertTablesChartsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureInsertTablesChartsTourContext();
        var captures = new List<InsertTablesChartsTourManifestCapture>();
        Window? openDialog = null;

        try
        {
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Insert"));
            UpdateViewport();
            RefreshToolbar();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(250);
            captures.Add(await CaptureInsertTablesChartsWindowStateAsync(
                outputDir,
                "UI-CAT-INSERT-001,UI-CAT-INSERT-002",
                "insert-tab-command-surface",
                "Insert ribbon",
                "freex_insert_tables_charts_insert_tab",
                "Insert tab command surface with Tables, Charts, and Sparklines groups visible; object/link/text flows are intentionally only incidental if present in the ribbon."));

            openDialog = new CreateTableDialog(
                context.Sheet.Id,
                context.SourceRange.ToString(),
                context.TableStyleName)
            {
                Owner = this
            };
            await ShowInsertTablesChartsTourDialogAsync(openDialog);
            captures.Add(await CaptureInsertTablesChartsDialogAsync(
                openDialog,
                outputDir,
                "UI-CAT-INSERT-001D",
                "create-table-dialog",
                "Create Table",
                "freex_insert_tables_charts_create_table_dialog",
                "Create Table dialog shows the seeded source range, headers checkbox, range picker affordance, and OK/Cancel command row."));
            CloseInsertTablesChartsTourDialog(openDialog);
            openDialog = null;

            CreateTourStructuredTable(context);
            SetSelectionRange(new GridRange(new CellAddress(context.Sheet.Id, 2, 2), new CellAddress(context.Sheet.Id, 2, 2)), new CellAddress(context.Sheet.Id, 2, 2));
            SelectRibbonTourTab(new RibbonScreenshotTourTab("Table Design", "Table_Design", "TableDesignTab"));
            UpdateViewport();
            RefreshToolbar();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(250);
            captures.Add(await CaptureInsertTablesChartsWindowStateAsync(
                outputDir,
                "UI-CAT-INSERT-001D",
                "table-result-table-design",
                "Table result",
                "freex_insert_tables_charts_table_result_table_design",
                "Created structured table result with Table Design contextual tab selected, visible table style, row striping, and active table selection."));

            EnsurePivotTableScreenshotTourContext();
            SelectRibbonTourTab(new RibbonScreenshotTourTab("PivotTable Analyze", "PivotTable_Analyze", "PivotTableAnalyzeTab"));
            UpdateViewport();
            RefreshToolbar();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(250);
            captures.Add(await CaptureInsertTablesChartsWindowStateAsync(
                outputDir,
                "UI-CAT-INSERT-001A",
                "pivot-result-analyze",
                "PivotTable result",
                "freex_insert_tables_charts_pivot_result_analyze",
                "Created PivotTable result with PivotTable Analyze contextual tab and Field List surface visible for the active pivot target."));

            SeedInsertTablesChartsTourSourceData(context.Sheet);
            SetSelectionRange(context.SourceRange, context.SourceRange.Start);
            openDialog = new InsertChartDialog { Owner = this };
            await ShowInsertTablesChartsTourDialogAsync(openDialog);
            captures.Add(await CaptureInsertTablesChartsDialogAsync(
                openDialog,
                outputDir,
                "UI-CAT-INSERT-002A",
                "recommended-charts-dialog",
                "Insert Chart",
                "freex_insert_tables_charts_recommended_charts_dialog",
                "Insert Chart dialog opens to Recommended Charts with a seeded gallery choice and recommended layout checkbox."));
            CloseInsertTablesChartsTourDialog(openDialog);
            openDialog = null;

            CreateTourChart(context);
            _options.ObjectsDisplay = AppOptionsObjectDisplay.Placeholders;
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Insert"));
            UpdateViewport();
            RefreshToolbar();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(250);
            captures.Add(await CaptureInsertTablesChartsWindowStateAsync(
                outputDir,
                "UI-CAT-INSERT-002A",
                "chart-result",
                "Chart result",
                "freex_insert_tables_charts_chart_result",
                "Created embedded column chart target from the selected source range, shown through the existing chart placeholder display mode for deterministic visual evidence."));

            openDialog = new SparklineDialog(
                "B2:C2",
                context.SparklineLocation.ToA1(),
                SparklineKind.Line,
                sheetId: context.Sheet.Id)
            {
                Owner = this
            };
            await ShowInsertTablesChartsTourDialogAsync(openDialog);
            captures.Add(await CaptureInsertTablesChartsDialogAsync(
                openDialog,
                outputDir,
                "UI-CAT-INSERT-002",
                "sparkline-dialog",
                "Insert Sparkline",
                "freex_insert_tables_charts_sparkline_dialog",
                "Insert Sparkline dialog shows data range, location range, line/column/win-loss type selector, and both range picker buttons."));
            CloseInsertTablesChartsTourDialog(openDialog);
            openDialog = null;

            CreateTourSparklines(context);
            SetSelectionRange(new GridRange(context.SparklineLocation, context.SparklineLocation), context.SparklineLocation);
            UpdateViewport();
            RefreshToolbar();
            await WaitForRibbonScreenshotRenderPassAsync();
            await Task.Delay(250);
            captures.Add(await CaptureInsertTablesChartsWindowStateAsync(
                outputDir,
                "UI-CAT-INSERT-002",
                "sparkline-result",
                "Sparkline result",
                "freex_insert_tables_charts_sparkline_result",
                "Produced line, column, and win/loss sparkline cells next to the seeded source rows, with the first sparkline cell selected."));

            ValidateInsertTablesChartsTourEvidence(outputDir, captures);
            await WriteInsertTablesChartsTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteInsertTablesChartsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openDialog is { IsVisible: true })
                CloseInsertTablesChartsTourDialog(openDialog);
        }
    }

    private InsertTablesChartsTourContext EnsureInsertTablesChartsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Insert tables/charts tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        sheet.StructuredTables.RemoveAll(table => string.Equals(table.Name, ScreenshotTourTableName, StringComparison.OrdinalIgnoreCase));
        sheet.PivotTables.RemoveAll(pivot => string.Equals(pivot.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase));
        sheet.Charts.Clear();
        sheet.Sparklines.Clear();

        for (uint row = 1; row <= 12; row++)
        {
            for (uint col = 1; col <= 10; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        SeedInsertTablesChartsTourSourceData(sheet);

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        SetSelectionRange(sourceRange, sourceRange.Start);
        EnsureCellVisible(sourceRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new InsertTablesChartsTourContext(
            sheet,
            sourceRange,
            new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 8, 8)),
            new CellAddress(sheet.Id, 2, 4),
            "TableStyleMedium2");
    }

    private static void SeedInsertTablesChartsTourSourceData(Sheet sheet)
    {
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Q1")),
            (1, 3, new TextValue("Q2")),
            (1, 4, new TextValue("Trend")),
            (2, 1, new TextValue("North")),
            (2, 2, new NumberValue(1280)),
            (2, 3, new NumberValue(1510)),
            (3, 1, new TextValue("South")),
            (3, 2, new NumberValue(960)),
            (3, 3, new NumberValue(1120)),
            (4, 1, new TextValue("West")),
            (4, 2, new NumberValue(1140)),
            (4, 3, new NumberValue(1030)),
            (5, 1, new TextValue("East")),
            (5, 2, new NumberValue(1410)),
            (5, 3, new NumberValue(1680))
        };

        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
    }

    private void CreateTourStructuredTable(InsertTablesChartsTourContext context)
    {
        if (FindScreenshotTourTable(context.Sheet) is not null)
            return;

        if (!TableStyleGalleryPlanner.TryGetOption(context.TableStyleName, _workbook.Theme, out var option))
            option = TableStyleGalleryPlanner.GetOption(0, _workbook.Theme);

        if (!TryExecuteCommand(
                new CreateStyledStructuredTableCommand(
                    context.Sheet.Id,
                    context.SourceRange,
                    context.TableStyleName,
                    firstRowHasHeaders: true,
                    option.Banding),
                "Create Table",
                out var outcome))
        {
            throw new InvalidOperationException(outcome.ErrorMessage ?? "Insert tables/charts tour could not create the structured table.");
        }

        if (!context.Sheet.StructuredTables.Any(table => table.Range.Equals(context.SourceRange)))
            throw new InvalidOperationException("Insert tables/charts tour created no structured table on the planned source range.");
    }

    private void CreateTourChart(InsertTablesChartsTourContext context)
    {
        if (context.Sheet.Charts.Count > 0)
            return;

        if (!TryExecuteCommand(
                new AddChartCommand(
                    context.Sheet.Id,
                    context.SourceRange,
                    ChartType.Column,
                    "Quarterly Sales",
                    left: 430,
                    top: 150,
                    width: 440,
                    height: 270),
                "Insert Chart",
                out var outcome))
        {
            throw new InvalidOperationException(outcome.ErrorMessage ?? "Insert tables/charts tour could not create the chart.");
        }
    }

    private void CreateTourSparklines(InsertTablesChartsTourContext context)
    {
        if (context.Sheet.Sparklines.Count > 0)
            return;

        var sparklineSpecs = new[]
        {
            (Row: 2u, Kind: SparklineKind.Line),
            (Row: 3u, Kind: SparklineKind.Column),
            (Row: 4u, Kind: SparklineKind.WinLoss)
        };

        foreach (var (row, kind) in sparklineSpecs)
        {
            var dataRange = new GridRange(new CellAddress(context.Sheet.Id, row, 2), new CellAddress(context.Sheet.Id, row, 3));
            var location = new CellAddress(context.Sheet.Id, row, 4);
            if (!TryExecuteCommand(
                    new AddSparklineCommand(context.Sheet.Id, dataRange, location, kind),
                    "Insert Sparkline",
                    out var outcome))
            {
                throw new InvalidOperationException(outcome.ErrorMessage ?? "Insert tables/charts tour could not create a sparkline.");
            }
        }
    }

    private static async Task ShowInsertTablesChartsTourDialogAsync(Window dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Show();
        dialog.Activate();
        dialog.UpdateLayout();
        await Task.Delay(450);
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void CloseInsertTablesChartsTourDialog(Window dialog)
    {
        if (dialog.IsVisible)
            dialog.Close();
    }

    private async Task<InsertTablesChartsTourManifestCapture> CaptureInsertTablesChartsDialogAsync(
        Window dialog,
        string outputDir,
        string catalogId,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        dialog.UpdateLayout();
        await Task.Delay(250);
        await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        return CreateInsertTablesChartsCapture(
            catalogId,
            state,
            surface,
            fileName,
            "RenderTargetBitmap-insert-dialog-window",
            dialog.ActualWidth,
            dialog.ActualHeight,
            evidenceSummary);
    }

    private async Task<InsertTablesChartsTourManifestCapture> CaptureInsertTablesChartsWindowStateAsync(
        string outputDir,
        string catalogId,
        string state,
        string surface,
        string fileName,
        string evidenceSummary)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateInsertTablesChartsCapture(
            catalogId,
            state,
            surface,
            fileName,
            "RenderTargetBitmap-window",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary);
    }

    private InsertTablesChartsTourManifestCapture CreateInsertTablesChartsCapture(
        string catalogId,
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidenceSummary)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet();
        return new InsertTablesChartsTourManifestCapture(
            CaptureKey: $"insert-tables-charts:{state}",
            PairKey: $"interactive:insert-tables-charts:{state}",
            CatalogId: catalogId,
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            StructuredTableCount: sheet?.StructuredTables.Count ?? 0,
            PivotTableCount: sheet?.PivotTables.Count ?? 0,
            ChartCount: sheet?.Charts.Count ?? 0,
            SparklineCount: sheet?.Sparklines.Count ?? 0,
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteInsertTablesChartsTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_insert_tables_charts_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, InsertTablesChartsTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateInsertTablesChartsTourEvidence(
        string outputDir,
        IReadOnlyList<InsertTablesChartsTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Insert tables/charts tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private async Task CaptureKeyTipOverlayTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteKeyTipOverlayTourEvidence(outputDir);

        var captures = new List<KeyTipOverlayTourManifestCapture>();

        try
        {
            await ApplyScreenshotTourWidthAsync(new RibbonScreenshotTourWidth("1100", 1100));
            await CaptureKeyTipOverlayWindowStateAsync(
                outputDir,
                captures,
                "top-level-tabs-qat",
                "top-level",
                "Top-level Alt/F10 mode with top-level tab and QAT badges.",
                () => EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel));

            await CaptureKeyTipOverlayWindowStateAsync(
                outputDir,
                captures,
                "home-visible-commands",
                "commands",
                "Home command scope with visible command badges, including combo box and dropdown-command placements.",
                () =>
                {
                    SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
                    EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
                });

            await CaptureKeyTipOverlayMenuStateAsync(outputDir, captures);

            await ApplyScreenshotTourWidthAsync(new RibbonScreenshotTourWidth("750", 750));
            await CaptureKeyTipOverlayWindowStateAsync(
                outputDir,
                captures,
                "narrow-home-collapsed-commands",
                "commands",
                "Narrow Home command scope with generated collapsed-group keytip badges.",
                () =>
                {
                    SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
                    EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
                },
                requireCollapsedGroupBadges: true);

            ValidateKeyTipOverlayTourEvidence(outputDir, captures);
            await WriteKeyTipOverlayTourManifestAsync(outputDir, captures);
        }
        catch
        {
            DeleteKeyTipOverlayTourEvidence(outputDir);
            throw;
        }
        finally
        {
            ExitRibbonKeyTipMode();
        }
    }

    private async Task CaptureKeyTipOverlayWindowStateAsync(
        string outputDir,
        List<KeyTipOverlayTourManifestCapture> captures,
        string fileName,
        string scope,
        string stateDescription,
        Action prepareState,
        bool requireCollapsedGroupBadges = false)
    {
        ExitRibbonKeyTipMode();
        prepareState();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(350);
        UpdateLayout();

        var badgeCount = KeyTipOverlay.Children.OfType<Border>().Count();
        var collapsedBadgeCount = string.Equals(scope, "commands", StringComparison.Ordinal)
            ? GetVisibleKeyTipElements(RibbonKeyTipScope.Commands).Count(RibbonMetadata.IsCollapsedGroupButton)
            : 0;
        if (badgeCount == 0)
            throw new InvalidOperationException($"Keytip overlay tour state '{fileName}' produced no badges.");
        if (requireCollapsedGroupBadges && collapsedBadgeCount == 0)
            throw new InvalidOperationException($"Keytip overlay tour state '{fileName}' did not expose any collapsed-group badges.");

        await CaptureCurrentWindowAsync(outputDir, fileName, ScreenshotTourCaptureHeight);
        captures.Add(new KeyTipOverlayTourManifestCapture(
            CaptureKey: $"keytip-overlay:{scope}:{fileName}",
            State: fileName,
            Scope: scope,
            Description: stateDescription,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-top-band",
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: ScreenshotTourCaptureHeight,
            BadgeCount: badgeCount,
            CollapsedGroupBadgeCount: collapsedBadgeCount,
            MenuItemKeyTipCount: 0,
            IsInProcess: true,
            IsForegroundGuarded: !IsScreenshotTourBackgroundRenderAllowed()));
    }

    private async Task CaptureKeyTipOverlayMenuStateAsync(
        string outputDir,
        List<KeyTipOverlayTourManifestCapture> captures)
    {
        ExitRibbonKeyTipMode();
        await ApplyScreenshotTourWidthAsync(new RibbonScreenshotTourWidth("1100", 1100));
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);
        HandleActiveRibbonKeyTip(Key.H);
        HandleActiveRibbonKeyTip(Key.B);
        await Task.Delay(350);
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        var menu = _activeRibbonKeyTipMenu
            ?? throw new InvalidOperationException("Keytip overlay tour could not open Home > Borders menu with Alt,H,B.");
        menu.UpdateLayout();
        var menuKeyTipCount = GetEnabledMenuItems(menu)
            .Count(item => !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item)));

        await CaptureElementAsync(menu, outputDir, "home-borders-menu-scope");
        captures.Add(new KeyTipOverlayTourManifestCapture(
            CaptureKey: "keytip-overlay:menu:home-borders-menu-scope",
            State: "home-borders-menu-scope",
            Scope: "menu",
            Description: "Home Borders dropdown opened through keytip routing; menu item keytips are rendered as scoped input gesture text.",
            FileName: "home-borders-menu-scope",
            OutputFileName: "home-borders-menu-scope.png",
            CaptureMethod: "RenderTargetBitmap-context-menu",
            CaptureLogicalWidth: menu.ActualWidth,
            CaptureLogicalHeight: menu.ActualHeight,
            BadgeCount: 0,
            CollapsedGroupBadgeCount: 0,
            MenuItemKeyTipCount: menuKeyTipCount,
            IsInProcess: true,
            IsForegroundGuarded: false));

        HandleActiveRibbonKeyTip(Key.C);
        await Task.Delay(350);
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        var submenuChild = FindOpenPopupChild(menu)
            ?? throw new InvalidOperationException("Keytip overlay tour could not locate the open Borders > Line Color submenu popup.");
        var activeItemsControl = _activeRibbonKeyTipItemsControl
            ?? throw new InvalidOperationException("Keytip overlay tour did not retain the nested menu keytip scope.");
        var nestedKeyTipCount = GetEnabledMenuItems(activeItemsControl)
            .Count(item => !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item)));

        await CaptureElementAsync(submenuChild, outputDir, "home-borders-line-color-submenu-scope");
        captures.Add(new KeyTipOverlayTourManifestCapture(
            CaptureKey: "keytip-overlay:menu:home-borders-line-color-submenu-scope",
            State: "home-borders-line-color-submenu-scope",
            Scope: "nested-menu",
            Description: "Home Borders > Line Color submenu opened through keytip routing after Alt,H,B,C.",
            FileName: "home-borders-line-color-submenu-scope",
            OutputFileName: "home-borders-line-color-submenu-scope.png",
            CaptureMethod: "RenderTargetBitmap-menu-popup-child",
            CaptureLogicalWidth: submenuChild.ActualWidth,
            CaptureLogicalHeight: submenuChild.ActualHeight,
            BadgeCount: 0,
            CollapsedGroupBadgeCount: 0,
            MenuItemKeyTipCount: nestedKeyTipCount,
            IsInProcess: true,
            IsForegroundGuarded: false));
    }

    private static void DeleteKeyTipOverlayTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, KeyTipOverlayTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateKeyTipOverlayTourEvidence(
        string outputDir,
        IReadOnlyList<KeyTipOverlayTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Keytip overlay tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task CaptureRibbonTourAsync(string outputDir, RibbonScreenshotTourPlan plan)
    {
        DeleteRibbonScreenshotTourEvidence(outputDir, plan);

        try
        {
            await PrepareRibbonScreenshotTourContextAsync(plan.Context);

            if (plan.IsBurst)
            {
                await CaptureRibbonBurstTourAsync(outputDir, plan);
                ValidateRibbonScreenshotTourCaptures(outputDir, plan);
                await WriteRibbonScreenshotTourManifestAsync(outputDir, plan);
                return;
            }

            RibbonScreenshotTourWidth? activeWidth = null;
            foreach (var capture in plan.Captures)
            {
                if (!Equals(activeWidth, capture.Width))
                {
                    await ApplyScreenshotTourWidthAsync(capture.Width);
                    activeWidth = capture.Width;
                }

                await CaptureRibbonTabAsync(outputDir, capture);
            }

            ValidateRibbonScreenshotTourCaptures(outputDir, plan);
            await WriteRibbonScreenshotTourManifestAsync(outputDir, plan);
        }
        catch
        {
            DeleteRibbonScreenshotTourEvidence(outputDir, plan);
            throw;
        }
    }

    private static void DeleteStaleRibbonScreenshotTourCaptures(string outputDir, RibbonScreenshotTourPlan plan)
    {
        foreach (var capture in plan.Captures)
        {
            var path = Path.Combine(outputDir, $"{capture.FileName}.png");
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void DeleteRibbonScreenshotTourEvidence(string outputDir, RibbonScreenshotTourPlan plan)
    {
        DeleteStaleRibbonScreenshotTourCaptures(outputDir, plan);

        var manifestPath = Path.Combine(outputDir, RibbonScreenshotTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateRibbonScreenshotTourCaptures(string outputDir, RibbonScreenshotTourPlan plan)
    {
        var missing = plan.Captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Ribbon screenshot tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private async Task ApplyScreenshotTourWidthAsync(RibbonScreenshotTourWidth width)
    {
        ApplyScreenshotTourWidth(width);

        if (width.WindowWidth is not null)
        {
            await Task.Delay(600);
            return;
        }

        await Task.Delay(1200);
    }

    private async Task CaptureRibbonTabAsync(string outputDir, RibbonScreenshotTourCapture capture)
    {
        PrepareRibbonScreenshotTourTabContext(capture);
        SelectRibbonTourTab(capture.Tab);
        UpdateLayout();
        await Task.Delay(350);
        UpdateLayout();

        await CaptureCurrentWindowAsync(outputDir, capture.FileName, ScreenshotTourCaptureHeight);
    }

    private void PrepareRibbonScreenshotTourTabContext(RibbonScreenshotTourCapture capture)
    {
        switch (capture.Tab.CatalogId)
        {
            case "ShapeFormatTab":
            {
                var context = EnsureDrawObjectFormattingTourContext();
                SelectDrawObjectFormattingShape(context);
                break;
            }
            case "PictureFormatTab":
            {
                var context = EnsureDrawObjectFormattingTourContext();
                SelectDrawObjectFormattingPicture(context);
                break;
            }
        }
    }

    private async Task PrepareRibbonScreenshotTourContextAsync(string? context)
    {
        if (context is null)
            return;

        switch (context)
        {
            case "drawing":
                EnsureDrawObjectFormattingTourContext();
                break;
            case "table":
                EnsureTableDesignScreenshotTourContext();
                break;
            case "pivot":
                EnsurePivotTableScreenshotTourContext();
                break;
            case "chart":
                EnsureChartScreenshotTourContext();
                break;
            default:
                throw new InvalidOperationException($"Unknown ribbon screenshot tour context '{context}'.");
        }

        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
    }

    private void EnsureTableDesignScreenshotTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet();
        if (sheet is null)
            return;

        var headers = new[] { "Region", "Product", "Sales" };
        var rows = new[]
        {
            new object[] { "North", "Coffee", 1280d },
            new object[] { "South", "Tea", 960d },
            new object[] { "West", "Cocoa", 1140d }
        };

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                if (rows[row][col] is double number)
                    sheet.SetCell(address, new NumberValue(number));
                else
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
            }
        }

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        var table = FindScreenshotTourTable(sheet);
        if (table is null)
        {
            table = new StructuredTableModel
            {
                Id = sheet.StructuredTables.Count == 0 ? 1 : sheet.StructuredTables.Max(candidate => candidate.Id) + 1,
                Name = ScreenshotTourTableName,
                DisplayName = ScreenshotTourTableName,
                Range = range,
                HasAutoFilter = true,
                HeaderRowCount = 1,
                StyleName = "TableStyleMedium2",
                ShowRowStripes = true
            };

            for (var index = 0; index < headers.Length; index++)
                table.Columns.Add(new StructuredTableColumnModel(index + 1, headers[index]));

            sheet.StructuredTables.Add(table);
        }

        if (SheetGrid is not null)
            SheetGrid.SelectedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2));
    }

    private void EnsurePivotTableScreenshotTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet();
        if (sheet is null)
            return;

        _currentSheetId = sheet.Id;
        var headers = new[] { "Region", "Product", "Sales" };
        var rows = new[]
        {
            new object[] { "North", "Coffee", 1280d },
            new object[] { "North", "Tea", 760d },
            new object[] { "South", "Coffee", 960d },
            new object[] { "West", "Cocoa", 1140d }
        };

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                if (rows[row][col] is double number)
                    sheet.SetCell(address, new NumberValue(number));
                else
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
            }
        }

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var pivotTable = FindScreenshotTourPivotTable(sheet);
        if (pivotTable is null)
        {
            var targetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 8, 8));
            var command = new AddPivotTableCommand(
                sheet.Id,
                sourceRange,
                targetRange,
                ScreenshotTourPivotTableName,
                rowFieldIndexes: [0],
                dataFieldIndexes: [2]);

            if (!TryExecuteCommand(command, "Insert PivotTable", out var outcome))
                throw new InvalidOperationException(outcome.ErrorMessage ?? "PivotTable screenshot tour setup failed.");

            pivotTable = FindScreenshotTourPivotTable(sheet);
        }

        if (pivotTable is not null && SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start);
            RefreshPivotFieldListPane();
        }
    }

    private void EnsureChartScreenshotTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet();
        if (sheet is null)
            return;

        _currentSheetId = sheet.Id;
        var headers = new[] { "Month", "North", "South" };
        var rows = new[]
        {
            new object[] { "Jan", 1280d, 940d },
            new object[] { "Feb", 1460d, 1020d },
            new object[] { "Mar", 1325d, 1180d },
            new object[] { "Apr", 1580d, 1210d }
        };

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                if (rows[row][col] is double number)
                    sheet.SetCell(address, new NumberValue(number));
                else
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
            }
        }

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var chart = FindScreenshotTourChart(sheet);
        if (chart is null)
        {
            if (!TryExecuteCommand(
                    new AddChartCommand(sheet.Id, sourceRange, ChartType.Column, ScreenshotTourChartName),
                    "Insert Chart",
                    out var outcome))
            {
                throw new InvalidOperationException(outcome.ErrorMessage ?? "Chart screenshot tour setup failed.");
            }

            chart = FindScreenshotTourChart(sheet);
        }

        if (chart is not null && SheetGrid is not null)
        {
            SheetGrid.SelectedRange = new GridRange(sourceRange.Start, sourceRange.Start);
            SheetGrid.SelectedObjectId = chart.Id;
            SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart;
        }
    }

    private Sheet? GetCurrentOrFirstScreenshotTourSheet()
    {
        var currentSheet = _workbook.GetSheet(_currentSheetId);
        if (currentSheet is not null)
            return currentSheet;

        foreach (var sheet in _workbook.Sheets)
            return sheet;

        return null;
    }

    private static StructuredTableModel? FindScreenshotTourTable(Sheet sheet)
    {
        foreach (var table in sheet.StructuredTables)
        {
            if (string.Equals(table.Name, ScreenshotTourTableName, StringComparison.OrdinalIgnoreCase))
                return table;
        }

        return null;
    }

    private static PivotTableModel? FindScreenshotTourPivotTable(Sheet sheet)
    {
        foreach (var pivotTable in sheet.PivotTables)
        {
            if (string.Equals(pivotTable.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase))
                return pivotTable;
        }

        return null;
    }

    private static ChartModel? FindScreenshotTourChart(Sheet sheet)
    {
        foreach (var chart in sheet.Charts)
        {
            if (string.Equals(chart.Title, ScreenshotTourChartName, StringComparison.OrdinalIgnoreCase))
                return chart;
        }

        return null;
    }

    private async Task CaptureRibbonBurstTourAsync(string outputDir, RibbonScreenshotTourPlan plan)
    {
        foreach (var width in plan.Widths)
        {
            ApplyScreenshotTourWidth(width);

            foreach (var tab in plan.Tabs)
            {
                SelectRibbonTourTab(tab);

                foreach (var phase in plan.Phases)
                {
                    await PrepareRibbonBurstCapturePhaseAsync(phase);
                    var capture = new RibbonScreenshotTourCapture(tab, width, phase);
                    await CaptureCurrentWindowAsync(outputDir, capture.FileName, ScreenshotTourCaptureHeight);
                }
            }
        }
    }

    private void ApplyScreenshotTourWidth(RibbonScreenshotTourWidth width)
    {
        if (width.WindowWidth is { } windowWidth)
        {
            WindowState = WindowState.Normal;
            Width = windowWidth;
            Height = 768;
            return;
        }

        WindowState = WindowState.Maximized;
    }

    private void SelectRibbonTourTab(RibbonScreenshotTourTab tab)
    {
        var tabItem = FindRibbonTourTab(tab);

        if (tabItem is null)
            throw new InvalidOperationException(
                $"Ribbon screenshot tour expected tab '{tab.Header}' ({tab.CatalogId}) but it was not found in the live ribbon.");

        RibbonTabs.SelectedItem = tabItem;
    }

    private TabItem? FindRibbonTourTab(RibbonScreenshotTourTab tab)
    {
        foreach (var item in RibbonTabs.Items)
        {
            if (item is TabItem tabItem &&
                RibbonMetadata.TryGetCatalogId(tabItem, out var catalogId) &&
                string.Equals(catalogId, tab.CatalogId, StringComparison.Ordinal))
                return tabItem;
        }

        foreach (var item in RibbonTabs.Items)
        {
            if (item is TabItem tabItem &&
                string.Equals(tabItem.Header?.ToString(), tab.Header, StringComparison.Ordinal))
                return tabItem;
        }

        return null;
    }

    private async Task PrepareRibbonBurstCapturePhaseAsync(RibbonScreenshotTourPhase phase)
    {
        switch (phase.Label)
        {
            case "immediate":
                UpdateLayout();
                return;
            case "first-render":
                await WaitForRibbonScreenshotRenderPassAsync();
                return;
            case "settled":
                await Task.Delay(350);
                UpdateLayout();
                await WaitForRibbonScreenshotRenderPassAsync();
                return;
            default:
                throw new InvalidOperationException($"Unknown ribbon screenshot tour burst phase '{phase.Label}'.");
        }
    }

    private async Task WaitForRibbonScreenshotRenderPassAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private async Task CaptureCurrentWindowAsync(string outputDir, string fileName, double logicalHeight)
    {
        await EnsureWindowForegroundForScreenshotTourAsync($"capturing {fileName}.png");

        // Render/crop/encode/write are the shared, app-neutral primitives
        // (Free.Shared.Ribbon.Wpf.ScreenshotCapture); the foreground-focus guards stay FreeX-specific.
        AssertWindowForegroundForScreenshotTour($"rendering {fileName}.png");
        AssertWindowForegroundForScreenshotTour($"saving {fileName}.png");
        await ScreenshotCapture.CaptureVisualToPngAsync(this, outputDir, fileName, logicalHeight);
    }

    private async Task EnsureWindowForegroundForScreenshotTourAsync(string operation)
    {
        if (IsScreenshotTourBackgroundRenderAllowed())
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            return;
        }

        Activate();
        Focus();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        AssertWindowForegroundForScreenshotTour(operation);
    }

    private static async Task EnsureWindowForegroundForScreenshotTourAsync(Window window, string operation)
    {
        if (IsScreenshotTourBackgroundRenderAllowed())
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            return;
        }

        window.Activate();
        window.Focus();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        AssertWindowForegroundForScreenshotTour(window, operation);
    }

    private void AssertWindowForegroundForScreenshotTour(string operation)
    {
        if (IsScreenshotTourBackgroundRenderAllowed())
            return;

        var expectedWindowHandle = new WindowInteropHelper(this).Handle;
        var foregroundWindowHandle = GetForegroundWindow();
        if (expectedWindowHandle == IntPtr.Zero ||
            foregroundWindowHandle != expectedWindowHandle ||
            !IsActive)
        {
            throw new InvalidOperationException(
                $"Screenshot tour blocked: FreeX main window must own foreground focus before {operation}; " +
                $"foreground handle 0x{foregroundWindowHandle.ToInt64():X}, expected 0x{expectedWindowHandle.ToInt64():X}.");
        }
    }

    private static void AssertWindowForegroundForScreenshotTour(Window window, string operation)
    {
        if (IsScreenshotTourBackgroundRenderAllowed())
            return;

        var expectedWindowHandle = new WindowInteropHelper(window).Handle;
        var foregroundWindowHandle = GetForegroundWindow();
        if (expectedWindowHandle == IntPtr.Zero ||
            foregroundWindowHandle != expectedWindowHandle ||
            !window.IsActive)
        {
            throw new InvalidOperationException(
                $"Screenshot tour blocked: expected WPF window must own foreground focus before {operation}; " +
                $"foreground handle 0x{foregroundWindowHandle.ToInt64():X}, expected 0x{expectedWindowHandle.ToInt64():X}.");
        }
    }

    private static bool IsScreenshotTourBackgroundRenderAllowed() =>
        Environment.GetEnvironmentVariable(ScreenshotTourAllowBackgroundRenderEnvVar) == "1";

    private static async Task WriteRibbonScreenshotTourManifestAsync(string outputDir, RibbonScreenshotTourPlan plan)
    {
        var manifest = new RibbonScreenshotTourManifest(
            Tool: "FREEX_SS_TOUR",
            EvidenceFamily: "ribbon",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            OutputDirectory: outputDir,
            OutputNaming: "<WidthLabel>_<RibbonTab>[_<Phase>].png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            Context: plan.Context,
            BurstMode: plan.IsBurst,
            CaptureLogicalHeight: ScreenshotTourCaptureHeight,
            PlannedCaptureCount: plan.Captures.Count,
            ActualCaptureCount: plan.Captures.Count,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-window-top-band",
            Pairing: new RibbonScreenshotTourManifestPairing(
                "ribbon:<WidthLabel>:<TabFileName>",
                "excel",
                "screenshot_excel.ps1",
                "excel_<WidthLabel>_<RibbonTab>.png"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture without OS foreground ownership; no global mouse, keyboard, or screen capture input is used."
                    : "Abort and clear current PNG/manifest evidence unless the FreeX main window owns foreground focus immediately before render and file write."),
            Tabs: plan.Tabs.Select(tab => tab.Header).ToArray(),
            Widths: plan.Widths
                .Select(width => new RibbonScreenshotTourManifestWidth(
                    width.Label,
                    width.WindowWidth,
                    width.EvidencePurpose()))
                .ToArray(),
            Phases: plan.Phases
                .Select(phase => new RibbonScreenshotTourManifestPhase(phase.Label, phase.FileNameSuffix))
                .ToArray(),
            Captures: plan.Captures
                .Select(capture => new RibbonScreenshotTourManifestCapture(
                    capture.CaptureKey,
                    capture.PairKey,
                    capture.Tab.Header,
                    capture.Tab.FileName,
                    capture.Width.Label,
                    capture.Phase.Label,
                    capture.FileName,
                    capture.OutputFileName,
                    capture.CounterpartFileName))
                .ToArray(),
            Limitations:
            [
                "Ribbon captures cover the top window band only.",
                "Transient popups, dropdowns, native dialogs, and context menus require separate guarded captures.",
                "This in-app tour deletes only the currently requested plan's expected PNG files before capture.",
                IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 was used for in-process rendering; pair with foreground-guarded screen captures when validating OS compositing or input focus."
                    : "The in-app tour aborts before file write unless the FreeX main window owns foreground focus."
            ]);

        var path = Path.Combine(outputDir, RibbonScreenshotTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.RibbonScreenshotTourManifest);
    }

    private static async Task WriteAutoFilterFlyoutTourManifestAsync(
        string outputDir,
        AutoFilterDialog dialog,
        AutoFilterDropdownPlan plan)
    {
        var capture = new AutoFilterFlyoutTourManifestCapture(
            CaptureKey: "interactive:table-autofilter-dropdown:opened",
            PairKey: "interactive:table-autofilter-dropdown:opened",
            ScenarioId: "popup:table-autofilter-dropdown",
            State: "opened",
            FileName: AutoFilterFlyoutTourCaptureFileName,
            OutputFileName: $"{AutoFilterFlyoutTourCaptureFileName}.png",
            CounterpartFileName: "interactive_table_autofilter_dropdown_opened.png",
            CaptureLogicalWidth: dialog.ActualWidth,
            CaptureLogicalHeight: dialog.ActualHeight);

        var manifest = new AutoFilterFlyoutTourManifest(
            Tool: "FREEX_AUTOFILTER_FLYOUT_TOUR",
            EvidenceFamily: "popup",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "popup:table-autofilter-dropdown",
            OutputDirectory: outputDir,
            OutputNaming: "freex_table_autofilter_dropdown.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            HeaderCell: plan.Range.Start.ToA1(),
            HeaderText: "score",
            AutoFilterRange: plan.Range.ToString(),
            FilterColumnOffset: plan.FilterColumnOffset,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-autofilter-flyout-window",
            Pairing: new AutoFilterFlyoutTourManifestPairing(
                "interactive:table-autofilter-dropdown:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_table_autofilter_dropdown_opened.png"),
            Captures: [capture],
            Limitations:
            [
                "This in-app tour captures the actual FreeX AutoFilter flyout window without global mouse or keyboard input.",
                "The paired Microsoft Excel transient capture is declared by tools/screenshot_excel.ps1 and remains a separate foreground-guarded capture.",
                "The scenario opens the worksheet AutoFilter dropdown for the score header against numeric values 1-4 plus a blank row."
            ]);

        var path = Path.Combine(outputDir, AutoFilterFlyoutTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.AutoFilterFlyoutTourManifest);
    }

    private static async Task WriteHomeNumberFormatDropdownTourManifestAsync(string outputDir, FrameworkElement popupChild)
    {
        var capture = new HomeNumberFormatDropdownTourManifestCapture(
            CaptureKey: "interactive:home-number-format:opened",
            PairKey: "interactive:home-number-format:opened",
            ScenarioId: "dropdown:home-number-format",
            State: "opened",
            FileName: HomeNumberFormatDropdownTourCaptureFileName,
            OutputFileName: $"{HomeNumberFormatDropdownTourCaptureFileName}.png",
            CounterpartFileName: "interactive_home_number_format_opened.png",
            CaptureLogicalWidth: popupChild.ActualWidth,
            CaptureLogicalHeight: popupChild.ActualHeight);

        var manifest = new HomeNumberFormatDropdownTourManifest(
            Tool: "FREEX_HOME_NUMBER_FORMAT_DROPDOWN_TOUR",
            EvidenceFamily: "dropdown",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "dropdown:home-number-format",
            OutputDirectory: outputDir,
            OutputNaming: "freex_dropdown_home_number_format_opened.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SelectedCell: "A1",
            SelectedFormat: HomeNumberFormatDropdownPlanner.Options[HomeNumberFormatDropdownPlanner.DefaultSelectionIndex].Label,
            OptionLabels: HomeNumberFormatDropdownPlanner.Options.Select(option => option.Label).ToArray(),
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-combobox-popup-child",
            Pairing: new HomeNumberFormatDropdownTourManifestPairing(
                "interactive:home-number-format:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_home_number_format_opened.png"),
            Captures: [capture],
            Limitations:
            [
                "This in-app tour opens the production Home Number Format ComboBox and captures the open WPF popup child without global mouse or keyboard input.",
                "The paired Microsoft Excel transient capture is declared by tools/screenshot_excel.ps1 and remains a separate foreground-guarded capture.",
                "The scenario captures the opened dropdown with the default General format selected."
            ]);

        var path = Path.Combine(outputDir, HomeNumberFormatDropdownTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeNumberFormatDropdownTourManifest);
    }

    private static async Task WriteHomeAlignmentNumberTourManifestAsync(
        string outputDir,
        HomeAlignmentNumberTourContext context,
        IReadOnlyList<HomeAlignmentNumberTourManifestCapture> captures)
    {
        var manifest = new HomeAlignmentNumberTourManifest(
            Tool: "FREEX_HOME_ALIGNMENT_NUMBER_TOUR",
            EvidenceFamily: "home-ribbon",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "home:alignment-number",
            OutputDirectory: outputDir,
            OutputNaming: "freex_home_alignment_*.png, freex_home_number_*.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SheetName: context.SheetName,
            AlignmentRange: context.AlignmentRange.ToString(),
            NumberRange: context.NumberRange.ToString(),
            SampleFormats: context.SampleFormats,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-main-window-context-menu-and-dialogs",
            Pairing: new HomeAlignmentNumberTourManifestPairing(
                "interactive:home-alignment-number:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_home_alignment_number_<state>.png"),
            Captures: captures,
            Limitations:
            [
                "This in-app tour seeds worksheet cells, executes the production FreeX style command path, and captures WPF output with RenderTargetBitmap.",
                "The paired Microsoft Excel transient captures remain a separate foreground-guarded capture set.",
                "The tour covers visible Home Alignment and Number group command rendering, Orientation menu shape, and Format Cells Alignment/Number entry states; save/reload and locale-specific number-format fidelity remain follow-up verification."
            ]);

        var path = Path.Combine(outputDir, HomeAlignmentNumberTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeAlignmentNumberTourManifest);
    }

    private static async Task WriteWorksheetContextMenuTourManifestAsync(
        string outputDir,
        ContextMenu menu,
        CellAddress address)
    {
        var menuHeaders = menu.Items
            .OfType<MenuItem>()
            .Select(item => item.Header?.ToString() ?? string.Empty)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToArray();

        var capture = new WorksheetContextMenuTourManifestCapture(
            CaptureKey: "interactive:worksheet-cell-context-menu:opened",
            PairKey: "interactive:worksheet-cell-context-menu:opened",
            ScenarioId: "context-menu:worksheet-cell",
            State: "opened",
            FileName: WorksheetContextMenuTourCaptureFileName,
            OutputFileName: $"{WorksheetContextMenuTourCaptureFileName}.png",
            CounterpartFileName: "interactive_worksheet_cell_context_menu_opened.png",
            CaptureLogicalWidth: menu.ActualWidth,
            CaptureLogicalHeight: menu.ActualHeight);

        var manifest = new WorksheetContextMenuTourManifest(
            Tool: "FREEX_WORKSHEET_CONTEXT_MENU_TOUR",
            EvidenceFamily: "context-menu",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "context-menu:worksheet-cell",
            OutputDirectory: outputDir,
            OutputNaming: "freex_context_menu_worksheet_cell_opened.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SelectedCell: address.ToA1(),
            EntryPath: "keyboard-context-menu-point",
            MenuHeaders: menuHeaders,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-worksheet-context-menu",
            Pairing: new WorksheetContextMenuTourManifestPairing(
                "interactive:worksheet-cell-context-menu:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_worksheet_cell_context_menu_opened.png"),
            Captures: [capture],
            Limitations:
            [
                "This in-app tour opens the production worksheet-cell ContextMenu and captures the live WPF menu without global mouse or keyboard input.",
                "The paired Microsoft Excel transient capture remains a separate foreground-guarded capture.",
                "The scenario captures the default worksheet-cell context menu for A1."
            ]);

        var path = Path.Combine(outputDir, WorksheetContextMenuTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.WorksheetContextMenuTourManifest);
    }

    private static async Task WriteHomeBordersDropdownTourManifestAsync(string outputDir, ContextMenu menu)
    {
        var menuHeaders = menu.Items
            .OfType<MenuItem>()
            .Select(item => item.Header?.ToString() ?? string.Empty)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToArray();

        var capture = new HomeBordersDropdownTourManifestCapture(
            CaptureKey: "interactive:home-borders:opened",
            PairKey: "interactive:home-borders:opened",
            ScenarioId: "dropdown:home-borders",
            State: "opened",
            FileName: HomeBordersDropdownTourCaptureFileName,
            OutputFileName: $"{HomeBordersDropdownTourCaptureFileName}.png",
            CounterpartFileName: "interactive_home_borders_opened.png",
            CaptureLogicalWidth: menu.ActualWidth,
            CaptureLogicalHeight: menu.ActualHeight);

        var manifest = new HomeBordersDropdownTourManifest(
            Tool: "FREEX_HOME_BORDERS_DROPDOWN_TOUR",
            EvidenceFamily: "dropdown",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "dropdown:home-borders",
            OutputDirectory: outputDir,
            OutputNaming: "freex_dropdown_home_borders_opened.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            EntryPath: "Home > Borders",
            MenuHeaders: menuHeaders,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-context-menu",
            Pairing: new HomeBordersDropdownTourManifestPairing(
                "interactive:home-borders:<State>",
                "excel",
                "screenshot_excel.ps1",
                "interactive_home_borders_opened.png"),
            Captures: [capture],
            Limitations:
            [
                "This in-app tour opens the production Home Borders menu and captures the live WPF ContextMenu without global mouse or keyboard input.",
                "The paired Microsoft Excel transient capture remains a separate foreground-guarded capture.",
                "The scenario captures the top-level Borders menu; nested Line Color and Line Style submenus are separate future captures."
            ]);

        var path = Path.Combine(outputDir, HomeBordersDropdownTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeBordersDropdownTourManifest);
    }

    private static async Task WriteHomeFontColorsTourManifestAsync(
        string outputDir,
        GridRange sampleRange,
        IReadOnlyList<HomeFontColorsTourManifestCapture> captures)
    {
        var manifest = new HomeFontColorsTourManifest(
            Tool: "FREEX_HOME_FONT_COLORS_TOUR",
            EvidenceFamily: "home-formatting",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "UI-CAT-HOME-002A-M",
            OutputDirectory: outputDir,
            OutputNaming: "freex_home_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SampleRange: sampleRange.ToString(),
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "RenderTargetBitmap; no global mouse, keyboard, or screen capture input is used"
                : "foreground CopyFromScreen",
            CaptureLogicalHeight: 760,
            PlannedCaptureCount: HomeFontColorsTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            Pairing: new HomeFontColorsTourManifestPairing(
                "interactive:home-font-colors:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? "FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1 permits deterministic in-process WPF rendering; foreground mouse/keytip/input ownership remains a separate gap."
                    : "FreeX main window owns foreground focus for screen captures."),
            CoveredFeatures:
            [
                "font family",
                "font size",
                "grow font",
                "shrink font",
                "bold",
                "italic",
                "underline",
                "double underline",
                "strikethrough",
                "font color",
                "fill color",
                "theme-backed font/fill colors",
                "border presets",
                "full implemented Borders menu",
                "implemented Borders Line Color theme choices"
            ],
            RemainingGaps:
            [
                "foreground mouse/keytip evidence for Home font/color/border commands",
                "Excel-paired Home font/color/border screenshots",
                "full LCID/theme matrix",
                "font/fill color gallery parity beyond the current custom color picker and swatch buttons",
                "persistence breadth across save/reload and native JSON state"
            ],
            Captures: captures);

        var path = Path.Combine(outputDir, HomeFontColorsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeFontColorsTourManifest);
    }

    private static async Task WriteHomeStylesConditionalFormattingTourManifestAsync(
        string outputDir,
        HomeStylesConditionalFormattingTourContext context,
        IReadOnlyList<HomeStylesConditionalFormattingTourManifestCapture> captures)
    {
        var manifest = new HomeStylesConditionalFormattingTourManifest(
            Tool: "FREEX_HOME_STYLES_CF_TOUR",
            EvidenceFamily: "home-styles-conditional-formatting",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "home-styles-cf:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_home_styles_cf_<Surface>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#Home font/alignment/number/styles",
            CatalogCommandRows:
            [
                "UI-CAT-HOME-003A-C",
                "UI-CMD-HOME-STYLES-001",
                "UI-CMD-HOME-STYLES-002",
                "UI-CMD-HOME-STYLES-003"
            ],
            SheetName: context.Sheet.Name,
            ResultRange: context.ResultRange.ToString(),
            TableRange: context.TableRange.ToString(),
            ConditionalFormatRange: context.ConditionalFormatRange.ToString(),
            CellStyleRange: context.CellStyleRange.ToString(),
            TableStyleName: context.TableStyleName,
            ConditionalFormatRuleCount: context.Sheet.ConditionalFormats.Count,
            StructuredTableCount: context.Sheet.StructuredTables.Count,
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "RenderTargetBitmap-in-process"
                : "foreground CopyFromScreen",
            PlannedCaptureCount: HomeStylesConditionalFormattingTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, keytip, range-picker, native dialog, or screen capture input is used."
                    : "Window, menu, and dialog captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            Pairing: new HomeStylesConditionalFormattingTourManifestPairing(
                "interactive:home-styles-cf:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            Captures: captures,
            CoveredStates:
            [
                "Home Styles group grid result with a real structured table, conditional-format rules, and Cell Style preset cells",
                "Conditional Formatting top-level menu",
                "Conditional Formatting Data Bars preset submenu",
                "Conditional Formatting Rules Manager dialog",
                "Format as Table gallery",
                "Cell Styles gallery"
            ],
            Limitations:
            [
                "This bounded tour drives FreeX commands and WPF menus/dialogs in process, then captures them with RenderTargetBitmap.",
                "The tour does not synthesize physical mouse, Alt/keytip, menu keyboard navigation, range-picker collapse/selection, dialog OK/Apply workflows, or foreground CopyFromScreen input.",
                "The Format as Table result uses the direct CreateStyledStructuredTableCommand path instead of clicking a gallery item and submitting the Create Table dialog.",
                "Only representative Conditional Formatting rules are seeded; full highlight/top-bottom/color-scale/icon-set rule taxonomy, edit/duplicate/delete/reorder manager flows, and clear-rules workflows remain separate.",
                "Excel-paired screenshots, save/reload persistence breadth, protected/table/formula target breadth, and LCID/theme matrix coverage remain open."
            ]);

        var path = Path.Combine(outputDir, HomeStylesConditionalFormattingTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeStylesConditionalFormattingTourManifest);
    }

    private static async Task WriteHomeClipboardCellsEditingTourManifestAsync(
        string outputDir,
        HomeClipboardCellsEditingTourContext context,
        IReadOnlyList<HomeClipboardCellsEditingTourManifestCapture> captures)
    {
        var manifest = new HomeClipboardCellsEditingTourManifest(
            Tool: "FREEX_HOME_CLIPBOARD_CELLS_EDITING_TOUR",
            EvidenceFamily: "home-clipboard-cells-editing",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "home-clipboard-cells-editing:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_home_clipboard_cells_editing_<Surface>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-HOME-001",
            CatalogCommandRows:
            [
                "UI-CMD-HOME-CLIP-001",
                "UI-CMD-HOME-CLIP-002",
                "UI-CMD-HOME-CELLS-001",
                "UI-CMD-HOME-CELLS-002",
                "UI-CMD-HOME-CELLS-003",
                "UI-CMD-HOME-CELLS-004",
                "UI-CMD-HOME-EDIT-003",
                "UI-CMD-HOME-EDIT-004"
            ],
            SheetName: context.Sheet.Name,
            CopySourceRange: context.CopySourceRange.ToString(),
            PasteTargetRange: context.PasteTargetRange.ToString(),
            SortRange: context.SortRange.ToString(),
            UsedRange: context.UsedRange.ToString(),
            CaptureStatus: "complete",
            CaptureMode: "RenderTargetBitmap-in-process",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, keytip, OS clipboard, or screen capture input is used."
                    : "Window, menu, and dialog captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            Captures: captures,
            CoveredStates:
            [
                "Home Clipboard copied-source marquee and Paste menu",
                "Home Cells Insert, Delete, and Format menus",
                "Insert Cells and Delete Cells shift-choice dialogs",
                "Home Editing Clear menu",
                "Home Editing Sort & Filter menu and Custom Sort dialog",
                "Home Editing Find & Select menu",
                "Find, Replace, Go To, and Go To Special dialog surfaces"
            ],
            Limitations:
            [
                "This bounded tour opens production FreeX WPF menu and dialog surfaces in process and captures them with RenderTargetBitmap.",
                "The copied-source state is seeded in the FreeX internal clipboard visual model without reading or writing the operating-system clipboard.",
                "The tour does not synthesize physical mouse, keytip, Ctrl+V/Ctrl+F/Ctrl+H/F5, dialog access-key, Enter/Escape, or range-picker input.",
                "The tour avoids submitting destructive insert/delete/clear/sort/replace actions; command mutation, undo/repeat, persistence, and Excel-paired evidence remain separate lanes.",
                "Paste Special, Format Painter persistent/double-click mode, row/column hide/unhide results, filter dropdown criteria, and Selection Pane object workflows are not captured in this slice."
            ]);

        var path = Path.Combine(outputDir, HomeClipboardCellsEditingTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeClipboardCellsEditingTourManifest);
    }

    private static async Task WriteQatUndoRedoTourManifestAsync(
        string outputDir,
        CellAddress address,
        IReadOnlyList<QatUndoRedoTourManifestCapture> captures)
    {
        var manifest = new QatUndoRedoTourManifest(
            Tool: "FREEX_QAT_UNDO_REDO_TOUR",
            EvidenceFamily: "qat",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "qat:undo-redo",
            OutputDirectory: outputDir,
            OutputNaming: "freex_qat_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SelectedCell: address.ToA1(),
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-window-full-and-qat-history-context-menu",
            Pairing: new QatUndoRedoTourManifestPairing(
                "interactive:qat-undo-redo:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            Captures: captures,
            Limitations:
            [
                "This in-app tour drives the real FreeX command bus and Quick Access Toolbar controls, then captures WPF output with RenderTargetBitmap.",
                "The tour does not use global mouse or keyboard input; foreground/live OS-input validation remains separate unless the capture is run without the background-render override.",
                "The edit and style mutation are created by the in-app harness through the same command stack used by routed UI commands, not by physical keyboard text entry.",
                "No Microsoft Excel counterpart capture is produced by this tool."
            ]);

        var path = Path.Combine(outputDir, QatUndoRedoTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.QatUndoRedoTourManifest);
    }

    private static async Task WriteTitlebarWindowChromeTourManifestAsync(
        string outputDir,
        IReadOnlyList<TitlebarWindowChromeTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        var manifest = new TitlebarWindowChromeTourManifest(
            Tool: "FREEX_TITLEBAR_WINDOW_CHROME_TOUR",
            EvidenceFamily: "window-chrome",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "window-chrome:titlebar",
            OutputDirectory: outputDir,
            OutputNaming: "freex_titlebar_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            PlannedCaptureCount: 5,
            ActualCaptureCount: captures.Count,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-window-top-band",
            SavedWorkbookOutputFileName: Path.GetFileName(savedWorkbookPath),
            SavedWorkbookRetained: File.Exists(savedWorkbookPath),
            Pairing: new TitlebarWindowChromeTourManifestPairing(
                "interactive:titlebar-window-chrome:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no global mouse, keyboard, close, minimize, or drag input was used."
                    : "Abort and clear titlebar/window-chrome tour evidence unless the FreeX main window owns foreground focus immediately before render and file write."),
            Captures: captures,
            Limitations:
            [
                "This tour captures real FreeX WPF titlebar/window chrome visuals and changes WindowState directly instead of using global mouse input.",
                "Minimize and Close are not clicked; evidence is limited to visible button/UIA state so the tour cannot lose unsaved work.",
                "Alt+Space/system menu, native titlebar drag, hover styling, and live mouse clicks remain foreground-runner gaps.",
                "The saved/renamed title state is produced through SaveWorkbookToTargetAsync against an XLSX target without opening the native Save As dialog.",
                "No Microsoft Excel counterpart capture is produced by this tool."
            ]);

        var path = Path.Combine(outputDir, TitlebarWindowChromeTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.TitlebarWindowChromeTourManifest);
    }

    private static async Task WriteFormulaBarNameBoxTourManifestAsync(
        string outputDir,
        FormulaBarNameBoxTourContext context,
        IReadOnlyList<FormulaBarNameBoxTourManifestCapture> captures)
    {
        var manifest = new FormulaBarNameBoxTourManifest(
            Tool: "FREEX_FORMULA_BAR_NAME_BOX_TOUR",
            EvidenceFamily: "formula-bar-name-box",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "formula-bar-name-box:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_formula_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            SheetName: context.SheetName,
            NamedRangeName: context.NamedRangeName,
            NamedRangeAddress: context.NamedRangeAddress,
            StartCell: context.StartCell,
            ObjectNames: context.ObjectNames,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-window-full-top-band-dropdown-and-dialog",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no global mouse, keyboard, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX window owns foreground focus for each window/dialog capture."),
            Pairing: new FormulaBarNameBoxTourManifestPairing(
                "interactive:formula-bar-name-box:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            Captures: captures,
            CoveredStates:
            [
                "Name Box displays exact selected defined name",
                "Name Box dropdown opens and lists a named shape, picture, text box, and chart",
                "Name Box dropdown selection navigates to the named range",
                "Formula bar edit mode with Cancel and Enter controls",
                "Cancel restores formula bar text and worksheet focus",
                "Enter commits formula bar edit and returns worksheet focus",
                "Formula bar fx button focus and Insert Function dialog surface",
                "Expanded/collapsed formula bar visual state",
                "Formula bar focus",
                "Top-level keytips while focus starts in the Name Box"
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF output with RenderTargetBitmap rather than OS CopyFromScreen.",
                "The Name Box dropdown is opened through the production ComboBox state, and the Sales dropdown navigation uses the production SelectionChanged path without global mouse input; the open capture includes all four named drawing-object kinds.",
                "The formula-bar Enter and Cancel evidence uses the production button handlers, but button activation is invoked in process rather than by physical mouse input.",
                "The Insert Function dialog capture uses the production InsertFunctionDialog shown by the tour because invoking the fx button's modal handler would block deterministic screenshot capture.",
                "The keytip capture enters the production top-level keytip mode while focus starts in the Name Box; it is not a physical Alt-key foreground input capture.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, FormulaBarNameBoxTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.FormulaBarNameBoxTourManifest);
    }

    private async Task WriteGridSelectionEditingTourManifestAsync(
        string outputDir,
        GridSelectionEditingTourContext context,
        IReadOnlyList<GridSelectionEditingTourManifestCapture> captures)
    {
        var manifest = new GridSelectionEditingTourManifest(
            Tool: "FREEX_GRID_SELECTION_EDITING_TOUR",
            EvidenceFamily: "grid-selection-editing",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "grid-selection-editing:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_grid_selection_editing_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-GRID-001",
            CatalogRows: ["UI-CAT-GRID-001", "UI-CAT-GRID-002"],
            SheetName: context.Sheet.Name,
            SelectedCell: context.SelectedCell.ToA1(),
            SelectedRange: context.SelectedRange.ToString(),
            WholeRowSelection: $"{context.RowSelectionIndex}:{context.RowSelectionIndex}",
            WholeColumnSelection: $"{FormatColumnReference(context.ColumnSelectionIndex)}:{FormatColumnReference(context.ColumnSelectionIndex)}",
            EditCell: context.EditCell.ToA1(),
            FilterVisibleRange: context.FilterVisibleRange.ToString(),
            FillRange: context.FillRange.ToString(),
            ClearRange: context.ClearRange.ToString(),
            FilterHiddenRows: context.FilterHiddenRows,
            ManualHiddenRows: context.ManualHiddenRows,
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, native dialog, range-picker, or screen capture input is used."
                    : "Window captures abort unless the FreeX main window owns foreground focus immediately before render and file write."),
            Pairing: new GridSelectionEditingTourManifestPairing(
                "interactive:grid-selection-editing:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            Captures: captures,
            CoveredStates:
            [
                "Single selected cell with Name Box/formula bar/status agreement",
                "Selected rectangular range with status aggregate text",
                "Whole row selection through the production row-selection helper",
                "Whole column selection through the production column-selection helper",
                "Inline edit mode with visible grid editor chrome/caret and Edit status mode",
                "Committed inline edit value displayed in grid, formula bar, and workbook model",
                "AutoFilter/manual-hidden row visual gap with filtered/manual rows omitted from the viewport",
                "Fill Down result state through FillCellsCommand",
                "Clear Contents result state through ClearContentsCommand"
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF output with RenderTargetBitmap rather than OS CopyFromScreen.",
                "Mouse drag, Shift+click, Ctrl+multi-area, keyboard navigation shortcuts, F2 physical key entry, Enter/Tab/Escape movement, and foreground UIA invocation remain open.",
                "Whole-row and whole-column states are supported; whole-sheet selection and multi-area selection are not covered by this bounded slice.",
                "Filtered/hidden row evidence seeds worksheet AutoFilter and hidden-row model state directly; it does not open the AutoFilter popup or prove filter criteria input.",
                "Fill and clear evidence uses production workbook commands and visual result states, but not physical ribbon/menu activation.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, GridSelectionEditingTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.GridSelectionEditingTourManifest);
    }

    private static async Task WriteStatusFooterTourManifestAsync(
        string outputDir,
        IReadOnlyList<StatusFooterTourManifestCapture> captures)
    {
        var manifest = new StatusFooterTourManifest(
            Tool: "FREEX_STATUS_FOOTER_TOUR",
            EvidenceFamily: "status-footer",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "status-footer:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_status_footer_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new StatusFooterTourManifestPairing(
                "interactive:status-footer:<State>",
                "manual-or-excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? "FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1 was set; no global mouse, keyboard, or screen capture input is used."
                    : "FreeX main window must own foreground focus before each RenderTargetBitmap window capture."),
            Captures: captures,
            Limitations:
            [
                "RenderTargetBitmap evidence only; it is not foreground CopyFromScreen proof.",
                "Zoom slider min/baseline/max are set programmatically through the in-app slider model; live mouse drag remains open.",
                "Ctrl+wheel, foreground mouse, native UIA RangeValue interaction, filtered selections, and multi-range visual stats remain open.",
                "Formula edit visual evidence covers Edit mode text; modal-dialog return and error status transitions remain open."
            ]);

        var path = Path.Combine(outputDir, StatusFooterTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.StatusFooterTourManifest);
    }

    private static async Task WriteInsertObjectsLinksTourManifestAsync(
        string outputDir,
        IReadOnlyList<InsertObjectsLinksTourManifestCapture> captures)
    {
        var manifest = new InsertObjectsLinksTourManifest(
            Tool: "FREEX_INSERT_OBJECTS_LINKS_TOUR",
            EvidenceFamily: "insert-objects-links-text",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "insert:objects-links-text",
            OutputDirectory: outputDir,
            OutputNaming: "freex_insert_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-INSERT-003",
                "UI-CMD-INSERT-008",
                "UI-CMD-INSERT-009",
                "UI-CMD-INSERT-010"
            ],
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new InsertObjectsLinksTourManifestPairing(
                "interactive:insert-objects-links:<State>",
                "manual-or-excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, or screen capture input is used."
                    : "FreeX WPF window/dialog must own foreground focus before RenderTargetBitmap capture."),
            Captures: captures,
            CoveredStates:
            [
                "Insert Hyperlink dialog with address box default focus/select-all behavior",
                "Symbol picker dialog with Symbols tab/grid and Insert/Cancel controls",
                "Model-backed worksheet visuals for hyperlink, rectangle shape, text box, picture placeholder, threaded comment, and note",
                "New Comment in-window inline popup",
                "New Note in-window inline popup",
                "Threaded comments list surface",
                "Notes list surface"
            ],
            Limitations:
            [
                "This tour renders FreeX WPF surfaces in process with RenderTargetBitmap; it is not foreground CopyFromScreen or physical mouse/keytip/UIA proof.",
                "The picture evidence uses the production InsertPictureCommand sizing/fallback path with deterministic placeholder bytes rather than opening the native Windows file picker.",
                "Dialog and inline popup captures show production initial states and focus targets but do not submit hyperlink, symbol, comment, or note editors through keyboard/mouse input.",
                "The inserted worksheet object evidence is applied through command model calls so save/reload persistence and selection-handle drag evidence remain separate.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, InsertObjectsLinksTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.InsertObjectsLinksTourManifest);
    }

    private static async Task WriteViewPanesZoomTourManifestAsync(
        string outputDir,
        IReadOnlyList<ViewPanesZoomTourManifestCapture> captures)
    {
        var manifest = new ViewPanesZoomTourManifest(
            Tool: "FREEX_VIEW_PANES_ZOOM_TOUR",
            EvidenceFamily: "view-panes-zoom",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "view-panes-zoom:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_view_panes_zoom_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-VIEW-001",
                "UI-CAT-VIEW-002",
                "UI-CMD-VIEW-001",
                "UI-CMD-VIEW-002",
                "UI-CMD-VIEW-003",
                "UI-CMD-VIEW-004"
            ],
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new ViewPanesZoomTourManifestPairing(
                "interactive:view-panes-zoom:<State>",
                "manual-or-excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 was set; no global mouse, keyboard, or screen capture input is used."
                    : "FreeX main window must own foreground focus before each RenderTargetBitmap window capture."),
            Captures: captures,
            CoveredStates:
            [
                "View ribbon selected with Normal, Page Layout, and Page Break Preview workbook states.",
                "Show toggles for gridlines, headings, ruler, and formula bar.",
                "Freeze Panes and Split pane model/visual states.",
                "Zoom dialog, View ribbon 100%, and Zoom to Selection states.",
                "Arrange All model state plus menu capture when the View ribbon button is discoverable.",
                "Custom Views dialog opened with a saved custom view when implemented."
            ],
            Limitations:
            [
                "RenderTargetBitmap evidence only; it is not foreground CopyFromScreen proof.",
                "The tour drives production handlers in process rather than physical mouse/keytip/UIA invocation.",
                "Split divider drag, pane scrollbar interaction, Ctrl+wheel zoom, status slider drag, and native UIA RangeValue remain open.",
                "Arrange All evidence records the workbook arrangement state and menu check state; multi-window OS layout proof remains open.",
                "Custom Views evidence opens the production dialog with a saved view, but add/show/delete keyboard and persistence round-trip proof remains open.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, ViewPanesZoomTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.ViewPanesZoomTourManifest);
    }

    private static async Task WritePageLayoutSetupTourManifestAsync(
        string outputDir,
        IReadOnlyList<PageLayoutSetupTourManifestCapture> captures)
    {
        var manifest = new PageLayoutSetupTourManifest(
            Tool: "FREEX_PAGE_LAYOUT_SETUP_TOUR",
            EvidenceFamily: "page-layout-setup",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "page-layout-setup:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_page_layout_setup_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-PAGE-001",
                "UI-CAT-PAGE-001A",
                "UI-CAT-DIALOG-001B",
                "UI-CMD-PAGE-001",
                "UI-CMD-PAGE-002",
                "UI-CMD-PAGE-003",
                "UI-CMD-PAGE-004",
                "UI-CMD-PAGE-005",
                "UI-CMD-PAGE-006",
                "UI-CMD-DRAW-002"
            ],
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new PageLayoutSetupTourManifestPairing(
                "interactive:page-layout-setup:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no foreground mouse, keyboard, native file dialog, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX window/dialog owns foreground focus for each capture."),
            Captures: captures,
            CoveredStates:
            [
                "Page Layout ribbon baseline with Page Setup, Scale to Fit, and Sheet Options groups visible.",
                "Margins, Orientation, Size, Print Area, Breaks, and Background menu surfaces.",
                "Page Setup dialog default Page state plus Page, Margins, Header/Footer, and Sheet tabs, including Print Titles fields.",
                "Scale to Fit field state and Sheet Options print/display checkbox state.",
                "Draw Arrange representative Selection Pane dialog surface."
            ],
            Limitations:
            [
                "RenderTargetBitmap evidence only; it is not foreground CopyFromScreen proof.",
                "The tour drives FreeX in process and captures WPF windows/menus without physical mouse, keyboard, keytip, or UIA invocation.",
                "Background captures the supported menu surface only; the native image picker, image tiling display, replacement, clear foreground proof, and persistence remain open.",
                "Page Setup dialog captures are visual states only; OK/Cancel/Escape/default-button execution, range-picker collapse/restore, Print/Preview/Options actions, and printer options are not executed.",
                "Arrange evidence uses a deterministic representative Selection Pane dialog item list rather than live overlapping drawing objects on the sheet.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, PageLayoutSetupTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.PageLayoutSetupTourManifest);
    }

    private static async Task WriteDrawObjectFormattingTourManifestAsync(
        string outputDir,
        DrawObjectFormattingTourContext context,
        IReadOnlyList<DrawObjectFormattingTourManifestCapture> captures)
    {
        var manifest = new DrawObjectFormattingTourManifest(
            Tool: "FREEX_DRAW_OBJECT_FORMATTING_TOUR",
            EvidenceFamily: "draw-object-formatting",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "draw-object-formatting:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_draw_object_formatting_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-DRAW-001",
                "UI-CAT-DRAW-001A",
                "UI-CAT-DRAW-001B",
                "UI-CAT-DRAW-001C",
                "UI-CMD-DRAW-001",
                "UI-CMD-DRAW-002",
                "UI-CMD-DRAW-003",
                "UI-CMD-DRAW-004",
                "UI-CMD-DRAW-005"
            ],
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new DrawObjectFormattingTourManifestPairing(
                "interactive:draw-object-formatting:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no foreground mouse, keyboard, native file dialog, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX window/dialog owns foreground focus for each capture."),
            SeededObjects:
            [
                $"shape:{context.Shape.Name}:{context.Shape.Anchor.ToA1()}",
                $"picture:{context.Picture.Name}:{context.Picture.Anchor.ToA1()}",
                $"text-box:{context.TextBox.Name}:{context.TextBox.Anchor.ToA1()}"
            ],
            Captures: captures,
            CoveredStates:
            [
                "Draw tab baseline with Arrange and Format groups plus seeded visible shape, picture, and text-box objects.",
                "Shape Fill and Object Outline color picker surfaces.",
                "Shape Gradient and Shape Effects dialogs.",
                "Crop/Reset Crop split-menu surface for a selected picture.",
                "Object Size dialog default numeric-input focus/select-all surface.",
                "Format Picture Size and Alt Text tabs for object size/rotation/crop/alt-text evidence.",
                "Selection Pane list/search/rename/visibility/reorder controls with seeded drawing objects."
            ],
            Limitations:
            [
                "RenderTargetBitmap evidence only; it is not foreground CopyFromScreen proof.",
                "The tour drives FreeX in process and captures WPF windows/menus without physical mouse, keyboard, keytip, drag-handle, or UIA invocation.",
                "Color picker and dialog captures are visual states only; OK/Cancel/Escape, invalid input recovery, and command mutation from those dialogs remain open.",
                "Picture insertion uses deterministic in-process placeholder bytes rather than opening the native Windows file picker.",
                "Selection Pane rename and visibility states are previewed in the dialog before OK/apply; grouped-sheet propagation and persistence breadth remain covered by planner/command tests.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, DrawObjectFormattingTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.DrawObjectFormattingTourManifest);
    }

    private static async Task WriteFormulaDiagnosticsTourManifestAsync(
        string outputDir,
        FormulaDiagnosticsTourContext context,
        IReadOnlyList<FormulaDiagnosticsTourManifestCapture> captures)
    {
        var manifest = new FormulaDiagnosticsTourManifest(
            Tool: "FREEX_FORMULA_DIAGNOSTICS_TOUR",
            EvidenceFamily: "formula-diagnostics",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "formula-diagnostics:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_formula_diagnostics_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-FORMULAS-002", "UI-CMD-FORM-003", "UI-CMD-FORM-005"],
            SheetName: context.SheetName,
            InputCell: context.InputCell.ToA1(),
            ResultCell: context.ResultCell.ToA1(),
            ErrorCell: context.ErrorCell.ToA1(),
            ResultFormula: context.ResultFormula,
            ErrorFormula: context.ErrorFormula,
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new FormulaDiagnosticsTourManifestPairing(
                "interactive:formula-diagnostics:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no foreground mouse, keyboard, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX window/dialog owns foreground focus for each capture."),
            Captures: captures,
            CoveredStates:
            [
                "Trace Precedents visible arrows",
                "Trace Dependents visible arrows",
                "Remove Arrows cleared state",
                "Show Formulas enabled sheet state",
                "Error Checking dialog/list",
                "Evaluate Formula default button and one-step advance",
                "Add Watch dialog",
                "Watch Window list, refresh, and delete states"
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF windows with RenderTargetBitmap; it is not foreground CopyFromScreen proof.",
                "No global mouse or keyboard input is synthesized; command handlers and WPF button events are invoked in process for deterministic capture.",
                "The Add Watch surface is captured by showing the production AddWatchDialog directly; the actual watch insertion then uses the same AddWatchFromSelection/WatchWindowService path as the command.",
                "The Evaluate Formula dialog is shown modeless so the tour can capture the default command and a stepped state without blocking on ShowDialog.",
                "The trace-arrow and show-formulas captures are FreeX-only visual states; no paired Microsoft Excel evidence is produced by this tool."
            ]);

        var path = Path.Combine(outputDir, FormulaDiagnosticsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.FormulaDiagnosticsTourManifest);
    }

    private static async Task WriteFormulaAuthoringNamesTourManifestAsync(
        string outputDir,
        FormulaAuthoringNamesTourContext context,
        IReadOnlyList<FormulaAuthoringNamesTourManifestCapture> captures)
    {
        var manifest = new FormulaAuthoringNamesTourManifest(
            Tool: "FREEX_FORMULA_AUTHORING_NAMES_TOUR",
            EvidenceFamily: "formula-authoring-names",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "formula-authoring-names:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_formula_authoring_names_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-FORMULAS-001", "UI-CMD-FORM-001", "UI-CMD-FORM-002", "UI-CAT-DIALOG-001C"],
            SheetName: context.Sheet.Name,
            AuthoringRange: context.AuthoringRange.ToString(),
            RevenueRange: context.RevenueRange.ToString(),
            CostRange: context.CostRange.ToString(),
            ProfitRange: context.ProfitRange.ToString(),
            MarginRange: context.MarginRange.ToString(),
            DefinedNames: context.DefinedNames,
            SummaryFormulaCell: context.SummaryFormulaCell.ToA1(),
            ProfitFormulaCell: context.ProfitFormulaCell.ToA1(),
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new FormulaAuthoringNamesTourManifestPairing(
                "interactive:formula-authoring-names:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no foreground mouse, keyboard, keytip, UIA, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX window/dialog owns foreground focus for each capture."),
            Captures: captures,
            CoveredStates:
            [
                "Formulas ribbon Function Library and Defined Names groups over a seeded formula/names worksheet.",
                "AutoSum split-menu state.",
                "Logical Functions function-library menu state.",
                "Use in Formula menu populated by seeded workbook defined names.",
                "Insert Function dialog with a non-default category and selected function.",
                "Name Manager dialog with seeded workbook names.",
                "Define Name dialog with name, scope, comment, and Refers To fields.",
                "Create from Selection dialog with default Top row/Left column choices."
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF windows/menus with RenderTargetBitmap; it is not foreground CopyFromScreen proof.",
                "Ribbon menus are opened through production handlers or context-menu state without physical mouse, keytip, shortcut, or UIA invocation.",
                "Dialog captures show production visual/default-focus states but do not submit OK/Cancel, create/delete names, insert a function, or persist/save/reload the defined-name model.",
                "Use in Formula evidence covers menu population and active formula text only; committing the selected name into formulas and undo/redo remain separate workflow proof.",
                "Formula diagnostics, formula-bar/name-box, and Excel-paired screenshot evidence are intentionally outside this bounded slice."
            ]);

        var path = Path.Combine(outputDir, FormulaAuthoringNamesTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.FormulaAuthoringNamesTourManifest);
    }

    private static async Task WriteReviewCommentsProtectionTourManifestAsync(
        string outputDir,
        ReviewCommentsProtectionTourContext context,
        IReadOnlyList<ReviewCommentsProtectionTourManifestCapture> captures)
    {
        var manifest = new ReviewCommentsProtectionTourManifest(
            Tool: "FREEX_REVIEW_COMMENTS_PROTECTION_TOUR",
            EvidenceFamily: "review-comments-protection",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "review-comments-protection:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_review_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-REVIEW-001",
                "UI-CAT-REVIEW-002",
                "UI-CMD-REVIEW-001",
                "UI-CMD-REVIEW-002",
                "UI-CMD-REVIEW-003",
                "UI-CMD-REVIEW-004"
            ],
            SheetName: context.Sheet.Name,
            SpellingCell: context.SpellingCell.ToA1(),
            SpellingWord: context.SpellingWord,
            SpellingSuggestion: context.SpellingSuggestion,
            ThreadedCommentCell: context.ThreadedCommentCell.ToA1(),
            NoteCell: context.NoteCell.ToA1(),
            NewThreadedCommentCell: context.NewThreadedCommentCell.ToA1(),
            AllowEditRange: context.AllowEditRange.ToString(),
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new ReviewCommentsProtectionTourManifestPairing(
                "interactive:review-comments-protection:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no foreground mouse, keyboard, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX main window or owned Review window owns foreground focus for each capture."),
            Captures: captures,
            CoveredStates:
            [
                "Review tab supported command groups and current Thesaurus/change-history gaps",
                "Spelling dialog",
                "Accessibility Checker issue-list dialog",
                "New threaded comment in-window inline popup",
                "Show Comments and Show Notes list windows",
                "Protect Sheet and Protect Workbook dialogs",
                "Allow Users to Edit Ranges dialog"
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF windows with RenderTargetBitmap; it is not foreground CopyFromScreen proof.",
                "No global mouse, keytip, keyboard, native share UI, or range-picker input is synthesized.",
                "The Spelling capture shows the production dialog with a deterministic word/suggestion pair; it does not run the full modal replacement loop.",
                "Thesaurus and Show Changes are not currently supported FreeX Review commands, so the baseline Review tab capture documents their absence rather than placeholder dialogs.",
                "Protect/unprotect confirmation, wrong-password, Permissions, Share, foreground focus trapping, and paired Microsoft Excel screenshots remain open."
            ]);

        var path = Path.Combine(outputDir, ReviewCommentsProtectionTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.ReviewCommentsProtectionTourManifest);
    }

    private static async Task WritePrintPreviewTourManifestAsync(
        string outputDir,
        Sheet sheet,
        int totalPages,
        bool closedViaEscapeEquivalent,
        bool focusReturned)
    {
        var includeLastPage = totalPages > 1;
        var captures = new List<PrintPreviewTourManifestCapture>
        {
            new(
                CaptureKey: "print-preview:file-print-entry:opened",
                PairKey: "interactive:print-preview:file-print-entry:opened",
                ScenarioId: "print-preview:file-print-entry",
                State: "opened",
                EntryPath: "File > Print",
                FileName: "freex_print_backstage_file_print_entry",
                OutputFileName: "freex_print_backstage_file_print_entry.png",
                EvidenceSummary: "Backstage Print view shows the print preview directly with page and print options on the left."),
            new(
                CaptureKey: "print-preview:ctrl-p-entry:opened",
                PairKey: "interactive:print-preview:ctrl-p-entry:opened",
                ScenarioId: "print-preview:ctrl-p-entry",
                State: "opened",
                EntryPath: "Ctrl+P routed to File > Print, then Print Preview",
                FileName: "freex_print_preview_ctrlp_entry_opened",
                OutputFileName: "freex_print_preview_ctrlp_entry_opened.png",
                EvidenceSummary: "Print Preview dialog opens with the production toolbar, preview surface, settings panel, and Print as the initial keyboard target."),
            new(
                CaptureKey: "print-preview:toolbar:first-page",
                PairKey: "interactive:print-preview:toolbar:first-page",
                ScenarioId: "print-preview:toolbar-navigation",
                State: "first-page",
                EntryPath: "File > Print > Print Preview",
                FileName: "freex_print_preview_toolbar_first_page",
                OutputFileName: "freex_print_preview_toolbar_first_page.png",
                EvidenceSummary: "Toolbar shows first-page navigation state, page count label, print controls, zoom, margins, page setup, close, and settings summary.")
        };

        if (includeLastPage)
        {
            captures.Add(new PrintPreviewTourManifestCapture(
                CaptureKey: "print-preview:toolbar:last-page",
                PairKey: "interactive:print-preview:toolbar:last-page",
                ScenarioId: "print-preview:toolbar-navigation",
                State: "last-page",
                EntryPath: "File > Print > Print Preview, page number box to final page",
                FileName: "freex_print_preview_toolbar_last_page",
                OutputFileName: "freex_print_preview_toolbar_last_page.png",
                EvidenceSummary: "Toolbar shows the final-page page-count label after keyboard-equivalent page-number navigation."));
        }

        captures.AddRange(
        [
            new PrintPreviewTourManifestCapture(
                CaptureKey: "print-preview:zoom-settings-summary:page-width",
                PairKey: "interactive:print-preview:zoom-settings-summary:page-width",
                ScenarioId: "print-preview:zoom-settings-summary",
                State: "page-width-zoom",
                EntryPath: "Print Preview > Zoom > Page Width",
                FileName: "freex_print_preview_zoom_settings_summary",
                OutputFileName: "freex_print_preview_zoom_settings_summary.png",
                EvidenceSummary: "Zoom combo is changed to Page Width while the print settings summary remains visible."),
            new PrintPreviewTourManifestCapture(
                CaptureKey: "print-preview:closed:focus-return",
                PairKey: "interactive:print-preview:closed:focus-return",
                ScenarioId: "print-preview:close-focus-return",
                State: "closed-focus-return",
                EntryPath: "Print Preview close via IsCancel Close button route",
                FileName: "freex_print_preview_closed_focus_return",
                OutputFileName: "freex_print_preview_closed_focus_return.png",
                EvidenceSummary: "Preview is closed and the workbook window is visible again with focus explicitly returned to the backstage Print command.")
        ]);

        var manifest = new PrintPreviewTourManifest(
            Tool: "FREEX_PRINT_PREVIEW_TOUR",
            EvidenceFamily: "print-preview",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "print-preview:foreground-focus-return",
            OutputDirectory: outputDir,
            OutputNaming: "freex_print_preview_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            EntryPaths: ["Ctrl+P", "File > Print > Print Preview"],
            SheetName: sheet.Name,
            TotalPages: totalPages,
            SettingsSummary: PrintSettingsPlanner.Build(sheet, textResolver: WpfPrintSettingsTextResolver.Instance).Summary,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-print-preview-dialog-and-main-window",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no global mouse, keyboard, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX main window or Print Preview dialog owns foreground focus for each capture."),
            ClosedViaEscapeEquivalent: closedViaEscapeEquivalent,
            FocusReturnedToBackstagePrintPreviewCommand: focusReturned,
            Captures: captures,
            Limitations:
            [
                "This in-app tour renders real FreeX WPF windows using RenderTargetBitmap rather than OS CopyFromScreen.",
                "The Ctrl+P route is represented by FreeX's existing source-proven Ctrl+P-to-File-Print path plus a live Print Preview dialog opened from that backstage entry point; no global Ctrl+P keystroke is synthesized.",
                "The close capture uses the PrintPreviewCloseButton IsCancel route as the Escape-equivalent path, then explicitly returns focus to the backstage Print command before the final screenshot.",
                "The native Windows print dialog is not opened during this tour to avoid sending output to a real printer or blocking on system print UI."
            ]);

        var path = Path.Combine(outputDir, PrintPreviewTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.PrintPreviewTourManifest);
    }

    private static async Task WriteBackstageRecentExportShareTourManifestAsync(
        string outputDir,
        BackstageRecentExportShareTourContext context,
        IReadOnlyList<BackstageRecentExportShareTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        var manifest = new BackstageRecentExportShareTourManifest(
            Tool: "FREEX_BACKSTAGE_RECENT_EXPORT_SHARE_TOUR",
            EvidenceFamily: "backstage-recent-export-share",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "backstage:recent-export-share",
            OutputDirectory: outputDir,
            OutputNaming: "freex_backstage_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-FILE-001",
                "UI-CAT-FILE-002"
            ],
            EntryPaths:
            [
                "File > Open / Recent",
                "File > Open / Pinned",
                "File > Info",
                "File > Export",
                "File > Share",
                "File > Back"
            ],
            SheetName: context.SheetName,
            ActiveRange: context.ActiveRange,
            RecentFileNames: context.RecentFileNames,
            PinnedFileNames: context.PinnedFileNames,
            UnsavedShareStatus: context.UnsavedShareStatus,
            ExportStatus: context.ExportStatus,
            SavedWorkbookOutputFileName: Path.GetFileName(savedWorkbookPath),
            SavedWorkbookRetained: File.Exists(savedWorkbookPath),
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: BackstageRecentExportShareTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process WPF RenderTargetBitmap captures plus owned native warning-dialog PrintWindow capture; no global mouse, keyboard, native Open/Save, or Windows Share UI input is used."
                    : "Abort before WPF window file writes unless the expected FreeX window owns foreground focus; owned native warning dialog is captured by HWND ownership and caption."),
            Captures: captures,
            CoveredStates:
            [
                "Backstage Open navigation with Recent list populated from deterministic existing local files.",
                "Backstage Pinned tab with deterministic existing pinned local files and row command surfaces.",
                "Backstage Info saved/unsaved file, share readiness, and export readiness status text.",
                "Owned unsupported XLSX feature save-warning dialog for an in-memory unsupported-feature report.",
                "Backstage Export command focus without launching the native Save As dialog.",
                "Production Export Options dialog surfaces for PDF and XPS, including disabled PDF-only XPS choices.",
                "Share unsaved guard status requiring Save As before Windows Share.",
                "Share saved-ready status after saving to a deterministic XLSX path without launching Windows Share.",
                "Back exits Backstage and returns focus to the worksheet grid."
            ],
            Limitations:
            [
                "This tour is deterministic visual evidence and does not synthesize physical mouse, keytip, Tab/F6, or UIA invocation input.",
                "The native Open dialog, native Export Save As dialog, and Windows Share UI are intentionally not launched; those remain foreground-guarded OS UI gaps.",
                "The unsupported-feature evidence captures the production save-warning dialog from an in-memory feature report rather than opening a corpus workbook in the tour.",
                "The Share saved-ready proof stops at the planner/status surface before invoking Windows Share to avoid external OS UI.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, BackstageRecentExportShareTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.BackstageRecentExportShareTourManifest);
    }

    private static async Task WriteOptionsAccountTourManifestAsync(
        string outputDir,
        LocalAccountPlan accountPlan,
        OptionsAccountTourManifestCapture accountMessageCapture,
        IReadOnlyList<OptionsAccountTourManifestCapture> optionCaptures,
        bool categoryListFocusedByDefault,
        bool closedViaCancelEquivalent,
        bool focusReturned)
    {
        var captures = new List<OptionsAccountTourManifestCapture>
        {
            new(
                CaptureKey: "account:backstage-entry:focused",
                PairKey: "interactive:options-account:account-backstage-entry-focused",
                ScenarioId: "options-account:account-backstage-entry",
                State: "account-entry-focused",
                Surface: "Backstage Account entry",
                FileName: "freex_account_backstage_entry_focused",
                OutputFileName: "freex_account_backstage_entry_focused.png",
                CaptureMethod: "RenderTargetBitmap-main-window",
                EvidenceSummary: "Backstage is open with the Account navigation command focused beside the Options command.",
                CategoryName: null,
                CategoryIndex: null,
                FocusedElementAutomationId: "BackstageAccountButton",
                CaptureLogicalWidth: 1120,
                CaptureLogicalHeight: 760),
            accountMessageCapture,
            new(
                CaptureKey: "account:closed:focus-return",
                PairKey: "interactive:options-account:account-focus-return",
                ScenarioId: "options-account:account-focus-return",
                State: "account-focus-return",
                Surface: "Backstage Account entry",
                FileName: "freex_account_backstage_focus_return",
                OutputFileName: "freex_account_backstage_focus_return.png",
                CaptureMethod: "RenderTargetBitmap-main-window",
                EvidenceSummary: "After the Account message closes, focus is restored to the Backstage Account command.",
                CategoryName: null,
                CategoryIndex: null,
                FocusedElementAutomationId: "BackstageAccountButton",
                CaptureLogicalWidth: 1120,
                CaptureLogicalHeight: 760)
        };
        captures.AddRange(optionCaptures);
        captures.Add(new OptionsAccountTourManifestCapture(
            CaptureKey: "options:closed:cancel-focus-return",
            PairKey: "interactive:options-account:options-cancel-focus-return",
            ScenarioId: "options-account:options-focus-return",
            State: "options-cancel-focus-return",
            Surface: "Backstage Options entry",
            FileName: "freex_options_cancel_focus_return",
            OutputFileName: "freex_options_cancel_focus_return.png",
            CaptureMethod: "RenderTargetBitmap-main-window",
            EvidenceSummary: "After verifying the OptionsCancelButton IsCancel metadata and closing the tour dialog, focus is restored to the Backstage Options command.",
            CategoryName: null,
            CategoryIndex: null,
            FocusedElementAutomationId: "BackstageOptionsButton",
            CaptureLogicalWidth: 1120,
            CaptureLogicalHeight: 760));

        var manifest = new OptionsAccountTourManifest(
            Tool: "FREEX_OPTIONS_ACCOUNT_TOUR",
            EvidenceFamily: "backstage-options-account",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "options-account:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_<Surface>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CMD-FILE-005",
            EntryPaths: ["File > Account", "File > Options"],
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-WPF-windows-and-PrintWindow-owned-native-dialog",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process WPF RenderTargetBitmap captures plus owned native Account dialog PrintWindow capture; no global mouse or keyboard input is used."
                    : "Abort before WPF window file writes unless the expected FreeX window owns foreground focus; owned native Account dialog is captured by HWND ownership and caption."),
            AccountTitle: accountPlan.Title,
            AccountDetailLabels: accountPlan.Details.Select(detail => detail.Label).ToArray(),
            CategoryListFocusedByDefault: categoryListFocusedByDefault,
            OptionsClosedViaCancelEquivalent: closedViaCancelEquivalent,
            FocusReturnedToBackstageOptionsCommand: focusReturned,
            PlannedCaptureCount: OptionsAccountTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            Limitations:
            [
                "This in-app tour captures real FreeX Backstage and Options WPF surfaces with RenderTargetBitmap and the real owned Account MessageBox with PrintWindow.",
                "The tour does not synthesize global mouse/keytip/UIA input; those interaction paths remain separate from this visual evidence.",
                "The Options close proof verifies the OptionsCancelButton IsCancel metadata before closing the modeless tour dialog directly; modal Escape/Cancel event routing remains separate.",
                "The tour does not persist option changes through OK.",
                "The Account command is a local-account information message, not a cloud account sign-in surface."
            ]);

        var path = Path.Combine(outputDir, OptionsAccountTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.OptionsAccountTourManifest);
    }

    private static async Task WriteHelpAboutLegalTourManifestAsync(
        string outputDir,
        IReadOnlyList<HelpAboutLegalTourManifestCapture> captures)
    {
        var manifest = new HelpAboutLegalTourManifest(
            Tool: "FREEX_HELP_ABOUT_LEGAL_TOUR",
            EvidenceFamily: "help-about-legal",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "help-about-legal:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_<Surface>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CMD-HELP-001",
            EntryPaths:
            [
                "Help tab",
                "Help > Help Online",
                "Help > Feedback",
                "Help > Check for Updates",
                "Help > About FreeX",
                "Help > Legal Notices",
                "Help tab focus return / Ready status"
            ],
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-WPF-windows-and-PrintWindow-owned-native-dialogs",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process WPF RenderTargetBitmap captures plus owned native guarded-message PrintWindow captures; no global mouse, keyboard, UIA input, or external browser launch is used."
                    : "Abort before WPF window file writes unless the expected FreeX window owns foreground focus; owned native guarded messages are captured by HWND ownership and caption."),
            ExternalBrowserLaunched: false,
            PlannedCaptureCount: HelpAboutLegalTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            Limitations:
            [
                "This in-app tour captures real FreeX WPF Help, About, and Legal Notices surfaces with RenderTargetBitmap.",
                "The Help Online, Feedback, and Check for Updates captures intentionally render FreeX-owned guarded failure messages instead of launching a browser or external process.",
                "The tour does not synthesize foreground mouse clicks, keytips, or UI Automation invoke; those interaction paths remain separate from this visual evidence.",
                "The About and Legal Notices dialogs are shown as owned WPF windows for deterministic capture, then closed directly by the tour.",
                "The final full-window capture records FreeX focus returned to the Help ribbon context and the Ready status bar after owned dialogs close.",
                "No Microsoft Excel counterpart capture is produced by this tool."
            ]);

        var path = Path.Combine(outputDir, HelpAboutLegalTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HelpAboutLegalTourManifest);
    }

    private static async Task WriteDataToolsDialogsTourManifestAsync(
        string outputDir,
        DataToolsDialogsTourContext context,
        IReadOnlyList<DataToolsDialogsTourManifestCapture> captures)
    {
        var manifest = new DataToolsDialogsTourManifest(
            Tool: "FREEX_DATA_TOOLS_DIALOGS_TOUR",
            EvidenceFamily: "data-tools-dialogs",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "data-tools-dialogs:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_data_tools_<Surface>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-DATA-002",
            CatalogCategoryId: "UI-CAT-DATA-002",
            CatalogCommandRows: ["UI-CMD-DATA-003", "UI-CMD-DATA-004", "UI-CMD-DATA-005", "UI-CMD-DATA-006"],
            SheetName: context.Sheet.Name,
            TextToColumnsRange: context.TextToColumnsRange.ToString(),
            RemoveDuplicatesRange: context.RemoveDuplicatesRange.ToString(),
            DataTableRange: context.DataTableRange.ToString(),
            ConsolidateSourceRange: context.ConsolidateSourceRange.ToString(),
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-data-tools-dialog-window",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, keytip, range-picker, or screen capture input is used."
                    : "Dialog captures abort unless the expected FreeX WPF dialog owns foreground focus immediately before render and file write."),
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            CoveredStates:
            [
                "Advanced Filter dialog",
                "Text to Columns wizard step 1 original data type",
                "Text to Columns wizard step 2 delimited choices",
                "Text to Columns wizard step 2 fixed-width ruler choices",
                "Text to Columns wizard step 3 column format/destination choices",
                "Remove Duplicates header checkbox and column list",
                "Data Validation Settings tab",
                "Data Validation Input Message tab",
                "Data Validation Error Alert tab",
                "Goal Seek dialog",
                "Goal Seek Status dialog",
                "Scenario Manager dialog",
                "Data Table dialog",
                "Consolidate dialog",
                "Forecast Sheet dialog"
            ],
            Limitations:
            [
                "This bounded first tour opens production FreeX WPF dialog surfaces in process and captures them with RenderTargetBitmap.",
                "The tour does not synthesize physical mouse/keytip/range-picker/Enter/Escape input; those interaction paths remain separate UI evidence gaps.",
                "The tour avoids native import/open/save dialogs and does not submit data-tool mutations to the workbook.",
                "Goal Seek status is seeded with a deterministic converged result instead of running iterative recalculation during screenshot capture.",
                "No Microsoft Excel counterpart screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, DataToolsDialogsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.DataToolsDialogsTourManifest);
    }

    private static async Task WriteDataSortFilterOutlineTourManifestAsync(
        string outputDir,
        DataSortFilterOutlineTourContext context,
        IReadOnlyList<DataSortFilterOutlineTourManifestCapture> captures)
    {
        var hiddenRows = context.Sheet.GroupHiddenRows.OrderBy(row => row).Select(row => row.ToString()).ToArray();
        var outlinedRows = context.Sheet.RowOutlineLevels
            .OrderBy(entry => entry.Key)
            .Select(entry => $"{entry.Key}:{entry.Value}")
            .ToArray();

        var manifest = new DataSortFilterOutlineTourManifest(
            Tool: "FREEX_DATA_SORT_FILTER_OUTLINE_TOUR",
            EvidenceFamily: "data-sort-filter-outline",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "data-sort-filter-outline:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_data_sort_filter_outline_<Surface>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-DATA-001",
            CatalogRows: ["UI-CAT-DATA-001", "UI-CAT-DATA-003", "UI-CMD-DATA-001", "UI-CMD-DATA-002", "UI-CMD-DATA-007", "UI-CMD-DATA-008"],
            SheetName: context.Sheet.Name,
            TableRange: context.TableRange.ToString(),
            FilterHeaderCell: context.FilterHeaderCell.ToA1(),
            OutlineRange: context.OutlineRange.ToString(),
            RowOutlineLevels: outlinedRows,
            GroupHiddenRowsAfterShowDetail: hiddenRows,
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-window-dialog-menu",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, native file dialog, range-picker, or screen capture input is used."
                    : "Window, dialog, and menu captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            CoveredStates:
            [
                "Data tab command surface for Get Data, Refresh All, Sort & Filter, and Outline",
                "Sort dialog with multiple levels and header-aware columns",
                "Sort Options dialog with case-sensitive, custom first-key order, and left-to-right orientation",
                "AutoFilter flyout searched to a deterministic Status value",
                "Subtotal dialog default command surface",
                "Outline Group, Hide Detail, and Show Detail visual states",
                "Group and Ungroup dropdown menu states"
            ],
            Limitations:
            [
                "This tour captures production FreeX WPF surfaces in process and does not synthesize physical mouse clicks, keytips, access keys, or UI Automation invoke.",
                "Get Data is represented by the Data tab command surface only; the native OpenFileDialog import workflow is intentionally not opened in this deterministic tour.",
                "Refresh All is represented by its Data tab command surface only; no external data source refresh or recalculation assertion is performed by this screenshot slice.",
                "The AutoFilter flyout is opened through the existing in-app flyout factory rather than OS pointer input, then the search box is set directly for deterministic checklist filtering.",
                "Subtotal is captured as the production dialog surface; this tour does not submit subtotal insertion or verify generated subtotal rows.",
                "No Microsoft Excel counterpart screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, DataSortFilterOutlineTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.DataSortFilterOutlineTourManifest);
    }

    private static async Task WriteInsertTablesChartsTourManifestAsync(
        string outputDir,
        InsertTablesChartsTourContext context,
        IReadOnlyList<InsertTablesChartsTourManifestCapture> captures)
    {
        var manifest = new InsertTablesChartsTourManifest(
            Tool: "FREEX_INSERT_TABLES_CHARTS_TOUR",
            EvidenceFamily: "insert-tables-charts",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "insert-tables-charts:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_insert_tables_charts_<Surface>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-INSERT-001",
            CatalogIds: ["UI-CAT-INSERT-001", "UI-CAT-INSERT-002", "UI-CAT-INSERT-001A", "UI-CAT-INSERT-001D", "UI-CAT-INSERT-002A"],
            SheetName: context.Sheet.Name,
            SourceRange: context.SourceRange.ToString(),
            PivotTargetRange: context.PivotTargetRange.ToString(),
            SparklineLocation: context.SparklineLocation.ToA1(),
            TableName: context.Sheet.StructuredTables.FirstOrDefault(table => table.Range.Equals(context.SourceRange))?.Name
                ?? ScreenshotTourTableName,
            PivotTableName: ScreenshotTourPivotTableName,
            TableStyleName: context.TableStyleName,
            CaptureStatus: "complete",
            CaptureMode: "RenderTargetBitmap-in-process",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, keytip, range-picker, or screen capture input is used."
                    : "Window and dialog captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            Captures: captures,
            CoveredStates:
            [
                "Insert tab Tables/Charts/Sparklines command surface",
                "Create Table dialog",
                "Created structured table with Table Design contextual tab",
                "Created PivotTable with PivotTable Analyze contextual tab and Field List",
                "Insert Chart dialog on Recommended Charts",
                "Created embedded column chart target",
                "Insert Sparkline dialog",
                "Produced line, column, and win/loss sparklines"
            ],
            Limitations:
            [
                "This bounded tour opens production FreeX WPF dialog surfaces in process and captures them with RenderTargetBitmap.",
                "The tour seeds table, pivot, chart, and sparkline workbook state deterministically; it does not synthesize physical mouse/keytip/range-picker/Enter/Escape input.",
                "Picture, shape, hyperlink, text box, header/footer, symbol, object, comment, slicer, timeline, and recommended PivotTable workflows are intentionally outside this slice.",
                "The chart-result capture uses FreeX's existing object-placeholder display mode for a visible created chart target; full embedded chart renderer proof remains with the broader chart visual/persistence lane.",
                "No Microsoft Excel counterpart screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, InsertTablesChartsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.InsertTablesChartsTourManifest);
    }

    private static async Task WriteKeyTipOverlayTourManifestAsync(
        string outputDir,
        IReadOnlyList<KeyTipOverlayTourManifestCapture> captures)
    {
        var manifest = new KeyTipOverlayTourManifest(
            Tool: "FREEX_KEYTIP_OVERLAY_TOUR",
            EvidenceFamily: "keytip-overlay",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "ribbon-keytip-overlay-pixel-placement",
            OutputDirectory: outputDir,
            OutputNaming: "<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "in-process-background-render-allowed"
                : "foreground-guarded-in-process-render",
            FocusGuard: new KeyTipOverlayTourManifestFocusGuard(
                RequiredForWindowCaptures: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, or screen capture input is used."
                    : "Window-band captures abort unless the FreeX main window owns foreground focus immediately before render and file write. Popup element captures are in-process element renders."),
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            CoveredStates:
            [
                "Top-level Alt/F10 tab badges",
                "QAT badges in top-level keytip mode",
                "Home visible command-scope badges",
                "Home Borders dropdown menu keytip scope",
                "Home Borders > Line Color nested submenu keytip scope",
                "Narrow Home command-scope collapsed-group badges"
            ],
            Limitations:
            [
                "Window-band captures cover the top 300 logical pixels of the FreeX window.",
                "Top-level, QAT, visible command, and narrow collapsed cases capture the production KeyTipOverlay badges.",
                "Dropdown and nested submenu states are captured as live WPF popup elements; their scoped keytips are rendered as menu input gesture text rather than overlay badges because the production keytip mode intentionally clears the owner-window badge overlay while menu scope is active.",
                "This evidence proves FreeX pixel placement for the captured states only; broader Excel pair captures remain separate foreground-guarded work."
            ]);

        var path = Path.Combine(outputDir, KeyTipOverlayTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.KeyTipOverlayTourManifest);
    }

    private sealed record RibbonScreenshotTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string? Context,
        bool BurstMode,
        double CaptureLogicalHeight,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<string> Tabs,
        IReadOnlyList<RibbonScreenshotTourManifestWidth> Widths,
        IReadOnlyList<RibbonScreenshotTourManifestPhase> Phases,
        IReadOnlyList<RibbonScreenshotTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record RibbonScreenshotTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record RibbonScreenshotTourManifestFocusGuard(bool Required, string Policy);

    private sealed record RibbonScreenshotTourManifestWidth(string Label, double? WindowWidth, string EvidencePurpose);

    private sealed record RibbonScreenshotTourManifestPhase(string Label, string? FileNameSuffix);

    private sealed record RibbonScreenshotTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string Tab,
        string TabFileName,
        string Width,
        string Phase,
        string FileName,
        string OutputFileName,
        string CounterpartFileName);

    private sealed record AutoFilterFlyoutTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string HeaderCell,
        string HeaderText,
        string AutoFilterRange,
        uint FilterColumnOffset,
        string CaptureStatus,
        string CaptureMethod,
        AutoFilterFlyoutTourManifestPairing Pairing,
        IReadOnlyList<AutoFilterFlyoutTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record AutoFilterFlyoutTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record AutoFilterFlyoutTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record HomeNumberFormatDropdownTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SelectedCell,
        string SelectedFormat,
        IReadOnlyList<string> OptionLabels,
        string CaptureStatus,
        string CaptureMethod,
        HomeNumberFormatDropdownTourManifestPairing Pairing,
        IReadOnlyList<HomeNumberFormatDropdownTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record HomeNumberFormatDropdownTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record HomeNumberFormatDropdownTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record HomeAlignmentNumberTourContext(
        string SheetName,
        GridRange AlignmentRange,
        GridRange NumberRange,
        IReadOnlyList<string> SampleFormats);

    private sealed record HomeAlignmentNumberTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SheetName,
        string AlignmentRange,
        string NumberRange,
        IReadOnlyList<string> SampleFormats,
        string CaptureStatus,
        string CaptureMethod,
        HomeAlignmentNumberTourManifestPairing Pairing,
        IReadOnlyList<HomeAlignmentNumberTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record HomeAlignmentNumberTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record HomeAlignmentNumberTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string EvidencePurpose);

    private sealed record HomeBordersDropdownTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string EntryPath,
        IReadOnlyList<string> MenuHeaders,
        string CaptureStatus,
        string CaptureMethod,
        HomeBordersDropdownTourManifestPairing Pairing,
        IReadOnlyList<HomeBordersDropdownTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record HomeBordersDropdownTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record HomeBordersDropdownTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record HomeFontColorsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SampleRange,
        string CaptureStatus,
        string CaptureMode,
        double CaptureLogicalHeight,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        HomeFontColorsTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<string> CoveredFeatures,
        IReadOnlyList<string> RemainingGaps,
        IReadOnlyList<HomeFontColorsTourManifestCapture> Captures);

    private sealed record HomeFontColorsTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record HomeFontColorsTourManifestCapture(
        string State,
        string FileName,
        string CaptureKey,
        string EvidencePurpose,
        string CaptureMethod,
        double LogicalWidth,
        double LogicalHeight,
        string ActiveCell,
        string ActiveCellFontName,
        double ActiveCellFontSize,
        bool ActiveCellBold,
        bool ActiveCellItalic,
        bool ActiveCellUnderline,
        bool ActiveCellDoubleUnderline,
        bool ActiveCellStrikethrough,
        string? ActiveCellFontColor,
        string? ActiveCellFillColor,
        IReadOnlyList<string> MenuHeaders);

    private sealed record HomeStylesConditionalFormattingTourContext(
        Sheet Sheet,
        GridRange TableRange,
        GridRange ConditionalFormatRange,
        GridRange CellStyleRange,
        GridRange ResultRange,
        string TableStyleName);

    private sealed record HomeStylesConditionalFormattingTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogCommandRows,
        string SheetName,
        string ResultRange,
        string TableRange,
        string ConditionalFormatRange,
        string CellStyleRange,
        string TableStyleName,
        int ConditionalFormatRuleCount,
        int StructuredTableCount,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        HomeStylesConditionalFormattingTourManifestPairing Pairing,
        IReadOnlyList<HomeStylesConditionalFormattingTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record HomeStylesConditionalFormattingTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record HomeStylesConditionalFormattingTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        int StructuredTableCount,
        int ConditionalFormatRuleCount,
        IReadOnlyList<string> MenuHeaders,
        string EvidenceSummary);

    private sealed record HomeClipboardCellsEditingTourContext(
        Sheet Sheet,
        GridRange CopySourceRange,
        GridRange PasteTargetRange,
        GridRange SortRange,
        GridRange UsedRange,
        string GoToDefaultAddress);

    private sealed record HomeClipboardCellsEditingTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogCommandRows,
        string SheetName,
        string CopySourceRange,
        string PasteTargetRange,
        string SortRange,
        string UsedRange,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<HomeClipboardCellsEditingTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record HomeClipboardCellsEditingTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string CatalogCommandRow,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        string ClipboardRange,
        bool ClipboardIsCut,
        int NoteCount,
        int HyperlinkCount,
        IReadOnlyList<string> MenuHeaders,
        string EvidenceSummary);

    private sealed record WorksheetContextMenuTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SelectedCell,
        string EntryPath,
        IReadOnlyList<string> MenuHeaders,
        string CaptureStatus,
        string CaptureMethod,
        WorksheetContextMenuTourManifestPairing Pairing,
        IReadOnlyList<WorksheetContextMenuTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record WorksheetContextMenuTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record WorksheetContextMenuTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record PrintPreviewTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> EntryPaths,
        string SheetName,
        int TotalPages,
        string SettingsSummary,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        bool ClosedViaEscapeEquivalent,
        bool FocusReturnedToBackstagePrintPreviewCommand,
        IReadOnlyList<PrintPreviewTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record PrintPreviewTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string EntryPath,
        string FileName,
        string OutputFileName,
        string EvidenceSummary);

    private sealed record OptionsAccountTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> EntryPaths,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        string AccountTitle,
        IReadOnlyList<string> AccountDetailLabels,
        bool CategoryListFocusedByDefault,
        bool OptionsClosedViaCancelEquivalent,
        bool FocusReturnedToBackstageOptionsCommand,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<OptionsAccountTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record OptionsAccountTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string EvidenceSummary,
        string? CategoryName,
        int? CategoryIndex,
        string? FocusedElementAutomationId,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record OptionsAccountTourNativeCaptureSize(int Width, int Height);

    private sealed record BackstageRecentExportShareTourContext(
        string SheetName,
        string ActiveRange,
        IReadOnlyList<string> RecentFileNames,
        IReadOnlyList<string> PinnedFileNames,
        string UnsavedShareStatus,
        string ExportStatus);

    private sealed record BackstageRecentExportShareTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        IReadOnlyList<string> EntryPaths,
        string SheetName,
        string ActiveRange,
        IReadOnlyList<string> RecentFileNames,
        IReadOnlyList<string> PinnedFileNames,
        string UnsavedShareStatus,
        string ExportStatus,
        string SavedWorkbookOutputFileName,
        bool SavedWorkbookRetained,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<BackstageRecentExportShareTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record BackstageRecentExportShareTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string EntryPath,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string? FocusedElementAutomationId,
        string SelectedRange,
        string? CurrentFilePath,
        string SharePlanKind,
        string ShareStatus,
        string ExportStatus,
        string? ExportRequestSummary,
        string EvidenceSummary);

    private sealed record HelpAboutLegalTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> EntryPaths,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        bool ExternalBrowserLaunched,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<HelpAboutLegalTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record HelpAboutLegalTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string EntryPath,
        string EvidenceSummary,
        string? Url,
        string? FocusedElementAutomationId,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record DataToolsDialogsTourContext(
        Sheet Sheet,
        GridRange TextToColumnsRange,
        GridRange RemoveDuplicatesRange,
        GridRange DataTableRange,
        GridRange ConsolidateSourceRange,
        CellAddress GoalSeekSetCell,
        CellAddress GoalSeekChangingCell);

    private sealed record DataToolsDialogsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string CatalogCategoryId,
        IReadOnlyList<string> CatalogCommandRows,
        string SheetName,
        string TextToColumnsRange,
        string RemoveDuplicatesRange,
        string DataTableRange,
        string ConsolidateSourceRange,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<DataToolsDialogsTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record DataToolsDialogsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string CatalogCommandRow,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string EvidenceSummary,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record DataSortFilterOutlineTourContext(
        Sheet Sheet,
        GridRange TableRange,
        GridRange OutlineRange,
        CellAddress FilterHeaderCell);

    private sealed record DataSortFilterOutlineTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogRows,
        string SheetName,
        string TableRange,
        string FilterHeaderCell,
        string OutlineRange,
        IReadOnlyList<string> RowOutlineLevels,
        IReadOnlyList<string> GroupHiddenRowsAfterShowDetail,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<DataSortFilterOutlineTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record DataSortFilterOutlineTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string CatalogRow,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string EvidenceSummary,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record InsertTablesChartsTourContext(
        Sheet Sheet,
        GridRange SourceRange,
        GridRange PivotTargetRange,
        CellAddress SparklineLocation,
        string TableStyleName);

    private sealed record InsertTablesChartsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string SourceRange,
        string PivotTargetRange,
        string SparklineLocation,
        string TableName,
        string PivotTableName,
        string TableStyleName,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<InsertTablesChartsTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record InsertTablesChartsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string CatalogId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        int StructuredTableCount,
        int PivotTableCount,
        int ChartCount,
        int SparklineCount,
        string EvidenceSummary);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    private sealed record KeyTipOverlayTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string CaptureStatus,
        string CaptureMode,
        KeyTipOverlayTourManifestFocusGuard FocusGuard,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<KeyTipOverlayTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record KeyTipOverlayTourManifestFocusGuard(
        bool RequiredForWindowCaptures,
        string Policy);

    private sealed record KeyTipOverlayTourManifestCapture(
        string CaptureKey,
        string State,
        string Scope,
        string Description,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        int BadgeCount,
        int CollapsedGroupBadgeCount,
        int MenuItemKeyTipCount,
        bool IsInProcess,
        bool IsForegroundGuarded);

    private sealed record QatUndoRedoTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SelectedCell,
        string CaptureStatus,
        string CaptureMethod,
        QatUndoRedoTourManifestPairing Pairing,
        IReadOnlyList<QatUndoRedoTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record QatUndoRedoTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record QatUndoRedoTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        bool UndoButtonEnabled,
        bool UndoHistoryButtonEnabled,
        bool RedoButtonEnabled,
        bool RedoHistoryButtonEnabled,
        bool CanUndo,
        bool CanRedo,
        string ActiveCell,
        string ActiveCellText,
        bool ActiveCellBold,
        string? ActiveCellFillColor,
        string StatusText,
        IReadOnlyList<string> UndoHistoryLabels,
        IReadOnlyList<string> RedoHistoryLabels,
        IReadOnlyList<string> MenuHeaders);

    private sealed record SheetTabTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<SheetTabTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record SheetTabTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string EvidenceSummary);
    private sealed record TitlebarWindowChromeTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        string CaptureStatus,
        string CaptureMethod,
        string SavedWorkbookOutputFileName,
        bool SavedWorkbookRetained,
        TitlebarWindowChromeTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<TitlebarWindowChromeTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record TitlebarWindowChromeTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record TitlebarWindowChromeTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string EvidenceSummary,
        string WindowState,
        string WindowTitle,
        string WorkbookNameText,
        string WorkbookName,
        bool WorkbookDirty,
        string? CurrentFileName,
        bool TitleBarQatVisible,
        IReadOnlyList<string> TitleBarQatCommandIds,
        TitlebarWindowChromeTourManifestButtonState MinimizeButton,
        TitlebarWindowChromeTourManifestButtonState MaxRestoreButton,
        TitlebarWindowChromeTourManifestButtonState CloseButton,
        string MaxRestoreIconKind);

    private sealed record TitlebarWindowChromeTourManifestButtonState(
        string AutomationId,
        string AutomationName,
        string HelpText,
        bool IsVisible,
        bool IsEnabled,
        double ActualWidth,
        double ActualHeight);

    private sealed record FormulaBarNameBoxTourContext(
        string SheetName,
        string NamedRangeName,
        string NamedRangeAddress,
        string StartCell,
        IReadOnlyList<string> ObjectNames);

    private sealed record GridSelectionEditingTourContext(
        Sheet Sheet,
        CellAddress SelectedCell,
        GridRange SelectedRange,
        uint RowSelectionIndex,
        uint ColumnSelectionIndex,
        CellAddress EditCell,
        GridRange FilterVisibleRange,
        GridRange FillRange,
        GridRange ClearRange,
        IReadOnlyList<string> FilterHiddenRows,
        IReadOnlyList<string> ManualHiddenRows);

    private sealed record FormulaDiagnosticsTourContext(
        string SheetName,
        CellAddress InputCell,
        CellAddress ResultCell,
        CellAddress ErrorCell,
        string ResultFormula,
        string ErrorFormula);

    private sealed record FormulaAuthoringNamesTourContext(
        Sheet Sheet,
        GridRange AuthoringRange,
        GridRange RevenueRange,
        GridRange CostRange,
        GridRange ProfitRange,
        GridRange MarginRange,
        IReadOnlyList<string> DefinedNames,
        CellAddress SummaryFormulaCell,
        CellAddress ProfitFormulaCell);

    private sealed record ReviewCommentsProtectionTourContext(
        Sheet Sheet,
        CellAddress SpellingCell,
        string SpellingWord,
        string SpellingSuggestion,
        CellAddress ThreadedCommentCell,
        CellAddress NoteCell,
        CellAddress NewThreadedCommentCell,
        GridRange AllowEditRange);

    private sealed record DrawObjectFormattingTourContext(
        Sheet Sheet,
        DrawingShapeModel Shape,
        PictureModel Picture,
        FreeX.Core.Model.TextBoxModel TextBox);

    private sealed record FormulaBarNameBoxTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string SheetName,
        string NamedRangeName,
        string NamedRangeAddress,
        string StartCell,
        IReadOnlyList<string> ObjectNames,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        FormulaBarNameBoxTourManifestPairing Pairing,
        IReadOnlyList<FormulaBarNameBoxTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record FormulaBarNameBoxTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record FormulaBarNameBoxTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string NameBoxText,
        bool NameBoxDropDownOpen,
        string FormulaBarText,
        bool FormulaBarAcceptsReturn,
        bool FormulaBarExpanded,
        string SelectedRange,
        string ActiveCellText,
        string FocusedAutomationId,
        int KeyTipBadgeCount,
        string EvidenceSummary);

    private sealed record GridSelectionEditingTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogRows,
        string SheetName,
        string SelectedCell,
        string SelectedRange,
        string WholeRowSelection,
        string WholeColumnSelection,
        string EditCell,
        string FilterVisibleRange,
        string FillRange,
        string ClearRange,
        IReadOnlyList<string> FilterHiddenRows,
        IReadOnlyList<string> ManualHiddenRows,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        GridSelectionEditingTourManifestPairing Pairing,
        IReadOnlyList<GridSelectionEditingTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record GridSelectionEditingTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record GridSelectionEditingTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        string ActiveCell,
        string NameBoxText,
        string FormulaBarText,
        string StatusReadyText,
        string StatusAverageText,
        string StatusCountText,
        string StatusNumericalCountText,
        string StatusSumText,
        string EditingCell,
        bool InlineEditorVisible,
        string ActiveCellText,
        IReadOnlyList<string> VisibleRows,
        string EvidenceSummary);

    private sealed record StatusFooterTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        StatusFooterTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<StatusFooterTourManifestCapture> Captures,
        IReadOnlyList<string> Limitations);

    private sealed record StatusFooterTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record StatusFooterTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string EvidencePurpose,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string ActiveRange,
        string StatusModeText,
        bool StatusModeVisible,
        string AverageText,
        string CountText,
        string NumericalCountText,
        string SumText,
        string MinText,
        string MaxText,
        bool StatsVisible,
        string ViewMode,
        bool NormalViewChecked,
        bool PageLayoutViewChecked,
        bool PageBreakPreviewChecked,
        string ZoomText,
        double ZoomSliderValue,
        bool ZoomOutButtonEnabled,
        bool ZoomInButtonEnabled,
        string FormulaBarText);

    private sealed record InsertObjectsLinksTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        InsertObjectsLinksTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<InsertObjectsLinksTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record InsertObjectsLinksTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record InsertObjectsLinksTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CounterpartFileName,
        string EvidenceSummary,
        string CommandRow,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);

    private sealed record ViewPanesZoomTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        ViewPanesZoomTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<ViewPanesZoomTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record ViewPanesZoomTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record ViewPanesZoomTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string EvidencePurpose,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string ActiveRange,
        string ViewMode,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        bool FormulaBarVisible,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        string ZoomText,
        double ZoomSliderValue,
        string WindowArrangement,
        int CustomViewCount,
        bool ViewNormalChecked,
        bool ViewPageLayoutChecked,
        bool ViewPageBreakPreviewChecked,
        bool ViewGridlinesChecked,
        bool ViewHeadingsChecked,
        bool ViewRulerChecked,
        bool ViewFormulaBarChecked,
        bool SplitButtonChecked);

    private sealed record PageLayoutSetupTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        PageLayoutSetupTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<PageLayoutSetupTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record PageLayoutSetupTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record PageLayoutSetupTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string ActiveRange,
        string ViewMode,
        string PageOrientation,
        string PaperSize,
        string PrintArea,
        string PrintTitleRows,
        string PrintTitleColumns,
        IReadOnlyList<uint> RowPageBreaks,
        IReadOnlyList<uint> ColumnPageBreaks,
        string ScaleToFit,
        bool ShowGridlines,
        bool ShowHeadings,
        bool PrintGridlines,
        bool PrintHeadings,
        string ScaleWidthText,
        string ScaleHeightText,
        string ScalePercentText,
        IReadOnlyList<string> MenuHeaders,
        string EvidencePurpose);

    private sealed record DrawObjectFormattingTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        DrawObjectFormattingTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<string> SeededObjects,
        IReadOnlyList<DrawObjectFormattingTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record DrawObjectFormattingTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record DrawObjectFormattingTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string ActiveRange,
        string SelectedObjectKind,
        string SelectedObjectName,
        int ShapeCount,
        int PictureCount,
        int TextBoxCount,
        IReadOnlyList<string> DrawingZOrder,
        IReadOnlyList<string> CommandRows,
        IReadOnlyList<string> MenuHeaders,
        string EvidencePurpose);

    private sealed record FormulaDiagnosticsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string InputCell,
        string ResultCell,
        string ErrorCell,
        string ResultFormula,
        string ErrorFormula,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        FormulaDiagnosticsTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<FormulaDiagnosticsTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record FormulaDiagnosticsTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record FormulaDiagnosticsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        bool ShowFormulas,
        int FormulaTraceArrowCount,
        int WatchCount,
        string EvidenceSummary);

    private sealed record FormulaAuthoringNamesTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string AuthoringRange,
        string RevenueRange,
        string CostRange,
        string ProfitRange,
        string MarginRange,
        IReadOnlyList<string> DefinedNames,
        string SummaryFormulaCell,
        string ProfitFormulaCell,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        FormulaAuthoringNamesTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<FormulaAuthoringNamesTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record FormulaAuthoringNamesTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record FormulaAuthoringNamesTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string SelectedRange,
        string FormulaBarText,
        int NameCount,
        IReadOnlyList<string> DefinedNames,
        IReadOnlyList<string> MenuHeaders,
        string EvidenceSummary);

    private sealed record ReviewCommentsProtectionTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string SpellingCell,
        string SpellingWord,
        string SpellingSuggestion,
        string ThreadedCommentCell,
        string NoteCell,
        string NewThreadedCommentCell,
        string AllowEditRange,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        ReviewCommentsProtectionTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<ReviewCommentsProtectionTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record ReviewCommentsProtectionTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record ReviewCommentsProtectionTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        int ThreadedCommentCount,
        int NoteCount,
        int AllowEditRangeCount,
        int AccessibilityIssueCount,
        string EvidenceSummary);

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(RibbonScreenshotTourManifest))]
    [JsonSerializable(typeof(AutoFilterFlyoutTourManifest))]
    [JsonSerializable(typeof(HomeNumberFormatDropdownTourManifest))]
    [JsonSerializable(typeof(HomeAlignmentNumberTourManifest))]
    [JsonSerializable(typeof(HomeBordersDropdownTourManifest))]
    [JsonSerializable(typeof(HomeFontColorsTourManifest))]
    [JsonSerializable(typeof(HomeStylesConditionalFormattingTourManifest))]
    [JsonSerializable(typeof(HomeClipboardCellsEditingTourManifest))]
    [JsonSerializable(typeof(HomeSubmittedWorkflowsTourManifest))]
    [JsonSerializable(typeof(HomeStylePersistenceTourManifest))]
    [JsonSerializable(typeof(RibbonOverflowKeytipTourManifest))]
    [JsonSerializable(typeof(WorksheetContextMenuTourManifest))]
    [JsonSerializable(typeof(WorksheetContextTargetsTourManifest))]
    [JsonSerializable(typeof(WorksheetContextSubmittedTourManifest))]
    [JsonSerializable(typeof(PrintPreviewTourManifest))]
    [JsonSerializable(typeof(BackstageRecentExportShareTourManifest))]
    [JsonSerializable(typeof(OptionsAccountTourManifest))]
    [JsonSerializable(typeof(HelpAboutLegalTourManifest))]
    [JsonSerializable(typeof(KeyTipOverlayTourManifest))]
    [JsonSerializable(typeof(QatUndoRedoTourManifest))]
    [JsonSerializable(typeof(SheetTabTourManifest))]
    [JsonSerializable(typeof(SheetTabWorkflowsTourManifest))]
    [JsonSerializable(typeof(TitlebarWindowChromeTourManifest))]
    [JsonSerializable(typeof(FormulaBarNameBoxTourManifest))]
    [JsonSerializable(typeof(GridSelectionEditingTourManifest))]
    [JsonSerializable(typeof(StatusFooterTourManifest))]
    [JsonSerializable(typeof(StatusFooterInteractionsTourManifest))]
    [JsonSerializable(typeof(InsertObjectsLinksTourManifest))]
    [JsonSerializable(typeof(InsertObjectPersistenceTourManifest))]
    [JsonSerializable(typeof(DataToolsDialogsTourManifest))]
    [JsonSerializable(typeof(DataSortFilterOutlineTourManifest))]
    [JsonSerializable(typeof(DataSubmittedWorkflowsTourManifest))]
    [JsonSerializable(typeof(DataWhatIfWorkflowsTourManifest))]
    [JsonSerializable(typeof(FileIoImportSmokeTourManifest))]
    [JsonSerializable(typeof(FileBackstageWorkflowsTourManifest))]
    [JsonSerializable(typeof(InsertTablesChartsTourManifest))]
    [JsonSerializable(typeof(TableWorkflowsTourManifest))]
    [JsonSerializable(typeof(ChartDataLayoutTourManifest))]
    [JsonSerializable(typeof(ChartPersistenceRenderTourManifest))]
    [JsonSerializable(typeof(ChartObjectSelectionTourManifest))]
    [JsonSerializable(typeof(PivotFieldListContextTourManifest))]
    [JsonSerializable(typeof(PivotOptionsSlicerTourManifest))]
    [JsonSerializable(typeof(PivotAdvancedWorkflowsTourManifest))]
    [JsonSerializable(typeof(ViewPanesZoomTourManifest))]
    [JsonSerializable(typeof(ViewWorkflowsTourManifest))]
    [JsonSerializable(typeof(PageLayoutSetupTourManifest))]
    [JsonSerializable(typeof(PageLayoutOutputTourManifest))]
    [JsonSerializable(typeof(DrawObjectFormattingTourManifest))]
    [JsonSerializable(typeof(DrawObjectPersistenceTourManifest))]
    [JsonSerializable(typeof(FormulaDiagnosticsTourManifest))]
    [JsonSerializable(typeof(FormulaAuthoringNamesTourManifest))]
    [JsonSerializable(typeof(FormulaSubmittedPersistenceTourManifest))]
    [JsonSerializable(typeof(ReviewCommentsProtectionTourManifest))]
    [JsonSerializable(typeof(ReviewProtectionMatrixTourManifest))]
    [JsonSerializable(typeof(ReviewStatsShareTourManifest))]
    private sealed partial class RibbonScreenshotTourManifestJsonContext : JsonSerializerContext;

    // Activated by FREEX_ACCENT_BAR_TOUR=1 env var. Output lands in <repo-root>/screenshots/accent-bars-tour/.
    private void TryStartAccentBarVisualTour()
    {
        if (Environment.GetEnvironmentVariable("FREEX_ACCENT_BAR_TOUR") != "1")
            return;

        var outputDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots", "accent-bars-tour"));
        Directory.CreateDirectory(outputDir);
        _ = RunAccentBarVisualTourAsync(outputDir);
    }

    private async Task RunAccentBarVisualTourAsync(string outputDir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(outputDir, "*.png"))
                File.Delete(file);

            WindowState = WindowState.Normal;
            Width = 1280;
            Height = 760;
            await Task.Delay(900);

            await CaptureElementAsync(TitleBarRoot, outputDir, "title-normal");
            await CaptureElementAsync(StatusBarRoot, outputDir, "status-normal");

            if (GetQuickAccessToolbarButton(QuickAccessToolbarCommandIds.Save) is { } saveQatButton)
                await HoverAndCaptureElementAsync(saveQatButton, TitleBarRoot, outputDir, "title-save-hover");
            await HoverAndCaptureElementAsync(MaxRestoreBtn, TitleBarRoot, outputDir, "title-system-hover");
            await HoverAndCaptureElementAsync(StatusZoomOutButton, StatusBarRoot, outputDir, "status-minus-hover");
            await HoverAndCaptureElementAsync(StatusZoomInButton, StatusBarRoot, outputDir, "status-plus-hover");
            await HoverAndCaptureElementAsync(CloseSysBtn, TitleBarRoot, outputDir, "title-close-hover");

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("accent_bar_tour_failed", new Dictionary<string, string?>
            {
                ["reason"] = ex.GetType().Name,
                ["message"] = ex.Message
            });
            Application.Current.Shutdown();
        }
    }

    private async Task HoverAndCaptureElementAsync(
        FrameworkElement hoverTarget,
        FrameworkElement captureTarget,
        string outputDir,
        string fileName)
    {
        UpdateLayout();
        var center = hoverTarget.PointToScreen(new Point(hoverTarget.ActualWidth / 2, hoverTarget.ActualHeight / 2));
        SetCursorPos((int)Math.Round(center.X), (int)Math.Round(center.Y));
        await Task.Delay(220);
        await CaptureElementAsync(captureTarget, outputDir, fileName);
    }

    private static Task CaptureElementAsync(FrameworkElement element, string outputDir, string fileName) =>
        ScreenshotCapture.CaptureElementToPngAsync(element, outputDir, fileName);

    // Activated by FREEX_SHEET_TAB_TOUR=1 env var. Output lands in <repo-root>/screenshots/sheet-tabs-tour/.
    private void TryStartSheetTabVisualTour()
    {
        if (Environment.GetEnvironmentVariable("FREEX_SHEET_TAB_TOUR") != "1")
            return;

        var outputDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots", SheetTabTourOutputDirectoryName));
        Directory.CreateDirectory(outputDir);
        _ = RunSheetTabVisualTourAsync(outputDir);
    }

    private async Task RunSheetTabVisualTourAsync(string outputDir)
    {
        try
        {
            DeleteSheetTabTourEvidence(outputDir);
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            WindowState = WindowState.Normal;
            Width = 1180;
            Height = 760;
            await Task.Delay(700);

            var captures = new List<SheetTabTourManifestCapture>();
            await CaptureSheetTabsForTourAsync(
                outputDir,
                captures,
                "freex_sheet_tabs_single_sheet",
                "single-sheet",
                "Fresh workbook tab strip shows the selected Sheet1 tab and the plus add-sheet affordance.");

            InsertNewSheet();
            await Task.Delay(300);
            await CaptureSheetTabsForTourAsync(
                outputDir,
                captures,
                "freex_sheet_tabs_after_add_sheet",
                "after-add-sheet",
                "Production Insert Sheet route added Sheet2, selected it, and left the plus affordance visible.");

            PrepareSheetTabVisualTourWorkbook();
            await Task.Delay(400);
            var visibleSheets = _workbook.Sheets.Where(sheet => !sheet.IsHidden).Take(20).ToList();

            _currentSheetId = visibleSheets[3].Id;
            _groupedSheetIds.Clear();
            foreach (var sheet in visibleSheets.Skip(1).Take(5))
                _groupedSheetIds.Add(sheet.Id);
            _sheetGroupAnchor = visibleSheets[1].Id;
            RefreshSheetTabs();
            await Task.Delay(300);
            await CaptureSheetTabsForTourAsync(
                outputDir,
                captures,
                "freex_sheet_tabs_grouped_colored",
                "grouped-colored-tabs",
                "Grouped tabs 2-6 show active/grouped styling while tab colors render on colored sheets.");

            await CaptureSheetTabContextMenuForTourAsync(outputDir, captures, visibleSheets[3]);
            await CaptureSheetNameDialogForTourAsync(outputDir, captures, visibleSheets[3].Name);

            var hiddenSheet = visibleSheets[6];
            hiddenSheet.IsHidden = true;
            _currentSheetId = visibleSheets[3].Id;
            RefreshSheetTabs();
            await Task.Delay(300);
            await CaptureSheetTabsForTourAsync(
                outputDir,
                captures,
                "freex_sheet_tabs_hidden_sheet_excluded",
                "hidden-sheet-excluded",
                "Hidden sheet is absent from the visible tab strip while adjacent visible tabs remain selectable.");
            await CaptureUnhideSheetDialogForTourAsync(outputDir, captures, hiddenSheet.Name);
            hiddenSheet.IsHidden = false;
            RefreshSheetTabs();

            Width = 760;
            await Task.Delay(450);
            await CaptureSheetTabStateForTourAsync(
                outputDir,
                captures,
                visibleSheets,
                0,
                "freex_sheet_tabs_overflow_start",
                "overflow-start",
                "Narrow tab strip at the first visible sheet shows overflow navigation affordances.");
            await CaptureSheetTabStateForTourAsync(
                outputDir,
                captures,
                visibleSheets,
                10,
                "freex_sheet_tabs_overflow_middle",
                "overflow-middle",
                "Narrow tab strip scrolls the active middle sheet into view with left/right navigation affordances.");
            await CaptureSheetTabStateForTourAsync(
                outputDir,
                captures,
                visibleSheets,
                19,
                "freex_sheet_tabs_overflow_end",
                "overflow-end",
                "Narrow tab strip scrolls to the final sheet and shows the right edge overflow state.");

            ValidateSheetTabTourEvidence(outputDir, captures);
            await WriteSheetTabTourManifestAsync(outputDir, captures);

            _suppressClosePrompt = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            RecordDiagnosticEvent("sheet_tab_tour_failed", new Dictionary<string, string?>
            {
                ["reason"] = ex.GetType().Name,
                ["message"] = ex.Message
            });
            _suppressClosePrompt = true;
            Application.Current.Shutdown();
        }
    }

    private async Task CaptureSheetTabStateForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        IReadOnlyList<Sheet> visibleSheets,
        int activeIndex,
        string fileName,
        string state,
        string evidenceSummary)
    {
        var sheet = visibleSheets[activeIndex];
        _currentSheetId = sheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(sheet.Id);
        _sheetGroupAnchor = sheet.Id;
        RefreshSheetTabs();
        await Task.Delay(260);
        await CaptureSheetTabsForTourAsync(outputDir, captures, fileName, state, evidenceSummary);
    }

    private void PrepareSheetTabVisualTourWorkbook()
    {
        while (_workbook.Sheets.Count < 20)
            _workbook.AddSheet(SheetTabListPlanner.GenerateUniqueSheetName(_workbook));

        var names = new[]
        {
            "Overview",
            "Inputs",
            "Assumptions",
            "Forecast",
            "Actuals",
            "Charts",
            "Audit",
            "Archive",
            "Region East",
            "Region West",
            "Region North",
            "Region South",
            "Ops",
            "People",
            "Capital",
            "Cash Flow",
            "Notes",
            "Review",
            "Signoff",
            "2026 Plan"
        };
        for (var index = 0; index < names.Length && index < _workbook.Sheets.Count; index++)
            _workbook.Sheets[index].Name = names[index];

        var colors = new CellColor?[]
        {
            null,
            new(232, 121, 65),
            new(83, 141, 213),
            new(112, 173, 71),
            new(165, 105, 189),
            null,
            new(243, 156, 18),
            new(75, 172, 198)
        };

        for (var index = 0; index < colors.Length && index < _workbook.Sheets.Count; index++)
            _workbook.Sheets[index].TabColor = colors[index];

        _currentSheetId = _workbook.Sheets[0].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
    }

    private async Task CaptureSheetTabsForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        string fileName,
        string state,
        string evidenceSummary,
        bool revealCurrentSheet = true)
    {
        UpdateLayout();
        SheetTabsRowGrid.UpdateLayout();
        if (revealCurrentSheet)
            BringCurrentSheetTabIntoView();
        UpdateSheetTabNavigation();
        UpdateLayout();
        SheetTabsRowGrid.UpdateLayout();
        if (revealCurrentSheet)
            BringCurrentSheetTabIntoView();
        UpdateSheetTabNavigation();
        UpdateLayout();
        SheetTabsRowGrid.UpdateLayout();

        var source = PresentationSource.FromVisual(SheetTabsRowGrid);
        var dpiX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
        int pw = Math.Max(1, (int)(SheetTabsRowGrid.ActualWidth * dpiX));
        int ph = Math.Max(1, (int)(SheetTabsRowGrid.ActualHeight * dpiY));

        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        rtb.Render(SheetTabsRowGrid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        var path = Path.Combine(outputDir, $"{fileName}.png");
        await using var stream = File.Create(path);
        encoder.Save(stream);

        captures.Add(new SheetTabTourManifestCapture(
            CaptureKey: $"sheet-tabs:{state}",
            PairKey: $"interactive:sheet-tabs:{state}",
            ScenarioId: "sheet-tabs:visual-parity",
            State: state,
            Surface: "sheet-tab-strip",
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            EvidenceSummary: evidenceSummary));
    }

    private async Task CaptureSheetTabContextMenuForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        Sheet sheet)
    {
        _currentSheetId = sheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(sheet.Id);
        _sheetGroupAnchor = sheet.Id;
        RefreshSheetTabs();
        await Task.Delay(300);

        var tab = FindSheetTab(sheet.Id)
            ?? throw new InvalidOperationException("Sheet-tab tour could not locate the context-menu target tab.");
        var target = FindSheetTabContextMenuTarget(tab)
            ?? throw new InvalidOperationException("Sheet-tab tour could not locate the tab ContextMenu visual.");
        var menu = target.ContextMenu
            ?? throw new InvalidOperationException("Sheet-tab tour could not locate the tab ContextMenu.");

        try
        {
            MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
            await Task.Delay(350);
            menu.UpdateLayout();
            await CaptureElementAsync(menu, outputDir, "freex_sheet_tabs_context_menu_opened");
            captures.Add(new SheetTabTourManifestCapture(
                CaptureKey: "sheet-tabs:context-menu-opened",
                PairKey: "interactive:sheet-tabs:context-menu-opened",
                ScenarioId: "sheet-tabs:context-menu",
                State: "context-menu-opened",
                Surface: "sheet-tab-context-menu",
                FileName: "freex_sheet_tabs_context_menu_opened",
                OutputFileName: "freex_sheet_tabs_context_menu_opened.png",
                EvidenceSummary: "Production sheet-tab ContextMenu is open for the active tab, including Insert, Delete, Rename, Move or Copy, Tab Color, Hide, Unhide, Select All Sheets, and Ungroup Sheets entries."));
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private async Task CaptureSheetNameDialogForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        string currentName)
    {
        var dialog = new SheetNameDialog(currentName) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_sheet_tabs_rename_dialog_opened");
            captures.Add(new SheetTabTourManifestCapture(
                CaptureKey: "sheet-tabs:rename-dialog-opened",
                PairKey: "interactive:sheet-tabs:rename-dialog-opened",
                ScenarioId: "sheet-tabs:rename-dialog",
                State: "rename-dialog-opened",
                Surface: "rename-sheet-dialog",
                FileName: "freex_sheet_tabs_rename_dialog_opened",
                OutputFileName: "freex_sheet_tabs_rename_dialog_opened.png",
                EvidenceSummary: "Rename Sheet dialog is open through the same SheetNameDialog used by sheet-tab double-click and context Rename, with the name box focused and selected on load."));
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task CaptureUnhideSheetDialogForTourAsync(
        string outputDir,
        List<SheetTabTourManifestCapture> captures,
        string hiddenSheetName)
    {
        var dialog = new UnhideSheetDialog([hiddenSheetName]) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_sheet_tabs_unhide_dialog_opened");
            captures.Add(new SheetTabTourManifestCapture(
                CaptureKey: "sheet-tabs:unhide-dialog-opened",
                PairKey: "interactive:sheet-tabs:unhide-dialog-opened",
                ScenarioId: "sheet-tabs:unhide-dialog",
                State: "unhide-dialog-opened",
                Surface: "unhide-sheet-dialog",
                FileName: "freex_sheet_tabs_unhide_dialog_opened",
                OutputFileName: "freex_sheet_tabs_unhide_dialog_opened.png",
                EvidenceSummary: $"Unhide Sheet dialog lists the hidden worksheet '{hiddenSheetName}' and focuses the hidden-sheet list."));
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void DeleteSheetTabTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_sheet_tabs_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, SheetTabTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateSheetTabTourEvidence(string outputDir, IReadOnlyList<SheetTabTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Sheet-tab tour did not create planned capture {capture.OutputFileName}.");
        }
    }

    private static async Task WriteSheetTabTourManifestAsync(
        string outputDir,
        IReadOnlyList<SheetTabTourManifestCapture> captures)
    {
        var manifest = new SheetTabTourManifest(
            Tool: "FREEX_SHEET_TAB_TOUR",
            EvidenceFamily: "sheet-tabs",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "sheet-tabs:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_sheet_tabs_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CaptureStatus: "complete",
            CaptureMethod: "RenderTargetBitmap-sheet-tab-strip-context-menu-and-dialogs",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, or screen capture input is used."
                    : "Dialog captures abort unless the expected FreeX WPF window owns foreground focus immediately before render and file write."),
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            CoveredStates:
            [
                "Selected single-sheet tab and plus add-sheet affordance",
                "Add Sheet route selecting the newly created sheet",
                "Grouped sheet-tab styling and tab color rendering",
                "Production sheet-tab context menu",
                "Rename Sheet dialog focus/select-all affordance",
                "Hidden sheet excluded from the tab strip",
                "Unhide Sheet dialog with hidden-sheet list",
                "Narrow tab-strip overflow navigation at start, middle, and end positions"
            ],
            Limitations:
            [
                "This tour renders FreeX WPF surfaces in-process; it does not synthesize physical mouse clicks, Ctrl/Shift modifiers, drag reorder, or double-click input.",
                "The context menu capture is opened from the production tab ContextMenu object rather than by OS right-click, so live placement/focus evidence remains separate.",
                "The rename and unhide dialog captures show the production dialogs and initial focus targets, but they do not submit dialog changes.",
                "No Microsoft Excel counterpart or macOS/native-host capture is produced by this tool."
            ]);

        var path = Path.Combine(outputDir, SheetTabTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.SheetTabTourManifest);
    }
}
