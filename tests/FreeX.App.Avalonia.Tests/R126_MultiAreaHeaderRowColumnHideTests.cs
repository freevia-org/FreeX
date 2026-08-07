using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R126-cellscmds-multiarea-rowheight-2: Avalonia counterpart of the WPF host's R124 Row
/// Height/Column Width/Hide/AutoFit multi-area fix (FreeX.App.Host.Tests.
/// R124_MultiAreaHeaderRowColumnSizingTests). A Ctrl+click multi-area row/column-header selection
/// (built via WorkbookSession.SelectRanges, exactly what AddAdditionalRowSelection/
/// AddAdditionalColumnSelection populate through the real header Ctrl+click flow,
/// MainWindow.RowColumnVisibility.cs) used to have SetSelectedRowsHidden/SetSelectedColumnsHidden
/// read only the single active _session.SelectedRange, so every disjoint area but the active one was
/// silently left untouched by Hide/Unhide Rows/Columns (Ctrl+9/Ctrl+Shift+9/Ctrl+0/Ctrl+Shift+0, the
/// ribbon, and the header context menu) -- unlike real Excel and unlike the WPF host as of R124. The
/// fix routes both handlers through the same SelectionStyleCommandPlanner.ResolveRanges choke point
/// MainWindow.Outline.cs's Group/Ungroup and MainWindow.RibbonMenuWires.cs's Insert/Delete already use.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R126_MultiAreaHeaderRowColumnHideTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task SetSelectedRowsHidden_MultiAreaRowSelection_HidesEveryDisjointRow() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MultiAreaHideRows");
            window.Session.SelectSheet(sheet.Id);

            // Ctrl+click rows 2 and 5 (disjoint): SelectedRange is the active/last-clicked area
            // (row 5), SelectedRanges holds both -- exactly what AddAdditionalRowSelection produces.
            var row2 = WholeRow(sheet.Id, 2);
            var row5 = WholeRow(sheet.Id, 5);
            window.Session.SelectRanges(row5, [row2, row5]);

            InvokePrivate(window, "SetSelectedRowsHidden", true);

            // Before the fix, only row 5 (the active area) was hidden; row 2 was silently left visible.
            sheet.HiddenRows.Should().Contain(2, "row 2's disjoint area must also be hidden");
            sheet.HiddenRows.Should().Contain(5, "row 5 (the active area) must be hidden");
            sheet.HiddenRows.Should().NotContain(1, "row 1 was never part of the selection");
            sheet.HiddenRows.Should().NotContain(3, "row 3 was never part of the selection");

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task SetSelectedColumnsHidden_MultiAreaColumnSelection_HidesEveryDisjointColumn() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MultiAreaHideCols");
            window.Session.SelectSheet(sheet.Id);

            var col2 = WholeColumn(sheet.Id, 2);
            var col5 = WholeColumn(sheet.Id, 5);
            window.Session.SelectRanges(col5, [col2, col5]);

            InvokePrivate(window, "SetSelectedColumnsHidden", true);

            sheet.HiddenCols.Should().Contain(2, "column 2's disjoint area must also be hidden");
            sheet.HiddenCols.Should().Contain(5, "column 5 (the active area) must be hidden");
            sheet.HiddenCols.Should().NotContain(1);
            sheet.HiddenCols.Should().NotContain(3);

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task SetSelectedColumnsHidden_ThenUnhide_MultiAreaColumnSelection_UnhidesEveryDisjointColumn() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MultiAreaUnhideCols");
            window.Session.SelectSheet(sheet.Id);

            // Hide columns 2 and 5 individually first (single-range calls, unaffected by this fix)
            // so Unhide has something real to reveal at both disjoint areas.
            window.Session.SelectRange(WholeColumn(sheet.Id, 2));
            InvokePrivate(window, "SetSelectedColumnsHidden", true);
            window.Session.SelectRange(WholeColumn(sheet.Id, 5));
            InvokePrivate(window, "SetSelectedColumnsHidden", true);
            sheet.HiddenCols.Should().Contain([2u, 5u]);

            var col2 = WholeColumn(sheet.Id, 2);
            var col5 = WholeColumn(sheet.Id, 5);
            window.Session.SelectRanges(col5, [col2, col5]);
            InvokePrivate(window, "SetSelectedColumnsHidden", false);

            sheet.HiddenCols.Should().NotContain(2, "column 2's disjoint area must be unhidden too");
            sheet.HiddenCols.Should().NotContain(5);

            window.Close();
        }, CancellationToken.None);

    // No-regression sibling: a plain single active-range Hide Rows (no Ctrl+click multi-area
    // selection) must keep hiding exactly that one row, unaffected by routing the command
    // construction through the ranges-aware plumbing.
    [Fact]
    public Task SetSelectedRowsHidden_SingleActiveRange_StillHidesOnlyThatRow_NoRegression() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("SingleRangeHideRows");
            window.Session.SelectSheet(sheet.Id);

            window.Session.SelectRange(WholeRow(sheet.Id, 3));

            InvokePrivate(window, "SetSelectedRowsHidden", true);

            sheet.HiddenRows.Should().ContainSingle().Which.Should().Be(3u);

            window.Close();
        }, CancellationToken.None);

    private static GridRange WholeRow(SheetId sheetId, uint row) =>
        new(new CellAddress(sheetId, row, 1), new CellAddress(sheetId, row, CellAddress.MaxCol));

    private static GridRange WholeColumn(SheetId sheetId, uint col) =>
        new(new CellAddress(sheetId, 1, col), new CellAddress(sheetId, CellAddress.MaxRow, col));

    private static void InvokePrivate(MainWindow window, string methodName, bool arg)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, [arg]);
    }
}
