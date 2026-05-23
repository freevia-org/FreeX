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
        mainSource.Should().NotContain("private async Task<bool> SaveWorkbookWithDialogAsync()");

        backstageSource.Should().Contain("private void ShowStartScreen()");
        backstageSource.Should().Contain("private void UpdateSsRecentList(");
        backstageSource.Should().Contain("private async Task OpenFileAsync(");
        backstageSource.Should().Contain("private async void OpenButton_Click(");
        backstageSource.Should().Contain("private async Task<bool> SaveWorkbookWithDialogAsync()");
        backstageSource.Should().Contain("private async void SaveAsButton_Click(");
    }

    [Fact]
    public void BackstageSaveAs_ForcesSaveDialogInsteadOfExistingPathSave()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        xaml.Should().Contain("Content=\"Save _As\"");
        xaml.Should().Contain("Click=\"SaveAsButton_Click\"");
        backstageSource.Should().Contain("private async void SaveAsButton_Click(object sender, RoutedEventArgs e) =>");
        backstageSource.Should().Contain("await SaveWorkbookWithDialogAsync();");
    }

    [Fact]
    public void BackstageOpenAndSave_UseFormatDescriptorRegistry()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        backstageSource.Should().Contain("FileDialogFilterBuilder.BuildOpenFilter(_fileAdapters)");
        backstageSource.Should().Contain("FileDialogFilterBuilder.BuildSaveFilter(_fileAdapters)");
        backstageSource.Should().Contain("FileDialogFilterBuilder.FindOpenAdapter(_fileAdapters, ext, out var format)");
        backstageSource.Should().Contain("_currentFilePath = result.OpenedAsTemplate ? null : path;");
    }

    [Fact]
    public void BackstageOpen_FocusesHomeNavigationForKeyboardUsers()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        backstageSource.Should().Contain("StartScreenOverlay.Visibility = Visibility.Visible;");
        backstageSource.Should().Contain("FocusBackstageHomeNavigation();");
        backstageSource.Should().Contain("private void FocusBackstageHomeNavigation()");
        backstageSource.Should().Contain("SsHomeNavBtn.Focus();");
        backstageSource.Should().Contain("Keyboard.Focus(SsHomeNavBtn);");
    }

    [Fact]
    public void BackstageSidebar_UpDownKeysMoveThroughNavigation()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        xaml.Should().Contain("PreviewKeyDown=\"StartScreenOverlay_PreviewKeyDown\"");
        xaml.Should().Contain("x:Name=\"StartScreenSidebar\"");
        backstageSource.Should().Contain("private void StartScreenOverlay_PreviewKeyDown(object sender, KeyEventArgs e)");
        backstageSource.Should().Contain("IsDescendantOf(focusedElement, StartScreenSidebar)");
        backstageSource.Should().Contain("e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)");
        backstageSource.Should().Contain("FocusNavigationDirection.Previous");
        backstageSource.Should().Contain("FocusNavigationDirection.Next");
        backstageSource.Should().Contain("focusedElement.MoveFocus(new TraversalRequest(direction));");
    }

    [Fact]
    public void BackstageSidebar_HomeEndKeysMoveToNavigationEdges()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        backstageSource.Should().Contain("e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)");
        backstageSource.Should().Contain("Key.Home => FocusNavigationDirection.First");
        backstageSource.Should().Contain("Key.End => FocusNavigationDirection.Last");
    }

    [Fact]
    public void BackstageOverlay_CyclesTabFocusWithinOverlay()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        xaml.Should().Contain("x:Name=\"StartScreenOverlay\"");
        xaml.Should().Contain("KeyboardNavigation.TabNavigation=\"Cycle\"");
        xaml.Should().Contain("KeyboardNavigation.ControlTabNavigation=\"Cycle\"");
    }

    [Fact]
    public void BackstageContextMenu_UsesFocusedBackstageElementBeforeWorksheetFallback()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        selectionSource.Should().Contain("if (commandShortcut == KeyboardCommandShortcut.OpenContextMenu && TryOpenFocusedBackstageContextMenu())");
        selectionSource.IndexOf("TryOpenFocusedBackstageContextMenu()", StringComparison.Ordinal)
            .Should().BeLessThan(selectionSource.IndexOf("ExecuteCommandShortcut(commandShortcut, sender, e);", StringComparison.Ordinal));
        backstageSource.Should().Contain("private bool TryOpenFocusedBackstageContextMenu()");
        backstageSource.Should().Contain("!IsStartScreenVisible()");
        backstageSource.Should().Contain("Keyboard.FocusedElement is not FrameworkElement focusedElement");
        backstageSource.Should().Contain("!IsInsideStartScreenOverlay(focusedElement)");
        backstageSource.Should().Contain("focusedElement.ContextMenu is not { } menu");
        backstageSource.Should().Contain("menu.PlacementTarget = focusedElement;");
        backstageSource.Should().Contain("menu.IsOpen = true;");
    }

    [Fact]
    public void BackstageContextMenu_FocusesFirstEnabledMenuItem()
    {
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        backstageSource.Should().Contain("menu.Opened += BackstageContextMenu_Opened;");
        backstageSource.Should().Contain("private static void BackstageContextMenu_Opened(object sender, RoutedEventArgs e)");
        backstageSource.Should().Contain("menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled)");
        backstageSource.Should().Contain("Keyboard.Focus(firstEnabledItem);");
    }

    [Fact]
    public void BackstageF6_CyclesWithinOverlayBeforeWorkbookShellFallback()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));

        const string backstageRoute = "if (IsStartScreenVisible() && TryHandleBackstageShellFocusCycle";
        const string workbookFallback = "ExecuteCommandShortcut(commandShortcut, this, e);";

        selectionSource.Should().Contain(backstageRoute);
        selectionSource.IndexOf(backstageRoute, StringComparison.Ordinal)
            .Should()
            .BeLessThan(selectionSource.IndexOf(workbookFallback, StringComparison.Ordinal));
        backstageSource.Should().Contain("private bool TryHandleBackstageShellFocusCycle(bool reverse)");
        backstageSource.Should().Contain("IsInsideStartScreenOverlay(focusedElement)");
        backstageSource.Should().Contain("StartScreenOverlay.MoveFocus");
    }

    [Fact]
    public void GetData_IncludesDelimitedTextAdapters()
    {
        var dataCommandsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataCommands.cs"));

        dataCommandsSource.Should().Contain("\".csv\", \".txt\", \".tsv\", \".tab\"");
    }

    [Fact]
    public void StandaloneAltKeyTips_DoNotRouteAltKeyChords()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var altKeyTipSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.AltKeyTips.cs"));

        selectionSource.Should().NotContain("TryHandleTopLevelRibbonKeyTip(keyTip)");
        selectionSource.Should().NotContain("TryInvokeTopLevelQatKeyTip(qatKeyTip)");
        altKeyTipSource.Should().Contain("WM_SYSKEYDOWN");
        altKeyTipSource.Should().Contain("StandaloneAltKeyTipTracker.IsAltVirtualKey");
        altKeyTipSource.Should().Contain("_standaloneAltKeyTipTracker.CancelStandaloneAltCandidate();");
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
        contextMenuSource.Should().Contain("WorksheetContextMenuPlanner.BuildCommands(targetKind, state)");
        contextMenuSource.Should().Contain("MenuKeyTipAssigner.AssignUniqueKeyTips");
    }

    [Fact]
    public void OutlineGroupingCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
    public void SelectionAndGridInteractionController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var selectionSourcePath = Path.Combine(appHostDirectory, "MainWindow.Selection.cs");

        File.Exists(selectionSourcePath).Should().BeTrue();
        var selectionSource = File.ReadAllText(selectionSourcePath);

        mainSource.Should().NotContain("private void SelectRow(");
        mainSource.Should().NotContain("private void SheetGrid_MouseDown(");
        mainSource.Should().NotContain("private void MainWindow_TextInput(");
        mainSource.Should().NotContain("private void MainWindow_KeyDown(");
        mainSource.Should().NotContain("private void SetActiveCell(");
        mainSource.Should().NotContain("private void SelectCurrentRegionOrAll(");
        mainSource.Should().NotContain("private void AddOrMoveAdditionalSelection(");
        mainSource.Should().NotContain("private void SheetGrid_MouseMove(");
        mainSource.Should().NotContain("private void SheetGrid_MouseUp(");

        selectionSource.Should().Contain("private void SelectRow(");
        selectionSource.Should().Contain("private void SheetGrid_MouseDown(");
        selectionSource.Should().Contain("private void MainWindow_TextInput(");
        selectionSource.Should().Contain("private void MainWindow_KeyDown(");
        selectionSource.Should().Contain("private void SetActiveCell(");
        selectionSource.Should().Contain("private void SelectCurrentRegionOrAll(");
        selectionSource.Should().Contain("private void AddOrMoveAdditionalSelection(");
        selectionSource.Should().Contain("private void SheetGrid_MouseMove(");
        selectionSource.Should().Contain("private void SheetGrid_MouseUp(");
        selectionSource.Should().Contain("ExcelWorksheetNavigationPlanner");
    }

    [Fact]
    public void InlineEditingController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var editingSourcePath = Path.Combine(appHostDirectory, "MainWindow.Editing.cs");
        var dropdownSourcePath = Path.Combine(appHostDirectory, "MainWindow.EditingDropdowns.cs");

        File.Exists(editingSourcePath).Should().BeTrue();
        File.Exists(dropdownSourcePath).Should().BeTrue();
        var editingSource = File.ReadAllText(editingSourcePath);
        var dropdownSource = File.ReadAllText(dropdownSourcePath);

        mainSource.Should().NotContain("private void EnterEditMode(");
        mainSource.Should().NotContain("private void ShowInlineEditor(");
        mainSource.Should().NotContain("private void RefreshValidationDropdown(");
        mainSource.Should().NotContain("private void OpenActiveDropdown(");
        mainSource.Should().NotContain("private void InlineEditor_KeyDown(");
        mainSource.Should().NotContain("private void FormulaBar_KeyDown(");
        mainSource.Should().NotContain("private bool CommitEdit(");
        mainSource.Should().NotContain("private bool TryCreateCellFromEntryText(");
        mainSource.Should().NotContain("private bool CommitPreparedEdits(");

        editingSource.Should().Contain("private void EnterEditMode(");
        editingSource.Should().Contain("private void ShowInlineEditor(");
        editingSource.Should().Contain("private void InlineEditor_KeyDown(");
        editingSource.Should().Contain("private void FormulaBar_KeyDown(");
        editingSource.Should().Contain("private bool CommitEdit(");
        editingSource.Should().Contain("private bool TryCreateCellFromEntryText(");
        editingSource.Should().Contain("private bool CommitPreparedEdits(");
        editingSource.Should().Contain("ExcelEditKeyPlanner");
        editingSource.Should().Contain("CellEntryParser");
        dropdownSource.Should().Contain("private void RefreshValidationDropdown(");
        dropdownSource.Should().Contain("private void OpenActiveDropdown(");
        dropdownSource.Should().Contain("AutoFilterDropdownPlanner");
        dropdownSource.Should().Contain("DataValidationService");
    }

    [Fact]
    public void InlineEditing_StartsWithCaretAtEndInsteadOfSelectingAll()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var editingSource = ReadEditingSource();

        editingSource.Should().NotContain("_inlineEditor.SelectAll();");
        editingSource.Should().Contain("_inlineEditor.CaretIndex = _inlineEditor.Text.Length;");
        editingSource.Should().Contain("_inlineEditor.SelectionLength = 0;");
    }

    [Fact]
    public void GridStatusAndResizeController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var gridSourcePath = Path.Combine(appHostDirectory, "MainWindow.GridStatus.cs");

        File.Exists(gridSourcePath).Should().BeTrue();
        var gridSource = File.ReadAllText(gridSourcePath);

        mainSource.Should().NotContain("private void RefreshStatusBar(");
        mainSource.Should().NotContain("private void OnColumnResizing(");
        mainSource.Should().NotContain("private void OnColumnResized(");
        mainSource.Should().NotContain("private void OnRowResizing(");
        mainSource.Should().NotContain("private void OnRowResized(");
        mainSource.Should().NotContain("private void OnPageMarginsChanged(");
        mainSource.Should().NotContain("private void CaptureColumnResizeSnapshot(");
        mainSource.Should().NotContain("private void RestoreRowResizeSnapshot(");

        gridSource.Should().Contain("private void RefreshStatusBar(");
        gridSource.Should().Contain("private void OnColumnResizing(");
        gridSource.Should().Contain("private void OnColumnResized(");
        gridSource.Should().Contain("private void OnRowResizing(");
        gridSource.Should().Contain("private void OnRowResized(");
        gridSource.Should().Contain("private void OnPageMarginsChanged(");
        gridSource.Should().Contain("private void CaptureColumnResizeSnapshot(");
        gridSource.Should().Contain("private void RestoreRowResizeSnapshot(");
        gridSource.Should().Contain("StatusBarCalculator");
    }

    [Fact]
    public void RibbonKeyTipController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var commandSourcePath = Path.Combine(appHostDirectory, "MainWindow.CommandExecution.cs");

        File.Exists(commandSourcePath).Should().BeTrue();
        var commandSource = File.ReadAllText(commandSourcePath);

        mainSource.Should().NotContain("private static void ShowCommandError(");
        mainSource.Should().NotContain("private bool TryExecuteCommand(");
        mainSource.Should().NotContain("private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds(");
        mainSource.Should().NotContain("private bool TryExecuteEditCells(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableGroupedSheetCommand(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableCurrentRangeCommand(");
        mainSource.Should().NotContain("private bool TryExecuteRepeatableChartLayout(");
        mainSource.Should().NotContain("private void ExecuteUndo(");
        mainSource.Should().NotContain("private void ExecuteRepeatLast(");
        mainSource.Should().NotContain("private IWorkbookCommand CreateSingleCellEditCommand(");

        commandSource.Should().Contain("private static void ShowCommandError(");
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        dataFilterSource.Should().Contain("FilterInputParser.TryParseCriterion");
    }

    [Fact]
    public void InsertCommands_LiveOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        shellSource.Should().Contain("private void UndoQatBtn_Click(");
        shellSource.Should().Contain("private void RedoQatBtn_Click(");
    }

    [Fact]
    public void QuickAccessUndoRedoButtons_ReflectCommandStackState()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorkbookUiState.cs"));

        source.Should().Contain("var canUndo = _commandBus.CanUndo(_workbook.Id);");
        source.Should().Contain("var canRedo = _commandBus.CanRedo(_workbook.Id);");
        source.Should().Contain("UndoQatBtn.IsEnabled = state.CanUndo;");
        source.Should().Contain("RedoQatBtn.IsEnabled = state.CanRedo;");
    }

    [Fact]
    public void WorkbookUiStateController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
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
        var mainWindowPath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml");
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
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Startup.cs")) +
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
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

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
        var source = ReadChartCommandSource();
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        source.Should().Contain("ShowDeferredChartFamilyMessage");
        source.Should().Contain("retained when opening XLSX files");
        source.Should().NotContain("InsertChartOfType(ChartType.Treemap)");
        source.Should().NotContain("InsertChartOfType(ChartType.Sunburst)");
        source.Should().NotContain("InsertChartOfType(ChartType.Histogram)");
        source.Should().NotContain("InsertChartOfType(ChartType.Pareto)");
        source.Should().NotContain("InsertChartOfType(ChartType.BoxAndWhisker)");
        source.Should().NotContain("InsertChartOfType(ChartType.Waterfall)");
        source.Should().NotContain("InsertChartOfType(ChartType.Funnel)");
        source.Should().NotContain("InsertChartOfType(ChartType.Map)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDPie)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDLine)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDArea)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDColumn)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDBar)");
        source.Should().Contain("InsertChartOfType(ChartType.Surface)");
        source.Should().Contain("InsertChartOfType(ChartType.ThreeDSurface)");
        xaml.Should().Contain("Click=\"DeferredChartFamilyMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DPieMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DLineMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DAreaMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DColumnMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DBarMenuItem_Click\"");
        xaml.Should().Contain("Click=\"ChartSurfaceMenuItem_Click\"");
        xaml.Should().Contain("Click=\"Chart3DSurfaceMenuItem_Click\"");
        xaml.Should().Contain("Surface");
        xaml.Should().Contain("Treemap");
        xaml.Should().Contain("Sunburst");
        xaml.Should().Contain("Histogram");
        xaml.Should().Contain("Pareto");
        xaml.Should().Contain("Box Plot");
        xaml.Should().Contain("Waterfall");
        xaml.Should().Contain("Funnel");
        xaml.Should().Contain("Map");
        xaml.Should().Contain("3D Pie");
        xaml.Should().Contain("3D Line");
        xaml.Should().Contain("3D Area");
        xaml.Should().Contain("3D Column");
        xaml.Should().Contain("3D Bar");
        xaml.Should().Contain("3D Surface");
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
        var source = ReadPivotCommandSource();

        source.Should().Contain("new AddPivotTableToNewWorksheetCommand(");
        source.Should().Contain("command.CreatedSheetId");
        source.Should().NotContain("New chart-style PivotTable sheets are tracked for Wave 2");
    }

    [Fact]
    public void WorksheetContextMenuPickFromDropDown_ReusesActiveDropdownPath()
    {
        var source =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs")) +
            ReadEditingSource();

        source.Should().Contain("case WorksheetContextMenuAction.PickFromDropDown:");
        source.Should().Contain("OpenActiveDropdown();");
    }

    [Fact]
    public void WorksheetContextMenuQuickAnalysis_ReusesCtrlQPath()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("case WorksheetContextMenuAction.QuickAnalysis:");
        source.Should().Contain("ShowQuickAnalysisMenu();");
    }

    [Fact]
    public void WorksheetContextMenu_UsesAccessKeyHeaders()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("Header = command.AccessHeader");
    }

    [Fact]
    public void KeyboardWorksheetContextMenu_IsAnchoredToActiveCell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("OpenKeyboardContextMenu()");
        source.Should().Contain("TryGetCellOverlayRect(address)");
        source.Should().Contain("menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint");
        source.Should().Contain("menu.HorizontalOffset = screenPoint.X");
        source.Should().Contain("menu.VerticalOffset = screenPoint.Y");
        source.Should().NotContain("OnGridContextMenuRequested(address, default);");
    }

    [Fact]
    public void KeyboardWorksheetContextMenu_FocusesFirstEnabledMenuItem()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("menu.Opened += WorksheetContextMenu_Opened;");
        source.Should().Contain("private static void WorksheetContextMenu_Opened(object sender, RoutedEventArgs e)");
        source.Should().Contain("menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled)");
        source.Should().Contain("Keyboard.Focus(firstEnabledItem);");
    }

    [Fact]
    public void KeyboardContextMenu_RoutesFocusedSheetTabToSheetTabMenu()
    {
        var contextMenuSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));
        var sheetTabsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.SheetTabs.cs"));
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        contextMenuSource.Should().Contain("if (TryOpenFocusedSheetTabContextMenu())");
        sheetTabsSource.Should().Contain("private bool TryOpenFocusedSheetTabContextMenu()");
        sheetTabsSource.Should().Contain("Keyboard.FocusedElement is not DependencyObject focusedElement");
        sheetTabsSource.Should().Contain("contextMenu.PlacementTarget = target;");
        sheetTabsSource.Should().Contain("contextMenu.IsOpen = true;");
        selectionSource.Should().Contain("return TryFocusCurrentSheetTab() || AddSheetButton.Focus();");
        xaml.Should().Contain("Focusable=\"True\"");
    }

    [Fact]
    public void KeyboardSheetTabContextMenu_FocusesFirstEnabledMenuItem()
    {
        var sheetTabsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.SheetTabs.cs"));

        sheetTabsSource.Should().Contain("contextMenu.Opened += SheetTabContextMenu_Opened;");
        sheetTabsSource.Should().Contain("private static void SheetTabContextMenu_Opened(object sender, RoutedEventArgs e)");
        sheetTabsSource.Should().Contain("contextMenu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled)");
        sheetTabsSource.Should().Contain("Keyboard.Focus(firstEnabledItem);");
    }

    [Fact]
    public void FocusedSheetTabs_HandleArrowNavigationBeforeWorksheetNavigation()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var sheetTabsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.SheetTabs.cs"));

        selectionSource.Should().Contain("if (TryHandleFocusedSheetTabKeyboardNavigation(e))");
        sheetTabsSource.Should().Contain("private bool TryHandleFocusedSheetTabKeyboardNavigation(System.Windows.Input.KeyEventArgs e)");
        sheetTabsSource.Should().Contain("Keyboard.Modifiers != ModifierKeys.None");
        sheetTabsSource.Should().Contain("Key.Left => FocusAdjacentVisibleSheetTab(-1)");
        sheetTabsSource.Should().Contain("Key.Right => FocusAdjacentVisibleSheetTab(1)");
        sheetTabsSource.Should().Contain("Key.Home => FocusEdgeVisibleSheetTab(first: true)");
        sheetTabsSource.Should().Contain("Key.End => FocusEdgeVisibleSheetTab(first: false)");
        sheetTabsSource.Should().Contain("FocusSheetTab(tab.Id);");
    }

    [Fact]
    public void F6StatusBar_FocusesFirstZoomControlBeforeSliderFallback()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        selectionSource.Should().Contain("return FocusStatusBar();");
        selectionSource.Should().Contain("private bool FocusStatusBar()");
        selectionSource.Should().Contain("return StatusZoomOutButton.Focus() || ZoomSlider.Focus();");
        xaml.Should().Contain("x:Name=\"StatusZoomOutButton\"");
        xaml.Should().Contain("x:Name=\"StatusZoomInButton\"");
    }

    [Fact]
    public void FocusedStatusBar_TabTraversalIsNotHijackedByWorksheetMovement()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        selectionSource.Should().Contain("if (TryHandleFocusedStatusBarKeyboardNavigation(e))");
        selectionSource.Should().Contain("private bool TryHandleFocusedStatusBarKeyboardNavigation(System.Windows.Input.KeyEventArgs e)");
        selectionSource.Should().Contain("!IsDescendantOf(focusedElement, StatusBarGrid)");
        selectionSource.Should().Contain("Keyboard.Modifiers is not ModifierKeys.None and not ModifierKeys.Shift");
        selectionSource.Should().Contain("new TraversalRequest(Keyboard.Modifiers == ModifierKeys.Shift");
        selectionSource.Should().Contain("FocusNavigationDirection.Previous");
        selectionSource.Should().Contain("FocusNavigationDirection.Next");
        selectionSource.Should().Contain("focusedElement.MoveFocus(request);");
    }

    [Fact]
    public void FocusedRibbon_TabAndArrowKeysRequestFocusTraversal()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        selectionSource.Should().Contain("MoveFocusedRibbonElement(focusedElement, Keyboard.Modifiers == ModifierKeys.Shift");
        selectionSource.Should().Contain("FocusNavigationDirection.Previous");
        selectionSource.Should().Contain("FocusNavigationDirection.Next");
        selectionSource.Should().Contain("Key.Left => FocusNavigationDirection.Left");
        selectionSource.Should().Contain("Key.Right => FocusNavigationDirection.Right");
        selectionSource.Should().Contain("Key.Up => FocusNavigationDirection.Up");
        selectionSource.Should().Contain("Key.Down => FocusNavigationDirection.Down");
        selectionSource.Should().Contain("Key.Home => FocusNavigationDirection.First");
        selectionSource.Should().Contain("Key.End => FocusNavigationDirection.Last");
        selectionSource.Should().Contain("private static bool MoveFocusedRibbonElement(DependencyObject focusedElement, FocusNavigationDirection direction)");
        selectionSource.Should().Contain("focusedUiElement.MoveFocus(new TraversalRequest(direction));");
    }

    [Fact]
    public void WorksheetContextMenu_UsesObjectAwareTargetKind()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("GetWorksheetContextMenuTargetKind(actualAddr)");
        source.Should().Contain("WorksheetContextMenuPlanner.BuildCommands(targetKind, state)");
        source.Should().Contain("DrawingTargetResolver.GetTargetPicture(sheet, address)");
        source.Should().Contain("WorksheetContextMenuTargetKind.Picture");
        source.Should().Contain("case WorksheetContextMenuAction.FormatPicture:");
        source.Should().Contain("PictureSizeBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.FormatDrawingObject:");
        source.Should().Contain("ObjectSizeBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.EditAltText:");
        source.Should().Contain("SetAltTextBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.SelectionPane:");
        source.Should().Contain("SelectionPaneBtn_Click(this, new RoutedEventArgs());");
    }

    [Fact]
    public void WorksheetContextMenu_UsesRowAndColumnSelectionTargetKinds()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("SheetGrid.SelectedRange is { } selectedRange");
        source.Should().Contain("SelectionRangeService.IsWholeRowSelection(selectedRange)");
        source.Should().Contain("WorksheetContextMenuTargetKind.RowSelection");
        source.Should().Contain("SelectionRangeService.IsWholeColumnSelection(selectedRange)");
        source.Should().Contain("WorksheetContextMenuTargetKind.ColumnSelection");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("case WorksheetContextMenuAction.NewComment:");
        source.Should().Contain("ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs());");
    }

    [Fact]
    public void WorksheetContextMenuEditAndDeleteComment_UseThreadedCommentWorkflow()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));
        var reviewSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("case WorksheetContextMenuAction.EditComment:");
        source.Should().Contain("case WorksheetContextMenuAction.DeleteComment:");
        source.Should().Contain("ReviewDeleteThreadedCommentBtn_Click(this, new RoutedEventArgs());");
        reviewSource.Should().Contain("private void ReviewDeleteThreadedCommentBtn_Click(");
        reviewSource.Should().Contain("new DeleteThreadedCommentCommand(");
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
    public void QuickAnalysisMenu_UsesKeyboardSelectionAnchorAndInitialMenuFocus()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("QuickAnalysisMenuItem_MouseEnter");
        source.Should().Contain("QuickAnalysisMenuItem_MouseLeave");
        source.Should().Contain("QuickAnalysisPlanner.BuildHoverPreview(range, option)");
        source.Should().Contain("StatusReadyText.Text = preview.StatusText");
        source.Should().Contain("StatusReadyText.Text = \"Ready\"");
    }

    [Fact]
    public void QuickAnalysisMenu_RoutesExpandedConditionalFormattingGallery()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("case QuickAnalysisCommand.MoreCharts:");
        source.Should().Contain("InsertChartPickerBtn_Click(sender, e);");
    }

    [Fact]
    public void QuickAnalysisMenu_RoutesExpandedTotalsGallery()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("case QuickAnalysisCommand.PercentTotal:");
        source.Should().Contain("case QuickAnalysisCommand.RunningTotal:");
        source.Should().Contain("QuickAnalysisTotalsPlanner.BuildPercentTotalEdits");
        source.Should().Contain("QuickAnalysisTotalsPlanner.BuildRunningTotalEdits");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_IsAnchoredToActiveHeaderCell()
    {
        var source = ReadEditingSource();

        source.Should().Contain("PositionAutoFilterDialogAtActiveCell(dialog, activeCell);");
        source.Should().Contain("private void PositionAutoFilterDialogAtActiveCell");
        source.Should().Contain("TryGetCellOverlayRect(activeCell)");
        source.Should().Contain("SheetGrid.PointToScreen");
        source.Should().Contain("WindowStartupLocation.Manual");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_ReusesFullFilterDialogResultRouting()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var editingSource = ReadEditingSource();
        var dataFilterSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.DataFilterCommands.cs"));

        editingSource.Should().Contain("ApplyAutoFilterDialogResult(plan.Range, plan.FilterColumnOffset, dialog.Result, \"AutoFilter\")");
        dataFilterSource.Should().Contain("private bool ApplyAutoFilterDialogResult(");
        dataFilterSource.Should().Contain("FilterInputParser.TryParseTopBottom");
        dataFilterSource.Should().Contain("FilterInputParser.TryParseCriterion");
        dataFilterSource.Should().Contain("FilterInputParser.TryParseAverage");
    }

    [Fact]
    public void AutoFilterKeyboardDropdown_UsesExcelStyleMenuPlanner()
    {
        var source = ReadEditingSource();
        var dialog = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutoFilterDialog.cs"));

        source.Should().Contain("AutoFilterDropdownPlanner.CreateMenuPlan(_workbook, sheet, plan)");
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
    public void SpellCheckWorkflow_RoutesDistinctDialogActionsToIntendedResults()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("SpellCheckDialogAction.ReplaceAll");
        source.Should().Contain("SpellCheckDialogAction.IgnoreAll");
        source.Should().Contain("SpellCheckDialogAction.Ignore");
        source.Should().Contain("SpellCheckDialogAction.Add");
        source.Should().Contain("while (true)");
        source.Should().Contain("ignoredWords.Contains(issue.Word)");
        source.Should().Contain("ignoredIssues.Contains((issue.Address, issue.Word))");
        source.Should().Contain("BuildSpellCheckReplaceAllEdits(issues, issue.Word, replacement)");
        source.Should().Contain("SpellCheckService.ApplyCorrection(issue, replacement)");
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
    public void ScenarioShow_IsRepeatableForF4WithoutReopeningDialog()
    {
        var dataSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataCommands.cs"));

        dataSource.Should().Contain("_commandBus.ExecuteRepeatable(_workbook.Id, () => new ApplyScenarioCommand(name))");
        dataSource.Should().Contain("RecalculateIfAutomatic(outcome.AffectedCells ?? []);");
        dataSource.Should().Contain("SetActiveCell(first);");
        dataSource.Should().Contain("EnsureCellVisible(first);");
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
        var source = ReadPivotCommandSource();

        xaml.Should().Contain("local:RibbonTooltip.Description=\"Open PivotTable layout and style options.");
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

    [Fact]
    public void CollapsedRibbonOverflowCommands_ReturnFocusToVisibleGroupButton()
    {
        var adaptiveSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.RibbonAdaptive.cs"));

        adaptiveSource.Should().Contain("FocusCollapsedRibbonMenuPlacementTarget(item)");
        adaptiveSource.Should().Contain("private static void FocusCollapsedRibbonMenuPlacementTarget(MenuItem item)");
        adaptiveSource.Should().Contain("contextMenu.PlacementTarget is UIElement placementTarget");
        adaptiveSource.Should().Contain("placementTarget.Focus();");
    }

    [Fact]
    public void StartupController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var startupSourcePath = Path.Combine(appHostDirectory, "MainWindow.Startup.cs");

        File.Exists(startupSourcePath).Should().BeTrue();
        var startupSource = File.ReadAllText(startupSourcePath);

        mainSource.Should().NotContain("private void MainWindow_Loaded(");
        mainSource.Should().NotContain("NumberFormatOptions");

        startupSource.Should().Contain("private void MainWindow_Loaded(");
        startupSource.Should().Contain("NumberFormatOptions");
        startupSource.Should().Contain("CreateNewWorkbook();");
        startupSource.Should().Contain("NormalizeRibbonSurface(forceCompact: true);");
    }

    [Fact]
    public void GridResizeSnapshots_LiveWithGridStatusController()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var mainSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.xaml.cs"));
        var gridStatusSource = File.ReadAllText(Path.Combine(appHostDirectory, "MainWindow.GridStatus.cs"));

        mainSource.Should().NotContain("private sealed record ColumnResizeSnapshot(");
        mainSource.Should().NotContain("private sealed record RowResizeSnapshot(");

        gridStatusSource.Should().Contain("private sealed record ColumnResizeSnapshot(");
        gridStatusSource.Should().Contain("private sealed record RowResizeSnapshot(");
    }



    private static string ReadEditingSource()
    {
        return string.Join(
            "\n",
            new[]
            {
                "MainWindow.Editing.cs",
                "MainWindow.EditingDropdowns.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", fileName))));
    }

    private static string ReadChartCommandSource()
    {
        return string.Join(
            "\n",
            new[]
            {
                "MainWindow.ChartCommands.cs",
                "MainWindow.ChartAxisCommands.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", fileName))));
    }

    private static string ReadPivotCommandSource()
    {
        return string.Join(
            "\n",
            new[]
            {
                "MainWindow.PivotCommands.cs",
                "MainWindow.PivotSlicerTimeline.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", fileName))));
    }
}
