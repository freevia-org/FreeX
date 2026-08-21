using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class ShapeGeometryWpfAdapterTests
{
    [Fact]
    public void Create_ReturnsNonEmptyFrozenGeometryForEveryRenderableShape()
    {
        var rect = new Rect(10, 20, 120, 80);

        foreach (var kind in Enum.GetValues<DrawingShapeKind>().Where(DrawingShapeKindSupport.IsRenderable))
        {
            var geometry = ShapeGeometryWpfAdapter.Create(kind, rect);

            geometry.IsFrozen.Should().BeTrue(kind.ToString());
            geometry.Bounds.IsEmpty.Should().BeFalse(kind.ToString());
            geometry.Bounds.Width.Should().BeGreaterThan(0, kind.ToString());
            geometry.Bounds.Height.Should().BeGreaterThan(0, kind.ToString());
        }
    }

    [Fact]
    public void SupportCatalog_IdentifiesConnectorShapesAsLineLike()
    {
        DrawingShapeKindSupport.IsLineLike(DrawingShapeKind.Line).Should().BeTrue();
        DrawingShapeKindSupport.IsLineLike(DrawingShapeKind.ElbowConnector).Should().BeTrue();
        DrawingShapeKindSupport.IsLineLike(DrawingShapeKind.CurvedConnector).Should().BeTrue();
        DrawingShapeKindSupport.IsLineLike(DrawingShapeKind.RightArrow).Should().BeFalse();
    }

    [Fact]
    public void Create_UsesNonzeroFillRuleForOverlappingCalloutContours()
    {
        var geometry = ShapeGeometryWpfAdapter.Create(
            DrawingShapeKind.RectangularCallout,
            new Rect(10, 20, 120, 80));

        geometry.Should().BeOfType<StreamGeometry>();
        ((StreamGeometry)geometry).FillRule.Should().Be(FillRule.Nonzero);
    }
}
