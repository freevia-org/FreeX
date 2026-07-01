using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeRuleDialogSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesMergeRuleDialogPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("MailMergeRuleDialogPlanner.GetConditionOperators()");
        source.Should().Contain("MailMergeRuleDialogPlanner.GetConditionOperator(");
        source.Should().Contain("MailMergeRuleDialogPlanner.IsComparisonValueEnabled(");
        source.Should().Contain("MailMergeRuleDialogPlanner.CreateIfResult(");
        source.Should().Contain("MailMergeRuleDialogPlanner.CreateConditionResult(");
        source.Should().NotContain("private static readonly (MergeConditionOperator Op, string Label)[] ConditionOperators");
        source.Should().NotContain("private sealed record MergeRuleIfResult");
        source.Should().NotContain("private sealed record MergeRuleCondResult");
        source.Should().NotContain("valueBox.IsEnabled = op != MergeConditionOperator.IsBlank");
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
