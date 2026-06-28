using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MiscWorkflowPlannerDedupSourceTests
{
    [Fact]
    public void PortableWorkflowPlanners_DoNotRemainAsPureHostFacades()
    {
        var hostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var servicesDirectory = Path.Combine(hostDirectory, "..", "FreeX.App.Services");
        var presentationDirectory = Path.Combine(hostDirectory, "..", "FreeX.App.Presentation");

        File.Exists(Path.Combine(hostDirectory, "FindReplaceDialog.Planner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostDirectory, "FormulaAuditSelectionPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostDirectory, "ShareWorkbookPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostDirectory, "SpellCheckWorkflowPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostDirectory, "ZoomSelectionPlanner.cs")).Should().BeFalse();

        File.Exists(Path.Combine(servicesDirectory, "FindReplaceDialogPlanner.cs")).Should().BeTrue();
        File.Exists(Path.Combine(servicesDirectory, "SpellCheckWorkflowPlanner.cs")).Should().BeTrue();
        File.Exists(Path.Combine(servicesDirectory, "LocalAccountWorkflowPlanner.cs")).Should().BeTrue();
        File.Exists(Path.Combine(servicesDirectory, "CrashAnalyticsConsentWorkflowPlanner.cs")).Should().BeTrue();
        File.Exists(Path.Combine(presentationDirectory, "FormulaAuditSelectionPlanner.cs")).Should().BeTrue();
        File.Exists(Path.Combine(servicesDirectory, "ZoomSelectionPlanner.cs")).Should().BeTrue();
    }

    [Fact]
    public void RemainingHostWorkflowPlannerFiles_AreDocumentedAdapters()
    {
        DialogSourceTestSupport.ReadHostSources("LocalAccountPlanner.cs")
            .Should()
            .Contain("Host adapter");
        DialogSourceTestSupport.ReadHostSources("CrashAnalyticsConsentPlanner.cs")
            .Should()
            .Contain("Host adapter");
        DialogSourceTestSupport.ReadHostSources("HyperlinkNavigationPlanner.cs")
            .Should()
            .Contain("Only the WPF-specific dialog prefill");
    }
}
