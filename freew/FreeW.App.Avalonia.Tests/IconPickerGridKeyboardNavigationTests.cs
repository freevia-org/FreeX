using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round-138 finding (A): the Avalonia "Insert Icon" grid (<see cref="IconPickerDialog"/>) was
/// entirely mouse-only, same as its WPF twin. These tests drive real <see cref="Key"/> routed
/// KeyDown events through the dialog's actual <c>_tiles</c> panel (the same routed-event mechanism
/// a real keyboard produces), asserting on the resulting internal focus/selection state -- not on
/// a Focusable/IsTabStop property existing, and not by calling <see cref="IconGridNavigation.Move"/>
/// directly (that pure helper is covered separately in FreeW.App.Presentation.Tests; a bug wiring it
/// into this dialog would not show up there).
/// </summary>
public sealed class IconPickerGridKeyboardNavigationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task ArrowKeysMoveFocusAndEnterSelectsTheFocusedTile_NoMouseInputAnywhere()
    {
        await Session.Dispatch(() =>
        {
            var dialog = CreateDialog();
            var tiles = Tiles(dialog);
            var tilesPerRow = IconPickerDialogPlanner.Surface.TilesPerRow;
            tiles.Count.Should().BeGreaterThan(tilesPerRow,
                "the real icon catalog must span at least two rows for this test to exercise Down");
            FocusedIndex(dialog).Should().Be(0, "Refresh() seeds keyboard focus on the first tile");

            RaiseKey(dialog, Key.Right);
            FocusedIndex(dialog).Should().Be(1);

            RaiseKey(dialog, Key.Down);
            FocusedIndex(dialog).Should().Be(1 + tilesPerRow);

            // Only keyboard focus has moved so far -- nothing is selected yet.
            Session_(dialog).State.SelectedEntry.Should().BeNull();

            RaiseKey(dialog, Key.Enter);

            var expected = (IconPickerEntry)tiles[1 + tilesPerRow].Tag!;
            Session_(dialog).State.SelectedEntry.Should().Be(expected);

            // A visible focus indicator marks the focused tile, and only that tile.
            tiles[1 + tilesPerRow].BorderBrush.Should().Be(Brushes.Black);
            tiles[0].BorderBrush.Should().NotBe(Brushes.Black);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SpaceAlsoSelectsTheFocusedTileAndHomeEndJumpToTheEdges()
    {
        await Session.Dispatch(() =>
        {
            var dialog = CreateDialog();
            var tiles = Tiles(dialog);

            RaiseKey(dialog, Key.End);
            FocusedIndex(dialog).Should().Be(tiles.Count - 1);

            RaiseKey(dialog, Key.Space);
            Session_(dialog).State.SelectedEntry.Should().Be((IconPickerEntry)tiles[^1].Tag!);

            RaiseKey(dialog, Key.Home);
            FocusedIndex(dialog).Should().Be(0);

            // Left/Up at the very first tile do not wrap around or leave the grid.
            RaiseKey(dialog, Key.Left);
            FocusedIndex(dialog).Should().Be(0);
            RaiseKey(dialog, Key.Up);
            FocusedIndex(dialog).Should().Be(0);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MouseClickStillSelectsATile_NoRegression()
    {
        await Session.Dispatch(() =>
        {
            var dialog = CreateDialog();
            var tiles = Tiles(dialog);
            var thirdEntry = (IconPickerEntry)tiles[2].Tag!;
            var select = typeof(IconPickerDialog).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!;
            select.Invoke(dialog, [thirdEntry, tiles[2]]);

            Session_(dialog).State.SelectedEntry.Should().Be(thirdEntry);
        }, CancellationToken.None);
    }

    private static IconPickerDialog CreateDialog() =>
        (IconPickerDialog)(typeof(IconPickerDialog)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!
            .Invoke(null));

    private static WrapPanel Grid(IconPickerDialog dialog) =>
        (WrapPanel)typeof(IconPickerDialog).GetField("_tiles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static List<Border> Tiles(IconPickerDialog dialog) =>
        [.. Grid(dialog).Children.OfType<Border>()];

    private static int FocusedIndex(IconPickerDialog dialog) =>
        (int)typeof(IconPickerDialog).GetField("_focusedIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static IconPickerDialogSession Session_(IconPickerDialog dialog) =>
        (IconPickerDialogSession)typeof(IconPickerDialog).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static void RaiseKey(IconPickerDialog dialog, Key key) =>
        Grid(dialog).RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
        });
}
