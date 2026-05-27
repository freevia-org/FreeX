using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowWorksheetContextMenuSourceTests
{
    [Fact]
    public void InsertDeleteContextMenuActionsRouteToExistingWorksheetMutationCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "Freexcel.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("case WorksheetContextMenuAction.InsertCells:");
        source.Should().Contain("InsertCellsMenuItem_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.InsertRowAbove:");
        source.Should().Contain("InsertRows(address.Row);");
        source.Should().Contain("case WorksheetContextMenuAction.InsertRowBelow:");
        source.Should().Contain("InsertRows(address.Row + 1);");
        source.Should().Contain("case WorksheetContextMenuAction.InsertColumnLeft:");
        source.Should().Contain("InsertColumns(address.Col);");
        source.Should().Contain("case WorksheetContextMenuAction.InsertColumnRight:");
        source.Should().Contain("InsertColumns(address.Col + 1);");
        source.Should().Contain("case WorksheetContextMenuAction.DeleteCells:");
        source.Should().Contain("DeleteCellsMenuItem_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.DeleteRows:");
        source.Should().Contain("DeleteSelectedRows();");
        source.Should().Contain("case WorksheetContextMenuAction.DeleteColumns:");
        source.Should().Contain("DeleteSelectedColumns();");
    }
}
