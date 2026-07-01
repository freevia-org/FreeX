using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeRecipientFilterSortSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesRecipientFilterSortPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("MailMergeRecipientFilterSortPlanner.GetPreviewColumns(");
        source.Should().Contain("MailMergeRecipientFilterSortPlanner.FormatPreviewHeader(");
        source.Should().Contain("MailMergeRecipientFilterSortPlanner.FormatPreviewRow(");
        source.Should().Contain("MailMergeRecipientFilterSortPlanner.Apply(");
        source.Should().NotContain("const int MaxPreviewCols");
        source.Should().NotContain("chosen.OrderBy(");
        source.Should().NotContain("chosen.OrderByDescending(");
        source.Should().NotContain("new MergeData(data.Header, result.Select");
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
