using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaWorksheetStructureOwnershipSourceTests
{
    [Fact]
    public void InsertDeleteAdapters_DoNotConstructPortableCommands()
    {
        var sources = new[]
        {
            ReadAppSource("MainWindow.InsertDeleteCells.cs"),
            ReadAppSource("MainWindow.ContextMenuGridActions.cs"),
            ReadAppSource("MainWindow.RibbonMenuWires.cs"),
        };

        foreach (var source in sources)
        {
            source.Should().NotContain("new InsertRowsCommand");
            source.Should().NotContain("new InsertColumnsCommand");
            source.Should().NotContain("new InsertCellsCommand");
            source.Should().NotContain("new DeleteRowsCommand");
            source.Should().NotContain("new DeleteColumnsCommand");
            source.Should().NotContain("new DeleteCellsCommand");
        }

        sources.Should().Contain(source => source.Contains("ApplyWorksheetStructureResult("));
    }

    [Fact]
    public void LayoutOutlineAndPaneAdapters_DelegatePortableOwnershipToWorkbookSession()
    {
        var sizing = ReadAppSource("MainWindow.RowColumnVisibility.cs") +
            ReadAppSource("MainWindow.cs");
        sizing.Should().NotContain("new SetRowsHiddenCommand");
        sizing.Should().NotContain("new SetColumnsHiddenCommand");
        sizing.Should().NotContain("new SetRowHeightCommand");
        sizing.Should().NotContain("new SetColumnWidthCommand");
        sizing.Should().Contain("_session.SetSelectedRowsHidden(");
        sizing.Should().Contain("_session.SetRowsHeightPixels(");

        var outline = ReadAppSource("MainWindow.Outline.cs") +
            ReadAppSource("MainWindow.OutlineGrid.cs") +
            ReadAppSource("MainWindow.RibbonMenuWires.cs");
        outline.Should().NotContain("new GroupRowsCommand");
        outline.Should().NotContain("new GroupColumnsCommand");
        outline.Should().NotContain("new ClearWorksheetOutlineCommand");
        outline.Should().NotContain("new CollapseRowGroupCommand");
        outline.Should().NotContain("new ExpandRowGroupCommand");
        outline.Should().NotContain("new SetRowOutlineGroupCollapsedCommand");
        outline.Should().NotContain("new SetColumnOutlineGroupCollapsedCommand");
        outline.Should().Contain("_session.GroupSelectedOutline(");
        outline.Should().Contain("_session.SetOutlineGroupCollapsed(");

        var panes = ReadAppSource("MainWindow.ParityWires.cs") +
            ReadAppSource("MainWindow.SplitPanePointer.cs");
        panes.Should().NotContain("new SetSplitPanesCommand");
        panes.Should().Contain("_session.ToggleSplitPanesAtActiveCell(");
        panes.Should().Contain("_session.SetSplitPanes(");
    }

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", fileName));

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", parts);
}
