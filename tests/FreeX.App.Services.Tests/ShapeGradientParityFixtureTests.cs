using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ShapeGradientParityFixtureTests
{
    [Fact]
    public void Fixture_UsesPlannerDefaultsAndAppliesThemToASelectedShape()
    {
        var shape = new DrawingShapeModel();

        ShapeGradientParityFixture.Apply(shape);

        ShapeGradientParityFixture.StartColor.Should().Be(ShapeGradientPlanner.DefaultStartColor);
        ShapeGradientParityFixture.EndColor.Should().Be(ShapeGradientPlanner.DefaultEndColor);
        ShapeGradientParityFixture.Direction.Should().Be(DrawingShapeGradientDirection.DiagonalDown);
        shape.FillColor.Should().Be(ShapeGradientParityFixture.StartColor);
        shape.GradientFillEndColor.Should().Be(ShapeGradientParityFixture.EndColor);
        shape.GradientFillDirection.Should().Be(ShapeGradientParityFixture.Direction);
    }
}
