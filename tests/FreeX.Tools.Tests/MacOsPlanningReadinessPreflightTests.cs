using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsPlanningReadinessPreflightTests
{
    [Fact]
    public void MacOsPlanningDocs_RecordLandedReadinessEvidenceWithoutDroppingRemainingBlockers()
    {
        var portPlan = File.ReadAllText(WorkspaceFileLocator.Find(
            "docs",
            "planning",
            "multiplatform-macos-port.md"));
        var dependencyBacklog = File.ReadAllText(WorkspaceFileLocator.Find(
            "docs",
            "planning",
            "macos-port-dependency-backlog.md"));
        var combined = portPlan + "\n" + dependencyBacklog;

        combined.Should().Contain("GoalSeekRequestParser");
        combined.Should().Contain("WorkbookSession.ExecuteGoalSeek");
        combined.Should().Contain("SortDialogPlanner");
        combined.Should().Contain("WorkbookExportReadinessPlanner");
        combined.Should().Contain("WorkbookExportPrintPlanner");
        combined.Should().Contain("DataValidationPresetPlanner");
        combined.Should().Contain("ApplyDataValidationToSelectedRange");
        combined.Should().Contain("ClearSelectedRangeDataValidation");
        combined.Should().Contain("DataValidationPreviewPlanner");
        combined.Should().Contain("Native Share Sheet Integration Plan");
        combined.Should().Contain("NSSharingServicePicker");
        combined.Should().Contain("GitHub-hosted macOS runners can build the app and prove that the menu route, saved-path preconditions, and fallback evidence stay wired");
        combined.Should().Contain("matching multi-range Paste Special");

        portPlan.Should().Contain("format_cells_style_roundtrip_count");
        portPlan.Should().Contain("tools/Test-MacOsPublicPreviewReadiness.ps1");
        portPlan.Should().Contain("tools/Test-MacOsHumanValidationChecklist.ps1");
        portPlan.Should().Contain("real macOS validation required for the interactive share sheet itself");
        portPlan.Should().Contain("Paste Special also handles FreeX-owned copied ranges when every selected target range matches the copied size");
        portPlan.Should().Contain("mismatched or cut-backed multi-range requests still fail explicitly");
        portPlan.Should().Contain("Public distribution still needs");
        portPlan.Should().Contain("human or local macOS proof is still needed");
        portPlan.Should().Contain("keyboard-only");
        portPlan.Should().Contain("VoiceOver");
        portPlan.Should().NotContain("Data Validation still needs fuller mutation parity beyond Paste Special metadata transfer.");
        portPlan.Should().NotContain("multi-range clipboard routes now fail explicitly for unsupported copy, cut, and Paste Special requests");
        portPlan.Should().NotContain("Multi-range clipboard requests are guarded with explicit unsupported-result messaging.");
        dependencyBacklog.Should().Contain("bounded matching multi-range Paste Special route");
        dependencyBacklog.Should().NotContain("Defer remaining WPF clipboard parity such as multi-range and full Paste Special dialog/access-key parity.");
        dependencyBacklog.Should().NotContain("Leave print/export, Windows Share");
    }
}
