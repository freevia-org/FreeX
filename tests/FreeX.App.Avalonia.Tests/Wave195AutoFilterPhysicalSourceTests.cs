using System.Threading;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class Wave195AutoFilterPhysicalSourceTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void PhysicalSelectors_PinProductionX11ScenariosAndPackageTransitions()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var multiFixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave195AutoFilterMultiColumnFixture.ps1");
        var colorFixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave195AutoFilterColorChangeFixture.ps1");
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");

        runner.Should().Contain("autofilter-multi-column-persistence");
        runner.Should().Contain("autofilter-color-change-clear-persistence");
        runner.Should().Contain("Assert-AutoFilterMultiColumnPostcondition");
        runner.Should().Contain("Assert-AutoFilterColorChangeClearPostcondition");
        probe.Should().Contain("probe_autofilter_multi_column_persistence_physical");
        probe.Should().Contain("probe_autofilter_color_change_clear_persistence_physical");
        probe.Should().Contain("columns=0:North;1:Hardware;");
        probe.Should().Contain("columns=1:Software;");
        probe.Should().Contain("click_autofilter_control 190 220");
        probe.Should().Contain("click_autofilter_control 151 168");
        probe.Should().Contain("click_autofilter_control \"$((column_offset * cell_width + 292))\" \"$ok_y\"");
        probe.Should().Contain("local column_offset=\"$1\"\n        click_autofilter_control \"$((column_offset * cell_width + 151))\" 117");
        probe.Should().Contain("choose_value 0 1 405");
        probe.Should().Contain("choose_value 1 0 391");
        probe.Should().Contain("change_value 1 0 1");
        probe.Should().Contain("clipboard_sentinel_value=\"__FREEX_CLIPBOARD_SENTINEL_${BASHPID}_${RANDOM}_${RANDOM}__\"");
        probe.Should().Contain("wait_for_non_sentinel_clipboard");
        probe.Should().Contain("reload-witness-before=$reload_witness_before");
        probe.Should().Contain("reload-witness-discarded=$reload_witness_discarded");
        probe.Should().Contain("restore_calibrated_window_geometry || return 1");
        probe.Should().Contain("$reload_witness_passed");
        probe.Should().Contain("fill:#FFC000");
        probe.Should().Contain("cleared-package=$cleared_package");
        multiFixture.Should().Contain("<autoFilter ref=`\"A1:C7`\"");
        multiFixture.Should().Contain("North");
        multiFixture.Should().Contain("Hardware");
        colorFixture.Should().Contain("FF00B050");
        colorFixture.Should().Contain("FFFFC000");
        colorFixture.Should().Contain("<autoFilter ref=`\"A1:B5`\"");
        source.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan(");
        source.Should().Contain("AutoFilterMenuPlanner.BuildResult");
        source.Should().Contain("RecalculateAfterAutoFilterMutation");
    }

    [Fact]
    public Task ProductionFilterEntryPoint_AndsTwoColumnsAndClearsEachColumn() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Wave195 Multi Column");
            window.Session.SelectSheet(sheet.Id);
            PopulateRows(sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 7, 3));
            sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C7", null);

            window.RunAutoFilterForTest(range, 0, ["North"]);
            sheet.AutoFilter!.FilterColumns
                .Where(column => column.ColumnId == 0 && column.Values.SequenceEqual(new[] { "North" }))
                .Should().ContainSingle();
            window.RunAutoFilterForTest(range, 1, ["Hardware"]);
            sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u, 6u, 7u]);
            sheet.AutoFilter!.FilterColumns.Should().HaveCount(2);
            sheet.AutoFilter.FilterColumns
                .Where(column => column.ColumnId == 1 && column.Values.SequenceEqual(new[] { "Hardware" }))
                .Should().ContainSingle();

            window.RunAutoFilterForTest(range, 1, ["Software"]);
            sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u, 5u, 6u, 7u]);
            sheet.AutoFilter!.FilterColumns.Should().HaveCount(2);
            sheet.AutoFilter.FilterColumns
                .Where(column => column.ColumnId == 1 && column.Values.SequenceEqual(new[] { "Software" }))
                .Should().ContainSingle();

            window.RunAutoFilterForTest(range, 0, []);
            sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u, 6u]);
            sheet.AutoFilter!.FilterColumns
                .Where(column => column.ColumnId == 1 && column.Values.SequenceEqual(new[] { "Software" }))
                .Should().ContainSingle();

            window.RunAutoFilterForTest(range, 1, []);
            sheet.FilterHiddenRows.Should().BeEmpty();
            sheet.AutoFilter!.FilterColumns.Should().BeEmpty();
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ProductionDropdownPlanner_RoutesBColumnChecklistAndResult() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Wave195 Planner Route");
            window.Session.SelectSheet(sheet.Id);
            PopulateRows(sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 7, 3));
            sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C7", null);

            window.RunAutoFilterForTest(range, 0, ["North"]);
            window.RunAutoFilterForTest(range, 1, ["Hardware"]);

            AutoFilterDropdownMenuPlanner.TryPlan(
                    range,
                    new CellAddress(sheet.Id, 1, 2),
                    out var plan)
                .Should().BeTrue();
            plan.FilterColumnOffset.Should().Be(1);
            var menuPlan = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
                window.Session.Workbook,
                sheet,
                plan,
                InvariantAutoFilterMenuTextProvider.Instance,
                InvariantAutoFilterMenuTextProvider.BlankDisplayText);
            var menu = AutoFilterMenuPlanner.Build(menuPlan);

            menu.Header.Should().Be("Category");
            menu.Items
                .Where(item => item.Kind == AutoFilterMenuItemKind.ChecklistItem)
                .Select(item => (item.Label, item.IsChecked))
                .Should().Equal(("Hardware", true), ("Software", false));
            var result = AutoFilterMenuPlanner.BuildResult(
                AutoFilterMenuPlanner.CreateDialogItems(menu),
                searchText: "",
                criteriaText: "");
            result.SelectedValues.Should().Equal("Hardware");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ProductionColorFilterClear_RemovesTheSerializedCriterion() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Wave195 Color Clear");
            window.Session.SelectSheet(sheet.Id);
            PopulateColorRows(sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 2));
            var red = new CellColor(0, 176, 80);
            sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B5", null);

            window.Session.ExecuteReviewCommand(new CellFillColorFilterCommand(sheet.Id, range, 0, red))
                .Success.Should().BeTrue();
            sheet.AutoFilter!.FilterColumns
                .Where(column => column.ColorFilter is { CellColor: true, Color: var color } && color == red)
                .Should().ContainSingle();

            window.RunAutoFilterForTest(range, 0, []);

            sheet.AutoFilter!.FilterColumns.Should().BeEmpty();
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);

    private static void PopulateRows(Sheet sheet)
    {
        var rows = new (string Region, string Category, double Amount)[]
        {
            ("Region", "Category", 0),
            ("North", "Hardware", 100),
            ("North", "Software", 200),
            ("South", "Hardware", 300),
            ("South", "Software", 400),
            ("East", "Hardware", 500),
            ("East", "Software", 600)
        };

        for (var row = 0; row < rows.Length; row++)
        {
            var addressRow = (uint)(row + 1);
            sheet.SetCell(new CellAddress(sheet.Id, addressRow, 1), new TextValue(rows[row].Region));
            sheet.SetCell(new CellAddress(sheet.Id, addressRow, 2), new TextValue(rows[row].Category));
            if (row > 0)
                sheet.SetCell(new CellAddress(sheet.Id, addressRow, 3), new NumberValue(rows[row].Amount));
        }
    }

    private static void PopulateColorRows(Sheet sheet)
    {
        var rows = new[] { "Region", "North", "South", "East", "West" };
        for (var row = 0; row < rows.Length; row++)
        {
            var addressRow = (uint)(row + 1);
            sheet.SetCell(new CellAddress(sheet.Id, addressRow, 1), new TextValue(rows[row]));
            sheet.SetCell(new CellAddress(sheet.Id, addressRow, 2), new TextValue("Value"));
        }
    }
}
