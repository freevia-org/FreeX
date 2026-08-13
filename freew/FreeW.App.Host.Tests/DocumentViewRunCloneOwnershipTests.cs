using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class DocumentViewRunCloneOwnershipTests
{
    [Fact]
    public void WpfTabFragmentation_UsesCanonicalModelRunCloneHelper()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "Editing",
            "DocumentView.cs"));

        source.Should().Contain("var segmentRun = RevisionEditPlanner.CloneRunWithText(run, segment);");
        source.Should().Contain("var remainderRun = RevisionEditPlanner.CloneRunWithText(run, remainder);");
        source.Should().NotContain("private static ModelRun CloneTextRun(ModelRun source, string text)");
        source.Should().Contain("private static WpfRun CloneTextRun(WpfRun source, string text)");
    }
}
