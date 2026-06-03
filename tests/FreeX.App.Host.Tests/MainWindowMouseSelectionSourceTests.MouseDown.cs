using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowMouseSelectionSourceTests
{
    [Fact]
    public void MouseDownSelectionIgnoresNonLeftButtonsBeforeHitTesting()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];

        mouseDown.Should().Contain("if (e.ChangedButton != MouseButton.Left)");
        mouseDown.Should().Contain("return;");
        mouseDown.IndexOf("if (e.ChangedButton != MouseButton.Left)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDown.IndexOf("var pos = e.GetPosition(SheetGrid);", StringComparison.Ordinal));
    }

    [Fact]
    public void MouseDownSelectionHandlesSuccessfulGridSelectionPaths()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        var mouseDown = selectionSource[
            selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal)..
            selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal)];

        var topLeftSelection = mouseDown[
            mouseDown.IndexOf("if (pos.X < rowHeaderW && pos.Y < colHeaderH)", StringComparison.Ordinal)..
            mouseDown.IndexOf("// Column header: select entire column", StringComparison.Ordinal)];
        var columnHeaderSelection = mouseDown[
            mouseDown.IndexOf("if (pos.Y < colHeaderH)", StringComparison.Ordinal)..
            mouseDown.IndexOf("// Row header: select entire row", StringComparison.Ordinal)];
        var rowHeaderSelection = mouseDown[
            mouseDown.IndexOf("// Row header: select entire row", StringComparison.Ordinal)..
            mouseDown.IndexOf("Cell area", StringComparison.Ordinal)];
        var cellSelection = mouseDown[
            mouseDown.IndexOf("if (hitAddress is { } newAddr)", StringComparison.Ordinal)..];

        mouseDown.Should().Contain("if (pos.X >= 0 && pos.Y >= 0 && (pos.X < rowHeaderW || pos.Y < colHeaderH))");
        mouseDown.IndexOf("if (pos.X >= 0 && pos.Y >= 0 && (pos.X < rowHeaderW || pos.Y < colHeaderH))", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDown.IndexOf("if (pos.X < rowHeaderW && pos.Y < colHeaderH)", StringComparison.Ordinal));

        topLeftSelection.Should().Contain("SelectAll();");
        topLeftSelection.Should().Contain("e.Handled = true;");
        topLeftSelection.IndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(topLeftSelection.IndexOf("SelectAll();", StringComparison.Ordinal));

        columnHeaderSelection.Should().Contain("SelectColumn(cm.Col);");
        columnHeaderSelection.Should().Contain("e.Handled = true;");
        columnHeaderSelection.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(columnHeaderSelection.LastIndexOf("return;", StringComparison.Ordinal));

        rowHeaderSelection.Should().Contain("SelectRow(rm.Row);");
        rowHeaderSelection.Should().Contain("e.Handled = true;");
        rowHeaderSelection.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rowHeaderSelection.LastIndexOf("return;", StringComparison.Ordinal));

        cellSelection.Should().Contain("SetActiveCell(newAddr);");
        cellSelection.Should().Contain("SheetGrid.CaptureMouse();");
        cellSelection.LastIndexOf("e.Handled = true;", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(cellSelection.LastIndexOf("SheetGrid.CaptureMouse();", StringComparison.Ordinal));
    }
}
