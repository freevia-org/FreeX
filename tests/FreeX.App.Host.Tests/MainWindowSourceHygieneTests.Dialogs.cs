using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void ZoomCustomDialog_ReturnsFocusToWorksheetAfterAcceptOrCancel()
    {
        var viewSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));
        var method = ExtractMethodSource(viewSource, "private void ZoomCustomMenuItem_Click(");

        method.Should().Contain("try");
        method.Should().Contain("if (dialog.ShowDialog() != true)");
        method.Should().Contain("var zoomPercent = ZoomSelectionPlanner.CalculateDialogZoomPercent(");
        method.Should().Contain("dialog.Result,");
        method.Should().Contain("SheetGrid.SelectedRange?.ColCount ?? 1,");
        method.Should().Contain("ZoomSlider.Value = FreeX.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);");
        method.Should().Contain("finally");
        method.Should().Contain("FocusSheetGridIfNeeded();");
    }

    [Fact]
    public void DrawingAndPictureController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var drawingSourcePath = Path.Combine(appHostDirectory, "MainWindow.Drawing.cs");

        File.Exists(drawingSourcePath).Should().BeTrue();
        var drawingSource = File.ReadAllText(drawingSourcePath);

        mainSource.Should().NotContain("private void InsertPictureBtn_Click(");
        mainSource.Should().NotContain("private void PictureCropBtn_Click(");
        mainSource.Should().NotContain("private void InsertTextBox()");
        mainSource.Should().NotContain("private void InsertDrawingShape(");
        mainSource.Should().NotContain("private void ResizeSelectedDrawingObject()");
        mainSource.Should().NotContain("private DrawingObjectTarget? GetTargetDrawingObject(");

        drawingSource.Should().Contain("private void InsertPictureBtn_Click(");
        drawingSource.Should().Contain("private void PictureCropBtn_Click(");
        drawingSource.Should().Contain("private void InsertTextBox()");
        drawingSource.Should().Contain("private void InsertDrawingShape(");
        drawingSource.Should().Contain("private void ResizeSelectedDrawingObject()");
        drawingSource.Should().Contain("private DrawingObjectTarget? GetTargetDrawingObject(");
    }

    [Fact]
    public void InsertPicture_UsesGuardedSingleFileDialogAndOwnedReadFailureMessage()
    {
        var drawingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));

        drawingSource.Should().Contain("Title = UiText.Get(\"MainWindowDialog_InsertPictureTitle\")");
        drawingSource.Should().Contain("Filter = UiText.Get(\"MainWindowDialog_ImageFilesFilter\")");
        drawingSource.Should().Contain("CheckFileExists = true");
        drawingSource.Should().Contain("Multiselect = false");
        drawingSource.Should().Contain("if (dialog.ShowDialog(this) != true) return;");
        drawingSource.Should().Contain("System.IO.File.ReadAllBytes(dialog.FileName)");
        drawingSource.Should().Contain("DrawingInputParser.GetImageContentType(dialog.FileName)");
        drawingSource.Should().Contain("new InsertPictureCommand(");
        drawingSource.Should().Contain("UiText.Format(\"MainWindowMessage_InsertPictureReadFailed\", ex.Message)");
        drawingSource.Should().Contain("SetActiveCell(range.Start);");
        drawingSource.Should().Contain("UpdateViewport();");
    }

    [Fact]
    public void DrawingCommands_UseOwnedNoTargetMessages()
    {
        var drawingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var pageLayoutSourcePath = Path.Combine(appHostDirectory, "MainWindow.PageLayout.cs");

        File.Exists(pageLayoutSourcePath).Should().BeTrue();
        var pageLayoutSource = File.ReadAllText(pageLayoutSourcePath);

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
        var pageLayoutSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PageLayout.cs"));

        pageLayoutSource.Should().Contain("private void BackgroundChooseMenuItem_Click(");
        pageLayoutSource.Should().Contain("Title = UiText.Get(\"MainWindowDialog_SheetBackgroundTitle\")");
        pageLayoutSource.Should().Contain("Filter = UiText.Get(\"MainWindowDialog_ImageFilesFilter\")");
        pageLayoutSource.Should().Contain("CheckFileExists = true");
        pageLayoutSource.Should().Contain("Multiselect = false");
        pageLayoutSource.Should().Contain("if (dialog.ShowDialog(this) != true)");
        pageLayoutSource.Should().Contain("IsSupportedSheetBackgroundFile(dialog.FileName)");
        pageLayoutSource.Should().Contain("UiText.Get(\"MainWindowMessage_SheetBackgroundUnsupportedImageType\")");
        pageLayoutSource.Should().Contain("File.ReadAllBytes(dialog.FileName)");
        pageLayoutSource.Should().Contain("UiText.Format(\"MainWindowMessage_SheetBackgroundReadFailed\", ex.Message)");
        pageLayoutSource.Should().Contain("UiText.Get(\"MainWindowMessage_SheetBackgroundTitle\")");
        pageLayoutSource.Should().Contain("new WorksheetBackgroundImage(");
        pageLayoutSource.Should().Contain("DrawingInputParser.GetImageContentType(dialog.FileName)");
        pageLayoutSource.Should().Contain("TryExecuteGroupedSheetCommand(\"Sheet Background\"");
        pageLayoutSource.Should().Contain("new SetWorksheetBackgroundCommand(sheetId, background)");
        pageLayoutSource.Should().Contain("private static bool IsSupportedSheetBackgroundFile(string fileName)");
        pageLayoutSource.Should().Contain("\".png\" or \".jpg\" or \".jpeg\" or \".bmp\" or \".gif\" => true");
        pageLayoutSource.Should().Contain("private void BackgroundClearMenuItem_Click(");
        pageLayoutSource.Should().Contain("TryExecuteGroupedSheetCommand(\"Clear Sheet Background\"");
        pageLayoutSource.Should().Contain("new ClearWorksheetBackgroundCommand(sheetId)");
    }

    [Fact]
    public void HelpExternalLinks_RouteThroughGuardedOwnedMessageHelper()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("private void OpenExternalHelpLink(string url, string title)");
        // External links route through the single guarded launcher (scheme allowlist enforced there),
        // so the raw shell launch must NOT live in this file anymore.
        source.Should().Contain("ExternalUrlLauncher.Open(url)");
        source.Should().NotContain("UseShellExecute");
        source.Should().Contain("ShowOwnedMessage(");
        source.Should().Contain("OpenExternalHelpLink(AppInfo.HelpUrl, UiText.Get(\"MainWindowMessage_HelpOnlineTitle\"))");
        source.Should().Contain("OpenExternalHelpLink(AppUpdateSource.CreateDefault().ReleasePageUrl, UiText.Get(\"MainWindowMessage_CheckForUpdatesTitle\"))");
        source.Should().Contain("OpenExternalHelpLink(AppIssueReporter.CreateIssueUrl(context), UiText.Get(\"MainWindowMessage_FeedbackTitle\"))");
    }

    [Fact]
    public void InsertSparkline_UsesDialogLocationForInitialInsertAndOwnedValidationWarnings()
    {
        var insertSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.InsertCommands.cs"));
        var method = ExtractMethodSource(insertSource, "private void InsertSparkline(");

        method.Should().Contain("new SparklineDialog(");
        method.Should().Contain("SparklineInputParser.TryParseDataRange(dialog.Result.DataRangeText, _currentSheetId, out var dataRange)");
        method.Should().Contain("SparklineInputParser.TryParseLocation(dialog.Result.LocationText, _currentSheetId, out var location)");
        method.Should().Contain("UiText.Get(\"MainWindowMessage_InsertSparklineInvalidDataRange\")");
        method.Should().Contain("UiText.Get(\"MainWindowMessage_InsertSparklineInvalidLocation\")");
        method.Should().Contain("UiText.Get(\"MainWindowMessage_InsertSparklineTitle\")");
        method.Should().Contain("var useDialogLocationForInitialInsert = true;");
        method.Should().Contain("useDialogLocationForInitialInsert");
        method.Should().Contain("? fallbackLocationRange");
        method.Should().Contain(": SheetGrid.SelectedRange ?? fallbackLocationRange");
        method.Should().Contain("var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);");
        method.Should().Contain("useDialogLocationForInitialInsert = false;");
        method.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void HyperlinkDialogAndCtrlClick_RouteThroughSetAndNavigatePlans()
    {
        var keyboardSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "KeyboardShortcutMatcher.CommandRules.cs"));
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));
        var insertSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.InsertCommands.cs"));
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().NotContain("input.Split(',')");
        source.Should().Contain("private bool TryShowColorPicker(");
        source.Should().Contain("new ColorPickerDialog");
        source.Should().Contain("TryShowColorPicker(\"Font Color\"");
        source.Should().Contain("TryShowColorPicker(\"Fill Color\"");
    }

    [Fact]
    public void SpellCheckWorkflow_RoutesDistinctDialogActionsToIntendedResults()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("SpellCheckDialogAction.ReplaceAll");
        source.Should().Contain("SpellCheckDialogAction.IgnoreAll");
        source.Should().Contain("SpellCheckDialogAction.Ignore");
        source.Should().Contain("SpellCheckDialogAction.Add");
        source.Should().Contain("while (true)");
        source.Should().Contain("SpellCheckWorkflowPlanner.FilterIssues(");
        source.Should().Contain("SpellCheckWorkflowPlanner.BuildReplaceAllCommand(issues, issue.Word, replacement)");
        source.Should().Contain("SpellCheckWorkflowPlanner.BuildReplacementCommand(issue, replacement)");
        source.Should().NotContain("BuildSpellCheckEdits");
        source.Should().Contain("TryExecuteSpellCheckCommand");
        source.Should().Contain("TryExecuteCommand(command, \"Spell Check\")");
        source.Should().NotContain("TryExecuteEditCells(edits, \"Spell Check\")");

        var plannerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SpellCheckWorkflowPlanner.cs"));
        plannerSource.Should().Contain("ContainsIgnoredWord(ignoredWords, issue.Word)");
        plannerSource.Should().Contain("ignoredIssues.Contains(CreateIssueKey(issue))");
        plannerSource.Should().Contain("SpellCheckService.ApplyCorrection(issue, replacement)");
        plannerSource.Should().Contain("SpellingIssueSource.ThreadedCommentReply");
    }

    [Fact]
    public void RemainingStatusWorkflows_OpenNamedDialogsInsteadOfMessageBoxes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml.cs"));
        var pageLayoutSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PageLayout.cs"));
        var dataSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));
        var reviewSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        pageLayoutSource.Should().Contain("new PageBreakDialog");
        dataSource.Should().Contain("new GoalSeekStatusDialog");
        reviewSource.Should().Contain("new WorkbookStatisticsDialog");
        reviewSource.Should().Contain("new AccessibilityCheckerDialog");
    }

    [Fact]
    public void ScenarioShow_IsRepeatableForF4WithoutReopeningDialog()
    {
        var scenarioSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ScenarioCommands.cs"));

        scenarioSource.Should().Contain("_commandBus.ExecuteRepeatable(_workbook.Id, () => new ApplyScenarioCommand(name))");
        scenarioSource.Should().Contain("RecalculateIfAutomatic(outcome.AffectedCells ?? []);");
        scenarioSource.Should().Contain("SetActiveCell(first);");
        scenarioSource.Should().Contain("EnsureCellVisible(first);");
    }

    [Fact]
    public void AdvancedFilterDialogApply_IsRepeatableForF4WithoutReopeningDialog()
    {
        var dataSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        dataSource.Should().Contain("var result = dialog.Result;");
        dataSource.Should().Contain("_commandBus.ExecuteRepeatable(");
        dataSource.Should().Contain("() => new AdvancedFilterCommand(");
        dataSource.Should().Contain("result.ListRange");
        dataSource.Should().Contain("result.CriteriaRange");
        dataSource.Should().Contain("result.CopyToCell");
        dataSource.Should().Contain("result.UniqueRecordsOnly");
        dataSource.Should().Contain("result.CopyToRange");
        dataSource.Should().NotContain("_commandBus.Execute(\r\n            _workbook.Id,\r\n            new AdvancedFilterCommand(");
    }

    [Fact]
    public void GoalSeekAndForecastSheet_DialogWorkflowsAreNotBlindF4Repeatable()
    {
        var dataSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));
        var goalSeekMethod = ExtractMethodSource(dataSource, "private void GoalSeekBtn_Click(");
        var forecastMethod = ExtractMethodSource(dataSource, "private void ForecastSheetBtn_Click(");

        goalSeekMethod.Should().Contain("new GoalSeekDialog(");
        goalSeekMethod.Should().Contain("new GoalSeekStatusDialog(");
        goalSeekMethod.Should().Contain("new GoalSeekCommand(");
        goalSeekMethod.Should().Contain("TryExecuteCommand(cmd, \"Goal Seek\")");
        goalSeekMethod.Should().NotContain("ExecuteRepeatable");
        goalSeekMethod.Should().NotContain("TryExecuteRepeatable");

        forecastMethod.Should().Contain("new ForecastSheetDialog");
        forecastMethod.Should().Contain("new ForecastSheetCommand(");
        forecastMethod.Should().Contain("TryExecuteCommand(new ForecastSheetCommand(range, dialog.Result.Periods), \"Forecast Sheet\")");
        forecastMethod.Should().NotContain("ExecuteRepeatable");
        forecastMethod.Should().NotContain("TryExecuteRepeatable");
    }

    [Fact]
    public void RowAndColumnDimensionDialogs_AreRepeatableForF4AgainstCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.CellsCommands.cs"));

        source.Should().Contain("TryExecuteRepeatableGroupedSheetCommand(");
        source.Should().Contain("\"Row Height\",");
        source.Should().Contain("\"Column Width\",");
        source.Should().Contain("new RowHeightDialog(RowColumnDimensionPlanner.GetRowHeightDialogValue(sheet, range)) { Owner = this };");
        source.Should().Contain("new ColumnWidthDialog(RowColumnDimensionPlanner.GetColumnWidthDialogValue(sheet, range)) { Owner = this };");
        source.Should().Contain("var currentRange = SheetGrid.SelectedRange ?? range;");
        source.Should().Contain("RowColumnDimensionPlanner.CreateRowHeightCommand(sheetId, currentRange, dialog.Result.Height)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateColumnWidthCommand(sheetId, currentRange, dialog.Result.Width)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateRowsHiddenCommand(sheetId, currentRange, hidden)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateColumnsHiddenCommand(sheetId, currentRange, hidden)");
        source.Should().NotContain("TryExecuteGroupedSheetCommand(\"Row Height\"");
        source.Should().NotContain("TryExecuteGroupedSheetCommand(\"Column Width\"");

        var plannerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RowColumnDimensionPlanner.cs"));
        plannerSource.Should().Contain("sheet.RowHeights.TryGetValue(startRow, out var height) ? height : sheet.DefaultRowHeight");
        plannerSource.Should().Contain("sheet.ColumnWidths.TryGetValue(startCol, out var width) ? width : sheet.DefaultColumnWidth");
        plannerSource.Should().Contain("new SetRowHeightCommand(sheetId, startRow, endRow, height)");
        plannerSource.Should().Contain("new SetColumnWidthCommand(sheetId, startCol, endCol, width)");
        plannerSource.Should().Contain("new SetRowsHiddenCommand(sheetId, startRow, endRow, hidden)");
        plannerSource.Should().Contain("new SetColumnsHiddenCommand(sheetId, startCol, endCol, hidden)");
    }

    [Fact]
    public void ConditionalFormattingEllipsisCommands_UseRuleFamilyDialogFactory()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("ConditionalFormatDialogFactory.Create(ruleType, range)");
        source.Should().NotContain("new ConditionalFormatDialog(ruleType, range)");
    }

    [Fact]
    public void ConditionalFormattingRulesManager_ApplyUsesSameWorkbookCommandAsOk()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("new ManageConditionalFormatsDialog(");
        source.Should().Contain("applyRules: ApplyManagedConditionalFormatRules)");
        source.Should().Contain("private void ApplyManagedConditionalFormatRules(IReadOnlyList<ConditionalFormat> newRules)");
        source.Should().Contain("new ReplaceAllConditionalFormatsCommand(sheetId, remapped)");
        source.Should().Contain("GroupedSheetRangePlanner.CloneConditionalFormatForSheet(r, sheetId)");
        CountOccurrences(source, "new ReplaceAllConditionalFormatsCommand(sheetId, remapped)").Should().Be(1);
    }

    [Fact]
    public void ConditionalFormattingRulesManager_WiresAppliesToRangePickerCallback()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("requestAppliesToRangeSelection: request => ApplyConditionalFormatAppliesToRangeSelection(dlg, request)");
        source.Should().Contain("private void ApplyConditionalFormatAppliesToRangeSelection(");
        source.Should().Contain("ConditionalFormatAppliesToRangeSelectionRequest request");
        source.Should().Contain("if (request.CollapseDialog)");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.ApplyAppliesToRangeSelection(request.RuleId, selectedRange);");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Fact]
    public void PivotTableDesignCommands_OpenOptionsDialogInsteadOfCyclingLayoutState()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = ReadPivotCommandSource();

        xaml.ShouldContainLocalizedAttribute("local:RibbonTooltip.Description", "Open PivotTable layout and style options.");
        xaml.Should().NotContain("Cycle grand totals");
        xaml.Should().NotContain("Cycle subtotals");
        xaml.Should().NotContain("Cycle PivotTable style gallery choices.");
        source.Should().Contain("_workbook.PivotCaches.FirstOrDefault(item => item.CacheId == pivotTable.CacheId)");
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
        source.Should().Contain("new ChartSeriesFormatDialog(chart, ChartOptionCycler.GetSeriesCount(chart))");
        source.Should().Contain("ApplyChartLayoutDialogResult(\"Format Data Labels\"");
        source.Should().Contain("ApplyChartLayoutDialogResult(\"Format Trendline\"");
        source.Should().Contain("ApplyChartLayoutDialogResult(caption, chart, dialog.Result.ToOptions())");
        source.Should().Contain("UiText.Get(\"ChartAxisFormat_XAxisTitle\")");
        source.Should().Contain("UiText.Get(\"ChartAxisFormat_YAxisTitle\")");
        source.Should().Contain("ApplyChartLayoutDialogResult(\"Format Data Series\"");
    }

    [Fact]
    public void PictureCropRibbon_OffersCropAndResetCropMenuActions()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));

        xaml.Should().Contain("AutomationProperties.AutomationId=\"DrawCropPictureButton\"");
        xaml.ShouldContainLocalizedAttribute("AutomationProperties.HelpText", "Open crop controls for the selected or most recent inserted picture.");
        xaml.ShouldContainLocalizedAttribute("Header", "Crop...");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DrawCropPictureMenuItem\"");
        xaml.ShouldContainLocalizedAttribute("Header", "Reset Crop");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DrawResetPictureCropMenuItem\"");
        xaml.Should().Contain("Click=\"PictureCropDialogMenuItem_Click\"");
        xaml.Should().Contain("Click=\"PictureResetCropMenuItem_Click\"");
        source.Should().Contain("PictureResetCropMenuItem_Click");
        source.Should().Contain("new SetPictureCropCommand(");
        source.Should().Contain("0, 0, 0, 0");
    }

    [Fact]
    public void MainWindowCommandPartials_UseMessageServiceNotDirectMessageBox()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;

        // Verify the service wiring exists in the constructor.
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        mainSource.Should().Contain("IUserMessageService messageService");
        mainSource.Should().Contain("_messageService = messageService;");

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
            var partialSource = File.ReadAllText(partialPath);
            partialSource.Should()
                .NotContain("MessageBox.Show(", because: $"{partial} should delegate to _messageService");
        }
    }
}
