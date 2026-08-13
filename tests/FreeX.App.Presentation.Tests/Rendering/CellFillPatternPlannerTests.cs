using FluentAssertions;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Rendering;

public sealed class CellFillPatternPlannerTests
{
    [Theory]
    [InlineData(CellFillPatternStyle.Gray0625, 0.12)]
    [InlineData(CellFillPatternStyle.Gray125, 0.18)]
    [InlineData(CellFillPatternStyle.LightGray, 0.28)]
    [InlineData(CellFillPatternStyle.MediumGray, 0.45)]
    [InlineData(CellFillPatternStyle.DarkGray, 0.62)]
    public void Plan_MapsGrayPatternOpacity(CellFillPatternStyle style, double opacity)
    {
        var plan = CellFillPatternPlanner.Plan(style);

        plan.Kind.Should().Be(CellFillPatternPlanKind.Opacity);
        plan.Opacity.Should().Be(opacity);
        plan.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CellFillPatternStyle.LightGrid, 6.0, CellFillPatternLinePrimitive.Horizontal, CellFillPatternLinePrimitive.Vertical)]
    [InlineData(CellFillPatternStyle.DarkTrellis, 8.0, CellFillPatternLinePrimitive.DescendingDiagonal, CellFillPatternLinePrimitive.AscendingDiagonal)]
    public void Plan_MapsHatchIntoOrderedLinePrimitives(
        CellFillPatternStyle style,
        double tileSize,
        CellFillPatternLinePrimitive first,
        CellFillPatternLinePrimitive second)
    {
        var plan = CellFillPatternPlanner.Plan(style);

        plan.Kind.Should().Be(CellFillPatternPlanKind.Hatch);
        plan.TileSize.Should().Be(tileSize);
        plan.StrokeThickness.Should().Be(0.75);
        plan.Lines.Should().Equal(first, second);
    }

    [Theory]
    [InlineData(CellFillPatternStyle.None)]
    [InlineData(CellFillPatternStyle.Solid)]
    public void Plan_ReturnsNoneForPatternsHandledByCellBackground(CellFillPatternStyle style)
    {
        CellFillPatternPlanner.Plan(style).Kind.Should().Be(CellFillPatternPlanKind.None);
    }
}
