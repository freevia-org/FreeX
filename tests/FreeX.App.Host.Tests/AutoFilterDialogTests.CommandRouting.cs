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
        source.Should().Contain("_session.ExecuteWorksheetFilterMutationPlan(plan)");
        source.Should().Contain("WorksheetFilterMessagePlanner.GetPlanErrorResourceKey(plan)");
        source.Should().NotContain("private static string FormatFilterPromptPlanError(");
        source.Should().NotContain("new CellFillColorFilterCommand");
        source.Should().NotContain("new CellNoFillColorFilterCommand");
        source.Should().NotContain("new CellFontColorFilterCommand");
        source.Should().NotContain("FilterPromptPlanner.TryPlan");

        var dialogResultHandler = SourceMethodExtractor.ExtractMethodSource(source, "private bool ApplyAutoFilterDialogResult(");
        dialogResultHandler.Should().NotContain("new CompositeWorkbookCommand");

        var filterButtonHandler = SourceMethodExtractor.ExtractMethodSource(source, "private void FilterButton_Click(");
        filterButtonHandler.Should().Contain("_session.ToggleSelectedRangeAutoFilter");
        filterButtonHandler.Should().NotContain("new ToggleWorksheetAutoFilterCommand");
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
        dataSource.Should().Contain("_session.ExecuteWorksheetFilterReapplyPlan(plan, \"Reapply Filter\")");
        dataSource.Should().NotContain("plan.CreateCommand(\"Reapply Filter\")");
        dataSource.Should().NotContain("_activeAutoFilterColumnFactories");
        dataSource.Should().NotContain("TryExecuteRememberedAutoFilterCommand");
        homeEditingSource.Should().Contain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => ReapplyAutoFilter();");
        homeEditingSource.Should().NotContain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);");
    }
}
