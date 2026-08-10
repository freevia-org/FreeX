using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageFramePlannerTests
{
    [Fact]
    public void Build_ComposesNavigationWithPaneFlowsAndCommandWorkflows()
    {
        var plan = FreeXBackstageFramePlanner.Build();

        plan.Entries.Select(entry => entry.Kind)
            .Should().Equal(FreeXBackstageNavigationPlanner.Build().Select(entry => entry.Kind));

        var home = plan.Entries.Single(entry => entry.PaneFlow?.Pane == FreeXBackstagePaneId.Home);
        home.StableId.Should().Be("freex.backstage.pane.home");
        home.Navigation.AutomationId.Should().Be(FreeXBackstageNavigationPlanner.HomePaneAutomationId);
        home.PaneFlow!.RefreshGreeting.Should().BeTrue();
        home.PaneFlow.ResetRecentTab.Should().BeTrue();
        home.CommandWorkflow.Should().BeNull();

        var print = plan.Entries.Single(entry => entry.PaneFlow?.Pane == FreeXBackstagePaneId.Print);
        print.PaneFlow!.ResetPrintPreviewSettings.Should().BeTrue();
        print.PaneFlow.RefreshPrintOptions.Should().BeTrue();
        print.PaneFlow.FocusTarget.Should().Be(FreeXBackstagePaneFocusTarget.PrintNowButton);

        var saveAs = plan.Entries.Single(entry =>
            entry.CommandWorkflow?.Command == FreeXBackstageCommandId.SaveAs);
        saveAs.StableId.Should().Be("freex.backstage.command.saveas");
        saveAs.Navigation.AutomationId.Should().Be("BackstageSaveAsButton");
        saveAs.CommandWorkflow!.Workflow.Should().Be(FreeXBackstageCommandWorkflowKind.SaveWorkbookAs);
        saveAs.CommandWorkflow.ForcesSaveAsDialog.Should().BeTrue();
        saveAs.PaneFlow.Should().BeNull();
    }

    [Fact]
    public void Build_ExposesLanguageInvariantPaneSelectionTargets()
    {
        var selection = FreeXBackstageFramePlanner.Build().Selection;

        selection.DefaultPane.Should().Be(FreeXBackstagePaneId.Home);
        selection.DefaultPaneAutomationId.Should().Be(FreeXBackstageNavigationPlanner.HomePaneAutomationId);
        selection.For(FreeXBackstagePaneId.Home).Should().Be(FreeXBackstageNavigationPlanner.HomePaneAutomationId);
        selection.For(FreeXBackstagePaneId.Info).Should().Be(FreeXBackstageNavigationPlanner.InfoPaneAutomationId);
        selection.For(FreeXBackstagePaneId.Print).Should().Be(FreeXBackstageNavigationPlanner.PrintPaneAutomationId);
    }

    [Fact]
    public void Build_LeavesDividersWithoutPaneOrCommandPolicy()
    {
        var dividers = FreeXBackstageFramePlanner.Build().Entries
            .Where(entry => entry.Kind == FreeXBackstageNavigationEntryKind.Divider);

        dividers.Should().OnlyContain(entry =>
            entry.StableId == null &&
            entry.PaneFlow == null &&
            entry.CommandWorkflow == null);
    }
}
