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

    [Fact]
    public void Avalonia_registers_shared_shape_soft_edge_presets()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ShapeEffectAuthoringPlanner.SoftEdgeNoneCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.SoftEdgeSubtleCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.SoftEdgeStrongCommandId");
        source.Should().Contain("Editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeNone())");
        source.Should().Contain("Editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeSubtle())");
        source.Should().Contain("Editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeStrong())");
    }

    [Fact]
    public void Avalonia_registers_shared_shape_bevel_presets()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ShapeEffectAuthoringPlanner.BevelNoneCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.BevelSubtleCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.BevelStrongCommandId");
        source.Should().Contain("Editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelNone())");
        source.Should().Contain("Editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelSubtle())");
        source.Should().Contain("Editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelStrong())");
    }

    [Fact]
    public void Avalonia_registers_shared_shape_3d_presets()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ShapeEffectAuthoringPlanner.Shape3dNoneCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.Shape3dSubtleCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.Shape3dStrongCommandId");
        source.Should().Contain("Editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dNone())");
        source.Should().Contain("Editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dSubtle())");
        source.Should().Contain("Editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dStrong())");
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
