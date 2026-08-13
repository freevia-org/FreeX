using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for two round-82 findings in
/// src/FreeX.App.Host/MainWindow.Selection.cs's ready-mode worksheet key handling:
///
///  - R82-app-keyboard-nav-5-1: plain Enter/Shift+Enter pressed on an already-selected,
///    non-edited cell (no active edit in progress) hardcoded a Down/Up move and ignored the
///    "After pressing Enter, move selection" option -- both its direction and its
///    enable/disable flag.
///  - R82-app-keyboard-nav-5-2: pressing End (toggling Excel's sticky END mode) followed by Home
///    performed a plain Home instead of reproducing Ctrl+End (the last used cell on the
///    worksheet), unlike "End, &lt;arrow&gt;" which correctly reproduces Ctrl+&lt;arrow&gt;.
/// </summary>
public sealed class R82_EnterDirectionAndEndModeHomeTests
{
    // ── R82-app-keyboard-nav-5-1 ─────────────────────────────────────────────

    [Fact]
    public void Enter_ReadyMode_ConfiguredRightDirection_MovesRightInsteadOfDown()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                SetOptions(window, moveSelectionAfterEnter: true, direction: AppOptionsEnterDirection.Right);
                var sheetId = workbook.GetSheetAt(0).Id;
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 2, 2)); // B2

                PressKey(window, Key.Enter);

                var expected = new CellAddress(sheetId, 2, 3); // C2
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(expected, expected),
                    "ready-mode Enter must honor the configured 'After pressing Enter, move selection' " +
                    "direction (Right), not hardcode Down");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void Enter_ReadyMode_MoveSelectionAfterEnterDisabled_StaysOnSameCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                SetOptions(window, moveSelectionAfterEnter: false, direction: AppOptionsEnterDirection.Down);
                var sheetId = workbook.GetSheetAt(0).Id;
                var current = new CellAddress(sheetId, 2, 2); // B2
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", current);

                PressKey(window, Key.Enter);

                window.SheetGrid.SelectedRange.Should().Be(new GridRange(current, current),
                    "with 'After pressing Enter, move selection' unchecked, ready-mode Enter must leave " +
                    "the active cell in place instead of always moving down");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: the default configuration (enabled, Down) still moves down exactly
    // like before this fix.
    [Fact]
    public void Enter_ReadyMode_DefaultDownDirection_StillMovesDown()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                SetOptions(window, moveSelectionAfterEnter: true, direction: AppOptionsEnterDirection.Down);
                var sheetId = workbook.GetSheetAt(0).Id;
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 2, 2)); // B2

                PressKey(window, Key.Enter);

                var expected = new CellAddress(sheetId, 3, 2); // B3
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(expected, expected));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // ── R82-app-keyboard-nav-5-2 ─────────────────────────────────────────────

    [Fact]
    public void EndThenHome_JumpsToLastUsedCell_LikeCtrlEnd()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.SetCell(new CellAddress(sheetId, 20, 10), new NumberValue(1)); // J20 is the used range's end.
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 2, 2)); // B2

                PressKey(window, Key.End);
                PressKey(window, Key.Home);

                var expected = new CellAddress(sheetId, 20, 10);
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(expected, expected),
                    "'End, Home' must reproduce Ctrl+End (the last used cell), not a plain Home to " +
                    "column A of the current row");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: plain Home (no preceding End) still moves to column A of the
    // current row.
    [Fact]
    public void PlainHome_WithoutEndMode_StillMovesToColumnAOfCurrentRow()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.SetCell(new CellAddress(sheetId, 20, 10), new NumberValue(1));
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", new CellAddress(sheetId, 5, 5)); // E5

                PressKey(window, Key.Home);

                var expected = new CellAddress(sheetId, 5, 1); // A5
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(expected, expected));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SetOptions(MainWindow window, bool moveSelectionAfterEnter, AppOptionsEnterDirection direction)
    {
        var field = typeof(MainWindow).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_options");
        var options = (AppOptions)field.GetValue(window)!;
        options.MoveSelectionAfterEnter = moveSelectionAfterEnter;
        options.AfterEnterDirection = direction;
    }

    private static void PressKey(MainWindow window, Key key)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        R49MainWindowTestHarness.Invoke(window, "MainWindow_KeyDown", window, args);
        R49MainWindowTestHarness.PumpDispatcher();
    }
}
