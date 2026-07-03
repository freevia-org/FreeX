using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeFinishPlannerSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesFinishRecipientPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("MailMergeFinishPlanner.PlanNewDocumentAllRecords(");
        source.Should().Contain("finishPlan.RowIndexes");
        source.Should().NotContain("var augmentedRows = data.Rows.Select(r => session.AugmentRow(r)).ToList();");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
