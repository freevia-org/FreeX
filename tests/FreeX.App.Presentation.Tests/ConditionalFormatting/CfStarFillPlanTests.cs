using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class CfStarFillPlanTests
{
    [Fact]
    public void PlanStarFill_CentralizesBoundsClampingAndClipWidth()
    {
        var op = CfGlyphOp.StarFillFraction(
        [
            new LayoutPoint(4, 2),
            new LayoutPoint(12, 6),
            new LayoutPoint(8, 14),
            new LayoutPoint(2, 10),
        ], 0.5);

        var plan = ConditionalIconGlyphGeometry.PlanStarFill(op);

        plan.Points.Should().BeSameAs(op.Points);
        plan.ClipRect.Should().Be(new LayoutRect(2, 2, 5, 12));
        plan.ShouldFill.Should().BeTrue();
        plan.RequiresClip.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, false, true, 0)]
    [InlineData(0, false, true, 0)]
    [InlineData(1, true, false, 10)]
    [InlineData(2, true, false, 10)]
    public void PlanStarFill_ClampsRendererInput(
        double fraction,
        bool shouldFill,
        bool requiresClip,
        double expectedWidth)
    {
        var op = CfGlyphOp.StarFillFraction(
            [new LayoutPoint(0, 0), new LayoutPoint(10, 0), new LayoutPoint(5, 10)],
            fraction);

        var plan = ConditionalIconGlyphGeometry.PlanStarFill(op);

        plan.ShouldFill.Should().Be(shouldFill);
        plan.RequiresClip.Should().Be(requiresClip);
        plan.ClipRect.Width.Should().Be(expectedWidth);
    }
}
