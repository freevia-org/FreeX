using System.IO;

public sealed class ShapeGlowAuthoringSourceTests
{
    [Fact]
    public void Avalonia_registers_shared_shape_glow_presets()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ShapeEffectAuthoringPlanner.GlowNoneCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.GlowSubtleCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.GlowStrongCommandId");
        source.Should().Contain("Editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowNone())");
        source.Should().Contain("Editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowSubtle())");
        source.Should().Contain("Editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowStrong())");
    }

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(parts)}");
    }
}
