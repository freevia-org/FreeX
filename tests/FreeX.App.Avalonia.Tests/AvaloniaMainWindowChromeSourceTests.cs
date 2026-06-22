using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaMainWindowChromeSourceTests
{
    [Fact]
    public void QuickAnalysisShell_UsesSharedGroupTitleMetadata()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("QuickAnalysisShellPlanner.GroupTitleResourceKey(group.Group)");
        source.Should().NotContain("QuickAnalysisGroupTitle(");
        source.Should().NotContain("QuickAnalysisGroup.Formatting => UiText.Get");
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

        source.Should().Contain("var rowDelta = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;");
        source.Should().Contain("_session.MoveActiveCell(rowDelta, 0);");
        source.Should().Contain("FocusShellRegion(ShellFocusRegion.Worksheet);");
        source.Should().Contain("_session.MoveActiveCell(0, colDelta);");
        source.Should().Contain("private static bool IsFormulaPointModeText(string? text)");
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
        captureSource.Should().Contain("FreeXBackstagePaneCatalog.BuildAccountDetails()");
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
        source.Should().Contain("FreeXBackstagePaneCatalog.BuildAccountDetails()");
        source.Should().Contain("FreeXBackstagePaneCatalog.BuildAccountActions(plan.OptionsAvailable)");
        source.Should().NotContain("FormatBackstageFileSize");
        source.Should().NotContain("FormatBackstageLastModified");
        source.Should().NotContain("FormatBackstageProtection");
        source.Should().NotContain("FormatBackstageStatistics");
        source.Should().NotContain("FormatExportScopeLabel");
    }

    [Fact]
    public void NativeFileMenu_InstallsForMacOsDockAndMirrorsBackstageCommandGroups()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var appSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "App.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        appSource.Should().Contain("private const string ApplicationTitle = \"FreeX\";");
        appSource.Should().Contain("Name = ApplicationTitle;");
        source.Should().Contain("private void InstallNativeMenu(NativeMenu menu)");
        source.Should().Contain("NativeDock.SetMenu(app, menu);");
        source.Should().Contain("NativeMenu.SetMenu(this, menu);");
        source.Should().Contain("InstallNativeMenu(_nativeMenu);");

        var fileMenuBlock = ExtractSourceBlock(
            normalizedSource,
            "var fileMenu = new NativeMenu();",
            "fileMenu.Items.Add(_quitMenuItem);");
        fileMenuBlock.Should().Contain("fileMenu.Items.Add(_openRecentMenuItem);");
        fileMenuBlock.Should().Contain("fileMenu.Items.Add(_shareWorkbookMenuItem);");
        fileMenuBlock.Should().Contain("fileMenu.Items.Add(_backstageInfoMenuItem);");
        fileMenuBlock.Should().Contain("fileMenu.Items.Add(_printMenuItem);");
        fileMenuBlock.Should().Contain("fileMenu.Items.Add(_printPreviewMenuItem);");
        fileMenuBlock.Should().Contain("fileMenu.Items.Add(_backstageExportMenuItem);");
        fileMenuBlock.Should().Contain("fileMenu.Items.Add(_exportPdfMenuItem);");
        fileMenuBlock.Should().Contain("fileMenu.Items.Add(_backstageAccountMenuItem);");
        fileMenuBlock.Should().Contain("fileMenu.Items.Add(_optionsMenuItem);");

        AssertBefore(fileMenuBlock, "_openRecentMenuItem", "_shareWorkbookMenuItem");
        AssertBefore(fileMenuBlock, "_shareWorkbookMenuItem", "_backstageInfoMenuItem");
        AssertBefore(fileMenuBlock, "_backstageInfoMenuItem", "_saveMenuItem");
        AssertBefore(fileMenuBlock, "_saveAsMenuItem", "_printMenuItem");
        AssertBefore(fileMenuBlock, "_printPreviewMenuItem", "_backstageExportMenuItem");
        AssertBefore(fileMenuBlock, "_backstageExportMenuItem", "_exportPdfMenuItem");
        AssertBefore(fileMenuBlock, "_exportPdfMenuItem", "_workbookStatisticsMenuItem");
        AssertBefore(fileMenuBlock, "_closeWorkbookMenuItem", "_backstageAccountMenuItem");
        AssertBefore(fileMenuBlock, "_backstageAccountMenuItem", "_optionsMenuItem");
    }

    [Fact]
    public void NativeMenuBar_UsesRibbonAndBackstageTopLevelOrder()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var smokeSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        var nativeMenuBlock = ExtractSourceBlock(
            normalizedSource,
            "_nativeMenu = new NativeMenu();",
            "_nativeMenu.NeedsUpdate += (_, _) => UpdateSaveButton();");

        nativeMenuBlock.Should().Contain("Header = \"File\"");
        nativeMenuBlock.Should().Contain("Header = \"Home\"");
        nativeMenuBlock.Should().Contain("Header = \"Insert\"");
        nativeMenuBlock.Should().Contain("Header = \"Page Layout\"");
        nativeMenuBlock.Should().Contain("Header = \"Formulas\"");
        nativeMenuBlock.Should().Contain("Header = \"Data\"");
        nativeMenuBlock.Should().Contain("Header = \"Review\"");
        nativeMenuBlock.Should().Contain("Header = \"View\"");
        nativeMenuBlock.Should().Contain("Header = \"Sheet\"");
        nativeMenuBlock.Should().Contain("Header = \"Window\"");
        nativeMenuBlock.Should().Contain("Header = \"Help\"");
        nativeMenuBlock.Should().NotContain("Header = \"Edit\"");
        nativeMenuBlock.Should().NotContain("Header = \"Format\"");

        AssertBefore(nativeMenuBlock, "Header = \"File\"", "Header = \"Home\"");
        AssertBefore(nativeMenuBlock, "Header = \"Home\"", "Header = \"Insert\"");
        AssertBefore(nativeMenuBlock, "Header = \"Insert\"", "Header = \"Page Layout\"");
        AssertBefore(nativeMenuBlock, "Header = \"Page Layout\"", "Header = \"Formulas\"");
        AssertBefore(nativeMenuBlock, "Header = \"Formulas\"", "Header = \"Data\"");
        AssertBefore(nativeMenuBlock, "Header = \"Data\"", "Header = \"Review\"");
        AssertBefore(nativeMenuBlock, "Header = \"Review\"", "Header = \"View\"");
        AssertBefore(nativeMenuBlock, "Header = \"View\"", "Header = \"Sheet\"");
        AssertBefore(nativeMenuBlock, "Header = \"Sheet\"", "Header = \"Window\"");
        AssertBefore(nativeMenuBlock, "Header = \"Window\"", "Header = \"Help\"");

        var homeMenuBlock = ExtractSourceBlock(
            normalizedSource,
            "var homeMenu = new NativeMenu();",
            "homeMenu.Items.Add(_openHyperlinkMenuItem);");
        homeMenuBlock.Should().Contain("homeMenu.Items.Add(_formatPainterMenuItem);");
        homeMenuBlock.Should().Contain("homeMenu.Items.Add(_conditionalFormattingMenuItem);");
        homeMenuBlock.Should().Contain("homeMenu.Items.Add(_fillCellsMenuItem);");
        homeMenuBlock.Should().Contain("homeMenu.Items.Add(_clearMenuItem);");
        homeMenuBlock.Should().Contain("homeMenu.Items.Add(_findMenuItem);");

        var pageLayoutMenuBlock = ExtractSourceBlock(
            normalizedSource,
            "var pageLayoutMenu = new NativeMenu();",
            "pageLayoutMenu.Items.Add(_printHeadingsMenuItem);");
        pageLayoutMenuBlock.Should().Contain("pageLayoutMenu.Items.Add(_themesMenuItem);");
        pageLayoutMenuBlock.Should().Contain("pageLayoutMenu.Items.Add(_pageMarginsMenuItem);");
        pageLayoutMenuBlock.Should().Contain("pageLayoutMenu.Items.Add(_printAreaMenuItem);");
        pageLayoutMenuBlock.Should().Contain("pageLayoutMenu.Items.Add(_pageBreaksMenuItem);");
        pageLayoutMenuBlock.Should().Contain("pageLayoutMenu.Items.Add(_sheetBackgroundMenuItem);");
        pageLayoutMenuBlock.Should().Contain("pageLayoutMenu.Items.Add(_pageSetupMenuItem);");

        smokeSource.Should().Contain("NativeTopLevelMenuOrder");
        smokeSource.Should().Contain("File|Home|Insert|Page Layout|Formulas|Data|Review|View|Sheet|Window|Help");
        smokeSource.Should().Contain("native_top_level_menu_order={snapshot.NativeTopLevelMenuOrder}");
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
        contextualTabsSource.Should().Contain("DrawingObjectCommandPlanner.BuildRotateCommand(");
        contextualTabsSource.Should().Contain("DrawingObjectCommandPlanner.BuildResizeCommand(");
        contextualTabsSource.Should().Contain("DrawingObjectCommandPlanner.BuildAltTextCommand(");
        formatDialogSource.Should().Contain("DrawingObjectCommandPlanner.BuildResizeCommand(");
        formatDialogSource.Should().Contain("DrawingObjectCommandPlanner.BuildRotateCommand(");
        formatDialogSource.Should().Contain("DrawingObjectCommandPlanner.BuildAltTextCommand(");

        contextualTabsSource.Should().NotContain("new MoveSelectionPaneObjectCommand(");
        contextualTabsSource.Should().NotContain("new BringDrawingShapeForwardCommand(");
        contextualTabsSource.Should().NotContain("new SendDrawingShapeBackwardCommand(");
        contextualTabsSource.Should().NotContain("new SetDrawingObjectRotationCommand(");
        contextualTabsSource.Should().NotContain("new ResizePictureCommand(");
        contextualTabsSource.Should().NotContain("new ResizeDrawingShapeCommand(");
        contextualTabsSource.Should().NotContain("new SetPictureAltTextCommand(");
        contextualTabsSource.Should().NotContain("new SetDrawingShapeAltTextCommand(");
        formatDialogSource.Should().NotContain("new SetDrawingObjectRotationCommand(");
        formatDialogSource.Should().NotContain("new ResizePictureCommand(");
        formatDialogSource.Should().NotContain("new ResizeDrawingShapeCommand(");
        formatDialogSource.Should().NotContain("new SetPictureAltTextCommand(");
        formatDialogSource.Should().NotContain("new SetDrawingShapeAltTextCommand(");
    }

    [Fact]
    public void TableDesign_DelegatesCommandCompositionToSharedPlanner()
    {
        var tableTabSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableDesignTab.cs"));
        var tableNameSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableName.cs"));
        var tableResizeSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableResize.cs"));
        var tableStyleSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.TableStyleGallery.cs"));

        tableTabSource.Should().Contain("TableDesignCommandPlanner.TryGetActiveStructuredTable(");
        tableTabSource.Should().Contain("TableDesignCommandPlanner.BuildConvertToRangeCommand(");
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

        source.Should().Contain("PageSetupDialogModel.OrientationChoices");
        source.Should().Contain("PageSetupDialogModel.PaperSizeChoices");
        source.Should().Contain("PageSetupDialogModel.PageOrderChoices");
        source.Should().Contain("PageSetupDialogModel.PrintErrorValueChoices");
        source.Should().Contain("PageSetupDialogModel.PrintCommentChoices");
        source.Should().Contain("PageSetupDialogModel.ChoiceIndex(");
        source.Should().Contain("PageSetupDialogModel.ChoiceValue(");
        source.Should().Contain("PageSetupDialogModel.GetValidationRoute(build.Target)");
        source.Should().NotContain("initial.PageOrder == WorksheetPageOrder.OverThenDown ? 1 : 0");
        source.Should().NotContain("WorksheetPrintErrorValue ReadErrorValue()");
        source.Should().NotContain("WorksheetPrintComments ReadComments()");
    }

    [Fact]
    public void StatusBarZoomSlider_UsesIdenticalMinMiddleMaxMarks()
    {
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var statusBarSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.StatusBar.cs"));
        var captureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        mainSource.Should().Contain("_statusZoomSliderHost.Children.Add(BuildStatusZoomTick(left: 60));");
        mainSource.Should().Contain("_statusZoomSlider.Minimum = FreeX.App.Services.ZoomLevelMapper.ZoomPercentToSlider(SetWorksheetZoomCommand.MinZoomPercent);");
        mainSource.Should().Contain("_statusZoomSlider.Maximum = FreeX.App.Services.ZoomLevelMapper.ZoomPercentToSlider(SetWorksheetZoomCommand.MaxZoomPercent);");
        mainSource.Should().Contain("FreeX.App.Services.ZoomLevelMapper.SliderToZoomPercent(args.NewValue)");
        statusBarSource.Should().Contain("FreeX.App.Services.ZoomLevelMapper.ZoomPercentToSlider(plan.ZoomPercent)");
        mainSource.Should().Contain("Width = 1,");
        mainSource.Should().Contain("Height = 4,");
        mainSource.Should().NotContain("BuildStatusZoomTick(left: 60, isMiddle: true)");
        mainSource.Should().NotContain("isMiddle ? 2 : 1");
        captureSource.Should().Contain("foreach (var left in new[] { 8d, 60d, 111d })");
        captureSource.Should().Contain("Canvas.SetLeft(canvas.Children[^1], 55.5);");
        captureSource.Should().NotContain("isMiddle ? 2 : 1");
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

    private static void AssertBefore(string source, string first, string second)
    {
        source.IndexOf(first, StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf(second, StringComparison.Ordinal), $"{first} should appear before {second}");
    }
}
