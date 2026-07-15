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
        source.Should().Contain("MailMergeEmailDeliveryPlanner.FormatStatus(");
    }

}
