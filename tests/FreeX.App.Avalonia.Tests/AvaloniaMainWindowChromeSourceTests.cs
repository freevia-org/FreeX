using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaMainWindowChromeSourceTests
{
    [Fact]
    public void WorksheetChrome_UsesCompactGridMetricsAndExcelSheetTabOrder()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private const double HeaderColumnWidth = 46;");
        source.Should().Contain("private const double HeaderRowHeight = 22;");
        source.Should().Contain("private const double MinimumDisplayedColumnWidth = 48;");
        source.Should().Contain("private const double MinimumDisplayedRowHeight = 20;");

        source.Should().Contain("tabsAndAdd.Children.Add(_sheetTabsHost);");
        source.Should().Contain("tabsAndAdd.Children.Add(_newSheetButton);");
        source.Should().Contain("DockPanel.SetDock(_horizontalWorksheetScrollBar, Dock.Right);");
        source.Should().Contain("CreateSheetTabNavigationButton(\"<\", \"Scroll Tabs Left\", -1)");
        source.Should().Contain("CreateSheetTabNavigationButton(\">\", \"Scroll Tabs Right\", 1)");
        source.Should().NotContain("AddGridChild(chrome, _horizontalWorksheetScrollBar, 1, 0);");
    }

    [Fact]
    public void FormulaBarToggle_HidesTheWholeFormulaBarRow()
    {
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var toggleSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ViewToggles.cs"));

        mainSource.Should().Contain("private readonly Border _formulaBarHost = new();");
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
        captureSource.Should().Contain("private const int ParityCaptureTitleBarHeight = 34;");
        captureSource.Should().Contain("RenderWindowWithCapturedTitleBarToPng(this, ParityCaptureWindowWidth, ParityCaptureWindowHeight");
        captureSource.Should().Contain("CreateParityCapturedTitleBar(window.Title ?? \"FreeX\")");
        hostCaptureSource.Should().Contain("EnsureFormulaBarVisibleForParityCapture(window);");
        hostCaptureSource.Should().Contain("window.FindName(\"FormulaBarBorder\")");
        captureSource.Should().Contain("Avalonia File surface is still dialog-based");
        captureSource.Should().NotContain("ShowBackstageInfoDialogAsync()");
        captureSource.Should().NotContain("ShowBackstageExportDialogAsync()");
        captureSource.Should().NotContain("ShowBackstageAccountDialogAsync()");
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
