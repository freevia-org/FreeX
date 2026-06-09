using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarCustomizeMenuSourceTests
{
    [Fact]
    public void DefaultStatusBarCustomization_MatchesExcelVisibleStatistics()
    {
        var options = new FreeXOptions();

        options.StatusBarShowAverage.Should().BeTrue();
        options.StatusBarShowCount.Should().BeTrue();
        options.StatusBarShowNumericalCount.Should().BeFalse();
        options.StatusBarShowMinimum.Should().BeFalse();
        options.StatusBarShowMaximum.Should().BeFalse();
        options.StatusBarShowSum.Should().BeTrue();
    }

    [Fact]
    public void StatusBarCustomizeMenu_WiresHandlersToPersistedOptions()
    {
        var gridStatusSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.GridStatus.cs"));
        var optionsSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FreeXOptions.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

        xaml.Should().Contain("Opened=\"StatusBarCustomizeMenu_Opened\"");
        xaml.Should().Contain("Click=\"StatusBarCustomizeMenuItem_Click\"");
        optionsSource.Should().Contain("public bool StatusBarShowCellMode { get; set; } = true;");
        optionsSource.Should().Contain("public bool StatusBarShowNumericalCount { get; set; }");
        optionsSource.Should().Contain("public bool StatusBarShowMinimum { get; set; }");
        optionsSource.Should().Contain("public bool StatusBarShowMaximum { get; set; }");
        optionsSource.Should().NotContain("public bool StatusBarShowNumericalCount { get; set; } = true;");
        optionsSource.Should().NotContain("public bool StatusBarShowMinimum { get; set; } = true;");
        optionsSource.Should().NotContain("public bool StatusBarShowMaximum { get; set; } = true;");
        optionsSource.Should().Contain("public bool StatusBarShowZoomSlider { get; set; } = true;");
        gridStatusSource.Should().Contain("private void StatusBarCustomizeMenu_Opened(object sender, RoutedEventArgs e)");
        gridStatusSource.Should().Contain("private void StatusBarCustomizeMenuItem_Click(object sender, RoutedEventArgs e)");
        gridStatusSource.Should().Contain("_options.StatusBarShowAverage = isChecked;");
        gridStatusSource.Should().Contain("_options.Save()");
        gridStatusSource.Should().Contain("ApplyStatusBarInteractiveDisplayState();");
    }
}
