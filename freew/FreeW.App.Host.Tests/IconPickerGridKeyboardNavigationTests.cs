using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round-138 finding (A): the WPF "Insert Icon" grid (<see cref="IconPickerDialog"/>) was entirely
/// mouse-only -- there was no keyboard way to move through or select a tile. These tests drive real
/// <see cref="System.Windows.Input.Key"/> routed KeyDown events through the dialog's actual
/// <c>_grid</c> panel (the same routed-event mechanism a real keyboard produces), asserting on the
/// resulting internal focus/selection state -- not on a Focusable/IsTabStop property existing, and
/// not by calling <see cref="IconGridNavigation.Move"/> directly (that pure helper is covered
/// separately in FreeW.App.Presentation.Tests; a bug wiring it into the dialog would not show up
/// there).
/// </summary>
public sealed class IconPickerGridKeyboardNavigationTests
{
    [StaFact]
    public void ArrowKeysMoveFocusAndEnterSelectsTheFocusedTile_NoMouseInputAnywhere()
    {
        var dialog = CreateDialog();
        try
        {
            var tiles = Tiles(dialog);
            var tilesPerRow = IconPickerDialogPlanner.Surface.TilesPerRow;
            tiles.Count.Should().BeGreaterThan(tilesPerRow,
                "the real icon catalog must span at least two rows for this test to exercise Down");
            FocusedIndex(dialog).Should().Be(0, "Refresh() seeds keyboard focus on the first tile");

            RaiseKey(Grid(dialog), Key.Right);
            FocusedIndex(dialog).Should().Be(1);

            RaiseKey(Grid(dialog), Key.Down);
            FocusedIndex(dialog).Should().Be(1 + tilesPerRow);

            // Only keyboard focus has moved so far -- nothing is selected yet.
            Session(dialog).State.SelectedEntry.Should().BeNull();

            RaiseKey(Grid(dialog), Key.Enter);

            var expected = (IconPickerEntry)tiles[1 + tilesPerRow].Tag!;
            Session(dialog).State.SelectedEntry.Should().Be(expected);

            // A visible focus indicator marks the focused tile, and only that tile.
            tiles[1 + tilesPerRow].BorderBrush.Should().Be(Brushes.Black);
            tiles[0].BorderBrush.Should().NotBe(Brushes.Black);
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void SpaceAlsoSelectsTheFocusedTileAndHomeEndJumpToTheEdges()
    {
        var dialog = CreateDialog();
        try
        {
            var tiles = Tiles(dialog);

            RaiseKey(Grid(dialog), Key.End);
            FocusedIndex(dialog).Should().Be(tiles.Count - 1);

            RaiseKey(Grid(dialog), Key.Space);
            Session(dialog).State.SelectedEntry.Should().Be((IconPickerEntry)tiles[^1].Tag!);

            RaiseKey(Grid(dialog), Key.Home);
            FocusedIndex(dialog).Should().Be(0);

            // Left/Up at the very first tile do not wrap around or leave the grid.
            RaiseKey(Grid(dialog), Key.Left);
            FocusedIndex(dialog).Should().Be(0);
            RaiseKey(Grid(dialog), Key.Up);
            FocusedIndex(dialog).Should().Be(0);
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void MouseClickStillSelectsATile_NoRegression()
    {
        var dialog = CreateDialog();
        try
        {
            var tiles = Tiles(dialog);
            var method = typeof(IconPickerDialog).GetMethod("OnTileClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            // OnTileClick(object sender, MouseButtonEventArgs e) never reads e, only sender -- so a
            // real MouseButtonEventArgs (which needs a live MouseDevice) is unnecessary here.
            method.Invoke(dialog, [tiles[2], null]);

            Session(dialog).State.SelectedEntry.Should().Be((IconPickerEntry)tiles[2].Tag!);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static IconPickerDialog CreateDialog()
    {
        var dialog = (IconPickerDialog)typeof(IconPickerDialog)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Window)], null)!
            .Invoke([null]);
        // A real PresentationSource is required to raise routed KeyEventArgs (its constructor
        // rejects a null inputSource) -- show the dialog off-screen, exactly as
        // SharedBackstageFrameTests.ArrowDown_OnRail_MovesFocusToNextNavButton does for the same
        // reason, rather than modally (Show, not ShowDialog, so the test thread is not blocked).
        dialog.WindowStyle = WindowStyle.None;
        dialog.Left = -10000;
        dialog.Top = -10000;
        dialog.Show();
        dialog.UpdateLayout();
        return dialog;
    }

    private static WrapPanel Grid(IconPickerDialog dialog) =>
        (WrapPanel)typeof(IconPickerDialog).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static List<Border> Tiles(IconPickerDialog dialog) =>
        [.. Grid(dialog).Children.OfType<Border>()];

    private static int FocusedIndex(IconPickerDialog dialog) =>
        (int)typeof(IconPickerDialog).GetField("_focusedIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static IconPickerDialogSession Session(IconPickerDialog dialog) =>
        (IconPickerDialogSession)typeof(IconPickerDialog).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static void RaiseKey(UIElement element, Key key)
    {
        var source = PresentationSource.FromVisual(element)
            ?? throw new InvalidOperationException("Dialog must be Shown before raising routed key events.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        };
        element.RaiseEvent(args);
    }
}
