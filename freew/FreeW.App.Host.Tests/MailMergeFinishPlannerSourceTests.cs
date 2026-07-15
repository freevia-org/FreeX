using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeFinishPlannerSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesFinishRecipientPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("MailMergeFinishPlanner.PlanNewDocumentAllRecords(");
        source.Should().Contain("finishPlan.RowIndexes");
        source.Should().NotContain("var augmentedRows = data.Rows.Select(r => session.AugmentRow(r)).ToList();");
    }

}
