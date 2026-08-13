using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeEmailDeliveryPlannerSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesEmailMergeDialogPolicyToSharedPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("MailMergeEmailDeliveryPlanner.CreateDialogPlan(");
        source.Should().Contain("MailMergeEmailDeliveryPlanner.CreateIntent(");
        source.Should().Contain("MailMergeEmailDeliveryPlanner.GetValidationMessages(");
        source.Should().Contain("workflow.ExecuteEmailDrafts(");
        source.Should().Contain("ExternalUriLauncher.Open(");
        source.Should().Contain("launch.Message");
        source.Should().NotContain("var plan = MailMerge.CreateEmailDeliveryPlan(data, intent)");
        source.Should().NotContain("MailMergeEmailDeliveryPlanner.CreateClientDraftPlan(");
        source.Should().NotContain("MailMergeEmailDeliveryPlanner.FormatStatus(plan)");
        source.Should().NotContain("drafts.Drafts.Count(draft =>");
    }

}
