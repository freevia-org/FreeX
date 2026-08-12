using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LinuxReleaseWorkflowTests
{
    private static string Workflow() =>
        File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "linux-release.yml"));

    [Fact]
    public void ReleaseWorkflow_IsManualOnlyAndGated()
    {
        var workflow = Workflow();

        workflow.Should().Contain("name: Linux Release");
        workflow.Should().Contain("workflow_dispatch:");
        // Manual-only: never auto-run on push/PR.
        workflow.Should().NotContain("pull_request:");
        workflow.Should().NotContain("\n  push:");

        // Inputs: version + public-preview + accessibility evidence.
        workflow.Should().Contain("release_version:");
        workflow.Should().Contain("public_preview_candidate:");
        workflow.Should().Contain("accessibility_keyboard_only:");
        workflow.Should().Contain("accessibility_screen_reader:");
        workflow.Should().Contain("accessibility_x11:");
        workflow.Should().Contain("accessibility_wayland:");
        workflow.Should().Contain("accessibility_known_issues:");

        // Publishing requires write permission and produces a release.
        workflow.Should().Contain("contents: write");
        workflow.Should().Contain("gh release");
        // Always staged as a draft; publishing stays manual.
        workflow.Should().Contain("--draft");
    }

    [Fact]
    public void ReleaseWorkflow_BuildsBothRuntimesWithHardGatesAndPromotion()
    {
        var workflow = Workflow();

        workflow.Should().Contain("runtime: linux-x64");
        workflow.Should().Contain("runtime: linux-arm64");
        workflow.Should().Contain("runner: ubuntu-latest");
        workflow.Should().Contain("runner: ubuntu-24.04-arm");

        workflow.Should().Contain("dotnet publish src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj");
        workflow.Should().Contain("--packaging-smoke");
        workflow.Should().Contain("\"$validation_published/FreeX.Validation.Avalonia\" --packaging-smoke");
        workflow.Should().Contain("xvfb-run -a");
        workflow.Should().Contain("bash tools/Run-PackagedProductLaunchProbe.sh");
        workflow.Should().Contain("--executable \"$published/FreeX\"");
        workflow.Should().Contain("packaged_product_launch_status=passed");
        workflow.Should().Contain("package-linux-app.sh");
        workflow.Should().Contain("sha256sum -c");
        workflow.Should().Contain("Test-LinuxPublicPreviewPromotion.ps1");
    }

    [Fact]
    public void ReleaseWorkflow_DoesNotLeakMacOsSigningMachinery()
    {
        var workflow = Workflow();
        foreach (var forbidden in new[] { "codesign", "notarytool", "MACOS_CODESIGN", "lsregister", "spctl", "Developer ID" })
            workflow.Should().NotContain(forbidden);
    }

    [Fact]
    public void PromotionTool_GatesPublicPreviewOnAccessibilityEvidence()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find("tools", "Test-LinuxPublicPreviewPromotion.ps1"));

        script.Should().Contain("linux-promotion.v1");
        script.Should().Contain("PublicPreviewCandidate");
        script.Should().Contain("AccessibilityKeyboardOnly");
        script.Should().Contain("AccessibilityScreenReader");
        script.Should().Contain("AccessibilityX11");
        script.Should().Contain("AccessibilityWayland");
        script.Should().Contain("AccessibilityKnownIssuesReviewed");
        // Promotion runs the artifact readiness validator first.
        script.Should().Contain("Test-LinuxPublicPreviewReadiness.ps1");
        script.Should().Contain("public_preview_ready");
    }
}
