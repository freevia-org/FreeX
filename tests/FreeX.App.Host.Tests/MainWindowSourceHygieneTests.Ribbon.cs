using System.IO;
using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void RibbonSplitButtonHover_UsesRibbonButtonHoverBrushInsteadOfMenuHoverBrush()
    {
        var ribbonDropdownSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonDropdown.cs");
        var resources = DialogSourceTestSupport.ReadHostSources("Resources\\MainWindowResources.xaml");
        var theme = DialogSourceTestSupport.ReadHostSources("Resources\\ThemeResources.xaml");
        var hoverMethod = ExtractMethodSource(ribbonDropdownSource, "private static Brush GetRibbonDropdownHoverBrush(");

        resources.Should().Contain("FreeXRibbonButtonHoverBrush");
        theme.Should().Contain("FreeXRibbonButtonHoverBrush\" Color=\"#BEE6FD\"");
        hoverMethod.Should().Contain("FreeXRibbonButtonHoverBrush");
        hoverMethod.Should().NotContain("FreeXAccentSoftBrush");
        ribbonDropdownSource.Should().Contain("Color.FromRgb(0x3C, 0x7F, 0xB1)");
    }

    [Fact]
    public void RibbonSplitButtons_CanRouteDropdownZoneToADirectAction()
    {
        var ribbonDropdownSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonDropdown.cs");
        var metadataSource = DialogSourceTestSupport.ReadHostSources("RibbonMetadata.cs");

        metadataSource.Should().Contain("public static readonly RoutedEvent DropdownClickEvent");
        metadataSource.Should().Contain("public static void AddDropdownClickHandler(DependencyObject element, RoutedEventHandler handler)");
        ribbonDropdownSource.Should().Contain("var dropdownArgs = new RoutedEventArgs(RibbonMetadata.DropdownClickEvent, button);");
        ribbonDropdownSource.Should().Contain("button.RaiseEvent(dropdownArgs);");
        ribbonDropdownSource.Should().Contain("if (!dropdownArgs.Handled)");
        ribbonDropdownSource.Should().Contain("button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));");
    }

    [Fact]
    public void StandaloneAltKeyTips_DoNotRouteAltKeyChords()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var altKeyTipSource = DialogSourceTestSupport.ReadHostSources("MainWindow.AltKeyTips.cs");

        selectionSource.Should().NotContain("TryHandleTopLevelRibbonKeyTip(keyTip)");
        selectionSource.Should().NotContain("TryInvokeTopLevelQatKeyTip(qatKeyTip)");
        altKeyTipSource.Should().Contain("WM_SYSKEYDOWN");
        altKeyTipSource.Should().Contain("StandaloneAltKeyTipTracker.IsAltVirtualKey");
        altKeyTipSource.Should().Contain("_standaloneAltKeyTipTracker.CancelStandaloneAltCandidate();");
    }

    [Fact]
    public void ViewWindowAndZoomController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var viewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ViewCommands.cs");

        mainSource.Should().NotContain("private void ViewGridlinesChk_Changed(");
        mainSource.Should().NotContain("private void SetWorksheetViewMode(");
        mainSource.Should().NotContain("private void FreezeAtSelectionMenuItem_Click(");
        mainSource.Should().NotContain("private void ZoomInBtn_Click(");
        mainSource.Should().NotContain("private void FormulaBarExpandBtn_Click(");
        mainSource.Should().NotContain("private void RibbonScroll_PreviewMouseWheel(");

        viewSource.Should().Contain("private void ViewGridlinesChk_Changed(");
        viewSource.Should().Contain("private void SetWorksheetViewMode(");
        viewSource.Should().Contain("private void FreezeAtSelectionMenuItem_Click(");
        viewSource.Should().Contain("private void ZoomInBtn_Click(");
        viewSource.Should().Contain("private void FormulaBarExpandBtn_Click(");
        viewSource.Should().Contain("private void RibbonScroll_PreviewMouseWheel(");
    }

    [Fact]
    public void RibbonSurfaceController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var ribbonSource = DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            "",
            "MainWindow.Ribbon.cs",
            "MainWindow.RibbonAdaptive.cs");

        mainSource.Should().NotContain("private void UpdateRibbonCompactMode(");
        mainSource.Should().NotContain("private void NormalizeRibbonSurface(");
        mainSource.Should().NotContain("private void NormalizeExistingRibbonIconText(");
        mainSource.Should().NotContain("private void ApplyToolbarDropdownWhiteBackgrounds(");
        mainSource.Should().NotContain("private static FrameworkElement CreateRibbonCommandContent(");

        ribbonSource.Should().Contain("UpdateRibbonCompactMode(");
        ribbonSource.Should().Contain("private void NormalizeRibbonSurface(");
        ribbonSource.Should().Contain("private void NormalizeExistingRibbonIconText(");
        ribbonSource.Should().Contain("private void ApplyToolbarDropdownWhiteBackgrounds(");
        ribbonSource.Should().Contain("private static FrameworkElement CreateRibbonCommandContent(");
    }

    [Fact]
    public void QuickAnalysisController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var quickAnalysisSource = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");

        mainSource.Should().NotContain("private void ShowQuickAnalysisMenu(");
        mainSource.Should().NotContain("private void QuickAnalysisMenuItem_Click(");
        mainSource.Should().NotContain("private void QuickAnalysisMenuItem_MouseEnter(");
        mainSource.Should().NotContain("private void QuickAnalysisMenuItem_MouseLeave(");

        quickAnalysisSource.Should().Contain("private void ShowQuickAnalysisMenu(");
        quickAnalysisSource.Should().Contain("private void QuickAnalysisMenuItem_Click(");
        quickAnalysisSource.Should().Contain("private void QuickAnalysisMenuItem_MouseEnter(");
        quickAnalysisSource.Should().Contain("private void QuickAnalysisMenuItem_MouseLeave(");
    }

    [Fact]
    public void FormatPainterController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var formatPainterSource = DialogSourceTestSupport.ReadHostSources("MainWindow.FormatPainter.cs");

        mainSource.Should().NotContain("private void FormatPainterBtn_Click(");
        mainSource.Should().NotContain("private void FormatPainterBtn_PreviewMouseLeftButtonDown(");
        mainSource.Should().NotContain("private void CaptureFormatPainterSource(");
        mainSource.Should().NotContain("private void CancelFormatPainter(");
        mainSource.Should().NotContain("private bool TryApplyFormatPainter(");

        formatPainterSource.Should().Contain("private void FormatPainterBtn_Click(");
        formatPainterSource.Should().Contain("private void FormatPainterBtn_PreviewMouseLeftButtonDown(");
        formatPainterSource.Should().Contain("private void CaptureFormatPainterSource(");
        formatPainterSource.Should().Contain("private void CancelFormatPainter(");
        formatPainterSource.Should().Contain("private bool TryApplyFormatPainter(");
    }

    [Fact]
    public void DataCommandsController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");
        var scenarioSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ScenarioCommands.cs");

        mainSource.Should().NotContain("private void GetDataBtn_Click(");
        mainSource.Should().NotContain("private void TextToColumnsBtn_Click(");
        mainSource.Should().NotContain("private void AdvancedFilterBtn_Click(");
        mainSource.Should().NotContain("private void ScenariosBtn_Click(");
        mainSource.Should().NotContain("private void DataTableBtn_Click(");
        dataSource.Should().NotContain("private void ScenariosBtn_Click(");

        dataSource.Should().Contain("private async void GetDataBtn_Click(");
        dataSource.Should().Contain("private void TextToColumnsBtn_Click(");
        dataSource.Should().Contain("private void AdvancedFilterBtn_Click(");
        dataSource.Should().Contain("private void DataTableBtn_Click(");
        // ScenariosBtn_Click became async in commit ca58f0ab81 (R35 backlog fix wave) so it could
        // await scenario-manager work; the pinned literal was never updated to match.
        scenarioSource.Should().Contain("private async void ScenariosBtn_Click(");
    }

    [Fact]
    public void ReviewProtectionShareCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var reviewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        mainSource.Should().NotContain("private void SpellCheckBtn_Click(");
        mainSource.Should().NotContain("private void ReviewNewThreadedCommentBtn_Click(");
        mainSource.Should().NotContain("private void ProtectSheetBtn_Click(");
        mainSource.Should().NotContain("private async Task ShareWorkbookAsync(");
        mainSource.Should().NotContain("private void HelpOnlineBtn_Click(");

        reviewSource.Should().Contain("private void SpellCheckBtn_Click(");
        reviewSource.Should().Contain("private void ReviewNewThreadedCommentBtn_Click(");
        reviewSource.Should().Contain("private void ProtectSheetBtn_Click(");
        reviewSource.Should().Contain("private async Task ShareWorkbookAsync(");
        reviewSource.Should().Contain("private void HelpOnlineBtn_Click(");
    }

    [Fact]
    public void FormulaCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var formulaSource = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");

        mainSource.Should().NotContain("private void SelectFormulaAuditCells(");
        mainSource.Should().NotContain("private void InsertFunctionBtn_Click(");
        mainSource.Should().NotContain("private void TracePrecedentsBtn_Click(");
        mainSource.Should().NotContain("private void EvaluateFormulaBtn_Click(");
        mainSource.Should().NotContain("private void FormulaLogicalBtn_Click(");

        formulaSource.Should().Contain("private void SelectFormulaAuditCells(");
        formulaSource.Should().Contain("private void InsertFunctionBtn_Click(");
        formulaSource.Should().Contain("private void TracePrecedentsBtn_Click(");
        formulaSource.Should().Contain("private void EvaluateFormulaBtn_Click(");
        formulaSource.Should().Contain("private void FormulaLogicalBtn_Click(");
    }

    [Fact]
    public void ClipboardCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var clipboardSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ClipboardCommands.cs");

        mainSource.Should().NotContain("private record InternalClipboard(");
        mainSource.Should().NotContain("private void CutBtn_Click(");
        mainSource.Should().NotContain("private void PasteMenuItem_Click(");
        mainSource.Should().NotContain("private void ExecuteCopy(");
        mainSource.Should().NotContain("private void ExecutePaste(");
        mainSource.Should().NotContain("private void PasteSpecialBtn_Click(");
        mainSource.Should().NotContain("private void ExecutePasteLink(");

        clipboardSource.Should().Contain("private record InternalClipboard(");
        clipboardSource.Should().Contain("private void CutBtn_Click(");
        clipboardSource.Should().Contain("private void PasteMenuItem_Click(");
        clipboardSource.Should().Contain("private void ExecuteCopy(");
        clipboardSource.Should().Contain("private void ExecutePaste(");
        clipboardSource.Should().Contain("private void PasteSpecialBtn_Click(");
        clipboardSource.Should().Contain("private void ExecutePasteLink(");
    }

    [Fact]
    public void PasteSpecialExternalText_RoutesToLiteralTextPaste()
    {
        var clipboardSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ClipboardCommands.cs");

        clipboardSource.Should().Contain("case PasteSpecialAction.ExternalText:");
        clipboardSource.Should().Contain("externalTextAsText: true");
        clipboardSource.Should().Contain("preserveText: externalTextAsText");
    }

    [Fact]
    public void HomeFormattingCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var formattingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        mainSource.Should().NotContain("private void BoldButton_Click(");
        mainSource.Should().NotContain("private IWorkbookCommand CreateMergeAndCenterCommand(");
        mainSource.Should().NotContain("private void ApplyRangeBorderPreset(");
        mainSource.Should().NotContain("private void CfPickerBtn_Click(");
        mainSource.Should().NotContain("private void FormatTableBtn_Click(");
        mainSource.Should().NotContain("private void CellStylesBtn_Click(");

        formattingSource.Should().Contain("private void BoldButton_Click(");
        formattingSource.Should().Contain("private IWorkbookCommand CreateMergeAndCenterCommand(");
        formattingSource.Should().Contain("private void ApplyRangeBorderPreset(");
        formattingSource.Should().Contain("private void CfPickerBtn_Click(");
        formattingSource.Should().Contain("private void FormatTableBtn_Click(");
        formattingSource.Should().Contain("private void CellStylesBtn_Click(");
    }

    [Fact]
    public void HomeCellsCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var cellsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");

        mainSource.Should().NotContain("private void InsertPickerBtn_Click(");
        mainSource.Should().NotContain("private void InsertCellsMenuItem_Click(");
        mainSource.Should().NotContain("private void InsertRowBtn_Click(");
        mainSource.Should().NotContain("private void DeleteSelectedRows(");
        mainSource.Should().NotContain("private void ExecuteKeyboardInsert(");
        mainSource.Should().NotContain("private bool ExecuteKeyboardDeleteCellsWithPrompt(");
        mainSource.Should().NotContain("private void ExecuteRowsHidden(");
        mainSource.Should().NotContain("private void OpenFormatCellsDialog(");
        mainSource.Should().NotContain("private void OnAutofillRequested(");
        mainSource.Should().NotContain("private void FormatAutoRowMenuItem_Click(");
        mainSource.Should().NotContain("private IWorkbookCommand CreateAutoFitRowHeightCommand(");
        mainSource.Should().NotContain("private void FormatLockCellMenuItem_Click(");

        cellsSource.Should().Contain("private void InsertPickerBtn_Click(");
        cellsSource.Should().Contain("private void InsertCellsMenuItem_Click(");
        cellsSource.Should().Contain("private void InsertRowBtn_Click(");
        cellsSource.Should().Contain("private void DeleteSelectedRows(");
        cellsSource.Should().Contain("private void ExecuteKeyboardInsert(");
        cellsSource.Should().Contain("private bool ExecuteKeyboardDeleteCellsWithPrompt(");
        cellsSource.Should().Contain("private void ExecuteRowsHidden(");
        cellsSource.Should().Contain("private void OpenFormatCellsDialog(");
        cellsSource.Should().Contain("private void OnAutofillRequested(");
        cellsSource.Should().Contain("private void FormatAutoRowMenuItem_Click(");
        cellsSource.Should().Contain("private IWorkbookCommand CreateAutoFitRowHeightCommand(");
        cellsSource.Should().Contain("private void FormatLockCellMenuItem_Click(");
    }

    [Fact]
    public void HomeEditingCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var editingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeEditing.cs");

        mainSource.Should().NotContain("private void AutoSumPickerBtn_Click(");
        mainSource.Should().NotContain("private void ExecuteFillCells(");
        mainSource.Should().NotContain("private void TryFlashFill(");
        mainSource.Should().NotContain("private void FindSelectPickerBtn_Click(");
        mainSource.Should().NotContain("private void SelectGoToSpecialMatches(");
        mainSource.Should().NotContain("private void ClearAllMenuItem_Click(");

        editingSource.Should().Contain("private void AutoSumPickerBtn_Click(");
        editingSource.Should().Contain("private void ExecuteFillCells(");
        editingSource.Should().Contain("private void TryFlashFill(");
        editingSource.Should().Contain("private void FindSelectPickerBtn_Click(");
        editingSource.Should().Contain("private void SelectGoToSpecialMatches(");
        editingSource.Should().Contain("private void ClearAllMenuItem_Click(");
    }

    [Fact]
    public void OutlineGroupingCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var outlineSource = DialogSourceTestSupport.ReadHostSources("MainWindow.OutlineCommands.cs");

        mainSource.Should().NotContain("private void GroupRowsBtn_Click(");
        mainSource.Should().NotContain("private void UngroupRowsBtn_Click(");
        mainSource.Should().NotContain("private void CollapseGroupBtn_Click(");
        mainSource.Should().NotContain("private void ExpandGroupBtn_Click(");
        mainSource.Should().NotContain("private IWorkbookCommand CreateGroupCommand(");

        outlineSource.Should().Contain("private void GroupRowsBtn_Click(");
        outlineSource.Should().Contain("private void UngroupRowsBtn_Click(");
        outlineSource.Should().Contain("private void CollapseGroupBtn_Click(");
        outlineSource.Should().Contain("private void ExpandGroupBtn_Click(");
        outlineSource.Should().Contain("private IWorkbookCommand CreateGroupCommand(");
        outlineSource.Should().Contain("OutlineGroupingPlanner.GetNextOutlineLevel");
        outlineSource.Should().Contain("preserveExistingHierarchy: true");
    }

    [Fact]
    public void ChartCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var chartSource = ReadChartCommandSource();

        mainSource.Should().NotContain("private void InsertChartButton_Click(");
        mainSource.Should().NotContain("private void InsertChartPickerBtn_Click(");
        mainSource.Should().NotContain("private void ChangeChartTypeBtn_Click(");
        mainSource.Should().NotContain("private void ChartDataLabelsBtn_Click(");
        mainSource.Should().NotContain("private void ChartTrendlineBtn_Click(");
        mainSource.Should().NotContain("private void ChartSecondaryAxisSeriesBtn_Click(");
        mainSource.Should().NotContain("private void ChartSeriesMarkerSizeBtn_Click(");
        mainSource.Should().NotContain("private void InsertChartOfType(");

        chartSource.Should().Contain("private void InsertChartButton_Click(");
        chartSource.Should().Contain("private void InsertChartPickerBtn_Click(");
        chartSource.Should().Contain("private void ChangeChartTypeBtn_Click(");
        chartSource.Should().Contain("private void ChartDataLabelsBtn_Click(");
        chartSource.Should().Contain("private void ChartTrendlineBtn_Click(");
        chartSource.Should().Contain("private void ChartSecondaryAxisSeriesBtn_Click(");
        chartSource.Should().Contain("private void ChartSeriesMarkerSizeBtn_Click(");
        chartSource.Should().Contain("private void InsertChartOfType(");
        chartSource.Should().Contain("ChartOptionCycler");
    }

    [Fact]
    public void PivotCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var pivotSource = ReadPivotCommandSource();

        mainSource.Should().NotContain("private void PivotTableBtn_Click(");
        mainSource.Should().NotContain("private void RefreshPivotTableBtn_Click(");
        mainSource.Should().NotContain("private void PivotChartBtn_Click(");
        mainSource.Should().NotContain("private void PivotInsertSlicerBtn_Click(");
        mainSource.Should().NotContain("private void PivotFieldListBtn_Click(");
        mainSource.Should().NotContain("private void MovePivotFieldToZone(");
        mainSource.Should().NotContain("private void ApplyPivotFieldListLayout(");
        mainSource.Should().NotContain("private enum PivotFieldDropZone");

        pivotSource.Should().Contain("private void PivotTableBtn_Click(");
        pivotSource.Should().Contain("private void RefreshPivotTableBtn_Click(");
        pivotSource.Should().Contain("private void PivotChartBtn_Click(");
        pivotSource.Should().Contain("private void PivotInsertSlicerBtn_Click(");
        pivotSource.Should().Contain("private void PivotFieldListBtn_Click(");
        pivotSource.Should().Contain("private void MovePivotFieldToZone(");
        pivotSource.Should().Contain("private void ApplyPivotFieldListLayout(");
        pivotSource.Should().Contain("private enum PivotFieldDropZone");
        pivotSource.Should().Contain("PivotUiPlanner");
        pivotSource.Should().Contain("SlicerTimelinePlanner");
    }

    [Fact]
    public void PivotContextualTabs_UseStrictPivotSelectionInsteadOfWorkbookFallback()
    {
        var pivotSource = ReadPivotCommandSource();
        var refreshFieldListStart = pivotSource.IndexOf("private void RefreshPivotFieldListPane()", StringComparison.Ordinal);
        var setTabsStart = pivotSource.IndexOf("private void SetPivotContextualTabsVisible", StringComparison.Ordinal);
        var refreshFieldListSource = pivotSource[refreshFieldListStart..setTabsStart];

        pivotSource.Should().Contain("SetPivotContextualTabsVisible(false);");
        refreshFieldListSource.Should().Contain(
            "PivotUiPlanner.CreateFieldListPanePlan(sheet, SheetGrid.SelectedRange)",
            "Excel only shows PivotTable contextual tabs and the field list when the active selected cell is inside a PivotTable");
        refreshFieldListSource.Should().NotContain(
            "FindPivotTableForSelection(sheet, SheetGrid.SelectedRange)",
            "the workbook fallback would show contextual PivotTable tabs after selection leaves the PivotTable");
    }

    [Fact]
    public void RibbonKeyTipController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var keyTipSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyTips.cs");

        mainSource.Should().NotContain("private void EnterRibbonKeyTipMode(");
        mainSource.Should().NotContain("private void HandleActiveRibbonKeyTip(");
        mainSource.Should().NotContain("private void ShowKeyTipOverlay(");
        mainSource.Should().NotContain("private bool TryInvokeVisibleCommandKeyTip(");
        mainSource.Should().NotContain("private void EnterRibbonMenuKeyTipScope(");
        mainSource.Should().NotContain("private bool TryInvokeTopLevelQatKeyTip(");
        mainSource.Should().NotContain("private IEnumerable<FrameworkElement> GetVisibleKeyTipElements(");
        mainSource.Should().NotContain("private enum RibbonKeyTipScope");

        keyTipSource.Should().Contain("private void EnterRibbonKeyTipMode(");
        keyTipSource.Should().Contain("private void HandleActiveRibbonKeyTip(");
        keyTipSource.Should().Contain("private void ShowKeyTipOverlay(");
        keyTipSource.Should().Contain("private bool TryInvokeVisibleCommandKeyTip(");
        keyTipSource.Should().Contain("private void EnterRibbonMenuKeyTipScope(");
        keyTipSource.Should().Contain("private bool TryInvokeTopLevelQatKeyTip(");
        keyTipSource.Should().Contain("private IEnumerable<FrameworkElement> GetVisibleKeyTipElements(");
        keyTipSource.Should().Contain("private enum RibbonKeyTipScope");
        keyTipSource.Should().Contain("RibbonKeyTipRouting");
    }

    [Fact]
    public void CommandExecutionController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.CommandExecution.cs");

        mainSource.Should().NotContain("void ShowCommandError(");
        mainSource.Should().NotContain("private bool TryExecuteCommand(");
        mainSource.Should().NotContain("private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds(");
        mainSource.Should().NotContain("private bool TryExecuteEditCells(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableGroupedSheetCommand(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableCurrentSelectionRangesCommand(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableCurrentRangeCommand(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableChartLayout(");
        mainSource.Should().NotContain("private bool ExecuteUndo(");
        mainSource.Should().NotContain("private void ExecuteRepeatLast(");
        mainSource.Should().NotContain("private IWorkbookCommand CreateSingleCellEditCommand(");

        commandSource.Should().Contain("private void ShowCommandError(");
        commandSource.Should().Contain("private bool TryExecuteCommand(");
        commandSource.Should().Contain("private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds(");
        commandSource.Should().Contain("private bool TryExecuteEditCells(");
        commandSource.Should().Contain("private bool TryExecuteRepeatableGroupedSheetCommand(");
        commandSource.Should().Contain("private bool TryExecuteRepeatableCurrentSelectionRangesCommand(");
        commandSource.Should().Contain("private bool TryExecuteRepeatableCurrentRangeCommand(");
        commandSource.Should().Contain("private bool TryExecuteRepeatableChartLayout(");
        commandSource.Should().Contain("private bool ExecuteUndo(");
        commandSource.Should().Contain("private void ExecuteRepeatLast(");
        commandSource.Should().Contain("private IWorkbookCommand CreateSingleCellEditCommand(");
        commandSource.Should().Contain("ExecuteRepeatable");
    }

    [Fact]
    public void DataFilterCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var dataFilterSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        mainSource.Should().NotContain("private void SortAscButton_Click(");
        mainSource.Should().NotContain("private void SortCustomButton_Click(");
        mainSource.Should().NotContain("private void FilterButton_Click(");
        mainSource.Should().NotContain("private bool ApplyAutoFilterDialogResult(");
        mainSource.Should().NotContain("private void CfRuleButton_Click(");
        mainSource.Should().NotContain("private void ValidationButton_Click(");
        mainSource.Should().NotContain("private void ClearFilterButton_Click(");
        mainSource.Should().NotContain("private void NamedRangesButton_Click(");

        dataFilterSource.Should().Contain("private void SortAscButton_Click(");
        dataFilterSource.Should().Contain("private void SortCustomButton_Click(");
        dataFilterSource.Should().Contain("private void FilterButton_Click(");
        dataFilterSource.Should().Contain("private bool ApplyAutoFilterDialogResult(");
        dataFilterSource.Should().Contain("private void CfRuleButton_Click(");
        dataFilterSource.Should().Contain("private void ValidationButton_Click(");
        dataFilterSource.Should().Contain("private void ClearFilterButton_Click(");
        dataFilterSource.Should().Contain("private void NamedRangesButton_Click(");
        dataFilterSource.Should().Contain("FilterPromptPlanner.TryPlan");
    }

    [Fact]
    public void InsertCommands_LiveOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");

        mainSource.Should().NotContain("private void InsertCurrentDateOrTime(");
        mainSource.Should().NotContain("private void TableBtn_Click(");
        mainSource.Should().NotContain("private void InsertSparkline(");
        mainSource.Should().NotContain("private void InsertLinkBtn_Click(");
        mainSource.Should().NotContain("private void HeaderFooterBtn_Click(");
        mainSource.Should().NotContain("private void SymbolPickerBtn_Click(");

        insertSource.Should().Contain("private void InsertCurrentDateOrTime(");
        insertSource.Should().Contain("private void TableBtn_Click(");
        insertSource.Should().Contain("private void InsertSparkline(");
        insertSource.Should().Contain("private void InsertLinkBtn_Click(");
        insertSource.Should().Contain("private void HeaderFooterBtn_Click(");
        insertSource.Should().Contain("private void SymbolPickerBtn_Click(");
        insertSource.Should().Contain("SparklinePlanner");
        insertSource.Should().NotContain("SparklineInputParser");
    }

    [Fact]
    public void ShellChromeController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var shellSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Shell.cs");

        mainSource.Should().NotContain("private void UpdateMaximizedContentInset(");
        mainSource.Should().NotContain("private static Thickness GetMaximizedSafeInset(");
        mainSource.Should().NotContain("private void UndoQatBtn_Click(");
        mainSource.Should().NotContain("private void RedoQatBtn_Click(");

        shellSource.Should().Contain("private void UpdateMaximizedContentInset(");
        shellSource.Should().Contain("private static Thickness GetMaximizedSafeInset(");
        shellSource.Should().NotContain("private void UndoQatBtn_Click(");
        shellSource.Should().NotContain("private void RedoQatBtn_Click(");
    }

    [Fact]
    public void QuickAccessUndoRedoButtons_ReflectCommandStackState()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");
        var qatSource = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAccessToolbar.cs");
        var qatStateSource = DialogSourceTestSupport.ReadAppServicesRibbonSource("QuickAccessCommandState.cs");
        var toolbarSource = DialogSourceTestSupport.ReadHostSources("ToolbarVisualState.cs");
        var cacheSource = DialogSourceTestSupport.ReadHostSources("ToolbarVisualStateCache.cs");

        source.Should().Contain("RefreshQuickAccessToolbarCommandStates();");
        source.Should().Contain("RefreshQuickAccessToolbarCommandStatesAfterSelectionChange();");
        qatSource.Should().Contain("private void RefreshQuickAccessToolbarCommandStates(bool force = false)");
        qatSource.Should().Contain("private void RefreshQuickAccessToolbarCommandStatesAfterSelectionChange()");
        qatSource.Should().Contain("private QuickAccessCommandState CreateQuickAccessCommandState()");
        qatSource.Should().Contain("_session.CanUndo");
        qatSource.Should().Contain("_session.CanRedo");
        qatSource.Should().Contain("HasActiveWorksheetForQuickAccessCommandState()");
        qatSource.Should().Contain("HasSelectionForQuickAccessCommandState()");
        qatSource.Should().Contain("state.WithSelectionContext(");
        qatSource.Should().Contain("_lastQuickAccessCommandStateWorkbookId == _workbook.Id");
        qatSource.Should().Contain("QuickAccessCommandStateResolver.CanExecute(target.Availability, state)");
        qatSource.Should().Contain("GetQuickAccessHistoryButtonName(command.Id)");
        qatSource.Should().Contain("\"UndoQatHistoryBtn\"");
        qatSource.Should().Contain("\"RedoQatHistoryBtn\"");
        qatSource.Should().Contain("_session.GetUndoHistory(QuickAccessHistoryMaxCount)");
        qatSource.Should().Contain("_session.GetRedoHistory(QuickAccessHistoryMaxCount)");
        qatSource.Should().Contain("private void ExecuteQuickAccessHistory(string commandId, int actionCount)");
        qatSource.Should().Contain("QuickAccessToolbarCommandIds.Undo => ExecuteUndo()");
        qatSource.Should().Contain("QuickAccessToolbarCommandIds.Redo => ExecuteRedo()");
        qatStateSource.Should().Contain("WithSelectionContext(bool hasActiveWorksheet, bool hasSelection)");
        qatStateSource.Should().Contain("QuickAccessToolbarCommandIds.Undo => QuickAccessCommandAvailability.Undo");
        qatStateSource.Should().Contain("QuickAccessToolbarCommandIds.Redo => QuickAccessCommandAvailability.Redo");
        toolbarSource.Should().NotContain("bool CanUndo");
        toolbarSource.Should().NotContain("bool CanRedo");
        cacheSource.Should().Contain("private readonly record struct Source(WorkbookId WorkbookId, StyleId StyleId);");
    }

    [Fact]
    public void RefreshToolbar_AvoidsRepeatedDependencyPropertyWrites()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");
        var refreshToolbar = ExtractMethodSource(source, "private void RefreshToolbarVisualState()");

        // Toolbar visual state now flows through the platform-neutral RibbonStateStore, which dedups
        // no-op writes internally (so it never churns the renderer-bound controls). The Font combos are
        // driven entirely through the rendered declarative combo (no hidden backplane combo), so
        // SetRibbonComboValue only writes the store value and no longer mirrors onto a stub.
        source.Should().Contain("private void SetRibbonComboValue(");
        source.Should().NotContain("private static void SetSelectedItemIfChanged(");
        source.Should().Contain("private void RefreshToolbarAfterSelectionChange()");
        refreshToolbar.Should().Contain("_ribbonState.SetChecked(\"Bold\", state.Bold)");
        refreshToolbar.Should().Contain("SetRibbonComboValue(\"Font\", state.FontName)");
        refreshToolbar.Should().NotContain("BoldButton.IsChecked = state.Bold");
        refreshToolbar.Should().NotContain("FontNameBox.SelectedItem = state.FontName");
        refreshToolbar.Should().NotContain(", FontNameBox)");
    }

    [Fact]
    public void SetActiveCellCallers_AvoidDuplicateToolbarAndStatusRefresh()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var multiWindowSource = DialogSourceTestSupport.ReadHostSources("MainWindow.MultiWindow.cs");
        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");
        var scenarioSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ScenarioCommands.cs");

        var createNewWorkbook = ExtractMethodSource(backstageSource, "private void CreateNewWorkbook()");
        createNewWorkbook.Should().Contain("InitializeNewWorkbook(workbookName: null);");
        createNewWorkbook.Should().NotContain("RefreshToolbar();");

        var adoptWorkbookAsInitial = ExtractMethodSource(backstageSource, "private void AdoptWorkbookAsInitial(");
        adoptWorkbookAsInitial.Should().Contain("SetActiveCell(new CellAddress(_currentSheetId, 1, 1));");
        adoptWorkbookAsInitial.Should().NotContain("RefreshToolbar();");
        adoptWorkbookAsInitial.Should().NotContain("RefreshStatusBar();");

        // R120-app-newwindow-copies-selection: this used to pin
        // SetActiveCell(new CellAddress(_currentSheetId, 1, 1)) -- i.e. it asserted the DEFECT that
        // View > New Window always opened the sibling at A1 instead of copying the invoking
        // window's active cell/selection the way Excel does. The selection seeding now goes
        // through ApplyAdoptedWorksheetSelection(). This test's actual subject is unchanged: the
        // adopt path must not duplicate the toolbar/status refreshes that SetActiveCell and the
        // subsequent UpdateViewport already perform.
        var adoptSharedWorkbook = ExtractMethodSource(multiWindowSource, "private void AdoptSharedWorkbook()");
        adoptSharedWorkbook.Should().Contain("ApplyAdoptedWorksheetSelection();");
        adoptSharedWorkbook.Should().NotContain("RefreshToolbar();");
        adoptSharedWorkbook.Should().NotContain("RefreshStatusBar();");

        // R68-async-ordering-race-sweep-2 split the actual import work (and its RefreshStatusBar
        // call) out of GetDataBtn_Click into ImportDataFromFileAsync so the ordering-race guard is
        // directly testable without a real WPF OpenFileDialog; GetDataBtn_Click now just awaits it.
        ExtractMethodSource(dataSource, "private async void GetDataBtn_Click(")
            .Should()
            .Contain("await ImportDataFromFileAsync(result.FileName!, adapter, ext, format);");
        ExtractMethodSource(dataSource, "private async Task ImportDataFromFileAsync(")
            .Should()
            .Contain("RefreshStatusBar();");
        ExtractMethodSource(dataSource, "private void ForecastSheetBtn_Click(")
            .Should()
            .Contain("if (!refreshedSelectionUi)");
        ExtractMethodSource(scenarioSource, "private void ShowScenarioByName(")
            .Should()
            .Contain("if (!refreshedSelectionUi)");
        ExtractMethodSource(scenarioSource, "private void CreateScenarioSummaryReport(")
            .Should()
            .Contain("if (!refreshedSelectionUi)");
    }

    [Fact]
    public void TitleBar_UsesSharedFormatterForDirtyGroupedAndSavedFileState()
    {
        var editingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var lifecycleSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookLifecycle.cs");

        // Multi-window slice 1 adds an Excel-style per-window number suffix to the shared formatter.
        editingSource.Should().Contain("WorkbookTitleFormatter.Format(");
        editingSource.Should().Contain("_workbook.Name, _workbookDirty, IsWorkbookGrouped(), _windowTitleSuffix)");
        // Dirty/save-point mutations belong to the shared WorkbookSession used by both hosts.
        lifecycleSource.Should().Contain("_session.MarkDirtyFromHost();");
        lifecycleSource.Should().Contain("_session.MarkSavedFromHost();");
        lifecycleSource.Should().NotContain("_documentState");
        lifecycleSource.Should().Contain("UpdateTitleBar();");
        backstageSource.Should().Contain("_workbook.Name = fileContext.DisplayName;");
        backstageSource.Should().Contain("MarkWorkbookSaved();");
    }

    [Fact]
    public void KeyboardShortcuts_RegisterExcelNameManagerCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NameManager, NamedRangesButton_Click);");
        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CreateNamesFromSelection, CreateNamesFromSelectionBtn_Click);");
    }

    [Fact]
    public void FormulaBarTextChanged_SkipsFormulaHighlightWorkForSelectionDisplayUpdates()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

        source.Should().Contain("var formulaBarHasFocus = ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, FormulaBar);");
        source.Should().Contain("if (!formulaBarHasFocus && _inlineEditor?.IsVisible != true)");
        source.Should().Contain("ClearFormulaReferenceHighlights();");
    }

    [Fact]
    public void WorkbookUiStateController_LivesOutsideMainWindowCodeBehind()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var uiStateSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");

        mainSource.Should().NotContain("private void ApplyOptionsToView(");
        mainSource.Should().NotContain("private void RecalculateWorkbook(");
        mainSource.Should().NotContain("private string FormatCellReference(");
        mainSource.Should().NotContain("private void RefreshToolbar(");
        mainSource.Should().NotContain("private void ApplyStyleDiff(");
        mainSource.Should().NotContain("private void NavigateToCell(");
        mainSource.Should().NotContain("private void RefreshSheetProtectionUi(");

        uiStateSource.Should().Contain("private void ApplyOptionsToView(");
        uiStateSource.Should().Contain("private void RecalculateWorkbook(");
        uiStateSource.Should().Contain("private string FormatCellReference(");
        uiStateSource.Should().Contain("private void RefreshToolbar(");
        uiStateSource.Should().Contain("private void ApplyStyleDiff(");
        uiStateSource.Should().Contain("private void NavigateToCell(");
        uiStateSource.Should().Contain("private void RefreshSheetProtectionUi(");
        uiStateSource.Should().Contain("SpreadsheetDisplayFormatter");
    }

    [Fact]
    public void MainWindow_MergesVisualRefreshResourceDictionaries()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var resourcesXaml = DialogSourceTestSupport.ReadHostSources("Resources\\MainWindowResources.xaml");

        DialogSourceTestSupport.ReadHostSources("Resources\\ThemeResources.xaml").Should().NotBeNull();
        DialogSourceTestSupport.ReadHostSources("Resources\\IconResources.xaml").Should().NotBeNull();
        xaml.Should().Contain("Source=\"Resources/MainWindowResources.xaml\"");
        resourcesXaml.Should().Contain("Source=\"ThemeResources.xaml\"");
        resourcesXaml.Should().Contain("Source=\"IconResources.xaml\"");
    }

    [Fact]
    public void RibbonIconSet_UsesSharedIconSlotsAndDecorator()
    {
        var iconResources = DialogSourceTestSupport.ReadHostSources("Resources\\IconResources.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Ribbon.cs");
        // RibbonCommandIconKind/Accent now live in Free.Shared.Ribbon (Model/RibbonCommandIcon.cs);
        // the shared presentation planner still references them, which is what this test asserts.
        var planner =
            DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonCommandPresentationPlanner.cs") +
            DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonCommandPresentationPlanner.Icons.cs");

        DialogSourceTestSupport.ReadHostSources("RibbonIconFactory.cs").Should().NotBeNull();
        iconResources.Should().Contain("FreeXRibbonLargeIconSlot");
        iconResources.Should().Contain("FreeXRibbonSmallIconSlot");
        iconResources.Should().Contain("FreeXRibbonLargeLabel");
        iconResources.Should().Contain("FreeXRibbonSmallLabel");

        source.Should().Contain("CreateRibbonCommandContent(commandName, label, layoutKind)");
        source.Should().Contain("NormalizeExistingRibbonIconText(surface);");
        source.Should().Contain("GetRibbonIconAccentBrushes");
        source.Should().Contain("RibbonIconFactory.CreateCommandIcon(commandName, icon, iconSize, glyphBrush)");
        source.Should().Contain("ReplaceRibbonGlyphIcons(button.Content, button, tall)");
        source.Should().NotContain("icon.Glyph");
        source.Should().Contain("RibbonCommandIconAccent.Chart");
        source.Should().Contain("HorizontalAlignment.Left");

        planner.Should().Contain("RibbonCommandIconKind.ChartColumn");
        planner.Should().NotContain("FontFamily");
        planner.Should().NotContain("Glyph");
        planner.Should().Contain("RibbonCommandIconAccent.Chart");
        planner.Should().Contain("RibbonCommandIconAccent.Data");
        planner.Should().Contain("RibbonCommandIconAccent.Warning");
        planner.Should().Contain("RibbonCommandIconAccent.Help");
    }

    [Fact]
    public void HomeNumberFormatDropdown_ExposesExcelFormatFamiliesFromOneCatalog()
    {
        // After the ribbon XAML→declarative cutover the Number Format combo is populated from the one
        // catalog on the *rendered* declarative ribbon (MainWindow.RibbonDeclarative.cs) rather than a
        // startup stub, so the label projection now lives there.
        var source = DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            "",
            "MainWindow.RibbonDeclarative.cs",
            "MainWindow.HomeFormatting.cs")
            + DialogSourceTestSupport.ReadAppServicesSource("HomeNumberFormatDropdownPlanner.cs")
            + DialogSourceTestSupport.ReadAppServicesSource("FormatCellsNumberFormatPlanner.cs");

        source.Should().Contain("HomeNumberFormatDropdownPlanner.Options.Select(option => option.Label)");
        source.Should().Contain("HomeNumberFormatDropdownPlanner.Options[selectedIndex]");
        source.Should().Contain("Accounting ($#,##0.00)");
        source.Should().Contain("Fraction (# ?/?)");
        source.Should().Contain("Scientific (0.00E+00)");
        source.Should().Contain("\"# ?/?\"");
        source.Should().Contain("\"0.00E+00\"");
    }

    [Fact]
    public void ArrangeAllMenu_ReflectsStoredWorkbookArrangementAndAppliesLiveLayout()
    {
        // After the ribbon XAML→declarative cutover the Arrange All split-button menu is declared in the
        // single-source ribbon (FreeXRibbonDefinition.cs). The host attaches the same Opened handler the
        // original XAML used onto the rendered menu (MainWindow.RibbonDeclarative.cs), and that handler
        // still drives the per-item checkmarks from the stored workbook arrangement.
        var ribbon = DialogSourceTestSupport.ReadRibbonDefinitionSource("FreeXRibbonDefinition.cs");
        var declarativeSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonDeclarative.cs");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ViewCommands.cs");

        ribbon.Should().Contain(".Medium(\"Arrange All\", \"Arrange All\"");
        ribbon.Should().Contain(".Item(\"Cascade\", \"Cascade\"");
        declarativeSource.Should().Contain("arrangeMenu.Opened += ArrangeAllContextMenu_Opened;");
        source.Should().Contain("ArrangeAllContextMenu_Opened");
        source.Should().Contain("item.IsChecked = ArrangeAllMenuPlanner.IsChecked(item.Tag, _workbook.WindowArrangement)");
        source.Should().Contain("ArrangeAllMenuPlanner.TryParseArrangement");
        source.Should().Contain("_windowRegistry?.ArrangeVisibleWindows(arrangement, workArea.Width, workArea.Height)");
    }

    [Fact]
    public void SplitRibbonCommand_ReflectsActiveSplitState()
    {
        // The ribbon is now declared in the single-source FreeXRibbonDefinition (FreeX.Ribbon.Definitions
        // project) rather than MainWindow.xaml. Split is an IconToggle whose control name "Split" maps to
        // the SplitViewBtn backplane control, and its checked state is driven through the neutral ribbon
        // state in MainWindow.Viewport.cs.
        var ribbon = DialogSourceTestSupport.ReadRibbonDefinitionSource("FreeXRibbonDefinition.cs");
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");

        ribbon.Should().Contain(".IconToggle(\"Split\", \"Split\"");
        // Round 89 (26c4e92783, "R89-freeze-split-per-window-1") made Split state per-window rather
        // than reading the shared Sheet directly, so a "New Window" sibling's split doesn't leak
        // into this window's ribbon toggle; the check now reads the per-window viewState record.
        source.Should().Contain("_ribbonState.SetChecked(\"Split\", viewState.SplitRow is not null || viewState.SplitColumn is not null)");
    }

    [Fact]
    public void QuickAccessToolbar_UsesVectorIcons()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAccessToolbar.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var iconResources = DialogSourceTestSupport.ReadHostSources("Resources\\IconResources.xaml");

        // The QAT button glyph is a vector RibbonIcon driven by the catalog's IconKind, built through the
        // shared renderer's icon factory (FreeX's own RibbonIcon, so the icons match the rest of the app)
        // from a neutral descriptor that carries command.IconKind. No text/font-glyph content.
        source.Should().Contain("new RibbonIcon");
        source.Should().Contain("Kind = kind");
        source.Should().Contain("command.IconKind");
        source.Should().NotContain("Content = \"");
        source.Should().NotContain("FreeXQatOnAccentIcon");
        iconResources.Should().NotContain("FreeXQatIcon");
        xaml.Should().NotContain("Content=\"💾\"");
        xaml.Should().NotContain("Content=\"↩\"");
        xaml.Should().NotContain("Content=\"↪\"");
    }

    [Fact]
    public void TitleBarIcons_UseExplicitWhiteForeground()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var titleBarStart = xaml.IndexOf("<!-- Title / quick-access bar", StringComparison.Ordinal);
        var titleBarEnd = xaml.IndexOf("<!-- Workbook name centred -->", StringComparison.Ordinal);

        titleBarStart.Should().BeGreaterThanOrEqualTo(0);
        titleBarEnd.Should().BeGreaterThan(titleBarStart);

        var titleBarCommands = xaml[titleBarStart..titleBarEnd];
        foreach (var kind in new[] { "WindowClose", "WindowMaximize", "WindowMinimize" })
        {
            titleBarCommands.Should().Contain($"Kind=\"{kind}\"");
        }

        titleBarCommands.Should().NotContain("Foreground=\"{Binding Foreground");
        // WS-G round 4: title-bar foregrounds converted to DynamicResource for full-chrome reskin.
        titleBarCommands.Split("Foreground=\"{DynamicResource FreeXWhiteBrush}\"").Length.Should().BeGreaterThanOrEqualTo(4);
        var qatSource = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAccessToolbar.cs");
        qatSource.Should().Contain("? \"FreeXTextBrush\"");
        qatSource.Should().Contain(": \"FreeXWhiteBrush\"");
    }

    [Fact]
    public void ToolbarIcons_DoNotUseFontGlyphAssets()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var iconResources = DialogSourceTestSupport.ReadHostSources("Resources\\IconResources.xaml");

        xaml.Should().NotContain("Segoe MDL2 Assets");
        xaml.Should().NotContain("RibbonIconGlyph");
        xaml.Should().NotContain("FreeXQatOnAccentIcon");
        iconResources.Should().NotContain("Segoe MDL2 Assets");
        iconResources.Should().NotContain("FreeXRibbonGlyph");
    }

    [Fact]
    public void MainWindow_UsesVisibleFreeXBrandingAndWindowIcon()
    {
        // The physical icon asset was deduplicated into the shared shell tier in commit 894b77247a
        // ("Deduplicate shared UI resources and localization"); FreeX.App.Host.csproj now links to
        // it (see the `Resource`/`Content`/`ApplicationIcon` entries below) rather than keeping its
        // own copy under src/FreeX.App.Host/Resources.
        var iconPath = WorkspaceFileLocator.Find("shared", "Free.Shared.Shell", "Resources", "FreeX.ico");
        var theme = DialogSourceTestSupport.ReadHostSources("Resources\\ThemeResources.xaml");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var project = DialogSourceTestSupport.ReadHostSources("FreeX.App.Host.csproj");

        File.Exists(iconPath).Should().BeTrue();
        xaml.Should().Contain("Icon=\"Resources/FreeX.ico\"");
        xaml.Should().Contain("x:Name=\"TitleBarAppIcon\"");
        xaml.Should().Contain("x:Name=\"TitleBarAppFreeBand\"");
        xaml.Should().Contain("x:Name=\"TitleBarAppXOutlineTop\"");
        xaml.Should().Contain("x:Name=\"TitleBarAppXOutlineBottom\"");
        xaml.Should().Contain("x:Name=\"TitleBarAppXOutlineLeft\"");
        xaml.Should().Contain("x:Name=\"TitleBarAppXOutlineRight\"");
        xaml.Should().Contain("x:Name=\"TitleBarAppX\"");
        xaml.Should().Contain("<TextBlock Text=\"FREE\"");
        xaml.Should().Contain("<TextBlock Text=\"X\"");
        xaml.Should().Contain("<RowDefinition Height=\"8\"/>");
        xaml.Should().Contain("<RowDefinition Height=\"1\"/>");
        xaml.Should().Contain("<RowDefinition Height=\"*\"/>");
        xaml.Should().Contain("Margin=\"0\"");
        xaml.Should().Contain("Grid.RowSpan=\"3\"");
        xaml.Should().Contain("FontSize=\"6.6\"");
        xaml.Should().Contain("FontSize=\"14.5\"");
        xaml.Should().Contain("Foreground=\"#10253A\"");
        xaml.Should().Contain("Margin=\"0,-3,0,0\"");
        xaml.Should().Contain("Margin=\"0,-1,0,0\"");
        xaml.Should().Contain("Margin=\"-1,-2,0,0\"");
        xaml.Should().Contain("Margin=\"1,-2,0,0\"");
        xaml.Should().Contain("Margin=\"0,-2,0,0\"");
        xaml.Should().NotContain("<Image Source=\"Resources/FreeX.ico\"");
        xaml.Should().NotContain("<TextBlock Text=\"F\" Foreground=\"{StaticResource FreeXAccentBrush}\"");
        theme.Should().Contain("x:Key=\"FreeXTitleBarBrush\"");
        // WS-G round 2 made the title bar token-driven and runtime-swappable, so the brand
        // background now binds via DynamicResource rather than StaticResource.
        xaml.Should().Contain("Background=\"{DynamicResource FreeXTitleBarBrush}\"");
        project.Should().Contain("<ApplicationIcon>..\\..\\shared\\Free.Shared.Shell\\Resources\\FreeX.ico</ApplicationIcon>");
    }

    [Fact]
    public void PersistentFormatPainter_UsesPreviewMouseDownSoButtonDoubleClickCannotBeOverwrittenByClick()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormatPainter.cs");
        // After the ribbon XAML→declarative cutover the Format Painter command is declared in the
        // single-source ribbon (HomeRibbonDefinition.cs). The persistent (double-click) capture still
        // uses PreviewMouseLeftButtonDown with an explicit ClickCount == 2 guard so a Click handler can
        // never overwrite the double-click that toggles persistence, rather than a MouseDoubleClick.
        var ribbon = DialogSourceTestSupport.ReadRibbonDefinitionSource("HomeRibbonDefinition.cs");

        source.Should().Contain("private bool _formatPainterPersistent;");
        source.Should().Contain("private void FormatPainterBtn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("if (e.ClickCount != 2) return;");
        source.Should().Contain("CaptureFormatPainterSource(persistent: true);");
        source.Should().Contain("e.Handled = true;");
        source.Should().Contain("CancelFormatPainter");
        source.Should().NotContain("FormatPainterBtn_MouseDoubleClick");
        ribbon.Should().Contain(".Medium(\"Format Painter\", \"Format Painter\"");
    }

    [Fact]
    public void FormatPainterApplication_UsesTargetSelectionRangeWhenAvailable()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormatPainter.cs");
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        source.Should().Contain("private SheetId? _formatPainterSourceSheetId;");
        source.Should().Contain("private GridRange? _formatPainterSourceRange;");
        source.Should().Contain("private bool _formatPainterTargetSelectionActive;");
        source.Should().Contain("TryApplyFormatPainter(GridRange targetRange)");
        source.Should().Contain("_formatPainterSourceRange = range;");
        source.Should().Contain("var targetRanges = GetCurrentSelectionRanges(targetRange);");
        source.Should().Contain("SelectionStyleCommandPlanner.CreateRangeCommand(");
        source.Should().Contain("FormatPainterCommandFactory.Create(");
        selectionSource.Should().Contain("SheetGrid.SelectedRange is { } selectedRange");
        selectionSource.Should().Contain("selectedRange.Contains(newAddr)");
        selectionSource.Should().Contain("TryApplyFormatPainter(selectedRange)");
        source.Should().NotContain("var targetRange = new GridRange(addr, addr);");
    }

    [Fact]
    public void AutoFitMenuHandlers_UsePlannerAndPerTargetExplicitSizes()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");
        var planner = DialogSourceTestSupport.ReadAppServicesRibbonSource("RowColumnSizingPlanner.cs");

        source.Should().Contain("RowColumnSizingPlanner.PlanAutoFitRowHeights");
        source.Should().Contain("RowColumnSizingPlanner.PlanAutoFitColumnWidths");
        source.Should().Contain("RowColumnSizingPlanner.CreateAutoFitRowHeightCommand(sheetId, plans)");
        source.Should().Contain("RowColumnSizingPlanner.CreateAutoFitColumnWidthCommand(sheetId, plans)");
        planner.Should().Contain("if (plans.Count == 1)");
        planner.Should().Contain("return createCommand(plans[0]);");
        planner.Should().Contain("new SetRowHeightCommand(sheetId, plan.Index, plan.Index, plan.Size)");
        planner.Should().Contain("new SetColumnWidthCommand(sheetId, plan.Index, plan.Index, plan.Size)");
        source.Should().NotContain("return new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, height)");
        source.Should().NotContain("return new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, width)");
        planner.Should().Contain("AutoFitSizingService.EstimateRowHeight");
        planner.Should().Contain("AutoFitSizingService.EstimateColumnWidth");
        source.Should().NotContain("new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, height: null)");
        source.Should().NotContain("new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, width: null)");
    }

    [Fact]
    public void AdvancedChartFamilies_RouteRenderableFamiliesToAuthoringAndHideDeferredMap()
    {
        // After the ribbon XAML→declarative cutover the advanced chart-family entry points are no longer
        // hand-authored MainWindow.xaml buttons. The renderable families (Treemap/Sunburst/Histogram/…/3D)
        // still route through the same InsertChartOfType command handlers, and the Map family is still
        // hidden (deferred via ShowDeferredChartFamilyMessage). This asserts the renderable→authoring and
        // Map→deferred routing from the host source; the declarative chart-picker surface that exposes
        // these handlers is flagged separately (see flaggedDeviations) since the advanced families are not
        // yet declared in the single-source ribbon.
        var source = ReadChartCommandSource();

        source.Should().Contain("ShowDeferredChartFamilyMessage");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ChartFamilyDeferred\")");
        source.Should().Contain("ChartTreemapMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Treemap)");
        source.Should().Contain("ChartSunburstMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Sunburst)");
        source.Should().Contain("ChartHistogramMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Histogram)");
        source.Should().Contain("ChartParetoMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Pareto)");
        source.Should().Contain("ChartBoxAndWhiskerMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.BoxAndWhisker)");
        source.Should().Contain("ChartWaterfallMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Waterfall)");
        source.Should().Contain("ChartFunnelMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Funnel)");
        source.Should().NotContain("InsertChartOfType(ChartType.Map)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDPie)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDLine)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDArea)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDColumn)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDBar)");
        source.Should().Contain("InsertChartOfType(ChartType.Surface)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDSurface)");
        // The deferred Map family routes to the deferred-message handler rather than an authoring command.
        source.Should().Contain("DeferredChartFamilyMenuItem_Click(object sender, RoutedEventArgs e) => ShowDeferredChartFamilyMessage()");
        source.Should().Contain("Chart3DPieMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDPie)");
        source.Should().Contain("ChartSurfaceMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Surface)");
        source.Should().Contain("Chart3DSurfaceMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.ThreeDSurface)");
    }

    [Fact]
    public void ChartKeyboardShortcuts_UseSeparateEmbeddedAndChartSheetPaths()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertEmbeddedChart, (_, _) => InsertEmbeddedChart())");
        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertChartSheet, (_, _) => InsertChartSheet())");
    }

    [Fact]
    public void RibbonChartButtons_RouteThroughRenderableChartInsertionCommandPath()
    {
        // After the ribbon XAML→declarative cutover the chart-insertion entry points are declared in the
        // single-source ribbon (FreeXRibbonDefinition.cs, Insert tab Charts group) rather than hand-authored
        // MainWindow.xaml buttons. The renderable families (Recommended/Column/Line/Pie) still route through
        // the same InsertChartOfType command path asserted from the host source below.
        var ribbon = DialogSourceTestSupport.ReadRibbonDefinitionSource("FreeXRibbonDefinition.cs");
        var source = ReadChartCommandSource();

        ribbon.Should().Contain(".Large(\"Recommended Charts\", \"Recommended Charts\"");
        ribbon.Should().Contain(".Medium(\"Column Chart\", \"Column\"");
        ribbon.Should().Contain(".Medium(\"Line Chart\", \"Line\"");
        ribbon.Should().Contain(".Medium(\"Pie Chart\", \"Pie\"");
        source.Should().Contain("private void InsertChartOfType(ChartType type)");
        source.Should().Contain("ChartAuthoringPlanner.CanAuthor(type)");
        source.Should().Contain("ChartCommandWorkflowPlanner.CreateEmbeddedChartPlan(");
        source.Should().Contain("UpdateViewport();");
    }

    [Fact]
    public void FontDropdownSelection_SyncsThroughStyleDiffToolbarStateAndGridTypeface()
    {
        var declarativeSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonDeclarative.cs");
        var formattingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var uiStateSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");
        var renderSource = DialogSourceTestSupport.ReadAppUiSources("GridView.Rendering.CellStyles.cs");

        // The Font combo is now the rendered declarative combo, populated + wired host-side. Selecting
        // or typing a value drives ApplyStyleDiff through the rendered control's sender.
        declarativeSource.Should().Contain("PopulateAndWireRenderedHomeCombos(");
        declarativeSource.Should().Contain("fontBox.SelectionChanged += FontNameBox_SelectionChanged");
        declarativeSource.Should().Contain("fontBox.LostKeyboardFocus += FontNameBox_LostKeyboardFocus");
        formattingSource.Should().Contain("(sender as ComboBox)?.SelectedItem is string name");
        formattingSource.Should().Contain("ApplyStyleDiff(new StyleDiff(FontName: name))");
        uiStateSource.Should().Contain("SetRibbonComboValue(\"Font\", state.FontName)");
        renderSource.Should().Contain("ResolveCellFontForDisplay(style?.FontName)");
        renderSource.Should().Contain("AvailableCellFontNames.Value.Contains");
        renderSource.Should().Contain("new CellTypefaceKey(fontName, stretch, style?.Italic == true, style?.Bold == true)");
    }

    [Fact]
    public void InsertPivotTable_NewWorksheetDestination_UsesUndoableCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("PivotCreatePlanner.BuildNewWorksheetCommand(");
        source.Should().Contain("command.CreatedSheetId");
        source.Should().NotContain("New chart-style PivotTable sheets are tracked for Wave 2");
    }

    [Fact]
    public void FontSizeDropdown_UsesSharedFontSizeApplyPath()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("GetSelectedFontSizeText(combo)");
        source.Should().Contain("ApplyFontSizeAndFitRows(size)");
        source.Should().Contain("RefreshToolbar();");
    }

    [Fact]
    public void QuickAnalysisMenu_UsesPlannerPreviewMetadataForHoverTooltips()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");
        var requestPlanner = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisShellRequestPlanner.cs");
        var openPlanner = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisShellOpenPlanner.cs");
        var planner = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisPlanner.cs");
        var shellPlanner = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisShellPlanner.cs");

        source.Should().Contain("_quickAnalysisSession.PlanOpen(");
        source.Should().NotContain("QuickAnalysisShellRequestPlanner.Build(");
        source.Should().NotContain("QuickAnalysisShellOpenPlanner.Plan(request)");
        source.Should().NotContain("if (!request.CanOpen");
        source.Should().Contain("QuickAnalysisShellOpenPlanner.FormatIssueText(");
        source.Should().Contain("QuickAnalysisShellOpenIssueTextTarget.Status");
        source.Should().NotContain("var issue = openPlan.Issue");
        source.Should().NotContain("openPlan.Decision == QuickAnalysisShellOpenDecision");
        source.Should().Contain("ToolTip = item.ToolTip");
        source.Should().NotContain("QuickAnalysisSelectionReader.Describe(sheet, range)");
        source.Should().NotContain("QuickAnalysisModelBuilder.Build(description).ToDisplayModel()");
        source.Should().NotContain("QuickAnalysisPlanner.BuildDisplayModel(range)");
        source.Should().Contain("Header = UiText.Get(group.TitleResourceKey)");
        source.Should().NotContain("Header = group.TitleFallback");
        source.Should().Contain("foreach (var group in shellPlan.Groups)");
        source.Should().NotContain("foreach (var group in displayModel.Groups)");
        requestPlanner.Should().Contain("QuickAnalysisSelectionReader.Describe(sheet, range)");
        requestPlanner.Should().Contain("QuickAnalysisModelBuilder.Build(description).ToDisplayModel()");
        requestPlanner.Should().Contain("QuickAnalysisShellPlanner.BuildMenuPlan(displayModel, capabilities, range)");
        openPlanner.Should().Contain("new QuickAnalysisShellOpenIssuePlan(");
        openPlanner.Should().Contain("\"TableLoc_QaNoSuggestions\"");
        planner.Should().Contain("QuickAnalysisPreviewKind");
        shellPlanner.Should().Contain("public static QuickAnalysisShellPlan BuildMenuPlan(");
        shellPlanner.Should().Contain("QuickAnalysisShellActionPlanner.Plan(item, capabilities)");
        shellPlanner.Should().Contain("QuickAnalysisPlanner.BuildHoverPreview(selection, item)");
    }

    [Fact]
    public void QuickAnalysisPreviewAssignments_AvoidNoOpRenderInvalidations()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");
        var showPreview = ExtractMethodSource(source, "private void ShowQuickAnalysisPreview(");
        var clearPreview = ExtractMethodSource(source, "private void ClearQuickAnalysisPreview(");
        var applyPreview = ExtractMethodSource(source, "private void ApplyQuickAnalysisPreview(");

        showPreview.Should().Contain("ApplyQuickAnalysisPreview(");
        clearPreview.Should().Contain("_quickAnalysisSession.PlanPreviewClear(resetStatus)");
        showPreview.Should().NotContain("SheetGrid.QuickAnalysisPreviewRange = preview.Range");
        clearPreview.Should().NotContain("SheetGrid.QuickAnalysisPreviewRange = null");
        applyPreview.Should().Contain("if (SheetGrid.QuickAnalysisPreviewRange != range)");
        applyPreview.Should().Contain("if (SheetGrid.QuickAnalysisPreviewVisual != visual)");
    }

    [Fact]
    public void QuickAnalysisMenu_RendersPlannerVisualPreviewIcons()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");
        var planner = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisPlanner.cs");
        var iconPlanner = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisPreviewIconPlanner.cs");

        planner.Should().Contain("QuickAnalysisPreviewVisual");
        iconPlanner.Should().Contain("QuickAnalysisPreviewIconGlyph");
        source.Should().Contain("QuickAnalysisPreviewIconFactory.Create(item.PreviewIcon)");
    }

    [Fact]
    public void QuickAnalysisMenu_UsesKeyboardSelectionAnchorAndInitialMenuFocus()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");

        source.Should().NotContain("PlacementMode.MousePoint");
        source.Should().Contain("Placement = PlacementMode.RelativePoint");
        source.Should().Contain("QuickAnalysisMenuPlacementPlanner.BuildAnchor");
        source.Should().Contain("menu.HorizontalOffset = anchor.X;");
        source.Should().Contain("menu.VerticalOffset = anchor.Y;");
        source.Should().Contain("menu.Opened += QuickAnalysisMenu_Opened;");
        source.Should().Contain("private static void QuickAnalysisMenu_Opened(object sender, RoutedEventArgs e)");
        source.Should().Contain("Keyboard.Focus(firstEnabledItem);");
    }

    [Fact]
    public void QuickAnalysisMenu_UpdatesLiveHoverPreviewStatus()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");

        source.Should().Contain("QuickAnalysisMenuItem_MouseEnter");
        source.Should().Contain("QuickAnalysisMenuItem_MouseLeave");
        source.Should().Contain("var preview = _quickAnalysisSession.PlanPreview(item)");
        source.Should().NotContain("QuickAnalysisPlanner.BuildHoverPreview(range, item)");
        source.Should().Contain("if (preview.StatusText is { } statusText)");
        source.Should().Contain("StatusReadyText.Text = statusText");
        source.Should().Contain("StatusReadyText.Text = UiText.Get(\"MainWindow_Text_Ready\")");
    }

    [Fact]
    public void QuickAnalysisMenu_RoutesExpandedConditionalFormattingGallery()
    {
        var hostSource = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");
        var routeSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisCommandRouter.cs");
        var catalogSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisCatalog.cs");
        var actionPlannerSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisShellActionPlanner.cs");
        var operationPlannerSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisHostOperationPlanner.cs");
        var shellPlannerSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisShellPlanner.cs");

        hostSource.Should().Contain("var operation = _quickAnalysisSession.PlanSelection(item);");
        hostSource.Should().NotContain("QuickAnalysisHostOperationPlanner.Plan(item)");
        hostSource.Should().NotContain("QuickAnalysisShellActionPlanner.Plan(item, QuickAnalysisShellCapabilities.DialogBacked)");
        hostSource.Should().Contain("QuickAnalysisHostOperationKind.OpenConditionalFormatDialog");
        hostSource.Should().Contain("ShowCfDialog(title)");
        hostSource.Should().NotContain("QuickAnalysisConditionalFormatDialogTitle(");

        shellPlannerSource.Should().Contain("QuickAnalysisShellActionPlanner.Plan(item, capabilities)");
        routeSource.Should().Contain("return item.Route;");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.LessThan => QuickAnalysisConditionalFormatCommand.LessThan");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.Between => QuickAnalysisConditionalFormatCommand.Between");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.EqualTo => QuickAnalysisConditionalFormatCommand.EqualTo");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.TextContains => QuickAnalysisConditionalFormatCommand.TextContains");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.DateOccurring => QuickAnalysisConditionalFormatCommand.DateOccurring");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.DuplicateValues => QuickAnalysisConditionalFormatCommand.DuplicateValues");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.Top10Percent => QuickAnalysisConditionalFormatCommand.Top10Percent");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.Bottom10 => QuickAnalysisConditionalFormatCommand.Bottom10Items");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.Bottom10Percent => QuickAnalysisConditionalFormatCommand.Bottom10Percent");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.AboveAverage => QuickAnalysisConditionalFormatCommand.AboveAverage");
        catalogSource.Should().Contain("QuickAnalysisFormatKind.BelowAverage => QuickAnalysisConditionalFormatCommand.BelowAverage");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.LessThan => \"Less Than\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.Between => \"Between\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.EqualTo => \"Equal To\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.TextContains => \"Text Contains\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.DateOccurring => \"Date Occurring\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.DuplicateValues => \"Duplicate Values\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.Top10Percent => \"Top 10%\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.Bottom10Items => \"Bottom 10 Items\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.Bottom10Percent => \"Bottom 10%\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.AboveAverage => \"Above Average\"");
        actionPlannerSource.Should().Contain("QuickAnalysisConditionalFormatCommand.BelowAverage => \"Below Average\"");
        operationPlannerSource.Should().Contain("QuickAnalysisHostOperationKind.OpenConditionalFormatDialog");
    }

    [Fact]
    public void QuickAnalysisMenu_MoreChartsReusesInsertChartDialogPath()
    {
        var hostSource = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");
        var catalogSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisCatalog.cs");

        hostSource.Should().Contain("case QuickAnalysisHostOperationKind.OpenChartPicker:");
        hostSource.Should().Contain("InsertChartPickerBtn_Click(sender, e);");
        catalogSource.Should().Contain("QuickAnalysisCommand.MoreCharts");
        catalogSource.Should().Contain("new QuickAnalysisCommandRoute(QuickAnalysisCommandKind.MoreCharts)");
    }

    [Fact]
    public void QuickAnalysisMenu_RoutesExpandedTotalsGallery()
    {
        var hostSource = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");
        var catalogSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisCatalog.cs");
        var actionPlannerSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisShellActionPlanner.cs");
        var operationPlannerSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisHostOperationPlanner.cs");

        hostSource.Should().Contain("QuickAnalysisHostOperationKind.InsertPercentTotalFormula");
        hostSource.Should().Contain("QuickAnalysisHostOperationKind.InsertRunningTotalFormula");
        hostSource.Should().Contain("QuickAnalysisHostOperationPlanner.TryBuildTotalFormulaEdits(operation, range, out var edits)");
        hostSource.Should().NotContain("QuickAnalysisTotalsPlanner.BuildPercentTotalEdits");
        hostSource.Should().NotContain("QuickAnalysisTotalsPlanner.BuildRunningTotalEdits");
        catalogSource.Should().Contain("QuickAnalysisCommand.PercentTotal");
        catalogSource.Should().Contain("QuickAnalysisTotalFunction.PercentTotal");
        catalogSource.Should().Contain("TotalFormulaKind: QuickAnalysisTotalFormulaKind.PercentTotal");
        catalogSource.Should().Contain("QuickAnalysisCommand.RunningTotal");
        catalogSource.Should().Contain("QuickAnalysisTotalFunction.RunningTotal");
        catalogSource.Should().Contain("TotalFormulaKind: QuickAnalysisTotalFormulaKind.RunningTotal");
        actionPlannerSource.Should().Contain("QuickAnalysisShellActionKind.InsertPercentTotalFormula");
        actionPlannerSource.Should().Contain("QuickAnalysisShellActionKind.InsertRunningTotalFormula");
        actionPlannerSource.Should().Contain("TotalCommandTitle: \"Quick Analysis % Total\"");
        actionPlannerSource.Should().Contain("TotalCommandTitle: \"Quick Analysis Running Total\"");
        operationPlannerSource.Should().Contain("TotalCommandTitle: action.TotalCommandTitle");
        operationPlannerSource.Should().Contain("QuickAnalysisTotalsPlanner.BuildPercentTotalEdits(range)");
        operationPlannerSource.Should().Contain("QuickAnalysisTotalsPlanner.BuildRunningTotalEdits(range)");
    }

    [Fact]
    public void ConditionalFormattingPopupRows_UseSharedCatalogEvidenceAndGeneratedRibbonMenu()
    {
        var homeFormattingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var menuSource = DialogSourceTestSupport.ReadRibbonDefinitionSource("HomeRibbonMenus.g.cs");

        foreach (var item in ConditionalFormatPresetGalleryPlanner.PopupItems)
            menuSource.Should().Contain($"\"{item.CommandId}\"");

        homeFormattingSource.Should().Contain("PopulateConditionalFormatDataBarGallery");
        homeFormattingSource.Should().Contain("PopulateConditionalFormatColorScaleGallery");
        homeFormattingSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.DataBarGroups");
        homeFormattingSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.ColorScaleGroups");
        homeFormattingSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.CreateDataBarRule(style, range)");
        homeFormattingSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.CreateColorScaleRule(style, range)");
        homeFormattingSource.Should().Contain("ConditionalFormatIconSetCatalog.CreateRule(style, range)");
    }

    [Fact]
    public void BorderGallery_ExposesExpandedPresetsAndUsesReusablePlanners()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        // After the ribbon XAML→declarative cutover the Borders gallery is declared in the single-source
        // ribbon (HomeRibbonMenus.g.cs) rather than a hand-authored MainWindow.xaml ContextMenu. The
        // expanded preset labels live there; the host source still owns the handler methods + planners.
        var bordersMenu = DialogSourceTestSupport.ReadRibbonDefinitionSource("HomeRibbonMenus.g.cs");

        foreach (var label in new[]
        {
            "Bottom Double Border",
            "Inside Borders",
            "Thick Outside Borders",
            "Top and Bottom Border",
            "Top and Thick Bottom Border",
            "Top and Double Bottom Border",
            "Draw Border",
            "Draw Border Grid",
            "Erase Border",
            "Line Color",
            "Line Style",
            "Black",
            "Accent 1",
            "Dashed",
            "Dotted",
            "More Borders..."
        })
            bordersMenu.Should().Contain($"\"{label}\"");

        foreach (var handler in new[]
        {
            "BorderBottomDoubleMenuItem_Click",
            "BorderInsideMenuItem_Click",
            "BorderThickBoxMenuItem_Click",
            "BorderTopAndBottomMenuItem_Click",
            "BorderTopAndThickBottomMenuItem_Click",
            "BorderTopAndDoubleBottomMenuItem_Click",
            "BorderDrawMenuItem_Click",
            "BorderDrawGridMenuItem_Click",
            "BorderEraseMenuItem_Click",
            "BorderLineColorBlackMenuItem_Click",
            "BorderLineColorAccent1MenuItem_Click",
            "BorderLineStyleDashedMenuItem_Click",
            "BorderLineStyleDottedMenuItem_Click",
            "BorderMoreMenuItem_Click"
        })
        {
            source.Should().Contain(handler);
        }

        source.Should().Contain("ApplyRangeBorderPreset");
        source.Should().Contain("SelectionStyleCommandPlanner.CreatePerCellStyleCommand");
        var plannerSource = DialogSourceTestSupport.ReadAppServicesSource("SelectionStyleCommandPlanner.cs");
        plannerSource.Should().Contain("new CompositeWorkbookCommand(title, commands)");
        plannerSource.Should().Contain("MergeCompleteRectangularBands");
        source.Should().Contain("OpenFormatCellsDialog(FormatCellsDialogTab.Border)");
        source.Should().Contain("_borderPickerColor");
        source.Should().Contain("_borderPickerStyle");
        source.Should().Contain("BeginBorderDrawMode(BorderDrawMode.Draw)");
        source.Should().Contain("BeginBorderDrawMode(BorderDrawMode.DrawGrid)");
        source.Should().Contain("BeginBorderDrawMode(BorderDrawMode.Erase)");
        source.Should().Contain("ApplyBorderDrawMode");
        source.Should().Contain("BorderDrawPlanner.CreateCommand");
        source.Should().Contain("BorderShortcutService.GetSingleBorderDiff");
        source.Should().Contain("BorderShortcutService.GetInsideBorderDiff");
        source.Should().Contain("BorderShortcutService.GetTopAndBottomBorderDiff");
        source.Should().Contain("BorderShortcutService.GetOutlineBorderDiff");
    }

    [Fact]
    public void FormatAsTable_CreatesStructuredTableMetadataAndBandingAsOneCommand()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("new CreateTableDialog");
        source.Should().Contain("TableCreationPlanner.BuildStyledCommand(");
        source.Should().Contain("ApplyTableFormat(item.Option);");
        source.Should().NotContain("new CreateStructuredTableCommand(");
        source.Should().NotContain("new CreateStyledStructuredTableCommand(");
        source.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(dialog.Result.Range, sheetId)");
        source.Should().Contain("dialog.Result.TableStyleName");
        source.Should().Contain("tableStyle.Banding");
    }

    [Fact]
    public void CellStyleMenu_UsesActiveWorkbookThemeForPresetPlanning()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme)");
    }

    [Fact]
    public void DrawObjectButtons_ExposeStableAutomationMetadata()
    {
        // After the ribbon XAML→declarative cutover the Draw-tab object commands are declared in the
        // single-source ribbon (FreeXRibbonDefinition.cs). Their stable automation identity is now the
        // command name carried by each declarative control (the renderer derives AutomationProperties
        // from it), so the catalog command names are the post-cutover equivalent of the old explicit
        // AutomationIds. The Shape Effects command keeps its full submenu of effect choices.
        var ribbon = DialogSourceTestSupport.ReadRibbonDefinitionSource("FreeXRibbonDefinition.cs");

        ribbon.Should().Contain(".Large(\"Bring Forward\", \"Bring Forward\"");
        ribbon.Should().Contain(".Large(\"Send Backward\", \"Send Backward\"");
        ribbon.Should().Contain(".Large(\"Selection Pane#SelectionPaneBtn_Click\", \"Selection Pane\"");
        ribbon.Should().Contain(".Large(\"Rotate Object\", \"Rotate Object\"");
        ribbon.Should().Contain(".Large(\"Object Size\", \"Object Size\"");
        ribbon.Should().Contain(".Medium(\"Shape Fill\", \"Shape Fill\"");
        ribbon.Should().Contain(".Medium(\"Object Outline\", \"Object Outline\"");
        ribbon.Should().Contain(".Medium(\"Shape Gradient\", \"Shape Gradient\"");
        ribbon.Should().Contain(".Medium(\"Shape Effects\", \"Shape Effects\"");
        ribbon.Should().Contain(".Item(\"No Effect\", \"No Effect\"");
        ribbon.Should().Contain(".Item(\"3-D Rotation\", \"3-D Rotation\"");
    }

    [Fact]
    public void CollapsedRibbonOverflowCommands_ReturnFocusToVisibleGroupButton()
    {
        var adaptiveSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonAdaptive.cs");

        adaptiveSource.Should().Contain("FocusCollapsedRibbonMenuPlacementTarget(item)");
        adaptiveSource.Should().Contain("private static void FocusCollapsedRibbonMenuPlacementTarget(MenuItem item)");
        adaptiveSource.Should().Contain("contextMenu.PlacementTarget is UIElement placementTarget");
        adaptiveSource.Should().Contain("placementTarget.Focus();");
    }
}
