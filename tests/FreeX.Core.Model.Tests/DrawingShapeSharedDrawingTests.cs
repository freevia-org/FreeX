using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DrawingShapeSharedDrawingTests
{
    [Fact]
    public void DrawingShapeModel_UsesSharedDrawingShapeKind()
    {
        var kindProperty = typeof(DrawingShapeModel).GetProperty(nameof(DrawingShapeModel.Kind));

        kindProperty.Should().NotBeNull();
        kindProperty!.PropertyType.Should().Be(typeof(DrawingShapeKind));
        kindProperty.PropertyType.Assembly.FullName.Should().Be(typeof(DrawingShapeKindSupport).Assembly.FullName);
        kindProperty.PropertyType.Namespace.Should().Be("Free.Shared.Drawing");
    }

    [Fact]
    public void CoreModelShapeKindSources_RemainNeutralized()
    {
        var sharedRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("shared", "Free.Shared.Drawing");
        var coreModelRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Model");

        File.Exists(Path.Combine(sharedRoot, "DrawingShapeKind.cs"))
            .Should()
            .BeTrue("DrawingShapeKind should remain owned by Free.Shared.Drawing");
        File.Exists(Path.Combine(sharedRoot, "DrawingShapeKindSupport.cs"))
            .Should()
            .BeTrue("DrawingShapeKindSupport should remain owned by Free.Shared.Drawing");
        File.Exists(Path.Combine(coreModelRoot, "DrawingShapeKindSupport.cs"))
            .Should()
            .BeFalse("Core.Model should consume the shared shape-kind support instead of keeping a facade copy");

        var coreModelSources = Directory
            .EnumerateFiles(coreModelRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        coreModelSources.Should().NotContain(source => source.Contains("public enum DrawingShapeKind", StringComparison.Ordinal));
        coreModelSources.Should().NotContain(source => source.Contains("public static class DrawingShapeKindSupport", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedShapeKindSupport_PreservesRenderableCatalog()
    {
        DrawingShapeKindSupport.IsRenderable(DrawingShapeKind.Rectangle).Should().BeTrue();
        DrawingShapeKindSupport.IsRenderable(DrawingShapeKind.Cylinder).Should().BeTrue();
        DrawingShapeKindSupport.IsLineLike(DrawingShapeKind.Line).Should().BeTrue();
        DrawingShapeKindSupport.IsLineLike(DrawingShapeKind.Cylinder).Should().BeFalse();
        ((int)DrawingShapeKind.Cylinder).Should().Be(44);
    }
}
