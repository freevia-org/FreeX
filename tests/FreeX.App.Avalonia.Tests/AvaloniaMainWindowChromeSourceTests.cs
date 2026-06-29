using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaMainWindowChromeSourceTests
{
    [Fact]
    public void QuickAnalysisShell_UsesSharedGroupTitleMetadata()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.QuickAnalysis.cs"));
        var plannerSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisShellPlanner.cs"));
        var requestPlannerSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisShellRequestPlanner.cs"));
        var openPlannerSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisShellOpenPlanner.cs"));

        source.Should().Contain("QuickAnalysisShellRequestPlanner.Build(");
        source.Should().Contain("QuickAnalysisShellOpenPlanner.Plan(request)");
        source.Should().NotContain("request.Status is QuickAnalysisShellRequestStatus");
        source.Should().NotContain("if (!request.CanOpen)");
        source.Should().Contain("Text = UiText.Get(group.TitleResourceKey)");
        source.Should().Contain("foreach (var group in shellPlan.Groups)");
        source.Should().NotContain("foreach (var group in displayModel.Groups)");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, item.AutomationId)");
        requestPlannerSource.Should().Contain("QuickAnalysisShellPlanner.BuildMenuPlan(displayModel, capabilities, range)");
        openPlannerSource.Should().Contain("QuickAnalysisShellOpenDecision.ShowNoSuggestionsIssue");
        plannerSource.Should().Contain("GroupTitleResourceKey(group.Group)");
        plannerSource.Should().Contain("QuickAnalysisShellActionPlanner.Plan(item, capabilities)");
        source.Should().NotContain("QuickAnalysisGroupTitle(");
        source.Should().NotContain("QuickAnalysisGroup.Formatting => UiText.Get");
    }

    [Fact]
    public void QuickAnalysisShell_UsesSharedActionPlanning()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.QuickAnalysis.cs"));
        var actionPlannerSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisShellActionPlanner.cs"));
        var operationPlannerSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "QuickAnalysis",
            "QuickAnalysisHostOperationPlanner.cs"));

        source.Should().Contain("var operation = QuickAnalysisHostOperationPlanner.Plan(item);");
        source.Should().NotContain("QuickAnalysisShellActionPlanner.Plan(item, QuickAnalysisShellCapabilities.DirectApplyLimited)");
        source.Should().Contain("operation.ConditionalFormatPreset is { } preset");
        source.Should().NotContain("TryMapQuickAnalysisConditionalFormatPreset(");
        source.Should().Contain("QuickAnalysisHostOperationKind.ApplyConditionalFormat");
        source.Should().Contain("QuickAnalysisHostOperationKind.Deferred");
        source.Should().NotContain("IsQuickAnalysisAutoSumFunction(");
        source.Should().NotContain("QuickAnalysisCommandKind.PivotTable");
        actionPlannerSource.Should().Contain("This total is not yet available on {capabilities.DeferredPlatformName}.");
        actionPlannerSource.Should().Contain("Converting to a PivotTable is not yet available on {capabilities.DeferredPlatformName}.");
        operationPlannerSource.Should().Contain("QuickAnalysisHostOperationKind.ApplyConditionalFormat");
    }

    [Fact]
    public void ChartContextualTabs_UseSharedQuickFormatCycler()
    {
        var chartTabsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTabs.cs"));
        var chartDialogSources = string.Join(
            Environment.NewLine,
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartRemainingDialogs.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTypeFormatDialogs.cs")));

        chartTabsSource.Should().Contain("ChartQuickFormatCycler.NextDataLabelPosition(");
        chartTabsSource.Should().Contain("ChartQuickFormatCycler.NextGridlineState(");
        chartTabsSource.Should().Contain("ChartQuickFormatCycler.ReadFirstSeriesFormat(chart).FillColor");
        chartTabsSource.Should().Contain("ChartQuickFormatCycler.DefaultSeriesColor");
        chartTabsSource.Should().Contain("ChartWorkflowTargetPlanner.FindSelectedChart(_session.ActiveSheet, _selectedDrawingObjectId)");
        chartTabsSource.Should().Contain("ChartQuickFormatCycler.NextChartStyleId(chart.ChartStyleId)");
        chartTabsSource.Should().Contain("ChartQuickFormatCycler.NextPlotAreaBorderThickness(chart.PlotAreaBorderThickness)");
        chartTabsSource.Should().Contain("ChartQuickFormatCycler.MergeFirstSeriesFillColor(chart, chosen)");
        chartDialogSources.Should().Contain("ChartQuickFormatCycler.DefaultSeriesColor");

        var combined = chartTabsSource + Environment.NewLine + chartDialogSources;
        combined.Should().NotContain("ChartCycleBlue");
        combined.Should().NotContain("ResolveFirstSeriesFillColor");
        combined.Should().NotContain("candidate.Id == id && candidate.IsVisible && !candidate.IsPivotChart");
        combined.Should().NotContain("chart.PlotAreaBorderThickness >= 3 ? 0.75");
        combined.Should().NotContain("current >= 45 ? 1 : current + 4");
        combined.Should().NotContain("private static ChartDataLabelPosition NextDataLabelPosition");
        combined.Should().NotContain("private static (bool ShowMajor, bool ShowMinor) NextGridlineState");
    }

    [Fact]
    public void SelectChartDataDialog_UsesSharedSelectDataSourcePlanner()
    {
        var chartTabsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ChartTabs.cs"));

        chartTabsSource.Should().Contain("SelectDataSourcePlanner.InferPreviewEntries(");
        chartTabsSource.Should().Contain("SelectDataSourcePlanner.FormatSeriesListItem");
        chartTabsSource.Should().Contain("SelectDataSourcePlanner.FormatNewSeriesItem");
        chartTabsSource.Should().Contain("SelectDataSourcePlanner.CreateResult(");
        chartTabsSource.Should().NotContain("TryParseCellRef");
        chartTabsSource.Should().NotContain("TryParseRangeReference");
    }

    [Fact]
    public void WorksheetChrome_UsesCompactGridMetricsAndExcelSheetTabOrder()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private const double HeaderColumnWidth = 30;");
        source.Should().Contain("private const double HeaderRowHeight = 18;");
        source.Should().Contain("private const double MinimumDisplayedColumnWidth = 48;");
        source.Should().Contain("private const double MinimumDisplayedRowHeight = 20;");

        source.Should().Contain("Child = _sheetTabLeftNavButton,");
        source.Should().Contain("tabCluster.Children.Add(_sheetTabsScroller);");
        source.Should().Contain("tabCluster.Children.Add(_sheetTabRightNavButton);");
        source.Should().Contain("panel.Children.Add(_newSheetButton);");
        source.Should().Contain("ConfigureSheetTabNavigationButton(_sheetTabLeftNavButton, \"<\", \"Scroll Tabs Left\", -1)");
        source.Should().Contain("ConfigureSheetTabNavigationButton(_sheetTabRightNavButton, \">\", \"Scroll Tabs Right\", 1)");
        source.Should().Contain("_horizontalWorksheetScrollBar.Width = SheetHorizontalScrollbarDefaultWidth;");
        source.Should().Contain("_horizontalWorksheetScrollBar.MaxWidth = SheetHorizontalScrollbarMaximumWidth;");
        source.Should().Contain("var contentWidth = _session.SheetTabs.Sum(tab => EstimateSheetTabWidth(tab.Name)) + _newSheetButton.Width;");
        source.Should().Contain("button.FontSize = 15;");
        source.Should().Contain("Margin = new Thickness(0, -2, 0, 0),");
        source.Should().Contain("var horizontalRuleLeft = 0d;");
        source.Should().Contain("var horizontalRuleRight = totalWidth;");
        source.Should().Contain("var contourRight = Math.Clamp(scrollerRight, contourLeft, Math.Max(contourLeft, scrollBarLeft));");
        source.Should().Contain("var activeTabFullyVisible = activeLeft >= contourLeft - SheetTabContourClipTolerance");
        source.Should().Contain("if (!activeTabFullyVisible)");
        source.Should().Contain("AddSheetTabContourLine(horizontalRuleLeft, horizontalRuleRight, topY);");
        source.Should().Contain("AddSheetTabContourLine(horizontalRuleLeft, leftJoin, topY);");
        source.Should().Contain("AddSheetTabContourLine(rightJoin, horizontalRuleRight, topY);");
        source.Should().NotContain("Math.Clamp(activeLeft, contourLeft");
        source.Should().NotContain("Math.Clamp(activeRight, activeLeft + 16");
        source.Should().NotContain("AddSheetTabTopRuleSegment");
        source.Should().Contain("UpdateSheetTabsContourLayer();");
        source.Should().Contain("private void UpdateSheetTabNavigationVisibility()");
        source.Should().Contain("private void UpdateSheetTabsContourLayer()");
        source.Should().NotContain("AddGridChild(chrome, _horizontalWorksheetScrollBar, 1, 0);");
    }

    [Fact]
    public void MainWindow_UsesSharedAvaloniaShellFrameAndStatusChrome()
    {
        var project = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj"));
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        var buildContentBlock = ExtractSourceBlock(
            normalizedSource,
            "private Control BuildContent()",
            "private Control BuildWorkbookWorkArea()");
        var statusBarBlock = ExtractSourceBlock(
            normalizedSource,
            "private Control BuildStatusBar()",
            "/// <summary>\n    /// Looks up a named brush");

        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");
        source.Should().Contain("using Free.Shared.Shell.Avalonia;");

        buildContentBlock.Should().Contain("SisterAppClientFrameBuilder.Build(new SisterAppClientFrameSpec(");
        buildContentBlock.Should().Contain("Ribbon: ribbon,");
        buildContentBlock.Should().Contain("WorkArea: BuildWorkbookWorkArea(),");
        buildContentBlock.Should().Contain("StatusBar: statusBar,");
        buildContentBlock.Should().Contain("BottomPanelsAboveStatus: [sheetTabs],");
        buildContentBlock.Should().Contain("TopPanelsBelowRibbon: [formulaBar]");
        buildContentBlock.Should().NotContain("var root = new DockPanel();");
        buildContentBlock.Should().NotContain("root.Children.Add(");

        statusBarBlock.Should().Contain("SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(");
        statusBarBlock.Should().Contain("LeftContent: leftPanel,");
        statusBarBlock.Should().Contain("RightItems: [rightPanel],");
        statusBarBlock.Should().Contain("CenterContent: statsViewport,");
        statusBarBlock.Should().Contain("Padding: new Thickness(8, 3)");
        statusBarBlock.Should().NotContain("var grid = new AvaloniaGrid");
        statusBarBlock.Should().NotContain("AddGridChild(grid, leftPanel");
        statusBarBlock.Should().NotContain("Child = grid");
    }

    [Fact]
    public void FormulaBarToggle_HidesTheWholeFormulaBarRow()
    {
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var toggleSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ViewToggles.cs"));

        mainSource.Should().Contain("private readonly Border _formulaBarHost = new();");
        mainSource.Should().Contain("private static readonly FontFamily FormulaBarFontFamily");
        mainSource.Should().Contain("_cellAddressText.FontFamily = FormulaBarFontFamily;");
        mainSource.Should().Contain("_cellAddressText.TextAlignment = TextAlignment.Left;");
        mainSource.Should().Contain("_formulaBox.FontFamily = FormulaBarFontFamily;");
        mainSource.Should().Contain("_formulaBox.FontSize = 15;");
        mainSource.Should().Contain("AutomationProperties.SetAutomationId(_formulaBarHost, \"FormulaBarRow\");");
        toggleSource.Should().Contain("_formulaBarHost.IsVisible = visible;");
    }

    [Fact]
    public void FormulaEditing_PointModeAndEnterRestoreWorksheetKeyboardRouting()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("TryInsertFormulaPointReference(address)");
        source.Should().Contain("private bool TryInsertFormulaPointReference(CellAddress address)");
        source.Should().Contain("_session.FormulaEditAddress is null");
        source.Should().Contain("!IsFormulaPointModeText(_formulaBox.Text)");
        source.Should().Contain("var reference = FormatCellReference(address);");
        source.Should().Contain("_formulaBox.Text = string.Concat(");
        source.Should().Contain("_formulaBox.Focus();");

        source.Should().Contain("ExcelEditKeyPlanner.GetIntent(");
        source.Should().Contain("FormulaBarAvaloniaInputAdapter.ToFormulaEditorKey(e.Key)");
        source.Should().Contain("FormulaBarAvaloniaInputAdapter.ToFormulaEditorModifiers(e.KeyModifiers)");
        source.Should().Contain("intent.Action == ExcelEditKeyAction.CommitAndMove");
        source.Should().Contain("var rowDelta = GetCellIndexDelta(current.Row, target.Row);");
        source.Should().Contain("var colDelta = GetCellIndexDelta(current.Col, target.Col);");
        source.Should().Contain("_session.MoveActiveCell(rowDelta, colDelta);");
        source.Should().Contain("FocusShellRegion(ShellFocusTarget.Worksheet);");
        source.Should().Contain("private static bool IsFormulaPointModeText(string? text)");
    }

    [Fact]
    public void F6ShellFocusCycle_UsesSharedPresentationPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ShellFocusCyclePlanner.GetNextAvailable(current, reverse, IsShellFocusTargetAvailable)");
        source.Should().Contain("private static bool IsShellFocusTargetAvailable(ShellFocusTarget target)");
        source.Should().Contain("private ShellFocusTarget GetCurrentShellFocusTarget()");
        source.Should().Contain("private bool FocusShellRegion(ShellFocusTarget target)");
        source.Should().Contain("ShellFocusTarget.Ribbon => FocusFirstEnabledToolbarControl()");
        source.Should().Contain("ShellFocusTarget.TaskPane => false");

        source.Should().NotContain("private enum ShellFocusRegion");
        source.Should().NotContain("private static readonly ShellFocusRegion[] ShellFocusCycle");
        source.Should().NotContain("GetNextShellFocusRegion");
        source.Should().NotContain("Array.IndexOf(ShellFocusCycle");
    }

    [Fact]
    public void ParityCapture_UsesSameResolutionAndDoesNotMislabelBackstageDialogs()
    {
        var captureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var hostCaptureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "ParityCapture.cs"));
        var runnerSource = File.ReadAllText(RepoFile("tools", "FreeX.ParityCompare", "CaptureRunner.cs"));

        runnerSource.Should().Contain("-screen 0 1120x720x24");
        runnerSource.Should().NotContain("-screen 0 1600x1000x24");
        captureSource.Should().Contain("private const int ParityCaptureTitleBarHeight = 30;");
        captureSource.Should().Contain("RenderWindowWithCapturedTitleBarToPng(this, ParityCaptureWindowWidth, ParityCaptureWindowHeight");
        captureSource.Should().Contain("RenderWindowClientContentToBitmap(window, pixelWidth, contentHeight)");
        captureSource.Should().Contain("window.Height = height;");
        captureSource.Should().Contain("window.Content as Visual ?? window");
        captureSource.Should().Contain("CreateParityCapturedTitleBar(FormatParityCapturedWindowTitle(window.Title ?? \"FreeX\"))");
        captureSource.Should().Contain("CreateParityCapturedAppIcon()");
        captureSource.Should().Contain("TryCreateParityCapturedAppIconFromResource()");
        captureSource.Should().Contain("TryDecodeParityCapturedIcoPngFrame(iconPath, desiredSize: 48)");
        captureSource.Should().Contain("Width = 20,");
        captureSource.Should().Contain("Height = 20,");
        captureSource.Should().Contain("CreateParityCapturedQatButton(RibbonCommandIconKind.Save, width: 26, iconSize: 16, isEnabled: true)");
        captureSource.Should().Contain("CreateParityCapturedQatButton(RibbonCommandIconKind.ChevronDown, width: 12, iconSize: 9, isEnabled: false)");
        captureSource.Should().Contain("CreateParityCapturedTitleBarButton(RibbonCommandIconKind.WindowMinimize)");
        hostCaptureSource.Should().Contain("EnsureFormulaBarVisibleForParityCapture(window);");
        hostCaptureSource.Should().Contain("window?.SuppressNextClosePrompt();");
        hostCaptureSource.Should().Contain("window.FindName(\"FormulaBarBorder\")");
        captureSource.Should().Contain("CaptureBackstageSurface(outputDirectory, surfaceId)");
        captureSource.Should().Contain("CreateParityCapturedBackstageSurface(surfaceId)");
        captureSource.Should().Contain("FreeXBackstageNavigationPlanner.Build()");
        captureSource.Should().Contain("FreeXBackstagePaneCatalog.BuildInfoActions(FreeXBackstageInfoSurface.ParityCapture)");
        captureSource.Should().Contain("FreeXBackstagePaneCatalog.BuildInfoDetails(FreeXBackstageInfoSurface.ParityCapture)");
        // The parity-captured Account pane mirrors the WPF host page ("Local account information"
        // with the local app/OS identity rows) rather than the 4-row product-info catalog, so the
        // Linux capture no longer mislabels Account as "Product information".
        captureSource.Should().Contain("BuildParityCapturedBackstageAccountRows()");
        captureSource.Should().Contain("Backstage_Account_LocalInfoHeading");
        captureSource.Should().Contain("CreateParityCapturedBackstageContentScroll(content)");
        captureSource.Should().Contain("CreateParityCapturedBackstageScrollbar()");
        captureSource.Should().Contain("CreateParityCapturedStatusBarFooter()");
        captureSource.Should().Contain("_ribbonContextSource.SetParityCaptureContext(null);");
        captureSource.Should().Contain("_ribbonContextSource.SetParityCaptureContext(activationKey);");
        captureSource.Should().Contain("LayoutWindow();");
        captureSource.Should().NotContain("ShowBackstageInfoDialogAsync()");
        captureSource.Should().NotContain("ShowBackstageExportDialogAsync()");
        captureSource.Should().NotContain("ShowBackstageAccountDialogAsync()");
    }

    [Fact]
    public void BackstageInfo_DelegatesDisplayTextToSharedPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Backstage.cs"));

        source.Should().Contain("WorkbookInfoDisplayPlanner.Build(");
        source.Should().Contain("WorkbookInfoDisplaySurface.AvaloniaBackstageInfoDialog");
        source.Should().Contain("FreeXBackstagePaneCatalog.BuildInfoDetails(FreeXBackstageInfoSurface.AvaloniaInfoDialog)");
        source.Should().Contain("FreeXBackstagePaneCatalog.BuildInfoActions(FreeXBackstageInfoSurface.AvaloniaInfoDialog)");
        source.Should().Contain("FreeXBackstagePaneCatalog.GetExportScopeLabelKey(");
        source.Should().Contain("FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(");
        source.Should().Contain("ToBackstageExportScopeId(option.Scope)");
        source.Should().Contain("ToBackstageExportOutputKindId(outputKind)");
        source.Should().NotContain("GetExportScopeLabelKey(option.Scope");
        source.Should().NotContain("GetExportOutputKindLabelKey(outputKind");
        source.Should().Contain("FreeXBackstagePaneCatalog.BuildAccountDetails()");
        source.Should().Contain("FreeXBackstagePaneCatalog.BuildAccountActions(plan.OptionsAvailable)");
        source.Should().NotContain("FormatBackstageFileSize");
        source.Should().NotContain("FormatBackstageLastModified");
        source.Should().NotContain("FormatBackstageProtection");
        source.Should().NotContain("FormatBackstageStatistics");
        source.Should().NotContain("FormatExportScopeLabel");
    }

    [Fact]
    public void BackstageDialogs_UseSharedAvaloniaBackstageChrome()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Backstage.cs"));
        var sharedSource = File.ReadAllText(RepoFile(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaBackstageChrome.cs"));

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("AvaloniaBackstageChromeStyle BackstageChromeStyle");
        source.Should().Contain("AvaloniaBackstageChrome.CreateDialogLayout(");
        source.Should().Contain("AvaloniaBackstageChrome.CreatePane(");
        source.Should().Contain("new AvaloniaBackstagePaneSpec(elements)");
        source.Should().Contain("AvaloniaBackstageDetailRowsElementSpec");
        source.Should().Contain("AvaloniaBackstageActionRowElementSpec");
        source.Should().Contain("AvaloniaBackstageRadioGroupElementSpec");
        source.Should().Contain("AvaloniaBackstageChrome.CreateActionButton(");
        source.Should().Contain("CreateBackstageClosingActionButtonSpec(");
        source.Should().NotContain("new ScrollViewer");
        source.Should().NotContain("new RadioButton");
        source.Should().NotContain("CreateBackstageDetailGrid");
        source.Should().NotContain("AddBackstageDetailRow");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateHeading(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateSectionHeader(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateNote(");
        source.Should().NotContain("AvaloniaBackstageChrome.CreateDetailGrid(");
        source.Should().NotContain("AvaloniaBackstageChrome.AddDetailRow(");
        source.Should().NotContain("new StackPanel { Spacing = 14 }");
        source.Should().NotContain("var rowIndex = grid.RowDefinitions.Count");
        source.Should().NotContain("new RowDefinition(GridLength.Auto)");
        source.Should().NotContain("ColumnDefinitions = new ColumnDefinitions(\"Auto,*\")");
        source.Should().NotContain("            LineHeight = 20");

        sharedSource.Should().Contain("public static class AvaloniaBackstageChrome");
        sharedSource.Should().Contain("public sealed record AvaloniaBackstagePaneSpec");
        sharedSource.Should().Contain("public sealed record AvaloniaBackstageRadioGroupElementSpec");
        sharedSource.Should().Contain("public static StackPanel CreatePane(");
        sharedSource.Should().Contain("public static DockPanel CreateDialogLayout(");
        sharedSource.Should().Contain("public static Grid CreateDetailRows(");
        sharedSource.Should().Contain("public static StackPanel CreateActionRow(");
        sharedSource.Should().Contain("public static StackPanel CreateRadioGroup(");
    }

    [Fact]
    public void NativeFileMenu_InstallsForMacOsDockAndMirrorsBackstageCommandGroups()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var appSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "App.cs"));
        var catalogSource = File.ReadAllText(RepoFile("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedCatalogSource = catalogSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        appSource.Should().Contain("private const string ApplicationTitle = \"FreeX\";");
        appSource.Should().Contain("Name = ApplicationTitle;");
        source.Should().Contain("private void InstallNativeMenu(NativeMenu menu)");
        source.Should().Contain("NativeDock.SetMenu(app, menu);");
        source.Should().Contain("NativeMenu.SetMenu(this, menu);");
        source.Should().Contain("InstallNativeMenu(_nativeMenu);");
        source.Should().Contain("private NativeMenu CreateNativeFileMenu()");
        source.Should().Contain("foreach (var entry in NativeMenuCatalog.FileMenuEntries)");
        source.Should().Contain("menu.Items.Add(GetNativeFileMenuItem(entry.Item!.Id));");

        var fileMenuCatalogBlock = ExtractSourceBlock(
            normalizedCatalogSource,
            "public static IReadOnlyList<NativeFileMenuEntryPlan> FileMenuEntries",
            "    ];");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.OpenRecent)");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.ShareWorkbook)");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.BackstageInfo)");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.Print)");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.PrintPreview)");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.BackstageExport)");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.ExportPdf)");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.PageSetup)");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.BackstageAccount)");
        fileMenuCatalogBlock.Should().Contain("FileItem(NativeFileMenuItemId.Options)");
        source.Should().Contain("NativeFileMenuItemId.PageSetup => _filePageSetupMenuItem");

        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.OpenRecent", "NativeFileMenuItemId.ShareWorkbook");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.ShareWorkbook", "NativeFileMenuItemId.BackstageInfo");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.BackstageInfo", "NativeFileMenuItemId.Save");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.SaveAs", "NativeFileMenuItemId.Print");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.PrintPreview", "NativeFileMenuItemId.BackstageExport");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.BackstageExport", "NativeFileMenuItemId.ExportPdf");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.ExportPdf", "NativeFileMenuItemId.WorkbookStatistics");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.WorkbookStatistics", "NativeFileMenuItemId.PageSetup");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.PageSetup", "NativeFileMenuItemId.CloseWorkbook");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.CloseWorkbook", "NativeFileMenuItemId.BackstageAccount");
        AssertBefore(fileMenuCatalogBlock, "NativeFileMenuItemId.BackstageAccount", "NativeFileMenuItemId.Options");
    }

    [Fact]
    public void NativeMenuBar_UsesRibbonAndBackstageTopLevelOrder()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var catalogSource = File.ReadAllText(RepoFile("src", "FreeX.App.Presentation", "Shell", "NativeMenuCatalog.cs"));
        var smokeSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedCatalogSource = catalogSource.Replace("\r\n", "\n", StringComparison.Ordinal);

        var nativeMenuBlock = ExtractSourceBlock(
            normalizedSource,
            "_nativeMenu = new NativeMenu();",
            "_nativeMenu.NeedsUpdate += (_, _) => UpdateSaveButton();");

        nativeMenuBlock.Should().Contain("AddNativeTopLevelMenus(_nativeMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.File] = fileMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.Home] = homeMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.Insert] = insertMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.PageLayout] = pageLayoutMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.Formulas] = formulasMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.Data] = dataMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.Review] = reviewMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.View] = viewMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.Sheet] = sheetMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.Window] = windowMenu");
        nativeMenuBlock.Should().Contain("[NativeMenuTopLevelId.Help] = helpMenu");

        var topLevelCatalogBlock = ExtractSourceBlock(
            normalizedCatalogSource,
            "public static IReadOnlyList<NativeMenuTopLevelPlan> TopLevelMenus",
            "    ];");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.File, \"File\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.Home, \"Home\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.Insert, \"Insert\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.PageLayout, \"Page Layout\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.Formulas, \"Formulas\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.Data, \"Data\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.Review, \"Review\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.View, \"View\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.Sheet, \"Sheet\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.Window, \"Window\")");
        topLevelCatalogBlock.Should().Contain("new(NativeMenuTopLevelId.Help, \"Help\")");
        nativeMenuBlock.Should().NotContain("Header = \"Edit\"");
        nativeMenuBlock.Should().NotContain("Header = \"Format\"");

        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.File", "NativeMenuTopLevelId.Home");
        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.Home", "NativeMenuTopLevelId.Insert");
        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.Insert", "NativeMenuTopLevelId.PageLayout");
        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.PageLayout", "NativeMenuTopLevelId.Formulas");
        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.Formulas", "NativeMenuTopLevelId.Data");
        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.Data", "NativeMenuTopLevelId.Review");
        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.Review", "NativeMenuTopLevelId.View");
        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.View", "NativeMenuTopLevelId.Sheet");
        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.Sheet", "NativeMenuTopLevelId.Window");
        AssertBefore(topLevelCatalogBlock, "NativeMenuTopLevelId.Window", "NativeMenuTopLevelId.Help");

        source.Should().Contain("var homeMenu = CreateNativeMenu(NativeMenuTopLevelId.Home);");
        source.Should().Contain("var insertMenu = CreateNativeMenu(NativeMenuTopLevelId.Insert);");
        source.Should().Contain("var pageLayoutMenu = CreateNativeMenu(NativeMenuTopLevelId.PageLayout);");
        source.Should().Contain("var formulasMenu = CreateNativeMenu(NativeMenuTopLevelId.Formulas);");
        source.Should().Contain("var dataMenu = CreateNativeMenu(NativeMenuTopLevelId.Data);");
        source.Should().Contain("var reviewMenu = CreateNativeMenu(NativeMenuTopLevelId.Review);");
        source.Should().Contain("var viewMenu = CreateNativeMenu(NativeMenuTopLevelId.View);");
        source.Should().Contain("var sheetMenu = CreateNativeMenu(NativeMenuTopLevelId.Sheet);");
        source.Should().Contain("var windowMenu = CreateNativeMenu(NativeMenuTopLevelId.Window);");
        source.Should().Contain("var helpMenu = CreateNativeMenu(NativeMenuTopLevelId.Help);");

        var homeMenuCatalogBlock = ExtractSourceBlock(
            normalizedCatalogSource,
            "public static IReadOnlyList<NativeMenuEntryPlan> HomeMenuEntries",
            "    ];");
        homeMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.FormatPainter)");
        homeMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.ConditionalFormatting)");
        homeMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.FillCells)");
        homeMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.Clear)");
        homeMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.Find)");

        var pageLayoutMenuCatalogBlock = ExtractSourceBlock(
            normalizedCatalogSource,
            "public static IReadOnlyList<NativeMenuEntryPlan> PageLayoutMenuEntries",
            "    ];");
        pageLayoutMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.Themes)");
        pageLayoutMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.PageMargins)");
        pageLayoutMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.PrintArea)");
        pageLayoutMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.PageBreaks)");
        pageLayoutMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.SheetBackground)");
        pageLayoutMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.PageSetup)");
        AssertBefore(pageLayoutMenuCatalogBlock, "NativeMenuItemId.Themes", "NativeMenuItemId.PageMargins");
        AssertBefore(pageLayoutMenuCatalogBlock, "NativeMenuItemId.PageMargins", "NativeMenuItemId.PageSetup");
        AssertBefore(pageLayoutMenuCatalogBlock, "NativeMenuItemId.PageSetup", "NativeMenuItemId.PrintGridlines");

        var formulasMenuCatalogBlock = ExtractSourceBlock(
            normalizedCatalogSource,
            "public static IReadOnlyList<NativeMenuEntryPlan> FormulasMenuEntries",
            "    ];");
        formulasMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.AutoSum)");
        formulasMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.InsertFunction)");
        formulasMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.NameManager)");
        formulasMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.DefineName)");
        formulasMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.CreateNamesFromSelection)");
        formulasMenuCatalogBlock.Should().Contain("Item(NativeMenuItemId.ShowFormulas)");
        AssertBefore(formulasMenuCatalogBlock, "NativeMenuItemId.AutoSum", "NativeMenuItemId.InsertFunction");
        AssertBefore(formulasMenuCatalogBlock, "NativeMenuItemId.InsertFunction", "NativeMenuItemId.NameManager");
        AssertBefore(formulasMenuCatalogBlock, "NativeMenuItemId.CreateNamesFromSelection", "NativeMenuItemId.ShowFormulas");
        source.Should().NotContain("var pageLayoutMenu = new NativeMenu();");
        source.Should().NotContain("pageLayoutMenu.Items.Add(");
        source.Should().NotContain("formulasMenu.Items.Add(");

        smokeSource.Should().Contain("NativeTopLevelMenuOrder");
        smokeSource.Should().Contain("NativeDockTopLevelMenuOrder");
        smokeSource.Should().Contain("File|Home|Insert|Page Layout|Formulas|Data|Review|View|Sheet|Window|Help");
        smokeSource.Should().Contain("native_top_level_menu_order={snapshot.NativeTopLevelMenuOrder}");
        smokeSource.Should().Contain("native_dock_top_level_menu_order={snapshot.NativeDockTopLevelMenuOrder}");
        smokeSource.Should().Contain("native_dock_file_menu_item_count={snapshot.NativeDockFileMenuItemCount}");
    }

    [Fact]
    public void NativeMenuBar_DoesNotAttachTheSameMenuItemToMultipleParents()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var duplicates = System.Text.RegularExpressions.Regex
            .Matches(source, @"\.Items\.Add\((_\w+MenuItem)\)")
            .Select(match => match.Groups[1].Value)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToArray();

        duplicates.Should().BeEmpty("Avalonia NativeMenuItem instances can only have one NativeMenu parent");
        source.Should().Contain("ConfigurePageSetupNativeMenuItem(_filePageSetupMenuItem);");
        source.Should().Contain("_pageSetupMenuItem.Click += async (_, _) => await ShowPageSetupDialogAsync();");
    }

    [Fact]
    public void InsertObjects_DelegatesDrawingInsertionToSharedPlanner()
    {
        var insertObjectsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.InsertObjects.cs"));
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        insertObjectsSource.Should().Contain("foreach (var group in DrawingInsertionPlanner.ShapeGroups)");
        insertObjectsSource.Should().Contain("DrawingInsertionPlanner.BuildShapeCommand(");
        insertObjectsSource.Should().Contain("DrawingInsertionPlanner.BuildTextBoxCommand(");
        mainSource.Should().Contain("DrawingInsertionPlanner.DefaultShape");
        File.Exists(RepoFile("src", "FreeX.App.Avalonia", "InsertShapeCommandFactory.cs")).Should().BeFalse();
        File.Exists(RepoFile("src", "FreeX.App.Avalonia", "InsertTextBoxCommandFactory.cs")).Should().BeFalse();
    }

    [Fact]
    public void DrawingObjectCommands_DelegateToSharedPlanner()
    {
        var contextualTabsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PictureShapeTabs.cs"));
        var formatDialogSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DrawingFormatDialogs.cs"));

        contextualTabsSource.Should().Contain("DrawingObjectCommandPlanner.BuildZOrderCommand(");
        contextualTabsSource.Should().Contain("DrawingObjectFormatCommandPolicy.BuildRotationCommand(");
        contextualTabsSource.Should().Contain("DrawingObjectFormatCommandPolicy.BuildResizeCommand(");
        contextualTabsSource.Should().Contain("DrawingObjectFormatCommandPolicy.BuildAltTextCommand(");
        contextualTabsSource.Should().Contain("DrawingObjectCommandPlanner.BuildFillColorCommand(");
        contextualTabsSource.Should().Contain("DrawingObjectCommandPlanner.BuildOutlineColorCommand(");
        contextualTabsSource.Should().Contain("DrawingObjectFormatCommandPolicy.ResolveSelectedFormatTarget(");
        contextualTabsSource.Should().Contain("DrawingObjectFormatCommandPolicy.SupportsFillAndOutline(");
        contextualTabsSource.Should().Contain("DrawingObjectFormatCommandPolicy.ResolveFillColor(");
        contextualTabsSource.Should().Contain("DrawingObjectFormatCommandPolicy.ResolveOutlineColor(");
        contextualTabsSource.Should().Contain("FormatPicturePlanner.TryCreateRotationResult(");
        contextualTabsSource.Should().Contain("ObjectSizeDialogPlanner.TryCreateSize(");
        contextualTabsSource.Should().Contain("new ObjectSizeDialogSubmission(");
        contextualTabsSource.Should().Contain("DrawingTargetResolver.ResolveSelectedPicture(");
        contextualTabsSource.Should().Contain("DrawingTargetResolver.ResolveSelectedDrawingShape(");
        formatDialogSource.Should().Contain("DrawingObjectFormatCommandPolicy.BuildFormatCommands(");

        contextualTabsSource.Should().NotContain("_session.ActiveSheet.Pictures.FirstOrDefault(");
        contextualTabsSource.Should().NotContain("_session.ActiveSheet.DrawingShapes.FirstOrDefault(");
        contextualTabsSource.Should().NotContain("switch (_selectedDrawingObjectKind)");
        contextualTabsSource.Should().NotContain("new MoveSelectionPaneObjectCommand(");
        contextualTabsSource.Should().NotContain("new BringDrawingShapeForwardCommand(");
        contextualTabsSource.Should().NotContain("new SendDrawingShapeBackwardCommand(");
        contextualTabsSource.Should().NotContain("new SetDrawingObjectRotationCommand(");
        contextualTabsSource.Should().NotContain("DrawingObjectCommandPlanner.BuildRotateCommand(");
        contextualTabsSource.Should().NotContain("DrawingObjectCommandPlanner.BuildResizeCommand(");
        contextualTabsSource.Should().NotContain("DrawingObjectCommandPlanner.BuildAltTextCommand(");
        contextualTabsSource.Should().NotContain("new SetDrawingShapeColorsCommand(");
        contextualTabsSource.Should().NotContain("new ResizePictureCommand(");
        contextualTabsSource.Should().NotContain("new ResizeDrawingShapeCommand(");
        contextualTabsSource.Should().NotContain("new SetPictureAltTextCommand(");
        contextualTabsSource.Should().NotContain("new SetDrawingShapeAltTextCommand(");
        formatDialogSource.Should().NotContain("new SetDrawingObjectRotationCommand(");
        formatDialogSource.Should().NotContain("new ResizePictureCommand(");
        formatDialogSource.Should().NotContain("new ResizeDrawingShapeCommand(");
        formatDialogSource.Should().NotContain("new SetPictureLockAspectRatioCommand(");
        formatDialogSource.Should().NotContain("new SetPictureAltTextCommand(");
        formatDialogSource.Should().NotContain("new SetDrawingShapeAltTextCommand(");
        formatDialogSource.Should().NotContain("DrawingObjectCommandPlanner.BuildResizeCommand(");
        formatDialogSource.Should().NotContain("DrawingObjectCommandPlanner.BuildRotateCommand(");
        formatDialogSource.Should().NotContain("DrawingObjectCommandPlanner.BuildAltTextCommand(");
    }

    [Fact]
    public void DrawingObjectOverlay_DelegatesDisplayBoundsToSharedViewportPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var sharedPlanner = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "DrawingUI",
            "DrawingObjectViewportPlanner.cs"));
        var displayBoundsBlock = ExtractSourceBlock(
            normalizedSource,
            "private static bool TryGetDisplayedDrawingObjectBounds(",
            "private static bool TryGetDisplayedCellBounds(");

        source.Should().Contain("using FreeX.App.Presentation.DrawingUI;");
        displayBoundsBlock.Should().Contain("DrawingObjectViewportPlanner.TryCreateDisplayedObjectRect(");
        displayBoundsBlock.Should().Contain("var rowHeaderWidth = showHeadings ? GetRowHeaderWidth(viewport, zoomFactor) : 0;");
        displayBoundsBlock.Should().Contain("var columnHeaderHeight = showHeadings ? HeaderRowHeight * zoomFactor : 0;");
        displayBoundsBlock.Should().NotContain("TryGetDisplayedColumnLeft(viewport.ColMetrics, drawingObject.AnchorCol");
        displayBoundsBlock.Should().NotContain("TryGetDisplayedRowTop(viewport.RowMetrics, drawingObject.AnchorRow");
        sharedPlanner.Should().Contain("rowHeaderWidth + (drawingObject.Left * zoomFactor)");
        sharedPlanner.Should().Contain("columnHeaderHeight + (drawingObject.Top * zoomFactor)");
        source.Should().Contain("DrawingObjectViewportPlanner.ShouldDisplayObjectRect(");
        source.Should().Contain("new LayoutRect(left, top, width, height)");
    }

    [Fact]
    public void TableDesign_DelegatesCommandCompositionToSharedPlanner()
    {
        var tableTabSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableDesignTab.cs"));
        var tableNameSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableName.cs"));
        var tableResizeSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableResize.cs"));
        var tableStyleSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableStyleGallery.cs"));

        tableTabSource.Should().Contain("TableDesignCommandPlanner.TryGetActiveStructuredTable(");
        tableTabSource.Should().Contain("TableDesignCommandPlanner.BuildConvertToRangePlan(");
        tableTabSource.Should().Contain("plan.TableDisplayName");
        tableTabSource.Should().Contain("TableDesignCommandPlanner.BuildStyleOptionsCommand(");
        tableTabSource.Should().Contain("TableDesignCommandPlanner.GetDisplayName(table)");
        tableNameSource.Should().Contain("TableDesignCommandPlanner.BuildRenameCommand(");
        tableResizeSource.Should().Contain("TableDesignCommandPlanner.BuildResizeCommand(");
        tableStyleSource.Should().Contain("TableDesignCommandPlanner.BuildApplyStyleCommand(");
        tableTabSource.Should().NotContain("new ReapplyStructuredTableStyleCommand(");
        tableTabSource.Should().NotContain("new SetStructuredTableTotalsRowCommand(");
        tableResizeSource.Should().NotContain("private IWorkbookCommand BuildResizeCommand(");
    }

    [Fact]
    public void PageSetup_DelegatesChoiceMappingToSharedModel()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PageLayout.cs"));

        source.Should().Contain("PageSetupDialogPlanner.OrientationChoices");
        source.Should().Contain("PageSetupDialogPlanner.PaperSizeChoices");
        source.Should().Contain("PageSetupDialogPlanner.PageOrderChoices");
        source.Should().Contain("PageSetupDialogPlanner.PrintErrorValueChoices");
        source.Should().Contain("PageSetupDialogPlanner.PrintCommentChoices");
        source.Should().Contain("PageSetupDialogPlanner.ResolveChoiceLabels(");
        source.Should().Contain("PageSetupDialogModel.HeaderPresetChoices");
        source.Should().Contain("PageSetupDialogModel.FooterPresetChoices");
        source.Should().Contain("HeaderFooterEditorPlanner.ApplyCenterPreset(");
        source.Should().Contain(".IndexOf(initial.Orientation)");
        source.Should().Contain(".ValueAt(orientationBox.SelectedIndex)");
        source.Should().Contain("PageSetupDialogModel.HeaderFooterPresetIndex(");
        source.Should().Contain("PageSetupDialogModel.HeaderFooterPresetValue(");
        source.Should().Contain("PageSetupSubmissionPlanner.TryBuild(");
        source.Should().Contain("SelectValidationRoute(validation.Route)");
        source.Should().Contain("ResolvePageSetupValidationIssue(validation)");
        source.Should().NotContain("PageSetupDialogModel.ChoiceIndex(");
        source.Should().NotContain("PageSetupDialogModel.ChoiceValue(");
        source.Should().NotContain("PageSetupDialogModel.TryBuildCommand(_session.ActiveSheet, fields)");
        source.Should().NotContain("headerCenterBox.Text = PageSetupDialogModel.HeaderFooterPresetValue(");
        source.Should().NotContain("footerCenterBox.Text = PageSetupDialogModel.HeaderFooterPresetValue(");
        source.Should().NotContain("private static int PageSetupPresetIndex");
        source.Should().NotContain("var presetLabels = new[]");
        source.Should().NotContain("initial.PageOrder == WorksheetPageOrder.OverThenDown ? 1 : 0");
        source.Should().NotContain("WorksheetPrintErrorValue ReadErrorValue()");
        source.Should().NotContain("WorksheetPrintComments ReadComments()");
    }

    [Fact]
    public void PageBreakActions_DelegateMenuPolicyToSharedPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PageBreakActions.cs"));
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ApplyPageBreakAction(PageBreakMenuAction.Insert)");
        source.Should().Contain("ApplyPageBreakAction(PageBreakMenuAction.Remove)");
        source.Should().Contain("ApplyPageBreakAction(PageBreakMenuAction.ResetAll)");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.PlanPageBreakAction(");
        source.Should().Contain("PageLayoutRibbonCommandPlanner.BuildPageBreaksCommand(sheet.Id, plan)");
        source.Should().NotContain("private enum PageBreakAction");
        source.Should().NotContain("new SetPageBreaksCommand(");
        source.Should().NotContain("PageBreakActionPlanner.Insert(");
        source.Should().NotContain("PageBreakActionPlanner.Remove(");
        mainSource.Should().Contain("ApplyPageBreakAction(PageBreakMenuAction.Insert)");
        mainSource.Should().Contain("ApplyPageBreakAction(PageBreakMenuAction.Remove)");
        mainSource.Should().Contain("ApplyPageBreakAction(PageBreakMenuAction.ResetAll)");
    }

    [Fact]
    public void PrintPreview_DelegatesPaginationContextToSharedPresentationModel()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PrintPreview.cs"));
        var sharedSource = File.ReadAllText(RepoFile("src", "FreeX.App.Presentation", "PageLayout", "PrintPreviewPaginationContext.cs"));

        source.Should().Contain("PrintPreviewPaginationContext.TryCreate(_session.Workbook, sheet, PrintPreviewTextMeasurer, out var context)");
        source.Should().NotContain("internal sealed class PrintPreviewPaginationContext");
        source.Should().NotContain("var plan = PagePaginationPlanner.Paginate(");
        sharedSource.Should().Contain("PageBreakPreviewInstructionBuilder.TryResolvePrintRange(sheet, out var printRange)");
        sharedSource.Should().Contain("PagePaginationPlanner.Paginate(");
    }

    [Fact]
    public void WorkbookProgressStatus_DelegatesOpenAndSaveTextToSharedFormatter()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("WorkbookProgressTextFormatter");
        source.Should().Contain(".FormatOpen(\"preparing\", TimeSpan.Zero, percent: null, UiText.Get)");
        source.Should().Contain("WorkbookProgressTextFormatter.FormatOpen(update, UiText.Get).Detail");
        source.Should().Contain(".FormatSave(\"preparing\", TimeSpan.Zero, percent: null, UiText.Get)");
        source.Should().Contain("WorkbookProgressTextFormatter.FormatSave(update, UiText.Get).Detail");

        source.Should().NotContain("private static string FormatOpenStatus(");
        source.Should().NotContain("private static string FormatSaveStatus(");
        source.Should().NotContain("\"Opening...\"");
        source.Should().NotContain("\"Saving...\"");
        source.Should().NotContain("\"Preparing save...\"");
        source.Should().NotContain("\"Writing file...\"");
    }

    [Fact]
    public void StatusBarZoomSlider_UsesIdenticalMinMiddleMaxMarks()
    {
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var statusBarSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.StatusBar.cs"));
        var captureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        mainSource.Should().Contain("var statusZoomPlan = StatusBarZoomSliderPlanner.Build(_session.ZoomPercent);");
        mainSource.Should().Contain("_statusZoomSlider.Minimum = statusZoomPlan.MinimumSliderValue;");
        mainSource.Should().Contain("_statusZoomSlider.Maximum = statusZoomPlan.MaximumSliderValue;");
        mainSource.Should().Contain("var inputPlan = StatusBarZoomSliderPlanner.BuildInput(args.NewValue);");
        mainSource.Should().Contain("foreach (var left in zoomSliderPlan.VisualTickLefts)");
        mainSource.Should().Contain("StatusBarZoomSliderPlanner.BuildThumbPlan(");
        statusBarSource.Should().Contain("var sliderPlan = StatusBarZoomSliderPlanner.Build(plan.ZoomPercent);");
        mainSource.Should().Contain("Width = 1,");
        mainSource.Should().Contain("Height = 4,");
        mainSource.Should().NotContain("BuildStatusZoomTick(left: 60, isMiddle: true)");
        mainSource.Should().NotContain("isMiddle ? 2 : 1");
        mainSource.Should().NotContain("ZoomLevelMapper.ZoomPercentToSlider(SetWorksheetZoomCommand.MinZoomPercent)");
        mainSource.Should().NotContain("ZoomLevelMapper.SliderToZoomPercent(args.NewValue)");
        captureSource.Should().Contain("foreach (var left in new[] { 8d, 60d, 111d })");
        captureSource.Should().Contain("Canvas.SetLeft(canvas.Children[^1], 55.5);");
        captureSource.Should().NotContain("isMiddle ? 2 : 1");
    }

    [Fact]
    public void LaunchSmokeStatusValue_AllowsSharedStatusBarReadouts()
    {
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var statusBarSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.StatusBar.cs"));

        mainSource.Should().Contain("HasStatusTextValue: HasStatusBarAccessibleValue()");
        mainSource.Should().Contain("private bool HasStatusBarAccessibleValue() =>");
        mainSource.Should().Contain("!string.IsNullOrWhiteSpace(_statusText.Text) ||");
        mainSource.Should().Contain("!string.IsNullOrWhiteSpace(_selectionStatsText.Text);");
        statusBarSource.Should().Contain("_statusText.IsVisible = visibility.ReadyTextVisible && plan.VisibleReadoutText.Length == 0;");
        statusBarSource.Should().Contain("_selectionStatsText.Text = plan.VisibleReadoutText;");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }

    private static string ExtractSourceBlock(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"source should contain '{start}'");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThanOrEqualTo(0, $"source should contain '{end}' after '{start}'");
        return source[startIndex..(endIndex + end.Length)];
    }

    [Fact]
    public void AccessibilityCheckerDialog_HasIssueListGoToCloseAndDispatchesViaNewMethod()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.AccessibilityChecker.cs"));
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var parityCaptureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        // New partial file has the dedicated dialog method
        source.Should().Contain("private async Task ShowAccessibilityCheckerDialogAsync()");
        source.Should().Contain("private async Task ShowAccessibilityCheckerCleanDialogAsync()");
        source.Should().Contain("private async Task ShowAccessibilityCheckerIssuesDialogAsync(");

        // Issues dialog has the key WPF-parity controls. Redesigned to the Excel task-pane layout:
        // an "Inspection Results" TreeView grouped by Errors/Warnings/Tips (replacing the flat
        // ListBox), keyed by the same AccessibilityCheckerIssueList automation id.
        source.Should().Contain("new TreeView");
        source.Should().Contain("AutomationProperties.SetAutomationId(resultsTree, \"AccessibilityCheckerIssueList\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(goToButton, \"AccessibilityCheckerGoToButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(closeButton, \"AccessibilityCheckerCloseButton\");");

        // Go To navigates via _session.GoToAccessibilityIssue (shared portable logic)
        source.Should().Contain("_session.GoToAccessibilityIssue(selectedIssue)");
        source.Should().NotContain("ReviewWorkflowPlanner.GetAccessibilityNavigationTarget(",
            "navigation target resolution must stay in the shared session layer, not be duplicated in the shell");

        // Uses shared localization keys (the Excel-style title resolves via the shared
        // AccessibilityChecker_Title catalog key)
        source.Should().Contain("AcText(\"AccessibilityChecker_Title\"");
        source.Should().Contain("UiText.Get(\"ShellLoc_AccessibilityCheckerGoToButton\")");
        source.Should().Contain("UiText.Get(\"ShellLoc_AccessibilityCheckerNoIssues\")");

        // MainWindow.cs dispatches to the new method (not inline to ReviewSummary)
        mainSource.Should().Contain("_checkAccessibilityMenuItem.Click += async (_, _) => await ShowAccessibilityCheckerDialogAsync();");
        mainSource.Should().Contain("[\"review.checkAccessibility\"] = () => _ = ShowAccessibilityCheckerDialogAsync(),");

        // Parity capture uses the new method
        parityCaptureSource.Should().Contain("(\"dialog.AccessibilityChecker\", () => ShowAccessibilityCheckerDialogAsync()),");
    }

    private static void AssertBefore(string source, string first, string second)
    {
        source.IndexOf(first, StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf(second, StringComparison.Ordinal), $"{first} should appear before {second}");
    }
}
