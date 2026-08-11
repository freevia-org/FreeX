using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class TableOfAuthoritiesDialogParitySourceTests
{
    [Theory]
    [InlineData("FreeW.App.Host")]
    [InlineData("FreeW.App.Avalonia")]
    public void Both_renderers_consume_the_shared_visual_metrics(string project)
    {
        var source = File.ReadAllText(Path.Combine(
            Workspace(),
            "freew",
            project,
            "TableOfAuthoritiesDialog.cs"));

        source.Should().Contain("TableOfAuthoritiesDialogPlanner.VisualMetrics");
    }

    private static string Workspace() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
}
