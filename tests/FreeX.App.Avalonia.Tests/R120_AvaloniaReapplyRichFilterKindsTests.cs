using System.Reflection;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R120-avalonia-datatools-reapply-1: MainWindow.DataTools.cs's ReapplyCurrentFilterSort used to skip
/// any AutoFilter column whose persisted <see cref="WorksheetAutoFilterColumnModel.Values"/> list was
/// empty -- which is every Top 10/Above-Average/custom-condition/color column, since those persist
/// their criterion in the Top10/DynamicFilter/CustomFilters/ColorFilter fields instead. Data &gt;
/// Reapply therefore silently did nothing for those columns (no error, no updated visibility) while
/// WPF's ReapplyAutoFilter (MainWindow.DataFilterCommands.cs) correctly re-ran all of them. These tests
/// drive the real production entry points -- the same Core.Commands types the Avalonia AutoFilter UI
/// itself constructs (MainWindow.AutoFilter.cs) -- through <c>_session.ExecuteReviewCommand</c>, edit
/// the underlying data, invoke the real (private) ReapplyCurrentFilterSort via reflection, and assert
/// the resulting <see cref="Sheet.FilterHiddenRows"/> set matches what re-running each mechanism from
/// scratch on the edited data would produce.
///
/// NOTE: every callback here is a plain synchronous <c>Func&lt;TResult&gt;</c> passed to
/// <see cref="HeadlessUnitTestSession.Dispatch{TResult}(Func{TResult}, CancellationToken)"/> rather than
/// an <c>async () =&gt; ...</c> lambda -- there is no <c>Dispatch(Func&lt;Task&gt;, ...)</c> overload on
/// this type, so an async lambda with no return value binds to the plain
/// <c>Dispatch(Action, CancellationToken)</c> overload instead, compiling as "async void". An async void
/// delegate routes any exception (including every FluentAssertions failure) through
/// AsyncVoidMethodBuilder to its captured SynchronizationContext instead of back through the awaited
/// Task, so the assertion never fails the test -- confirmed by temporarily asserting a deliberately
/// wrong value inside that pattern (both here and in the pre-existing R101 test file) and observing the
/// test still report "Passed". None of these tests need real async waiting (no dialogs/timers), so the
/// synchronous overload sidesteps the gap entirely; see this round's report for the sibling lead this
/// raises for every other Avalonia headless test using the async-lambda-into-Dispatch pattern.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R120_AvaloniaReapplyRichFilterKindsTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Reapply_Top10Column_ReevaluatesAfterDataEdit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;

                SetText(sheet, 1, 1, "Amount");
                SetNumber(sheet, 2, 1, 10);
                SetNumber(sheet, 3, 1, 20);
                SetNumber(sheet, 4, 1, 30);
                SetNumber(sheet, 5, 1, 40);
                SetNumber(sheet, 6, 1, 50);

                var range = Range(sheet, 1, 1, 6, 1);
                sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

                // Real production entry point: Filter > Top 10... keeping the top 2 by value (40, 50).
                window.Session.ExecuteReviewCommand(new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true))
                    .Success.Should().BeTrue();
                sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u], "only the bottom 3 of 5 values should be hidden initially");

                // Row 2's value grows past the old top-2 boundary; the top 2 are now (100, 50), so row 6
                // (40) should now be the one hidden instead of row 2.
                SetNumber(sheet, 2, 1, 100);

                InvokeReapply(window);

                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u], "Reapply must re-rank Top 10 against the edited data");
                return true;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reapply_AboveAverageColumn_ReevaluatesAfterDataEdit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;

                SetText(sheet, 1, 1, "Amount");
                SetNumber(sheet, 2, 1, 10);
                SetNumber(sheet, 3, 1, 20);
                SetNumber(sheet, 4, 1, 30); // average = 20; only row 4 (30) is above average

                var range = Range(sheet, 1, 1, 4, 1);
                sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

                // Real production entry point: Filter > Number Filters > Above Average.
                window.Session.ExecuteReviewCommand(new AverageFilterCommand(sheet.Id, range, filterColOffset: 0, above: true))
                    .Success.Should().BeTrue();
                sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u], "rows at/below the average must be hidden initially");

                // Push row 2 far above the others; the new average (150/3=50) leaves only row 2 (100)
                // above it -- rows 3 (20) and 4 (30) are now both below it, unlike the initial state
                // where row 4 (30) was the one above the average of 10/20/30.
                SetNumber(sheet, 2, 1, 100);

                InvokeReapply(window);

                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u], "Reapply must recompute the average against the edited data");
                return true;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reapply_ColorFilterColumn_ReevaluatesAfterRecolor()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;
                var workbook = window.Session.Workbook;

                SetText(sheet, 1, 1, "Status");
                SetText(sheet, 2, 1, "Ready");
                SetText(sheet, 3, 1, "Blocked");
                SetText(sheet, 4, 1, "Pending");

                var red = new CellColor(255, 0, 0);
                Colorize(workbook, sheet, 2, 1, red);

                var range = Range(sheet, 1, 1, 4, 1);
                sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

                // Real production entry point: the AutoFilter dropdown's Filter by Color > red.
                window.Session.ExecuteReviewCommand(new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, red))
                    .Success.Should().BeTrue();
                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u], "only the red row should be visible initially");

                // Recolor row 3 red too; Reapply must pick it up as newly matching.
                Colorize(workbook, sheet, 3, 1, red);

                InvokeReapply(window);

                sheet.FilterHiddenRows.Should().BeEquivalentTo([4u], "Reapply must re-scan cell colors against the edited data");
                return true;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reapply_CustomConditionColumn_ReevaluatesAfterDataEdit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;

                SetText(sheet, 1, 1, "Amount");
                SetNumber(sheet, 2, 1, 10);
                SetNumber(sheet, 3, 1, 20);
                SetNumber(sheet, 4, 1, 30);

                var range = Range(sheet, 1, 1, 4, 1);
                sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

                // Real production entry point: the AutoFilter criteria textbox routed through
                // FilterPromptPlanner, e.g. typing ">15".
                window.Session.ExecuteReviewCommand(
                        new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(15)))
                    .Success.Should().BeTrue();
                sheet.FilterHiddenRows.Should().BeEquivalentTo([2u], "only the row at/below the threshold should be hidden initially");

                // Row 3 drops below the threshold; row 2 rises above it.
                SetNumber(sheet, 2, 1, 50);
                SetNumber(sheet, 3, 1, 5);

                InvokeReapply(window);

                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u], "Reapply must re-run the custom condition against the edited data");
                return true;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling/no-regression coverage for the adjacent gap in the same block: the previous
    /// implementation passed <c>column.Values</c> straight to <see cref="FilterCommand"/> without ever
    /// re-adding the "" blank sentinel <see cref="WorksheetAutoFilterColumnModel.IncludeBlank"/>
    /// represents, so a blank-inclusive value-list filter would silently stop allowing blanks through
    /// the moment Reapply ran.
    /// </summary>
    [Fact]
    public async Task Reapply_ValueListWithIncludeBlank_KeepsBlanksAllowedAfterEdit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;

                SetText(sheet, 1, 1, "Region");
                SetText(sheet, 2, 1, "West");
                // Row 3 explicitly cleared to blank -- a brand-new MainWindow's default sheet is not
                // empty (it carries starter/demo content), so leaving a cell merely untouched does not
                // reliably make it blank the way it would on a truly empty sheet.
                sheet.SetCell(new CellAddress(sheet.Id, 3, 1), BlankValue.Instance);
                SetText(sheet, 4, 1, "East");

                var range = Range(sheet, 1, 1, 4, 1);
                sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

                // Real production entry point: the checklist dropdown with "West" and "(Blanks)" both
                // checked -- "" is the blank sentinel (FilterCommand.SplitBlankSentinel).
                window.RunAutoFilterForTest(range, columnOffset: 0, ["West", ""]);
                sheet.FilterHiddenRows.Should().BeEquivalentTo([4u], "only the non-blank, non-West row should be hidden initially");

                // Editing an unrelated cell should not disturb the filter; Reapply must still allow
                // both "West" and the still-blank row 3 through.
                SetText(sheet, 4, 1, "North");

                InvokeReapply(window);

                sheet.FilterHiddenRows.Should().BeEquivalentTo([4u], "Reapply must keep allowing blanks through, not just the literal value list");
                return true;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// SHARED-FEATURE combination coverage: two DIFFERENT rich filter kinds active on two columns of
    /// the SAME AutoFilter range must both be rebuilt and replayed together as one undoable Reapply,
    /// mirroring WPF's per-column factory replay (which iterates every active column, not just one).
    ///
    /// Deliberately pairs a custom condition with a color filter rather than a Top 10/Above-Average
    /// filter: TopBottomFilterCommand/AverageFilterCommand scope their ranking to rows not currently
    /// owned by any OTHER active column's filter (FilterHiddenRowUpdater.IsHiddenByAnyOtherActiveMechanism),
    /// so within a single Reapply pass a ranked column can only "discover" a new leader among rows the
    /// OTHER column's criterion (still mid-recompute at that point) has not already hidden -- a real,
    /// pre-existing cross-column staleness order-dependency shared by both shells' identical
    /// build-one-composite-of-independently-rebuilt-column-commands architecture (see this round's
    /// siblingLeads), not something this fix controls. Condition and color filters carry no such
    /// scoping (FilterHiddenRowUpdater.ApplyColumnOwnedVisibility hides/shows a row purely from its own
    /// column's current data), so they compose safely and are the right pair to prove the combination.
    /// </summary>
    [Fact]
    public async Task Reapply_CombinesCustomConditionAndColorColumns_AsOneUndoRedoUnit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;
                var workbook = window.Session.Workbook;

                SetText(sheet, 1, 1, "Amount");
                SetText(sheet, 1, 2, "Status");
                SetNumber(sheet, 2, 1, 10);
                SetText(sheet, 2, 2, "Ready");
                SetNumber(sheet, 3, 1, 20);
                SetText(sheet, 3, 2, "Ready");
                SetNumber(sheet, 4, 1, 30);
                SetText(sheet, 4, 2, "Ready");

                var red = new CellColor(255, 0, 0);
                // Row 4 (amount 30, passing ">15") is initially the only row that is also red, so it is
                // the one row that passes both independent column filters.
                Colorize(workbook, sheet, 4, 2, red);

                var range = Range(sheet, 1, 1, 4, 2);
                sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

                window.Session.ExecuteReviewCommand(
                        new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(15)))
                    .Success.Should().BeTrue();
                window.Session.ExecuteReviewCommand(new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 1, red))
                    .Success.Should().BeTrue();

                // Only row 4 passes both (amount > 15 AND red status), so everything else is hidden.
                sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u], "combining two independent column filters hides whatever fails either one");

                // Row 3 becomes the new row passing both (already > 15; recolor it red too). Row 4 drops
                // to <= 15, so it flips from visible to hidden despite staying red -- this can only be
                // seen if Reapply re-evaluates BOTH columns' mechanisms together, not just one of them.
                SetNumber(sheet, 4, 1, 10);
                Colorize(workbook, sheet, 3, 2, red);

                InvokeReapply(window);

                sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u], "Reapply must re-run the custom condition AND re-scan color together in one pass");

                // A single undo restores the pre-Reapply visibility, proving both column rebuilds landed
                // as one composite history entry (mirrors R101's identical AutoFilter+AdvancedFilter check).
                window.Session.UndoLastEdit().Success.Should().BeTrue();
                sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u]);
                return true;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    private static void SetText(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));

    private static void SetNumber(Sheet sheet, uint row, uint col, double value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));

    private static void Colorize(Workbook workbook, Sheet sheet, uint row, uint col, CellColor color)
    {
        var style = CellStyle.Default.Clone();
        style.FillColor = color;
        var styleId = workbook.RegisterStyle(style);
        sheet.GetCell(row, col)!.StyleId = styleId;
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));

    private static void InvokeReapply(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("ReapplyCurrentFilterSort", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(window, null);
}
