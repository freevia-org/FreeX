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
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_AUTOFILTER_FLYOUT_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_PRINT_PREVIEW_TOUR\")");
        source.Should().Contain("Environment.GetEnvironmentVariable(\"FREEX_QAT_UNDO_REDO_TOUR\")");
        source.Should().Contain("RibbonScreenshotTourPlan?");
        source.Should().Contain("PrepareRibbonScreenshotTourContextAsync");
        source.Should().Contain("EnsureTableDesignScreenshotTourContext");
        source.Should().Contain("EnsurePivotTableScreenshotTourContext");
        source.Should().Contain("CaptureAutoFilterFlyoutTourAsync");
        source.Should().Contain("CapturePrintPreviewTourAsync");
        source.Should().Contain("CaptureQatUndoRedoTourAsync");
        source.Should().Contain("PrepareRibbonBurstCapturePhaseAsync");
        source.Should().Contain("WaitForRibbonScreenshotRenderPassAsync");
        source.Should().Contain("DeleteStaleRibbonScreenshotTourCaptures");
        source.Should().Contain("DeleteRibbonScreenshotTourEvidence");
        source.Should().Contain("ValidateRibbonScreenshotTourCaptures");
        source.Should().Contain("DeleteAutoFilterFlyoutTourEvidence");
        source.Should().Contain("DeletePrintPreviewTourEvidence");
        source.Should().Contain("WriteRibbonScreenshotTourManifestAsync");
        source.Should().Contain("WriteAutoFilterFlyoutTourManifestAsync");
        source.Should().Contain("WritePrintPreviewTourManifestAsync");
        source.Should().Contain("WriteQatUndoRedoTourManifestAsync");
        source.Should().Contain("ribbon_screenshot_tour_manifest.json");
        source.Should().Contain("autofilter_flyout_tour_manifest.json");
        source.Should().Contain("print_preview_tour_manifest.json");
        source.Should().Contain("qat_undo_redo_tour_manifest.json");
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
}
