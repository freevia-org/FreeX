using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// r188-avalonia-home-target: NavigateActiveCell cleared the sticky End-Mode flag BEFORE the switch
/// that computes the navigation target, and its Home case never read the flag at all -- it always
/// returned column 1 of the current row (or A1 with Ctrl). So on Avalonia:
///
///   * End then Home performed a plain Home instead of reproducing Ctrl+End, the behaviour the WPF
///     host implements deliberately and pins with R82-app-keyboard-nav-5-2; and
///   * Ctrl+Home ignored frozen panes, jumping to A1 rather than to the first unfrozen cell.
///
/// Both now go through the shared ExcelWorksheetNavigationPlanner.GetHomeTarget, with the flag read
/// before it is cleared.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R188_AvaloniaEndHomeAndFrozenHomeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task EndThenHome_JumpsToTheUsedRangeEndLikeCtrlEnd()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo; use a clean sheet.
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            sheet.SetCell(new CellAddress(sheet.Id, 9, 4), new TextValue("used range end"));
            window.Session.SelectCell(new CellAddress(sheet.Id, 3, 2));

            await window.RaiseKeyDownForTest(
                new KeyEventArgs { Key = Key.End, KeyModifiers = KeyModifiers.None });
            await window.RaiseKeyDownForTest(
                new KeyEventArgs { Key = Key.Home, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Should().Be(
                new CellAddress(sheet.Id, 9, 4),
                "End then Home reproduces Ctrl+End, as it does in Excel and in the WPF host");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HomeWithoutEndMode_StillMovesToTheFirstColumnOfTheRow()
    {
        // The fix must not turn every Home into a Ctrl+End: without End Mode the plain behaviour
        // is unchanged.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 9, 4), new TextValue("used range end"));
            window.Session.SelectCell(new CellAddress(sheet.Id, 3, 5));

            await window.RaiseKeyDownForTest(
                new KeyEventArgs { Key = Key.Home, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 3, 1));

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlHome_OnAFrozenSheet_LandsOnTheFirstUnfrozenCell()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            sheet.FrozenRows = 2;
            sheet.FrozenCols = 3;
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 8, 8));

            await window.RaiseKeyDownForTest(
                new KeyEventArgs { Key = Key.Home, KeyModifiers = KeyModifiers.Control });

            window.Session.ActiveCell.Should().Be(
                new CellAddress(sheet.Id, 3, 4),
                "Ctrl+Home goes to the first cell below and right of the frozen panes, not A1");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
