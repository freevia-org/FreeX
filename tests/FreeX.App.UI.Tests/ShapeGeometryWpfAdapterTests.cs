using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Free.Shared.Drawing.Wpf;
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

    [Fact]
    public void Create_OvalCallout_FillsTheBodyTailOverlap()
    {
        // (47, 76) lies inside both the body ellipse and the tail triangle of the 100x100 callout.
        var geometry = ShapeGeometryWpfAdapter.Create(DrawingShapeKind.OvalCallout, new Rect(0, 0, 100, 100));

        geometry.FillContains(new Point(47, 76)).Should().BeTrue();
        geometry.FillContains(new Point(50, 41)).Should().BeTrue();
        geometry.FillContains(new Point(5, 95)).Should().BeFalse();
    }

    [Fact]
    public void SharedAdapter_OverlappingSameDirectionContours_NonzeroUnionsEvenOddCutsOut()
    {
        var shape = new ShapeGeometry([Square(0, 0, 60, clockwise: true), Square(30, 30, 60, clockwise: true)]);
        var overlap = new Point(45, 45);

        WpfShapeGeometryAdapter.ToGeometry(shape, FillRule.Nonzero).FillContains(overlap).Should().BeTrue();
        WpfShapeGeometryAdapter.ToGeometry(shape, FillRule.EvenOdd).FillContains(overlap).Should().BeFalse();
        WpfShapeGeometryAdapter.ToGeometry(shape).FillRule.Should().Be(FillRule.EvenOdd);
    }

    [Fact]
    public void SharedAdapter_OppositeDirectionHole_StaysAHoleUnderBothRules()
    {
        // How the ECMA-376 donut/frame presets wind their holes.
        var shape = new ShapeGeometry([Square(0, 0, 90, clockwise: true), Square(30, 30, 30, clockwise: false)]);

        foreach (var rule in new[] { FillRule.Nonzero, FillRule.EvenOdd })
        {
            var geometry = WpfShapeGeometryAdapter.ToGeometry(shape, rule);
            geometry.FillContains(new Point(45, 45)).Should().BeFalse(rule.ToString());
            geometry.FillContains(new Point(10, 10)).Should().BeTrue(rule.ToString());
        }
    }

    [Fact]
    public void SharedAdapter_SameDirectionHole_IsFilledByNonzeroSoFreePKeepsEvenOdd()
    {
        // Imported custGeom subpaths can draw a hole in the same direction as the outer contour.
        var shape = new ShapeGeometry([Square(0, 0, 90, clockwise: true), Square(30, 30, 30, clockwise: true)]);
        var hole = new Point(45, 45);

        WpfShapeGeometryAdapter.ToGeometry(shape, FillRule.EvenOdd).FillContains(hole).Should().BeFalse();
        WpfShapeGeometryAdapter.ToGeometry(shape, FillRule.Nonzero).FillContains(hole).Should().BeTrue();
    }

    [Fact]
    public void SharedAdapter_ReturnsFrozenEmptyGeometryForEmptyShape()
    {
        var geometry = WpfShapeGeometryAdapter.ToGeometry(ShapeGeometry.Empty);

        geometry.IsFrozen.Should().BeTrue();
        geometry.Bounds.IsEmpty.Should().BeTrue();
    }

    private static ShapeContour Square(double x, double y, double size, bool clockwise)
    {
        var topRight = new LayoutPoint(x + size, y);
        var bottomRight = new LayoutPoint(x + size, y + size);
        var bottomLeft = new LayoutPoint(x, y + size);
        // Screen coordinates (y down): top-left -> right -> down -> left is clockwise.
        LayoutPoint[] path = clockwise
            ? [topRight, bottomRight, bottomLeft]
            : [bottomLeft, bottomRight, topRight];
        return new ShapeContour(
            new LayoutPoint(x, y),
            path.Select(ShapeSegment.LineTo).ToArray(),
            Closed: true,
            Filled: true);
    }
}
