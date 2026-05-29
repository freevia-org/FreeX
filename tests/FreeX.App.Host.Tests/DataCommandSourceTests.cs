using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DataCommandSourceTests
{
    [Theory]
    [InlineData("Sort A to Z", "SA", "SortAscButton_Click")]
    [InlineData("Sort Z to A", "SD", "SortDescButton_Click")]
    [InlineData("Filter", "T", "FilterButton_Click")]
    [InlineData("Clear", "C", "ClearFilterButton_Click")]
    [InlineData("Advanced", "A", "AdvancedFilterBtn_Click")]
    [InlineData("Reapply", "R", "FilterReapplyMenuItem_Click")]
    public void DataSortAndFilterCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
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
    public void DataQueriesAndConnectionsCommand_RemainsDisabledWithExcelTitle()
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), "Queries &amp; Connections");

        button.Should().Contain("Content=\"Queries &amp; Connections\"");
        button.Should().Contain("IsEnabled=\"False\"");
        button.Should().Contain("local:RibbonTooltip.Title=\"Queries &amp; Connections\"");
        button.Should().Contain("local:RibbonTooltip.KeyTip=\"Q\"");
        button.Should().Contain("External workbook queries and connection management are deferred in FreeX.");
        button.Should().NotContain("Click=");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractButtonElementByTitle(string xaml, string title)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonTooltip.Title=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} Data command should be present");

        var start = xaml.LastIndexOf("<Button", titleIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} Data command should be a Button");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf("</Button>", titleIndex, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (selfClosingEnd < 0 || closingEnd < selfClosingEnd)
            ? closingEnd + "</Button>".Length
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} Data button should have a closing marker");
        return xaml[start..end];
    }
}
