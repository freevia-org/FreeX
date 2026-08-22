namespace FreeP.RenderCompare.Tests;

public sealed class VisualEvidenceOrchestrationSourceTests
{
    [Fact]
    public void Whole_window_hosts_share_portable_coordination_and_keep_native_realization()
    {
        var portable = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "freep", "TestSupport", "VisualEvidence", "WholeWindowVisualEvidenceHostCoordinator.cs");
        var hosts = new[]
        {
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "TestSupport", "VisualEvidence.Wpf", "WpfWholeWindowVisualEvidenceCoordinator.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "TestSupport", "VisualEvidence.Avalonia", "AvaloniaWholeWindowVisualEvidenceCoordinator.cs"),
        };

        portable.Should().Contain("public interface IWholeWindowVisualEvidenceNativeInspector")
            .And.Contain("private void Activate(WholeWindowVisualEvidenceActivation activation)")
            .And.Contain("private bool PrepareViewState(")
            .And.Contain("new WholeWindowVisualEvidenceSemanticState(")
            .And.NotContain("using System.Windows")
            .And.NotContain("using Avalonia");
        hosts.Should().AllSatisfy(host =>
        {
            host.Should().Contain(": IWholeWindowVisualEvidenceNativeInspector")
                .And.Contain(": IVisualEvidenceAppHost")
                .And.Contain("_coordinator = new(new ")
                .And.Contain("BoundsRelativeTo(")
                .And.Contain("DescribeFocus(")
                .And.NotContain("private void Activate(")
                .And.NotContain("private bool PrepareViewState(")
                .And.NotContain("new WholeWindowVisualEvidenceSemanticState(");
        });
    }

    [Fact]
    public void Whole_window_manifest_hashes_portable_and_native_test_support_sources()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Generate-FreePWholeWindowVisualEvidenceManifest.ps1");

        source.Should().Contain("freep\\TestSupport\\VisualEvidence\\WholeWindowVisualEvidenceHostCoordinator.cs")
            .And.Contain("freep\\TestSupport\\VisualEvidence.Wpf\\WpfWholeWindowVisualEvidenceCapture.cs")
            .And.Contain("freep\\TestSupport\\VisualEvidence.Wpf\\WpfWholeWindowVisualEvidenceCoordinator.cs")
            .And.Contain("freep\\TestSupport\\VisualEvidence.Wpf\\MainWindow.VisualCaptureAdapter.cs")
            .And.Contain("freep\\TestSupport\\VisualEvidence.Avalonia\\AvaloniaWholeWindowVisualEvidenceCapture.cs")
            .And.Contain("freep\\TestSupport\\VisualEvidence.Avalonia\\AvaloniaWholeWindowVisualEvidenceCoordinator.cs")
            .And.Contain("freep\\TestSupport\\VisualEvidence.Avalonia\\MainWindow.VisualCaptureAdapter.cs")
            .And.NotContain("freep\\FreeP.App.Host\\MainWindow.WholeWindowVisualEvidence.cs")
            .And.NotContain("freep\\FreeP.App.Avalonia\\MainWindow.WholeWindowVisualEvidence.cs");
    }

    [Fact]
    public void Wpf_and_Avalonia_capture_adapters_delegate_ui_free_orchestration()
    {
        var sources = new[]
        {
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "TestSupport", "VisualEvidence.Wpf", "WpfDialogPaneVisualEvidenceCapture.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "TestSupport", "VisualEvidence.Wpf", "WpfWholeWindowVisualEvidenceCapture.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "TestSupport", "VisualEvidence.Avalonia", "AvaloniaDialogPaneVisualEvidenceCapture.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "TestSupport", "VisualEvidence.Avalonia", "AvaloniaWholeWindowVisualEvidenceCapture.cs"),
        };

        sources.Should().AllSatisfy(source =>
        {
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.ParseRequest(");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.CreateAppHostPolicy(");
            source.Should().Contain("hostPolicy.CreateOutputPlan(");
            source.Should().Contain("hostPolicy.CreateScenarioOutputPlan(");
            source.Should().Contain("VisualEvidenceCaptureOrchestrator.RunScenariosAsync(");
            source.Should().Contain("VisualEvidenceCaptureOrchestrator.FinalizeHostRun(");
            source.Should().NotContain("FreePVisualEvidenceCaptureOrchestration.WriteManifest(");
            source.Should().NotContain("Array.FindIndex(");
            source.Should().NotContain("private static readonly JsonSerializerOptions");
            source.Should().NotContain("private static string Sha256(");
        });

        var dialogCaptureSources = new[] { sources[0], sources[2] };
        dialogCaptureSources.Should().AllSatisfy(source =>
        {
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.SemanticActionId(");
            source.Should().NotContain("private static string NormalizeLabel(");
            source.Should().NotContain("private static string SemanticActionId(");
            source.Should().NotContain("private static string ToSafeFileName(");
            source.Should().NotContain("DialogPaneVisualEvidenceRouteHost");
        });
    }

    [Fact]
    public void Wpf_capture_hosts_suppress_interactive_startup_recovery()
    {
        var sources = new[]
        {
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "TestSupport", "VisualEvidence.Wpf", "WpfDialogPaneVisualEvidenceCapture.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "TestSupport", "VisualEvidence.Wpf", "WpfWholeWindowVisualEvidenceCapture.cs"),
        };

        sources.Should().AllSatisfy(source =>
            source.Should().Contain(
                "new MainWindow(new FreePOptions(), suppressStartupRecoveryOffer: true)"));
    }

    [Fact]
    public void Wpf_dialog_capture_forces_normal_window_state_before_showing_the_owner()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "freep", "TestSupport", "VisualEvidence.Wpf", "WpfDialogPaneVisualEvidenceCapture.cs");

        var normalState = source.IndexOf("WindowState = WindowState.Normal", StringComparison.Ordinal);
        var showOwner = source.IndexOf("owner.Show();", StringComparison.Ordinal);

        normalState.Should().BeGreaterThanOrEqualTo(0);
        showOwner.Should().BeGreaterThan(normalState);
    }

    [Fact]
    public void Wpf_whole_window_capture_normalizes_to_the_requested_responsive_width()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "freep", "TestSupport", "VisualEvidence.Wpf", "WpfWholeWindowVisualEvidenceCapture.cs");

        source.Should().Contain("NormalizeContentSize(owner, hostPolicy.LogicalWidth);")
            .And.Contain("private static void NormalizeContentSize(Window owner, double logicalWidth)")
            .And.Contain("owner.Width += logicalWidth - content.ActualWidth;")
            .And.NotContain("owner.Width += WholeWindowVisualEvidenceCatalog.LogicalClientWidth - content.ActualWidth;");
    }

    [Fact]
    public void RenderCompare_uses_one_paired_collector_for_routes_manifests_processes_and_artifacts()
    {
        var collector = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "FreeP.RenderCompare", "PairedVisualEvidenceCollector.cs");
        var routeSources = new[]
        {
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "tools", "FreeP.RenderCompare", "DialogPaneVisualEvidence.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "tools", "FreeP.RenderCompare", "WholeWindowVisualEvidence.cs"),
        };

        collector.Should().Contain("new VisualEvidenceRunDirectory(")
            .And.Contain("using Free.ToolsShared;")
            .And.Contain("FreePVisualEvidenceCaptureOrchestration.CreateScenarioProcessPlan(")
            .And.Contain("FreePVisualEvidenceCaptureOrchestration.ReadScenarioManifest<")
            .And.Contain("FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(")
            .And.Contain("TryCopyArtifacts(")
            .And.NotContain("Guid.NewGuid()")
            .And.NotContain("JsonSerializer.Deserialize<");
        routeSources.Should().AllSatisfy(source =>
        {
            source.Should().Contain("PairedVisualEvidenceCollector.Collect(");
            source.Should().NotContain("private const string HostOutputArgument");
            source.Should().NotContain("new VisualEvidenceRunDirectory(");
            source.Should().NotContain("FreePVisualEvidenceCaptureOrchestration.CreateScenarioProcessPlan(");
            source.Should().NotContain("FreePVisualEvidenceCaptureOrchestration.ReadScenarioManifest<");
            source.Should().NotContain("Guid.NewGuid()");
            source.Should().NotContain("JsonSerializer.Deserialize<");
        });
    }

    [Fact]
    public void Generic_capture_lifecycle_is_owned_by_tools_shared_and_reused_by_FreeP_and_FreeX()
    {
        var shared = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Free.ToolsShared", "VisualEvidenceCaptureOrchestrator.cs");
        var freeP = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "freep", "TestSupport", "VisualEvidence", "VisualEvidenceCaptureOrchestration.cs");
        var freeX = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "FreeX.ParityCompare", "CaptureRunner.cs");

        shared.Should().Contain("public sealed class VisualEvidenceRunDirectory")
            .And.Contain("public sealed record VisualEvidenceHostOutputPlan(")
            .And.Contain("public sealed record VisualEvidenceProcessPlan(")
            .And.Contain("public static async Task<VisualEvidenceScenarioRun")
            .And.Contain("public static int FinalizeHostRun")
            .And.Contain("VisualEvidenceManifestIO.Write(")
            .And.NotContain("FreeP")
            .And.NotContain("FreeX");
        freeP.Should().Contain("using Free.ToolsShared;")
            .And.Contain("VisualEvidenceProcessPlan.Create(")
            .And.NotContain("sealed class VisualEvidenceRunDirectory")
            .And.NotContain("sealed record VisualEvidenceHostOutputPlan")
            .And.NotContain("sealed record VisualEvidenceProcessPlan")
            .And.NotContain("RunScenariosAsync<TScenario")
            .And.NotContain("FinalizeHostRun<TScenario");
        freeX.Should().Contain("VisualEvidenceProcessPlan.Create(")
            .And.Contain("private static void Run(VisualEvidenceProcessPlan plan)");
    }
}
