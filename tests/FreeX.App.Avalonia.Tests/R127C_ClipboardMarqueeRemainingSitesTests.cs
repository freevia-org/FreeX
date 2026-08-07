using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R127C (final closure pass on R127-services-clipboard-formats-copy-cancel-1 / R127B): the r127B fix
/// made <c>WorkbookSession.CancelPendingCutAfterMutatingEdit</c> retire the SESSION-level pending
/// Copy/Cut unconditionally (including a plain Copy, not only a Cut) at every one of its call sites --
/// but that session-level fix is separate from this shell's own marching-ants overlay state
/// (<c>_clipboardMarqueeRange</c> in MainWindow.cs), which <c>RefreshShell</c> never touches. r127B's
/// own fixer only added the matching <c>SetClipboardMarquee(null, isCut: false)</c> call at the 6 sites
/// in MainWindow.InsertDeleteCells.cs, leaving these named sites with the overlay still visibly
/// displayed after a successful edit that already invalidated the session-level clipboard:
///   - MainWindow.RibbonMenuWires.cs: InsertSheetRows / InsertSheetColumns / DeleteSheetRows /
///     DeleteSheetColumns (the Ribbon's multi-area Insert/Delete Sheet Rows/Columns handlers)
///   - MainWindow.cs: ApplyEditHistoryResult (the shared post-Undo/Redo handler)
///   - MainWindow.cs: ClearSelectedRangeContents (Delete key / ribbon Clear Contents)
///   - MainWindow.KeyboardParity.cs: ClearSelectionAndEdit (Backspace)
/// This test class closes all of them.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R127C_ClipboardMarqueeRemainingSitesTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task InsertSheetRows_RibbonMultiArea_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("InsertSheetRowsMarquee");
            window.Session.SelectSheet(sheet.Id);

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);
            window.ClipboardMarqueeRangeForTest.Should().NotBeNull("sanity: the marquee must be active before Insert Sheet Rows runs");

            // Ctrl+click rows 2 and 5 (disjoint multi-area): SelectedRange is the active/last-clicked
            // area (row 5), SelectedRanges holds both -- exercises the CompositeWorkbookCommand path.
            var row2 = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
            var row5 = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRanges(row5, [row2, row5]);

            InvokePrivate(window, "InsertSheetRows");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "a Ribbon multi-area Insert Sheet Rows must retire the pending Copy marquee overlay the " +
                "same way WPF's ClearClipboardMarqueeAfterStructuralEdit does");

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task InsertSheetColumns_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("InsertSheetColumnsMarquee");
            window.Session.SelectSheet(sheet.Id);

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: true);
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 4),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 4)));

            InvokePrivate(window, "InsertSheetColumns");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "Insert Sheet Columns must retire the pending Cut marquee overlay");

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task DeleteSheetRows_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("DeleteSheetRowsMarquee");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("R3"));

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 3, 1),
                new CellAddress(sheet.Id, 3, CellAddress.MaxCol)));

            InvokePrivate(window, "DeleteSheetRows");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "Delete Sheet Rows must retire the pending Copy marquee overlay");

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task DeleteSheetColumns_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("DeleteSheetColumnsMarquee");
            window.Session.SelectSheet(sheet.Id);

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 3),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 3)));

            InvokePrivate(window, "DeleteSheetColumns");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "Delete Sheet Columns must retire the pending Copy marquee overlay");

            window.Close();
        }, CancellationToken.None);

    // Multi-area enablement guard (per the round's own SUGGESTED FIX warning): the ACTIVE area (row 5)
    // does NOT independently qualify as anything special here, but the operation must still act on the
    // sibling area (row 2) rather than silently no-oping the whole command -- and must still clear the
    // marquee once the (successful, multi-area) command completes.
    [Fact]
    public Task InsertSheetRows_ActiveAreaAloneWouldNotBlockSiblingArea_StillActsAndClearsMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MultiAreaActiveVsSibling");
            window.Session.SelectSheet(sheet.Id);
            for (uint row = 1; row <= 6; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));

            window.SetClipboardMarqueeForTest(
                new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)), isCut: false);

            var row2 = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
            var row5 = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRanges(row5, [row2, row5]);

            InvokePrivate(window, "InsertSheetRows");

            // Both disjoint areas must have been acted on -- proves no early-gate no-op crept in.
            MarkerAt(sheet, 3, 1).Should().Be("R2", "row 2's insert must still run even though the active area is row 5");
            window.ClipboardMarqueeRangeForTest.Should().BeNull("a successful multi-area insert must still clear the marquee");

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ApplyEditHistoryResult_Undo_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("UndoMarquee");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));
            window.Session.CommitCellText("hello");

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);
            window.ClipboardMarqueeRangeForTest.Should().NotBeNull("sanity");

            InvokePrivate(window, "UndoLastEdit");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "Undo must retire the pending Copy marquee overlay (session-level clipboard is already " +
                "cancelled by CancelPendingCutAfterMutatingEdit; the shell overlay must follow)");

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ApplyEditHistoryResult_Redo_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("RedoMarquee");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));
            window.Session.CommitCellText("hello");
            window.Session.UndoLastEdit();

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: true);

            InvokePrivate(window, "RedoLastEdit");

            window.ClipboardMarqueeRangeForTest.Should().BeNull("Redo must retire the pending Cut marquee overlay");

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ClearSelectedRangeContents_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ClearContentsMarquee");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
            window.Session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);

            InvokePrivate(window, "ClearSelectedRangeContents");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "Delete key / ribbon Clear Contents must retire the pending Copy marquee overlay");

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ClearSelectionAndEdit_Backspace_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("BackspaceMarquee");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
            window.Session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);

            InvokePrivate(window, "ClearSelectionAndEdit");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "Backspace's ordinary-edit path must retire the pending Copy marquee overlay");

            window.Close();
        }, CancellationToken.None);

    // No-regression sibling: Backspace with a drawing object selected must remain a total no-op
    // (R124-model-drawing-backspace-avalonia-1) -- it must neither touch the active cell nor clear an
    // unrelated marquee that has nothing to do with the (untouched) selection.
    [Fact]
    public Task ClearSelectionAndEdit_WithDrawingObjectSelected_LeavesTheMarqueeAlone_NoRegression() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("BackspaceDrawingNoRegression");
            window.Session.SelectSheet(sheet.Id);

            var anchor = new CellAddress(sheet.Id, 5, 5);
            var shape = new DrawingShapeModel { Name = "Shape1", Anchor = anchor };
            sheet.DrawingShapes.Add(shape);
            window.SelectDrawingObjectForTest(SelectionPaneObjectKind.Shape, shape.Id, anchor);

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);

            InvokePrivate(window, "ClearSelectionAndEdit");

            window.ClipboardMarqueeRangeForTest.Should().NotBeNull(
                "Backspace must be a total no-op while a drawing object is selected, so an unrelated " +
                "active marquee must survive untouched");

            window.Close();
        }, CancellationToken.None);

    private static string? MarkerAt(Sheet sheet, uint row, uint col)
    {
        var cell = sheet.GetCell(new CellAddress(sheet.Id, row, col));
        return (cell?.Value as TextValue)?.Value;
    }

    private static void InvokePrivate(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, null);
    }
}
