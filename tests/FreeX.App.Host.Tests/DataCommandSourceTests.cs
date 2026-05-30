using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DataCommandSourceTests
{
    [Theory]
    [InlineData("Sort A to Z", "SA", "SortAscButton_Click")]
    [InlineData("Sort Z to A", "SD", "SortDescButton_Click")]
    [InlineData("Filter", "T", "FilterButton_Click")]
    [InlineData("Clear Filter", "C", "ClearFilterButton_Click")]
    [InlineData("Advanced Filter", "A", "AdvancedFilterBtn_Click")]
    [InlineData("Reapply", "R", "FilterReapplyMenuItem_Click")]
    public void DataSortAndFilterCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title, handler);

        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void DataSortAndFilterHandlers_RouteThroughExpectedCommandsAndPlanners()
    {
        var filterSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));
        var dataSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        filterSource.Should().Contain("new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: true)");
        filterSource.Should().Contain("new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: false)");
        filterSource.Should().Contain("new SortDialog(");
        filterSource.Should().Contain("AutoFilterDropdownPlanner.CreateMenuPlan(");
        filterSource.Should().Contain("FilterPromptPlanner.TryPlan(value, out var promptPlan, out var promptError)");
        filterSource.Should().Contain("new FilterCommand(_currentSheetId, currentRange, filterColOffset, allowedValues: allowedValues)");
        filterSource.Should().Contain("private void ClearFilterButton_Click(object sender, RoutedEventArgs e)");
        filterSource.Should().Contain("ClearRememberedAutoFilterCommand();");
        filterSource.Should().Contain("private void ReapplyAutoFilter()");

        dataSource.Should().Contain("new AdvancedFilterDialog(");
        dataSource.Should().Contain("() => new AdvancedFilterCommand(");
        dataSource.Should().Contain("ApplyAdvancedFilterRangeSelection(dialog, request)");
    }

    [Fact]
    public void DataQueriesAndConnectionsUnsupportedCommand_IsNotSurfacedAsDisabledRibbonButton()
    {
        var xaml = ReadMainWindowXaml();

        xaml.ShouldContainLocalizedAttribute("Text", "Queries &amp; Connections");
        xaml.ShouldContainInvariantCommandName("Refresh All");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Queries &amp; Connections\"");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractButtonElementByTitle(string xaml, string title, string? handler = null)
        => xaml.ExtractElementByInvariantCommandName(
            "Button",
            title,
            handler is null ? null : $"Click=\"{handler}\"");
}
