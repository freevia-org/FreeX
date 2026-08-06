using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R90-meta-2: r89 taught <see cref="FreeX.App.Presentation.Sparklines.SparklineLayoutEngine"/> to
/// honor <see cref="SparklineModel.RightToLeft"/>, but the WPF host's own drawing path --
/// <see cref="GridView.DrawSparklineIntoCell"/>, the exact cross-assembly method both the interactive
/// grid overlay (GridView.Overlays.Sparklines.cs) and the print/PDF/XPS renderer call into -- still
/// called the OLD non-rightToLeft overloads, so the option was silently dropped in this shell. These
/// tests drive the real product entry point (<see cref="GridView.DrawSparklineIntoCell"/>, public
/// specifically for this cross-assembly reuse) with a real WPF <see cref="DrawingContext"/> and
/// inspect the recorded <see cref="Drawing"/> tree, instead of re-implementing/bypassing the planner.
/// </summary>
public sealed class R90_SparklineRightToLeftRenderTests
{
    private static IReadOnlyList<Rect> DrawColumnSparklineAndCollectBarRects(bool rightToLeft)
    {
        var sparkline = new SparklineModel
        {
            Kind = SparklineKind.Column,
            RightToLeft = rightToLeft,
        };
        var values = new List<double> { 2, -4 };
        var rect = new Rect(0, 0, 100, 40);
        var axisScalePlan = SparklineAxisScalePlanner.Build(
            [sparkline],
            new Dictionary<Guid, IReadOnlyList<double>> { [sparkline.Id] = values });

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            GridView.DrawSparklineIntoCell(
                dc,
                sparkline,
                values,
                rect,
                axisScalePlan);
        }

        return CollectRects(visual.Drawing);
    }

    private static List<Rect> CollectRects(Drawing? drawing)
    {
        var result = new List<Rect>();
        Walk(drawing, result);
        return result;
    }

    private static void Walk(Drawing? drawing, List<Rect> result)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                foreach (var child in group.Children)
                    Walk(child, result);
                break;
            case GeometryDrawing { Geometry: not null } geometryDrawing:
                // A drawn bar's bounds are its rectangle; the axis line (unused here, ShowAxis
                // defaults to false) would otherwise show up as a zero-area bounds entry.
                var bounds = geometryDrawing.Geometry.Bounds;
                if (bounds.Width > 0 && bounds.Height > 0)
                    result.Add(bounds);
                break;
        }
    }

    [Fact]
    public void DrawSparklineIntoCell_ColumnRightToLeftFalse_FirstValueBarSitsInLeftmostSlot()
    {
        // [2, -4]: rect width 100 / 2 values -> slot 50, bar width 32.5 (matches
        // SparklineLayoutPlannerTests' known geometry for the same values/rect). With
        // RightToLeft off, the first data value (2) draws into slot 0 (left).
        var rects = DrawColumnSparklineAndCollectBarRects(rightToLeft: false);

        rects.Should().HaveCount(2);
        rects[0].Left.Should().BeApproximately(8.75, 0.01,
            "with Plot Data Right-to-Left OFF the first value's bar sits in the leftmost slot");
        rects[1].Left.Should().BeApproximately(58.75, 0.01);
    }

    [Fact]
    public void DrawSparklineIntoCell_ColumnRightToLeftTrue_FirstValueBarSitsInRightmostSlot()
    {
        // Failure scenario from the finding: before the fix, GridView.DrawSparklineIntoCell (the
        // exact method the print path and the interactive grid both call) ignored
        // SparklineModel.RightToLeft entirely, so this would render identically to the false case
        // above (first value's bar in the LEFT slot) instead of mirroring like the Avalonia shell.
        var rects = DrawColumnSparklineAndCollectBarRects(rightToLeft: true);

        rects.Should().HaveCount(2);
        rects[0].Left.Should().BeApproximately(58.75, 0.01,
            "Plot Data Right-to-Left mirrors bar slots so the first data value lands in the rightmost slot, matching Excel");
        rects[1].Left.Should().BeApproximately(8.75, 0.01);
    }
}
