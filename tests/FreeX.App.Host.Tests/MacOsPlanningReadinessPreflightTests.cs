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
        combined.Should().Contain("WorkbookShareActionPlanner");
        combined.Should().Contain("WorkbookShareActionSurface");
        combined.Should().Contain("NSSharingServicePicker");
        combined.Should().Contain("GitHub-hosted macOS runners can build the app and prove that the menu route, saved-path preconditions, and fallback evidence stay wired");

        portPlan.Should().Contain("format_cells_style_roundtrip_count");
        portPlan.Should().Contain("tools/Test-MacOsPublicPreviewReadiness.ps1");
        portPlan.Should().Contain("real macOS validation required for the interactive share sheet itself");
        portPlan.Should().Contain("Public distribution still needs");
        portPlan.Should().Contain("Human macOS proof is still needed");
        portPlan.Should().Contain("keyboard-only");
        portPlan.Should().Contain("VoiceOver");
        portPlan.Should().NotContain("Data Validation still needs fuller mutation parity beyond Paste Special metadata transfer.");
        dependencyBacklog.Should().NotContain("Leave print/export, Windows Share");
    }
}
