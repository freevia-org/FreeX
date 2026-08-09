using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class StatusBarViewSwitchSourceTests
{
    [Fact]
    public void StatusBarViewSwitches_RenderAsCompactAccessibleIcons()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "freew", "FreeW.App.Host", "MainWindow.cs");

        var viewSwitchStart = source.IndexOf("private UIElement BuildViewSwitchControl()", StringComparison.Ordinal);
        var navPaneStart = source.IndexOf("// The left navigation pane:", viewSwitchStart, StringComparison.Ordinal);
        var viewSwitchSource = source[viewSwitchStart..navPaneStart];

        viewSwitchSource.Should().Contain("Content = StatusViewIcon(icon)");
        viewSwitchSource.Should().Contain("Free.Shared.Ribbon.Wpf.RibbonIconFactory.CreateIcon(new RibbonCommandIcon(icon), 13, Brushes.White)");
        viewSwitchSource.Should().Contain("AutomationProperties.SetName(button, label)");
        viewSwitchSource.Should().Contain("AutomationProperties.SetName(toggle, label)");
        viewSwitchSource.Should().Contain("Width = 24");
        viewSwitchSource.Should().NotContain("Content = label");
    }

    [Fact]
    public void StatusBarTextPlanning_LivesInPresentationPlanner()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "freew", "FreeW.App.Host", "MainWindow.cs");

        source.Should().Contain("using FreeW.App.Presentation.Shell;");
        source.Should().Contain("FreeWEditorStatusPlanner.Build(");
        source.Should().Contain("new FreeWEditorStatusSnapshot(");
        source.Should().NotContain("WordCount.Words(selectionText)");
        source.Should().NotContain("FormatDocumentSelectionStatus(words, characters)");
    }
}
