using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeEmailDeliveryPlannerSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesEmailMergeDialogPolicyToSharedPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(
            Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var planner = File.ReadAllText(
            Path.Combine(root, "freew", "FreeW.App.Presentation", "Ribbon", "MailMergeEmailDeliveryPlanner.cs"));

        // The dialog plan / intent / validation policy moved from the ribbon into the shared
        // MailMergeEmailDeliveryDialogSession, which is a stronger form of the same guarantee: the
        // host now drives the session and the workflow and owns none of the policy itself.
        source.Should().Contain("new MailMergeEmailDeliveryDialogSession(data, currentRecordIndex, selectedRecordIndexes)");
        source.Should().Contain("workflow.ExecuteEmailDrafts(");
        source.Should().Contain("ExternalUriLauncher.Open(");
        source.Should().Contain("launch.Message");
        source.Should().NotContain("MailMergeEmailDeliveryPlanner.");
        source.Should().NotContain("var plan = MailMerge.CreateEmailDeliveryPlan(data, intent)");
        source.Should().NotContain("drafts.Drafts.Count(draft =>");

        planner.Should().Contain("MailMergeEmailDeliveryPlanner.CreateDialogPlan(");
        planner.Should().Contain("MailMergeEmailDeliveryPlanner.CreateIntent(");
        planner.Should().Contain("MailMergeEmailDeliveryPlanner.GetValidationMessages(");
    }

}
