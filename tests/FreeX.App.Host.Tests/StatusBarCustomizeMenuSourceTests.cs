using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarCustomizeMenuSourceTests
{
    [Fact]
    public void DefaultStatusBarCustomization_MatchesExcelVisibleStatistics()
    {
        var options = new AppOptions();

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
        var optionsSource = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "AppOptions.cs");
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var contextMenuSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ContextMenus.cs"));

        // The status-bar customize menu is now built at runtime from StatusBarCustomizeContextMenuPlanner and
        // attached via StatusBarRoot's Loaded handler, replacing the hand-authored XAML ContextMenu. The
        // Opened/Click handlers and the persisted-option wiring are unchanged.
        xaml.Should().Contain("Loaded=\"StatusBarRoot_Loaded\"");
        contextMenuSource.Should().Contain("menu.Opened += StatusBarCustomizeMenu_Opened;");
        contextMenuSource.Should().Contain("menuItem.Click += StatusBarCustomizeMenuItem_Click;");
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
        gridStatusSource.Should().Contain("StatusBarPresentationPlanner.Build(");
        gridStatusSource.Should().Contain("StatusBarPresentationPlanner.BuildRendererPlan(plan);");
        gridStatusSource.Should().Contain("foreach (var entry in rendererPlan.VisibilityElements)");
        gridStatusSource.Should().Contain("GetStatusBarReadoutTextBlock(readout.Kind)");
        gridStatusSource.Should().Contain("private void ApplyStatusBarInteractiveDisplayState(StatusBarRendererPlan rendererPlan)");
        gridStatusSource.Should().Contain("rendererPlan.IsElementVisible(StatusBarPresentationElement.ViewShortcuts)");
        gridStatusSource.Should().Contain("StatusBarOptionVisibilityStore.ToVisibility(_options)");
        gridStatusSource.Should().Contain("StatusBarOptionUpdateWorkflow.ApplyToRuntimeSession(");
        gridStatusSource.Should().NotContain("case StatusBarOptionTags.Average");
        gridStatusSource.Should().NotContain("AppOptionsStore.Save(_options)");
        gridStatusSource.Should().NotContain("ApplyStatusBarInteractiveDisplayState(BuildStatusBarPresentationPlan(state).Visibility);");
    }
}
