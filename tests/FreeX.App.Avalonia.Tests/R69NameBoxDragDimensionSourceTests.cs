using System.IO;
using System.Linq;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R69-render-active-cell-selection-6-2: while a mouse-drag selection is in progress, Excel's
/// Name Box shows a live "{rows}R x {cols}C" dimension readout instead of the range address,
/// reverting to the address once the drag ends. Before this fix SelectRangeFromAnchor (the sole
/// caller of which is the pointer-drag continuation, ContinueCellSelectionDrag) always left the
/// Name Box showing only the plain active-cell reference, with no live dimension readout at all.
/// </summary>
public sealed class R69NameBoxDragDimensionSourceTests
{
    [Fact]
    public void SelectRangeFromAnchorShowsLiveDimensionTextInNameBoxWhileDragging()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        var method = source[
            source.IndexOf("private void SelectRangeFromAnchor(", System.StringComparison.Ordinal)..
            source.IndexOf("private Control AddColumnResizeHandle(", System.StringComparison.Ordinal)];

        method.Should().Contain("_session.SelectAnchoredRange(anchor, address);");
        method.Should().Contain("if (!_cellAddressBoxHasPendingEdit)");
        method.Should().Contain("_cellAddressText.Text = FormatDragSelectionDimensionText(_session.SelectedRange);");
        method.Should().Contain("_cellSelectionDragShowedDimensionText = true;");
    }

    [Fact]
    public void FormatDragSelectionDimensionTextFormatsRowsByColumns()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        var helper = source[
            source.IndexOf("private static string FormatDragSelectionDimensionText(", System.StringComparison.Ordinal)..
            source.IndexOf("private static string FormatFillCellsAction(", System.StringComparison.Ordinal)];

        helper.Should().Contain("range.End.Row - range.Start.Row + 1");
        helper.Should().Contain("range.End.Col - range.Start.Col + 1");
        helper.Should().Contain("$\"{rowCount}R x {colCount}C\"");
    }

    // No-regression: once the drag ends (a normal release, or an interrupted pointer-capture
    // loss), the Name Box must revert to the plain range address -- matching a keyboard-driven
    // selection (SelectRange), which never shows dimension text at all since it never touches
    // _cellAddressText/_cellSelectionDragShowedDimensionText.
    [Fact]
    public void DragEndHandlersRevertNameBoxToPlainRangeAddress()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private void RevertNameBoxAfterCellSelectionDragEnd()");
        var revertHelper = source[
            source.IndexOf("private void RevertNameBoxAfterCellSelectionDragEnd(", System.StringComparison.Ordinal)..
            source.IndexOf("private void DetachCellSelectionDragHandlers(", System.StringComparison.Ordinal)];
        revertHelper.Should().Contain("if (!_cellSelectionDragShowedDimensionText)");
        revertHelper.Should().Contain("_cellSelectionDragShowedDimensionText = false;");
        revertHelper.Should().Contain("_cellAddressText.Text = FormatRangeReference(_session.SelectedRange);");

        var captureLost = source[
            source.IndexOf("private void CellSelectionCapturePointerCaptureLost(", System.StringComparison.Ordinal)..
            source.IndexOf("private void RevertNameBoxAfterCellSelectionDragEnd(", System.StringComparison.Ordinal)];
        captureLost.Should().Contain("RevertNameBoxAfterCellSelectionDragEnd();");

        var endDrag = source[
            source.IndexOf("private async Task EndCellSelectionDragAsync(", System.StringComparison.Ordinal)..
            source.IndexOf("private bool TryResolveCellPointerAddress(", System.StringComparison.Ordinal)];
        endDrag.Should().Contain("RevertNameBoxAfterCellSelectionDragEnd();");
    }

    [Fact]
    public void BeginCellSelectionDragResetsStaleDimensionTextFlag()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        var beginDrag = source[
            source.IndexOf("private void BeginCellSelectionDrag(", System.StringComparison.Ordinal)..
            source.IndexOf("private void CellSelectionCapturePointerMoved(", System.StringComparison.Ordinal)];

        beginDrag.Should().Contain("_cellSelectionDragShowedDimensionText = false;");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
