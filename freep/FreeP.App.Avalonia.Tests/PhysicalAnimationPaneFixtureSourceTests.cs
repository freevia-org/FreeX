using FluentAssertions;

namespace FreeP.App.Avalonia.Tests;

public sealed class PhysicalAnimationPaneFixtureSourceTests
{
    [Fact]
    public void PhysicalAnimationPaneFixture_IsExplicitlyOptInAndSeedsOneRealAnimation()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("FREEP_PHYSICAL_ANIMATION_PANE_SEED");
        source.Should().Contain("Editor.InsertTextBox(\"Animation Pane sample\")");
        source.Should().Contain("Editor.AddAnimation(shape.Id, new ShapeAnimation");
        source.Should().Contain("Preset = AnimationPreset.Fade");
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
