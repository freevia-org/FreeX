namespace FreeP.RenderCompare.Tests;

public sealed class VisualEvidenceOrchestrationSourceTests
{
    [Fact]
    public void Wpf_and_Avalonia_capture_adapters_delegate_ui_free_orchestration()
    {
        var sources = new[]
        {
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "FreeP.App.Host", "WpfDialogPaneVisualEvidenceCapture.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "FreeP.App.Host", "WpfWholeWindowVisualEvidenceCapture.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "FreeP.App.Avalonia", "AvaloniaDialogPaneVisualEvidenceCapture.cs"),
            TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "freep", "FreeP.App.Avalonia", "AvaloniaWholeWindowVisualEvidenceCapture.cs"),
        };

        sources.Should().AllSatisfy(source =>
        {
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.ParseRequest(");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.WriteManifest(");
            source.Should().NotContain("Array.FindIndex(");
            source.Should().NotContain("private static readonly JsonSerializerOptions");
            source.Should().NotContain("private static string Sha256(");
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
            source.Should().Contain("new TestTemporaryDirectory(");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.CreateScenarioProcessPlan(");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.ReadScenarioManifest<");
            source.Should().Contain("FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(");
            source.Should().NotContain("private const string HostOutputArgument");
            source.Should().NotContain("Guid.NewGuid()");
            source.Should().NotContain("JsonSerializer.Deserialize<");
        });
    }
}
