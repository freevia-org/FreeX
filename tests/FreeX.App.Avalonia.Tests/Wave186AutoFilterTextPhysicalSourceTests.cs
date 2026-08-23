using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave186AutoFilterTextPhysicalSourceTests
{
    [Fact]
    public void PhysicalSelector_IsExplicitAndRequiresBothTextCriteriaRows()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");

        runner.Should().Contain("autofilter-text-criteria-persistence");
        runner.Should().Contain("autofilter-text-criteria-begins-with-save-reopen-physical");
        runner.Should().Contain("autofilter-text-criteria-equals-save-reopen-physical");
        probe.Should().Contain("FREEX_X11_PROBE_SELECTOR");
        probe.Should().Contain("probe_autofilter_text_criteria_persistence_physical");
        probe.Should().Contain("begins-visible=$begins_visible");
        probe.Should().Contain("equals-visible=$equals_visible");
        probe.Should().Contain("begins-dialog-closed=$begins_dialog_closed");
        probe.Should().Contain("equals-dialog-closed=$equals_dialog_closed");
        probe.Should().Contain("\"$begins_visible\" == \"North,Northwest,\"");
        probe.Should().Contain("\"$equals_visible\" == \"East,,\"");
        probe.Should().Contain("\"$begins_reopened\" == \"North,Northwest,\"");
        probe.Should().Contain("\"$equals_reopened\" == \"East,,\"");
        probe.Should().Contain("value=North*");
        probe.Should().Contain("value=East");
        probe.Should().Contain("status\":\"failed\"");
    }

    [Fact]
    public void AvaloniaCriteriaSurface_UsesSharedCriteriaPlannerAndWorkflowSession()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");

        source.Should().Contain("AutoFilterMenuPlanner.BuildCompletedCriteriaText");
        source.Should().Contain("_filterWorkflowSession.PlanDialogResult(");
        source.Should().Contain("_session.ExecuteWorksheetFilterMutationPlan(plan)");
        source.Should().NotContain("new FilterConditionCommand(");
    }
}
