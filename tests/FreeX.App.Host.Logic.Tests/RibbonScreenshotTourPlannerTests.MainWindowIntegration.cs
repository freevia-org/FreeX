using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RibbonScreenshotTourPlannerTests
{
    [Fact]
    public void MainWindowScreenshotTour_UsesPlannerForEnvironmentFilters()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.ScreenshotTour.cs",
            "MainWindow.ScreenshotTour.ChartDataLayout.cs",
            "MainWindow.ScreenshotTour.ChartPersistenceRender.cs",
            "MainWindow.ScreenshotTour.RibbonOverflowKeytip.cs");

        source.Should().Contain("RibbonScreenshotTourPlanner.CreatePlan");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_SS_TOUR_BURST\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_SS_TOUR_CONTEXT\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_SS_TOUR_TABS\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_SS_TOUR_WIDTHS\")");
        source.Should().Contain("ScreenshotTourOutputSubdirectoryEnvVar = \"FREEX_SS_TOUR_OUTPUT_SUBDIR\"");
        source.Should().Contain("Environment.GetEnvironmentVariable(ScreenshotTourOutputSubdirectoryEnvVar)");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_AUTOFILTER_FLYOUT_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_HOME_ALIGNMENT_NUMBER_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_PRINT_PREVIEW_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_OPTIONS_ACCOUNT_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_HELP_ABOUT_LEGAL_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_QAT_UNDO_REDO_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_TITLEBAR_WINDOW_CHROME_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_FORMULA_BAR_NAME_BOX_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_GRID_SELECTION_EDITING_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_STATUS_FOOTER_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_INSERT_OBJECTS_LINKS_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_DATA_TOOLS_DIALOGS_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_DATA_SUBMITTED_WORKFLOWS_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_CHART_DATA_LAYOUT_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_CHART_PERSISTENCE_RENDER_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_VIEW_PANES_ZOOM_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_REVIEW_COMMENTS_PROTECTION_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_PAGE_LAYOUT_SETUP_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_DRAW_OBJECT_FORMATTING_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_HOME_FONT_COLORS_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_HOME_STYLES_CF_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_HOME_CLIPBOARD_CELLS_EDITING_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_RIBBON_OVERFLOW_KEYTIP_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_FORMULA_AUTHORING_NAMES_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_PIVOT_OPTIONS_SLICER_TOUR\")");
        source.Should().Contain("RibbonScreenshotTourPlan?");
        source.Should().Contain("ResolveScreenshotTourOutputDirectory");
        source.Should().Contain("PrepareRibbonScreenshotTourContextAsync");
        source.Should().Contain("case \"drawing\":");
        source.Should().Contain("EnsureDrawObjectFormattingTourContext");
        source.Should().Contain("PrepareRibbonScreenshotTourTabContext(capture)");
        source.Should().Contain("case \"ShapeFormatTab\":");
        source.Should().Contain("case \"PictureFormatTab\":");
        source.Should().Contain("SelectDrawObjectFormattingPicture(context)");
        source.Should().Contain("EnsureTableDesignScreenshotTourContext");
        source.Should().Contain("EnsurePivotTableScreenshotTourContext");
        source.Should().Contain("EnsureChartScreenshotTourContext");
        source.Should().Contain("SheetGrid.SelectedObjectId = chart.Id");
        source.Should().Contain("SheetGrid.SelectedObjectKind = FreeX.App.UI.ObjectKind.Chart");
        source.Should().Contain("new AddChartCommand(sheet.Id, sourceRange, ChartType.Column, ScreenshotTourChartName)");
        source.Should().Contain("FindScreenshotTourChart");
        source.Should().Contain("CaptureAutoFilterFlyoutTourAsync");
        source.Should().Contain("CaptureHomeAlignmentNumberTourAsync");
        source.Should().Contain("CapturePrintPreviewTourAsync");
        source.Should().Contain("CaptureOptionsAccountTourAsync");
        source.Should().Contain("CaptureHelpAboutLegalTourAsync");
        source.Should().Contain("CaptureQatUndoRedoTourAsync");
        source.Should().Contain("CaptureTitlebarWindowChromeTourAsync");
        source.Should().Contain("CaptureFormulaBarNameBoxTourAsync");
        source.Should().Contain("CaptureGridSelectionEditingTourAsync");
        source.Should().Contain("CaptureStatusFooterTourAsync");
        source.Should().Contain("CaptureInsertObjectsLinksTourAsync");
        source.Should().Contain("CaptureDataToolsDialogsTourAsync");
        source.Should().Contain("CaptureDataSubmittedWorkflowsTourAsync");
        source.Should().Contain("CaptureViewPanesZoomTourAsync");
        source.Should().Contain("CaptureReviewCommentsProtectionTourAsync");
        source.Should().Contain("CapturePageLayoutSetupTourAsync");
        source.Should().Contain("CaptureDrawObjectFormattingTourAsync");
        source.Should().Contain("CaptureChartDataLayoutTourAsync");
        source.Should().Contain("CaptureChartPersistenceRenderTourAsync");
        source.Should().Contain("CaptureHomeFontColorsTourAsync");
        source.Should().Contain("CaptureHomeStylesConditionalFormattingTourAsync");
        source.Should().Contain("CaptureHomeClipboardCellsEditingTourAsync");
        source.Should().Contain("CaptureRibbonOverflowKeytipTourAsync");
        source.Should().Contain("CaptureFormulaAuthoringNamesTourAsync");
        source.Should().Contain("CapturePivotOptionsSlicerTourAsync");
        source.Should().Contain("PrepareRibbonBurstCapturePhaseAsync");
        source.Should().Contain("WaitForRibbonScreenshotRenderPassAsync");
        source.Should().Contain("DeleteStaleRibbonScreenshotTourCaptures");
        source.Should().Contain("DeleteRibbonScreenshotTourEvidence");
        source.Should().Contain("ValidateRibbonScreenshotTourCaptures");
        source.Should().Contain("DeleteAutoFilterFlyoutTourEvidence");
        source.Should().Contain("DeletePrintPreviewTourEvidence");
        source.Should().Contain("DeleteOptionsAccountTourEvidence");
        source.Should().Contain("DeleteHelpAboutLegalTourEvidence");
        source.Should().Contain("WriteRibbonScreenshotTourManifestAsync");
        source.Should().Contain("WriteAutoFilterFlyoutTourManifestAsync");
        source.Should().Contain("WriteHomeAlignmentNumberTourManifestAsync");
        source.Should().Contain("WritePrintPreviewTourManifestAsync");
        source.Should().Contain("WriteOptionsAccountTourManifestAsync");
        source.Should().Contain("WriteHelpAboutLegalTourManifestAsync");
        source.Should().Contain("WriteQatUndoRedoTourManifestAsync");
        source.Should().Contain("WriteTitlebarWindowChromeTourManifestAsync");
        source.Should().Contain("WriteFormulaBarNameBoxTourManifestAsync");
        source.Should().Contain("WriteGridSelectionEditingTourManifestAsync");
        source.Should().Contain("WriteStatusFooterTourManifestAsync");
        source.Should().Contain("WriteInsertObjectsLinksTourManifestAsync");
        source.Should().Contain("WriteDataToolsDialogsTourManifestAsync");
        source.Should().Contain("WriteViewPanesZoomTourManifestAsync");
        source.Should().Contain("WriteReviewCommentsProtectionTourManifestAsync");
        source.Should().Contain("WritePageLayoutSetupTourManifestAsync");
        source.Should().Contain("WriteDrawObjectFormattingTourManifestAsync");
        source.Should().Contain("WriteFormulaAuthoringNamesTourManifestAsync");
        source.Should().Contain("ribbon_screenshot_tour_manifest.json");
        source.Should().Contain("autofilter_flyout_tour_manifest.json");
        source.Should().Contain("home_alignment_number_tour_manifest.json");
        source.Should().Contain("print_preview_tour_manifest.json");
        source.Should().Contain("options_account_tour_manifest.json");
        source.Should().Contain("help_about_legal_tour_manifest.json");
        source.Should().Contain("qat_undo_redo_tour_manifest.json");
        source.Should().Contain("titlebar_window_chrome_tour_manifest.json");
        source.Should().Contain("formula_bar_name_box_tour_manifest.json");
        source.Should().Contain("grid_selection_editing_tour_manifest.json");
        source.Should().Contain("status_footer_tour_manifest.json");
        source.Should().Contain("insert_objects_links_tour_manifest.json");
        source.Should().Contain("data_tools_dialogs_tour_manifest.json");
        source.Should().Contain("data_submitted_workflows_tour_manifest.json");
        source.Should().Contain("view_panes_zoom_tour_manifest.json");
        source.Should().Contain("review_comments_protection_tour_manifest.json");
        source.Should().Contain("page_layout_setup_tour_manifest.json");
        source.Should().Contain("draw_object_formatting_tour_manifest.json");
        source.Should().Contain("chart_data_layout_tour_manifest.json");
        source.Should().Contain("chart_persistence_render_tour_manifest.json");
        source.Should().Contain("home_font_colors_tour_manifest.json");
        source.Should().Contain("home_styles_conditional_formatting_tour_manifest.json");
        source.Should().Contain("home_clipboard_cells_editing_tour_manifest.json");
        source.Should().Contain("ribbon_overflow_keytip_tour_manifest.json");
        source.Should().Contain("formula_authoring_names_tour_manifest.json");
        source.Should().Contain("pivot_options_slicer_tour_manifest.json");
        source.Should().Contain("EvidencePurpose()");
        source.Should().Contain("EnsureWindowForegroundForScreenshotTourAsync");
        source.Should().Contain("AssertWindowForegroundForScreenshotTour");
        source.Should().Contain("GetForegroundWindow");
        source.Should().Contain("_suppressClosePrompt = true;");
        source.Should().Contain("throw new InvalidOperationException");
    }

    [Fact]
    public void RibbonOverflowKeytipTour_CapturesCollapsedMenusAndEscapeCancellation()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.ScreenshotTour.cs",
            "MainWindow.ScreenshotTour.RibbonOverflowKeytip.cs");

        source.Should().Contain("RibbonOverflowKeytipTourOutputDirectoryName = \"ribbon-overflow-keytip-tour\"");
        source.Should().Contain("HomeEditingGroup");
        source.Should().Contain("InsertChartsGroup");
        source.Should().Contain("ViewWindowGroup");
        source.Should().Contain("OpenRibbonContextMenu(collapsedButton, menu)");
        source.Should().Contain("HandleActiveRibbonKeyTip(Key.Escape)");
        source.Should().Contain("freex_keytip_escape_after_cancel");
        source.Should().Contain("IsNonBlankPng");
        source.Should().Contain("UI-CAT-RIBBON-002A");
        source.Should().Contain("UI-CAT-RIBBON-002B");
        source.Should().Contain("UI-CMD-KEYTIP-001");
    }

    [Fact]
    public void MainWindowScreenshotTour_StaleCleanupDeletesOnlyRequestedPlanCaptures()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var method = Regex.Match(
            source,
            @"private static void DeleteStaleRibbonScreenshotTourCaptures\([^)]*\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);

        method.Success.Should().BeTrue("stale cleanup should stay source-visible and plan-scoped");
        method.Groups["body"].Value.Should().Contain("foreach (var capture in plan.Captures)");
        method.Groups["body"].Value.Should().Contain("Path.Combine(outputDir, $\"{capture.FileName}.png\")");
        method.Groups["body"].Value.Should().Contain("File.Exists(path)");
        method.Groups["body"].Value.Should().Contain("File.Delete(path)");
        method.Groups["body"].Value.Should().NotContain("EnumerateFiles");
        method.Groups["body"].Value.Should().NotContain("GetFiles");
        method.Groups["body"].Value.Should().NotContain("*.png");
    }

    [Fact]
    public void MainWindowScreenshotTour_ClearsManifestOnFailureAndRecordsPairableFocusGuardedManifest()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("DeleteRibbonScreenshotTourEvidence(outputDir, plan);");
        source.Should().Contain("RibbonScreenshotTourManifestFileName");
        source.Should().Contain("ActualCaptureCount: plan.Captures.Count");
        source.Should().Contain("CaptureStatus: \"complete\"");
        source.Should().Contain("CaptureMethod: \"RenderTargetBitmap-window-top-band\"");
        source.Should().Contain("RibbonScreenshotTourManifestPairing");
        source.Should().Contain("RibbonScreenshotTourManifestFocusGuard");
        source.Should().Contain("capture.CaptureKey");
        source.Should().Contain("capture.PairKey");
        source.Should().Contain("capture.CounterpartFileName");
        source.Should().Contain("FreeX main window owns foreground focus");
        source.Should().Contain("FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER");
        source.Should().Contain("IsScreenshotTourBackgroundRenderAllowed");
        source.Should().Contain("no global mouse, keyboard, or screen capture input is used");
    }

    [Fact]
    public void MainWindowScreenshotTour_AllowsRelativeOutputSubdirectoryUnderScreenshots()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_SS_TOUR_OUTPUT_SUBDIR");
        source.Should().Contain("Path.IsPathRooted(requestedSubdirectory)");
        source.Should().Contain("must be a relative path under screenshots");
        source.Should().Contain("Path.GetFullPath(Path.Combine(root, requestedSubdirectory))");
        source.Should().Contain("resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)");
        source.Should().Contain("must stay under screenshots");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesHomeAlignmentAndNumberEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_HOME_ALIGNMENT_NUMBER_TOUR");
        source.Should().Contain("home-alignment-number-tour");
        source.Should().Contain("EnsureHomeAlignmentNumberTourContext");
        source.Should().Contain("ApplyHomeAlignmentNumberTourStyle");
        source.Should().Contain("TryExecuteApplyStyle(range, diff, \"Apply Style\")");
        source.Should().Contain("CreateMergeAndCenterCommand(mergeRange)");
        source.Should().Contain("FindRenderedRibbonControl(\"Orientation\") as Button");
        source.Should().Contain("OpenRibbonContextMenu(orientationButton, orientationButton.ContextMenu!)");
        source.Should().Contain("new FormatCellsDialog(");
        source.Should().Contain("FormatCellsDialogTab.Alignment");
        source.Should().Contain("FormatCellsDialogTab.Number");
        source.Should().Contain("freex_home_alignment_grid_commands");
        source.Should().Contain("freex_home_alignment_orientation_menu_opened");
        source.Should().Contain("freex_home_number_format_grid_commands");
        source.Should().Contain("freex_home_alignment_format_cells_dialog");
        source.Should().Contain("freex_home_number_format_cells_dialog");
        source.Should().Contain("interactive:home-alignment-number:<State>");
        source.Should().Contain("HomeAlignmentNumberTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.HomeAlignmentNumberTourManifest");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesRealAutoFilterFlyoutEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var editingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.EditingDropdowns.cs");

        source.Should().Contain("FREEX_AUTOFILTER_FLYOUT_TOUR");
        source.Should().Contain("EnsureAutoFilterFlyoutTourContext");
        source.Should().Contain("new WorksheetAutoFilterModel(range.ToString(), null)");
        source.Should().Contain("CreateAutoFilterFlyoutDialog(sheet, headerCell, null, out var plan)");
        source.Should().Contain("AutoFilterFlyoutTourCaptureFileName = \"freex_table_autofilter_dropdown\"");
        source.Should().Contain("RenderTargetBitmap-autofilter-flyout-window");
        source.Should().Contain("interactive:table-autofilter-dropdown:opened");
        source.Should().Contain("CaptureElementAsync(dialog, outputDir, AutoFilterFlyoutTourCaptureFileName)");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.AutoFilterFlyoutTourManifest");

        editingSource.Should().Contain("private AutoFilterDialog? CreateAutoFilterFlyoutDialog");
        editingSource.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan(");
        editingSource.Should().Contain("AutoFilterMenuResources.TextProvider");
        editingSource.Should().Contain("dialog.ConfigureAsModelessFlyout();");
        editingSource.Should().Contain("PositionAutoFilterFlyout(dialog, headerCell, anchorPoint);");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesQatUndoRedoStatesAndHistoryMenus()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var qatSource = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAccessToolbar.cs");

        source.Should().Contain("FREEX_QAT_UNDO_REDO_TOUR");
        source.Should().Contain("qat-undo-redo-tour");
        source.Should().Contain("ExecuteQatUndoRedoTourMutation");
        source.Should().Contain("TryExecuteEditCells([edit], \"Edit Cell\", out var editOutcome)");
        source.Should().Contain("new StyleDiff(FillColor: new CellColor(255, 242, 204), Bold: true)");
        source.Should().Contain("CaptureQatUndoRedoHistoryMenuAsync");
        source.Should().Contain("CreateQuickAccessHistoryMenu(commandId, historyButton)");
        source.Should().Contain("freex_qat_initial_disabled");
        source.Should().Contain("freex_qat_after_edit_undo_enabled");
        source.Should().Contain("freex_qat_undo_history_menu_opened");
        source.Should().Contain("freex_qat_after_one_undo_redo_enabled");
        source.Should().Contain("freex_qat_redo_history_menu_opened");
        source.Should().Contain("freex_qat_after_redo_restored");
        source.Should().Contain("interactive:qat-undo-redo:<State>");
        source.Should().Contain("UndoHistoryLabels");
        source.Should().Contain("RedoHistoryLabels");
        source.Should().Contain("MenuHeaders");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.QatUndoRedoTourManifest");

        qatSource.Should().Contain("private ContextMenu CreateQuickAccessHistoryMenu");
        qatSource.Should().Contain("OpenQuickAccessHistoryMenu(string commandId, ButtonBase placementTarget)");
        qatSource.Should().Contain("var menu = CreateQuickAccessHistoryMenu(commandId, placementTarget);");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesFormulaBarNameBoxEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_FORMULA_BAR_NAME_BOX_TOUR");
        source.Should().Contain("formula-bar-name-box-tour");
        source.Should().Contain("EnsureFormulaBarNameBoxTourContext");
        source.Should().Contain("_workbook.DefineNamedRange(\"Sales\", namedRange);");
        source.Should().Contain("Tour Name Box Shape");
        source.Should().Contain("Tour Name Box Picture");
        source.Should().Contain("Tour Name Box Text Box");
        source.Should().Contain("Tour Name Box Chart");
        source.Should().Contain("ObjectNames: [nameBoxChart, nameBoxPicture, nameBoxShape, nameBoxTextBox]");
        source.Should().Contain("CellAddressBox.IsDropDownOpen = true;");
        source.Should().Contain("NameBoxDropdownPlanner");
        source.Should().Contain("BeginFormulaBarFormulaEdit(\"=SUM(B2:C3)\");");
        source.Should().Contain("FormulaBarCancelButton_Click(FormulaBarCancelButton");
        source.Should().Contain("FormulaBarEnterButton_Click(FormulaBarEnterButton");
        source.Should().Contain("FormulaBarFxButton.Focus();");
        source.Should().Contain("new InsertFunctionDialog");
        source.Should().Contain("FormulaBarExpandBtn_Click(FormulaBarExpandBtn");
        source.Should().Contain("EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);");
        source.Should().Contain("freex_formula_name_box_named_range_selected");
        source.Should().Contain("freex_formula_name_box_dropdown_opened");
        source.Should().Contain("freex_formula_bar_edit_mode_cancel_focused");
        source.Should().Contain("freex_formula_bar_edit_mode_enter_focused");
        source.Should().Contain("freex_formula_bar_fx_insert_function_dialog");
        source.Should().Contain("freex_formula_bar_expanded");
        source.Should().Contain("freex_formula_keytips_from_name_box_focus");
        source.Should().Contain("FormulaBarNameBoxTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.FormulaBarNameBoxTourManifest");
        source.Should().Contain("The Insert Function dialog capture uses the production InsertFunctionDialog shown by the tour");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesGridSelectionEditingVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_GRID_SELECTION_EDITING_TOUR");
        source.Should().Contain("grid-selection-editing-tour");
        source.Should().Contain("EnsureGridSelectionEditingTourContext");
        source.Should().Contain("sheet.AutoFilter = new WorksheetAutoFilterModel(\"A1:E8\", null);");
        source.Should().Contain("sheet.FilterHiddenRows.Add(6);");
        source.Should().Contain("sheet.HiddenRows.Add(8);");
        source.Should().Contain("SetActiveCell(context.SelectedCell);");
        source.Should().Contain("SetSelectionRange(context.SelectedRange, context.SelectedRange.Start);");
        source.Should().Contain("SelectRow(context.RowSelectionIndex);");
        source.Should().Contain("SelectColumn(context.ColumnSelectionIndex);");
        source.Should().Contain("ShowInlineEditor(context.EditCell);");
        source.Should().Contain("CommitEdit();");
        source.Should().Contain("new FillCellsCommand(_currentSheetId, currentRange, FillCellsDirection.Down)");
        source.Should().Contain("new ClearContentsCommand(_currentSheetId, currentRange)");
        source.Should().Contain("freex_grid_selection_editing_selected_cell");
        source.Should().Contain("freex_grid_selection_editing_selected_range");
        source.Should().Contain("freex_grid_selection_editing_whole_row");
        source.Should().Contain("freex_grid_selection_editing_whole_column");
        source.Should().Contain("freex_grid_selection_editing_inline_edit_mode");
        source.Should().Contain("freex_grid_selection_editing_committed_value");
        source.Should().Contain("freex_grid_selection_editing_filtered_hidden_rows");
        source.Should().Contain("freex_grid_selection_editing_fill_down_result");
        source.Should().Contain("freex_grid_selection_editing_clear_contents_result");
        source.Should().Contain("NameBoxText: CellAddressBox.Text");
        source.Should().Contain("StatusAverageText: StatusAvgText.Text");
        source.Should().Contain("VisibleRows: visibleRows");
        source.Should().Contain("GridSelectionEditingTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.GridSelectionEditingTourManifest");
        source.Should().Contain("Whole-row and whole-column states are supported");
        source.Should().Contain("Mouse drag, Shift+click, Ctrl+multi-area, keyboard navigation shortcuts");

        catalog.Should().Contain("FREEX_GRID_SELECTION_EDITING_TOUR=1");
        catalog.Should().Contain("screenshots/grid-selection-editing-tour/");
        catalog.Should().Contain("grid_selection_editing_tour_manifest.json");
        catalog.Should().Contain("freex_grid_selection_editing_selected_range.png");
        catalog.Should().Contain("foreground mouse/keyboard and Excel-paired proof remain");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesRealPrintPreviewEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_PRINT_PREVIEW_TOUR");
        source.Should().Contain("OpenPrintBackstage();");
        source.Should().Contain("freex_print_backstage_file_print_entry");
        source.Should().Contain("freex_print_preview_ctrlp_entry_opened");
        source.Should().Contain("freex_print_preview_toolbar_first_page");
        source.Should().Contain("freex_print_preview_toolbar_last_page");
        source.Should().Contain("freex_print_preview_zoom_settings_summary");
        source.Should().Contain("freex_print_preview_closed_focus_return");
        source.Should().Contain("CreatePrintPreviewTourDialog");
        source.Should().Contain("new PrintPreviewDialog(");
        source.Should().Contain("refreshPreviewWithSettings: BuildActiveSheetPrintPreview");
        source.Should().Contain("FindDescendantByAutomationId<TextBox>(dialog, \"PrintPreviewPageNumberBox\")");
        source.Should().Contain("NavigationCommands.GoToPage.Execute(null, pageNumberBox)");
        source.Should().Contain("FindDescendantByAutomationId<ComboBox>(dialog, \"PrintPreviewZoomBox\")");
        source.Should().Contain("ClosePrintPreviewTourDialogWithEscape");
        source.Should().Contain("PrintPreviewCloseButton");
        source.Should().Contain("SsBackstagePrintNowButton.Focus();");
        source.Should().Contain("Keyboard.Focus(SsBackstagePrintNowButton);");
        source.Should().Contain("PrintPreviewTourManifest");
        source.Should().Contain("RenderTargetBitmap-print-preview-dialog-and-main-window");
        source.Should().Contain("The native Windows print dialog is not opened during this tour");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesBackstageRecentExportShareVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_BACKSTAGE_RECENT_EXPORT_SHARE_TOUR");
        source.Should().Contain("BackstageRecentExportShareTourOutputDirectoryName = \"backstage-recent-export-share-tour\"");
        source.Should().Contain("freex_backstage_open_recent_list");
        source.Should().Contain("freex_backstage_open_pinned_list");
        source.Should().Contain("freex_backstage_info_unsaved_status");
        source.Should().Contain("freex_backstage_info_unsupported_feature_save_warning");
        source.Should().Contain("freex_backstage_export_entry_focused");
        source.Should().Contain("freex_backstage_export_pdf_options");
        source.Should().Contain("freex_backstage_export_xps_options");
        source.Should().Contain("freex_backstage_share_unsaved_guard_status");
        source.Should().Contain("freex_backstage_share_saved_ready_status");
        source.Should().Contain("freex_backstage_back_to_workbook_focus_return");
        source.Should().Contain("SwitchToPinnedTab();");
        source.Should().Contain("new ExportOptionsDialog(");
        source.Should().Contain("WpfExportDescriptionPlanner.DescribeRequest(request)");
        source.Should().Contain("WorkbookShareReadinessPlanner.CreatePlan(null, WorkbookShareSurface.WindowsShare)");
        source.Should().Contain("SaveBackstageRecentExportShareTourWorkbookAsync");
        source.Should().Contain("ConfirmUnsupportedXlsxFeatureSave();");
        source.Should().Contain("CaptureBackstageOwnedNativeDialogWhenShownAsync");
        source.Should().Contain("BackstageRecentExportShareTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.BackstageRecentExportShareTourManifest");
        source.Should().Contain("The native Open dialog, native Export Save As dialog, and Windows Share UI are intentionally not launched");

        catalog.Should().Contain("FREEX_BACKSTAGE_RECENT_EXPORT_SHARE_TOUR=1");
        catalog.Should().Contain("screenshots/backstage-recent-export-share-tour/");
        catalog.Should().Contain("backstage_recent_export_share_tour_manifest.json");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesOptionsAndAccountVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_OPTIONS_ACCOUNT_TOUR");
        source.Should().Contain("options-account-tour");
        source.Should().Contain("freex_account_backstage_entry_focused");
        source.Should().Contain("freex_account_local_account_message");
        source.Should().Contain("freex_account_backstage_focus_return");
        source.Should().Contain("freex_options_default_general_category_list");
        source.Should().Contain("freex_options_formulas_category_navigation");
        source.Should().Contain("freex_options_quick_access_toolbar_category_navigation");
        source.Should().Contain("freex_options_view_category_navigation");
        source.Should().Contain("freex_options_cancel_focus_return");
        source.Should().Contain("LocalAccountPlanner.Create");
        // The backstage rail is now the shared BackstageFrame: the tour focuses the Account entry by its
        // automation id and invokes the account command rather than driving a named SsAccountNavBtn control.
        source.Should().Contain("_backstageFrame?.FocusEntry(\"BackstageAccountButton\")");
        source.Should().Contain("SsAccountBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("CaptureOwnedNativeDialogWhenShownAsync");
        source.Should().Contain("PrintWindow-owned-native-dialog");
        source.Should().Contain("new OptionsDialog(_options, _workbook.DisabledFormulaErrorCodes)");
        source.Should().Contain("FindDescendantByAutomationId<ListBox>(dialog, \"OptionsCategoryList\")");
        source.Should().Contain("OptionsCancelButton");
        source.Should().Contain("CategoryListFocusedByDefault");
        source.Should().Contain("OptionsClosedViaCancelEquivalent");
        source.Should().Contain("FocusReturnedToBackstageOptionsCommand");
        source.Should().Contain("AccountDetailLabels");
        source.Should().Contain("local OS account and app build details");
        source.Should().Contain("not a cloud account sign-in surface");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.OptionsAccountTourManifest");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesHelpAboutLegalVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_HELP_ABOUT_LEGAL_TOUR");
        source.Should().Contain("help-about-legal-tour");
        source.Should().Contain("freex_help_ribbon_command_context");
        source.Should().Contain("freex_help_online_guarded_message");
        source.Should().Contain("freex_feedback_guarded_message");
        source.Should().Contain("freex_updates_guarded_message");
        source.Should().Contain("freex_about_dialog");
        source.Should().Contain("freex_legal_notices_dialog");
        source.Should().Contain("freex_help_focus_return_status");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == \"Help\"))");
        source.Should().Contain("FindRenderedRibbonControl(\"Help Online\")");
        source.Should().Contain("helpOnlineButton.Focus();");
        source.Should().Contain("CreateExternalLinkOpenFailedMessageForHelpTour");
        source.Should().Contain("AppIssueReporter.CreateIssueUrl(CreateDeterministicIssueReportContextForHelpTour())");
        source.Should().Contain("visual-evidence-session");
        source.Should().Contain("AppUpdateSource.CreateDefault().ReleasePageUrl");
        source.Should().Contain("new AboutDialog");
        source.Should().Contain("new LegalNoticesDialog");
        source.Should().Contain("CaptureOwnedNativeDialogWhenShownForHelpTourAsync");
        source.Should().Contain("PrintWindow-owned-native-dialog");
        source.Should().Contain("help-about-legal:focus-return");
        source.Should().Contain("Ready status bar");
        source.Should().Contain("ExternalBrowserLaunched: false");
        source.Should().Contain("no global mouse, keyboard, UIA input, or external browser launch is used");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.HelpAboutLegalTourManifest");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesTitlebarWindowChromeStates()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        source.Should().Contain("FREEX_TITLEBAR_WINDOW_CHROME_TOUR");
        source.Should().Contain("titlebar-window-chrome-tour");
        source.Should().Contain("freex_titlebar_unsaved_restored");
        source.Should().Contain("freex_titlebar_dirty_marker_restored");
        source.Should().Contain("freex_titlebar_saved_renamed_restored");
        source.Should().Contain("freex_titlebar_saved_renamed_maximized");
        source.Should().Contain("freex_titlebar_saved_renamed_restored_after_maximize");
        source.Should().Contain("ExecuteTitlebarWindowChromeTourDirtyMutation");
        source.Should().Contain("TryExecuteEditCells([edit], \"Edit Cell\", out var outcome)");
        source.Should().Contain("SaveTitlebarWindowChromeTourWorkbookAsync");
        source.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        source.Should().Contain("WindowState = WindowState.Maximized");
        source.Should().Contain("WindowState = WindowState.Normal");
        source.Should().Contain("CreateTitlebarWindowChromeButtonState(MinimizeBtn)");
        source.Should().Contain("CreateTitlebarWindowChromeButtonState(MaxRestoreBtn)");
        source.Should().Contain("CreateTitlebarWindowChromeButtonState(CloseSysBtn)");
        source.Should().Contain("TitleBarQatCommandIds");
        source.Should().Contain("MaxRestoreIcon.Kind.ToString()");
        source.Should().Contain("interactive:titlebar-window-chrome:<State>");
        source.Should().Contain("Alt+Space/system menu, native titlebar drag, hover styling, and live mouse clicks remain foreground-runner gaps.");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.TitlebarWindowChromeTourManifest");

        xaml.Should().Contain("x:Name=\"WorkbookNameText\"");
        xaml.Should().Contain("x:Name=\"TitleBarQatPanel\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"MinimizeBtn\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"MaxRestoreBtn\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"CloseSysBtn\"");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesStatusFooterVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_STATUS_FOOTER_TOUR");
        source.Should().Contain("status-footer-tour");
        source.Should().Contain("EnsureStatusFooterTourContext");
        source.Should().Contain("StatusBarShowAverage = true");
        source.Should().Contain("StatusBarShowNumericalCount = true");
        source.Should().Contain("CaptureElementAsync(StatusBarRoot, outputDir, fileName)");
        source.Should().Contain("RenderTargetBitmap-status-footer-element");
        source.Should().Contain("freex_status_footer_ready_baseline");
        source.Should().Contain("freex_status_footer_selection_stats_numeric_mixed");
        source.Should().Contain("freex_status_footer_formula_edit_mode");
        source.Should().Contain("freex_status_footer_view_shortcut_page_layout");
        source.Should().Contain("freex_status_footer_zoom_min_10");
        source.Should().Contain("freex_status_footer_zoom_baseline_100");
        source.Should().Contain("freex_status_footer_zoom_max_400");
        source.Should().Contain("interactive:status-footer:<State>");
        source.Should().Contain("StatusModeText");
        source.Should().Contain("NumericalCountText");
        source.Should().Contain("ZoomSliderValue");
        source.Should().Contain("NormalViewChecked");
        source.Should().Contain("PageLayoutViewChecked");
        source.Should().Contain("PageBreakPreviewChecked");
        source.Should().Contain("FormulaBarText");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.StatusFooterTourManifest");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesAccentBarVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs", "MainWindow.Startup.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_ACCENT_BAR_TOUR");
        source.Should().Contain("TryStartAccentBarVisualTour();");
        source.Should().Contain("screenshots\", \"accent-bars-tour");
        source.Should().Contain("RunAccentBarVisualTourAsync");
        source.Should().Contain("CaptureElementAsync(TitleBarRoot, outputDir, \"title-normal\")");
        source.Should().Contain("CaptureElementAsync(StatusBarRoot, outputDir, \"status-normal\")");
        source.Should().Contain("HoverAndCaptureElementAsync(saveQatButton, TitleBarRoot, outputDir, \"title-save-hover\")");
        source.Should().Contain("HoverAndCaptureElementAsync(MaxRestoreBtn, TitleBarRoot, outputDir, \"title-system-hover\")");
        source.Should().Contain("HoverAndCaptureElementAsync(CloseSysBtn, TitleBarRoot, outputDir, \"title-close-hover\")");
        source.Should().Contain("HoverAndCaptureElementAsync(StatusZoomOutButton, StatusBarRoot, outputDir, \"status-minus-hover\")");
        source.Should().Contain("HoverAndCaptureElementAsync(StatusZoomInButton, StatusBarRoot, outputDir, \"status-plus-hover\")");

        catalog.Should().Contain("FREEX_ACCENT_BAR_TOUR=1");
        catalog.Should().Contain("screenshots/accent-bars-tour/");
        catalog.Should().Contain("title-save-hover.png");
        catalog.Should().Contain("status-plus-hover.png");
        catalog.Should().Contain("PNG-only legacy evidence");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesInsertObjectsLinksTextVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_INSERT_OBJECTS_LINKS_TOUR");
        source.Should().Contain("insert-objects-links-tour");
        source.Should().Contain("EnsureInsertObjectsLinksTourContext");
        source.Should().Contain("new HyperlinkDialog(HyperlinkDialogParityFixture.Target, HyperlinkDialogParityFixture.DisplayText)");
        source.Should().Contain("freex_insert_hyperlink_dialog_address_focus");
        source.Should().Contain("new SymbolPickerDialog");
        source.Should().Contain("freex_insert_symbol_picker_opened");
        source.Should().Contain("ApplyInsertObjectsLinksTourModelEvidenceAsync");
        source.Should().Contain("DrawingInsertionPlanner.BuildShapeCommand(sheetId, new CellAddress(sheetId, 4, 2), DrawingShapeKind.Rectangle)");
        source.Should().Contain("DrawingInsertionPlanner.BuildTextBoxCommand(sheetId, new CellAddress(sheetId, 4, 5), \"Text Box evidence\")");
        source.Should().Contain("PictureInsertionPlacementPlanner.CreateInsertPictureCommand(");
        source.Should().Contain("new SetThreadedCommentCommand(sheetId, new CellAddress(sheetId, 6, 4), \"Threaded comment evidence\")");
        source.Should().Contain("new SetCommentCommand(sheetId, new CellAddress(sheetId, 6, 5), \"Note evidence\")");
        source.Should().Contain("freex_insert_objects_grid_visuals");
        source.Should().Contain("CaptureInsertObjectsLinksInlineThreadedCommentEditorAsync");
        source.Should().Contain("CaptureInsertObjectsLinksInlineNoteEditorAsync");
        source.Should().Contain("freex_insert_new_comment_inline_popup");
        source.Should().Contain("freex_insert_new_note_inline_popup");
        source.Should().Contain("ReviewShowCommentsBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("freex_insert_comments_list_surface");
        source.Should().Contain("ReviewShowNotesBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("freex_insert_notes_list_surface");
        source.Should().Contain("InsertObjectsLinksTourManifest");
        source.Should().Contain("UI-CAT-INSERT-003");
        source.Should().Contain("UI-CMD-INSERT-008");
        source.Should().Contain("UI-CMD-INSERT-009");
        source.Should().Contain("UI-CMD-INSERT-010");
        source.Should().Contain("RenderTargetBitmap-hyperlink-dialog-window");
        source.Should().Contain("RenderTargetBitmap-symbol-picker-dialog-window");
        source.Should().Contain("RenderTargetBitmap-window-full");
        source.Should().Contain("deterministic placeholder bytes rather than opening the native Windows file picker");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.InsertObjectsLinksTourManifest");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesDataToolsDialogsVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_DATA_TOOLS_DIALOGS_TOUR");
        source.Should().Contain("data-tools-dialogs-tour");
        source.Should().Contain("EnsureDataToolsDialogsTourContext");
        source.Should().Contain("AdvancedFilterDialog(");
        source.Should().Contain("TextToColumnsDialog(");
        source.Should().Contain("RemoveDuplicatesDialog(");
        source.Should().Contain("CreateDataValidationTourDialog");
        source.Should().Contain("GoalSeekDialog(");
        source.Should().Contain("GoalSeekStatusDialog(new GoalSeekResult(true, 125d, 5000d, 7), 5000d)");
        source.Should().Contain("ScenarioManagerDialog(_workbook, context.Sheet.Id, ResolveSheetIdByName)");
        source.Should().Contain("DataTableDialog(context.Sheet.Id, context.DataTableRange");
        source.Should().Contain("ConsolidateDialog(");
        source.Should().Contain("ConsolidateParityFixture.SourceReference");
        source.Should().Contain("ConsolidateParityFixture.DestinationReference");
        source.Should().Contain("ForecastSheetDialog(6)");
        source.Should().Contain("freex_data_tools_advanced_filter_dialog");
        source.Should().Contain("freex_data_tools_text_to_columns_step1_original_data_type");
        source.Should().Contain("freex_data_tools_text_to_columns_step2_delimited");
        source.Should().Contain("freex_data_tools_text_to_columns_step2_fixed_width");
        source.Should().Contain("freex_data_tools_text_to_columns_step3_column_format_destination");
        source.Should().Contain("freex_data_tools_remove_duplicates_headers_columns");
        source.Should().Contain("freex_data_tools_data_validation_settings_tab");
        source.Should().Contain("freex_data_tools_data_validation_input_message_tab");
        source.Should().Contain("freex_data_tools_data_validation_error_alert_tab");
        source.Should().Contain("freex_data_tools_goal_seek_dialog");
        source.Should().Contain("freex_data_tools_goal_seek_status_dialog");
        source.Should().Contain("freex_data_tools_scenario_manager_dialog");
        source.Should().Contain("freex_data_tools_data_table_dialog");
        source.Should().Contain("freex_data_tools_consolidate_dialog");
        source.Should().Contain("freex_data_tools_forecast_sheet_dialog");
        source.Should().Contain("UI-CMD-DATA-003");
        source.Should().Contain("UI-CMD-DATA-004");
        source.Should().Contain("UI-CMD-DATA-005");
        source.Should().Contain("UI-CMD-DATA-006");
        source.Should().Contain("RenderTargetBitmap-data-tools-dialog-window");
        source.Should().Contain("no global mouse, keyboard, keytip, range-picker, or screen capture input is used");
        source.Should().Contain("Goal Seek status is seeded with a deterministic converged result");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.DataToolsDialogsTourManifest");

        catalog.Should().Contain("FREEX_DATA_TOOLS_DIALOGS_TOUR=1");
        catalog.Should().Contain("screenshots/data-tools-dialogs-tour/");
        catalog.Should().Contain("data_tools_dialogs_tour_manifest.json");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesDataSortFilterOutlineVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_DATA_SORT_FILTER_OUTLINE_TOUR");
        source.Should().Contain("data-sort-filter-outline-tour");
        source.Should().Contain("EnsureDataSortFilterOutlineTourContext");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == \"Data\"))");
        source.Should().Contain("new SortDialog(");
        source.Should().Contain("new SortOptionsDialog(new SortDialogOptions(");
        source.Should().Contain("CreateAutoFilterFlyoutDialog(context.Sheet, context.FilterHeaderCell");
        source.Should().Contain("searchBox.Text = \"Open\"");
        source.Should().Contain("new SubtotalDialog(SubtotalDialog.BuildColumnChoices(context.Sheet, context.TableRange))");
        source.Should().Contain("GroupRowsBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("CollapseGroupBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("ExpandGroupBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("CaptureDataSortFilterOutlineRibbonMenuAsync");
        source.Should().Contain("freex_data_sort_filter_outline_data_tab_surface");
        source.Should().Contain("freex_data_sort_filter_outline_sort_dialog");
        source.Should().Contain("freex_data_sort_filter_outline_sort_options_dialog");
        source.Should().Contain("freex_data_sort_filter_outline_autofilter_search_open");
        source.Should().Contain("freex_data_sort_filter_outline_subtotal_dialog");
        source.Should().Contain("freex_data_sort_filter_outline_group_expanded");
        source.Should().Contain("freex_data_sort_filter_outline_hide_detail_collapsed");
        source.Should().Contain("freex_data_sort_filter_outline_show_detail_expanded");
        source.Should().Contain("freex_data_sort_filter_outline_group_dropdown");
        source.Should().Contain("freex_data_sort_filter_outline_ungroup_dropdown");
        source.Should().Contain("UI-CMD-DATA-001");
        source.Should().Contain("UI-CMD-DATA-002");
        source.Should().Contain("UI-CMD-DATA-007");
        source.Should().Contain("UI-CMD-DATA-008");
        source.Should().Contain("RenderTargetBitmap-window-dialog-menu");
        source.Should().Contain("Get Data is represented by the Data tab command surface only");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.DataSortFilterOutlineTourManifest");

        catalog.Should().Contain("FREEX_DATA_SORT_FILTER_OUTLINE_TOUR=1");
        catalog.Should().Contain("screenshots/data-sort-filter-outline-tour/");
        catalog.Should().Contain("data_sort_filter_outline_tour_manifest.json");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesViewPanesZoomVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_VIEW_PANES_ZOOM_TOUR");
        source.Should().Contain("view-panes-zoom-tour");
        source.Should().Contain("EnsureViewPanesZoomTourContext");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == \"View\"))");
        source.Should().Contain("SetWorksheetViewMode(WorksheetViewMode.PageLayout)");
        source.Should().Contain("SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview)");
        source.Should().Contain("SetViewPanesZoomTourShowToggles(showGridlines: false, showHeadings: false, showRulers: false)");
        source.Should().Contain("SetViewPanesZoomTourFormulaBarVisible(false)");
        source.Should().Contain("FreezeAtSelectionMenuItem_Click(this, new RoutedEventArgs())");
        source.Should().Contain("SplitViewBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("new ZoomDialog(125) { Owner = this }");
        source.Should().Contain("Zoom100Btn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("ZoomSelectionBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("new SetWorkbookWindowArrangementCommand(WorkbookWindowArrangement.Horizontal)");
        source.Should().Contain("FindDescendantByRibbonCommandName<Button>(RibbonTabs, \"Arrange All\")");
        source.Should().Contain("new SaveCustomViewCommand(ViewPanesZoomTourCustomViewName)");
        source.Should().Contain("new CustomViewsDialog(_workbook, _commandBus) { Owner = this }");
        source.Should().Contain("freex_view_panes_zoom_view_tab_normal");
        source.Should().Contain("freex_view_panes_zoom_page_layout_ruler_on");
        source.Should().Contain("freex_view_panes_zoom_page_break_preview");
        source.Should().Contain("freex_view_panes_zoom_show_toggles_hidden");
        source.Should().Contain("freex_view_panes_zoom_freeze_panes_c4");
        source.Should().Contain("freex_view_panes_zoom_split_panes_e6");
        source.Should().Contain("freex_view_panes_zoom_dialog_custom_125");
        source.Should().Contain("freex_view_panes_zoom_100_percent_command");
        source.Should().Contain("freex_view_panes_zoom_to_selection");
        source.Should().Contain("freex_view_panes_zoom_arrange_horizontal_state");
        source.Should().Contain("freex_view_panes_zoom_custom_views_dialog");
        source.Should().Contain("UI-CAT-VIEW-001");
        source.Should().Contain("UI-CAT-VIEW-002");
        source.Should().Contain("UI-CMD-VIEW-001");
        source.Should().Contain("UI-CMD-VIEW-004");
        source.Should().Contain("ViewPanesZoomTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.ViewPanesZoomTourManifest");
        source.Should().Contain("Split divider drag, pane scrollbar interaction, Ctrl+wheel zoom, status slider drag, and native UIA RangeValue remain open.");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesPageLayoutSetupVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_PAGE_LAYOUT_SETUP_TOUR");
        source.Should().Contain("page-layout-setup-tour");
        source.Should().Contain("EnsurePageLayoutSetupTourContext");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == \"Page Layout\"))");
        source.Should().Contain("sheet.PageOrientation = WorksheetPageOrientation.Landscape");
        source.Should().Contain("sheet.PrintArea = new GridRange");
        source.Should().Contain("sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1)");
        source.Should().Contain("sheet.RowPageBreaks.Add(12)");
        source.Should().Contain("sheet.ScaleToFit = new WorksheetScaleToFit(90, null, null)");
        source.Should().Contain("CapturePageLayoutSetupMenuAsync");
        source.Should().Contain("FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName)");
        source.Should().Contain("new PageSetupDialog(sheet, SheetGrid.SelectedRange, null, PageSetupInitialFocusTarget.PageOrientation)");
        source.Should().Contain("pageSetupDialog.PageSetupTabs.SelectedItem = pageSetupDialog.MarginsTab");
        source.Should().Contain("pageSetupDialog.PageSetupTabs.SelectedItem = pageSetupDialog.SheetTab");
        source.Should().Contain("ApplyPageLayoutScaleToFit(new WorksheetScaleToFit(null, 1, 2))");
        source.Should().Contain("new SelectionPaneDialog(CreatePageLayoutSetupSelectionPaneItems())");
        source.Should().Contain("freex_page_layout_setup_ribbon_baseline");
        source.Should().Contain("freex_page_layout_setup_margins_menu_opened");
        source.Should().Contain("freex_page_layout_setup_orientation_menu_opened");
        source.Should().Contain("freex_page_layout_setup_size_menu_opened");
        source.Should().Contain("freex_page_layout_setup_print_area_menu_opened");
        source.Should().Contain("freex_page_layout_setup_breaks_menu_opened");
        source.Should().Contain("freex_page_layout_setup_background_menu_opened");
        source.Should().Contain("freex_page_layout_setup_dialog_page_tab");
        source.Should().Contain("freex_page_layout_setup_dialog_margins_tab");
        source.Should().Contain("freex_page_layout_setup_dialog_sheet_tab_print_titles");
        source.Should().Contain("freex_page_layout_setup_scale_to_fit_state");
        source.Should().Contain("freex_page_layout_setup_sheet_options_toggled");
        source.Should().Contain("freex_page_layout_setup_arrange_selection_pane_dialog");
        source.Should().Contain("UI-CMD-PAGE-001");
        source.Should().Contain("UI-CMD-PAGE-006");
        source.Should().Contain("UI-CMD-DRAW-002");
        source.Should().Contain("PageLayoutSetupTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.PageLayoutSetupTourManifest");
        source.Should().Contain("Background captures the supported menu surface only");
        source.Should().Contain("Arrange evidence uses a deterministic representative Selection Pane dialog item list");

        catalog.Should().Contain("FREEX_PAGE_LAYOUT_SETUP_TOUR=1");
        catalog.Should().Contain("screenshots/page-layout-setup-tour/");
        catalog.Should().Contain("page_layout_setup_tour_manifest.json");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesPageLayoutOutputVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.ScreenshotTour.cs",
            "MainWindow.ScreenshotTour.PageLayoutOutput.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_PAGE_LAYOUT_OUTPUT_TOUR");
        source.Should().Contain("page-layout-output-tour");
        source.Should().Contain("CapturePageLayoutOutputTourAsync");
        source.Should().Contain("EnsurePageLayoutOutputTourContext");
        source.Should().Contain("sheet.BackgroundImage = new WorksheetBackgroundImage");
        source.Should().Contain("sheet.PrintArea = Range(sheet.Id, 1, 1, 24, 6)");
        source.Should().Contain("sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2)");
        source.Should().Contain("sheet.RowPageBreaks.Add(12)");
        source.Should().Contain("sheet.RowPageBreaks.Add(24)");
        source.Should().Contain("new WorksheetScaleToFit(null, 1, 2)");
        source.Should().Contain("PageSetupInitialFocusTarget.RepeatRows");
        source.Should().Contain("RowsRepeatPickerButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))");
        source.Should().Contain("PrintRenderer.RenderWorksheet(_workbook, sheet.Id, _viewportService)");
        source.Should().Contain("PdfDocumentExporter.Save(");
        source.Should().Contain("PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import)");
        source.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, xlsxSaveAdapter))");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.PageLayoutOutputTourManifest");
        source.Should().Contain("freex_page_layout_output_background_native_picker_guard");
        source.Should().Contain("freex_page_layout_output_print_titles_defaults");
        source.Should().Contain("freex_page_layout_output_print_titles_range_picker_result");
        source.Should().Contain("freex_page_layout_output_print_area_menu_status");
        source.Should().Contain("freex_page_layout_output_breaks_menu_status");
        source.Should().Contain("freex_page_layout_output_scale_to_fit_result_status");
        source.Should().Contain("freex_page_layout_output_print_preview_summary");
        source.Should().Contain("freex_page_layout_output_saved.xlsx");
        source.Should().Contain("freex_page_layout_output_print_titles.pdf");
        source.Should().Contain("native image picker is intentionally not opened");
        source.Should().Contain("PlannedCaptureCount: 7");

        catalog.Should().Contain("FREEX_PAGE_LAYOUT_OUTPUT_TOUR=1");
        catalog.Should().Contain("screenshots/page-layout-output-tour/");
        catalog.Should().Contain("page_layout_output_tour_manifest.json");
        catalog.Should().Contain("freex_page_layout_output_print_preview_summary.png");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesDrawObjectFormattingVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_DRAW_OBJECT_FORMATTING_TOUR");
        source.Should().Contain("draw-object-formatting-tour");
        source.Should().Contain("EnsureDrawObjectFormattingTourContext");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == \"Draw\"))");
        source.Should().Contain("Id = Guid.Parse(\"aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa\")");
        source.Should().Contain("Id = Guid.Parse(\"bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb\")");
        source.Should().Contain("Id = Guid.Parse(\"cccccccc-3333-4333-8333-cccccccccccc\")");
        source.Should().Contain("sheet.DrawingShapes.Add(shape);");
        source.Should().Contain("sheet.Pictures.Add(picture);");
        source.Should().Contain("sheet.TextBoxes.Add(textBox);");
        source.Should().Contain("new ColorPickerDialog(context.Shape.FillColor, allowNoColor: true, UiText.Get(\"FormatCells_NoFill\"))");
        source.Should().Contain("new ShapeGradientDialog(");
        source.Should().Contain("context.Shape.GradientFillEndColor ?? ShapeGradientPlanner.DefaultEndColor");
        source.Should().Contain("context.Shape.GetEffectiveGradientFillDirection()");
        source.Should().Contain("new ShapeEffectsDialog(context.Shape.GetEffectiveEffectPreset())");
        source.Should().Contain("new ObjectSizeDialog(context.Shape.Width, context.Shape.Height, UiText.Get(\"MainWindowMessage_ObjectSizeTitle\"))");
        source.Should().Contain("new FormatPictureDialog(picture)");
        source.Should().Contain("new SelectionPaneDialog(SelectionPaneDialog.BuildItems(context.Sheet))");
        source.Should().Contain("FindDescendantByRibbonCommandName<Button>(RibbonTabs, \"Crop Picture\")");
        source.Should().Contain("SelectionPaneToggleVisibilityButton");
        source.Should().Contain("freex_draw_object_formatting_draw_tab_baseline");
        source.Should().Contain("freex_draw_object_formatting_shape_fill_color_picker");
        source.Should().Contain("freex_draw_object_formatting_object_outline_color_picker");
        source.Should().Contain("freex_draw_object_formatting_shape_gradient_dialog");
        source.Should().Contain("freex_draw_object_formatting_shape_effects_dialog");
        source.Should().Contain("freex_draw_object_formatting_crop_menu_opened");
        source.Should().Contain("freex_draw_object_formatting_object_size_dialog");
        source.Should().Contain("freex_draw_object_formatting_picture_size_tab");
        source.Should().Contain("freex_draw_object_formatting_picture_alt_text_tab");
        source.Should().Contain("freex_draw_object_formatting_selection_pane_rename_visibility");
        source.Should().Contain("UI-CAT-DRAW-001");
        source.Should().Contain("UI-CMD-DRAW-003");
        source.Should().Contain("UI-CMD-DRAW-005");
        source.Should().Contain("DrawObjectFormattingTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.DrawObjectFormattingTourManifest");
        source.Should().Contain("Picture insertion uses deterministic in-process placeholder bytes");
        source.Should().Contain("Selection Pane rename and visibility states are previewed in the dialog before OK/apply");

        catalog.Should().Contain("FREEX_DRAW_OBJECT_FORMATTING_TOUR=1");
        catalog.Should().Contain("screenshots/draw-object-formatting-tour/");
        catalog.Should().Contain("draw_object_formatting_tour_manifest.json");
        catalog.Should().Contain("freex_draw_object_formatting_selection_pane_rename_visibility.png");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesChartDataLayoutVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs", "MainWindow.ScreenshotTour.ChartDataLayout.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_CHART_DATA_LAYOUT_TOUR");
        source.Should().Contain("chart-data-layout-tour");
        source.Should().Contain("EnsureChartDataLayoutTourContext");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == \"Chart Design\"))");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.ChartContextTabs.Single(tab => tab.Header == \"Format\"))");
        source.Should().Contain("new SelectDataSourceDialog(");
        source.Should().Contain("new MoveChartDialog(context.Sheet.Name)");
        source.Should().Contain("new ChangeChartTypeDialog(context.Chart.Type)");
        source.Should().Contain("new ChartStyleDialog(context.Chart)");
        source.Should().Contain("new ChartTitlesDialog(context.Chart.Title, context.Chart.XAxisTitle, context.Chart.YAxisTitle)");
        source.Should().Contain("new ChartAreaLegendDialog(context.Chart)");
        source.Should().Contain("OnWaterfallChartPointContextMenuRequested(context.WaterfallChart, pointIndex: 1");
        source.Should().Contain("freex_chart_data_layout_selected_chart_design_context");
        source.Should().Contain("freex_chart_data_layout_select_data_dialog");
        source.Should().Contain("freex_chart_data_layout_move_chart_dialog");
        source.Should().Contain("freex_chart_data_layout_change_chart_type_dialog");
        source.Should().Contain("freex_chart_data_layout_chart_styles_dialog");
        source.Should().Contain("freex_chart_data_layout_chart_titles_dialog");
        source.Should().Contain("freex_chart_data_layout_format_chart_area_dialog");
        source.Should().Contain("freex_chart_data_layout_waterfall_point_context_menu");
        source.Should().Contain("UI-CAT-INSERT-002B");
        source.Should().Contain("UI-CAT-INSERT-002C");
        source.Should().Contain("UI-CMD-INSERT-016");
        source.Should().Contain("ChartDataLayoutTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.ChartDataLayoutTourManifest");
        source.Should().Contain("physical chart selection handles");

        catalog.Should().Contain("FREEX_CHART_DATA_LAYOUT_TOUR=1");
        catalog.Should().Contain("screenshots/chart-data-layout-tour/");
        catalog.Should().Contain("chart_data_layout_tour_manifest.json");
        catalog.Should().Contain("freex_chart_data_layout_select_data_dialog.png");
        catalog.Should().Contain("freex_chart_data_layout_waterfall_point_context_menu.png");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesChartPersistenceRenderVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs", "MainWindow.ScreenshotTour.ChartPersistenceRender.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_CHART_PERSISTENCE_RENDER_TOUR");
        source.Should().Contain("chart-persistence-render-tour");
        source.Should().Contain("EnsureChartPersistenceRenderTourContext");
        source.Should().Contain("AppOptionsObjectDisplay.All");
        source.Should().Contain("AppOptionsObjectDisplay.Placeholders");
        source.Should().Contain("new ChangeChartSourceCommand(context.Sheet.Id, context.Chart.Id, context.MutatedSourceRange");
        source.Should().Contain("new ChangeChartTypeCommand(context.Sheet.Id, context.Chart.Id, ChartType.Line)");
        source.Should().Contain("new SetChartStyleCommand(context.Sheet.Id, context.Chart.Id, 18)");
        source.Should().Contain("new SetChartLayoutCommand(");
        source.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        source.Should().Contain("OpenFileAsync(savedWorkbookPath)");
        source.Should().Contain("ChartTypeSupport.IsRenderable(context.Chart.Type)");
        source.Should().Contain("OnWaterfallChartPointContextMenuRequested(context.WaterfallChart, pointIndex: 1");
        source.Should().Contain("freex_chart_persistence_render_seeded_rendered_chart");
        source.Should().Contain("freex_chart_persistence_render_mutated_rendered_chart");
        source.Should().Contain("freex_chart_persistence_render_mutated_placeholder_chart");
        source.Should().Contain("freex_chart_persistence_render_saved_native_json_title");
        source.Should().Contain("freex_chart_persistence_render_reopened_rendered_chart");
        source.Should().Contain("freex_chart_persistence_render_reopened_placeholder_chart");
        source.Should().Contain("freex_chart_persistence_render_waterfall_point_context_menu");
        source.Should().Contain("ChartPersistenceRenderTourSavedWorkbookFileName");
        source.Should().Contain("freex_chart_persistence_render_saved.fxl");
        source.Should().Contain("ChartPersistenceRenderTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.ChartPersistenceRenderTourManifest");
        source.Should().Contain("XLSX chart mutation persistence remains a separate compatibility lane");

        catalog.Should().Contain("FREEX_CHART_PERSISTENCE_RENDER_TOUR=1");
        catalog.Should().Contain("screenshots/chart-persistence-render-tour/");
        catalog.Should().Contain("chart_persistence_render_tour_manifest.json");
        catalog.Should().Contain("freex_chart_persistence_render_mutated_rendered_chart.png");
        catalog.Should().Contain("freex_chart_persistence_render_reopened_rendered_chart.png");
        catalog.Should().Contain("freex_chart_persistence_render_saved.fxl");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesHomeFontColorsVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_HOME_FONT_COLORS_TOUR");
        source.Should().Contain("home-font-colors-tour");
        source.Should().Contain("EnsureHomeFontColorsTourContext");
        source.Should().Contain("FontSizePlanner.Increase(16)");
        source.Should().Contain("FontSizePlanner.Decrease(10)");
        source.Should().Contain("CellStyleDiffPlanner.UnderlineDiff(true)");
        source.Should().Contain("CellStyleDiffPlanner.DoubleUnderlineDiff(true)");
        source.Should().Contain("CellStyleDiffPlanner.StrikethroughDiff(true)");
        source.Should().Contain("new StyleDiff(FontColor: new CellColor(192, 0, 0))");
        source.Should().Contain("new StyleDiff(FillColor: new CellColor(255, 242, 204))");
        source.Should().Contain("new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)");
        source.Should().Contain("new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, 0.6)");
        source.Should().Contain("BorderShortcutService.GetAllBorderDiff(BorderStyle.Thin, CellColor.Black)");
        source.Should().Contain("freex_home_font_colors_grid_styled");
        source.Should().Contain("freex_home_font_family_dropdown_opened");
        source.Should().Contain("freex_home_font_size_dropdown_opened");
        source.Should().Contain("freex_home_underline_menu_opened");
        source.Should().Contain("freex_home_borders_full_menu_opened");
        source.Should().Contain("freex_home_borders_line_color_submenu_opened");
        source.Should().Contain("FindMenuItemByHeader(menu.Items, UiText.Get(\"MainWindow_Header_LineColor\"))");
        source.Should().Contain("foreground mouse/keytip evidence for Home font/color/border commands");
        source.Should().Contain("Excel-paired Home font/color/border screenshots");
        source.Should().Contain("full LCID/theme matrix");
        source.Should().Contain("font/fill color gallery parity beyond the current custom color picker and swatch buttons");
        source.Should().Contain("persistence breadth across save/reload and native JSON state");
        source.Should().Contain("HomeFontColorsTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.HomeFontColorsTourManifest");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesHomeStylesConditionalFormattingVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_HOME_STYLES_CF_TOUR");
        source.Should().Contain("home-styles-cf-tour");
        source.Should().Contain("EnsureHomeStylesConditionalFormattingTourContext");
        source.Should().Contain("new CreateStyledStructuredTableCommand(sheet.Id, tableRange");
        source.Should().Contain("new ApplyConditionalFormatCommand(sheet.Id, greaterThanRule)");
        source.Should().Contain("ConditionalFormatPresetGalleryPlanner.CreateDataBarRule(\"SolidBlue\", conditionalFormatRange)");
        source.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Good, _workbook.Theme)");
        source.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Bad, _workbook.Theme)");
        source.Should().Contain("PopulateFormatTableGalleryMenu();");
        source.Should().Contain("PopulateConditionalFormatDataBarGallery(dataBarsItem);");
        source.Should().Contain("new ManageConditionalFormatsDialog(");
        source.Should().Contain("freex_home_styles_cf_grid_result");
        source.Should().Contain("freex_home_styles_cf_conditional_formatting_menu_opened");
        source.Should().Contain("freex_home_styles_cf_data_bars_submenu_opened");
        source.Should().Contain("freex_home_styles_cf_manage_rules_dialog");
        source.Should().Contain("freex_home_styles_cf_format_as_table_gallery_opened");
        source.Should().Contain("freex_home_styles_cf_cell_styles_gallery_opened");
        source.Should().Contain("UI-CAT-HOME-003A-C");
        source.Should().Contain("UI-CMD-HOME-STYLES-001");
        source.Should().Contain("UI-CMD-HOME-STYLES-002");
        source.Should().Contain("UI-CMD-HOME-STYLES-003");
        source.Should().Contain("full highlight/top-bottom/color-scale/icon-set rule taxonomy");
        source.Should().Contain("Excel-paired screenshots");
        source.Should().Contain("HomeStylesConditionalFormattingTourManifest");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.HomeStylesConditionalFormattingTourManifest");

        catalog.Should().Contain("FREEX_HOME_STYLES_CF_TOUR=1");
        catalog.Should().Contain("screenshots/home-styles-cf-tour/");
        catalog.Should().Contain("home_styles_conditional_formatting_tour_manifest.json");
        catalog.Should().Contain("freex_home_styles_cf_manage_rules_dialog.png");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesFormulaDiagnosticsVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_FORMULA_DIAGNOSTICS_TOUR");
        source.Should().Contain("formula-diagnostics-tour");
        source.Should().Contain("EnsureFormulaDiagnosticsTourContext");
        source.Should().Contain("sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), \"A2+A3\")");
        source.Should().Contain("sheet.SetFormula(new CellAddress(sheet.Id, 2, 4), \"B2/0\")");
        source.Should().Contain("TracePrecedentsForCell(context.ResultCell, \"Trace Precedents\")");
        source.Should().Contain("TraceDependentsBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("ShowFormulasBtn_Click(this, new RoutedEventArgs())");
        source.Should().Contain("RemoveTraceArrows(kind: null, \"Remove Arrows\")");
        source.Should().Contain("FormulaAuditingService.FindFormulaErrorIssues(_workbook, _currentSheetId, _recalcEngine.CyclicCells)");
        source.Should().Contain("new ErrorCheckingDialog(");
        source.Should().Contain("new EvaluateFormulaDialog(resultSummary)");
        source.Should().Contain("FindDescendantButtonByContent(evaluateFormulaDialog, UiText.Get(\"EvaluateFormula_EvaluateButton\"))");
        source.Should().Contain("new AddWatchDialog(FormatRangeReference(context.ResultCell, context.ResultCell))");
        source.Should().Contain("CreateFormulaDiagnosticsWatchWindowDialog");
        source.Should().Contain("WatchWindowService.AddWatches(_workbook, new GridRange(context.ResultCell, context.ResultCell))");
        source.Should().Contain("FindDescendantByAutomationId<Button>(watchWindowDialog, \"WatchWindowRefreshButton\")");
        source.Should().Contain("FindDescendantByAutomationId<ListView>(watchWindowDialog, \"WatchWindowList\")");
        source.Should().Contain("FindDescendantByAutomationId<Button>(watchWindowDialog, \"WatchWindowDeleteButton\")");
        source.Should().Contain("freex_formula_diagnostics_trace_precedents");
        source.Should().Contain("freex_formula_diagnostics_trace_dependents");
        source.Should().Contain("freex_formula_diagnostics_show_formulas_enabled");
        source.Should().Contain("freex_formula_diagnostics_remove_arrows_cleared");
        source.Should().Contain("freex_formula_diagnostics_error_checking_dialog");
        source.Should().Contain("freex_formula_diagnostics_evaluate_default");
        source.Should().Contain("freex_formula_diagnostics_evaluate_after_step");
        source.Should().Contain("freex_formula_diagnostics_watch_add_dialog");
        source.Should().Contain("freex_formula_diagnostics_watch_window_list");
        source.Should().Contain("freex_formula_diagnostics_watch_window_after_refresh");
        source.Should().Contain("freex_formula_diagnostics_watch_window_after_delete");
        source.Should().Contain("FormulaDiagnosticsTourManifest");
        source.Should().Contain("UI-CAT-FORMULAS-002");
        source.Should().Contain("UI-CMD-FORM-003");
        source.Should().Contain("UI-CMD-FORM-005");
        source.Should().Contain("RenderTargetBitmap; it is not foreground CopyFromScreen proof");
        source.Should().Contain("No global mouse or keyboard input is synthesized");
        source.Should().Contain("The trace-arrow and show-formulas captures are FreeX-only visual states; no paired Microsoft Excel evidence is produced by this tool.");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.FormulaDiagnosticsTourManifest");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesHomeClipboardCellsEditingVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_HOME_CLIPBOARD_CELLS_EDITING_TOUR");
        source.Should().Contain("home-clipboard-cells-editing-tour");
        source.Should().Contain("EnsureHomeClipboardCellsEditingTourContext");
        source.Should().Contain("SeedHomeClipboardCellsEditingInternalClipboard");
        source.Should().Contain("new InternalClipboard(");
        source.Should().Contain("SheetGrid.ClipboardRange = copySourceRange;");
        source.Should().Contain("CaptureHomeClipboardCellsEditingMenuAsync");
        source.Should().Contain("FindDescendantByRibbonCommandName<Button>(RibbonTabs, commandName)");
        source.Should().Contain("new CellShiftDialog(CellShiftDialogMode.Insert)");
        source.Should().Contain("new CellShiftDialog(CellShiftDialogMode.Delete)");
        source.Should().Contain("CreateHomeClipboardCellsEditingSortDialog");
        source.Should().Contain("new SortDialog(");
        source.Should().Contain("CreateHomeClipboardCellsEditingFindReplaceDialog(replaceMode: false)");
        source.Should().Contain("CreateHomeClipboardCellsEditingFindReplaceDialog(replaceMode: true)");
        source.Should().Contain("new GoToDialog(_currentSheetId, context.GoToDefaultAddress");
        source.Should().Contain("new GoToSpecialDialog");
        source.Should().Contain("freex_home_clipboard_cells_editing_clipboard_copied_state");
        source.Should().Contain("freex_home_clipboard_cells_editing_paste_menu_opened");
        source.Should().Contain("freex_home_clipboard_cells_editing_insert_menu_opened");
        source.Should().Contain("freex_home_clipboard_cells_editing_delete_menu_opened");
        source.Should().Contain("freex_home_clipboard_cells_editing_format_menu_opened");
        source.Should().Contain("freex_home_clipboard_cells_editing_clear_menu_opened");
        source.Should().Contain("freex_home_clipboard_cells_editing_sort_filter_menu_opened");
        source.Should().Contain("freex_home_clipboard_cells_editing_find_select_menu_opened");
        source.Should().Contain("freex_home_clipboard_cells_editing_insert_cells_dialog");
        source.Should().Contain("freex_home_clipboard_cells_editing_delete_cells_dialog");
        source.Should().Contain("freex_home_clipboard_cells_editing_custom_sort_dialog");
        source.Should().Contain("freex_home_clipboard_cells_editing_find_dialog");
        source.Should().Contain("freex_home_clipboard_cells_editing_replace_dialog");
        source.Should().Contain("freex_home_clipboard_cells_editing_go_to_dialog");
        source.Should().Contain("freex_home_clipboard_cells_editing_go_to_special_dialog");
        source.Should().Contain("UI-CMD-HOME-CLIP-001");
        source.Should().Contain("UI-CMD-HOME-CLIP-002");
        source.Should().Contain("UI-CMD-HOME-CELLS-001");
        source.Should().Contain("UI-CMD-HOME-CELLS-002");
        source.Should().Contain("UI-CMD-HOME-CELLS-003");
        source.Should().Contain("UI-CMD-HOME-CELLS-004");
        source.Should().Contain("UI-CMD-HOME-EDIT-003");
        source.Should().Contain("UI-CMD-HOME-EDIT-004");
        source.Should().Contain("no global mouse, keyboard, keytip, OS clipboard, or screen capture input is used");
        source.Should().Contain("Paste Special, Format Painter persistent/double-click mode");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.HomeClipboardCellsEditingTourManifest");

        catalog.Should().Contain("FREEX_HOME_CLIPBOARD_CELLS_EDITING_TOUR=1");
        catalog.Should().Contain("screenshots/home-clipboard-cells-editing-tour/");
        catalog.Should().Contain("home_clipboard_cells_editing_tour_manifest.json");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesFormulaAuthoringNamesVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_FORMULA_AUTHORING_NAMES_TOUR");
        source.Should().Contain("formula-authoring-names-tour");
        source.Should().Contain("EnsureFormulaAuthoringNamesTourContext");
        source.Should().Contain("_workbook.DefineNamedRange(\"Revenue\", revenueRange);");
        source.Should().Contain("_workbook.DefineNamedRange(\"Profit\", profitRange);");
        source.Should().Contain("sheet.SetFormula(new CellAddress(sheet.Id, 7, 2), \"SUM(Revenue)\")");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == \"Formulas\"))");
        source.Should().Contain("CaptureFormulaAuthoringNamesMenuAsync");
        source.Should().Contain("CaptureFormulaAuthoringNamesFunctionMenuAsync");
        source.Should().Contain("FormulaLogicalBtn_Click");
        source.Should().Contain("UseInFormulaBtn_Click");
        source.Should().Contain("new InsertFunctionDialog");
        source.Should().Contain("categoryBox.SelectedItem = \"Lookup & Reference\";");
        source.Should().Contain("InsertFunctionCatalogEntry { Name: \"XLOOKUP\" }");
        source.Should().Contain("new NamedRangeDialog(_workbook, _commandBus, context.AuthoringRange)");
        source.Should().Contain("new NameDefinitionDialog(");
        source.Should().Contain("new CreateNamesFromSelectionDialog");
        source.Should().Contain("freex_formula_authoring_names_formulas_tab");
        source.Should().Contain("freex_formula_authoring_names_autosum_menu_opened");
        source.Should().Contain("freex_formula_authoring_names_logical_functions_menu_opened");
        source.Should().Contain("freex_formula_authoring_names_use_in_formula_menu_opened");
        source.Should().Contain("freex_formula_authoring_names_insert_function_lookup_xlookup");
        source.Should().Contain("freex_formula_authoring_names_name_manager_dialog");
        source.Should().Contain("freex_formula_authoring_names_define_name_dialog");
        source.Should().Contain("freex_formula_authoring_names_create_from_selection_dialog");
        source.Should().Contain("UI-CAT-FORMULAS-001");
        source.Should().Contain("UI-CMD-FORM-001");
        source.Should().Contain("UI-CMD-FORM-002");
        source.Should().Contain("RenderTargetBitmap-formulas-context-menu");
        source.Should().Contain("Formula diagnostics, formula-bar/name-box, and Excel-paired screenshot evidence are intentionally outside this bounded slice.");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.FormulaAuthoringNamesTourManifest");

        catalog.Should().Contain("FREEX_FORMULA_AUTHORING_NAMES_TOUR=1");
        catalog.Should().Contain("screenshots/formula-authoring-names-tour/");
        catalog.Should().Contain("formula_authoring_names_tour_manifest.json");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesReviewCommentsProtectionVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_REVIEW_COMMENTS_PROTECTION_TOUR");
        source.Should().Contain("review-comments-protection-tour");
        source.Should().Contain("EnsureReviewCommentsProtectionTourContext");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == \"Review\"))");
        source.Should().Contain("new SpellCheckDialog(context.SpellingWord, context.SpellingSuggestion)");
        source.Should().Contain("AccessibilityCheckerService.FindIssues(_workbook)");
        source.Should().Contain("CaptureReviewCommentsProtectionInlineThreadedCommentEditorAsync");
        source.Should().Contain("CommentListWindow.CreateThreadedCommentItems(context.Sheet.ThreadedComments)");
        source.Should().Contain("CommentListWindow.CreateNoteItems(context.Sheet.Comments)");
        source.Should().Contain("new PasswordProtectionDialog(");
        source.Should().Contain("new AllowEditRangeDialog(");
        source.Should().Contain("sheet.ThreadedComments[threadedCell] = new ThreadedComment");
        source.Should().Contain("sheet.Comments[noteCell] = \"Review seeded simple note.\"");
        source.Should().Contain("sheet.AllowEditRanges.Add(allowEditRange);");
        source.Should().Contain("sheet.AddMergedRegion(Range(sheet.Id, 4, 1, 4, 3));");
        source.Should().Contain("freex_review_comments_protection_review_tab");
        source.Should().Contain("freex_review_spell_check_dialog");
        source.Should().Contain("freex_review_accessibility_checker_dialog");
        source.Should().Contain("freex_review_new_threaded_comment_inline_popup");
        source.Should().Contain("freex_review_show_comments_list");
        source.Should().Contain("freex_review_show_notes_list");
        source.Should().Contain("freex_review_protect_sheet_dialog");
        source.Should().Contain("freex_review_protect_workbook_dialog");
        source.Should().Contain("freex_review_allow_edit_ranges_dialog");
        source.Should().Contain("UI-CAT-REVIEW-001");
        source.Should().Contain("UI-CAT-REVIEW-002");
        source.Should().Contain("UI-CMD-REVIEW-001");
        source.Should().Contain("UI-CMD-REVIEW-002");
        source.Should().Contain("UI-CMD-REVIEW-003");
        source.Should().Contain("UI-CMD-REVIEW-004");
        source.Should().Contain("Thesaurus and Show Changes are not currently supported FreeX Review commands");
        source.Should().Contain("Protect/unprotect confirmation, wrong-password, Permissions, Share, foreground focus trapping, and paired Microsoft Excel screenshots remain open.");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.ReviewCommentsProtectionTourManifest");

        catalog.Should().Contain("FREEX_REVIEW_COMMENTS_PROTECTION_TOUR=1");
        catalog.Should().Contain("screenshots/review-comments-protection-tour/");
        catalog.Should().Contain("review_comments_protection_tour_manifest.json");
    }

    [Fact]
    public void MainWindowScreenshotTour_CapturesReviewStatsShareVisualEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.ScreenshotTour.cs",
            "MainWindow.ScreenshotTour.ReviewStatsShare.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        source.Should().Contain("FREEX_REVIEW_STATS_SHARE_TOUR");
        source.Should().Contain("review-stats-share-tour");
        source.Should().Contain("EnsureReviewStatsShareTourContext");
        source.Should().Contain("SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == \"Review\"))");
        source.Should().Contain("new WorkbookStatisticsDialog(WorkbookStatisticsService.GetStatistics(_workbook))");
        source.Should().Contain("WorkbookStatisticsDialog.CreateMessage(WorkbookStatisticsService.GetStatistics(_workbook))");
        source.Should().Contain("WorkbookShareReadinessPlanner.CreatePlan(null, WorkbookShareSurface.WindowsShare)");
        source.Should().Contain("WorkbookShareReadinessPlanner.FormatStatus(savedSharePlan)");
        source.Should().Contain("ReviewShareButton");
        source.Should().Contain("freex_review_workbook_statistics_dialog");
        source.Should().Contain("freex_review_share_unsaved_guard_status");
        source.Should().Contain("freex_review_share_saved_ready_status");
        source.Should().Contain("UI-CAT-REVIEW-001");
        source.Should().Contain("UI-CMD-REVIEW-002");
        source.Should().Contain("UI-CMD-REVIEW-005");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.ReviewStatsShareTourManifest");

        catalog.Should().Contain("FREEX_REVIEW_STATS_SHARE_TOUR=1");
        catalog.Should().Contain("screenshots/review-stats-share-tour/");
        catalog.Should().Contain("review_stats_share_tour_manifest.json");
    }
}
