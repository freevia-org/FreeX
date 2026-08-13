using FluentAssertions;

namespace FreeP.App.Avalonia.Tests;

public sealed class PhysicalSmartArtAuthoringSourceTests
{
    [Fact]
    public void PhysicalSmartArtAuthoringLane_IsExplicitlyOptInAndUsesNativeProbeContract()
    {
        var renderer = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var tool = File.ReadAllText(RepoFile(
            "freep", "TestSupport", "Validation.Avalonia", "PhysicalFixtureValidation.cs"));

        renderer.Should().NotContain("FREEP_PHYSICAL_SMARTART_TEXT_PANE_SEED");
        renderer.Should().NotContain("SeedPhysicalSmartArtTextPaneIfRequested");
        tool.Should().Contain("--physical-smartart-text-pane-fixture");
        tool.Should().Contain(".FirstOrDefault(shape => shape.SmartArt is not null)");
        tool.Should().Contain("access.ShowSmartArtTextPane();");

        var runner = File.ReadAllText(RepoFile("tools", "Run-FreePSmartArtAuthoringValidation.ps1"));
        runner.Should().Contain("\"-Host\", \"Validation\"");
        runner.Should().Contain("--physical-smartart-text-pane-fixture");
        runner.Should().NotContain("FREEP_PHYSICAL_SMARTART_TEXT_PANE_SEED");
        runner.Should().Contain("two fresh harness-owned FreeP processes");
        runner.Should().NotContain("undo, redo");
        runner.Should().Contain("smartart-outline-apply-undo-redo");
        runner.Should().Contain("smartart-outline-apply-text|smartart-outline-apply-undo-redo|smartart-outline-reopen");
        runner.Should().Contain("Assert-ManifestContract");
        runner.Should().Contain("$($phaseManifest.Name)/$_");
        runner.Should().Contain("Assert-EvidenceReference");

        var probe = File.ReadAllText(RepoFile("tools", "LinuxInteractiveDocker", "run-freep-smartart-authoring-probe.sh"));
        probe.Should().Contain("ppt/diagrams/data1.xml");
        probe.Should().Contain("Plan|Design|New node|Build|Test|Deploy");
        probe.Should().Contain("send_key ctrl+z");
        probe.Should().Contain("send_key ctrl+y");
        probe.Should().Contain("apply_y=$((Y + HEIGHT - 167))");
        probe.Should().Contain("shell_focus_y=$((Y + 28))");
        probe.Should().Contain("apply-undo-row-clipboard.txt");
        probe.Should().Contain("apply-redo-row-clipboard.txt");
        probe.Should().Contain("xclip -selection clipboard -out");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeP.slnx", parts);
}
