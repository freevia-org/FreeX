using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDialogTests
{
    [Fact]
    public void DataFilterCommands_RouteColorFiltersAndCompositeCriteriaToRealCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));

        source.Should().Contain("result.Action == AutoFilterDialogAction.ClearFilter");
        source.Should().Contain("\"Clear Filter\"");
        source.Should().Contain("result.ColorFilter is { } colorFilter");
        source.Should().Contain("new CellFillColorFilterCommand");
        source.Should().Contain("new CellNoFillColorFilterCommand");
        source.Should().Contain("new CellFontColorFilterCommand");
        source.Should().Contain("FilterPromptPlanner.TryPlan");
        source.Should().Contain("promptPlan.CreateCommand");
    }

    [Fact]
    public void DataFilterCommands_ReapplyUsesRememberedFilterCommandWithoutOpeningDialog()
    {
        var dataSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));
        var homeEditingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeEditing.cs"));

        dataSource.Should().Contain("private GridRange? _lastAutoFilterRange;");
        dataSource.Should().Contain("private Func<GridRange, IWorkbookCommand>? _lastAutoFilterCommandFactory;");
        dataSource.Should().Contain("private bool TryExecuteRememberedAutoFilterCommand(");
        dataSource.Should().Contain("_lastAutoFilterCommandFactory = createCommand;");
        dataSource.Should().Contain("private void ReapplyAutoFilter()");
        dataSource.Should().Contain("TryExecuteRepeatableCurrentRangeCommand(");
        dataSource.Should().Contain("_lastAutoFilterCommandFactory");
        dataSource.Should().Contain("private void ClearRememberedAutoFilterCommand()");
        homeEditingSource.Should().Contain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => ReapplyAutoFilter();");
        homeEditingSource.Should().NotContain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);");
    }
}
