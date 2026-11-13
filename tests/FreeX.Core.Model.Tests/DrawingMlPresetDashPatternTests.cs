using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class DrawingMlPresetDashPatternTests
{
    // ECMA-376 ST_PresetLineDashVal, in multiples of line width.
    [Theory]
    [InlineData(DrawingMlPresetDash.Dot, new double[] { 1, 3 })]
    [InlineData(DrawingMlPresetDash.Dash, new double[] { 4, 3 })]
    [InlineData(DrawingMlPresetDash.LongDash, new double[] { 8, 3 })]
    [InlineData(DrawingMlPresetDash.DashDot, new double[] { 4, 3, 1, 3 })]
    [InlineData(DrawingMlPresetDash.LongDashDot, new double[] { 8, 3, 1, 3 })]
    [InlineData(DrawingMlPresetDash.LongDashDotDot, new double[] { 8, 3, 1, 3, 1, 3 })]
    [InlineData(DrawingMlPresetDash.SystemDash, new double[] { 3, 1 })]
    [InlineData(DrawingMlPresetDash.SystemDot, new double[] { 1, 1 })]
    [InlineData(DrawingMlPresetDash.SystemDashDot, new double[] { 3, 1, 1, 1 })]
    [InlineData(DrawingMlPresetDash.SystemDashDotDot, new double[] { 3, 1, 1, 1, 1, 1 })]
    public void Get_ReturnsEcmaPresetDefinition(DrawingMlPresetDash dash, double[] expected)
    {
        DrawingMlPresetDashPattern.Get(dash).Should().Equal(expected);
    }

    [Fact]
    public void Get_SolidAndUndefined_ReturnNull()
    {
        DrawingMlPresetDashPattern.Get(DrawingMlPresetDash.Solid).Should().BeNull();
        DrawingMlPresetDashPattern.Get((DrawingMlPresetDash)99).Should().BeNull();
        DrawingMlPresetDashPattern.ToArray(DrawingMlPresetDash.Solid).Should().BeNull();
    }

    [Fact]
    public void ToArray_ReturnsIndependentCopy()
    {
        var copy = DrawingMlPresetDashPattern.ToArray(DrawingMlPresetDash.LongDash)!;
        copy[0] = 99;

        DrawingMlPresetDashPattern.Get(DrawingMlPresetDash.LongDash).Should().Equal(8, 3);
    }

    [Fact]
    public void EveryPresetHasEvenDashGapPairs()
    {
        foreach (var dash in Enum.GetValues<DrawingMlPresetDash>().Where(d => d != DrawingMlPresetDash.Solid))
            (DrawingMlPresetDashPattern.Get(dash)!.Count % 2).Should().Be(0, dash.ToString());
    }

    [Fact]
    public void AppOutlineDashEnums_MapToSamePresetByName()
    {
        foreach (var dash in Enum.GetValues<DrawingShapeOutlineDash>())
            dash.ToPresetDash().ToString().Should().Be(dash.ToString());
    }
}
