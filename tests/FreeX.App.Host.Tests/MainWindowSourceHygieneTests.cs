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

        testSources
            .Where(file => file.Source.Contains("FreeX.App.Host.exe", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .Should()
            .Equal(["FormulaEditingUiE2eTests.cs"]);
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
        source.Should().Contain("rtb.Render(this)");
        source.Should().Contain("CroppedBitmap");
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
        source.Should().Contain("NumberFormatBox.IsDropDownOpen = true");
        source.Should().Contain("FindOpenPopupChild(NumberFormatBox)");
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
        var xaml = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.xaml");

        xaml.Should().Contain("x:Name=\"BordersMenuButton\"");
        source.Should().Contain("FREEX_HOME_BORDERS_DROPDOWN_TOUR");
        source.Should().Contain("CaptureHomeBordersDropdownTourAsync");
        source.Should().Contain("home-borders-dropdown-tour");
        source.Should().Contain("BordersMenuButton.ContextMenu");
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
    public void UpdateViewport_RoutesSparklineValuesThroughSparklineValueCache()
    {
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");
        const string assignment = "SheetGrid.SparklineValues = sheet is null";
        const string cacheRoute = "_sparklineValueCache.GetOrCreate(";
        const string directRoute = "SheetGrid.SparklineValues = SparklineValuePlanner.BuildValues(sheet)";
        const string plannerCall = "SparklineValuePlanner.BuildValues(sheet)";
        const string cacheCallback = "() => SparklineValuePlanner.BuildValues(sheet)";

        viewportSource.Should().Contain(assignment);
        viewportSource.Should().Contain(cacheRoute);
        viewportSource.Should().NotContain(directRoute);
        viewportSource.Should().Contain(cacheCallback);
        CountOccurrences(viewportSource, plannerCall).Should().Be(1);
        viewportSource.IndexOf(cacheRoute, StringComparison.Ordinal)
            .Should()
            .BeLessThan(viewportSource.IndexOf(plannerCall, StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateViewport_UsesCombinedNativeSlicerTimelinePlanning()
    {
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");

        viewportSource.Should().Contain("SlicerTimelinePlanner.GetNativeVisualFilters(_workbook, sheet)");
        viewportSource.Should().Contain("SheetGrid.NativeSlicers = nativeVisualFilters?.Slicers;");
        viewportSource.Should().Contain("SheetGrid.NativeTimelines = nativeVisualFilters?.Timelines;");
        viewportSource.Should().NotContain("SlicerTimelinePlanner.GetNativeVisualSlicers(_workbook, sheet)");
        viewportSource.Should().NotContain("SlicerTimelinePlanner.GetNativeVisualTimelines(_workbook, sheet)");
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

        mainSource.Should().NotContain("private void MainWindow_Loaded(");
        mainSource.Should().NotContain("HomeNumberFormatDropdownPlanner");

        startupSource.Should().Contain("private void MainWindow_Loaded(");
        startupSource.Should().Contain("HomeNumberFormatDropdownPlanner.Options");
        startupSource.Should().Contain("CreateNewWorkbook();");
        startupSource.Should().Contain("NormalizeRibbonSurface(forceCompact: true);");
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

        // New Window resolves a fresh MainWindow from DI; Switch Windows cycles via the registry.
        multiWindowSource.Should().Contain("App.Services.GetRequiredService<MainWindow>()");
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
