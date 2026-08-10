using System.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePTableCloneHelperSourceTests
{
    [Fact]
    public void TableCommandsAndSlideCloner_UseSharedCoreModelCloneHelper()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var commandSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.Core.Model",
            "PresentationCommands.Table.cs"));
        var clonerSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.Core.Model",
            "SlideCloner.cs"));

        commandSource.Should().NotContain("file static class TableCommandHelper");
        commandSource.Should().NotContain("file static class TableGridHelper");
        commandSource.Should().Contain("PresentationModelCloneHelper.FindTable");
        commandSource.Should().Contain("PresentationModelCloneHelper.CloneTable");
        commandSource.Should().Contain("PresentationModelCloneHelper.RestoreTableState");
        commandSource.Should().Contain("TextBodyModelCloner.CloneTextBody");

        clonerSource.Should().Contain("TextBodyModelCloner.CloneTextBody");
        clonerSource.Should().Contain("PresentationModelCloneHelper.CloneTable");
        clonerSource.Should().NotContain("private static TextBody CloneTextBody");
        clonerSource.Should().NotContain("private static TableShape CloneTable");
        clonerSource.Should().NotContain("private static TableCell CloneTableCell");
    }

}
