using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave191AutoFilterColorPhysicalSourceTests
{
    [Fact]
    public void ColorPhysicalSelector_RequiresRenderedFillSwatchAndExactDxfPostconditions()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave191AutoFilterColorFixture.ps1");

        runner.Should().Contain("autofilter-color-persistence");
        runner.Should().Contain("autofilter-color-fill-save-reopen-physical");
        runner.Should().Contain("New-FreeXWave191AutoFilterColorFixture.ps1");
        runner.Should().Contain("Assert-AutoFilterColorPostcondition");
        runner.Should().Contain("before-rgb=#FFFFFF");
        runner.Should().Contain("sample-rgb=#00B050");
        probe.Should().Contain("probe_autofilter_color_persistence_physical");
        probe.Should().Contain("verify_rendered_fill_swatch");
        probe.Should().Contain("%[hex:p{${sample_x},${sample_y}}]");
        probe.Should().Contain("autofilter-color-swatch-gate.txt");
        probe.Should().Contain("criteria=\"$(verify_rendered_fill_swatch");
        probe.Should().Contain("click_autofilter_control 110 220");
        probe.Should().Contain("swatch-gate=$swatch_gate");
        probe.Should().NotContain("criteria=\"fill:#00B050\"");
        probe.Should().Contain("fill:#00B050");
        probe.Should().Contain("North,East,");
        probe.Should().Contain("ref=A1:B5|colId=0|cellColor=1");
        probe.Should().Contain("FF00B050");
        probe.Should().Contain("copy_cell_formula_by_address A4");
        probe.Should().Contain("status\":\"failed\"");
        fixture.Should().Contain("00B050");
        fixture.Should().Contain("FFC000");
        fixture.Should().Contain("<autoFilter ref=`\"A1:B5`\"");
    }

    [Fact]
    public void ColorPhysicalSurface_UsesSharedColorWorkflowAndRealAvaloniaFlyout()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");
        var workflow = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "Filtering", "WorksheetFilterWorkflowSession.cs");

        source.Should().Contain("CreateAutoFilterColorPanel(model.ColorOptions");
        source.Should().Contain("new AutoFilterColorFilter(option.Kind, option.Color)");
        source.Should().Contain("_filterWorkflowSession.PlanDialogResult(");
        source.Should().Contain("_session.ExecuteWorksheetFilterMutationPlan(plan)");
        source.Should().NotContain("new CellFillColorFilterCommand(");
        workflow.Should().Contain("CellFillColorFilterCommand");
        workflow.Should().Contain("AutoFilterColorFilterKind.CellFillColor");
    }
}
