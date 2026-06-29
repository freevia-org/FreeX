using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageFlowPlannerTests
{
    [Fact]
    public void BuildPaneFlow_PinsRefreshResponsibilities()
    {
        FreeXBackstageFlowPlanner.BuildPaneFlow(FreeXBackstagePaneId.Home)
            .Should().Be(new FreeXBackstagePaneFlowPlan(
                FreeXBackstagePaneId.Home,
                RefreshGreeting: true,
                ResetRecentTab: true,
                RefreshRecentFiles: true,
                RefreshInfo: false,
                ResetPrintPreviewSettings: false,
                RefreshPrintOptions: false,
                RefreshPrintPreview: false,
                FocusTarget: FreeXBackstagePaneFocusTarget.None));

        FreeXBackstageFlowPlanner.BuildPaneFlow(FreeXBackstagePaneId.Info)
            .RefreshInfo.Should().BeTrue();

        var print = FreeXBackstageFlowPlanner.BuildPaneFlow(FreeXBackstagePaneId.Print);
        print.ResetPrintPreviewSettings.Should().BeTrue();
        print.RefreshPrintOptions.Should().BeTrue();
        print.RefreshPrintPreview.Should().BeTrue();
        print.FocusTarget.Should().Be(FreeXBackstagePaneFocusTarget.PrintNowButton);
    }

    [Fact]
    public void BuildCommandWorkflow_ClassifiesFileCommands()
    {
        FreeXBackstageFlowPlanner.BuildCommandWorkflow(FreeXBackstageCommandId.New)
            .Should().Be(new FreeXBackstageCommandWorkflowPlan(
                FreeXBackstageCommandId.New,
                FreeXBackstageCommandWorkflowKind.NewWorkbook,
                UsesDirtyGate: true,
                UsesSaveResolution: false,
                ForcesSaveAsDialog: false,
                OpensNativeFileDialog: false));

        var open = FreeXBackstageFlowPlanner.BuildCommandWorkflow(FreeXBackstageCommandId.Open);
        open.Workflow.Should().Be(FreeXBackstageCommandWorkflowKind.OpenWorkbook);
        open.UsesDirtyGate.Should().BeTrue();
        open.OpensNativeFileDialog.Should().BeTrue();

        var save = FreeXBackstageFlowPlanner.BuildCommandWorkflow(FreeXBackstageCommandId.Save);
        save.Workflow.Should().Be(FreeXBackstageCommandWorkflowKind.SaveWorkbook);
        save.UsesSaveResolution.Should().BeTrue();

        var saveAs = FreeXBackstageFlowPlanner.BuildCommandWorkflow(FreeXBackstageCommandId.SaveAs);
        saveAs.Workflow.Should().Be(FreeXBackstageCommandWorkflowKind.SaveWorkbookAs);
        saveAs.ForcesSaveAsDialog.Should().BeTrue();
        saveAs.OpensNativeFileDialog.Should().BeTrue();
    }

    [Fact]
    public void BuildCommandWorkflow_ClassifiesNonFileCommands()
    {
        FreeXBackstageFlowPlanner.BuildCommandWorkflow(FreeXBackstageCommandId.Account)
            .Should().Be(new FreeXBackstageCommandWorkflowPlan(
                FreeXBackstageCommandId.Account,
                FreeXBackstageCommandWorkflowKind.Account,
                UsesDirtyGate: false,
                UsesSaveResolution: false,
                ForcesSaveAsDialog: false,
                OpensNativeFileDialog: false));

        FreeXBackstageFlowPlanner.BuildCommandWorkflow(FreeXBackstageCommandId.Options)
            .Workflow.Should().Be(FreeXBackstageCommandWorkflowKind.Options);
    }

    [Fact]
    public async Task CommandWorkflowExecutor_RoutesEveryCommandThroughSharedHandlers()
    {
        var invoked = new List<string>();
        var handlers = new FreeXBackstageCommandHandlers(
            NewWorkbookAsync: () => RecordAsync("new"),
            OpenWorkbookAsync: () => RecordAsync("open"),
            ShareWorkbookAsync: () => RecordAsync("share"),
            SaveWorkbookAsync: () => RecordAsync("save"),
            SaveWorkbookAsAsync: () => RecordAsync("save-as"),
            ExportWorkbookAsync: () => RecordAsync("export"),
            CloseWorkbookAsync: () => RecordAsync("close"),
            AccountAsync: () => RecordAsync("account"),
            OptionsAsync: () => RecordAsync("options"));

        foreach (var command in Enum.GetValues<FreeXBackstageCommandId>())
            await FreeXBackstageCommandWorkflowExecutor.ExecuteAsync(command, handlers);

        invoked.Should().Equal(
            "new",
            "open",
            "share",
            "save",
            "save-as",
            "export",
            "close",
            "account",
            "options");

        Task RecordAsync(string value)
        {
            invoked.Add(value);
            return Task.CompletedTask;
        }
    }
}
