namespace Free.Shared.AppServices.Tests;

public sealed class SymbolPickerCatalogOwnershipSourceTests
{
    [Fact]
    public void SharedTier_OwnsTheSymbolPickerCatalogOnce()
    {
        File.Exists(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "shared", "Free.Shared.AppServices", "SymbolPickerCatalogPlanner.cs")).Should().BeTrue();

        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        File.Exists(Path.Combine(root, "src", "FreeX.App.Presentation", "Dialogs", "SymbolPickerCatalogPlanner.cs"))
            .Should().BeFalse("the catalog moved to the shared tier; FreeX consumes it from there");
    }

    [Theory]
    [InlineData("src", "FreeX.App.Host", "SymbolPickerDialog.cs", "SymbolPickerCatalogPlanner.")]
    [InlineData("src", "FreeX.App.Avalonia", "MainWindow.Symbol.cs", "SymbolPickerCatalogPlanner.")]
    [InlineData("freew", "FreeW.App.Presentation", "Dialogs/FreeWSymbolPickerDialogPlanner.cs", "SymbolPickerCatalogPlanner.GetSymbolsForSubset(")]
    public void Consumers_UseTheSharedCatalog(string tree, string project, string file, string expected)
    {
        var parts = new[] { tree, project }.Concat(file.Split('/')).ToArray();
        var source = File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));

        source.Should().Contain(expected);
    }
}
