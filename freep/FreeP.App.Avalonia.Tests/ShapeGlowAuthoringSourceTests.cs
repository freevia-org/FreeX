using System.IO;

public sealed class ShapeGlowAuthoringSourceTests
{
    [Fact]
    public void Avalonia_registers_shared_shape_glow_presets()
    {
        var source = ReadWorkflow();

        source.Should().Contain("ShapeEffectAuthoringPlanner.GlowNoneCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.GlowSubtleCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.GlowStrongCommandId");
        source.Should().Contain("editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowNone())");
        source.Should().Contain("editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowSubtle())");
        source.Should().Contain("editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowStrong())");
    }

    [Fact]
    public void Avalonia_registers_shared_shape_soft_edge_presets()
    {
        var source = ReadWorkflow();

        source.Should().Contain("ShapeEffectAuthoringPlanner.SoftEdgeNoneCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.SoftEdgeSubtleCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.SoftEdgeStrongCommandId");
        source.Should().Contain("editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeNone())");
        source.Should().Contain("editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeSubtle())");
        source.Should().Contain("editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeStrong())");
    }

    [Fact]
    public void Avalonia_registers_shared_shape_bevel_presets()
    {
        var source = ReadWorkflow();

        source.Should().Contain("ShapeEffectAuthoringPlanner.BevelNoneCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.BevelSubtleCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.BevelStrongCommandId");
        source.Should().Contain("editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelNone())");
        source.Should().Contain("editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelSubtle())");
        source.Should().Contain("editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelStrong())");
    }

    [Fact]
    public void Avalonia_registers_shared_shape_3d_presets()
    {
        var source = ReadWorkflow();

        source.Should().Contain("ShapeEffectAuthoringPlanner.Shape3dNoneCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.Shape3dSubtleCommandId");
        source.Should().Contain("ShapeEffectAuthoringPlanner.Shape3dStrongCommandId");
        source.Should().Contain("editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dNone())");
        source.Should().Contain("editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dSubtle())");
        source.Should().Contain("editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dStrong())");
    }

    private static string ReadWorkflow() => File.ReadAllText(RepoFile(
        "freep", "FreeP.App.Presentation", "Ribbon", "FreePRibbonCommandWorkflow.cs"));

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
