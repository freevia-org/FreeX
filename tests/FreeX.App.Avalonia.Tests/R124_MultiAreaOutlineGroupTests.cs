using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R124-outlinecmds-multiarea-group-1: Avalonia twin of the WPF host fix
/// (FreeX.App.Host.Tests.R124_MultiAreaOutlineGroupTests). GroupSelectedRows/UngroupSelection
/// (MainWindow.Outline.cs) used to read only the single active _session.SelectedRange, so a
/// Ctrl+click multi-area row/column selection (built via WorkbookSession.SelectRanges, exactly
/// what real header Ctrl+click does through AddAdditionalRowSelection-style flows) only
/// grouped/ungrouped the active (last) area and silently left the other disjoint areas untouched.
/// The fix routes both handlers through ResolveOutlineSelectionRanges, which uses the same
/// SelectionStyleCommandPlanner.ResolveRanges choke point the WPF host's multi-area fixes use.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R124_MultiAreaOutlineGroupTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task GroupSelectedRows_MultiAreaRowSelection_GroupsEveryDisjointRow() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MultiAreaGroup");
            window.Session.SelectSheet(sheet.Id);

            // Two disjoint whole-row areas: row 2 and row 5 (mirrors a Ctrl+click multi-area row
            // header selection -- SelectedRange is the active/last area, SelectedRanges holds both).
            var row2 = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
            var row5 = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRanges(row5, [row2, row5]);

            InvokePrivate(window, "GroupSelectedRows");

            // Before the fix, only row 5 (the active area) was grouped; row 2 was silently left
            // ungrouped.
            sheet.RowOutlineLevels.Should().ContainKey(2u, "row 2's disjoint area must also be grouped");
            sheet.RowOutlineLevels[2].Should().Be(1);
            sheet.RowOutlineLevels.Should().ContainKey(5u, "row 5 (the active area) must be grouped");
            sheet.RowOutlineLevels[5].Should().Be(1);
            sheet.RowOutlineLevels.Should().NotContainKey(3u, "row 3 was never part of the selection");

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task UngroupSelection_MultiAreaRowSelection_UngroupsEveryDisjointRow() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MultiAreaUngroup");
            window.Session.SelectSheet(sheet.Id);

            window.Session.ExecuteReviewCommand(new GroupRowsCommand(sheet.Id, 2, 2, 1)).Success.Should().BeTrue();
            window.Session.ExecuteReviewCommand(new GroupRowsCommand(sheet.Id, 5, 5, 1)).Success.Should().BeTrue();
            sheet.RowOutlineLevels.Should().ContainKey(2u);
            sheet.RowOutlineLevels.Should().ContainKey(5u);

            var row2 = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
            var row5 = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, CellAddress.MaxCol));
            window.Session.SelectRanges(row5, [row2, row5]);

            InvokePrivate(window, "UngroupSelection");

            // Before the fix, only row 5 (the active area) was ungrouped; row 2 silently stayed
            // grouped.
            sheet.RowOutlineLevels.Should().NotContainKey(2u, "row 2's disjoint area must also be ungrouped");
            sheet.RowOutlineLevels.Should().NotContainKey(5u, "row 5 (the active area) must be ungrouped");

            window.Close();
        }, CancellationToken.None);

    // No-regression sibling: a plain single active-range Group (no multi-area selection involved)
    // must keep grouping exactly that one range, unaffected by routing the command construction
    // through the ranges-aware plumbing.
    [Fact]
    public Task GroupSelectedRows_SingleActiveRange_StillGroupsOnlyThatRange_NoRegression() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("SingleRangeGroup");
            window.Session.SelectSheet(sheet.Id);

            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 3, 1),
                new CellAddress(sheet.Id, 3, CellAddress.MaxCol)));

            InvokePrivate(window, "GroupSelectedRows");

            sheet.RowOutlineLevels.Should().ContainSingle();
            sheet.RowOutlineLevels.Should().ContainKey(3u).WhoseValue.Should().Be(1);

            window.Close();
        }, CancellationToken.None);

    private static void InvokePrivate(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, null);
    }
}
