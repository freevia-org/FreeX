using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class PivotCreateDialogSourceTests
{
    [Fact]
    public void InsertPivotTableDialog_UsesPresentationPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotCreate.cs"));

        source.Should().Contain("using FreeX.App.Presentation.PivotUI;");
        source.Should().Contain("PivotApplication.PrepareCreate(");
        source.Should().Contain("PivotApplication.PlanCreate(");
        source.Should().Contain("new PivotCreateSubmission(");
        source.Should().NotContain("PivotCreatePlanner.BuildCommand(");
        source.Should().NotContain("using FreeX.App.Avalonia.Pivot;");
        File.Exists(RepoFileAllowMissing("src", "FreeX.App.Avalonia", "Pivot", "PivotCreatePlanner.cs")).Should().BeFalse();
    }

    private static string RepoFile(params string[] parts)
    {
        var path = RepoFileAllowMissing(parts);
        if (File.Exists(path))
            return path;

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }

    private static string RepoFileAllowMissing(params string[] parts)
    {
        return Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());
    }

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
