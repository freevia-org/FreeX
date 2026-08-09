using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-16 fix group regression guards:
///
///   R16-cross-sheet-3d-recalc-2 — the Avalonia shell never recalculated after delete/duplicate/
///     move sheet, so cross-sheet/3-D formula results went stale (WPF host's SheetCtxDelete_Click /
///     SheetCtxDuplicate_Click call RecalculateWorkbook() after the structural edit; Avalonia's
///     DeleteActiveSheet/DuplicateActiveSheet/MoveActiveSheetLeft/MoveActiveSheetRight did not).
///     Each test below forces a dependent formula to go stale in Manual calculation mode (so the
///     shared edit pipeline's automatic recalc-on-edit cannot be the one updating it), then invokes
///     the real production handler and asserts the formula's value is now current — proving the
///     handler itself forces a recalculation.
///
///   R16-merge-align-deep-2 — the Avalonia Merge & Center button errored on an already-merged
///     selection ("Range overlaps an existing merged region.") instead of toggling to unmerge it,
///     unlike Excel/the WPF host. The fix in <c>MergeAndCenterSelectedRangeAsync</c> checks
///     <c>WorkbookSession.IsSelectedRangeMerged</c> first and routes to <c>UnmergeSelectedRange</c>.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R16_avalonia_mw_Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── R16-cross-sheet-3d-recalc-2: delete/duplicate/move sheet must force a recalc ─────────────

    [Fact]
    public async Task DeleteActiveSheet_ForcesRecalc_UpdatesStaleCrossSheetFormula()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var refSheet = window.Session.Workbook.AddSheet("RefSheetDel");
            var formSheet = window.Session.Workbook.AddSheet("FormSheetDel");
            var deleteMeSheet = window.Session.Workbook.AddSheet("DeleteMeSheet");

            SeedStaleCrossSheetFormula(window, refSheet, formSheet);

            window.Session.SelectSheet(deleteMeSheet.Id);
            InvokePrivate(window, "DeleteActiveSheet");

            formSheet.GetValue(new CellAddress(formSheet.Id, 1, 1)).Should().Be(new NumberValue(200),
                "Delete Sheet must force a workbook recalculation (mirrors WPF host's " +
                "SheetCtxDelete_Click -> RecalculateWorkbook()) so cross-sheet formulas don't go stale");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DuplicateActiveSheet_ForcesRecalc_UpdatesStaleCrossSheetFormula()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var refSheet = window.Session.Workbook.AddSheet("RefSheetDup");
            var formSheet = window.Session.Workbook.AddSheet("FormSheetDup");
            var dupMeSheet = window.Session.Workbook.AddSheet("DupMeSheet");

            SeedStaleCrossSheetFormula(window, refSheet, formSheet);

            window.Session.SelectSheet(dupMeSheet.Id);
            InvokePrivate(window, "DuplicateActiveSheet");

            formSheet.GetValue(new CellAddress(formSheet.Id, 1, 1)).Should().Be(new NumberValue(200),
                "Duplicate Sheet must force a workbook recalculation (mirrors WPF host's " +
                "SheetCtxDuplicate_Click -> RecalculateWorkbook()) so cross-sheet formulas don't go stale");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MoveActiveSheetLeft_ForcesRecalc_UpdatesStaleCrossSheetFormula()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var refSheet = window.Session.Workbook.AddSheet("RefSheetMvL");
            var formSheet = window.Session.Workbook.AddSheet("FormSheetMvL");
            var moveMeSheet = window.Session.Workbook.AddSheet("MoveMeSheetL");

            SeedStaleCrossSheetFormula(window, refSheet, formSheet);

            // moveMeSheet is the third (rightmost) sheet, so Move Left is a valid single-step move.
            window.Session.SelectSheet(moveMeSheet.Id);
            InvokePrivate(window, "MoveActiveSheetLeft");

            formSheet.GetValue(new CellAddress(formSheet.Id, 1, 1)).Should().Be(new NumberValue(200),
                "Move Sheet Left must force a workbook recalculation, since reordering sheets can " +
                "change which sheets a 3-D reference spans, so cross-sheet formulas must not go stale");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MoveActiveSheetRight_ForcesRecalc_UpdatesStaleCrossSheetFormula()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var refSheet = window.Session.Workbook.AddSheet("RefSheetMvR");
            var formSheet = window.Session.Workbook.AddSheet("FormSheetMvR");
            var moveMeSheet = window.Session.Workbook.AddSheet("MoveMeSheetR");

            SeedStaleCrossSheetFormula(window, refSheet, formSheet);

            // refSheet is the first (leftmost) sheet, so Move Right is a valid single-step move.
            window.Session.SelectSheet(refSheet.Id);
            InvokePrivate(window, "MoveActiveSheetRight");

            formSheet.GetValue(new CellAddress(formSheet.Id, 1, 1)).Should().Be(new NumberValue(200),
                "Move Sheet Right must force a workbook recalculation, since reordering sheets can " +
                "change which sheets a 3-D reference spans, so cross-sheet formulas must not go stale");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Seeds <paramref name="refSheet"/>!A1 = 10 and <paramref name="formSheet"/>!A1 =
    /// "={refSheet}!A1*2" (evaluates to 20 while still Automatic), then switches the workbook to
    /// Manual calculation and edits the precedent to 100 — leaving the dependent formula stale at
    /// 20 (Manual mode's edit pipeline intentionally skips auto-recalc). A production handler that
    /// forces its own recalculation afterwards must bring the dependent back to 200; one that
    /// doesn't will leave it stuck at the stale 20.
    /// </summary>
    private static void SeedStaleCrossSheetFormula(MainWindow window, Sheet refSheet, Sheet formSheet)
    {
        window.Session.SelectSheet(refSheet.Id);
        window.Session.BeginFormulaEdit(new CellAddress(refSheet.Id, 1, 1));
        window.Session.CommitCellText("10").Success.Should().BeTrue();

        window.Session.SelectSheet(formSheet.Id);
        window.Session.BeginFormulaEdit(new CellAddress(formSheet.Id, 1, 1));
        window.Session.CommitCellText($"={refSheet.Name}!A1*2").Success.Should().BeTrue();
        formSheet.GetValue(new CellAddress(formSheet.Id, 1, 1)).Should().Be(new NumberValue(20));

        window.Session.ExecuteReviewCommand(new SetCalculationModeCommand(WorkbookCalculationMode.Manual))
            .Success.Should().BeTrue();

        window.Session.SelectSheet(refSheet.Id);
        window.Session.BeginFormulaEdit(new CellAddress(refSheet.Id, 1, 1));
        window.Session.CommitCellText("100").Success.Should().BeTrue();
        formSheet.GetValue(new CellAddress(formSheet.Id, 1, 1)).Should().Be(new NumberValue(20),
            "Manual mode must not auto-recalculate the dependent formula when its precedent changes");
    }

    // ── R16-merge-align-deep-2: Merge & Center must toggle to unmerge an already-merged selection ──

    [Fact]
    public async Task MergeAndCenter_TogglesToUnmerge_WhenSelectionIsAlreadyMerged()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MergeToggleFixture");
            window.Session.SelectSheet(sheet.Id);

            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2));
            window.Session.SelectRange(range);

            // First click merges (baseline sanity: not yet merged).
            window.Session.IsSelectedRangeMerged.Should().BeFalse();
            await InvokePrivateAsync(window, "MergeAndCenterSelectedRangeAsync");

            sheet.MergedRegions.Should().Contain(range, "the first Merge & Center click must merge the selection");
            window.Session.IsSelectedRangeMerged.Should().BeTrue();
            window.StatusTextForTest.Text.Should().NotContain("failed");

            // Second click on the now-merged selection must UNMERGE (Excel/WPF toggle), not error.
            await InvokePrivateAsync(window, "MergeAndCenterSelectedRangeAsync");

            sheet.MergedRegions.Should().NotContain(range,
                "a second Merge & Center click on an already-merged selection must unmerge it (Excel toggle behavior)");
            window.Session.IsSelectedRangeMerged.Should().BeFalse();
            window.StatusTextForTest.Text.Should().Contain("Unmerged",
                "the toggle must report an unmerge status, not a 'Merge & Center failed' error");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────────────────────

    private static void InvokePrivate(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, null);
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
