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

        portable.Should().Contain("public interface IWholeWindowVisualEvidenceProbe")
            .And.Contain("private void Activate(WholeWindowVisualEvidenceActivation activation)")
            .And.Contain("private bool PrepareViewState(")
            .And.Contain("new WholeWindowVisualEvidenceSemanticState(")
            .And.NotContain("using System.Windows")
            .And.NotContain("using Avalonia");
        hosts.Should().AllSatisfy(host =>
        {
            host.Should().Contain(": IWholeWindowVisualEvidenceProbe")
                .And.Contain("_coordinator = new(this);")
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
            .And.Contain("freep\\FreeP.App.Host\\MainWindow.VisualCaptureAdapter.cs")
            .And.Contain("freep\\TestSupport\\VisualEvidence.Avalonia\\AvaloniaWholeWindowVisualEvidenceCapture.cs")
            .And.Contain("freep\\TestSupport\\VisualEvidence.Avalonia\\AvaloniaWholeWindowVisualEvidenceCoordinator.cs")
            .And.Contain("freep\\FreeP.App.Avalonia\\MainWindow.VisualCaptureAdapter.cs")
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
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(");
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
        });
    }

    [Fact]
    public void RenderCompare_uses_shared_routes_manifests_process_plans_and_temp_lifecycle()
    {
        var sources = new[]
        {
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "tools", "FreeP.RenderCompare", "DialogPaneVisualEvidence.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "tools", "FreeP.RenderCompare", "WholeWindowVisualEvidence.cs"),
        };

        sources.Should().AllSatisfy(source =>
        {
            source.Should().Contain("new VisualEvidenceRunDirectory(");
            source.Should().Contain("using Free.ToolsShared;");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.CreateScenarioProcessPlan(");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.ReadScenarioManifest<");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(");
            source.Should().NotContain("private const string HostOutputArgument");
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
