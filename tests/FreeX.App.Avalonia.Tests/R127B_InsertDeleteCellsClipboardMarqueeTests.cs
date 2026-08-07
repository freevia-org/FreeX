using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R127B (round 127 ScopeAudit follow-up to R127-services-clipboard-formats-copy-cancel-1):
/// <see cref="MainWindow.ShowInsertCellsDialogAsync"/>/<see cref="MainWindow.ShowDeleteCellsDialogAsync"/>
/// (src/FreeX.App.Avalonia/MainWindow.InsertDeleteCells.cs) route their whole-row/whole-column paths
/// through <c>WorkbookSession.ExecuteReviewCommand</c>, which now retires the SESSION-level pending
/// Copy/Cut on a successful structural edit (see <c>WorkbookSession.IsStructuralCellShiftCommand</c>).
/// That session-level fix alone does not touch this shell's own marching-ants overlay state
/// (<c>_clipboardMarqueeRange</c>/<c>_clipboardMarqueeIsCut</c> in MainWindow.cs, covered by
/// <see cref="R75_ClipboardMarqueeOverlayTests"/>) -- a separate, UI-only concern that
/// <c>RefreshShell</c> does not touch. This adds the matching <c>SetClipboardMarquee(null, isCut: false)</c>
/// call at each Insert/Delete Cells whole-row/whole-column/band call site, mirroring this shell's own
/// <c>MainWindow.ContextMenuGridActions.InsertContextRow/InsertContextColumn</c> and the WPF host's
/// <c>ClearClipboardMarqueeAfterStructuralEdit</c> (which clears its session-level clipboard AND its
/// visual <c>SheetGrid.ClipboardRange</c>/<c>ClipboardIsCut</c> together).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R127B_InsertDeleteCellsClipboardMarqueeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ShowInsertCellsDialogAsync_WholeRowSelection_ClearsAnActivePendingClipboardMarquee()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("InsertRowsMarqueeFixture");
            window.Session.SelectSheet(sheet.Id);

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);
            window.ClipboardMarqueeRangeForTest.Should().NotBeNull("sanity: the marquee must be active before Insert Rows runs");

            var wholeRow5 = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRange(wholeRow5);

            await InvokePrivateAsync(window, "ShowInsertCellsDialogAsync");

            window.StatusTextForTest.Text.Should().Be("Inserted rows");
            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "a whole-row Insert Cells (routed to InsertRowsCommand) must retire the pending Copy/Cut " +
                "marquee overlay the same way the WPF host's ClearClipboardMarqueeAfterStructuralEdit does, " +
                "so the marching ants do not keep pointing at a range a later Paste no longer honors");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShowDeleteCellsDialogAsync_WholeColumnSelection_ClearsAnActivePendingClipboardMarquee()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("DeleteColumnsMarqueeFixture");
            window.Session.SelectSheet(sheet.Id);

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: true);
            window.ClipboardMarqueeRangeForTest.Should().NotBeNull("sanity: the marquee must be active before Delete Columns runs");

            var wholeColumnE = new GridRange(
                new CellAddress(sheet.Id, 1, 5),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 5));
            window.Session.SelectRange(wholeColumnE);

            await InvokePrivateAsync(window, "ShowDeleteCellsDialogAsync");

            window.StatusTextForTest.Text.Should().Be("Deleted columns");
            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "a whole-column Delete Cells (routed to DeleteColumnsCommand) must retire the pending Cut " +
                "marquee overlay -- a still-shown Cut marquee would misleadingly suggest a later Paste " +
                "would still move the (now stale) source range");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // No-regression sibling: Insert Rows failing (e.g. a protected sheet) must NOT clear an active
    // marquee -- only a successful structural edit retires the pending Copy/Cut, matching
    // WorkbookSession.ExecuteReviewCommand's own success-gated IsStructuralCellShiftCommand check.
    [Fact]
    public async Task ShowInsertCellsDialogAsync_WholeRowSelection_OnAProtectedSheet_LeavesTheMarqueeAloneOnFailure()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ProtectedInsertRowsMarquee");
            window.Session.SelectSheet(sheet.Id);
            // Protected with no permissions granted, so InsertRowsCommand's own
            // CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.InsertRows)
            // rejects the edit outright.
            sheet.IsProtected = true;

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);

            var wholeRow5 = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRange(wholeRow5);

            await InvokePrivateAsync(window, "ShowInsertCellsDialogAsync");

            window.StatusTextForTest.Text.Should().NotBe("Inserted rows", "sanity: protection must block the insert");
            window.ClipboardMarqueeRangeForTest.Should().NotBeNull(
                "a FAILED Insert Rows (blocked by sheet protection) must not clear the pending Copy/Cut " +
                "marquee -- only a successful structural edit retires it");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static async Task InvokePrivateAsync(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        var task = (Task)method.Invoke(window, null)!;
        await task;
    }
}
