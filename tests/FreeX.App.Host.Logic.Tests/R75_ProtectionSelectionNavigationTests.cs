using System;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R75-services-protection-security-4-1:
/// <see cref="FreeX.Core.Commands.CommandGuards.CanSelectCell"/> already existed in Core.Commands
/// but was never called from any shell, so a protected sheet with "Select locked cells" unchecked
/// still let the user click, Tab, or arrow onto a locked cell -- Excel refuses the selection and
/// skips locked cells during keyboard navigation instead.
///
/// Covers the WPF host's wiring in src/FreeX.App.Host/MainWindow.Selection.cs:
///   - SheetGrid_MouseDown's plain-click branch now consults the extracted
///     <c>CanSelectCellForClick</c> helper (split out for testability, mirroring the
///     R49-render-multiarea-selection-3-2 precedent for <c>TryHandleCellAreaExtendClick</c> --
///     driving a real, pixel-accurate WPF MouseButtonEventArgs through SheetGrid's hit-testing is
///     not a reliable unit-test surface) before selecting the clicked cell.
///   - MainWindow_KeyDown's arrow/Tab/Enter navigation now skips a locked cell in the direction of
///     travel instead of landing on it.
/// </summary>
public sealed class R75_ProtectionSelectionNavigationTests
{
    [Fact]
    public void CanSelectCellForClick_LockedCellOnProtectedSheetWithoutSelectLockedCells_ReturnsFalse()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                sheet.IsProtected = true;
                sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);
                var locked = new CellAddress(sheet.Id, 3, 3); // default cell style is Locked = true

                var canSelect = (bool)R49MainWindowTestHarness.Invoke(window, "CanSelectCellForClick", locked)!;

                canSelect.Should().BeFalse(
                    "a locked cell must not be selectable by a plain click when Select Locked Cells is unchecked");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void CanSelectCellForClick_UnlockedCellOnProtectedSheetWithoutSelectLockedCells_ReturnsTrue()
    {
        // Sibling/no-regression: an unlocked cell must remain selectable even with "Select locked
        // cells" unchecked (only "Select unlocked cells" governs it, and that stays on by default).
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var unlockedStyleId = workbook.RegisterStyle(new CellStyle { Locked = false });
                sheet.SetStyleOnly(3, 3, unlockedStyleId);
                sheet.IsProtected = true;
                sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);
                var unlocked = new CellAddress(sheet.Id, 3, 3);

                var canSelect = (bool)R49MainWindowTestHarness.Invoke(window, "CanSelectCellForClick", unlocked)!;

                canSelect.Should().BeTrue("an unlocked cell must stay selectable regardless of Select Locked Cells");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void CanSelectCellForClick_UnprotectedSheet_ReturnsTrueForALockedCell()
    {
        // Sibling/no-regression: an unprotected sheet must remain fully selectable.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var locked = new CellAddress(sheet.Id, 3, 3);

                var canSelect = (bool)R49MainWindowTestHarness.Invoke(window, "CanSelectCellForClick", locked)!;

                canSelect.Should().BeTrue("an unprotected sheet must not restrict selection at all");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void CanSelectCellForClick_SelectLockedCellsPermissionEnabled_ReturnsTrueForALockedCell()
    {
        // Sibling/no-regression: checking "Select locked cells" (the default) must keep locked
        // cells selectable on a protected sheet.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                sheet.IsProtected = true;
                // Sheet.ProtectionPermissions defaults to [SelectLockedCells, SelectUnlockedCells].
                var locked = new CellAddress(sheet.Id, 3, 3);

                var canSelect = (bool)R49MainWindowTestHarness.Invoke(window, "CanSelectCellForClick", locked)!;

                canSelect.Should().BeTrue("Select Locked Cells being checked must keep locked cells selectable");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void MainWindowKeyDown_RightArrow_ProtectedSheetWithLockedCellBetween_SkipsToNextSelectableCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var unlockedStyleId = workbook.RegisterStyle(new CellStyle { Locked = false });
                // A1 (start) and C1 (expected landing target) unlocked; B1 stays locked (the
                // default cell style) in between, so a plain click there would be refused.
                sheet.SetStyleOnly(1, 1, unlockedStyleId);
                sheet.SetStyleOnly(1, 3, unlockedStyleId);
                sheet.IsProtected = true;
                sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheet.Id, 1, 1));

                var handled = PressArrowKey(window, Key.Right);

                handled.Should().BeTrue();
                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 3)),
                    "Right-arrow navigation on a protected sheet must skip the locked B1 cell and land on the next selectable cell C1");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void MainWindowKeyDown_RightArrow_UnprotectedSheet_MovesToImmediatelyAdjacentCell()
    {
        // Sibling/no-regression: an unprotected sheet must still move one cell at a time, exactly
        // as before this fix.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheet.Id, 1, 1));

                var handled = PressArrowKey(window, Key.Right);

                handled.Should().BeTrue();
                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2)),
                    "an unprotected sheet must navigate one cell at a time");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void MainWindowKeyDown_RightArrow_SelectLockedCellsPermissionEnabled_MovesToImmediatelyAdjacentLockedCell()
    {
        // Sibling/no-regression: with Select Locked Cells checked (the default), a protected sheet
        // must still allow navigating straight onto a locked cell.
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

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheet.Id, 1, 1));

                var handled = PressArrowKey(window, Key.Right);

                handled.Should().BeTrue();
                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2)),
                    "Select Locked Cells being checked must allow navigating straight onto the locked B1 cell");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
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
