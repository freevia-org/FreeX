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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("private SheetId? _formatPainterSourceSheetId;");
        source.Should().Contain("private GridRange? _formatPainterSourceRange;");
        source.Should().Contain("private bool _formatPainterTargetSelectionActive;");
        source.Should().Contain("TryApplyFormatPainter(GridRange targetRange)");
        source.Should().Contain("_formatPainterSourceRange = range;");
        source.Should().Contain("var targetSheetIds = CurrentGroupedEditSheetIds();");
        source.Should().Contain("FormatPainterCommandFactory.Create(_workbook, sourceSheet, sourceRange, targetRange)");
        source.Should().Contain("new CompositeWorkbookCommand(\"Format Painter\", targetSheetIds.Select(CreateCommand).ToList())");
        source.Should().Contain("SheetGrid.SelectedRange is { } selectedRange");
        source.Should().Contain("selectedRange.Contains(newAddr)");
        source.Should().Contain("TryApplyFormatPainter(selectedRange)");
        source.Should().NotContain("var targetRange = new GridRange(addr, addr);");
    }

    [Fact]
    public void AutoFitMenuHandlers_UsePlannerAndPerTargetExplicitSizes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
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
    public void QuickAnalysisMenu_UsesPlannerPreviewMetadataForHoverTooltips()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var planner = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "QuickAnalysisPlanner.cs"));

        source.Should().Contain("ToolTip = option.PreviewText");
        planner.Should().Contain("QuickAnalysisPreviewKind");
    }

    [Fact]
    public void QuickAnalysisMenu_UpdatesLiveHoverPreviewStatus()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

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
    public void BorderGallery_ExposesExpandedPresetsAndUsesReusablePlanners()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        foreach (var label in new[]
        {
            "Bottom Double Border",
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
        source.Should().Contain("BorderShortcutService.GetTopAndBottomBorderDiff");
        source.Should().Contain("BorderShortcutService.GetOutlineBorderDiff");
    }

    [Fact]
    public void MainWindow_RoutesColorChoicesThroughColorPickerDialog()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().NotContain("input.Split(',')");
        source.Should().Contain("private bool TryShowColorPicker(");
        source.Should().Contain("new ColorPickerDialog");
        source.Should().Contain("TryShowColorPicker(\"Font Color\"");
        source.Should().Contain("TryShowColorPicker(\"Fill Color\"");
    }

    [Fact]
    public void SpellCheckWorkflow_RoutesDialogActionsThroughKnownCorrectionsPlan()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

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
    public void FormatAsTable_CreatesStructuredTableMetadataBeforeApplyingBanding()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("new CreateTableDialog");
        source.Should().Contain("new CreateStructuredTableCommand(");
        source.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(dialog.Result.Range, sheetId)");
        source.Should().Contain("\"TableStyleLight9\"");
        source.Should().Contain("\"TableStyleMedium2\"");
        source.Should().Contain("\"TableStyleDark1\"");
        source.Should().Contain("TryExecuteApplyStyle(");
    }

    [Fact]
    public void CellStyleMenu_UsesActiveWorkbookThemeForPresetPlanning()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme)");
    }

    [Fact]
    public void ExportWorkflow_SurfacesPlannedOptionsAndFallbackPath()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("ExportViaPrintToPdf(request)");
        source.Should().Contain("ExportAsXps(request.Path, ExportPlanner.DescribeOptions(request.Options))");
        source.Should().Contain("ExportPlanner.DescribeRequest(request)");
        source.Should().Contain("request.ActualPath");
    }

    [Fact]
    public void RemainingStatusWorkflows_OpenNamedDialogsInsteadOfMessageBoxes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("new PageBreakDialog");
        source.Should().Contain("new GoalSeekStatusDialog");
        source.Should().Contain("new WorkbookStatisticsDialog");
        source.Should().Contain("new AccessibilityCheckerDialog");
    }

    [Fact]
    public void ConditionalFormattingEllipsisCommands_UseRuleFamilyDialogFactory()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        xaml.Should().Contain("Header=\"Crop...\"");
        xaml.Should().Contain("Header=\"Reset Crop\"");
        xaml.Should().Contain("Click=\"PictureCropDialogMenuItem_Click\"");
        xaml.Should().Contain("Click=\"PictureResetCropMenuItem_Click\"");
        source.Should().Contain("PictureResetCropMenuItem_Click");
        source.Should().Contain("new SetPictureCropCommand(");
        source.Should().Contain("0, 0, 0, 0");
    }
}
