using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Pins the two behaviours the Avalonia ready-mode keyboard navigation had drifted from the WPF host
/// on, both now resolved by the shared <c>ExcelWorksheetNavigationPlanner.ResolveTarget</c>:
/// (a) Enter on a ready (non-edited) cell honors "After pressing Enter, move selection" -- on/off and
/// direction, with Shift+Enter moving the opposite way -- instead of the previously hard-coded
/// one-row-down / one-row-up; (b) a plain arrow key pressed on a merged cell moves to the first cell
/// BEYOND the merge instead of being absorbed because the +/-1 step still lands inside the same merge.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaReadyModeNavigationParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // In-memory options so the test never touches the real disk-backed options store.
    private static FreeXOptionsRuntimeSession CreateOptionsSession(
        bool moveSelectionAfterEnter,
        AppOptionsEnterDirection direction)
    {
        var shared = new AppOptions
        {
            MoveSelectionAfterEnter = moveSelectionAfterEnter,
            AfterEnterDirection = direction,
        };
        return new FreeXOptionsRuntimeSession(
            initialOptions: shared,
            load: () => shared.Clone(),
            save: _ => true);
    }

    private static Task<CellAddress> PressFromCell(
        bool moveSelectionAfterEnter,
        AppOptionsEnterDirection direction,
        Key key,
        KeyModifiers modifiers,
        uint row,
        uint col,
        Action<Sheet>? configureSheet = null) =>
        Session.Dispatch(async () =>
        {
            var window = new MainWindow([], null!, CreateOptionsSession(moveSelectionAfterEnter, direction));
            CellAddress landed;
            try
            {
                window.Show();
                var sheet = window.Session.Workbook.AddSheet("CleanFixture");
                configureSheet?.Invoke(sheet);
                window.Session.SelectSheet(sheet.Id);
                window.Session.SelectCell(new CellAddress(sheet.Id, row, col));

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = key, KeyModifiers = modifiers });

                var active = window.Session.ActiveCell;
                landed = new CellAddress(sheet.Id, active.Row, active.Col);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            // Returned at the lambda's own statement level so the headless Dispatch contract scanner
            // sees the value-returning (awaited) overload.
            return landed;
        }, CancellationToken.None);

    [Theory]
    [InlineData(AppOptionsEnterDirection.Down, KeyModifiers.None, 6u, 5u)]
    [InlineData(AppOptionsEnterDirection.Down, KeyModifiers.Shift, 4u, 5u)]
    [InlineData(AppOptionsEnterDirection.Right, KeyModifiers.None, 5u, 6u)]
    [InlineData(AppOptionsEnterDirection.Right, KeyModifiers.Shift, 5u, 4u)]
    [InlineData(AppOptionsEnterDirection.Up, KeyModifiers.None, 4u, 5u)]
    [InlineData(AppOptionsEnterDirection.Left, KeyModifiers.None, 5u, 4u)]
    public async Task ReadyModeEnter_HonorsAfterEnterDirection_AndShiftReversesIt(
        AppOptionsEnterDirection direction,
        KeyModifiers modifiers,
        uint expectedRow,
        uint expectedCol)
    {
        var active = await PressFromCell(true, direction, Key.Enter, modifiers, 5, 5);

        active.Row.Should().Be(expectedRow);
        active.Col.Should().Be(expectedCol);
    }

    [Theory]
    [InlineData(KeyModifiers.None)]
    [InlineData(KeyModifiers.Shift)]
    public async Task ReadyModeEnter_WithMoveSelectionAfterEnterOff_DoesNotMove(KeyModifiers modifiers)
    {
        var active = await PressFromCell(false, AppOptionsEnterDirection.Down, Key.Enter, modifiers, 5, 5);

        active.Row.Should().Be(5u);
        active.Col.Should().Be(5u);
    }

    [Theory]
    [InlineData(Key.Up, 1u, 2u)]
    [InlineData(Key.Down, 5u, 2u)]
    [InlineData(Key.Left, 2u, 1u)]
    [InlineData(Key.Right, 2u, 5u)]
    public async Task PlainArrowFromAMergedCell_MovesToTheFirstCellBeyondTheMerge(Key key, uint expectedRow, uint expectedCol)
    {
        var active = await PressFromCell(
            true,
            AppOptionsEnterDirection.Down,
            key,
            KeyModifiers.None,
            2,
            2,
            sheet => sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 4, 4))));

        active.Row.Should().Be(expectedRow);
        active.Col.Should().Be(expectedCol);
    }
}
