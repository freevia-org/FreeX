using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R87-commands-protection-lock-5-1: Shift+click / F8-extend /
/// Shift+Arrow selection extension bypassed the "Select locked cells" protection permission
/// (<see cref="FreeX.Core.Commands.CommandGuards.CanSelectCell"/>) on a protected sheet.
///
/// Before this fix:
///   - MainWindow.Selection.cs's <c>TryHandleCellAreaExtendClick</c> called
///     <c>ExtendSelection</c> unconditionally for any Shift-click/F8-extend click, never
///     consulting <c>CanSelectCell</c> (unlike the plain-click branch's
///     <c>CanSelectCellForClick</c>, added for R75-services-protection-security-4-1).
///   - <c>MainWindow_KeyDown</c>'s arrow-key navigation only ran the locked-cell-skip check when
///     <c>willSetActiveCell</c> was true, which is false whenever Shift/F8 EXTENDS the selection
///     (<c>extendSelection &amp;&amp; !moveOnly &amp;&amp; _selectionAnchor.HasValue</c>) -- so
///     Shift+Arrow could both extend the highlighted selection onto a locked cell AND land the
///     cursor there, unlike a plain arrow-key move which already skipped past it.
///
/// These tests drive <c>ExcelSelectionMode.Extend</c> (F8 "Extend Selection" mode) rather than a
/// physical Shift modifier, since <c>Keyboard.Modifiers</c> is naturally <c>None</c> in a headless
/// test run and <c>ExcelSelectionModePlanner.ShouldExtendSelection</c> treats F8 mode identically
/// to Shift (see R68_F8ExtendSelectionMouseClickTests for the established precedent).
/// </summary>
public sealed class R87_ProtectionExtendSelectionTests
{
    [Fact]
    public void TryHandleCellAreaExtendClick_F8ExtendMode_LockedCellOnProtectedSheetWithoutSelectLockedCells_DoesNotExtendSelection()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                sheet.IsProtected = true;
                sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var locked = new CellAddress(sheet.Id, 5, 4); // default cell style is Locked = true

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a1);
                SetSelectionMode(window, ExcelSelectionMode.Extend);

                var handled = (bool)R49MainWindowTestHarness.Invoke(window, "TryHandleCellAreaExtendClick", locked)!;

                handled.Should().BeTrue("the click must still be consumed, exactly like a refused plain click");
                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(a1, a1),
                    "extending onto a locked cell must be refused outright, leaving the selection unchanged");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void TryHandleCellAreaExtendClick_F8ExtendMode_UnlockedCellOnProtectedSheetWithoutSelectLockedCells_StillExtends()
    {
        // Sibling/no-regression: extending onto an UNLOCKED cell must still work exactly as
        // before this fix.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var unlockedStyleId = workbook.RegisterStyle(new CellStyle { Locked = false });
                sheet.SetStyleOnly(5, 4, unlockedStyleId);
                sheet.IsProtected = true;
                sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var unlocked = new CellAddress(sheet.Id, 5, 4);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a1);
                SetSelectionMode(window, ExcelSelectionMode.Extend);

                var handled = (bool)R49MainWindowTestHarness.Invoke(window, "TryHandleCellAreaExtendClick", unlocked)!;

                handled.Should().BeTrue();
                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(a1, unlocked),
                    "an unlocked cell must remain extendable regardless of Select Locked Cells");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void MainWindowKeyDown_RightArrowExtend_ProtectedSheetWithLockedCellBetween_SkipsLockedCellWhenExtending()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var unlockedStyleId = workbook.RegisterStyle(new CellStyle { Locked = false });
                // A1 (anchor) and C1 (expected extend target) unlocked; B1 stays locked (default
                // style) in between, so extending onto it must be skipped.
                sheet.SetStyleOnly(1, 1, unlockedStyleId);
                sheet.SetStyleOnly(1, 3, unlockedStyleId);
                sheet.IsProtected = true;
                sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);
                var a1 = new CellAddress(sheet.Id, 1, 1);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a1);
                SetSelectionMode(window, ExcelSelectionMode.Extend);

                var handled = PressArrowKey(window, Key.Right);

                handled.Should().BeTrue();
                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(a1, new CellAddress(sheet.Id, 1, 3)),
                    "Shift/F8 range-EXTENSION on a protected sheet must skip the locked B1 cell " +
                    "and extend to the next selectable cell C1, just like a plain move would");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void MainWindowKeyDown_RightArrowExtend_UnprotectedSheet_ExtendsToImmediatelyAdjacentCell()
    {
        // Sibling/no-regression: an unprotected sheet must still extend one cell at a time.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a1);
                SetSelectionMode(window, ExcelSelectionMode.Extend);

                var handled = PressArrowKey(window, Key.Right);

                handled.Should().BeTrue();
                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(a1, new CellAddress(sheet.Id, 1, 2)),
                    "an unprotected sheet must extend one cell at a time");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void MainWindowKeyDown_RightArrowExtend_SelectLockedCellsPermissionEnabled_ExtendsOntoLockedCell()
    {
        // Sibling/no-regression: with Select Locked Cells checked (the default), a protected
        // sheet must still allow extending straight onto a locked cell.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var unlockedStyleId = workbook.RegisterStyle(new CellStyle { Locked = false });
                sheet.SetStyleOnly(1, 1, unlockedStyleId);
                sheet.IsProtected = true;
                // Sheet.ProtectionPermissions defaults to [SelectLockedCells, SelectUnlockedCells].
                var a1 = new CellAddress(sheet.Id, 1, 1);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a1);
                SetSelectionMode(window, ExcelSelectionMode.Extend);

                var handled = PressArrowKey(window, Key.Right);

                handled.Should().BeTrue();
                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(a1, new CellAddress(sheet.Id, 1, 2)),
                    "Select Locked Cells being checked must allow extending straight onto the locked B1 cell");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SetSelectionMode(MainWindow window, ExcelSelectionMode mode)
    {
        var field = typeof(MainWindow).GetField("_selectionMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_selectionMode");
        field.SetValue(window, mode);
    }

    private static bool PressArrowKey(MainWindow window, Key key)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        R49MainWindowTestHarness.Invoke(window, "MainWindow_KeyDown", window, args);
        R49MainWindowTestHarness.PumpDispatcher();
        return args.Handled;
    }
}
