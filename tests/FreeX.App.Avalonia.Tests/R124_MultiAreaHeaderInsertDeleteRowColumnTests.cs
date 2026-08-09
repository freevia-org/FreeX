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
/// R124-ribbonwires-multiarea-insertdelete-1: Avalonia twin of the WPF host's R123 multi-area
/// insert/delete fix (FreeX.App.Host.Tests.R123_MultiAreaHeaderInsertTests /
/// R123_MultiAreaHeaderDeleteTests). The Home ▸ Cells ▸ Insert/Delete Sheet Rows/Columns ribbon
/// handlers (MainWindow.RibbonMenuWires.cs) used to read only the single active
/// _session.SelectedRange, so a Ctrl+click multi-area row/column-header selection (built via
/// WorkbookSession.SelectRanges, exactly what AddAdditionalRowSelection/AddAdditionalColumnSelection
/// populate through the real header Ctrl+click flow, MainWindow.RowColumnVisibility.cs) had every
/// disjoint area but the active one silently dropped from the insert/delete -- unlike real Excel,
/// which acts on every disjoint area of a multi-area selection in a single operation. The fix routes
/// all four handlers through ResolveSheetEditAreas, the same SelectionStyleCommandPlanner.ResolveRanges
/// choke point MainWindow.Outline.cs's R124 Group/Ungroup multi-area fix already uses.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R124_MultiAreaHeaderInsertDeleteRowColumnTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task DeleteSheetRows_MultiAreaRowSelection_DeletesEveryDisjointRow() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MultiAreaDeleteRows");
            window.Session.SelectSheet(sheet.Id);

            for (uint row = 1; row <= 6; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));

            // Ctrl+click rows 2 and 5 (disjoint): SelectedRange is the active/last-clicked area
            // (row 5), SelectedRanges holds both.
            var row2 = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
            var row5 = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRanges(row5, [row2, row5]);

            InvokePrivate(window, "DeleteSheetRows");

            // Before the fix, only row 5 (the active area) was deleted; row 2 was silently left in
            // place, so R2..R4 would still occupy rows 2..4 instead of shifting up to 2..3.
            MarkerAt(sheet, 1, 1).Should().Be("R1", "row 1 (above both deleted areas) must be untouched");
            MarkerAt(sheet, 2, 1).Should().Be("R3", "row 2 must be deleted, shifting R3 up into row 2");
            MarkerAt(sheet, 3, 1).Should().Be("R4", "row 3 stays put -- R4 shifts up only once, from row 4 to row 3");
            MarkerAt(sheet, 4, 1).Should().Be("R6", "row 5 must ALSO be deleted (its own disjoint area), shifting R6 up into row 4");
            MarkerAt(sheet, 5, 1).Should().BeNull("nothing remains past the last shifted marker");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task InsertSheetRows_MultiAreaRowSelection_InsertsAtEveryDisjointRow() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MultiAreaInsertRows");
            window.Session.SelectSheet(sheet.Id);

            for (uint row = 1; row <= 6; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));

            var row2 = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
            var row5 = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRanges(row5, [row2, row5]);

            InvokePrivate(window, "InsertSheetRows");

            // Before the fix, only a single blank row was inserted above row 5 (the active area);
            // row 2's area was silently skipped entirely.
            MarkerAt(sheet, 1, 1).Should().Be("R1", "row 1 (above both inserted areas) must be untouched");
            MarkerAt(sheet, 2, 1).Should().BeNull("a blank row must be inserted above original row 2");
            MarkerAt(sheet, 3, 1).Should().Be("R2", "original row 2's marker shifts down into row 3");
            MarkerAt(sheet, 4, 1).Should().Be("R3");
            MarkerAt(sheet, 5, 1).Should().Be("R4");
            MarkerAt(sheet, 6, 1).Should().BeNull("a SECOND blank row must be inserted above original row 5 (the second disjoint area)");
            MarkerAt(sheet, 7, 1).Should().Be("R5", "original row 5's marker shifts down into row 7 once both inserts have run");
            MarkerAt(sheet, 8, 1).Should().Be("R6");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    // No-regression sibling: a plain single active-range Delete Sheet Rows (no multi-area
    // selection involved) must keep deleting exactly that one band, unaffected by routing the
    // command construction through the ranges-aware plumbing.
    [Fact]
    public Task DeleteSheetRows_SingleActiveRange_StillDeletesOnlyThatRow_NoRegression() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("SingleRangeDeleteRows");
            window.Session.SelectSheet(sheet.Id);

            for (uint row = 1; row <= 4; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));

            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 2, CellAddress.MaxCol)));

            InvokePrivate(window, "DeleteSheetRows");

            MarkerAt(sheet, 1, 1).Should().Be("R1");
            MarkerAt(sheet, 2, 1).Should().Be("R3", "row 2 is deleted, shifting R3 up into row 2");
            MarkerAt(sheet, 3, 1).Should().Be("R4");
            MarkerAt(sheet, 4, 1).Should().BeNull();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

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
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, null);
    }
}
