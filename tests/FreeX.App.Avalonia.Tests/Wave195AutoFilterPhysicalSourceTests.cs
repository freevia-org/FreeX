using System.Threading;
using Avalonia.Headless;
using FluentAssertions;
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
            window.RunAutoFilterForTest(range, 1, ["Hardware"]);
            sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u, 6u, 7u]);

            window.RunAutoFilterForTest(range, 1, ["Software"]);
            sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u, 5u, 6u, 7u]);

            window.RunAutoFilterForTest(range, 0, []);
            sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u, 6u]);

            window.RunAutoFilterForTest(range, 1, []);
            sheet.FilterHiddenRows.Should().BeEmpty();
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
}
