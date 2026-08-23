using System.Threading;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class Wave194AutoFilterMixedTypePhysicalSourceTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void PhysicalSelector_PinsProductionChecklistClicksTransitionsAndPackagePostconditions()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave194AutoFilterMixedTypeFixture.ps1");
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");
        var planner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "Filtering", "AutoFilterChecklistPlanner.cs");
        var command = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.Core.Commands", "FilterCommand.cs");

        runner.Should().Contain("autofilter-mixed-type-persistence");
        runner.Should().Contain("autofilter-mixed-type-value-save-reopen-physical");
        runner.Should().Contain("Assert-AutoFilterMixedTypePostcondition");
        probe.Should().Contain("probe_autofilter_mixed_type_persistence_physical");
        probe.Should().Contain("click_autofilter_control 74 319");
        probe.Should().Contain("click_autofilter_control 74 362");
        probe.Should().Contain("click_autofilter_control 292 433");
        probe.Should().Contain("wait_for_mixed_type_popup_target");
        probe.Should().Contain("xdotool_mousemove_sync \"$((a1_x + 55))\" \"$((a1_y + 14))\" click 1");
        probe.Should().Contain("popup_route=\"header-arrow\"");
        probe.Should().Contain("target-selected=${target_selected}");
        probe.Should().Contain("popup-dismissed=${popup_dismissed}");
        probe.Should().Contain("$visible\" == \"42,42,\"");
        probe.Should().Contain("C1-formula=SUBTOTAL(103,A2:A7)|C1=2");
        fixture.Should().Contain("<c r=\"A2\"><v>42</v></c>");
        fixture.Should().Contain("New-InlineStringCell -Address \"A3\" -Value \"42\"");
        fixture.Should().Contain("New-InlineStringCell -Address \"B5\" -Value \"Blank\"");
        fixture.Should().Contain("<c r=\"A6\" s=\"1\"><v>45292</v></c>");
        fixture.Should().Contain("SUBTOTAL(103,A2:A7)");
        fixture.Should().Contain("<autoFilter ref=`\"A1:B7`\"");
        source.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan(");
        source.Should().Contain("AutoFilterMenuPlanner.BuildResult");
        source.Should().Contain("RecalculateAfterAutoFilterMutation");
        planner.Should().Contain("Workbook? workbook,");
        planner.Should().Contain("FilterValueFormatter.ToText(value)");
        command.Should().Contain("var text  = FilterValueFormatter.ToText(value);");
    }

    [Fact]
    public Task ProductionFilterEntryPoint_GroupsNumberAndNumericTextAndRecalculatesSubtotal() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Wave194 Mixed Type");
            window.Session.SelectSheet(sheet.Id);
            PopulateMixedTypeRows(sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 7, 2));
            sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B7", null);
            var totalAddress = new CellAddress(sheet.Id, 1, 3);
            sheet.SetFormula(totalAddress, "SUBTOTAL(103,A2:A7)");
            window.Session.RecalculateWorkbook();
            sheet.GetValue(totalAddress).Should().Be(new NumberValue(5));

            window.RunAutoFilterForTest(range, 0, ["42"]);

            sheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u, 7u]);
            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(42));
            sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new TextValue("42"));
            sheet.GetValue(totalAddress).Should().Be(new NumberValue(2));
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);

    private static void PopulateMixedTypeRows(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Mixed"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("42"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(45292));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new NumberValue(7));
    }
}
