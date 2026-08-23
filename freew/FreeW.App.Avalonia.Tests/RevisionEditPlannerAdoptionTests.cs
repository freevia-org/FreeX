namespace FreeW.App.Avalonia.Tests;

public sealed class RevisionEditPlannerAdoptionTests
{
    [Fact]
    public void DocumentView_UsesPortableRunInsertionPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Editing",
            "DocumentView.cs"));

        source.Should().Contain("RevisionEditPlanner.InsertRunAtOffset(");
        source.Should().NotContain("private static void InsertRunAtOffset(");
    }
}
