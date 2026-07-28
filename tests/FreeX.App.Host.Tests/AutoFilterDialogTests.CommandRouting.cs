using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDialogTests
{
    [Fact]
    public void DataFilterCommands_RouteColorFiltersAndCompositeCriteriaToRealCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("result.Action == AutoFilterDialogAction.ClearFilter");
        source.Should().Contain("\"Clear Filter\"");
        source.Should().Contain("result.ColorFilter is { } colorFilter");
        source.Should().Contain("new CellFillColorFilterCommand");
        source.Should().Contain("new CellNoFillColorFilterCommand");
        source.Should().Contain("new CellFontColorFilterCommand");
        source.Should().Contain("FilterPromptPlanner.TryPlan");
        source.Should().Contain("promptPlan.CreateCommand");
        source.Should().Contain("result.SelectedValues.Count > 0");
        source.Should().Contain("? result.SelectedValues");
        source.Should().Contain(": FilterInputParser.ParseAllowedValues(value)");

        var filterButtonHandler = SourceMethodExtractor.ExtractMethodSource(source, "private void FilterButton_Click(");
        filterButtonHandler.Should().Contain("new ToggleWorksheetAutoFilterCommand");
        filterButtonHandler.Should().NotContain("new AutoFilterDialog");
    }

    [Fact]
    public void DataFilterCommands_ReapplyUsesRememberedFilterCommandWithoutOpeningDialog()
    {
        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");
        var homeEditingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeEditing.cs");

        dataSource.Should().Contain("private GridRange? _lastAutoFilterRange;");
        dataSource.Should().Contain("private readonly Dictionary<uint, Func<GridRange, IWorkbookCommand>> _activeAutoFilterColumnFactories = new();");
        dataSource.Should().Contain("private bool TryExecuteRememberedAutoFilterCommand(");
        dataSource.Should().Contain("TryExecuteRememberedAutoFilterColumnCommand(title, range, filterColOffset: 0, createCommand);");
        dataSource.Should().Contain("_activeAutoFilterColumnFactories[range.Start.Col + filterColOffset] = createCommand;");
        dataSource.Should().Contain("private void ReapplyAutoFilter()");
        dataSource.Should().Contain("TryExecuteRepeatableCurrentRangeCommand(");
        dataSource.Should().Contain("BuildReapplyAllActiveAutoFilterColumnsCommand(_lastAutoFilterRange!.Value)");
        dataSource.Should().Contain("private void ClearRememberedAutoFilterCommand()");
        homeEditingSource.Should().Contain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => ReapplyAutoFilter();");
        homeEditingSource.Should().NotContain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);");
    }
}
