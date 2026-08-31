using System.Reflection;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R90-commands-shape-geometry-5-1: shape/text-box text was drawn while the flip's
/// <see cref="ScaleTransform"/> (pushed by GridView's private <c>PushDrawingObjectTransform</c>,
/// GridView.DrawingObjects.cs) was still active on the <see cref="DrawingContext"/>, so
/// flipH/flipV mirrored the text glyphs along with the shape geometry -- Excel mirrors a flipped
/// shape's outline but always keeps its text upright and readable. The fix splits the pop into a
/// flip-only pop (<c>PopDrawingObjectFlipTransform</c>, called right before drawing text) and the
/// existing full pop (<c>PopDrawingObjectTransform</c>), so rotation still applies to text (matching
/// Excel) while the flip no longer does. These tests invoke those exact private static helpers (the
/// real transform-stack mechanism the shape/text-box renderers call) against a real WPF
/// <see cref="DrawingContext"/> and inspect the recorded geometry, rather than re-implementing the
/// transform math.
/// </summary>
public sealed class R90_DrawingObjectFlipTextTests
{
    // dc.PushTransform wraps subsequent drawing commands in a DrawingGroup whose own .Transform
    // property carries the pushed transform -- child Geometry.Bounds stay in LOCAL (untransformed)
    // coordinates, so the cumulative transform down each branch must be applied by hand to get the
    // effective on-screen bounds actually produced by the flip/rotation being tested here.
    private static List<Rect> CollectRects(Drawing? drawing)
    {
        var result = new List<Rect>();
        Walk(drawing, Matrix.Identity, result);
        return result;
    }

    private static void Walk(Drawing? drawing, Matrix cumulative, List<Rect> result)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var next = cumulative;
                if (group.Transform is { } transform)
                    next = transform.Value * cumulative;
                foreach (var child in group.Children)
                    Walk(child, next, result);
                break;
            case GeometryDrawing { Geometry: not null } geometryDrawing:
                result.Add(Rect.Transform(geometryDrawing.Geometry.Bounds, cumulative));
                break;
        }
    }

    [Fact]
    public void PopDrawingObjectFlipTransform_UnmirrorsSubsequentDrawingWhileFlipped()
    {
        // Failure scenario from the finding: a horizontally-flipped shape's geometry should mirror
        // (marker drawn under the still-active flip lands on the OPPOSITE side of the rect's center),
        // but its text must not -- a marker drawn after popping just the flip must land back at its
        // true, unmirrored local position.
        var rect = new Rect(0, 0, 100, 40); // center = (50, 20)
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var state = GridView.PushDrawingObjectTransform(dc, 0.0, flipHorizontal: true, flipVertical: false, rect);

            // Drawn while the flip ScaleTransform(-1,1,50,20) is still active: local x in [5,15]
            // mirrors to screen x in [85,95] (100 - x).
            dc.DrawRectangle(Brushes.Black, null, new Rect(5, 5, 10, 10));

            GridView.PopDrawingObjectFlipTransform(dc, ref state);

            // Drawn at the identical local rect after the flip is popped: must land at its true,
            // unmirrored screen position (x in [5,15]) -- this is where DrawShapeText now runs.
            dc.DrawRectangle(Brushes.Black, null, new Rect(5, 5, 10, 10));

            GridView.PopDrawingObjectTransform(dc, state);
        }

        var rects = CollectRects(visual.Drawing);
        rects.Should().HaveCount(2);
        rects[0].Left.Should().BeApproximately(85, 0.5,
            "geometry drawn while the flip is still active must mirror around the rect's center");
        rects[1].Left.Should().BeApproximately(5, 0.5,
            "geometry (standing in for shape text) drawn after PopDrawingObjectFlipTransform must be unmirrored");
    }

    [Fact]
    public void PopDrawingObjectFlipTransform_NoOpWhenNothingWasFlipped()
    {
        // No-regression sibling: the overwhelming majority of shapes have no flip at all, so
        // PopDrawingObjectFlipTransform must be a true no-op then -- no extra dc.Pop() call that
        // would unbalance the transform stack for the common unflipped/unrotated case.
        var rect = new Rect(0, 0, 100, 40);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var state = GridView.PushDrawingObjectTransform(dc, 0.0, flipHorizontal: false, flipVertical: false, rect);

            dc.DrawRectangle(Brushes.Black, null, new Rect(5, 5, 10, 10));

            GridView.PopDrawingObjectFlipTransform(dc, ref state);

            dc.DrawRectangle(Brushes.Black, null, new Rect(5, 5, 10, 10));

            GridView.PopDrawingObjectTransform(dc, state);
        }

        var rects = CollectRects(visual.Drawing);
        rects.Should().HaveCount(2);
        rects[0].Left.Should().BeApproximately(5, 0.5, "no flip/rotation was pushed, so nothing should move");
        rects[1].Left.Should().BeApproximately(5, 0.5, "popping a no-op flip must not shift or unbalance the transform stack");
    }
}
