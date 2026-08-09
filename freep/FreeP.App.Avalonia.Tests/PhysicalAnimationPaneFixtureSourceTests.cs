using FluentAssertions;

namespace FreeP.App.Avalonia.Tests;

public sealed class PhysicalAnimationPaneFixtureSourceTests
{
    [Fact]
    public void PhysicalAnimationPaneFixture_IsExplicitlyOptInAndSeedsOneRealAnimation()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("FREEP_PHYSICAL_ANIMATION_PANE_SEED");
        source.Should().Contain("SeedPhysicalAnimationPaneFixtureIfRequested();");
        source.Should().Contain("Editor.InsertTextBox(\"Animation Pane sample\")");
        source.Should().Contain("Editor.CurrentSlide.Animations.Add(new ShapeAnimation");
        source.Should().Contain("ShapeId = shape.Id");
        source.Should().Contain("Preset = AnimationPreset.Fade");

        var runner = File.ReadAllText(RepoFile("tools", "Run-FamilyLinuxInteractionValidation.ps1"));
        runner.Should().Contain("-AppEnvironment");
        runner.Should().Contain("FREEP_PHYSICAL_ANIMATION_PANE_SEED=1");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeP.slnx", parts);
}
