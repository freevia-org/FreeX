using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave188AutoFilterNumericPhysicalSourceTests
{
    [Fact]
    public void NumericPhysicalSelector_RequiresBothCriteriaRowsAndB1GlyphEvidence()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave188AutoFilterNumericFixture.ps1");
        var entrypoint = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "entrypoint.sh");

        runner.Should().Contain("autofilter-numeric-criteria-persistence");
        runner.Should().Contain("autofilter-numeric-criteria-greater-than-save-reopen-physical");
        runner.Should().Contain("autofilter-numeric-criteria-equals-save-reopen-physical");
        runner.Should().Contain("New-FreeXWave188AutoFilterNumericFixture.ps1");
        probe.Should().Contain("wait_for_expected_document");
        probe.Should().Contain("AutoFilterButton_1_2");
        probe.Should().Contain("greaterThan|value=50");
        probe.Should().Contain("colId=1");
        probe.Should().Contain("\"75,100,\"");
        probe.Should().Contain("\"50,\"");
        fixture.Should().Contain("New-NumberCell");
        fixture.Should().Contain("<autoFilter ref=`\"A1:B5`\"");
        entrypoint.Should().Contain("expected_document_name");
        entrypoint.Should().Contain("$window_name\" == *\"$expected_document_name\"*");
    }

    [Fact]
    public void NumericCriteriaSurface_UsesSharedCriteriaPlannerAndWorkflowSession()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");

        source.Should().Contain("AutoFilterMenuPlanner.BuildCompletedCriteriaText");
        source.Should().Contain("_filterWorkflowSession.PlanDialogResult(");
        source.Should().Contain("_session.ExecuteWorksheetFilterMutationPlan(plan)");
        source.Should().Contain("AutoFilterButton_{address.Row}_{address.Col}");
        source.Should().Contain("buttonBorder.PointerPressed");
    }
}
