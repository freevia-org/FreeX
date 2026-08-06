using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WpfWorksheetStructureOwnershipSourceTests
{
    [Fact]
    public void LayoutOutlineAndPaneAdapters_DoNotConstructPortableCommands()
    {
        var cells = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.CellsCommands.cs");
        cells.Should().NotContain("CreateRowsHiddenCommand(");
        cells.Should().NotContain("CreateColumnsHiddenCommand(");
        cells.Should().NotContain("CreateRowHeightCommand(");
        cells.Should().NotContain("CreateColumnWidthCommand(");
        cells.Should().Contain("_session.SetSelectedRowsHeight(");
        cells.Should().Contain("_session.SetSelectedRowsHidden(");

        var headerSizing = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.GridStatus.cs");
        headerSizing.Should().NotContain("new SetRowHeightCommand");
        headerSizing.Should().NotContain("new SetColumnWidthCommand");
        headerSizing.Should().Contain("_session.SetRowsHeightPixels(");
        headerSizing.Should().Contain("_session.AutoFitColumns(");

        var outline = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.OutlineCommands.cs");
        outline.Should().NotContain("new GroupRowsCommand");
        outline.Should().NotContain("new GroupColumnsCommand");
        outline.Should().NotContain("new ClearWorksheetOutlineCommand");
        outline.Should().NotContain("new CollapseRowGroupCommand");
        outline.Should().NotContain("new ExpandRowGroupCommand");
        outline.Should().NotContain("new SetRowOutlineGroupCollapsedCommand");
        outline.Should().NotContain("new SetColumnOutlineGroupCollapsedCommand");
        outline.Should().Contain("_session.GroupSelectedOutline");
        outline.Should().Contain("_session.SetOutlineGroupCollapsed(");

        var panes = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs");
        panes.Should().NotContain("new SetFreezePanesCommand");
        panes.Should().NotContain("new SetSplitPanesCommand");
        panes.Should().Contain("_session.FreezePanesAtActiveCell");
        panes.Should().Contain("_session.ToggleSplitPanesAtActiveCell(");
    }
}
