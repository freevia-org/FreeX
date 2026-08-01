using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class UiTestCatalogInventoryTests
{
    [Fact]
    public void InventorySnapshot_MatchesSourceDerivedInventoryModel()
    {
        var snapshot = ReadInventorySnapshot();
        var inventory = ReadCommandInventory();
        var shortcutSummary = ReadShortcutSummary();
        var topLevelTabs = ReadVisibleTopLevelRibbonTabs();
        var contextualTabs = ReadContextualRibbonTabs();
        var dialogTypeNames = ReadDialogTypeNames();
        var xamlClickWiredControls = ReadMainWindowXamlClickHandlerCount();
        var xamlAutomationIds = ReadMainWindowXamlAutomationIdCount();
        var ribbonKeyTipMetadata = ReadMainWindowXamlRibbonKeyTipCount();
        var keyboardShortcutUsages = ReadKeyboardShortcutUsageCounts();
        var screenshotToolScripts = ReadDocumentedScreenshotToolScripts();
        var uiEvidenceScreenshotCount = ReadUiEvidenceScreenshotCount();
        var worksheetContextMenuCommandCount = CountWorksheetContextMenuActionCommands(WorksheetContextMenuPlanner.BuildCommands());

        AssertSnapshotRow(
            snapshot,
            "Command surface in-scope rows",
            inventory.CommandSurfaceTabs.Sum(tab => tab.Implemented + tab.Partial),
            "From `parity/command-inventory.json`: Implemented + Partial command-surface rows.");
        AssertSnapshotRow(
            snapshot,
            "Menu/toolbar in-scope rows",
            inventory.MenuToolbarTabs.Sum(tab => tab.Implemented + tab.Partial),
            "Includes the current Draw tab menu/toolbar delta.");
        AssertSnapshotRow(
            snapshot,
            "Top-level ribbon/backstage tabs",
            topLevelTabs.Count,
            $"{string.Join(", ", topLevelTabs)}.");
        AssertSnapshotRow(
            snapshot,
            "Contextual ribbon tab declarations",
            contextualTabs.Count,
            $"{string.Join(", ", contextualTabs)} from collapsed `MainWindow.xaml` tab declarations.");
        AssertSnapshotRow(
            snapshot,
            "Dialog source classes",
            dialogTypeNames.Count,
            "Unique `*Dialog` class/x:Class names in `src/FreeX.App.Host`.");
        AssertSnapshotRow(
            snapshot,
            "XAML click-wired controls",
            xamlClickWiredControls,
            "`Click=\"...\"` occurrences in `MainWindow.xaml` on latest synced `origin/main`.");
        AssertSnapshotRow(
            snapshot,
            "Explicit UIA automation ids",
            xamlAutomationIds,
            "`AutomationProperties.AutomationId=\"...\"` declarations in `MainWindow.xaml`.");
        AssertSnapshotRow(
            snapshot,
            "Ribbon keytip metadata declarations",
            ribbonKeyTipMetadata,
            "`RibbonTooltip.KeyTip=\"...\"` declarations in `MainWindow.xaml`.");
        AssertSnapshotRow(
            snapshot,
            "Keyboard command shortcut usages",
            keyboardShortcutUsages.MatcherRules,
            $"{keyboardShortcutUsages.MatcherRules} matcher rules / {keyboardShortcutUsages.DispatcherTargets} dispatcher targets");
        AssertSnapshotRow(
            snapshot,
            "Documented shortcut rows",
            shortcutSummary.TotalInScope,
            $"From `parity/shortcuts.md`: {shortcutSummary.Parity} parity, {shortcutSummary.Partial} partial.");
        AssertSnapshotRow(
            snapshot,
            "Worksheet context menu commands",
            worksheetContextMenuCommandCount,
            "From `WorksheetContextMenuPlanner.BuildCommands()`.");
        AssertSnapshotRow(
            snapshot,
            "Screenshot tool scripts",
            screenshotToolScripts.Count,
            $"{string.Join(", ", screenshotToolScripts.Select(script => $"`tools/{script}`"))} documented and present.");
        AssertSnapshotRow(
            snapshot,
            "Existing UI evidence screenshots",
            uiEvidenceScreenshotCount,
            "Historical PNG evidence artifacts were removed during documentation cleanup; append new evidence paths to the relevant row.");
    }

    [Fact]
    public void SourceInventoryModel_MatchesParityDocumentSummaries()
    {
        var inventory = ReadCommandInventory();
        var commandSurfaceSummary = ReadCommandCoverageSummary("parity/command-surface.md");
        var menuToolbarSummary = ReadCommandCoverageSummary("parity/menu-toolbar.md");
        var shortcutSummary = ReadShortcutSummary();
        var shortcutRows = ReadShortcutRows();

        commandSurfaceSummary.Should().BeEquivalentTo(Summarize(inventory.CommandSurfaceTabs));
        menuToolbarSummary.Should().BeEquivalentTo(Summarize(inventory.MenuToolbarTabs));
        shortcutRows.Count(row => row.Status == "Parity").Should().Be(shortcutSummary.Parity);
        shortcutRows.Count(row => row.Status == "Partial").Should().Be(shortcutSummary.Partial);
        shortcutRows.Count(row => row.Status is "Not Implemented" or "Missing").Should().Be(shortcutSummary.NotImplemented);
        shortcutRows.Count(row => row.Status == "Excluded").Should().Be(shortcutSummary.Excluded);
        shortcutRows.Count(row => row.Status != "Excluded").Should().Be(shortcutSummary.TotalInScope);
    }

    [Fact]
    public void TopLevelTabInventory_MatchesCommandInventoryKeyTips()
    {
        var inventory = ReadCommandInventory();
        var sourceTabs = ReadVisibleTopLevelRibbonTabs();
        var keyTipTabs = inventory.KeyTips.TopLevelTabs
            .Select(tab => tab.Name == "File/Backstage" ? "File" : tab.Name)
            .ToArray();

        sourceTabs.Should().Equal(keyTipTabs);
    }

    [Fact]
    public void NextCatalogTasks_RecordSourceBasedInventoryGuardAsExisting()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");

        catalog.Should().NotContain(
            "Generate a machine-readable row list from `parity/command-surface.md`",
            "the source-based inventory guard now exists and future work should expand it");
        catalog.Should().Contain("Continue expanding the source-based machine-readable inventory guard");
    }

    [Fact]
    public void ScreenshotHarnessCatalogRow_DocumentsInAppRibbonTourPath()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var row = Regex
            .Split(catalog, "\\r?\\n")
            .Single(line => line.StartsWith("| UI-CMD-HARNESS-001 |", StringComparison.Ordinal));
        var plannedCaptureCount = RibbonScreenshotTourPlanner.DefaultTabs.Count *
                                  RibbonScreenshotTourPlanner.DefaultWidths.Count;

        row.Should().Contain("FREEX_SS_TOUR=1");
        row.Should().Contain("FREEX_SS_TOUR_BURST=1");
        row.Should().Contain("FREEX_SS_TOUR_CONTEXT=table");
        row.Should().Contain("FREEX_SS_TOUR_CONTEXT=chart");
        row.Should().Contain("contextual-chart-tour");
        row.Should().Contain("900_Chart_Design.png");
        row.Should().Contain("900_Chart_Format.png");
        row.Should().Contain("FREEX_SS_TOUR_OUTPUT_SUBDIR");
        row.Should().Contain("FREEX_SS_TOUR_TABS");
        row.Should().Contain("FREEX_SS_TOUR_WIDTHS");
        row.Should().Contain("FREEX_TITLEBAR_WINDOW_CHROME_TOUR=1");
        row.Should().Contain("titlebar-window-chrome-tour");
        row.Should().Contain("titlebar_window_chrome_tour_manifest.json");
        row.Should().Contain("FREEX_HELP_ABOUT_LEGAL_TOUR=1");
        row.Should().Contain("help-about-legal-tour");
        row.Should().Contain("help_about_legal_tour_manifest.json");
        row.Should().Contain($"{plannedCaptureCount} planned captures");
        row.Should().Contain($"{plannedCaptureCount * RibbonScreenshotTourPlanner.BurstPhases.Count} burst-phase captures");
        row.Should().Contain("ribbon_screenshot_tour_manifest.json");
        row.Should().Contain("resize breakpoint");
        row.Should().Contain("deletes only the currently requested plan");
        row.Should().Contain("phase-grouped expected PNG names");
        row.Should().Contain("missing planned tabs abort");
        row.Should().Contain("max/1100/900/750 by tab matrix");
        row.Should().Contain("36 captures each");
        row.Should().Contain("excel_<WidthLabel>_<RibbonTab>.png");
        row.Should().Contain("ribbon_<WidthLabel>_<RibbonTab>.png");
        row.Should().Contain("PairKey");
        row.Should().Contain("ribbon:<WidthLabel>:<TabFileName>");
        row.Should().Contain("counterpart file names");
    }

    [Fact]
    public void ScreenshotHarnessCatalogRow_DocumentsNativeOpenSaveDialogEvidenceArtifacts()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var evidencePaths = new[]
        {
            "screenshots/open-workbook-dialog-tour/freex_open_workbook_dialog_opened.png",
            "screenshots/open-workbook-dialog-tour/freex_open_workbook_dialog_tour_manifest.json",
            "screenshots/save-as-workbook-dialog-tour/freex_save_as_workbook_dialog_opened.png",
            "screenshots/save-as-workbook-dialog-tour/freex_save_as_workbook_dialog_tour_manifest.json",
            "screenshots_excel/open-workbook-dialog-tour/interactive_open_workbook_dialog_opened.png",
            "screenshots_excel/open-workbook-dialog-tour/excel_open_workbook_dialog_tour_manifest.json",
            "screenshots_excel/save-as-workbook-dialog-tour/interactive_save_as_workbook_dialog_opened.png",
            "screenshots_excel/save-as-workbook-dialog-tour/excel_save_as_workbook_dialog_tour_manifest.json",
        };

        foreach (var evidencePath in evidencePaths)
        {
            catalog.Should().Contain(evidencePath);
            File.Exists(WorkspaceFileLocator.Find(evidencePath.Split('/'))).Should().BeTrue(evidencePath);
        }

        WorkspaceFileLocator
            .ReadAllText("screenshots", "open-workbook-dialog-tour", "freex_open_workbook_dialog_tour_manifest.json")
            .Should().Contain("interactive:open-workbook-dialog:opened");
        WorkspaceFileLocator
            .ReadAllText("screenshots_excel", "save-as-workbook-dialog-tour", "excel_save_as_workbook_dialog_tour_manifest.json")
            .Should().Contain("interactive:save-as-workbook-dialog:opened");
    }

    [Fact]
    public void ScreenshotHarnessCatalogRow_DocumentsPairedExcelPopupEvidenceArtifacts()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var evidencePaths = new[]
        {
            "screenshots_excel/autofilter-flyout-tour/interactive_table_autofilter_dropdown_opened.png",
            "screenshots_excel/autofilter-flyout-tour/excel_autofilter_flyout_tour_manifest.json",
            "screenshots_excel/home-number-format-dropdown-tour/interactive_home_number_format_opened.png",
            "screenshots_excel/home-number-format-dropdown-tour/excel_home_number_format_dropdown_tour_manifest.json",
            "screenshots_excel/worksheet-context-menu-tour/interactive_worksheet_cell_context_menu_opened.png",
            "screenshots_excel/worksheet-context-menu-tour/excel_worksheet_context_menu_tour_manifest.json",
        };

        foreach (var evidencePath in evidencePaths)
        {
            catalog.Should().Contain(evidencePath);
            File.Exists(WorkspaceFileLocator.Find(evidencePath.Split('/'))).Should().BeTrue(evidencePath);
        }

        WorkspaceFileLocator
            .ReadAllText("screenshots_excel", "autofilter-flyout-tour", "excel_autofilter_flyout_tour_manifest.json")
            .Should().Contain("interactive:table-autofilter-dropdown:opened");
        WorkspaceFileLocator
            .ReadAllText("screenshots_excel", "home-number-format-dropdown-tour", "excel_home_number_format_dropdown_tour_manifest.json")
            .Should().Contain("interactive:home-number-format:opened");
        WorkspaceFileLocator
            .ReadAllText("screenshots_excel", "worksheet-context-menu-tour", "excel_worksheet_context_menu_tour_manifest.json")
            .Should().Contain("interactive:worksheet-cell-context-menu:opened");
    }

    [Fact]
    public void FormulaCatalogRows_DocumentSubmittedPersistenceEvidenceArtifacts()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var evidencePaths = new[]
        {
            "screenshots/formula-submitted-persistence-tour/freex_formula_submitted_persistence_seeded_before_submit.png",
            "screenshots/formula-submitted-persistence-tour/freex_formula_submitted_persistence_formula_results.png",
            "screenshots/formula-submitted-persistence-tour/freex_formula_submitted_persistence_use_in_formula_inserted_reference.png",
            "screenshots/formula-submitted-persistence-tour/freex_formula_submitted_persistence_use_in_formula_menu.png",
            "screenshots/formula-submitted-persistence-tour/freex_formula_submitted_persistence_name_manager_submitted.png",
            "screenshots/formula-submitted-persistence-tour/freex_formula_submitted_persistence_saved_native_workbook.png",
            "screenshots/formula-submitted-persistence-tour/freex_formula_submitted_persistence_reopened_grid.png",
            "screenshots/formula-submitted-persistence-tour/freex_formula_submitted_persistence_name_manager_reopened.png",
            "screenshots/formula-submitted-persistence-tour/formula_submitted_persistence_tour_manifest.json",
            "screenshots/formula-submitted-persistence-tour/freex_formula_submitted_persistence_saved.fxl",
        };

        foreach (var evidencePath in evidencePaths)
        {
            catalog.Should().Contain(evidencePath);
            File.Exists(WorkspaceFileLocator.Find(evidencePath.Split('/'))).Should().BeTrue(evidencePath);
        }

        WorkspaceFileLocator
            .ReadAllText("screenshots", "formula-submitted-persistence-tour", "formula_submitted_persistence_tour_manifest.json")
            .Should().Contain("interactive:formula-submitted-persistence:reopened-persisted-formulas-names");
    }

    [Fact]
    public void FileCatalogRows_DocumentBackstageWorkflowEvidenceArtifacts()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var evidencePaths = new[]
        {
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_new_entry_focused.png",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_new_workbook_result.png",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_open_recent_filtered_list.png",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_open_pinned_list.png",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_save_as_native_dialog_guard.png",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_saved_title_path_info.png",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_reopened_workbook_title_path.png",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_print_entry_settings.png",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_print_preview_summary.png",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_export_entry_output_ready.png",
            "screenshots/file-backstage-workflows-tour/file_backstage_workflows_tour_manifest.json",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_workflows_saved.xlsx",
            "screenshots/file-backstage-workflows-tour/freex_file_backstage_workflows_export.pdf",
        };

        catalog.Should().Contain("screenshots/file-backstage-workflows-tour/");
        foreach (var evidencePath in evidencePaths)
        {
            catalog.Should().Contain(Path.GetFileName(evidencePath));
            File.Exists(WorkspaceFileLocator.Find(evidencePath.Split('/'))).Should().BeTrue(evidencePath);
        }

        var manifest = WorkspaceFileLocator.ReadAllText(
            "screenshots",
            "file-backstage-workflows-tour",
            "file_backstage_workflows_tour_manifest.json");
        manifest.Should().Contain("interactive:file-backstage-workflows:reopened-workbook-title-path");
        manifest.Should().Contain("MissingRecentFiltered");
        manifest.Should().Contain("ExportedPdfPageCount");
    }

    [Fact]
    public void ContextualObjectCatalogRow_DocumentsChartScreenshotEvidenceAndObjectTabGap()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var row = Regex
            .Split(catalog, "\\r?\\n")
            .Single(line => line.StartsWith("| UI-CAT-CONTEXT-003 |", StringComparison.Ordinal));

        row.Should().Contain("screenshots/contextual-table-tour/900_Table_Design.png");
        row.Should().Contain("screenshots/contextual-chart-tour/900_Chart_Design.png");
        row.Should().Contain("screenshots/contextual-chart-tour/900_Chart_Format.png");
        row.Should().Contain("screenshots/contextual-chart-tour/ribbon_screenshot_tour_manifest.json");
        row.Should().Contain("dedicated object contextual tabs");
        row.Should().Contain("Draw/context menus");
    }

    [Fact]
    public void WorksheetContextCatalogRows_DocumentSubmittedCommandEvidenceArtifacts()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var lines = catalog.Split('\n');
        var categoryRow = FindCatalogRow(lines, "UI-CAT-CONTEXT-001");
        var commandRow = FindCatalogRow(lines, "UI-CAT-CONTEXT-001C");
        var evidencePaths = new[]
        {
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_note_menu_available.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_delete_note_result.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_resolve_comment_result.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_hyperlink_menu_available.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_remove_hyperlink_result.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_clear_contents_result.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_insert_row_above_result.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_delete_column_result.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_protected_clear_blocked.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_undo_restored_delete_column.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_redo_reapplied_delete_column.png",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_reopened_persistence_result.png",
            "screenshots/worksheet-context-submitted-tour/worksheet_context_submitted_tour_manifest.json",
            "screenshots/worksheet-context-submitted-tour/freex_worksheet_context_submitted_saved.fxl",
        };

        categoryRow.Should().Contain("FREEX_WORKSHEET_CONTEXT_SUBMITTED_TOUR=1");
        categoryRow.Should().Contain("worksheet-context-submitted-tour");
        categoryRow.Should().Contain("protected locked-cell rejection");
        commandRow.Should().Contain("FREEX_WORKSHEET_CONTEXT_SUBMITTED_TOUR=1");
        commandRow.Should().Contain("freex_worksheet_context_submitted_clear_contents_result.png");
        commandRow.Should().Contain("freex_worksheet_context_submitted_saved.fxl");
        commandRow.Should().Contain("protected disabled menu-state modeling");

        foreach (var evidencePath in evidencePaths)
        {
            catalog.Should().Contain(evidencePath);
            File.Exists(WorkspaceFileLocator.Find(evidencePath.Split('/'))).Should().BeTrue(evidencePath);
        }

        WorkspaceFileLocator
            .ReadAllText("screenshots", "worksheet-context-submitted-tour", "worksheet_context_submitted_tour_manifest.json")
            .Should().Contain("context-menu:worksheet-submitted-mutation-evidence");
    }

    [Fact]
    public void InsertObjectsLinksCatalogRows_DocumentFreeXVisualEvidenceTour()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var lines = catalog.Split('\n');
        var categoryRow = FindCatalogRow(lines, "UI-CAT-INSERT-003");
        var objectsRow = FindCatalogRow(lines, "UI-CMD-INSERT-008");
        var hyperlinkSymbolRow = FindCatalogRow(lines, "UI-CMD-INSERT-009");
        var commentNoteRow = FindCatalogRow(lines, "UI-CMD-INSERT-010");

        categoryRow.Should().Contain("FREEX_INSERT_OBJECTS_LINKS_TOUR=1");
        categoryRow.Should().Contain("screenshots/insert-objects-links-tour/");
        categoryRow.Should().Contain("insert_objects_links_tour_manifest.json");
        categoryRow.Should().Contain("Hyperlink dialog");
        categoryRow.Should().Contain("Symbol picker");
        categoryRow.Should().Contain("picture placeholder");
        categoryRow.Should().Contain("comment/note");

        objectsRow.Should().Contain("freex_insert_objects_grid_visuals.png");
        objectsRow.Should().Contain("shape/text box/picture placeholder");
        hyperlinkSymbolRow.Should().Contain("freex_insert_hyperlink_dialog_address_focus.png");
        hyperlinkSymbolRow.Should().Contain("freex_insert_symbol_picker_opened.png");
        commentNoteRow.Should().Contain("freex_insert_new_comment_inline_popup.png");
        commentNoteRow.Should().Contain("freex_insert_new_note_inline_popup.png");
        commentNoteRow.Should().Contain("freex_insert_comments_list_surface.png");
        commentNoteRow.Should().Contain("freex_insert_notes_list_surface.png");
    }

    private static string FindCatalogRow(IEnumerable<string> lines, string rowId) =>
        lines.Single(line => line.TrimStart().StartsWith($"| {rowId} |", StringComparison.Ordinal));

    [Fact]
    public void DataCatalogRows_DocumentWhatIfWorkflowsTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-DATA-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-DIALOG-001A |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-DATA-006 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => row.Contains("data-what-if-workflows-tour"));

        catalog.Should().Contain("FREEX_DATA_WHAT_IF_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("data_what_if_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_data_what_if_workflows_goal_seek_status_success.png");
        catalog.Should().Contain("freex_data_what_if_workflows_scenario_summary_report.png");
        catalog.Should().Contain("freex_data_what_if_workflows_data_table_one_variable_result.png");
        catalog.Should().Contain("foreground-only");
    }

    [Fact]
    public void HomeCatalogRows_DocumentStylePersistenceTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-HOME-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-HOME-003 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-HOME-FONT-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-HOME-FONT-003 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-HOME-FONT-004 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-HOME-ALIGN-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-HOME-NUM-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-HOME-NUM-003 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-HOME-STYLE-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-HOME-STYLE-003 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(10);
        rows.Should().OnlyContain(row => row.Contains("home-style-persistence-tour"));

        catalog.Should().Contain("FREEX_HOME_STYLE_PERSISTENCE_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("home_style_persistence_tour_manifest.json");
        catalog.Should().Contain("freex_home_style_persistence_applied_home_style_result.png");
        catalog.Should().Contain("freex_home_style_persistence_saved_native_workbook.png");
        catalog.Should().Contain("freex_home_style_persistence_reopened_grid.png");
        catalog.Should().Contain("freex_home_style_persistence_saved.fxl");
        catalog.Should().Contain("foreground-only dropdown/keytip gaps");
    }

    [Fact]
    public void ViewCatalogRows_DocumentViewPanesZoomTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-VIEW-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-VIEW-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-VIEW-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-VIEW-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-VIEW-003 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-VIEW-004 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(6);
        foreach (var row in rows)
        {
            row.Should().Contain("view-panes-zoom-tour");
        }

        catalog.Should().Contain("FREEX_VIEW_PANES_ZOOM_TOUR=1");
        catalog.Should().Contain("view_panes_zoom_tour_manifest.json");
        catalog.Should().Contain("freex_view_panes_zoom_custom_views_dialog.png");
        catalog.Should().Contain("FREEX_VIEW_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("screenshots/view-workflows-tour/");
        catalog.Should().Contain("view_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_view_workflows_reopened_view_toggle_persistence.png");
        catalog.Should().Contain("freex_view_workflows_saved.fxl");
        catalog.Should().Contain("planned-but-blocked");
    }

    [Fact]
    public void ReviewCatalogRows_DocumentCommentsProtectionTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-REVIEW-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-REVIEW-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-REVIEW-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-REVIEW-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-REVIEW-003 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-REVIEW-004 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(6);
        rows.Should().OnlyContain(row => row.Contains("review-comments-protection-tour") || row.Contains("review-stats-share-tour"));

        catalog.Should().Contain("FREEX_REVIEW_COMMENTS_PROTECTION_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("dotnet run --project src/FreeX.App.Host/FreeX.App.Host.csproj --configuration Release");
        catalog.Should().Contain("review_comments_protection_tour_manifest.json");
        catalog.Should().Contain("freex_review_allow_edit_ranges_dialog.png");
        catalog.Should().Contain("Thesaurus is documented as not currently surfaced");
    }

    [Fact]
    public void ReviewCatalogRows_DocumentStatsShareTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-REVIEW-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-REVIEW-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-REVIEW-005 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => row.Contains("review-stats-share-tour"));

        catalog.Should().Contain("FREEX_REVIEW_STATS_SHARE_TOUR=1");
        catalog.Should().Contain("review_stats_share_tour_manifest.json");
        catalog.Should().Contain("freex_review_workbook_statistics_dialog.png");
        catalog.Should().Contain("freex_review_share_unsaved_guard_status.png");
        catalog.Should().Contain("freex_review_share_saved_ready_status.png");
    }

    [Fact]
    public void ReviewCatalogRows_DocumentProtectionMatrixTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var row = Regex
            .Split(catalog, "\\r?\\n")
            .Single(line => line.StartsWith("| UI-CMD-REVIEW-006 |", StringComparison.Ordinal));

        row.Should().Contain("FREEX_REVIEW_PROTECTION_MATRIX_TOUR=1");
        row.Should().Contain("screenshots/review-protection-matrix-tour/");
        row.Should().Contain("review_protection_matrix_tour_manifest.json");
        catalog.Should().Contain("freex_review_protection_matrix_protected_disabled_state.png");
        catalog.Should().Contain("freex_review_protection_matrix_locked_cell_blocked.png");
        catalog.Should().Contain("freex_review_protection_matrix_unlocked_cell_allowed.png");
        catalog.Should().Contain("freex_review_protection_matrix_allow_range_allowed.png");
        catalog.Should().Contain("freex_review_protection_matrix_reopened_persistence.png");
    }

    [Fact]
    public void InsertCatalogRows_DocumentTablesChartsTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-INSERT-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-INSERT-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-INSERT-001A |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-INSERT-001D |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-INSERT-002A |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(5);
        rows.Should().OnlyContain(row => row.Contains("insert-tables-charts-tour"));

        catalog.Should().Contain("FREEX_INSERT_TABLES_CHARTS_TOUR=1");
        catalog.Should().Contain("insert_tables_charts_tour_manifest.json");
        catalog.Should().Contain("freex_insert_tables_charts_create_table_dialog.png");
        catalog.Should().Contain("freex_insert_tables_charts_recommended_charts_dialog.png");
        catalog.Should().Contain("freex_insert_tables_charts_sparkline_result.png");
    }

    [Fact]
    public void TableCatalogRows_DocumentTableWorkflowsTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var lines = Regex.Split(catalog, "\\r?\\n");
        var rows = new[]
        {
            lines.Single(line => line.StartsWith("| UI-CAT-HOME-003 |", StringComparison.Ordinal)),
            lines.Single(line => line.StartsWith("| UI-CAT-INSERT-001 |", StringComparison.Ordinal)),
            lines.Single(line => line.StartsWith("| UI-CAT-INSERT-001D |", StringComparison.Ordinal)),
            lines.Single(line => line.StartsWith("| UI-CMD-HOME-STYLE-002 |", StringComparison.Ordinal)),
            lines.Single(line => line.StartsWith("| UI-CMD-INSERT-004 |", StringComparison.Ordinal))
        };

        foreach (var row in rows)
            row.Should().Contain("table-workflows-tour");

        catalog.Should().Contain("FREEX_TABLE_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("table_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_table_workflows_create_table_submitted_result.png");
        catalog.Should().Contain("freex_table_workflows_filter_totals_style_result.png");
        catalog.Should().Contain("freex_table_workflows_reopened_persisted_table.png");
        catalog.Should().Contain("freex_table_workflows_saved.xlsx");
    }

    [Fact]
    public void InsertCatalogRows_DocumentPivotOptionsSlicerTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-INSERT-001B |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-INSERT-001C |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-INSERT-001E-H |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-INSERT-011 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-INSERT-013 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-INSERT-014 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(6);
        rows.Should().OnlyContain(row => row.Contains("pivot-options-slicer-tour"));

        catalog.Should().Contain("FREEX_PIVOT_OPTIONS_SLICER_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("pivot_options_slicer_tour_manifest.json");
        catalog.Should().Contain("freex_pivotchart_field_button_menu_opened.png");
    }

    [Fact]
    public void InsertCatalogRows_DocumentPivotAdvancedWorkflowsTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var lines = Regex.Split(catalog, "\\r?\\n");
        var rows = new[]
        {
            lines.Single(line => line.StartsWith("| UI-CAT-INSERT-001 |", StringComparison.Ordinal)),
            lines.Single(line => line.StartsWith("| UI-CAT-CONTEXT-003 |", StringComparison.Ordinal))
        };

        rows.Should().OnlyContain(row => row.Contains("pivot-advanced-workflows-tour"));

        catalog.Should().Contain("FREEX_PIVOT_ADVANCED_WORKFLOWS_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("pivot_advanced_workflows_tour_manifest.json");
        catalog.Should().Contain("freex_pivot_advanced_label_value_filters_submitted.png");
        catalog.Should().Contain("freex_pivot_advanced_value_field_settings_result.png");
        catalog.Should().Contain("freex_pivot_advanced_reopened_persisted_pivot.png");
        catalog.Should().Contain("freex_pivot_advanced_workflows_saved.xlsx");
    }

    [Fact]
    public void InsertCatalogRows_DocumentChartDataLayoutTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-INSERT-002B |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-INSERT-002C |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-INSERT-016 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => row.Contains("chart-data-layout-tour"));

        catalog.Should().Contain("FREEX_CHART_DATA_LAYOUT_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("chart_data_layout_tour_manifest.json");
        catalog.Should().Contain("freex_chart_data_layout_select_data_dialog.png");
        catalog.Should().Contain("freex_chart_data_layout_change_chart_type_dialog.png");
        catalog.Should().Contain("freex_chart_data_layout_waterfall_point_context_menu.png");
    }

    [Fact]
    public void InsertCatalogRows_DocumentChartPersistenceRenderTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-INSERT-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-INSERT-002B |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-INSERT-002C |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-INSERT-016 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(4);
        rows.Should().OnlyContain(row => row.Contains("chart-persistence-render-tour"));

        catalog.Should().Contain("FREEX_CHART_PERSISTENCE_RENDER_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("chart_persistence_render_tour_manifest.json");
        catalog.Should().Contain("freex_chart_persistence_render_mutated_rendered_chart.png");
        catalog.Should().Contain("freex_chart_persistence_render_reopened_rendered_chart.png");
        catalog.Should().Contain("freex_chart_persistence_render_saved.fxl");
    }

    [Fact]
    public void PageCatalogRows_DocumentPageLayoutSetupTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-PAGE-001A |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-PAGE-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-PAGE-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-PAGE-003 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-PAGE-005 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-PAGE-006 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(6);
        foreach (var row in rows)
        {
            row.Should().Contain("page-layout-setup-tour");
        }

        catalog.Should().Contain("FREEX_PAGE_LAYOUT_SETUP_TOUR=1");
        catalog.Should().Contain("FREEX_PAGE_LAYOUT_OUTPUT_TOUR=1");
        catalog.Should().Contain("page_layout_setup_tour_manifest.json");
        catalog.Should().Contain("page_layout_output_tour_manifest.json");
        catalog.Should().Contain("freex_page_layout_setup_dialog_sheet_tab_print_titles.png");
        catalog.Should().Contain("freex_page_layout_output_print_preview_summary.png");
    }

    [Fact]
    public void DrawCatalogRows_DocumentObjectFormattingTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-DRAW-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-DRAW-001A |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-DRAW-001B |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-DRAW-001C |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-DRAW-001 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-DRAW-002 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-DRAW-003 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-DRAW-004 |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-DRAW-005 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(9);
        foreach (var row in rows)
        {
            row.Should().Contain("draw-object-formatting-tour");
        }

        catalog.Should().Contain("FREEX_DRAW_OBJECT_FORMATTING_TOUR=1");
        catalog.Should().Contain("FREEX_DRAW_OBJECT_PERSISTENCE_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("draw_object_formatting_tour_manifest.json");
        catalog.Should().Contain("draw_object_persistence_tour_manifest.json");
        catalog.Should().Contain("freex_draw_object_formatting_shape_effects_dialog.png");
        catalog.Should().Contain("freex_draw_object_formatting_selection_pane_rename_visibility.png");
        catalog.Should().Contain("freex_draw_object_persistence_reopened_persisted_objects.png");
        catalog.Should().Contain("freex_draw_object_persistence_saved.fxl");
    }

    [Fact]
    public void RibbonOverflowKeytipRows_DocumentOverflowAndCancellationTourEvidence()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var rows = Regex
            .Split(catalog, "\\r?\\n")
            .Where(line =>
                line.StartsWith("| UI-CAT-RIBBON-002A |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CAT-RIBBON-002B |", StringComparison.Ordinal) ||
                line.StartsWith("| UI-CMD-KEYTIP-001 |", StringComparison.Ordinal))
            .ToArray();

        rows.Should().HaveCount(3);
        foreach (var row in rows)
        {
            row.Should().Contain("ribbon-overflow-keytip-tour");
        }

        catalog.Should().Contain("FREEX_RIBBON_OVERFLOW_KEYTIP_TOUR=1");
        catalog.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1");
        catalog.Should().Contain("ribbon_overflow_keytip_tour_manifest.json");
        catalog.Should().Contain("freex_ribbon_overflow_home_editing_menu.png");
        catalog.Should().Contain("freex_ribbon_overflow_insert_charts_menu.png");
        catalog.Should().Contain("freex_ribbon_overflow_view_window_menu.png");
        catalog.Should().Contain("freex_keytip_escape_after_cancel.png");
        catalog.Should().Contain("freex_keytip_narrow_home_collapsed_badges.png");
    }

    [Theory]
    [InlineData("screenshot_excel.ps1")]
    [InlineData("screenshot_ribbon.ps1")]
    public void ScreenshotScripts_DefineForegroundOwnershipGuard(string scriptName)
    {
        var script = ReadScreenshotToolScript(scriptName);

        // The foreground-ownership guard function itself lives only in the shared
        // tools/ScreenshotCaptureSupport.ps1 (dot-sourced below) since "Centralize screenshot
        // foreground helpers" (9540d1f960); Test-ToolScripts.ps1 forbids scenario scripts from
        // redeclaring it. This test asserts the script wires into that shared guard rather than
        // requiring a local `function Assert-ForegroundWindowOwnership` declaration.
        script.Should().Contain("GetForegroundWindow");
        script.Should().Contain("ScreenshotCaptureSupport.ps1");
        script.Should().Contain("Assert-ForegroundWindowOwnership");
        script.Should().Contain("GetWindowThreadProcessId($foreground");
        script.Should().Contain("GetWindowText($foreground");
    }

    [Fact]
    public void ExcelScreenshotScript_ChecksForegroundOwnershipBeforeEveryGlobalInput()
    {
        var lines = WorkspaceFileLocator.ReadAllLines("tools", "screenshot_excel.ps1");

        for (var index = 0; index < lines.Length; index++)
        {
            if (!GlobalInputCall().IsMatch(lines[index]))
                continue;

            PreviousExecutableLine(lines, index).Should().Contain(
                "Assert-ForegroundWindowOwnership",
                $"global input on line {index + 1} must re-check foreground process and title immediately before sending input");
        }
    }

    [Fact]
    public void ExcelScreenshotScript_DoesNotSwallowGlobalInputFailures()
    {
        var script = ReadScreenshotToolScript("screenshot_excel.ps1");

        script.Should().NotContain("catch {}", "foreground guard failures must abort and discard invalid screenshots");
    }

    private static IReadOnlyDictionary<string, InventorySnapshotRow> ReadInventorySnapshot()
    {
        var lines = WorkspaceFileLocator.ReadAllLines("docs", "testing/ui-test-catalog.md");
        var heading = Array.IndexOf(lines, "## Inventory Snapshot");
        heading.Should().BeGreaterThanOrEqualTo(0);

        return lines
            .Skip(heading + 1)
            .SkipWhile(line => !line.StartsWith("| Source |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(SplitMarkdownRow)
            .Where(columns => columns.Count == 3 && int.TryParse(columns[1], CultureInfo.InvariantCulture, out _))
            .ToDictionary(
                columns => columns[0],
                columns => new InventorySnapshotRow(
                    int.Parse(columns[1], CultureInfo.InvariantCulture),
                    columns[2]),
                StringComparer.Ordinal);
    }

    private static CommandInventory ReadCommandInventory()
    {
        var json = WorkspaceFileLocator.ReadAllText("docs", "parity/command-inventory.json");
        return JsonSerializer.Deserialize<CommandInventory>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Command inventory is empty.");
    }

    private static CommandCoverageSummary ReadCommandCoverageSummary(string fileName)
    {
        var lines = WorkspaceFileLocator.ReadAllLines("docs", fileName);
        var total = lines
            .Select(SplitMarkdownRow)
            .Single(columns => columns.Count >= 6 && columns[0] == "**TOTAL**");

        return new CommandCoverageSummary(
            ParseBoldInt(total[1]),
            ParseBoldInt(total[2]),
            ParseBoldInt(total[3]),
            ParseBoldInt(total[4]),
            ParseBoldInt(total[5]));
    }

    private static ShortcutSummary ReadShortcutSummary()
    {
        var lines = WorkspaceFileLocator.ReadAllLines("docs", "parity/shortcuts.md");

        return new ShortcutSummary(
            ReadShortcutSummaryCount(lines, "Parity"),
            ReadShortcutSummaryCount(lines, "Partial"),
            ReadShortcutSummaryCount(lines, "Not Implemented"),
            ReadShortcutSummaryCount(lines, "Excluded"),
            ReadShortcutSummaryCount(lines, "**Total in-scope**"));
    }

    private static IReadOnlyList<ShortcutRow> ReadShortcutRows()
    {
        var lines = WorkspaceFileLocator.ReadAllLines("docs", "parity/shortcuts.md");
        var tableStart = Array.FindIndex(lines, line => line.StartsWith("| Area | Excel Shortcut |", StringComparison.Ordinal));
        tableStart.Should().BeGreaterThanOrEqualTo(0);

        return lines
            .Skip(tableStart + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(SplitMarkdownRow)
            .Where(columns => columns.Count >= 4)
            .Select(columns => new ShortcutRow(columns[0], columns[1], columns[2]))
            .Where(row => row.Status is "Parity" or "Partial" or "Not Implemented" or "Missing" or "Excluded")
            .ToArray();
    }

    private static IReadOnlyList<string> ReadVisibleTopLevelRibbonTabs()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        return document
            .Descendants(presentation + "TabItem")
            .Where(tab => tab.Attribute("Visibility")?.Value != "Collapsed")
            .Select(tab => tab.Attribute("Header")?.Value)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Cast<string>()
            .Select(header => LocalizedXamlTestSupport.ResolveLocalizedValue(header) ?? header)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadContextualRibbonTabs()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        return document
            .Descendants(presentation + "TabItem")
            .Where(tab => tab.Attribute("Visibility")?.Value == "Collapsed")
            .Select(tab => tab.Attribute("Header")?.Value)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Cast<string>()
            .Select(header => LocalizedXamlTestSupport.ResolveLocalizedValue(header) ?? header)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadDialogTypeNames()
    {
        var hostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var dialogNames = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var sourceFile in Directory.EnumerateFiles(hostDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var source = File.ReadAllText(sourceFile);
            foreach (Match match in DialogClassDeclaration().Matches(source))
            {
                dialogNames.Add(match.Groups["name"].Value);
            }
        }

        foreach (var xamlFile in Directory.EnumerateFiles(hostDirectory, "*.xaml", SearchOption.TopDirectoryOnly))
        {
            var xaml = File.ReadAllText(xamlFile);
            foreach (Match match in DialogXamlClassDeclaration().Matches(xaml))
            {
                dialogNames.Add(match.Groups["name"].Value.Split('.').Last());
            }
        }

        return dialogNames.ToArray();
    }

    private static int ReadMainWindowXamlClickHandlerCount()
        => RibbonXamlCatalogSnapshotReader.ReadMainWindowSnapshot().ClickHandlerCount;

    private static int ReadMainWindowXamlAutomationIdCount()
        => RibbonXamlCatalogSnapshotReader.ReadMainWindowSnapshot().AutomationIdCount;

    private static int ReadMainWindowXamlRibbonKeyTipCount()
        => RibbonXamlCatalogSnapshotReader.ReadMainWindowSnapshot().RibbonKeyTipCount;

    private static KeyboardShortcutUsageCounts ReadKeyboardShortcutUsageCounts()
    {
        var matcher = DialogSourceTestSupport.ReadHostSources("KeyboardShortcutMatcher.CommandRules.cs");
        var dispatcher = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

        return new KeyboardShortcutUsageCounts(
            CommandShortcutRuleDeclaration().Matches(matcher).Count,
            KeyboardCommandDispatcherRegistration().Matches(dispatcher).Count);
    }

    private static IReadOnlyList<string> ReadDocumentedScreenshotToolScripts()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var scripts = ScreenshotToolPath()
            .Matches(catalog)
            .Select(match => match.Groups["script"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var script in scripts)
        {
            File.Exists(WorkspaceFileLocator.FindToolScript(script)).Should().BeTrue();
        }

        return scripts;
    }

    private static string ReadScreenshotToolScript(string scriptName) =>
        WorkspaceFileLocator.ReadAllText("tools", scriptName);

    private static string PreviousExecutableLine(IReadOnlyList<string> lines, int index)
    {
        for (var previous = index - 1; previous >= 0; previous--)
        {
            var line = lines[previous].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            return line;
        }

        return string.Empty;
    }

    private static int ReadUiEvidenceScreenshotCount()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var artifactDirectory = Path.Combine(docsDirectory, "ui-test-artifacts");

        return Directory
            .EnumerateFiles(artifactDirectory, "*.png", SearchOption.TopDirectoryOnly)
            .Count();
    }

    private static int CountWorksheetContextMenuActionCommands(IEnumerable<WorksheetContextMenuCommand> commands)
    {
        var count = 0;
        foreach (var command in commands)
        {
            if (!command.IsSeparator && command.Action != WorksheetContextMenuAction.None)
                count++;

            count += CountWorksheetContextMenuActionCommands(command.Children);
        }

        return count;
    }

    private static int ReadShortcutSummaryCount(IReadOnlyList<string> lines, string label)
    {
        var row = lines.Single(line => line.StartsWith($"| {label} |", StringComparison.Ordinal));
        return ParseBoldInt(SplitMarkdownRow(row)[1]);
    }

    private static CommandCoverageSummary Summarize(IReadOnlyList<CommandInventoryTab> tabs) =>
        new(
            tabs.Sum(tab => tab.Implemented),
            tabs.Sum(tab => tab.Partial),
            tabs.Sum(tab => tab.NotImplemented),
            tabs.Sum(tab => tab.Deferred),
            tabs.Sum(tab => tab.Excluded));

    private static void AssertSnapshotRow(
        IReadOnlyDictionary<string, InventorySnapshotRow> snapshot,
        string source,
        int count,
        string notes)
    {
        snapshot.Should().ContainKey(source);
        snapshot[source].Should().Be(new InventorySnapshotRow(count, notes));
    }

    private static int ParseBoldInt(string text) =>
        int.Parse(text.Trim('*'), CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> SplitMarkdownRow(string row) =>
        row.Trim().Trim('|').Split('|').Select(column => column.Trim()).ToArray();

    [GeneratedRegex(@"\bnew\(KeyboardCommandShortcut\.")]
    private static partial Regex CommandShortcutRuleDeclaration();

    [GeneratedRegex(@"_keyboardCommandDispatcher\.Register\(KeyboardCommandShortcut\.")]
    private static partial Regex KeyboardCommandDispatcherRegistration();

    [GeneratedRegex(@"\bclass\s+(?<name>[A-Za-z0-9_]*Dialog)\b")]
    private static partial Regex DialogClassDeclaration();

    [GeneratedRegex(@"x:Class=""(?<name>[A-Za-z0-9_.]*Dialog)""")]
    private static partial Regex DialogXamlClassDeclaration();

    [GeneratedRegex(@"`tools/(?<script>screenshot_(?:excel|ribbon)\.ps1)`")]
    private static partial Regex ScreenshotToolPath();

    [GeneratedRegex(@"\[System\.Windows\.Forms\.SendKeys\]::SendWait\(|\[Clicker\]::mouse_event\(")]
    private static partial Regex GlobalInputCall();

    private sealed record InventorySnapshotRow(int Count, string Notes);

    private sealed record CommandInventory(
        IReadOnlyList<CommandInventoryTab> CommandSurfaceTabs,
        IReadOnlyList<CommandInventoryTab> MenuToolbarTabs,
        CommandInventoryKeyTips KeyTips);

    private sealed record CommandInventoryTab(
        string Name,
        int Implemented,
        int Partial,
        int NotImplemented,
        int Deferred,
        int Excluded);

    private sealed record CommandInventoryKeyTips(IReadOnlyList<KeyTipExpectation> TopLevelTabs);

    private sealed record KeyTipExpectation(string Name, string KeyTip);

    private sealed record CommandCoverageSummary(
        int Implemented,
        int Partial,
        int NotImplemented,
        int Deferred,
        int Excluded);

    private sealed record ShortcutSummary(
        int Parity,
        int Partial,
        int NotImplemented,
        int Excluded,
        int TotalInScope);

    private sealed record ShortcutRow(string Area, string Shortcut, string Status);

    private sealed record KeyboardShortcutUsageCounts(int MatcherRules, int DispatcherTargets);
}
