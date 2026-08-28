using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r164 remediation, unbounded declared quantity. An arc's reference angle is its
/// <c>StAng + SwAng</c>, read from custom geometry in the .pptx as a plain double with no range
/// check, and <c>NearestEquivalentAngle</c> used to walk to it 360 degrees at a time. Past roughly
/// 1e19 a double cannot change by 360 at all, so that loop never ends -- measured: dragging an arc's
/// end-angle handle on a shape whose SwAng is 1e18 or 1e308 never returned, on the UI thread, while
/// an ordinary 90-degree arc completed instantly.
///
/// Same shape, same fix as Free.Shared.Drawing's chord sweep: reduce modulo a full turn, and treat a
/// non-finite guide as nothing to normalize.
/// </summary>
public sealed class R164_ArcAngleNormalizationTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static SlideShape ArcShape(double sweepAngle)
    {
        var shape = new SlideShape
        {
            Id = 1,
            Name = "Custom",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
        };

        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 40, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.ArcTo, WR: 40, HR: 30, StAng: 0, SwAng: sweepAngle));
        shape.CustomGeometry.Add(path);
        return shape;
    }

    private static ShapeGeometryAdjustmentMutationPlan DragEndAngle(SlideShape shape)
    {
        ShapeGeometryAdjustmentMutationPlan? plan = null;
        var thread = new Thread(() => plan = ShapeGeometryAdjustmentPlanner.BuildMutationPlan(
            shape,
            new LayoutRect(0, 0, 100, 100),
            "arc:0:1:end",
            new LayoutPoint(80, 50)))
        {
            IsBackground = true,
        };

        thread.Start();
        // A Thread, not a Task: before the fix this loop never completed, and a spinning Task cannot
        // be detected by Task.Wait -- it just keeps burning a core after the timeout returns.
        thread.Join(Budget).Should().BeTrue("normalizing an arc angle must not spin on the UI thread");
        return plan!;
    }

    [Theory]
    [InlineData(1e18)]
    [InlineData(1e308)]
    [InlineData(-1e308)]
    [InlineData(double.MaxValue)]
    public void BuildMutationPlan_ArcWithAbsurdSweepAngle_Returns(double sweepAngle)
    {
        DragEndAngle(ArcShape(sweepAngle)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void BuildMutationPlan_ArcWithNonFiniteSweepAngle_Returns(double sweepAngle)
    {
        DragEndAngle(ArcShape(sweepAngle)).Should().NotBeNull();
    }

    [Fact]
    public void BuildMutationPlan_AnOrdinaryArc_StillPlansTheSameEndAngle()
    {
        // Sibling/no-regression: modular reduction returns exactly what the loop returned for every
        // angle a real document carries -- the drag still produces a mutation for the arc's segment.
        var plan = DragEndAngle(ArcShape(90));

        plan.ShouldApply.Should().BeTrue(plan.DisabledReason);
        plan.Name.Should().Be("arc:0:1:end");
        plan.ArcPoint.Should().NotBeNull();
        plan.ArcPoint!.Slot.Should().Be(CustomGeometryArcPointSlot.EndAngle);
        double.IsFinite(plan.ArcPoint.Value).Should().BeTrue();
    }
}
