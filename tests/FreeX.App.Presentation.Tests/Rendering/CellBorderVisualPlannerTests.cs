using FluentAssertions;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Rendering;

public sealed class CellBorderVisualPlannerTests
{
    [Theory]
    [InlineData(BorderStyle.Hair, 0.25)]
    [InlineData(BorderStyle.Thin, 0.5)]
    [InlineData(BorderStyle.Dashed, 0.5)]
    [InlineData(BorderStyle.Medium, 1.5)]
    [InlineData(BorderStyle.SlantDashDot, 1.5)]
    [InlineData(BorderStyle.Thick, 2.5)]
    public void Plan_ReturnsCanonicalThickness(BorderStyle style, double expected)
    {
        CellBorderVisualPlanner.Plan(style).Thickness.Should().Be(expected);
    }

    [Fact]
    public void Plan_ProvidesPortableDashClassificationAndNumericPattern()
    {
        var plan = CellBorderVisualPlanner.Plan(BorderStyle.MediumDashDotDot);

        plan.DashPattern.Should().Be(CellBorderDashPattern.DashDotDot);
        plan.DashArray.Should().Equal(2, 2, 1, 2, 1, 2);
    }

    [Fact]
    public void PlanDoubleEdge_PixelSnapsAxisAlignedLines()
    {
        var plan = CellBorderVisualPlanner.PlanDoubleEdge(0, 0, 20, 0, 0.5, 1);

        plan.HasSecond.Should().BeTrue();
        plan.First.Y1.Should().Be(-1.5);
        plan.Second.Y1.Should().Be(0.5);
    }

    [Fact]
    public void PlanDoubleEdge_OffsetsDiagonalLinesPerpendicularToEdge()
    {
        var plan = CellBorderVisualPlanner.PlanDoubleEdge(0, 0, 10, 10, 0.5, 1);

        plan.HasSecond.Should().BeTrue();
        plan.First.X1.Should().BeApproximately(-0.353553, 0.000001);
        plan.First.Y1.Should().BeApproximately(0.353553, 0.000001);
        plan.Second.X1.Should().BeApproximately(0.353553, 0.000001);
        plan.Second.Y1.Should().BeApproximately(-0.353553, 0.000001);
    }

    [Fact]
    public void ResolveEdgeWinner_IsSymmetricAndPrefersHeavierStyle()
    {
        var thin = new CellBorder(BorderStyle.Thin, new CellColor(1, 2, 3));
        var thick = new CellBorder(BorderStyle.Thick, new CellColor(4, 5, 6));

        CellBorderVisualPlanner.ResolveEdgeWinner(thin, thick).Should().Be(thick);
        CellBorderVisualPlanner.ResolveEdgeWinner(thick, thin).Should().Be(thick);
    }
}
