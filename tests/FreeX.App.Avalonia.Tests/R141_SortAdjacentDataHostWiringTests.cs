using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R141-services-sort-adjacent-data-1 built <see cref="WorkbookSession.SortAdjacentDataPromptResolver"/>
/// and <see cref="WorkbookSession.SortSelectedRange(bool)"/>'s consultation of it, but never wired a
/// resolver into either shell -- the property stayed null in production, so
/// <c>ResolveSortRangeAfterAdjacentDataPrompt</c> always short-circuited and a real user selecting
/// only part of a wider table and clicking ribbon Sort Ascending/Descending still got the silent,
/// unwarned, record-scrambling sort the original finding described. These tests exercise the real
/// production wiring rather than assigning the resolver themselves: <c>MainWindow</c>'s own
/// constructor/<c>ReplaceSession</c> wire <see cref="WorkbookSession.SortAdjacentDataPromptResolver"/>
/// to <c>MainWindow.ResolveSortAdjacentDataPrompt</c>, which checks the headless-injectable
/// <see cref="MainWindow.SortAdjacentDataPromptOverrideForTest"/> before falling back to a real owned
/// dialog -- mirroring the existing R73 <c>DataValidationPromptOverrideForTest</c> seam. Each test
/// drives the exact private UI method ribbon Sort Ascending/Descending calls via
/// <see cref="MainWindow.SortSelectedRangeForTest"/> rather than calling
/// <c>Session.SortSelectedRange</c> directly, so if the resolver wiring were ever deleted from
/// <c>MainWindow</c>, the "expand" test below would fail (the override would never be invoked and the
/// sort would silently scramble records) even though nothing here assigns the resolver itself.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R141_SortAdjacentDataHostWiringTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SubsetSelection_YesDecision_ExpandsAndKeepsRecordsAligned()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Session.SortAdjacentDataPromptResolver.Should().NotBeNull(
                "MainWindow's own construction must wire the resolver -- this test supplies only the dialog's answer, not the resolver itself");

            var sheet = SeedSalesTable(window, out var columnC);
            window.Session.SelectRange(columnC);
            var promptInvoked = false;
            window.SortAdjacentDataPromptOverrideForTest = request =>
            {
                promptInvoked = true;
                request.SelectedRange.Should().Be(columnC);
                return UserMessageResult.Yes; // "Expand the selection"
            };

            window.SortSelectedRangeForTest(ascending: true);

            promptInvoked.Should().BeTrue(
                "selecting only column C of a wider A1:C6 table must trigger Excel's Sort Warning through the app's own resolver wiring");
            // If the wiring were missing, the override above would never run and column A (Name)
            // would stay in its original, unsorted row order instead of traveling with its Team.
            var rows = Enumerable.Range(2, 5)
                .Select(row => (
                    Name: ((TextValue)sheet.GetValue((uint)row, 1)).Value,
                    Team: ((TextValue)sheet.GetValue((uint)row, 3)).Value))
                .ToList();
            var expectedByTeam = new Dictionary<string, string>
            {
                ["Beth"] = "West",
                ["Ada"] = "East",
                ["Cy"] = "North",
                ["Deb"] = "South",
                ["Eve"] = "Central",
            };
            foreach (var (name, team) in rows)
                expectedByTeam[name].Should().Be(team, $"{name}'s Team must still match their original record after the expanded sort");
            rows.Select(r => r.Team).Should().BeInAscendingOrder();

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SubsetSelection_NoDecision_SortsOnlySelectedColumnAndLeavesOthersInPlace()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = SeedSalesTable(window, out var columnC);
            window.Session.SelectRange(columnC);
            var promptInvoked = false;
            window.SortAdjacentDataPromptOverrideForTest = _ =>
            {
                promptInvoked = true;
                return UserMessageResult.No; // "Continue with the current selection"
            };

            window.SortSelectedRangeForTest(ascending: true);

            promptInvoked.Should().BeTrue();
            // Column A (Name) must stay untouched -- declining the expansion sorts exactly the
            // selected column, matching this session's pre-existing (unwarned) behavior.
            sheet.GetValue(2, 1).Should().Be(new TextValue("Beth"));
            sheet.GetValue(3, 1).Should().Be(new TextValue("Ada"));
            sheet.GetValue(4, 1).Should().Be(new TextValue("Cy"));
            sheet.GetValue(5, 1).Should().Be(new TextValue("Deb"));
            sheet.GetValue(6, 1).Should().Be(new TextValue("Eve"));

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WholeTableSelection_NeverPrompts()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("SortAdjacentWholeTableFixture");
            window.Session.SelectSheet(sheet.Id);
            SeedRows(sheet);
            var wholeTable = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));
            window.Session.SelectRange(wholeTable);
            window.SortAdjacentDataPromptOverrideForTest = _ =>
                throw new InvalidOperationException(
                    "Selecting the full current region is not a proper subset of anything -- the prompt must never fire.");

            window.SortSelectedRangeForTest(ascending: true);

            // No exception from the throwing override above is the point of this test; the sheet
            // still has all five records afterward regardless of which column ended up as the sort
            // key (QuickSortRangePlanner picks it from ActiveCell, which this test never pins).
            sheet.GetValue(1, 1).Should().Be(new TextValue("Name"));
            Enumerable.Range(2, 5)
                .Select(row => ((TextValue)sheet.GetValue((uint)row, 1)).Value)
                .Should().BeEquivalentTo(["Beth", "Ada", "Cy", "Deb", "Eve"]);

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    private static Sheet SeedSalesTable(MainWindow window, out GridRange columnC)
    {
        var sheet = window.Session.Workbook.AddSheet("SortAdjacentDataFixture");
        window.Session.SelectSheet(sheet.Id);
        SeedRows(sheet);
        columnC = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 6, 3));
        return sheet;
    }

    private static void SeedRows(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Team"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Beth"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Cy"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Deb"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("Eve"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 3), new TextValue("Central"));
    }
}
