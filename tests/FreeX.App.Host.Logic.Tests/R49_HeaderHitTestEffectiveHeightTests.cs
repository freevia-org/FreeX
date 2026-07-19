using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R49-render-header-frozen-corner-3-2
/// (src/FreeX.App.Host/MainWindow.Selection.cs, SheetGrid_MouseDown).
///
/// Before the fix: SheetGrid_MouseDown's header-area hit test pinned `colHeaderH` to the bare
/// `FreeX.App.UI.GridView.ColHeaderHeight` constant (18px), which never accounts for the
/// column-outline gutter GridView actually renders once any column outline group exists
/// (GridView.CalculateColumnHeaderHeight / SheetGrid.EffectiveColHeaderHeight). So once a column
/// outline group existed, the select-all corner and every column header stopped responding for any
/// click landing in the gutter's height (y in [18, effective)) -- those clicks fell through to the
/// "cell area" branch instead of SelectAll()/SelectColumn(), either doing nothing or mis-selecting
/// a cell.
///
/// After the fix, `colHeaderH` is assigned from `SheetGrid.EffectiveColHeaderHeight` -- the same
/// gutter-inclusive height the render path and the right-click header context-menu hit test
/// (GridHeaderContextMenuHitPlanner, a few hundred lines below in the same file) already used.
///
/// A full pixel-accurate simulation of a real WPF MouseButtonEventArgs isn't a reliable/
/// deterministic unit-test surface (MouseDevice.GetPosition resolves against the actual OS cursor,
/// not an arbitrary supplied point), so this test verifies the fix at its exact, well-defined
/// root cause: the source no longer pins the guard to the bare constant, and instead reads the
/// effective (gutter-inclusive) property.
/// </summary>
public sealed class R49_HeaderHitTestEffectiveHeightTests
{
    [Fact]
    public void SheetGridMouseDown_HeaderHitTest_UsesEffectiveColHeaderHeight_NotBareConstant()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseDownStart = selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal);
        var mouseDownEnd = selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal);
        mouseDownStart.Should().BeGreaterThan(-1, "SheetGrid_MouseDown must exist in MainWindow.Selection.cs");
        mouseDownEnd.Should().BeGreaterThan(mouseDownStart);

        var mouseDown = selectionSource[mouseDownStart..mouseDownEnd];

        mouseDown.Should().NotContain(
            "const double colHeaderH = FreeX.App.UI.GridView.ColHeaderHeight;",
            "the header hit-test guard must not be pinned to the bare 18px header-height constant " +
            "(it must not account only for a groupless sheet)");
        mouseDown.Should().Contain(
            "SheetGrid.EffectiveColHeaderHeight",
            "the header hit-test guard must read the gutter-inclusive effective header height, " +
            "matching what GridView actually renders once columns are grouped");
    }

    // Sibling no-regression: the overall header-vs-cell-area dispatch structure that
    // MouseDownSelectionHandlesSuccessfulGridSelectionPaths (FreeX.App.Host.Tests) already pins down
    // must still be intact after swapping the constant for the effective-height property -- the
    // select-all corner, column-header, and row-header branches are all still reachable and still
    // call SelectAll/SelectColumn/SelectRow.
    [Fact]
    public void SheetGridMouseDown_HeaderDispatchBranches_StillPresent()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        var mouseDownStart = selectionSource.IndexOf("private void SheetGrid_MouseDown", StringComparison.Ordinal);
        var mouseDownEnd = selectionSource.IndexOf("private void MainWindow_TextInput", StringComparison.Ordinal);
        var mouseDown = selectionSource[mouseDownStart..mouseDownEnd];

        mouseDown.Should().Contain("if (pos.X >= 0 && pos.Y >= 0 && (pos.X < rowHeaderW || pos.Y < colHeaderH))");
        mouseDown.Should().Contain("SelectAll();");
        mouseDown.Should().Contain("SelectColumn(cm.Col);");
        mouseDown.Should().Contain("SelectRow(rm.Row);");
        // The Ctrl+click multi-area header additions (R49-render-multiarea-selection-3-2) must also
        // still be present alongside the plain-click paths.
        mouseDown.Should().Contain("AddAdditionalColumnSelection(cm.Col);");
        mouseDown.Should().Contain("AddAdditionalRowSelection(rm.Row);");
    }
}
