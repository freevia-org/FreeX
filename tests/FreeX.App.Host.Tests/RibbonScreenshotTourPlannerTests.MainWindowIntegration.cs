using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RibbonScreenshotTourPlannerTests
{
    [Fact]
    public void MainWindowScreenshotTour_UsesPlannerForEnvironmentFilters()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

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
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_QAT_UNDO_REDO_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_TITLEBAR_WINDOW_CHROME_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_FORMULA_BAR_NAME_BOX_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_STATUS_FOOTER_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_VIEW_PANES_ZOOM_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_HOME_FONT_COLORS_TOUR\")");
        source.Should().Contain("RibbonScreenshotTourPlan?");
        source.Should().Contain("ResolveScreenshotTourOutputDirectory");
        source.Should().Contain("PrepareRibbonScreenshotTourContextAsync");
        source.Should().Contain("EnsureTableDesignScreenshotTourContext");
        source.Should().Contain("EnsurePivotTableScreenshotTourContext");
        source.Should().Contain("CaptureAutoFilterFlyoutTourAsync");
        source.Should().Contain("CaptureHomeAlignmentNumberTourAsync");
        source.Should().Contain("CapturePrintPreviewTourAsync");
        source.Should().Contain("CaptureOptionsAccountTourAsync");
        source.Should().Contain("CaptureQatUndoRedoTourAsync");
        source.Should().Contain("CaptureTitlebarWindowChromeTourAsync");
        source.Should().Contain("CaptureFormulaBarNameBoxTourAsync");
        source.Should().Contain("CaptureStatusFooterTourAsync");
        source.Should().Contain("CaptureViewPanesZoomTourAsync");
        source.Should().Contain("CaptureHomeFontColorsTourAsync");
        source.Should().Contain("PrepareRibbonBurstCapturePhaseAsync");
        source.Should().Contain("WaitForRibbonScreenshotRenderPassAsync");
        source.Should().Contain("DeleteStaleRibbonScreenshotTourCaptures");
        source.Should().Contain("DeleteRibbonScreenshotTourEvidence");
        source.Should().Contain("ValidateRibbonScreenshotTourCaptures");
        source.Should().Contain("DeleteAutoFilterFlyoutTourEvidence");
        source.Should().Contain("DeletePrintPreviewTourEvidence");
        source.Should().Contain("DeleteOptionsAccountTourEvidence");
        source.Should().Contain("WriteRibbonScreenshotTourManifestAsync");
        source.Should().Contain("WriteAutoFilterFlyoutTourManifestAsync");
        source.Should().Contain("WriteHomeAlignmentNumberTourManifestAsync");
        source.Should().Contain("WritePrintPreviewTourManifestAsync");
        source.Should().Contain("WriteOptionsAccountTourManifestAsync");
        source.Should().Contain("WriteQatUndoRedoTourManifestAsync");
        source.Should().Contain("WriteTitlebarWindowChromeTourManifestAsync");
        source.Should().Contain("WriteFormulaBarNameBoxTourManifestAsync");
        source.Should().Contain("WriteStatusFooterTourManifestAsync");
        source.Should().Contain("WriteViewPanesZoomTourManifestAsync");
        source.Should().Contain("ribbon_screenshot_tour_manifest.json");
        source.Should().Contain("autofilter_flyout_tour_manifest.json");
        source.Should().Contain("home_alignment_number_tour_manifest.json");
        source.Should().Contain("print_preview_tour_manifest.json");
        source.Should().Contain("options_account_tour_manifest.json");
        source.Should().Contain("qat_undo_redo_tour_manifest.json");
        source.Should().Contain("titlebar_window_chrome_tour_manifest.json");
        source.Should().Contain("formula_bar_name_box_tour_manifest.json");
        source.Should().Contain("status_footer_tour_manifest.json");
        source.Should().Contain("view_panes_zoom_tour_manifest.json");
        source.Should().Contain("home_font_colors_tour_manifest.json");
        source.Should().Contain("EvidencePurpose()");
        source.Should().Contain("EnsureWindowForegroundForScreenshotTourAsync");
        source.Should().Contain("AssertWindowForegroundForScreenshotTour");
        source.Should().Contain("GetForegroundWindow");
        source.Should().Contain("_suppressClosePrompt = true;");
        source.Should().Contain("throw new InvalidOperationException");
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
        source.Should().Contain("OpenRibbonContextMenu(OrientationPickerButton, OrientationPickerButton.ContextMenu)");
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
        editingSource.Should().Contain("AutoFilterDropdownPlanner.CreateMenuPlan(_workbook, sheet, plan)");
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
        source.Should().Contain("CellAddressBox.IsDropDownOpen = true;");
        source.Should().Contain("CellAddressBox.SelectedItem = \"Sales\";");
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
        source.Should().Contain("SsPrintPreviewButton.Focus();");
        source.Should().Contain("Keyboard.Focus(SsPrintPreviewButton);");
        source.Should().Contain("PrintPreviewTourManifest");
        source.Should().Contain("RenderTargetBitmap-print-preview-dialog-and-main-window");
        source.Should().Contain("The native Windows print dialog is not opened during this tour");
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
        source.Should().Contain("SsAccountBtn_Click(SsAccountNavBtn");
        source.Should().Contain("CaptureOwnedNativeDialogWhenShownAsync");
        source.Should().Contain("PrintWindow-owned-native-dialog");
        source.Should().Contain("new OptionsDialog(_options, _workbook.DisabledFormulaErrorCodes)");
        source.Should().Contain("FindDescendantByAutomationId<ListBox>(dialog, \"OptionsCategoryList\")");
        source.Should().Contain("OptionsCancelButton");
        source.Should().Contain("CategoryListFocusedByDefault");
        source.Should().Contain("OptionsClosedViaCancelEquivalent");
        source.Should().Contain("FocusReturnedToBackstageOptionsCommand");
        source.Should().Contain("AccountMicrosoft365Exclusion");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.OptionsAccountTourManifest");
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
        source.Should().Contain("SplitViewBtn_Click(SplitViewBtn, new RoutedEventArgs())");
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
        source.Should().Contain("ShowFormulasBtn_Click(ShowFormulasButton");
        source.Should().Contain("RemoveTraceArrows(kind: null, \"Remove Arrows\")");
        source.Should().Contain("FormulaAuditingService.FindFormulaErrorIssues(_workbook, _currentSheetId)");
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
}
