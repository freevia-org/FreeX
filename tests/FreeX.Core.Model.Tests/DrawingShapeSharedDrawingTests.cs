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
