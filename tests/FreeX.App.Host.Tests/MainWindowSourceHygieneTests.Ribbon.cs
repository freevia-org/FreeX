using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void RibbonSplitButtonHover_UsesRibbonButtonHoverBrushInsteadOfMenuHoverBrush()
    {
        var ribbonDropdownSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.RibbonDropdown.cs"));
        var resources = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "MainWindowResources.xaml"));
        var theme = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "ThemeResources.xaml"));
        var hoverMethod = ExtractMethodSource(ribbonDropdownSource, "private static Brush GetRibbonDropdownHoverBrush(");

        resources.Should().Contain("FreeXRibbonButtonHoverBrush");
        theme.Should().Contain("FreeXRibbonButtonHoverBrush\" Color=\"#BEE6FD\"");
        hoverMethod.Should().Contain("FreeXRibbonButtonHoverBrush");
        hoverMethod.Should().NotContain("FreeXAccentSoftBrush");
        ribbonDropdownSource.Should().Contain("Color.FromRgb(0x3C, 0x7F, 0xB1)");
    }

    [Fact]
    public void StandaloneAltKeyTips_DoNotRouteAltKeyChords()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var altKeyTipSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.AltKeyTips.cs"));

        selectionSource.Should().NotContain("TryHandleTopLevelRibbonKeyTip(keyTip)");
        selectionSource.Should().NotContain("TryInvokeTopLevelQatKeyTip(qatKeyTip)");
        altKeyTipSource.Should().Contain("WM_SYSKEYDOWN");
        altKeyTipSource.Should().Contain("StandaloneAltKeyTipTracker.IsAltVirtualKey");
        altKeyTipSource.Should().Contain("_standaloneAltKeyTipTracker.CancelStandaloneAltCandidate();");
    }

    [Fact]
    public void ViewWindowAndZoomController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var viewSourcePath = Path.Combine(appHostDirectory, "MainWindow.ViewCommands.cs");

        File.Exists(viewSourcePath).Should().BeTrue();
        var viewSource = File.ReadAllText(viewSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var ribbonSourcePath = Path.Combine(appHostDirectory, "MainWindow.Ribbon.cs");
        var ribbonAdaptiveSourcePath = Path.Combine(appHostDirectory, "MainWindow.RibbonAdaptive.cs");

        File.Exists(ribbonSourcePath).Should().BeTrue();
        File.Exists(ribbonAdaptiveSourcePath).Should().BeTrue();
        var ribbonSource =
            File.ReadAllText(ribbonSourcePath) +
            File.ReadAllText(ribbonAdaptiveSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var quickAnalysisSourcePath = Path.Combine(appHostDirectory, "MainWindow.QuickAnalysis.cs");

        File.Exists(quickAnalysisSourcePath).Should().BeTrue();
        var quickAnalysisSource = File.ReadAllText(quickAnalysisSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var formatPainterSourcePath = Path.Combine(appHostDirectory, "MainWindow.FormatPainter.cs");

        File.Exists(formatPainterSourcePath).Should().BeTrue();
        var formatPainterSource = File.ReadAllText(formatPainterSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var dataSourcePath = Path.Combine(appHostDirectory, "MainWindow.DataCommands.cs");
        var scenarioSourcePath = Path.Combine(appHostDirectory, "MainWindow.ScenarioCommands.cs");

        File.Exists(dataSourcePath).Should().BeTrue();
        File.Exists(scenarioSourcePath).Should().BeTrue();
        var dataSource = File.ReadAllText(dataSourcePath);
        var scenarioSource = File.ReadAllText(scenarioSourcePath);

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
        scenarioSource.Should().Contain("private void ScenariosBtn_Click(");
    }

    [Fact]
    public void ReviewProtectionShareCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var reviewSourcePath = Path.Combine(appHostDirectory, "MainWindow.ReviewCommands.cs");

        File.Exists(reviewSourcePath).Should().BeTrue();
        var reviewSource = File.ReadAllText(reviewSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var formulaSourcePath = Path.Combine(appHostDirectory, "MainWindow.FormulaCommands.cs");

        File.Exists(formulaSourcePath).Should().BeTrue();
        var formulaSource = File.ReadAllText(formulaSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var clipboardSourcePath = Path.Combine(appHostDirectory, "MainWindow.ClipboardCommands.cs");

        File.Exists(clipboardSourcePath).Should().BeTrue();
        var clipboardSource = File.ReadAllText(clipboardSourcePath);

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
        var clipboardSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ClipboardCommands.cs"));

        clipboardSource.Should().Contain("case PasteSpecialAction.ExternalText:");
        clipboardSource.Should().Contain("externalTextAsText: true");
        clipboardSource.Should().Contain("preserveText: externalTextAsText");
    }

    [Fact]
    public void HomeFormattingCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var formattingSourcePath = Path.Combine(appHostDirectory, "MainWindow.HomeFormatting.cs");

        File.Exists(formattingSourcePath).Should().BeTrue();
        var formattingSource = File.ReadAllText(formattingSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var cellsSourcePath = Path.Combine(appHostDirectory, "MainWindow.CellsCommands.cs");

        File.Exists(cellsSourcePath).Should().BeTrue();
        var cellsSource = File.ReadAllText(cellsSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var editingSourcePath = Path.Combine(appHostDirectory, "MainWindow.HomeEditing.cs");

        File.Exists(editingSourcePath).Should().BeTrue();
        var editingSource = File.ReadAllText(editingSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var outlineSourcePath = Path.Combine(appHostDirectory, "MainWindow.OutlineCommands.cs");

        File.Exists(outlineSourcePath).Should().BeTrue();
        var outlineSource = File.ReadAllText(outlineSourcePath);

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
    }

    [Fact]
    public void ChartCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var chartSourcePath = Path.Combine(appHostDirectory, "MainWindow.ChartCommands.cs");

        File.Exists(chartSourcePath).Should().BeTrue();
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var pivotSourcePath = Path.Combine(appHostDirectory, "MainWindow.PivotCommands.cs");

        File.Exists(pivotSourcePath).Should().BeTrue();
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
            "FindPivotTableContainingSelection(sheet, SheetGrid.SelectedRange)",
            "Excel only shows PivotTable contextual tabs and the field list when the selection is inside a PivotTable");
        refreshFieldListSource.Should().NotContain(
            "FindPivotTableForSelection(sheet, SheetGrid.SelectedRange)",
            "the workbook fallback would show contextual PivotTable tabs after selection leaves the PivotTable");
    }

    [Fact]
    public void RibbonKeyTipController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var keyTipSourcePath = Path.Combine(appHostDirectory, "MainWindow.KeyTips.cs");

        File.Exists(keyTipSourcePath).Should().BeTrue();
        var keyTipSource = File.ReadAllText(keyTipSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var commandSourcePath = Path.Combine(appHostDirectory, "MainWindow.CommandExecution.cs");

        File.Exists(commandSourcePath).Should().BeTrue();
        var commandSource = File.ReadAllText(commandSourcePath);

        mainSource.Should().NotContain("void ShowCommandError(");
        mainSource.Should().NotContain("private bool TryExecuteCommand(");
        mainSource.Should().NotContain("private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds(");
        mainSource.Should().NotContain("private bool TryExecuteEditCells(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableGroupedSheetCommand(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableCurrentRangeCommand(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableChartLayout(");
        mainSource.Should().NotContain("private void ExecuteUndo(");
        mainSource.Should().NotContain("private void ExecuteRepeatLast(");
        mainSource.Should().NotContain("private IWorkbookCommand CreateSingleCellEditCommand(");

        commandSource.Should().Contain("private void ShowCommandError(");
        commandSource.Should().Contain("private bool TryExecuteCommand(");
        commandSource.Should().Contain("private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds(");
        commandSource.Should().Contain("private bool TryExecuteEditCells(");
        commandSource.Should().Contain("private bool TryExecuteRepeatableGroupedSheetCommand(");
        commandSource.Should().Contain("private bool TryExecuteRepeatableCurrentRangeCommand(");
        commandSource.Should().Contain("private bool TryExecuteRepeatableChartLayout(");
        commandSource.Should().Contain("private void ExecuteUndo(");
        commandSource.Should().Contain("private void ExecuteRepeatLast(");
        commandSource.Should().Contain("private IWorkbookCommand CreateSingleCellEditCommand(");
        commandSource.Should().Contain("ExecuteRepeatable");
    }

    [Fact]
    public void DataFilterCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var dataFilterSourcePath = Path.Combine(appHostDirectory, "MainWindow.DataFilterCommands.cs");

        File.Exists(dataFilterSourcePath).Should().BeTrue();
        var dataFilterSource = File.ReadAllText(dataFilterSourcePath);

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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var insertSourcePath = Path.Combine(appHostDirectory, "MainWindow.InsertCommands.cs");

        File.Exists(insertSourcePath).Should().BeTrue();
        var insertSource = File.ReadAllText(insertSourcePath);

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
        insertSource.Should().Contain("SparklineInputParser");
    }

    [Fact]
    public void ShellChromeController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var shellSourcePath = Path.Combine(appHostDirectory, "MainWindow.Shell.cs");

        File.Exists(shellSourcePath).Should().BeTrue();
        var shellSource = File.ReadAllText(shellSourcePath);

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.WorkbookUiState.cs"));
        var qatSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAccessToolbar.cs"));
        var qatStateSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "QuickAccessCommandState.cs"));
        var toolbarSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ToolbarVisualState.cs"));
        var cacheSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ToolbarVisualStateCache.cs"));

        source.Should().Contain("RefreshQuickAccessToolbarCommandStates();");
        source.Should().Contain("RefreshQuickAccessToolbarCommandStatesAfterSelectionChange();");
        qatSource.Should().Contain("private void RefreshQuickAccessToolbarCommandStates(bool force = false)");
        qatSource.Should().Contain("private void RefreshQuickAccessToolbarCommandStatesAfterSelectionChange()");
        qatSource.Should().Contain("private QuickAccessCommandState CreateQuickAccessCommandState()");
        qatSource.Should().Contain("_commandBus.CanUndo(_workbook.Id)");
        qatSource.Should().Contain("_commandBus.CanRedo(_workbook.Id)");
        qatSource.Should().Contain("HasActiveWorksheetForQuickAccessCommandState()");
        qatSource.Should().Contain("HasSelectionForQuickAccessCommandState()");
        qatSource.Should().Contain("state.WithSelectionContext(");
        qatSource.Should().Contain("_lastQuickAccessCommandStateWorkbookId == _workbook.Id");
        qatSource.Should().Contain("QuickAccessCommandStateResolver.CanExecute(target.Availability, state)");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.WorkbookUiState.cs"));
        var refreshToolbar = ExtractMethodSource(source, "private void RefreshToolbarVisualState()");

        source.Should().Contain("private static void SetToggleCheckedIfChanged(");
        source.Should().Contain("private static void SetSelectedItemIfChanged(");
        source.Should().Contain("private void RefreshToolbarAfterSelectionChange()");
        refreshToolbar.Should().Contain("SetToggleCheckedIfChanged(BoldButton, state.Bold)");
        refreshToolbar.Should().Contain("SetSelectedItemIfChanged(FontNameBox, state.FontName)");
        refreshToolbar.Should().NotContain("BoldButton.IsChecked = state.Bold");
        refreshToolbar.Should().NotContain("FontNameBox.SelectedItem = state.FontName");
    }

    [Fact]
    public void SetActiveCellCallers_AvoidDuplicateToolbarAndStatusRefresh()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var multiWindowSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.MultiWindow.cs"));
        var dataSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));
        var scenarioSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ScenarioCommands.cs"));

        var createNewWorkbook = ExtractMethodSource(backstageSource, "private void CreateNewWorkbook()");
        createNewWorkbook.Should().Contain("SetActiveCell(new CellAddress(_currentSheetId, 1, 1));");
        createNewWorkbook.Should().NotContain("RefreshToolbar();");

        var adoptSharedWorkbook = ExtractMethodSource(multiWindowSource, "private void AdoptSharedWorkbook()");
        adoptSharedWorkbook.Should().Contain("SetActiveCell(new CellAddress(_currentSheetId, 1, 1));");
        adoptSharedWorkbook.Should().NotContain("RefreshToolbar();");
        adoptSharedWorkbook.Should().NotContain("RefreshStatusBar();");

        ExtractMethodSource(dataSource, "private async void GetDataBtn_Click(")
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
        var editingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Editing.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var lifecycleSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.WorkbookLifecycle.cs"));

        // Multi-window slice 1 adds an Excel-style per-window number suffix to the shared formatter.
        editingSource.Should().Contain("WorkbookTitleFormatter.Format(");
        editingSource.Should().Contain("_workbook.Name, _workbookDirty, IsWorkbookGrouped(), _windowTitleSuffix)");
        lifecycleSource.Should().Contain("_workbookDirty = true;");
        lifecycleSource.Should().Contain("_workbookDirty = false;");
        lifecycleSource.Should().Contain("UpdateTitleBar();");
        backstageSource.Should().Contain("_workbook.Name = WorkbookTitleFormatter.DisplayNameFromPath(target.Path);");
        backstageSource.Should().Contain("MarkWorkbookSaved();");
    }

    [Fact]
    public void KeyboardShortcuts_RegisterExcelNameManagerCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));

        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NameManager, NamedRangesButton_Click);");
        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.CreateNamesFromSelection, CreateNamesFromSelectionBtn_Click);");
    }

    [Fact]
    public void FormulaBarTextChanged_SkipsFormulaHighlightWorkForSelectionDisplayUpdates()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("var formulaBarHasFocus = ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, FormulaBar);");
        source.Should().Contain("if (!formulaBarHasFocus && _inlineEditor?.IsVisible != true)");
        source.Should().Contain("ClearFormulaReferenceHighlights();");
    }

    [Fact]
    public void WorkbookUiStateController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var uiStateSourcePath = Path.Combine(appHostDirectory, "MainWindow.WorkbookUiState.cs");

        File.Exists(uiStateSourcePath).Should().BeTrue();
        var uiStateSource = File.ReadAllText(uiStateSourcePath);

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
        var mainWindowPath = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml");
        var appHostDirectory = Directory.GetParent(mainWindowPath)!.FullName;
        var xaml = File.ReadAllText(mainWindowPath);
        var resourcesPath = Path.Combine(appHostDirectory, "Resources", "MainWindowResources.xaml");
        var resourcesXaml = File.ReadAllText(resourcesPath);

        File.Exists(Path.Combine(appHostDirectory, "Resources", "ThemeResources.xaml")).Should().BeTrue();
        File.Exists(Path.Combine(appHostDirectory, "Resources", "IconResources.xaml")).Should().BeTrue();
        xaml.Should().Contain("Source=\"Resources/MainWindowResources.xaml\"");
        resourcesXaml.Should().Contain("Source=\"ThemeResources.xaml\"");
        resourcesXaml.Should().Contain("Source=\"IconResources.xaml\"");
    }

    [Fact]
    public void RibbonIconSet_UsesSharedIconSlotsAndDecorator()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var iconResources = File.ReadAllText(Path.Combine(appHostDirectory, "Resources", "IconResources.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Ribbon.cs"));
        var planner = string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RibbonCommandPresentationPlanner.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RibbonCommandPresentationPlanner.Icons.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RibbonCommandPresentationTypes.cs")));

        File.Exists(Path.Combine(appHostDirectory, "RibbonIconFactory.cs")).Should().BeTrue();
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
        var source =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Startup.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "HomeNumberFormatDropdownPlanner.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsNumberFormatPlanner.cs"));

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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));

        xaml.Should().Contain("Opened=\"ArrangeAllContextMenu_Opened\"");
        xaml.Should().Contain("IsCheckable=\"True\"");
        source.Should().Contain("ArrangeAllContextMenu_Opened");
        source.Should().Contain("ArrangeAllMenuPlanner.IsChecked(item.Tag, _workbook.WindowArrangement)");
        source.Should().Contain("ArrangeAllMenuPlanner.TryParseArrangement");
        source.Should().Contain("_windowRegistry?.ArrangeVisibleWindows(arrangement, workArea.Width, workArea.Height)");
    }

    [Fact]
    public void SplitRibbonCommand_ReflectsActiveSplitState()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Viewport.cs"));

        xaml.Should().Contain("<ToggleButton x:Name=\"SplitViewBtn\"");
        xaml.Should().Contain("Style=\"{StaticResource RibbonToggleBtn}\"");
        source.Should().Contain("SplitViewBtn.IsChecked = sheet?.SplitRow is not null || sheet?.SplitColumn is not null");
    }

    [Fact]
    public void QuickAccessToolbar_UsesVectorIcons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAccessToolbar.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var iconResources = File.ReadAllText(Path.Combine(appHostDirectory, "Resources", "IconResources.xaml"));

        source.Should().Contain("Content = new RibbonIcon");
        source.Should().Contain("Kind = command.IconKind");
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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        titleBarCommands.Split("Foreground=\"{StaticResource FreeXWhiteBrush}\"").Length.Should().BeGreaterThanOrEqualTo(4);
        var qatSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAccessToolbar.cs"));
        qatSource.Should().Contain("? \"FreeXTextBrush\"");
        qatSource.Should().Contain(": \"FreeXWhiteBrush\"");
    }

    [Fact]
    public void ToolbarIcons_DoNotUseFontGlyphAssets()
    {
        var mainWindowPath = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml");
        var appHostDirectory = Path.GetDirectoryName(mainWindowPath)!;
        var xaml = File.ReadAllText(mainWindowPath);
        var iconResources = File.ReadAllText(Path.Combine(appHostDirectory, "Resources", "IconResources.xaml"));

        xaml.Should().NotContain("Segoe MDL2 Assets");
        xaml.Should().NotContain("RibbonIconGlyph");
        xaml.Should().NotContain("FreeXQatOnAccentIcon");
        iconResources.Should().NotContain("Segoe MDL2 Assets");
        iconResources.Should().NotContain("FreeXRibbonGlyph");
    }

    [Fact]
    public void MainWindow_UsesVisibleFreeXBrandingAndWindowIcon()
    {
        var mainWindowPath = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml");
        var projectPath = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FreeX.App.Host.csproj");
        var appHostDirectory = Directory.GetParent(mainWindowPath)!.FullName;
        var theme = File.ReadAllText(Path.Combine(appHostDirectory, "Resources", "ThemeResources.xaml"));
        var xaml = File.ReadAllText(mainWindowPath);
        var project = File.ReadAllText(projectPath);

        File.Exists(Path.Combine(appHostDirectory, "Resources", "FreeX.ico")).Should().BeTrue();
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
        xaml.Should().Contain("Background=\"{StaticResource FreeXTitleBarBrush}\"");
        project.Should().Contain("<ApplicationIcon>Resources\\FreeX.ico</ApplicationIcon>");
    }

    [Fact]
    public void PersistentFormatPainter_UsesPreviewMouseDownSoButtonDoubleClickCannotBeOverwrittenByClick()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.FormatPainter.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

        source.Should().Contain("private bool _formatPainterPersistent;");
        source.Should().Contain("FormatPainterBtn_PreviewMouseLeftButtonDown");
        source.Should().Contain("if (e.ClickCount != 2) return;");
        source.Should().Contain("CaptureFormatPainterSource(persistent: true);");
        source.Should().Contain("e.Handled = true;");
        source.Should().Contain("CancelFormatPainter");
        xaml.Should().Contain("PreviewMouseLeftButtonDown=\"FormatPainterBtn_PreviewMouseLeftButtonDown\"");
        xaml.Should().NotContain("MouseDoubleClick=\"FormatPainterBtn_MouseDoubleClick\"");
    }

    [Fact]
    public void FormatPainterApplication_UsesTargetSelectionRangeWhenAvailable()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.FormatPainter.cs"));
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        source.Should().Contain("private SheetId? _formatPainterSourceSheetId;");
        source.Should().Contain("private GridRange? _formatPainterSourceRange;");
        source.Should().Contain("private bool _formatPainterTargetSelectionActive;");
        source.Should().Contain("TryApplyFormatPainter(GridRange targetRange)");
        source.Should().Contain("_formatPainterSourceRange = range;");
        source.Should().Contain("var targetSheetIds = CurrentGroupedEditSheetIds();");
        source.Should().Contain("FormatPainterCommandFactory.Create(_workbook, sourceSheet, sourceRange, targetRange)");
        source.Should().Contain("new CompositeWorkbookCommand(\"Format Painter\", targetSheetIds.Select(CreateCommand).ToList())");
        selectionSource.Should().Contain("SheetGrid.SelectedRange is { } selectedRange");
        selectionSource.Should().Contain("selectedRange.Contains(newAddr)");
        selectionSource.Should().Contain("TryApplyFormatPainter(selectedRange)");
        source.Should().NotContain("var targetRange = new GridRange(addr, addr);");
    }

    [Fact]
    public void AutoFitMenuHandlers_UsePlannerAndPerTargetExplicitSizes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.CellsCommands.cs"));
        var planner = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFitPlanner.cs"));
        var dimensionPlanner = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RowColumnDimensionPlanner.cs"));

        source.Should().Contain("AutoFitPlanner.PlanRowHeights");
        source.Should().Contain("AutoFitPlanner.PlanColumnWidths");
        source.Should().Contain("RowColumnDimensionPlanner.CreateAutoFitRowHeightCommand(sheetId, plans)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateAutoFitColumnWidthCommand(sheetId, plans)");
        dimensionPlanner.Should().Contain("if (plans.Count == 1)");
        dimensionPlanner.Should().Contain("return createCommand(plans[0]);");
        dimensionPlanner.Should().Contain("new SetRowHeightCommand(sheetId, plan.Index, plan.Index, plan.Size)");
        dimensionPlanner.Should().Contain("new SetColumnWidthCommand(sheetId, plan.Index, plan.Index, plan.Size)");
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
        var source = ReadChartCommandSource();
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

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
        AssertChartButtonRoutesTo(xaml, "Treemap", "ChartTreemapMenuItem_Click", isDeferred: false);
        AssertChartButtonRoutesTo(xaml, "Sunburst", "ChartSunburstMenuItem_Click", isDeferred: false);
        AssertChartButtonRoutesTo(xaml, "Histogram", "ChartHistogramMenuItem_Click", isDeferred: false);
        AssertChartButtonRoutesTo(xaml, "Pareto", "ChartParetoMenuItem_Click", isDeferred: false);
        AssertChartButtonRoutesTo(xaml, "Box Plot", "ChartBoxAndWhiskerMenuItem_Click", isDeferred: false);
        AssertChartButtonRoutesTo(xaml, "Waterfall", "ChartWaterfallMenuItem_Click", isDeferred: false);
        AssertChartButtonRoutesTo(xaml, "Funnel", "ChartFunnelMenuItem_Click", isDeferred: false);
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Map Chart\"");
        xaml.Should().NotContain("Click=\"DeferredChartFamilyMenuItem_Click\"");
        xaml.Should().NotContain("local:RibbonTooltip.KeyTip=\"MP\"");
        xaml.Should().Contain("Click=\"Chart3DPieMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DLineMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DAreaMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DColumnMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DBarMenuItem_Click\"");
        xaml.Should().Contain("Click=\"ChartSurfaceMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DSurfaceMenuItem_Click\"");
        foreach (var chartLabel in new[]
        {
            "Surface",
            "Treemap",
            "Sunburst",
            "Histogram",
            "Pareto",
            "Box Plot",
            "Waterfall",
            "Funnel",
            "3D Pie",
            "3D Line",
            "3D Area",
            "3D Column",
            "3D Bar",
            "3D Surface"
        })
        {
            xaml.ShouldContainLocalizedAttribute("Content", chartLabel);
        }
    }

    [Fact]
    public void ChartKeyboardShortcuts_UseSeparateEmbeddedAndChartSheetPaths()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));

        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertEmbeddedChart, (_, _) => InsertEmbeddedChart())");
        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertChartSheet, (_, _) => InsertChartSheet())");
    }

    [Fact]
    public void RibbonChartButtons_RouteThroughRenderableChartInsertionCommandPath()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = ReadChartCommandSource();

        xaml.Should().Contain("Click=\"InsertChartPickerBtn_Click\"");
        xaml.Should().Contain("Click=\"ChartColumnMenuItem_Click\"");
        xaml.Should().Contain("Click=\"ChartLineMenuItem_Click\"");
        xaml.Should().Contain("Click=\"ChartPieMenuItem_Click\"");
        source.Should().Contain("private void InsertChartOfType(ChartType type)");
        source.Should().Contain("ChartAuthoringPlanner.CanAuthor(type)");
        source.Should().Contain("new AddChartCommand(_currentSheetId, currentRange, type, \"Chart\")");
        source.Should().Contain("UpdateViewport();");
    }

    [Fact]
    public void FontDropdownSelection_SyncsThroughStyleDiffToolbarStateAndGridTypeface()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var formattingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        var uiStateSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.WorkbookUiState.cs"));
        var renderSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Rendering.CellStyles.cs"));

        xaml.Should().Contain("x:Name=\"FontNameBox\"");
        xaml.Should().Contain("SelectionChanged=\"FontNameBox_SelectionChanged\"");
        formattingSource.Should().Contain("ApplyStyleDiff(new StyleDiff(FontName: name))");
        uiStateSource.Should().Contain("SetSelectedItemIfChanged(FontNameBox, state.FontName)");
        renderSource.Should().Contain("var fontName = string.IsNullOrWhiteSpace(style?.FontName)");
        renderSource.Should().Contain("new CellTypefaceKey(fontName, style?.Italic == true, style?.Bold == true)");
    }

    [Fact]
    public void InsertPivotTable_NewWorksheetDestination_UsesUndoableCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("new AddPivotTableToNewWorksheetCommand(");
        source.Should().Contain("command.CreatedSheetId");
        source.Should().NotContain("New chart-style PivotTable sheets are tracked for Wave 2");
    }

    [Fact]
    public void FontSizeDropdown_UsesSharedFontSizeApplyPath()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("GetSelectedFontSizeText()");
        source.Should().Contain("ApplyFontSizeAndFitRows(size)");
        source.Should().NotContain("FontSizeBox.Text;\r\n        if (WorksheetSizeInputParser.TryParsePositiveSize(text, out var size))\r\n            ApplyStyleDiff(new StyleDiff(FontSize: size));");
        source.Should().Contain("RefreshToolbar();");
    }

    [Fact]
    public void QuickAnalysisMenu_UsesPlannerPreviewMetadataForHoverTooltips()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));
        var planner = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "QuickAnalysisPlanner.cs"));

        source.Should().Contain("ToolTip = option.PreviewText");
        planner.Should().Contain("QuickAnalysisPreviewKind");
    }

    [Fact]
    public void QuickAnalysisPreviewAssignments_AvoidNoOpRenderInvalidations()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));
        var showPreview = ExtractMethodSource(source, "private void ShowQuickAnalysisPreview(");
        var clearPreview = ExtractMethodSource(source, "private void ClearQuickAnalysisPreview(");
        var applyPreview = ExtractMethodSource(source, "private void ApplyQuickAnalysisPreview(");

        showPreview.Should().Contain("ApplyQuickAnalysisPreview(");
        clearPreview.Should().Contain("ApplyQuickAnalysisPreview(null, GridQuickAnalysisPreviewVisualKind.None)");
        showPreview.Should().NotContain("SheetGrid.QuickAnalysisPreviewRange = preview.Range");
        clearPreview.Should().NotContain("SheetGrid.QuickAnalysisPreviewRange = null");
        applyPreview.Should().Contain("if (SheetGrid.QuickAnalysisPreviewRange != range)");
        applyPreview.Should().Contain("if (SheetGrid.QuickAnalysisPreviewVisual != visual)");
    }

    [Fact]
    public void QuickAnalysisMenu_RendersPlannerVisualPreviewIcons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));
        var planner = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "QuickAnalysisPlanner.cs"));

        planner.Should().Contain("QuickAnalysisPreviewVisual");
        source.Should().Contain("QuickAnalysisPreviewIconFactory.Create(option.PreviewVisual)");
    }

    [Fact]
    public void QuickAnalysisMenu_UsesKeyboardSelectionAnchorAndInitialMenuFocus()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("QuickAnalysisMenuItem_MouseEnter");
        source.Should().Contain("QuickAnalysisMenuItem_MouseLeave");
        source.Should().Contain("QuickAnalysisPlanner.BuildHoverPreview(range, option)");
        source.Should().Contain("StatusReadyText.Text = preview.StatusText");
        source.Should().Contain("StatusReadyText.Text = UiText.Get(\"MainWindow_Text_Ready\")");
    }

    [Fact]
    public void QuickAnalysisMenu_RoutesExpandedConditionalFormattingGallery()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("case QuickAnalysisCommand.LessThan:");
        source.Should().Contain("ShowCfDialog(\"Less Than\")");
        source.Should().Contain("case QuickAnalysisCommand.Between:");
        source.Should().Contain("ShowCfDialog(\"Between\")");
        source.Should().Contain("case QuickAnalysisCommand.EqualTo:");
        source.Should().Contain("ShowCfDialog(\"Equal To\")");
        source.Should().Contain("case QuickAnalysisCommand.TextContains:");
        source.Should().Contain("ShowCfDialog(\"Text Contains\")");
        source.Should().Contain("case QuickAnalysisCommand.DateOccurring:");
        source.Should().Contain("ShowCfDialog(\"Date Occurring\")");
        source.Should().Contain("case QuickAnalysisCommand.DuplicateValues:");
        source.Should().Contain("ShowCfDialog(\"Duplicate Values\")");
        source.Should().Contain("case QuickAnalysisCommand.Top10Percent:");
        source.Should().Contain("ShowCfDialog(\"Top 10%\")");
        source.Should().Contain("case QuickAnalysisCommand.Bottom10:");
        source.Should().Contain("ShowCfDialog(\"Bottom 10 Items\")");
        source.Should().Contain("case QuickAnalysisCommand.Bottom10Percent:");
        source.Should().Contain("ShowCfDialog(\"Bottom 10%\")");
        source.Should().Contain("case QuickAnalysisCommand.AboveAverage:");
        source.Should().Contain("ShowCfDialog(\"Above Average\")");
        source.Should().Contain("case QuickAnalysisCommand.BelowAverage:");
        source.Should().Contain("ShowCfDialog(\"Below Average\")");
    }

    [Fact]
    public void QuickAnalysisMenu_MoreChartsReusesInsertChartDialogPath()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("case QuickAnalysisCommand.MoreCharts:");
        source.Should().Contain("InsertChartPickerBtn_Click(sender, e);");
    }

    [Fact]
    public void QuickAnalysisMenu_RoutesExpandedTotalsGallery()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("case QuickAnalysisCommand.PercentTotal:");
        source.Should().Contain("case QuickAnalysisCommand.RunningTotal:");
        source.Should().Contain("QuickAnalysisTotalsPlanner.BuildPercentTotalEdits");
        source.Should().Contain("QuickAnalysisTotalsPlanner.BuildRunningTotalEdits");
    }

    [Fact]
    public void BorderGallery_ExposesExpandedPresetsAndUsesReusablePlanners()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

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
            xaml.ShouldContainLocalizedAttribute("Header", label);

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
            xaml.Should().Contain($"Click=\"{handler}\"");
            source.Should().Contain(handler);
        }

        source.Should().Contain("ApplyRangeBorderPreset");
        source.Should().Contain("new CompositeWorkbookCommand(title, commands)");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("new CreateTableDialog");
        source.Should().Contain("new CreateStyledStructuredTableCommand(");
        source.Should().Contain("TableStyleGalleryPlanner.GetOption(variant, _workbook.Theme)");
        source.Should().NotContain("new CreateStructuredTableCommand(");
        source.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(dialog.Result.Range, sheetId)");
        source.Should().Contain("tableStyle.Banding");
    }

    [Fact]
    public void CellStyleMenu_UsesActiveWorkbookThemeForPresetPlanning()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme)");
    }

    [Fact]
    public void DrawGradientAndEffectsButtons_ExposeStableAutomationMetadata()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

        xaml.Should().Contain("AutomationProperties.AutomationId=\"DrawShapeGradientButton\"");
        xaml.ShouldContainLocalizedAttribute("AutomationProperties.HelpText", "Open gradient fill controls for the selected shape.");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DrawShapeEffectsButton\"");
        xaml.ShouldContainLocalizedAttribute("AutomationProperties.HelpText", "Choose no effect, shadow, inner shadow, reflection, glow, or soft edges for the selected shape.");
        xaml.Should().Contain("Click=\"ObjectGradientBtn_Click\"");
        xaml.Should().Contain("Click=\"ObjectEffectsBtn_Click\"");
    }

    [Fact]
    public void CollapsedRibbonOverflowCommands_ReturnFocusToVisibleGroupButton()
    {
        var adaptiveSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.RibbonAdaptive.cs"));

        adaptiveSource.Should().Contain("FocusCollapsedRibbonMenuPlacementTarget(item)");
        adaptiveSource.Should().Contain("private static void FocusCollapsedRibbonMenuPlacementTarget(MenuItem item)");
        adaptiveSource.Should().Contain("contextMenu.PlacementTarget is UIElement placementTarget");
        adaptiveSource.Should().Contain("placementTarget.Focus();");
    }
}
