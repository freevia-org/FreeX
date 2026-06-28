using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ExportPlannerDedupSourceTests
{
    [Fact]
    public void HostExportPlannerFacades_AreRemovedOrWpfNamed()
    {
        var hostRoot = Path.Combine(WorkspaceFileLocator.FindWorkspaceRoot(), "src", "FreeX.App.Host");

        File.Exists(Path.Combine(hostRoot, "ExportReadinessPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostRoot, "ExportOptionsDialogPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostRoot, "ExportSheetSelectionPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostRoot, "ExportPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostRoot, "ExportPlanner.Descriptions.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostRoot, "PrintPreviewToolbarPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostRoot, "WpfExportDescriptionPlanner.cs")).Should().BeTrue();
        File.Exists(Path.Combine(hostRoot, "WpfPrintPreviewToolbarPlanner.cs")).Should().BeTrue();
    }
}
