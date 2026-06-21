using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaMainWindowChromeSourceTests
{
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
        source.Should().Contain("var scrollBarLeft = _horizontalWorksheetScrollBar.Bounds.Left > 0");
        source.Should().Contain("AddSheetTabTopRuleSegment(ruleLeft, leftJoin, topY);");
        source.Should().Contain("AddSheetTabTopRuleSegment(rightJoin, ruleRight, topY);");
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
        source.Should().NotContain("FormatBackstageFileSize");
        source.Should().NotContain("FormatBackstageLastModified");
        source.Should().NotContain("FormatBackstageProtection");
        source.Should().NotContain("FormatBackstageStatistics");
    }

    [Fact]
    public void StatusBarZoomSlider_UsesIdenticalMinMiddleMaxMarks()
    {
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var captureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        mainSource.Should().Contain("_statusZoomSliderHost.Children.Add(BuildStatusZoomTick(left: 60));");
        mainSource.Should().Contain("Width = 1,");
        mainSource.Should().Contain("Height = 4,");
        mainSource.Should().NotContain("BuildStatusZoomTick(left: 60, isMiddle: true)");
        mainSource.Should().NotContain("isMiddle ? 2 : 1");
        captureSource.Should().Contain("foreach (var left in new[] { 8d, 60d, 111d })");
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
}
