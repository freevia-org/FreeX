using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class FindReplaceDialogPolicySourceTests
{
    [Fact]
    public void FindReplaceDialog_RoutesStatePolicyThroughPresentationPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Host",
            "FindReplaceDialog.cs"));

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
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
