using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class FindReplaceDialogPolicySourceTests
{
    [Fact]
    public void FindReplaceDialog_RoutesStatePolicyThroughPresentationPlanner()
    {
        var repositoryRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "freep",
            "FreeP.App.Host",
            "FindReplaceDialog.cs"));
        var plannerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "freep",
            "FreeP.App.Presentation",
            "FindReplaceDialogPlanner.cs"));

        source.Should().Contain("FindReplaceDialogPlanner.TitleForMode(");
        source.Should().Contain("FindReplaceDialogPlanner.ReplacementTargetIndex(");
        source.Should().Contain("FindReplaceDialogPlanner.CanReplaceAll(");
        source.Should().Contain("FindReplaceDialogPlanner.ReplacementStatus(");
        source.Should().Contain("FindReplaceDialogPlanner.Navigate(");
        source.Should().Contain("FindReplaceDialogPlanner.BuildOptions(");
        source.Should().NotContain("showReplace ? \"Find and Replace\" : \"Find\"");
        source.Should().NotContain("string.IsNullOrEmpty(query)");
        source.Should().NotContain("_currentMatchIndex + direction + _matches.Count");
        source.Should().NotContain("\"No matches found.\"");
        source.Should().NotContain("\"No replacements made.\"");
        source.Should().NotContain("replacement(s) made.");

        plannerSource.Should().Contain("FindReplaceNavigationPolicyPlan Navigate(");
        plannerSource.Should().Contain("FindReplaceReplacementPolicyStatus ReplacementStatus(");
        plannerSource.Should().Contain("FindReplaceDialogPolicy.Navigate(");
        plannerSource.Should().Contain("FindReplaceDialogPolicy.BuildReplacementStatus(");
        plannerSource.Should().NotContain("public enum FindReplaceStatusKind");
        plannerSource.Should().NotContain("public sealed record FindReplaceNavigationPlan");
        plannerSource.Should().NotContain("public sealed record FindReplaceReplacementStatus");
        plannerSource.Should().NotContain("ToLocalStatusKind");
        plannerSource.Should().NotContain("FindReplaceStatusKind.");
    }

}
