using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeFinishPlannerSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesFinishRecipientPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("MailMergeFinishPlanner.Plan(");
        source.Should().Contain("workflow.RouteFinish(");
        source.Should().Contain("workflow.BuildFinish(template, finishPlan, mergeState)");
        source.Should().Contain("route.Route == MailMergeFinishRoute.Printer");
        source.Should().Contain("printDocument!(execution.Document)");
        source.Should().Contain("route.Route == MailMergeFinishRoute.Email");
        source.Should().Contain("emailDocuments!(route.EmailRecordIndexes)");
        source.Should().Contain("emailMergeCommand.Execute(indexes)");
        source.Should().Contain("MailMergeInteractivePromptPlanner.Plan(template)");
        source.Should().Contain("prompt.Prompt, prompt.DefaultAnswer");
        source.Should().NotContain("var augmentedRows = data.Rows.Select(r => session.AugmentRow(r)).ToList();");
        source.Should().NotContain("MailMerge.MergeAllWithRules(");
        source.Should().NotContain("finishPlan.Destination == MailMergeFinishDestination.Printer");
        source.Should().NotContain("finishPlan.Destination == MailMergeFinishDestination.Email");
    }

}
