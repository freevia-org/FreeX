using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R130 (parity debt, round 130): the Avalonia shell's "Merge Cells" (<c>MergeSelectedRangeAsync</c>)
/// and "Merge Across" (<c>MergeAcrossSelectedRangeAsync</c>, both in MainWindow.MergePaste.cs) built
/// their command(s) against ONLY <c>_session.ActiveSheet</c>, unlike the WPF host's
/// <c>MergeCellsMenuItem_Click</c> / <c>MergeAcrossMenuItem_Click</c> (MainWindow.HomeFormatting.cs),
/// which both fan out to every sheet <c>CurrentGroupedEditSheetIds()</c> returns via
/// <c>TryExecuteRepeatableCurrentSelectionRangesCommand</c>. With sheet tabs grouped, the same ribbon
/// gesture ("Merge Cells" / "Merge Across") therefore merged every grouped sheet on Windows but only
/// the active sheet on Linux/macOS -- a functional divergence, though NOT itself data loss pre-fix,
/// because the content-loss ANALYSIS was equally narrow (single-sheet) and so stayed consistent with
/// the equally narrow execution.
///
/// The fix widens BOTH the execution (fan `areas` across
/// <c>_session.GetCurrentGroupedEditSheetIds()</c> via <c>GroupedSheetRangePlanner.RemapRangeToSheet</c>,
/// mirroring <c>SelectionStyleCommandPlanner.CreateRangeCommand</c>'s sheet x range cross product) AND
/// the content-loss guard (<c>AnalyzeGroupedSheetMergeContent</c> -- the SAME grouped-sheet-aware helper
/// R128B already wired for Merge &amp; Center and the Format Cells merge checkbox) in the SAME change, to
/// avoid the r127/r128 trap of a widened execution outrunning a still-narrow guard and silently
/// discarding a non-active grouped sheet's content.
///
/// Mirrors R128B_GroupedSheetMergeContentWarningTests' established pattern for driving these private,
/// real, async production entry points headlessly (InvokePrivateTaskAsync + DrainInputAsync pumping +
/// a real modal warning dialog), and R127_MultiAreaMergeCellsTests' fixture conventions for
/// MergeSelectedRangeAsync/MergeAcrossSelectedRangeAsync specifically.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R130_MergeCellsGroupedSheetTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task MergeCells_GroupedSheets_MergesEveryGroupedSheet()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet1 = window.Session.Workbook.Sheets[0];
                var sheet2 = window.Session.Workbook.AddSheet("MergeCellsGroupedFanout");
                window.Session.SelectSheet(sheet1.Id);
                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();

                // Both sheets are content-free in this range (the sample-workbook seed lives at
                // B1:C1 -- see the R129B gotcha note below -- so pick a range it does not touch),
                // so no warning dialog is expected and the merge must land on BOTH sheets.
                var range = new GridRange(new CellAddress(sheet1.Id, 20, 2), new CellAddress(sheet1.Id, 20, 3)); // B20:C20
                window.Session.SelectRange(range);

                var task = InvokePrivateTaskAsync(window, "MergeSelectedRangeAsync");
                await DrainInputAsync();
                await task;

                window.OwnedWindows.Should().BeEmpty("neither grouped sheet has content in B20:C20 to lose");

                // THE DEFECT (pre-fix): only sheet1 (the active sheet) was merged; sheet2 was left
                // completely untouched despite being part of the same grouped-sheet edit.
                sheet1.MergedRegions.Should().Contain(range, "the active grouped sheet must be merged");
                sheet2.MergedRegions.Should().Contain(
                    new GridRange(new CellAddress(sheet2.Id, 20, 2), new CellAddress(sheet2.Id, 20, 3)),
                    "the non-active grouped sheet must ALSO be merged, matching the WPF host's " +
                    "CurrentGroupedEditSheetIds() fan-out for MergeCellsMenuItem_Click");
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MergeCells_GroupedSheets_NonActiveSheetContent_TriggersWarningAndCancelPreservesData()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet1 = window.Session.Workbook.Sheets[0];
                var sheet2 = window.Session.Workbook.AddSheet("MergeCellsGroupedSiblingLoss");

                // Write Sheet2's content BEFORE grouping, and ungrouped -- committing a cell edit
                // WHILE sheets are grouped fans the write out to every grouped sheet (real Excel
                // grouped-edit semantics: typing into a grouped cell mirrors onto every other grouped
                // sheet), which would silently also write this value onto Sheet1 and defeat the
                // "only Sheet2 has content" premise this test depends on.
                window.Session.SelectSheet(sheet2.Id);
                window.Session.BeginFormulaEdit(new CellAddress(sheet2.Id, 20, 3));
                window.Session.CommitCellText("keep-me-mergecells").Success.Should().BeTrue();

                window.Session.SelectSheet(sheet1.Id);
                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(sheet1.Id);

                // B20:C20 on the ACTIVE sheet (Sheet1) is empty -- pre-fix, analyzing only Sheet1 finds
                // nothing to lose here. Sheet2's C20 -- the same range's non-top-left cell once
                // remapped -- holds the real content the grouped-sheet fan-out merge is about to blank.
                var range = new GridRange(new CellAddress(sheet1.Id, 20, 2), new CellAddress(sheet1.Id, 20, 3));
                window.Session.SelectRange(range);

                var task = InvokePrivateTaskAsync(window, "MergeSelectedRangeAsync");
                await DrainInputAsync();

                // THE DEFECT: because only the active sheet (Sheet1, empty) was analyzed, the pre-fix
                // code never showed the warning here, and the fan-out merge that followed would have
                // silently discarded Sheet2's "keep-me-mergecells" content with zero warning.
                window.OwnedWindows.Should().ContainSingle(
                    "a grouped sheet's content is about to be blanked by the Merge Cells fan-out, " +
                    "even though the active sheet's own range is empty");
                var dialog = window.OwnedWindows.OfType<Window>().Single();
                AutomationProperties.GetAutomationId(dialog).Should().Be("MergeCellsContentWarningDialog");

                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await DrainInputAsync();
                await task;

                sheet1.MergedRegions.Should().BeEmpty("cancelling the warning must abort the whole grouped-sheet merge");
                sheet2.MergedRegions.Should().BeEmpty("cancelling the warning must abort the whole grouped-sheet merge");
                sheet2.GetCell(new CellAddress(sheet2.Id, 20, 3))!.Value.Should().Be(new TextValue("keep-me-mergecells"),
                    "Sheet2's content must survive an aborted grouped-sheet merge -- this is exactly " +
                    "the content the pre-fix bug silently discarded with no warning at all");
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MergeAcross_GroupedSheets_MergesEveryGroupedSheetPerRow()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet1 = window.Session.Workbook.Sheets[0];
                var sheet2 = window.Session.Workbook.AddSheet("MergeAcrossGroupedFanout");
                window.Session.SelectSheet(sheet1.Id);
                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();

                var range = new GridRange(new CellAddress(sheet1.Id, 20, 2), new CellAddress(sheet1.Id, 21, 3)); // B20:C21
                window.Session.SelectRange(range);

                var task = InvokePrivateTaskAsync(window, "MergeAcrossSelectedRangeAsync");
                await DrainInputAsync();
                await task;

                window.OwnedWindows.Should().BeEmpty("neither grouped sheet has content in B20:C21 to lose");

                // THE DEFECT (pre-fix): only sheet1's rows were merged; sheet2 was left untouched.
                sheet1.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet1.Id, 20, 2), new CellAddress(sheet1.Id, 20, 3)));
                sheet1.MergedRegions.Should().Contain(new GridRange(new CellAddress(sheet1.Id, 21, 2), new CellAddress(sheet1.Id, 21, 3)));
                sheet2.MergedRegions.Should().Contain(
                    new GridRange(new CellAddress(sheet2.Id, 20, 2), new CellAddress(sheet2.Id, 20, 3)),
                    "the non-active grouped sheet's row 20 must ALSO be merged");
                sheet2.MergedRegions.Should().Contain(
                    new GridRange(new CellAddress(sheet2.Id, 21, 2), new CellAddress(sheet2.Id, 21, 3)),
                    "the non-active grouped sheet's row 21 must ALSO be merged");
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MergeAcross_GroupedSheets_NonActiveSheetContent_TriggersWarningAndCancelPreservesData()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet1 = window.Session.Workbook.Sheets[0];
                var sheet2 = window.Session.Workbook.AddSheet("MergeAcrossGroupedSiblingLoss");

                // See the MergeCells sibling test above: write Sheet2's content BEFORE grouping (and
                // while ungrouped) -- committing a cell edit WHILE sheets are grouped fans the write
                // out to every grouped sheet, which would silently also write this value onto Sheet1.
                window.Session.SelectSheet(sheet2.Id);
                window.Session.BeginFormulaEdit(new CellAddress(sheet2.Id, 20, 3));
                window.Session.CommitCellText("keep-me-mergeacross").Success.Should().BeTrue();

                window.Session.SelectSheet(sheet1.Id);
                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(sheet1.Id);

                var range = new GridRange(new CellAddress(sheet1.Id, 20, 2), new CellAddress(sheet1.Id, 20, 3)); // B20:C20, empty on Sheet1
                window.Session.SelectRange(range);

                var task = InvokePrivateTaskAsync(window, "MergeAcrossSelectedRangeAsync");
                await DrainInputAsync();

                window.OwnedWindows.Should().ContainSingle(
                    "a grouped sheet's content is about to be blanked by the Merge Across fan-out, " +
                    "even though the active sheet's own row is empty");
                var dialog = window.OwnedWindows.OfType<Window>().Single();
                AutomationProperties.GetAutomationId(dialog).Should().Be("MergeCellsContentWarningDialog");

                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await DrainInputAsync();
                await task;

                sheet1.MergedRegions.Should().BeEmpty("cancelling the warning must abort the whole grouped-sheet merge");
                sheet2.MergedRegions.Should().BeEmpty("cancelling the warning must abort the whole grouped-sheet merge");
                sheet2.GetCell(new CellAddress(sheet2.Id, 20, 3))!.Value.Should().Be(new TextValue("keep-me-mergeacross"),
                    "Sheet2's content must survive an aborted grouped-sheet Merge Across -- this is " +
                    "exactly the content the pre-fix bug silently discarded with no warning at all");
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    // No-regression sibling: the ordinary UNGROUPED case must be entirely unaffected -- Merge Cells
    // still merges only the active (single) sheet, and a still-present content-loss warning on that
    // sheet still fires exactly as before.
    [Fact]
    public async Task MergeCells_Ungrouped_StillMergesOnlyActiveSheetAndStillWarns()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet = window.Session.Workbook.AddSheet("MergeCellsUngroupedFixture");
                window.Session.SelectSheet(sheet.Id);
                window.Session.IsWorkbookGrouped.Should().BeFalse();

                var range = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 5, 3)); // B20:C20
                window.Session.BeginFormulaEdit(new CellAddress(sheet.Id, 5, 3));
                window.Session.CommitCellText("active-sheet-only").Success.Should().BeTrue();
                window.Session.SelectRange(range);

                var task = InvokePrivateTaskAsync(window, "MergeSelectedRangeAsync");
                await DrainInputAsync();

                window.OwnedWindows.Should().ContainSingle("the active sheet itself still has content that would be lost");
                var dialog = window.OwnedWindows.OfType<Window>().Single();
                var keepFirstButton = dialog.GetVisualDescendants()
                    .OfType<Button>()
                    .First(candidate => AutomationProperties.GetAutomationId(candidate) == "MergeCellsKeepFirstButton");
                keepFirstButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                await task;

                sheet.MergedRegions.Should().ContainSingle();
                sheet.MergedRegions.Should().Contain(range);
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    private static Task InvokePrivateAsync(MainWindow window, string methodName, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        return (Task)method.Invoke(window, args)!;
    }

    private static Task InvokePrivateTaskAsync(MainWindow window, string methodName, params object[] args) =>
        InvokePrivateAsync(window, methodName, args);

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void CloseWindowAndOwnedDialogs(Window window)
    {
        foreach (var owned in window.OwnedWindows.OfType<Window>().ToArray())
        {
            try { owned.Close(); } catch { /* best effort: never mask the real test failure */ }
        }

        try
        {
            (window as MainWindow)?.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
        catch { /* best effort */ }
    }
}
