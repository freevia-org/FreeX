using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void ZoomCustomDialog_ReturnsFocusToWorksheetAfterAcceptOrCancel()
    {
        var viewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ViewCommands.cs");
        var method = ExtractMethodSource(viewSource, "private void ZoomCustomMenuItem_Click(");

        method.Should().Contain("try");
        method.Should().Contain("if (dialog.ShowDialog() != true)");
        // "Fix 39 verified review-7 FreeX findings" (cc73699978) replaced the crude
        // SheetGrid.SelectedRange?.ColCount ?? 1 approximation with GetSelectionPixelMetrics, which
        // walks the actual selected columns/rows and skips hidden ones before converting to pixels
        // (shared with ZoomFitSelection) -- a real fidelity fix, not just a rename.
        method.Should().Contain("var (selectedColumnWidths, selectedRowHeights) = GetSelectionPixelMetrics(SheetGrid.SelectedRange);");
        method.Should().Contain("var zoomPercent = ZoomSelectionPlanner.CalculateZoomPercent(");
        method.Should().Contain("dialog.Result.ZoomPercent,");
        method.Should().Contain("dialog.Result.FitSelection,");
        method.Should().Contain("selectedColumnWidths,");
        method.Should().Contain("selectedRowHeights);");
        method.Should().Contain("ZoomSlider.Value = StatusZoomSliderValueForPercent(zoomPercent);");
        method.Should().Contain("finally");
        method.Should().Contain("FocusSheetGridIfNeeded();");
    }

    [Fact]
    public void DrawingAndPictureController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var drawingSourcePath = Path.Combine(appHostDirectory, "MainWindow.Drawing.cs");

        File.Exists(drawingSourcePath).Should().BeTrue();
        var drawingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        mainSource.Should().NotContain("private void InsertPictureBtn_Click(");
        mainSource.Should().NotContain("private void PictureCropBtn_Click(");
        mainSource.Should().NotContain("private void InsertTextBox()");
        mainSource.Should().NotContain("private void InsertDrawingShape(");
        mainSource.Should().NotContain("private void ResizeSelectedDrawingObject()");
        mainSource.Should().NotContain("private DrawingObjectTarget? GetTargetDrawingObject(");

        drawingSource.Should().Contain("private async void InsertPictureBtn_Click(");
        drawingSource.Should().Contain("private void PictureCropBtn_Click(");
        drawingSource.Should().Contain("private void InsertTextBox()");
        drawingSource.Should().Contain("private void InsertDrawingShape(");
        drawingSource.Should().Contain("private void ResizeSelectedDrawingObject()");
        drawingSource.Should().Contain("private DrawingObjectTarget? GetTargetDrawingObject(");
    }

    [Fact]
    public void InsertPicture_UsesGuardedSingleFileDialogAndOwnedReadFailureMessage()
    {
        var drawingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        drawingSource.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        drawingSource.Should().Contain("UiText.Get(\"MainWindowDialog_InsertPictureTitle\")");
        drawingSource.Should().Contain("UiText.Get(\"MainWindowDialog_ImageFilesFilter\")");
        drawingSource.Should().Contain("checkFileExists: true");
        drawingSource.Should().Contain("multiselect: false");
        drawingSource.Should().Contain("if (!result.Chosen) return;");
        drawingSource.Should().Contain("System.IO.File.ReadAllBytesAsync(result.FileName!)");
        drawingSource.Should().Contain("DrawingInputParser.GetImageContentType(result.FileName!)");
        drawingSource.Should().Contain("PictureInsertionPlacementPlanner.CreateInsertPictureCommand(");
        drawingSource.Should().Contain("UiText.Format(\"MainWindowMessage_InsertPictureReadFailed\", ex.Message)");
        drawingSource.Should().Contain("SetActiveCell(range.Start);");
        drawingSource.Should().Contain("UpdateViewport();");
        drawingSource.Should().NotContain("new Microsoft.Win32.OpenFileDialog");
    }

    [Fact]
    public void DrawingCommands_UseOwnedNoTargetMessages()
    {
        var drawingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        foreach (var key in new[]
        {
            "MainWindowMessage_NoPictureFoundOnSheet",
            "MainWindowMessage_CropRequiresInsertedImage",
            "MainWindowMessage_NoDrawingShapesOnSheet",
            "MainWindowMessage_NoDrawingObjectOnSheet",
            "MainWindowMessage_NoDrawingShapeOnSheet",
            "MainWindowMessage_NoObjectsOnSheet",
            "MainWindowMessage_PictureSizeTitle",
            "MainWindowMessage_RotatePictureTitle",
            "MainWindowMessage_CropPictureTitle",
            "MainWindowMessage_ResetCropTitle",
            "MainWindowMessage_DrawTitle",
            "MainWindowMessage_ObjectSizeTitle",
            "MainWindowMessage_RotateObjectTitle",
            "MainWindowMessage_ObjectFillTitle",
            "MainWindowMessage_ObjectOutlineTitle",
            "MainWindowMessage_ShapeGradientTitle",
            "MainWindowMessage_ShapeEffectsTitle",
            "MainWindowMessage_SelectionPaneTitle"
        })
        {
            drawingSource.Should().Contain($"\"{key}\"");
        }
        drawingSource.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void PageLayoutCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var pageLayoutSourcePath = Path.Combine(appHostDirectory, "MainWindow.PageLayout.cs");

        File.Exists(pageLayoutSourcePath).Should().BeTrue();
        var pageLayoutSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PageLayout.cs");

        mainSource.Should().NotContain("private void PageLayoutDeferredBtn_Click(");
        mainSource.Should().NotContain("private void ThemeBtn_Click(");
        mainSource.Should().NotContain("private void PageMarginsBtn_Click(");
        mainSource.Should().NotContain("private void PrintAreaBtn_Click(");
        mainSource.Should().NotContain("private void PageSetupDialogBtn_Click(");

        pageLayoutSource.Should().Contain("private void PageLayoutDeferredBtn_Click(");
        pageLayoutSource.Should().Contain("private void ThemeBtn_Click(");
        pageLayoutSource.Should().Contain("private void PageMarginsBtn_Click(");
        pageLayoutSource.Should().Contain("private void PrintAreaBtn_Click(");
        pageLayoutSource.Should().Contain("private void PageSetupDialogBtn_Click(");
    }

    [Fact]
    public void SheetBackgroundImport_UsesNativeImageDialogGuardrailsAndOwnedWarnings()
    {
        var pageLayoutSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PageLayout.cs");

        pageLayoutSource.Should().Contain("private async void BackgroundChooseMenuItem_Click(");
        pageLayoutSource.Should().Contain("var openPlan = SheetBackgroundPickerPlanner.BuildOpenDialogPlan();");
        pageLayoutSource.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        pageLayoutSource.Should().Contain("UiText.Get(\"MainWindowDialog_ImageFilesFilter\")");
        pageLayoutSource.Should().Contain("checkFileExists: openPlan.CheckFileExists");
        pageLayoutSource.Should().Contain("multiselect: openPlan.Multiselect");
        pageLayoutSource.Should().Contain("title: UiText.Get(\"MainWindowDialog_SheetBackgroundTitle\")");
        pageLayoutSource.Should().Contain("if (!result.Chosen)");
        pageLayoutSource.Should().Contain("SheetBackgroundPickerPlanner.IsSupportedImagePath(result.FileName!)");
        pageLayoutSource.Should().Contain("UiText.Get(\"MainWindowMessage_SheetBackgroundUnsupportedImageType\")");
        pageLayoutSource.Should().Contain("File.ReadAllBytesAsync(result.FileName!)");
        pageLayoutSource.Should().Contain("UiText.Format(\"MainWindowMessage_SheetBackgroundReadFailed\", ex.Message)");
        pageLayoutSource.Should().Contain("UiText.Get(\"MainWindowMessage_SheetBackgroundTitle\")");
        pageLayoutSource.Should().Contain("SheetBackgroundPickerPlanner.TryBuildBackgroundImage(bytes, result.FileName!, out var background)");
        pageLayoutSource.Should().Contain("CreatePageLayoutCommandSession().PlanSetBackground(background)");
        pageLayoutSource.Should().NotContain("new Microsoft.Win32.OpenFileDialog");
        pageLayoutSource.Should().NotContain("private static bool IsSupportedSheetBackgroundFile(string fileName)");
        pageLayoutSource.Should().NotContain("DrawingInputParser.GetImageContentType(result.FileName!)");
        pageLayoutSource.Should().Contain("private void BackgroundClearMenuItem_Click(");
        pageLayoutSource.Should().Contain("CreatePageLayoutCommandSession().PlanClearBackground()");
    }

    [Fact]
    public void HelpExternalLinks_RouteThroughGuardedOwnedMessageHelper()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Should().Contain("private void OpenExternalHelpLink(string url, string title)");
        // External links route through the single guarded launcher (scheme allowlist enforced there),
        // so the raw shell launch must NOT live in this file anymore.
        source.Should().Contain("ExternalUrlLauncher.Open(url)");
        source.Should().NotContain("UseShellExecute");
        source.Should().Contain("ShowOwnedMessage(");
        source.Should().Contain("OpenExternalHelpLink(AppInfo.HelpUrl, UiText.Get(\"MainWindowMessage_HelpOnlineTitle\"))");
        source.Should().Contain("OpenExternalHelpLink(updates.ReleasesPageUrl, UiText.Get(\"MainWindowMessage_CheckForUpdatesTitle\"))");
        source.Should().Contain("OpenExternalHelpLink(AppIssueReporter.CreateIssueUrl(context), UiText.Get(\"MainWindowMessage_FeedbackTitle\"))");
    }

    [Fact]
    public void InsertSparkline_UsesDialogLocationForInitialInsertAndOwnedValidationWarnings()
    {
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");
        var method = ExtractMethodSource(insertSource, "private void InsertSparkline(");

        method.Should().Contain("new SparklineDialog(");
        method.Should().Contain("SparklinePlanner.ParseKind(type)");
        // "Fix 47 verified review-8 FreeX findings" (a336dccef7) taught the Location field to accept
        // a multi-cell range that expands into a sparkline group (matching Excel's "Insert
        // Sparklines" dialog), so single-cell ValidateInsert became group-aware ValidateInsertGroup.
        method.Should().Contain("SparklinePlanner.ValidateInsertGroup(");
        method.Should().Contain("SparklineInputValidation.InvalidDataRange");
        method.Should().Contain("SparklineInputValidation.InvalidLocation");
        method.Should().Contain("var kind = dialog.Result.Kind;");
        method.Should().NotContain("SparklineInputParser");
        method.Should().Contain("UiText.Get(\"MainWindowMessage_InsertSparklineInvalidDataRange\")");
        method.Should().Contain("UiText.Get(\"MainWindowMessage_InsertSparklineInvalidLocation\")");
        method.Should().Contain("UiText.Get(\"MainWindowMessage_InsertSparklineTitle\")");
        method.Should().Contain("var useDialogLocationForInitialInsert = true;");
        method.Should().Contain("useDialogLocationForInitialInsert");
        method.Should().Contain("? fallbackLocationRange");
        method.Should().Contain(": SheetGrid.SelectedRange ?? fallbackLocationRange");
        method.Should().Contain("TryExecuteRepeatableCommand(CreateCommand, \"Insert Sparkline\", out _)");
        method.Should().Contain("useDialogLocationForInitialInsert = false;");
        method.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void HyperlinkDialogAndCtrlClick_RouteThroughSetAndNavigatePlans()
    {
        var keyboardSource = DialogSourceTestSupport.ReadHostSources("KeyboardShortcutMatcher.CommandRules.cs");
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        keyboardSource.Should().Contain("KeyboardCommandShortcut.InsertHyperlink");
        commandSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertHyperlink, InsertLinkBtn_Click)");
        insertSource.Should().Contain("new HyperlinkDialog(prefill.Target, prefill.DisplayText) { Owner = this }");
        insertSource.Should().Contain("new SetHyperlinkCommand(");
        insertSource.Should().Contain("HyperlinkNavigationPlanner.TryCreatePlan");
        insertSource.Should().Contain("TryNavigateToWorkbookReference(plan.Target)");
        insertSource.Should().Contain("UiText.Get(\"MainWindowMessage_OpenHyperlinkTargetNotFound\")");
        insertSource.Should().Contain("UiText.Get(\"MainWindowMessage_OpenHyperlinkBlockedScheme\")");
        insertSource.Should().Contain("UiText.Get(\"MainWindowMessage_OpenHyperlinkOpenFailed\")");
        insertSource.Should().Contain("UiText.Get(\"MainWindowMessage_OpenHyperlinkTitle\")");
        ExtractMethodSource(insertSource, "private bool TryOpenHyperlink(").Should().NotContain("MessageBox.Show(");
        selectionSource.Should().Contain("else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)");
        selectionSource.Should().Contain("if (TryOpenHyperlink(newAddr))");
        selectionSource.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void MainWindow_RoutesColorChoicesThroughColorPickerDialog()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().NotContain("input.Split(',')");
        source.Should().Contain("private bool TryShowColorPicker(");
        source.Should().Contain("new ColorPickerDialog");
        source.Should().Contain("TryShowColorPicker(\"Font Color\"");
        source.Should().Contain("TryShowColorPicker(\"Fill Color\"");
    }

    [Fact]
    public void SpellCheckWorkflow_RoutesDistinctDialogActionsToIntendedResults()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        source.Should().Contain("SpellCheckDialogAction.ReplaceAll");
        source.Should().Contain("SpellCheckDialogAction.IgnoreAll");
        source.Should().Contain("SpellCheckDialogAction.Ignore");
        source.Should().Contain("SpellCheckDialogAction.Add");
        source.Should().Contain("while (true)");
        source.Should().Contain("SpellCheckWorkflowPlanner.ScanWorksheet(");
        source.Should().Contain("SpellCheckWorkflowPlanner.BuildReplaceAllCommand(issues, issue.Word, replacement)");
        source.Should().Contain("SpellCheckWorkflowPlanner.BuildReplacementCommand(issue, replacement)");
        source.Should().NotContain("BuildSpellCheckEdits");
        source.Should().Contain("TryExecuteSpellCheckCommand");
        source.Should().Contain("TryExecuteCommand(command, \"Spell Check\")");
        source.Should().NotContain("TryExecuteEditCells(edits, \"Spell Check\")");

        var plannerSource = DialogSourceTestSupport.ReadAppServicesSource("SpellCheckWorkflowPlanner.cs");
        plannerSource.Should().Contain("ContainsIgnoredWord(ignoredWords, issue.Word)");
        plannerSource.Should().Contain("ignoredIssues.Contains(CreateIssueKey(issue))");
        plannerSource.Should().Contain("new(FilterIssues(");
        plannerSource.Should().Contain("SpellCheckService.ApplyCorrection(issue, replacement)");
        plannerSource.Should().Contain("SpellingIssueSource.ThreadedCommentReply");
    }

    [Fact]
    public void RemainingStatusWorkflows_OpenNamedDialogsInsteadOfMessageBoxes()
    {
        _ = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var pageLayoutSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PageLayout.cs");
        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");
        var reviewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        pageLayoutSource.Should().Contain("new PageBreakDialog");
        dataSource.Should().Contain("new GoalSeekStatusDialog");
        reviewSource.Should().Contain("new WorkbookStatisticsDialog");
        reviewSource.Should().Contain("new AccessibilityCheckerDialog");
    }

    [Fact]
    public void ScenarioShow_IsRepeatableForF4WithoutReopeningDialog()
    {
        var scenarioSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ScenarioCommands.cs");

        scenarioSource.Should().Contain("TryExecuteRepeatableCommand(");
        scenarioSource.Should().Contain("() => new ApplyScenarioCommand(name)");
        scenarioSource.Should().NotContain("RecalculateIfAutomatic(outcome.AffectedCells ?? []);");
        scenarioSource.Should().Contain("CellAddress? first = null;");
        scenarioSource.Should().Contain("foreach (var cell in outcome.AffectedCells)");
        scenarioSource.Should().Contain("if (first is { } firstCell)");
        scenarioSource.Should().Contain("SetActiveCell(firstCell);");
        scenarioSource.Should().Contain("EnsureCellVisible(firstCell);");
    }

    [Fact]
    public void AdvancedFilterDialogApply_IsRepeatableForF4WithoutReopeningDialog()
    {
        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");

        // R72-commands-sort-filter-4-3 split this out of the dialog click handler into
        // ApplyAdvancedFilterResult(AdvancedFilterDialogResult result), called as
        // ApplyAdvancedFilterResult(dialog.Result), so "result" is now a method parameter rather
        // than a local assigned from dialog.Result inline.
        dataSource.Should().Contain("ApplyAdvancedFilterResult(dialog.Result);");
        dataSource.Should().Contain("private void ApplyAdvancedFilterResult(AdvancedFilterDialogResult result)");
        dataSource.Should().Contain("TryExecuteRepeatableCommand(");
        dataSource.Should().Contain("() => new AdvancedFilterCommand(");
        dataSource.Should().Contain("result.ListRange");
        dataSource.Should().Contain("result.CriteriaRange");
        dataSource.Should().Contain("result.CopyToCell");
        dataSource.Should().Contain("result.UniqueRecordsOnly");
        dataSource.Should().Contain("result.CopyToRange");
        dataSource.Should().NotContain("_commandBus.ExecuteRepeatable(");
    }

    [Fact]
    public void GoalSeekAndForecastSheet_DialogWorkflowsAreNotBlindF4Repeatable()
    {
        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");
        var goalSeekMethod = ExtractMethodSource(dataSource, "private void GoalSeekBtn_Click(");
        var forecastMethod = ExtractMethodSource(dataSource, "private void ForecastSheetBtn_Click(");

        goalSeekMethod.Should().Contain("new GoalSeekDialog(");
        goalSeekMethod.Should().Contain("new GoalSeekStatusDialog(");
        goalSeekMethod.Should().Contain("new GoalSeekCommand(");
        goalSeekMethod.Should().Contain("TryExecuteCommand(cmd, \"Goal Seek\")");
        goalSeekMethod.Should().NotContain("ExecuteRepeatable");
        goalSeekMethod.Should().NotContain("TryExecuteRepeatable");

        forecastMethod.Should().Contain("new ForecastSheetDialog");
        forecastMethod.Should().Contain("ForecastSheetSourceRangePlanner.Create(sheet, range)");
        forecastMethod.Should().Contain("new ForecastSheetCommand(");
        forecastMethod.Should().Contain("TryExecuteCommand(new ForecastSheetCommand(forecastRange, dialog.Result.Periods), \"Forecast Sheet\")");
        forecastMethod.Should().NotContain("ExecuteRepeatable");
        forecastMethod.Should().NotContain("TryExecuteRepeatable");
    }

    [Fact]
    public void RowAndColumnDimensionDialogs_AreRepeatableForF4AgainstCurrentSelection()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");

        source.Should().Contain("TryExecuteRepeatableGroupedSheetCommand(");
        source.Should().Contain("\"Row Height\",");
        source.Should().Contain("\"Column Width\",");
        source.Should().Contain("new RowHeightDialog(RowColumnSizingPlanner.GetRowHeightDialogValue(sheet, range)) { Owner = this };");
        source.Should().Contain("new ColumnWidthDialog(RowColumnSizingPlanner.GetColumnWidthDialogValue(sheet, range)) { Owner = this };");
        source.Should().Contain("var currentRange = SheetGrid.SelectedRange ?? range;");
        source.Should().Contain("RowColumnSizingPlanner.CreateRowHeightCommand(sheetId, currentRange, dialog.Result.Height)");
        source.Should().Contain("RowColumnSizingPlanner.CreateColumnWidthCommand(sheetId, currentRange, dialog.Result.Width)");
        source.Should().Contain("RowColumnSizingPlanner.CreateRowsHiddenCommand(sheetId, currentRange, hidden)");
        source.Should().Contain("RowColumnSizingPlanner.CreateColumnsHiddenCommand(sheetId, currentRange, hidden)");
        source.Should().NotContain("TryExecuteGroupedSheetCommand(\"Row Height\"");
        source.Should().NotContain("TryExecuteGroupedSheetCommand(\"Column Width\"");

        var plannerSource = DialogSourceTestSupport.ReadAppServicesRibbonSource("RowColumnSizingPlanner.cs");
        plannerSource.Should().Contain("sheet.RowHeights.TryGetValue(startRow, out var height) ? height : sheet.DefaultRowHeight");
        plannerSource.Should().Contain("sheet.ColumnWidths.TryGetValue(startCol, out var width) ? width : sheet.DefaultColumnWidth");
        // Round 83 (d8a9dbea7c) fixed CreateRowHeightCommand to convert the dialog's points value
        // back to the pixel unit Sheet.RowHeights stores (the same 96/72 conversion the XLSX file-I/O
        // boundary already applied) -- Column Width has no such unit mismatch, so it is unconverted.
        plannerSource.Should().Contain("new SetRowHeightCommand(sheetId, startRow, endRow, height * PixelsPerPoint)");
        plannerSource.Should().Contain("new SetColumnWidthCommand(sheetId, startCol, endCol, width)");
        plannerSource.Should().Contain("new SetRowsHiddenCommand(sheetId, startRow, endRow, hidden)");
        plannerSource.Should().Contain("new SetColumnsHiddenCommand(sheetId, startCol, endCol, hidden)");
    }

    [Fact]
    public void ConditionalFormattingEllipsisCommands_UseRuleFamilyDialogFactory()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("ConditionalFormatDialogFactory.Create(ruleType, range)");
        source.Should().NotContain("new ConditionalFormatDialog(ruleType, range)");
    }

    [Fact]
    public void ConditionalFormattingRulesManager_ApplyUsesSameWorkbookCommandAsOk()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("new ManageConditionalFormatsDialog(");
        source.Should().Contain("applyRules: ApplyManagedConditionalFormatRules)");
        source.Should().Contain("private void ApplyManagedConditionalFormatRules(IReadOnlyList<ConditionalFormat> newRules)");
        source.Should().Contain("ConditionalFormatCommandPlanner.PlanReplaceAll(");
        source.Should().NotContain("new ReplaceAllConditionalFormatsCommand(");
        CountOccurrences(source, "ConditionalFormatCommandPlanner.PlanReplaceAll(").Should().Be(1);
    }

    [Fact]
    public void ConditionalFormattingRulesManager_WiresAppliesToRangePickerCallback()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("requestAppliesToRangeSelection: request => ApplyConditionalFormatAppliesToRangeSelection(dlg, request)");
        source.Should().Contain("private void ApplyConditionalFormatAppliesToRangeSelection(");
        source.Should().Contain("ConditionalFormatAppliesToRangeSelectionRequest request");
        source.Should().Contain("BeginDialogRangeSelection(");
        source.Should().Contain("request.CollapseDialog");
        source.Should().Contain("selectedRange => dialog.ApplyAppliesToRangeSelection(request.RuleId, selectedRange)");
    }

    [Fact]
    public void PivotTableDesignCommands_OpenOptionsDialogInsteadOfCyclingLayoutState()
    {
        var ribbon = DialogSourceTestSupport.ReadRibbonDefinitionSource("FreeXRibbonDefinition.cs");
        var source = ReadPivotCommandSource();

        // After the ribbon XAML→declarative cutover the PivotTable options entry point is declared in the
        // single-source ribbon as the "PivotTable Options" command (PivotTable Analyze tab) rather than a
        // hand-authored MainWindow.xaml button. The command still opens the options dialog (asserted below)
        // instead of cycling layout/style state.
        ribbon.Should().Contain(".Medium(\"PivotTable Options\", \"PivotTable Options\"");
        ribbon.Should().NotContain("Cycle grand totals");
        ribbon.Should().NotContain("Cycle subtotals");
        ribbon.Should().NotContain("Cycle PivotTable style gallery choices.");
        source.Should().Contain("PivotCacheModel? cache = null;");
        source.Should().Contain("foreach (var item in _workbook.PivotCaches)");
        source.Should().Contain("if (item.CacheId != pivotTable.CacheId)");
        source.Should().Contain("cache = item;");
        source.Should().Contain("new PivotTableOptionsDialog(pivotTable, cache)");
        source.Should().Contain("ApplyPivotOptions(pivotTable, dialog.Result)");
        source.Should().NotContain("var reportLayout = pivotTable.ReportLayout switch");
        source.Should().NotContain("var styleName = pivotTable.StyleName switch");
    }

    [Fact]
    public void ChartFormattingCommands_OpenExplicitFormatDialogs()
    {
        var source = ReadChartCommandSource();

        source.Should().Contain("new ChartDataLabelsDialog(chart)");
        source.Should().Contain("new ChartTrendlineOptionsDialog(chart)");
        source.Should().Contain("new ChartAxisFormatDialog(chart, useXAxis)");
        source.Should().Contain("var seriesCount = ChartSeriesFormatPlanner.GetSeriesCount(chart);");
        source.Should().Contain("new ChartSeriesFormatDialog(chart, seriesCount)");
        source.Should().Contain("var command = ChartWorkflowCommandCatalog.FormatDataLabels;");
        source.Should().Contain("var command = ChartWorkflowCommandCatalog.FormatTrendline;");
        source.Should().Contain("var command = ChartWorkflowCommandCatalog.FormatDataSeries;");
        source.Should().Contain("ChartWorkflowCommandCatalog.CanOpenDialog(chart, command)");
        source.Should().Contain("ShowUnsupportedChartWorkflow(command)");
        source.Should().Contain("ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions())");
        source.Should().Contain("UiText.Get(\"ChartAxisFormat_XAxisTitle\")");
        source.Should().Contain("UiText.Get(\"ChartAxisFormat_YAxisTitle\")");
        source.Should().Contain("ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions(chart))");
    }

    [Fact]
    public void PictureCropRibbon_OffersCropAndResetCropMenuActions()
    {
        // The ribbon moved to the single-source FreeXRibbonDefinition (FreeX.Ribbon.Definitions project);
        // the "Crop Picture" split button exposes "Crop..." and "Reset Crop" menu items, which the
        // generated handler map wires to the dialog/reset handlers in MainWindow.Drawing.cs.
        var ribbon = DialogSourceTestSupport.ReadRibbonDefinitionSource("FreeXRibbonDefinition.cs");
        var handlers = DialogSourceTestSupport.ReadHostSources("Ribbon\\FreeXRibbonHandlerMap.g.cs");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        ribbon.Should().Contain("menu: m => m.Item(\"Crop\", \"Crop...\", \"C\").Item(\"Reset Crop\", \"Reset Crop\", \"R\")");
        handlers.Should().Contain("[\"Crop\"] = \"PictureCropDialogMenuItem_Click\"");
        handlers.Should().Contain("[\"Reset Crop\"] = \"PictureResetCropMenuItem_Click\"");
        source.Should().Contain("PictureResetCropMenuItem_Click");
        source.Should().Contain("new SetPictureCropCommand(");
        source.Should().Contain("0, 0, 0, 0");
    }

    [Fact]
    public void MainWindowCommandPartials_UseMessageServiceNotDirectMessageBox()
    {
        var appHostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");

        // Verify the service wiring exists in the constructor.
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        mainSource.Should().Contain("IUserMessageService messageService");
        mainSource.Should().Contain("_messageService = messageService;");

        var editingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");
        var showOwnedMessage = ExtractMethodSource(editingSource, "private MessageBoxResult ShowOwnedMessage(");
        showOwnedMessage.Should().Contain("_messageService.ShowMessage(");
        showOwnedMessage.Should().Contain("ToUserMessageButtons(button)");
        showOwnedMessage.Should().Contain("ToUserMessageIcon(icon)");
        showOwnedMessage.Should().NotContain("MessageBox.Show(");

        // Each migrated partial must not call MessageBox.Show directly.
        foreach (var partial in new[]
        {
            "MainWindow.CellsCommands.cs",
            "MainWindow.ChartCommands.cs",
            "MainWindow.CommandExecution.cs",
            "MainWindow.DataCommands.cs",
            "MainWindow.DataFilterCommands.cs",
            "MainWindow.FormulaCommands.cs",
            "MainWindow.HomeEditing.cs",
            "MainWindow.PageLayout.cs",
            "MainWindow.PivotChartCommands.cs",
            "MainWindow.PivotCommands.cs",
            "MainWindow.ReviewCommands.cs",
            "MainWindow.ScenarioCommands.cs",
            "MainWindow.SheetTabs.cs"
        })
        {
            var partialPath = Path.Combine(appHostDirectory, partial);
            if (!File.Exists(partialPath)) continue;
            var partialSource = DialogSourceTestSupport.ReadHostSources(partial);
            partialSource.Should()
                .NotContain("MessageBox.Show(", because: $"{partial} should delegate to _messageService");
        }
    }
}
