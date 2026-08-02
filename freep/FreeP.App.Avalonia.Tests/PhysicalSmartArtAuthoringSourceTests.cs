using FluentAssertions;

namespace FreeP.App.Avalonia.Tests;

public sealed class PhysicalSmartArtAuthoringSourceTests
{
    [Fact]
    public void PhysicalSmartArtAuthoringLane_IsExplicitlyOptInAndUsesNativeProbeContract()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("FREEP_PHYSICAL_SMARTART_TEXT_PANE_SEED");
        source.Should().Contain("SeedPhysicalSmartArtTextPaneIfRequested();");
        source.Should().Contain("Editor.CurrentSlide.Shapes.FirstOrDefault(shape => shape.SmartArt is not null)");
        source.Should().Contain("ShowSmartArtTextPane();");

        var runner = File.ReadAllText(RepoFile("tools", "Run-FreePSmartArtAuthoringValidation.ps1"));
        runner.Should().Contain("FREEP_PHYSICAL_SMARTART_TEXT_PANE_SEED=1");
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
        probe.Should().Contain("apply-undo-row-clipboard.txt");
        probe.Should().Contain("apply-redo-row-clipboard.txt");
        probe.Should().Contain("xclip -selection clipboard -out");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull();
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
