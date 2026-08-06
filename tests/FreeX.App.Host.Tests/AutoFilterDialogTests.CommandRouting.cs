using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDialogTests
{
    [Fact]
    public void DataFilterCommands_RouteColorFiltersAndCompositeCriteriaToRealCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("_filterWorkflowSession.PlanDialogResult(");
        source.Should().Contain("TryExecuteAutoFilterMutation(plan)");
        source.Should().Contain("FormatFilterPromptPlanError(plan.PromptError)");
        source.Should().NotContain("new CellFillColorFilterCommand");
        source.Should().NotContain("new CellNoFillColorFilterCommand");
        source.Should().NotContain("new CellFontColorFilterCommand");
        source.Should().NotContain("FilterPromptPlanner.TryPlan");

        var filterButtonHandler = SourceMethodExtractor.ExtractMethodSource(source, "private void FilterButton_Click(");
        filterButtonHandler.Should().Contain("new ToggleWorksheetAutoFilterCommand");
        filterButtonHandler.Should().NotContain("new AutoFilterDialog");
    }

    [Fact]
    public void DataFilterCommands_ReapplyUsesSharedWorkflowWithoutOpeningDialog()
    {
        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");
        var homeEditingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeEditing.cs");

        dataSource.Should().Contain("private readonly WorksheetFilterWorkflowSession _filterWorkflowSession = new();");
        dataSource.Should().Contain("private void ReapplyAutoFilter()");
        dataSource.Should().Contain("_filterWorkflowSession.CreateReapplyPlan(sheet)");
        dataSource.Should().Contain("plan.CreateCommand(\"Reapply Filter\")");
        dataSource.Should().NotContain("_activeAutoFilterColumnFactories");
        dataSource.Should().NotContain("TryExecuteRememberedAutoFilterCommand");
        homeEditingSource.Should().Contain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => ReapplyAutoFilter();");
        homeEditingSource.Should().NotContain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);");
    }
}
