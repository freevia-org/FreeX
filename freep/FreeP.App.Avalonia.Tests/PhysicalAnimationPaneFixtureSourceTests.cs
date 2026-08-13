using FluentAssertions;

namespace FreeP.App.Avalonia.Tests;

public sealed class PhysicalAnimationPaneFixtureSourceTests
{
    [Fact]
    public void PhysicalAnimationPaneFixture_IsOwnedByExternalValidationHost()
    {
        var renderer = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var tool = File.ReadAllText(RepoFile(
            "freep", "TestSupport", "Validation.Avalonia", "PhysicalFixtureValidation.cs"));
        var adapter = File.ReadAllText(RepoFile(
            "freep", "TestSupport", "Validation.Avalonia", "MainWindow.ValidationAccessAdapter.cs"));

        renderer.Should().NotContain("FREEP_PHYSICAL_ANIMATION_PANE_SEED");
        renderer.Should().NotContain("Animation Pane sample");
        renderer.Should().Contain("CoordinateAnimationPaneRequestObserver();");
        adapter.Should().Contain("partial void CoordinateAnimationPaneRequestObserver()");
        tool.Should().Contain("--physical-animation-pane-fixture");
        tool.Should().Contain("access.Editor.InsertTextBox(\"Animation Pane sample\")");
        tool.Should().Contain("slide.Animations.Add(new ShapeAnimation");
        tool.Should().Contain("ShapeId = shape.Id");
        tool.Should().Contain("Preset = AnimationPreset.Fade");

        var runner = File.ReadAllText(RepoFile("tools", "Run-FamilyLinuxInteractionValidation.ps1"));
        runner.Should().Contain("\"-Host\", \"Validation\"");
        runner.Should().Contain("--physical-animation-pane-fixture");
        runner.Should().NotContain("FREEP_PHYSICAL_ANIMATION_PANE_SEED");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeP.slnx", parts);
}
