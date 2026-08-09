using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;
using FreeX.App.Presentation;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R84-app-mouse-selection-5-1: Ctrl+click (cell or column/row header) never added a disjoint
/// multi-area selection on the Avalonia shell -- it fell into the same collapsing branch as a
/// plain click, discarding every prior selected area, unlike Excel and the WPF host's
/// AddOrMoveAdditionalSelection/AddAdditionalColumnSelection/AddAdditionalRowSelection
/// (MainWindow.Selection.cs). The fix makes <see cref="MainWindow.SelectClickedCell"/> and the
/// column/row header pointer-pressed handlers append a new disjoint area via
/// <c>WorkbookSession.SelectRanges</c> when Ctrl (without Shift) is held.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R84_MouseSelectionMultiAreaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CtrlClickCell_AddsDisjointSecondArea_KeepsBothAreasSelected()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("CtrlClickCellFixture");
                window.Session.SelectSheet(sheet.Id);

                var first = new CellAddress(sheet.Id, 1, 1);
                var second = new CellAddress(sheet.Id, 3, 3);
                window.Session.SelectCell(first);

                window.SelectClickedCell(second, KeyModifiers.Control);

                window.Session.SelectedRanges.Should().BeEquivalentTo(
                    [new GridRange(first, first), new GridRange(second, second)],
                    "Ctrl+click must ADD the clicked cell as a disjoint second area, not replace the first one -- matching Excel's 'A1,C3' multi-area selection");
                window.Session.ActiveCell.Should().Be(second,
                    "the newly Ctrl+clicked cell becomes the active cell within the multi-area selection");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PlainClickAfterCtrlClick_NoRegression_StillCollapsesToSingleCell()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("PlainClickAfterCtrlClickFixture");
                window.Session.SelectSheet(sheet.Id);

                var first = new CellAddress(sheet.Id, 1, 1);
                var second = new CellAddress(sheet.Id, 3, 3);
                var third = new CellAddress(sheet.Id, 5, 5);
                window.Session.SelectCell(first);
                window.SelectClickedCell(second, KeyModifiers.Control);
                window.Session.SelectedRanges.Should().HaveCount(2, "the Ctrl+click above must have built a two-area selection first");

                // A plain click (no modifiers) after a multi-area Ctrl+click selection must still
                // collapse everything down to just the newly clicked cell -- the Ctrl+click fix
                // must not leak into the ordinary click path.
                window.SelectClickedCell(third, KeyModifiers.None);

                window.Session.SelectedRanges.Should().BeEquivalentTo(
                    [new GridRange(third, third)],
                    "a plain click must still collapse a multi-area selection down to just the clicked cell");
                window.Session.SelectedRange.Should().Be(new GridRange(third, third));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlClickColumnHeader_AddsDisjointColumnBand()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("CtrlClickColumnHeaderFixture");
                window.Session.SelectSheet(sheet.Id);

                InvokeSelectEntireColumn(window, 2, extend: false);
                var firstBand = window.Session.SelectedRange;

                InvokeAddAdditionalColumnSelection(window, 5);

                var expectedSecondBand = new GridRange(
                    new CellAddress(sheet.Id, 1, 5),
                    new CellAddress(sheet.Id, CellAddress.MaxRow, 5));
                window.Session.SelectedRanges.Should().BeEquivalentTo(
                    [firstBand, expectedSecondBand],
                    "Ctrl+clicking a second column header must ADD it as a disjoint area, matching the WPF host's AddAdditionalColumnSelection, instead of replacing column B's selection");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShiftClickColumnHeader_NoRegression_StillExtendsInsteadOfAdding()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("ShiftClickColumnHeaderFixture");
                window.Session.SelectSheet(sheet.Id);

                InvokeSelectEntireColumn(window, 2, extend: false);
                InvokeSelectEntireColumn(window, 4, extend: true);

                // Shift-click extending a column-header selection must still produce ONE contiguous
                // band (B:D), not a disjoint multi-area selection -- the new Ctrl-only add path must
                // not affect the existing Shift-extend path.
                window.Session.SelectedRanges.Should().HaveCount(1);
                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 1, 2),
                    new CellAddress(sheet.Id, CellAddress.MaxRow, 4)));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HeaderDragAfterShiftClick_UsesPointerDownHeaderAsAnchor()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("HeaderDragAnchorFixture");
                window.Session.SelectSheet(sheet.Id);

                // Shift-click D after selecting B creates B:D, but the drag that follows starts
                // at D. WPF retains D as the pointer-down anchor rather than reusing the range's
                // active-cell anchor (B).
                InvokeSelectEntireColumn(window, 2, extend: false);
                InvokeSelectEntireColumn(window, 4, extend: true);
                InvokeSelectEntireColumnFromHeaderDrag(window, 6, 4);

                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 1, 4),
                    new CellAddress(sheet.Id, CellAddress.MaxRow, 6)));

                InvokeSelectEntireRow(window, 2, extend: false);
                InvokeSelectEntireRow(window, 4, extend: true);
                InvokeSelectEntireRowFromHeaderDrag(window, 6, 4);

                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 4, 1),
                    new CellAddress(sheet.Id, 6, CellAddress.MaxCol)));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void InvokeSelectEntireColumn(MainWindow window, uint col, bool extend) =>
        typeof(MainWindow)
            .GetMethod("SelectEntireColumn", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [col, extend]);

    private static void InvokeAddAdditionalColumnSelection(MainWindow window, uint col) =>
        typeof(MainWindow)
            .GetMethod("AddAdditionalColumnSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [col]);

    private static void InvokeSelectEntireColumnFromHeaderDrag(MainWindow window, uint targetCol, uint anchorCol) =>
        typeof(MainWindow)
            .GetMethod("SelectEntireColumnFromHeaderDrag", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [targetCol, anchorCol]);

    private static void InvokeSelectEntireRow(MainWindow window, uint row, bool extend) =>
        typeof(MainWindow)
            .GetMethod("SelectEntireRow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [row, extend]);

    private static void InvokeSelectEntireRowFromHeaderDrag(MainWindow window, uint targetRow, uint anchorRow) =>
        typeof(MainWindow)
            .GetMethod("SelectEntireRowFromHeaderDrag", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [targetRow, anchorRow]);
}
