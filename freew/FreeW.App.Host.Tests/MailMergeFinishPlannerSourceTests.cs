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
        source.Should().Contain("workflow.BuildFinish(template, finishPlan, mergeState)");
        source.Should().Contain("finishPlan.Destination == MailMergeFinishDestination.Printer");
        source.Should().Contain("printDocument!(execution.Document)");
        source.Should().Contain("finishPlan.Destination == MailMergeFinishDestination.Email");
        source.Should().Contain("emailDocuments(finishPlan.RowIndexes)");
        source.Should().Contain("emailMergeCommand.Execute(indexes)");
        source.Should().Contain("MailMergeInteractivePromptPlanner.Plan(template)");
        source.Should().Contain("prompt.Prompt, prompt.DefaultAnswer");
        source.Should().NotContain("var augmentedRows = data.Rows.Select(r => session.AugmentRow(r)).ToList();");
        source.Should().NotContain("MailMerge.MergeAllWithRules(");
    }

}
