using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void ViewportAndScrollbarController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var viewportSourcePath = Path.Combine(appHostDirectory, "MainWindow.Viewport.cs");

        File.Exists(viewportSourcePath).Should().BeTrue();
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");

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
    public void ViewportScrollbarMaximums_UsesUsedRangeWithoutMaterializingUsedCells()
    {
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");

        viewportSource.Should().Contain("GetUsedRange()");
        viewportSource.Should().NotContain("sheet.GetUsedCells()");
    }

    [Fact]
    public void UpdateViewport_BuildsViewportOnce_ViaPrecomputedRowHeaderWidth()
    {
        // Verifies the single-build fix: the row-header width must be derived from the cheap
        // ComputeRowMetricsSummary call before CreateViewport, so CreateViewport is only
        // called once per UpdateViewport invocation (no conditional rebuild for width mis-estimates).
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");

        viewportSource.Should().Contain("ComputeCorrectRowHeaderWidth(");
        viewportSource.Should().Contain("ComputeRowMetricsSummary(");
        // The old conditional rebuild pattern must not be present.
        viewportSource.Should().NotContain("if (Math.Abs(actualRowHeaderWidth - rowHeaderWidth) > 0.1)");
    }

    [Fact]
    public void MainWindow_InitializesCurrentSheetBeforeComponentSetupCanRaiseEvents()
    {
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

        var sessionAssignment = mainSource.IndexOf("_session = workbookSession ??", StringComparison.Ordinal);
        var currentSheetAssignment = mainSource.IndexOf("_currentSheetId = _session.ActiveSheet.Id;", StringComparison.Ordinal);
        var initializeComponentCall = mainSource.IndexOf("InitializeComponent();", StringComparison.Ordinal);

        mainSource.Should().Contain("private Workbook _workbook => _session.Workbook;");
        sessionAssignment.Should().BeGreaterThanOrEqualTo(0);
        currentSheetAssignment.Should().BeGreaterThan(sessionAssignment);
        currentSheetAssignment.Should().BeLessThan(initializeComponentCall);
    }

    [Fact]
    public void UpdateViewport_SyncsWorkbookViewStateAndPagePreviewInputs()
    {
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");
        var sheetTabsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");

        viewportSource.Should().Contain("SyncWorkbookActiveSheetIndex();");
        viewportSource.Should().Contain("sheet.ViewTopRow = topRow;");
        viewportSource.Should().Contain("sheet.ViewLeftCol = leftCol;");
        viewportSource.Should().Contain("SheetGrid.PagePreviewRange = CalculatePagePreviewRange(sheet, viewport);");
        viewportSource.Should().Contain("SheetGrid.PageOrder = sheet?.PageOrder ?? WorksheetPageOrder.DownThenOver;");
        viewportSource.Should().Contain("SheetGrid.ScaleToFit = sheet?.ScaleToFit ?? WorksheetScaleToFit.Default;");
        viewportSource.Should().Contain("SheetGrid.PrintTitleRows = sheet?.PrintTitleRows;");
        viewportSource.Should().Contain("SheetGrid.PrintTitleColumns = sheet?.PrintTitleColumns;");
        sheetTabsSource.Should().Contain("private void SyncWorkbookActiveSheetIndex()");
        sheetTabsSource.Should().Contain("private bool TrySelectWorkbookActiveSheet()");
    }

    [Fact]
    public void PagePreviewRangeFallback_ExtendsBeyondVisibleViewportSoPageLayoutEdgesScroll()
    {
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");
        var method = ExtractMethodSource(viewportSource, "private static GridRange? CalculatePagePreviewRange(");

        method.Should().Contain("var visibleRowSpan = lastRow - firstRow + 1;");
        method.Should().Contain("var visibleColumnSpan = lastColumn - firstColumn + 1;");
        method.Should().Contain("var startRow = Math.Min(usedRange?.Start.Row ?? 1u, firstRow);");
        method.Should().Contain("var startColumn = Math.Min(usedRange?.Start.Col ?? 1u, firstColumn);");
        method.Should().Contain("AddWithLimit(lastRow, visibleRowSpan, CellAddress.MaxRow)");
        method.Should().Contain("AddWithLimit(lastColumn, visibleColumnSpan, CellAddress.MaxCol)");
        method.Should().Contain("new CellAddress(sheet.Id, startRow, startColumn)");
        method.Should().Contain("new CellAddress(sheet.Id, endRow, endColumn)");
        method.Should().NotContain("usedRange?.End.Row ?? lastRow");
        method.Should().NotContain("usedRange?.End.Col ?? lastColumn");
        viewportSource.Should().Contain("private static uint AddWithLimit(uint value, uint addend, uint limit)");
    }

    [Fact]
    public void LiveUiE2eAppProcessLaunch_IsCentralizedInSharedHarness()
    {
        var testsDirectory = new DirectoryInfo(WorkspaceFileLocator.FindAppHostTestsDirectory());
        var testSources = testsDirectory
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !string.Equals(file.Name, "MainWindowSourceHygieneTests.cs", StringComparison.Ordinal))
            .Select(file => new
            {
                RelativePath = Path.GetRelativePath(testsDirectory.FullName, file.FullName).Replace('\\', '/'),
                Source = File.ReadAllText(file.FullName)
            })
            .ToList();

        // The live-UI E2E harness is the only place that *launches* FreeX.App.Host.exe as a process.
        // WindowsFileAssociationServiceTests names the exe purely as a registry-command argument (it
        // never spawns the app), so it is a legitimate non-launch reference rather than a parallel
        // process-launch path.
        testSources
            .Where(file => file.Source.Contains("FreeX.App.Host.exe", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Should()
            .Equal(["FileAssociations/WindowsFileAssociationServiceTests.cs", "FormulaEditingUiE2eTests.cs"]);
        testSources
            .Where(file => file.Source.Contains("FreeXUiRun.Start()", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Should()
            .Equal(["FormulaEditingUiE2eTests.cs"]);
        testSources
            .Single(file => file.RelativePath == "FormulaEditingUiE2eTests.cs")
            .Source
            .Should()
            .Contain("SharedAppInstance_CoversLiveUiScenarios")
            .And.Contain("UiAutomationCatalogSnapshotHarness.Run(run)")
            .And.Contain("CellOverflowEditingUiE2eHarness.Run(run)")
            .And.Contain("FormulaEditingUiE2eHarness.Run(run)");
    }

    [Fact]
    public void ScreenshotTour_CapturesFullRibbonBandAndGridSliver()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("ScreenshotTourCaptureHeight = 300");
        // The capture path was de-duplicated into the shared CaptureCurrentWindowAsync helper: it renders the
        // whole window in-process and crops to the top ribbon-band sliver (Int32Rect) rather than the old
        // inline rtb.Render(this)/CroppedBitmap. The "window-top-band" capture method is the 300px sliver.
        source.Should().Contain("new RenderTargetBitmap");
        source.Should().Contain("RenderTargetBitmap-window-top-band");
        source.Should().Contain("File.Create(path)");
        source.Should().NotContain("File.OpenWrite(path)");
        source.Should().NotContain("rtb.Render(RibbonTabs)");
        source.Should().NotContain("RibbonTabs.ActualHeight");
    }

    [Fact]
    public void ScreenshotTour_ProvidesHomeNumberFormatDropdownEvidenceHook()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_HOME_NUMBER_FORMAT_DROPDOWN_TOUR");
        source.Should().Contain("CaptureHomeNumberFormatDropdownTourAsync");
        source.Should().Contain("home-number-format-dropdown-tour");
        source.Should().Contain("numberFormatBox.IsDropDownOpen = true");
        source.Should().Contain("FindOpenPopupChild(numberFormatBox)");
        source.Should().Contain("FindRenderedRibbonControl(\"Number Format\") as ComboBox");
        source.Should().Contain("HomeNumberFormatDropdownTourManifest");
        source.Should().Contain("interactive:home-number-format:opened");
        source.Should().Contain("RenderTargetBitmap-combobox-popup-child");
        source.Should().Contain("interactive_home_number_format_opened.png");
        source.Should().Contain("HomeNumberFormatDropdownPlanner.Options.Select(option => option.Label)");
    }

    [Fact]
    public void ScreenshotTour_ProvidesHomeBordersDropdownEvidenceHook()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_HOME_BORDERS_DROPDOWN_TOUR");
        source.Should().Contain("CaptureHomeBordersDropdownTourAsync");
        source.Should().Contain("home-borders-dropdown-tour");
        source.Should().Contain("FindRenderedRibbonControl(\"Borders\") as Button");
        source.Should().Contain("bordersButton.ContextMenu");
        source.Should().Contain("HomeBordersDropdownTourManifest");
        source.Should().Contain("interactive:home-borders:opened");
        source.Should().Contain("RenderTargetBitmap-context-menu");
        source.Should().Contain("interactive_home_borders_opened.png");
        source.Should().Contain("The scenario captures the top-level Borders menu");
    }

    [Fact]
    public void ScreenshotTour_ProvidesWorksheetContextMenuEvidenceHook()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_WORKSHEET_CONTEXT_MENU_TOUR");
        source.Should().Contain("CaptureWorksheetContextMenuTourAsync");
        source.Should().Contain("worksheet-context-menu-tour");
        source.Should().Contain("OnGridContextMenuRequested(address, GetKeyboardContextMenuGridPoint(address))");
        source.Should().Contain("SheetGrid.ContextMenu");
        source.Should().Contain("WorksheetContextMenuTourManifest");
        source.Should().Contain("interactive:worksheet-cell-context-menu:opened");
        source.Should().Contain("RenderTargetBitmap-worksheet-context-menu");
        source.Should().Contain("interactive_worksheet_cell_context_menu_opened.png");
    }

    [Fact]
    public void ScreenshotTour_ProvidesSheetTabEvidenceHook()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_SHEET_TAB_TOUR");
        source.Should().Contain("sheet-tabs-tour");
        source.Should().Contain("sheet_tabs_tour_manifest.json");
        source.Should().Contain("freex_sheet_tabs_single_sheet");
        source.Should().Contain("freex_sheet_tabs_after_add_sheet");
        source.Should().Contain("freex_sheet_tabs_grouped_colored");
        source.Should().Contain("freex_sheet_tabs_context_menu_opened");
        source.Should().Contain("freex_sheet_tabs_rename_dialog_opened");
        source.Should().Contain("freex_sheet_tabs_hidden_sheet_excluded");
        source.Should().Contain("freex_sheet_tabs_unhide_dialog_opened");
        source.Should().Contain("freex_sheet_tabs_overflow_start");
        source.Should().Contain("freex_sheet_tabs_overflow_middle");
        source.Should().Contain("freex_sheet_tabs_overflow_end");
        source.Should().Contain("RenderTargetBitmap-sheet-tab-strip-context-menu-and-dialogs");
        source.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.SheetTabTourManifest");
        source.Should().Contain("No Microsoft Excel counterpart or macOS/native-host capture is produced by this tool.");
    }

    [Fact]
    public void ScreenshotTour_ProvidesKeyTipOverlayEvidenceHook()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");

        source.Should().Contain("FREEX_KEYTIP_OVERLAY_TOUR");
        source.Should().Contain("CaptureKeyTipOverlayTourAsync");
        source.Should().Contain("keytip-overlay-tour");
        source.Should().Contain("EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel)");
        source.Should().Contain("EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands)");
        source.Should().Contain("HandleActiveRibbonKeyTip(Key.B)");
        source.Should().Contain("HandleActiveRibbonKeyTip(Key.C)");
        source.Should().Contain("requireCollapsedGroupBadges: true");
        source.Should().Contain("KeyTipOverlayTourManifest");
        source.Should().Contain("ribbon-keytip-overlay-pixel-placement");
        source.Should().Contain("RenderTargetBitmap-window-top-band");
        source.Should().Contain("RenderTargetBitmap-context-menu");
        source.Should().Contain("RenderTargetBitmap-menu-popup-child");
        source.Should().Contain("Dropdown and nested submenu states are captured as live WPF popup elements");
    }

    [Fact]
    public void AppChrome_DoesNotUseLegacyGreenThemeConstants()
    {
        var appHostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var legacyFragments = new[]
        {
            "#217346",
            "#185C37",
            "33, 115, 70",
            "24, 92, 55",
            "0x21, 0x73, 0x46"
        };

        var offenders = Directory
            .EnumerateFiles(appHostDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return legacyFragments
                    .Where(fragment => source.Contains(fragment, StringComparison.Ordinal))
                    .Select(fragment => $"{Path.GetRelativePath(appHostDirectory, path)} contains {fragment}");
            })
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        offenders.Should().BeEmpty("legacy green chrome should use the shared FreeX theme resources or #0F6D8C accent instead");
    }

    [Fact]
    public void UpdateViewport_RoutesSparklineValuesThroughSparklineValueCacheAndSharedReader()
    {
        var appHostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");
        var hostPlannerPath = Path.Combine(appHostDirectory, "SparklineValuePlanner.cs");
        const string assignment = "SheetGrid.SparklineValues = sheet is null";
        const string cacheRoute = "_sparklineValueCache.GetOrCreate(";
        const string directRoute = "SheetGrid.SparklineValues = SparklineSeriesReader.BuildValues(_workbook, sheet)";
        const string readerCall = "SparklineSeriesReader.BuildValues(_workbook, sheet)";
        const string oldPlannerCall = "SparklineValuePlanner.BuildValues(sheet)";
        const string cacheCallback = "() => SparklineSeriesReader.BuildValues(_workbook, sheet)";

        File.Exists(hostPlannerPath).Should().BeFalse("Host should call the shared Presentation sparkline reader directly");
        viewportSource.Should().Contain(assignment);
        viewportSource.Should().Contain(cacheRoute);
        viewportSource.Should().NotContain(directRoute);
        viewportSource.Should().Contain(cacheCallback);
        viewportSource.Should().NotContain(oldPlannerCall);
        CountOccurrences(viewportSource, readerCall).Should().Be(1);
        viewportSource.IndexOf(cacheRoute, StringComparison.Ordinal)
            .Should()
            .BeLessThan(viewportSource.IndexOf(readerCall, StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateViewport_UsesCombinedNativeSlicerTimelinePlanning()
    {
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");

        viewportSource.Should().Contain("SlicerTimelinePanePlanner.GetNativeVisualFilters(_workbook, sheet)");
        viewportSource.Should().Contain("new SlicerTimelineSourceSession(_workbook).PopulateAvailableItems(nativeVisualFilters.Slicers)");
        viewportSource.Should().Contain("SheetGrid.NativeSlicers = nativeVisualFilters?.Slicers;");
        viewportSource.Should().Contain("SheetGrid.NativeTimelines = nativeVisualFilters?.Timelines;");
        viewportSource.Should().NotContain("SlicerTimelinePanePlanner.GetNativeVisualSlicers(_workbook, sheet)");
        viewportSource.Should().NotContain("SlicerTimelinePanePlanner.GetNativeVisualTimelines(_workbook, sheet)");
    }

    [Fact]
    public void MainWindow_DoesNotKeepLegacyZoomConversionHelpers()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");

        source.Should().NotContain("SliderToZoomPct(");
        source.Should().NotContain("ZoomPctToSlider(");
    }

    [Fact]
    public void StartupController_LivesOutsideMainWindowCodeBehind()
    {
        var appHostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var mainSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var startupSourcePath = Path.Combine(appHostDirectory, "MainWindow.Startup.cs");

        File.Exists(startupSourcePath).Should().BeTrue();
        var startupSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Startup.cs");
        // After the ribbon XAML→declarative cutover the Home Number Format combo is populated on the
        // *rendered* declarative ribbon by PopulateAndWireRenderedHomeCombos (MainWindow.RibbonDeclarative.cs),
        // which startup reaches via TryApplyDeclarativeRibbon(). Portable choices are injected by the
        // shared composition planner before either renderer sees the definition.
        var declarativeSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonDeclarative.cs");
        var compositionSource = DialogSourceTestSupport.ReadAppServicesRibbonSource("FreeXRibbonCompositionPlanner.cs");

        mainSource.Should().NotContain("private void MainWindow_Loaded(");
        mainSource.Should().NotContain("HomeNumberFormatDropdownPlanner");

        startupSource.Should().Contain("private void MainWindow_Loaded(");
        startupSource.Should().NotContain("HomeNumberFormatDropdownPlanner");
        startupSource.Should().Contain("TryApplyDeclarativeRibbon();");
        startupSource.IndexOf("TryApplyDeclarativeRibbon();", StringComparison.Ordinal).Should().BeLessThan(
            startupSource.IndexOf("ApplyOptionsToView();", StringComparison.Ordinal));
        declarativeSource.Should().Contain("FreeXRibbonCompositionPlanner.Compose(FreeXRibbon.Build(), UiText.Get)");
        compositionSource.Should().Contain("HomeNumberFormatDropdownPlanner.Options");
        startupSource.Should().Contain("CreateNewWorkbook();");
        startupSource.Should().NotContain("UpdateRibbonCompactMode");
    }

    [Fact]
    public void MultiWindow_RegistersFirstWindowBroadcastsEditsAndAdoptsSharedWorkbookForSecondaryWindows()
    {
        var multiWindowSource = DialogSourceTestSupport.ReadHostSources("MainWindow.MultiWindow.cs");
        var startupSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Startup.cs");
        var commandExecutionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.CommandExecution.cs");
        var appSource = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");

        // Registry is a DI singleton and the live window contract is implemented by MainWindow.
        appSource.Should().Contain("services.AddSingleton<WorkbookWindowRegistry>();");

        // First window self-registers on load; secondary windows adopt the shared workbook
        // instead of replacing it via CreateNewWorkbook().
        startupSource.Should().Contain("if (ShouldAdoptSharedWorkbookOnLoad)");
        startupSource.Should().Contain("AdoptSharedWorkbook();");
        startupSource.Should().Contain("RegisterWithWindowRegistry();");

        // New Window constructs the secondary window over the originating window's document
        // context (workbook ref + command bus + document state) so it shares the document; a
        // plain DI resolve would create an independent document (H39: File > Open / File > New
        // in one window must never replace another window's document). A shared view that opens
        // a different document detaches into its own context first.
        multiWindowSource.Should().Contain("ActivatorUtilities.CreateInstance<MainWindow>(");
        multiWindowSource.Should().Contain("public WorkbookId DocumentId => _workbook.Id;");
        multiWindowSource.Should().Contain("private void DetachFromSharedDocumentContext()");
        multiWindowSource.Should().Contain("_windowRegistry.SwitchToNextWindow(this)");
        multiWindowSource.Should().Contain("_windowRegistry.Register(this)");
        multiWindowSource.Should().Contain("_windowRegistry?.Unregister(this)");
        multiWindowSource.Should().Contain("public void RefreshFromSharedWorkbook()");
        multiWindowSource.Should().Contain("public void ApplyWindowTitleSuffix(string suffix)");

        // Cross-window live refresh is broadcast from the central post-command paths.
        commandExecutionSource.Should().Contain("NotifyOtherWindowsOfWorkbookChange();");
        multiWindowSource.Should().Contain("_windowRegistry?.NotifyWorkbookChanged(this);");
    }
}
