using System.Reflection;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R112-med-3 (src/FreeX.App.Host/MainWindow.CellsCommands.cs's
/// CreateAutoFitRowHeightCommand/CreateAutoFitColumnWidthCommand, and the shared choke point in
/// src/FreeX.Core.Commands/CompositeWorkbookCommand.cs).
///
/// RowColumnSizingPlanner.GetMeasurementBounds deliberately returns null -- meaning "nothing to
/// size" -- when the auto-fit axis's own extent already spans the selection's full row/column
/// count (e.g. Auto Row Height invoked on a selection that is one or more entire columns, reached
/// by clicking a column header then Home &gt; Format &gt; AutoFit Row Height). When that happens,
/// RowColumnSizingPlanner.CreateAutoFit*Command returns null and MainWindow.CellsCommands.cs's
/// fallback substitutes <c>new CompositeWorkbookCommand("Auto Row Height", [])</c> -- an empty
/// composite.
///
/// Before the fix: CompositeWorkbookCommand.Apply on an empty (or all-no-op) child list still
/// returned <c>CommandOutcome(true)</c> with IsNoOp left at its default false, so CommandBus.Execute
/// and MainWindow.TryExecuteCommand (which gate the undo push and MarkWorkbookDirty() purely on
/// Success &amp;&amp; !IsNoOp) treated the genuinely-empty auto-fit as a real edit: it pushed a
/// phantom "Auto Row Height"/"Auto Column Width" entry onto the undo stack and dirtied an
/// otherwise-clean workbook, contradicting Excel (a no-effect AutoFit creates no undo entry and
/// does not mark the workbook modified).
///
/// After the fix: CompositeWorkbookCommand.Apply reports IsNoOp true whenever it wraps zero
/// children, or every child that did run was itself a no-op, so the empty auto-fit composite is
/// correctly treated as "nothing happened" no matter how deep the CompositeWorkbookCommand
/// nesting goes (e.g. a grouped-sheet composite of per-sheet empty composites).
///
/// These tests drive the real ribbon menu click handlers (FormatAutoRowMenuItem_Click /
/// FormatAutoColMenuItem_Click) on a real MainWindow via TryExecuteGroupedSheetCommand ->
/// TryExecuteCommand -> CommandBus.Execute, not the private planner/command classes directly, so
/// the whole user-reachable path (undo stack + dirty flag) is covered.
/// </summary>
public sealed class R112_AutoFitEmptyPlanNoOpTests
{
    private static WorkbookSession GetDocumentState(MainWindow window) => window.Session;

    [Fact]
    public void FormatAutoRowMenuItem_Click_WholeColumnSelection_IsNoOp_NoUndoEntryNoDirty()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.SetCell(new CellAddress(sheetId, 5, 2), new TextValue("some content"));

                // A whole-column selection (row span == CellAddress.MaxRow) is exactly the shape
                // RowColumnSizingPlanner.GetMeasurementBounds refuses to size for the Rows axis
                // (its own extent already spans every row of the selection) -- reached in the
                // real app by clicking a column header, then Home > Format > AutoFit Row Height.
                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, CellAddress.MaxRow, 2));

                var documentState = GetDocumentState(window);
                var commandBusField = typeof(MainWindow).GetField("_commandBus", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var commandBus = (ICommandBus)commandBusField.GetValue(window)!;
                documentState.IsDirty.Should().BeFalse("the workbook starts clean");

                R49MainWindowTestHarness.Invoke(window, "FormatAutoRowMenuItem_Click", null, null);

                commandBus.GetUndoStackDepth(workbook.Id).Should().Be(0,
                    "an empty auto-fit plan must not push a phantom undo entry");
                documentState.IsDirty.Should().BeFalse(
                    "an empty auto-fit plan must not mark an otherwise-clean workbook as modified");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void FormatAutoColMenuItem_Click_WholeRowSelection_IsNoOp_NoUndoEntryNoDirty()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.SetCell(new CellAddress(sheetId, 2, 5), new TextValue("some content"));

                // A whole-row selection (column span == CellAddress.MaxCol) is the mirror shape for
                // the Columns axis -- reached by clicking a row header, then Home > Format > AutoFit
                // Column Width.
                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 2, CellAddress.MaxCol));

                var documentState = GetDocumentState(window);
                var commandBusField = typeof(MainWindow).GetField("_commandBus", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var commandBus = (ICommandBus)commandBusField.GetValue(window)!;

                R49MainWindowTestHarness.Invoke(window, "FormatAutoColMenuItem_Click", null, null);

                commandBus.GetUndoStackDepth(workbook.Id).Should().Be(0,
                    "an empty auto-fit plan must not push a phantom undo entry");
                documentState.IsDirty.Should().BeFalse(
                    "an empty auto-fit plan must not mark an otherwise-clean workbook as modified");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void FormatAutoRowMenuItem_Click_NormalSelectionWithRealResize_StillPushesUndoAndDirties()
    {
        // Sibling no-regression: a genuine AutoFit (one that actually resizes a row) must still
        // push exactly one undo entry and mark the workbook dirty, exactly as before the
        // IsNoOp-propagation fix -- the fix must only suppress the phantom empty-plan case.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 15;
                var address = new CellAddress(sheetId, 1, 1);
                sheet.SetCell(address, new TextValue("Tall"));
                sheet.GetCell(address)!.StyleId = workbook.RegisterStyle(new CellStyle { FontSize = 48 });

                window.SheetGrid.SelectedRange = new GridRange(address, address);

                var documentState = GetDocumentState(window);
                var commandBusField = typeof(MainWindow).GetField("_commandBus", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var commandBus = (ICommandBus)commandBusField.GetValue(window)!;

                R49MainWindowTestHarness.Invoke(window, "FormatAutoRowMenuItem_Click", null, null);

                sheet.RowHeights.Should().ContainKey(1u);
                sheet.RowHeights[1].Should().BeGreaterThan(sheet.DefaultRowHeight,
                    "a 48pt font must grow the row well past the 15pt default so this is a genuine, non-no-op resize");
                commandBus.GetUndoStackDepth(workbook.Id).Should().Be(1,
                    "a genuine resize must still push exactly one undo entry");
                documentState.IsDirty.Should().BeTrue("a genuine resize must still mark the workbook dirty");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
