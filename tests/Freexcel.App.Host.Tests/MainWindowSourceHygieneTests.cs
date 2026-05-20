using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowSourceHygieneTests
{
    [Fact]
    public void ViewportAndScrollbarController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var viewportSourcePath = Path.Combine(appHostDirectory, "MainWindow.Viewport.cs");

        File.Exists(viewportSourcePath).Should().BeTrue();
        var viewportSource = File.ReadAllText(viewportSourcePath);

        mainSource.Should().NotContain("private void UpdateViewport()");
        mainSource.Should().NotContain("private ViewportModel CreateViewport(");
        mainSource.Should().NotContain("private void EnsureCellVisible(");
        mainSource.Should().NotContain("private void SheetGrid_MouseWheel(");
        mainSource.Should().NotContain("private void Scroll_ValueChanged(");

        viewportSource.Should().Contain("private void UpdateViewport()");
        viewportSource.Should().Contain("private ViewportModel CreateViewport(");
        viewportSource.Should().Contain("private void EnsureCellVisible(");
        viewportSource.Should().Contain("private void SheetGrid_MouseWheel(");
        viewportSource.Should().Contain("private void Scroll_ValueChanged(");
    }

    [Fact]
    public void BackstageAndFileController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var backstageSourcePath = Path.Combine(appHostDirectory, "MainWindow.Backstage.cs");

        File.Exists(backstageSourcePath).Should().BeTrue();
        var backstageSource = File.ReadAllText(backstageSourcePath);

        mainSource.Should().NotContain("private void ShowStartScreen()");
        mainSource.Should().NotContain("private void UpdateSsRecentList(");
        mainSource.Should().NotContain("private async Task OpenFileAsync(");
        mainSource.Should().NotContain("private async void OpenButton_Click(");
        mainSource.Should().NotContain("private bool SaveWorkbookWithDialog()");

        backstageSource.Should().Contain("private void ShowStartScreen()");
        backstageSource.Should().Contain("private void UpdateSsRecentList(");
        backstageSource.Should().Contain("private async Task OpenFileAsync(");
        backstageSource.Should().Contain("private async void OpenButton_Click(");
        backstageSource.Should().Contain("private bool SaveWorkbookWithDialog()");
        backstageSource.Should().Contain("private void SaveAsButton_Click(");
    }

    [Fact]
    public void BackstageSaveAs_ForcesSaveDialogInsteadOfExistingPathSave()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        xaml.Should().Contain("Content=\"Save As\"");
        xaml.Should().Contain("Click=\"SaveAsButton_Click\"");
        backstageSource.Should().Contain("private void SaveAsButton_Click(object sender, RoutedEventArgs e) =>");
        backstageSource.Should().Contain("SaveWorkbookWithDialog();");
    }

    [Fact]
    public void SheetTabsController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var sheetTabsSourcePath = Path.Combine(appHostDirectory, "MainWindow.SheetTabs.cs");

        File.Exists(sheetTabsSourcePath).Should().BeTrue();
        var sheetTabsSource = File.ReadAllText(sheetTabsSourcePath);

        mainSource.Should().NotContain("private void RefreshSheetTabs()");
        mainSource.Should().NotContain("private void SheetTab_MouseLeftButtonDown(");
        mainSource.Should().NotContain("private void UpdateSheetTabNavigation()");
        mainSource.Should().NotContain("private void RenameSheetFromTab(");
        mainSource.Should().NotContain("private void MoveSheetTab(");

        sheetTabsSource.Should().Contain("private void RefreshSheetTabs()");
        sheetTabsSource.Should().Contain("private void SheetTab_MouseLeftButtonDown(");
        sheetTabsSource.Should().Contain("private void UpdateSheetTabNavigation()");
        sheetTabsSource.Should().Contain("private void RenameSheetFromTab(");
        sheetTabsSource.Should().Contain("private void MoveSheetTab(");
    }

    [Fact]
    public void ViewWindowAndZoomController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
    public void DrawingAndPictureController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
    public void PrintAndExportController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var printSourcePath = Path.Combine(appHostDirectory, "MainWindow.PrintExport.cs");

        File.Exists(printSourcePath).Should().BeTrue();
        var printSource = File.ReadAllText(printSourcePath);

        mainSource.Should().NotContain("private void PrintButton_Click(");
        mainSource.Should().NotContain("private void ExportPdfButton_Click(");
        mainSource.Should().NotContain("private bool ExportAsPdf(");
        mainSource.Should().NotContain("private bool ExportAsXps(");

        printSource.Should().Contain("private void PrintButton_Click(");
        printSource.Should().Contain("private void ExportPdfButton_Click(");
        printSource.Should().Contain("private bool ExportAsPdf(");
        printSource.Should().Contain("private bool ExportAsXps(");
    }

    [Fact]
    public void RibbonSurfaceController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var ribbonSourcePath = Path.Combine(appHostDirectory, "MainWindow.Ribbon.cs");

        File.Exists(ribbonSourcePath).Should().BeTrue();
        var ribbonSource = File.ReadAllText(ribbonSourcePath);

        mainSource.Should().NotContain("private void UpdateRibbonCompactMode(");
        mainSource.Should().NotContain("private void NormalizeRibbonSurface(");
        mainSource.Should().NotContain("private void NormalizeExistingRibbonIconText(");
        mainSource.Should().NotContain("private void ApplyToolbarDropdownWhiteBackgrounds(");
        mainSource.Should().NotContain("private static FrameworkElement CreateRibbonCommandContent(");

        ribbonSource.Should().Contain("private void UpdateRibbonCompactMode(");
        ribbonSource.Should().Contain("private void NormalizeRibbonSurface(");
        ribbonSource.Should().Contain("private void NormalizeExistingRibbonIconText(");
        ribbonSource.Should().Contain("private void ApplyToolbarDropdownWhiteBackgrounds(");
        ribbonSource.Should().Contain("private static FrameworkElement CreateRibbonCommandContent(");
    }

    [Fact]
    public void PageLayoutCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
    public void QuickAnalysisController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var dataSourcePath = Path.Combine(appHostDirectory, "MainWindow.DataCommands.cs");

        File.Exists(dataSourcePath).Should().BeTrue();
        var dataSource = File.ReadAllText(dataSourcePath);

        mainSource.Should().NotContain("private void GetDataBtn_Click(");
        mainSource.Should().NotContain("private void TextToColumnsBtn_Click(");
        mainSource.Should().NotContain("private void AdvancedFilterBtn_Click(");
        mainSource.Should().NotContain("private void ScenariosBtn_Click(");
        mainSource.Should().NotContain("private void DataTableBtn_Click(");

        dataSource.Should().Contain("private void GetDataBtn_Click(");
        dataSource.Should().Contain("private void TextToColumnsBtn_Click(");
        dataSource.Should().Contain("private void AdvancedFilterBtn_Click(");
        dataSource.Should().Contain("private void ScenariosBtn_Click(");
        dataSource.Should().Contain("private void DataTableBtn_Click(");
    }

    [Fact]
    public void ReviewProtectionShareCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
    public void HomeFormattingCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var cellsSourcePath = Path.Combine(appHostDirectory, "MainWindow.CellsCommands.cs");

        File.Exists(cellsSourcePath).Should().BeTrue();
        var cellsSource = File.ReadAllText(cellsSourcePath);

        mainSource.Should().NotContain("private void InsertPickerBtn_Click(");
        mainSource.Should().NotContain("private void InsertCellsMenuItem_Click(");
        mainSource.Should().NotContain("private void FormatAutoRowMenuItem_Click(");
        mainSource.Should().NotContain("private IWorkbookCommand CreateAutoFitRowHeightCommand(");
        mainSource.Should().NotContain("private void FormatLockCellMenuItem_Click(");

        cellsSource.Should().Contain("private void InsertPickerBtn_Click(");
        cellsSource.Should().Contain("private void InsertCellsMenuItem_Click(");
        cellsSource.Should().Contain("private void FormatAutoRowMenuItem_Click(");
        cellsSource.Should().Contain("private IWorkbookCommand CreateAutoFitRowHeightCommand(");
        cellsSource.Should().Contain("private void FormatLockCellMenuItem_Click(");
    }

    [Fact]
    public void HomeEditingCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
    public void WorksheetContextMenuController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var contextMenuSourcePath = Path.Combine(appHostDirectory, "MainWindow.WorksheetContextMenu.cs");

        File.Exists(contextMenuSourcePath).Should().BeTrue();
        var contextMenuSource = File.ReadAllText(contextMenuSourcePath);

        mainSource.Should().NotContain("private void OnGridContextMenuRequested(");
        mainSource.Should().NotContain("private void ExecuteWorksheetContextMenuAction(");
        mainSource.Should().NotContain("private void OpenKeyboardContextMenu(");

        contextMenuSource.Should().Contain("private void OnGridContextMenuRequested(");
        contextMenuSource.Should().Contain("private void ExecuteWorksheetContextMenuAction(");
        contextMenuSource.Should().Contain("private void OpenKeyboardContextMenu(");
        contextMenuSource.Should().Contain("WorksheetContextMenuPlanner.BuildCommands()");
        contextMenuSource.Should().Contain("MenuKeyTipAssigner.AssignUniqueKeyTips");
    }

    [Fact]
    public void MainWindow_MergesVisualRefreshResourceDictionaries()
    {
        var mainWindowPath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml");
        var appHostDirectory = Directory.GetParent(mainWindowPath)!.FullName;
        var xaml = File.ReadAllText(mainWindowPath);

        File.Exists(Path.Combine(appHostDirectory, "Resources", "ThemeResources.xaml")).Should().BeTrue();
        File.Exists(Path.Combine(appHostDirectory, "Resources", "IconResources.xaml")).Should().BeTrue();
        xaml.Should().Contain("Source=\"Resources/ThemeResources.xaml\"");
        xaml.Should().Contain("Source=\"Resources/IconResources.xaml\"");
    }

    [Fact]
    public void RibbonIconSet_UsesSharedIconSlotsAndDecorator()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var iconResources = File.ReadAllText(Path.Combine(appHostDirectory, "Resources", "IconResources.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Ribbon.cs"));
        var planner = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RibbonCommandPresentationPlanner.cs"));

        File.Exists(Path.Combine(appHostDirectory, "RibbonIconFactory.cs")).Should().BeTrue();
        iconResources.Should().Contain("FreexcelRibbonLargeIconSlot");
        iconResources.Should().Contain("FreexcelRibbonSmallIconSlot");
        iconResources.Should().Contain("FreexcelRibbonLargeLabel");
        iconResources.Should().Contain("FreexcelRibbonSmallLabel");

        source.Should().Contain("CreateRibbonCommandContent(commandName, label, layoutKind)");
        source.Should().Contain("NormalizeExistingRibbonIconText();");
        source.Should().Contain("GetRibbonIconAccentBrushes");
        source.Should().Contain("RibbonIconFactory.CreateIcon(icon, iconSize, glyphBrush)");
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
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("NumberFormatOptions.Select(option => option.Label)");
        source.Should().Contain("NumberFormatOptions[NumberFormatBox.SelectedIndex].Code");
        source.Should().Contain("Accounting ($#,##0.00)");
        source.Should().Contain("Fraction (# ?/?)");
        source.Should().Contain("Scientific (0.00E+00)");
        source.Should().Contain("\"# ?/?\"");
        source.Should().Contain("\"0.00E+00\"");
    }

    [Fact]
    public void ArrangeAllMenu_ReflectsStoredWorkbookArrangement()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ViewCommands.cs"));

        xaml.Should().Contain("Opened=\"ArrangeAllContextMenu_Opened\"");
        xaml.Should().Contain("IsCheckable=\"True\"");
        source.Should().Contain("ArrangeAllContextMenu_Opened");
        source.Should().Contain("ArrangeAllMenuPlanner.IsChecked(item.Tag, _workbook.WindowArrangement)");
        source.Should().Contain("ArrangeAllMenuPlanner.TryParseArrangement");
    }

    [Fact]
    public void SplitRibbonCommand_ReflectsActiveSplitState()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Viewport.cs"));

        xaml.Should().Contain("<ToggleButton x:Name=\"SplitViewBtn\"");
        xaml.Should().Contain("Style=\"{StaticResource RibbonToggleBtn}\"");
        source.Should().Contain("SplitViewBtn.IsChecked = sheet?.SplitRow is not null || sheet?.SplitColumn is not null");
    }

    [Fact]
    public void QuickAccessToolbar_UsesVectorIcons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var iconResources = File.ReadAllText(Path.Combine(appHostDirectory, "Resources", "IconResources.xaml"));

        xaml.Should().Contain("x:Name=\"SaveQatBtn\"");
        xaml.Should().Contain("<local:RibbonIcon Kind=\"Save\"");
        xaml.Should().Contain("<local:RibbonIcon Kind=\"Undo\"");
        xaml.Should().Contain("<local:RibbonIcon Kind=\"Redo\"");
        xaml.Should().NotContain("FreexcelQatOnAccentIcon");
        iconResources.Should().NotContain("FreexcelQatIcon");
        xaml.Should().NotContain("Content=\"💾\"");
        xaml.Should().NotContain("Content=\"↩\"");
        xaml.Should().NotContain("Content=\"↪\"");
    }

    [Fact]
    public void ToolbarIcons_DoNotUseFontGlyphAssets()
    {
        var mainWindowPath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml");
        var appHostDirectory = Path.GetDirectoryName(mainWindowPath)!;
        var xaml = File.ReadAllText(mainWindowPath);
        var iconResources = File.ReadAllText(Path.Combine(appHostDirectory, "Resources", "IconResources.xaml"));

        xaml.Should().NotContain("Segoe MDL2 Assets");
        xaml.Should().NotContain("RibbonIconGlyph");
        xaml.Should().NotContain("FreexcelQatOnAccentIcon");
        iconResources.Should().NotContain("Segoe MDL2 Assets");
        iconResources.Should().NotContain("FreexcelRibbonGlyph");
    }

    [Fact]
    public void MainWindow_UsesVisibleFreexcelBrandingAndWindowIcon()
    {
        var mainWindowPath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml");
        var projectPath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "Freexcel.App.Host.csproj");
        var appHostDirectory = Directory.GetParent(mainWindowPath)!.FullName;
        var theme = File.ReadAllText(Path.Combine(appHostDirectory, "Resources", "ThemeResources.xaml"));
        var xaml = File.ReadAllText(mainWindowPath);
        var project = File.ReadAllText(projectPath);

        File.Exists(Path.Combine(appHostDirectory, "Resources", "Freexcel.ico")).Should().BeTrue();
        xaml.Should().Contain("Icon=\"Resources/Freexcel.ico\"");
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
        xaml.Should().Contain("Foreground=\"#155C38\"");
        xaml.Should().Contain("Margin=\"0,-3,0,0\"");
        xaml.Should().Contain("Margin=\"0,-1,0,0\"");
        xaml.Should().Contain("Margin=\"-1,-2,0,0\"");
        xaml.Should().Contain("Margin=\"1,-2,0,0\"");
        xaml.Should().Contain("Margin=\"0,-2,0,0\"");
        xaml.Should().NotContain("<Image Source=\"Resources/Freexcel.ico\"");
        xaml.Should().NotContain("<TextBlock Text=\"F\" Foreground=\"{StaticResource FreexcelGreenBrush}\"");
        theme.Should().Contain("x:Key=\"FreexcelTitleBarBrush\"");
        xaml.Should().Contain("Background=\"{StaticResource FreexcelTitleBarBrush}\"");
        project.Should().Contain("<ApplicationIcon>Resources\\Freexcel.ico</ApplicationIcon>");
    }

    [Fact]
    public void SheetTabs_UseContextualNavigationArrowsInsteadOfAHorizontalScrollbar()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.SheetTabs.cs"));
        var navigationStart = source.IndexOf("private void UpdateSheetTabNavigation()", StringComparison.Ordinal);
        var navigationEnd = source.IndexOf("private void BringCurrentSheetTabIntoView()", navigationStart, StringComparison.Ordinal);
        navigationStart.Should().BeGreaterThanOrEqualTo(0);
        navigationEnd.Should().BeGreaterThan(navigationStart);
        var navigationSource = source[navigationStart..navigationEnd];

        xaml.Should().Contain("x:Name=\"SheetNavLeftBtn\" Grid.Column=\"0\"");
        xaml.Should().Contain("x:Name=\"SheetTabsScroller\" Grid.Column=\"1\"");
        xaml.Should().Contain("HorizontalScrollBarVisibility=\"Hidden\"");
        xaml.Should().Contain("ScrollChanged=\"SheetTabsScroller_ScrollChanged\"");
        xaml.Should().Contain("SizeChanged=\"SheetTabsScroller_SizeChanged\"");
        xaml.Should().Contain("x:Name=\"SheetNavRightBtn\" Grid.Column=\"2\"");
        xaml.Should().Contain("Visibility=\"Hidden\"");
        xaml.Should().NotContain("HorizontalScrollBarVisibility=\"Auto\"\r\n                              VerticalScrollBarVisibility=\"Disabled\">\r\n                    <StackPanel Orientation=\"Horizontal\">");

        source.Should().Contain("UpdateSheetTabNavigation();");
        navigationSource.Should().Contain("SheetNavLeftBtn.Visibility");
        navigationSource.Should().Contain("SheetNavRightBtn.Visibility");
        navigationSource.Should().Contain(": Visibility.Hidden;");
        navigationSource.Should().NotContain(": Visibility.Collapsed;");
        source.Should().NotContain("SheetTabsScroller.HorizontalOffset - 80");
        source.Should().NotContain("SheetTabsScroller.HorizontalOffset + 80");
    }

    [Fact]
    public void MainWindow_DoesNotKeepLegacyZoomConversionHelpers()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().NotContain("SliderToZoomPct(");
        source.Should().NotContain("ZoomPctToSlider(");
    }

    [Fact]
    public void PersistentFormatPainter_UsesPreviewMouseDownSoButtonDoubleClickCannotBeOverwrittenByClick()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.FormatPainter.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.FormatPainter.cs"));
        var mainSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("private SheetId? _formatPainterSourceSheetId;");
        source.Should().Contain("private GridRange? _formatPainterSourceRange;");
        source.Should().Contain("private bool _formatPainterTargetSelectionActive;");
        source.Should().Contain("TryApplyFormatPainter(GridRange targetRange)");
        source.Should().Contain("_formatPainterSourceRange = range;");
        source.Should().Contain("var targetSheetIds = CurrentGroupedEditSheetIds();");
        source.Should().Contain("FormatPainterCommandFactory.Create(_workbook, sourceSheet, sourceRange, targetRange)");
        source.Should().Contain("new CompositeWorkbookCommand(\"Format Painter\", targetSheetIds.Select(CreateCommand).ToList())");
        mainSource.Should().Contain("SheetGrid.SelectedRange is { } selectedRange");
        mainSource.Should().Contain("selectedRange.Contains(newAddr)");
        mainSource.Should().Contain("TryApplyFormatPainter(selectedRange)");
        source.Should().NotContain("var targetRange = new GridRange(addr, addr);");
    }

    [Fact]
    public void AutoFitMenuHandlers_UsePlannerAndPerTargetExplicitSizes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.CellsCommands.cs"));
        var planner = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFitPlanner.cs"));

        source.Should().Contain("AutoFitPlanner.PlanRowHeights");
        source.Should().Contain("AutoFitPlanner.PlanColumnWidths");
        source.Should().Contain("new SetRowHeightCommand(sheetId, plans[0].Index, plans[0].Index, plans[0].Size)");
        source.Should().Contain("new SetColumnWidthCommand(sheetId, plans[0].Index, plans[0].Index, plans[0].Size)");
        source.Should().Contain("new SetRowHeightCommand(sheetId, plan.Index, plan.Index, plan.Size)");
        source.Should().Contain("new SetColumnWidthCommand(sheetId, plan.Index, plan.Index, plan.Size)");
        source.Should().NotContain("return new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, height)");
        source.Should().NotContain("return new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, width)");
        planner.Should().Contain("AutoFitSizingService.EstimateRowHeight");
        planner.Should().Contain("AutoFitSizingService.EstimateColumnWidth");
        source.Should().NotContain("new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, height: null)");
        source.Should().NotContain("new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, width: null)");
    }

    [Fact]
    public void AdvancedChartFamilies_ArePresentedAsDeferredInsteadOfAuthored()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        source.Should().Contain("ShowDeferredChartFamilyMessage");
        source.Should().Contain("retained when opening XLSX files");
        source.Should().NotContain("InsertChartOfType(ChartType.Surface)");
        source.Should().NotContain("InsertChartOfType(ChartType.Treemap)");
        source.Should().NotContain("InsertChartOfType(ChartType.Sunburst)");
        source.Should().NotContain("InsertChartOfType(ChartType.Histogram)");
        source.Should().NotContain("InsertChartOfType(ChartType.Pareto)");
        source.Should().NotContain("InsertChartOfType(ChartType.BoxAndWhisker)");
        source.Should().NotContain("InsertChartOfType(ChartType.Waterfall)");
        source.Should().NotContain("InsertChartOfType(ChartType.Funnel)");
        source.Should().NotContain("InsertChartOfType(ChartType.Map)");
        source.Should().NotContain("InsertChartOfType(ChartType.ThreeDColumn)");
        xaml.Should().Contain("Click=\"DeferredChartFamilyMenuItem_Click\"");
        xaml.Should().Contain("Surface");
        xaml.Should().Contain("Treemap");
        xaml.Should().Contain("Sunburst");
        xaml.Should().Contain("Histogram");
        xaml.Should().Contain("Pareto");
        xaml.Should().Contain("Box Plot");
        xaml.Should().Contain("Waterfall");
        xaml.Should().Contain("Funnel");
        xaml.Should().Contain("Map");
        xaml.Should().Contain("3D Column");
    }

    [Fact]
    public void ChartKeyboardShortcuts_UseSeparateEmbeddedAndChartSheetPaths()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.KeyboardCommands.cs"));

        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertEmbeddedChart, (_, _) => InsertEmbeddedChart())");
        source.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.InsertChartSheet, (_, _) => InsertChartSheet())");
    }

    [Fact]
    public void InsertPivotTable_NewWorksheetDestination_UsesUndoableCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("new AddPivotTableToNewWorksheetCommand(");
        source.Should().Contain("command.CreatedSheetId");
        source.Should().NotContain("New chart-style PivotTable sheets are tracked for Wave 2");
    }

    [Fact]
    public void WorksheetContextMenuPickFromDropDown_ReusesActiveDropdownPath()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("case WorksheetContextMenuAction.PickFromDropDown:");
        source.Should().Contain("OpenActiveDropdown();");
    }

    [Fact]
    public void WorksheetContextMenuQuickAnalysis_ReusesCtrlQPath()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("case WorksheetContextMenuAction.QuickAnalysis:");
        source.Should().Contain("ShowQuickAnalysisMenu();");
    }

    [Fact]
    public void WorksheetContextMenu_UsesAccessKeyHeaders()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("Header = command.AccessHeader");
    }

    [Fact]
    public void ThreadedCommentShortcut_UsesDistinctThreadedCommentWorkflow()
    {
        var keyboard = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.KeyboardCommands.cs"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ReviewCommands.cs"));

        keyboard.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewThreadedComment, ReviewNewThreadedCommentBtn_Click)");
        keyboard.Should().NotContain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.NewThreadedComment, ReviewNewCommentBtn_Click)");
        source.Should().Contain("private void ReviewNewThreadedCommentBtn_Click");
        source.Should().Contain("new SetThreadedCommentCommand(");
    }

    [Fact]
    public void WorksheetContextMenuNewComment_ReusesThreadedCommentWorkflow()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("case WorksheetContextMenuAction.NewComment:");
        source.Should().Contain("ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs());");
    }

    [Fact]
    public void ReviewCommentNavigation_IncludesThreadedComments()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("CommentNavigationPlanner.FormatCommentList(sheet.Comments, sheet.ThreadedComments)");
        source.Should().Contain("CommentNavigationPlanner.OrderedCommentAddresses(sheet.Comments, sheet.ThreadedComments)");
        source.Should().Contain("sheet.Comments.Count == 0 && sheet.ThreadedComments.Count == 0");
    }

    [Fact]
    public void QuickAnalysisMenu_UsesPlannerPreviewMetadataForHoverTooltips()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));
        var planner = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "QuickAnalysisPlanner.cs"));

        source.Should().Contain("ToolTip = option.PreviewText");
        planner.Should().Contain("QuickAnalysisPreviewKind");
    }

    [Fact]
    public void QuickAnalysisMenu_RendersPlannerVisualPreviewIcons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));
        var planner = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "QuickAnalysisPlanner.cs"));

        planner.Should().Contain("QuickAnalysisPreviewVisual");
        source.Should().Contain("QuickAnalysisPreviewIconFactory.Create(option.PreviewVisual)");
    }

    [Fact]
    public void QuickAnalysisMenu_UpdatesLiveHoverPreviewStatus()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("QuickAnalysisMenuItem_MouseEnter");
        source.Should().Contain("QuickAnalysisMenuItem_MouseLeave");
        source.Should().Contain("QuickAnalysisPlanner.BuildHoverPreview(range, option)");
        source.Should().Contain("StatusReadyText.Text = preview.StatusText");
        source.Should().Contain("StatusReadyText.Text = \"Ready\"");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_IsAnchoredToActiveHeaderCell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("PositionAutoFilterDialogAtActiveCell(dialog, activeCell);");
        source.Should().Contain("private void PositionAutoFilterDialogAtActiveCell");
        source.Should().Contain("TryGetCellOverlayRect(activeCell)");
        source.Should().Contain("SheetGrid.PointToScreen");
        source.Should().Contain("WindowStartupLocation.Manual");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_ReusesFullFilterDialogResultRouting()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("ApplyAutoFilterDialogResult(plan.Range, plan.FilterColumnOffset, dialog.Result, \"AutoFilter\")");
        source.Should().Contain("private bool ApplyAutoFilterDialogResult(");
        source.Should().Contain("FilterInputParser.TryParseTopBottom");
        source.Should().Contain("FilterInputParser.TryParseCriterion");
        source.Should().Contain("FilterInputParser.TryParseAverage");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_UsesExcelStyleMenuPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var dialog = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFilterDialog.cs"));

        source.Should().Contain("AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan)");
        source.Should().Contain("new AutoFilterDialog(menuPlan)");
        dialog.Should().Contain("AutoFilterMenuPlan menuPlan");
        dialog.Should().Contain("CriteriaSuggestions");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_ExposesCriteriaSuggestionPicker()
    {
        var dialog = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFilterDialog.cs"));

        dialog.Should().Contain("_criteriaSuggestionBox");
        dialog.Should().Contain("GetCriteriaSuggestions(menuPlan)");
        dialog.Should().Contain("_criteriaSuggestionBox.SelectionChanged");
    }

    [Fact]
    public void BorderGallery_ExposesExpandedPresetsAndUsesReusablePlanners()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        foreach (var label in new[]
        {
            "Bottom Double Border",
            "Inside Borders",
            "Thick Box Border",
            "Top and Bottom Border",
            "Top and Thick Bottom Border",
            "Top and Double Bottom Border",
            "Line Color",
            "Line Style",
            "Black",
            "Accent 1",
            "Dashed",
            "Dotted",
            "More Borders..."
        })
            xaml.Should().Contain($"Header=\"{label}\"");

        foreach (var handler in new[]
        {
            "BorderBottomDoubleMenuItem_Click",
            "BorderInsideMenuItem_Click",
            "BorderThickBoxMenuItem_Click",
            "BorderTopAndBottomMenuItem_Click",
            "BorderTopAndThickBottomMenuItem_Click",
            "BorderTopAndDoubleBottomMenuItem_Click",
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
        source.Should().Contain("BorderShortcutService.GetSingleBorderDiff");
        source.Should().Contain("BorderShortcutService.GetInsideBorderDiff");
        source.Should().Contain("BorderShortcutService.GetTopAndBottomBorderDiff");
        source.Should().Contain("BorderShortcutService.GetOutlineBorderDiff");
    }

    [Fact]
    public void MainWindow_RoutesColorChoicesThroughColorPickerDialog()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().NotContain("input.Split(',')");
        source.Should().Contain("private bool TryShowColorPicker(");
        source.Should().Contain("new ColorPickerDialog");
        source.Should().Contain("TryShowColorPicker(\"Font Color\"");
        source.Should().Contain("TryShowColorPicker(\"Fill Color\"");
    }

    [Fact]
    public void SpellCheckWorkflow_RoutesDialogActionsThroughKnownCorrectionsPlan()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("SpellCheckService.PlanKnownCorrections(_workbook, _currentSheetId)");
        source.Should().Contain("SpellCheckDialogAction.ReplaceAll");
        source.Should().Contain("SpellCheckDialogAction.Ignore");
        source.Should().Contain("SpellCheckService.BuildCorrectionCellEdits(plan)");
        source.Should().NotContain("BuildSpellCheckEdits");
        source.Should().Contain("TryExecuteSpellCheckEdits");
        source.Should().Contain("new EditCellsCommand(_currentSheetId, edits)");
        source.Should().NotContain("TryExecuteEditCells(edits, \"Spell Check\")");
    }

    [Fact]
    public void FormatAsTable_CreatesStructuredTableMetadataAndBandingAsOneCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("new CreateTableDialog");
        source.Should().Contain("new CreateStyledStructuredTableCommand(");
        source.Should().Contain("TableStyleGalleryPlanner.GetOption(variant)");
        source.Should().NotContain("new CreateStructuredTableCommand(");
        source.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(dialog.Result.Range, sheetId)");
        source.Should().Contain("tableStyle.Banding");
    }

    [Fact]
    public void CellStyleMenu_UsesActiveWorkbookThemeForPresetPlanning()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme)");
    }

    [Fact]
    public void ExportWorkflow_SurfacesPlannedPdfAndXpsPaths()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PrintExport.cs"));

        source.Should().Contain("ExportAsPdf(request.Path, ExportPlanner.DescribeRequest(request), request.Options)");
        source.Should().Contain("ExportAsXps(request.Path, ExportPlanner.DescribeRequest(request), request.Options)");
        source.Should().Contain("var document = RenderExportDocument(options)");
        source.Should().Contain("var paginator = RenderExportPaginator(options)");
        source.Should().Contain("ExportPlanner.DescribeRequest(request)");
        source.Should().Contain("OpenExportedFile(request.ActualPath)");
        source.Should().NotContain("ExportPdfFallbackAsXps");
    }

    [Fact]
    public void CtrlP_RoutesThroughBackstagePrintEntryPoint()
    {
        var keyboardSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.KeyboardCommands.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        backstageSource.Should().Contain("private void OpenPrintBackstage()");
        backstageSource.Should().Contain("SsPrintNavBtn.Focus();");
        keyboardSource.Should().Contain("KeyboardCommandShortcut.OpenPrintPreview, (_, _) => OpenPrintBackstage()");
        keyboardSource.Should().NotContain("KeyboardCommandShortcut.OpenPrintPreview, PrintButton_Click");
        xaml.Should().Contain("x:Name=\"SsPrintNavBtn\"");
        xaml.Should().Contain("local:RibbonTooltip.Description=\"Open the print preview and native print dialog for the rendered worksheet.\"");
    }

    [Fact]
    public void RemainingStatusWorkflows_OpenNamedDialogsInsteadOfMessageBoxes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var pageLayoutSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PageLayout.cs"));
        var dataSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataCommands.cs"));
        var reviewSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ReviewCommands.cs"));

        pageLayoutSource.Should().Contain("new PageBreakDialog");
        dataSource.Should().Contain("new GoalSeekStatusDialog");
        reviewSource.Should().Contain("new WorkbookStatisticsDialog");
        reviewSource.Should().Contain("new AccessibilityCheckerDialog");
    }

    [Fact]
    public void ConditionalFormattingEllipsisCommands_UseRuleFamilyDialogFactory()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));

        source.Should().Contain("ConditionalFormatDialogFactory.Create(ruleType, range)");
        source.Should().NotContain("new ConditionalFormatDialog(ruleType, range)");
    }

    [Fact]
    public void PivotTableDesignCommands_OpenOptionsDialogInsteadOfCyclingLayoutState()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        xaml.Should().Contain("local:RibbonTooltip.Description=\"Open PivotTable layout and style options.");
        xaml.Should().NotContain("Cycle grand totals");
        xaml.Should().NotContain("Cycle subtotals");
        xaml.Should().NotContain("Cycle PivotTable style gallery choices.");
        source.Should().Contain("new PivotTableOptionsDialog(pivotTable)");
        source.Should().Contain("ApplyPivotOptions(pivotTable, dialog.Result)");
        source.Should().NotContain("var reportLayout = pivotTable.ReportLayout switch");
        source.Should().NotContain("var styleName = pivotTable.StyleName switch");
    }

    [Fact]
    public void ChartFormattingCommands_OpenExplicitFormatDialogs()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("new ChartDataLabelsDialog(chart)");
        source.Should().Contain("new ChartTrendlineOptionsDialog(chart)");
        source.Should().Contain("new ChartAxisFormatDialog(chart, useXAxis)");
        source.Should().Contain("new ChartSeriesFormatDialog(chart, ChartOptionCycler.GetSeriesCount(chart))");
        source.Should().Contain("ApplyChartLayoutDialogResult(\"Format Data Labels\"");
        source.Should().Contain("ApplyChartLayoutDialogResult(\"Format Trendline\"");
        source.Should().Contain("ApplyChartLayoutDialogResult(useXAxis ? \"Format X Axis\" : \"Format Y Axis\"");
        source.Should().Contain("ApplyChartLayoutDialogResult(\"Format Data Series\"");
    }

    [Fact]
    public void PictureCropRibbon_OffersCropAndResetCropMenuActions()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Drawing.cs"));

        xaml.Should().Contain("Header=\"Crop...\"");
        xaml.Should().Contain("Header=\"Reset Crop\"");
        xaml.Should().Contain("Click=\"PictureCropDialogMenuItem_Click\"");
        xaml.Should().Contain("Click=\"PictureResetCropMenuItem_Click\"");
        source.Should().Contain("PictureResetCropMenuItem_Click");
        source.Should().Contain("new SetPictureCropCommand(");
        source.Should().Contain("0, 0, 0, 0");
    }
}
