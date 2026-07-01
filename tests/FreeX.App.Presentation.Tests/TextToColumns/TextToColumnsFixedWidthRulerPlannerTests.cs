using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Tests.TextToColumns;

public sealed class TextToColumnsFixedWidthRulerPlannerTests
{
    [Fact]
    public void PositionFromRulerX_MapsRulerCoordinateToBreakPosition()
    {
        TextToColumnsFixedWidthRulerPlanner.PositionFromRulerX(110, rulerWidth: 440, maxLength: 20)
            .Should()
            .Be(5);
    }

    [Fact]
    public void RulerXFromPosition_MapsBreakPositionToRulerCoordinate()
    {
        TextToColumnsFixedWidthRulerPlanner.RulerXFromPosition(10, rulerWidth: 440, maxLength: 20)
            .Should()
            .Be(220);
    }

    [Fact]
    public void FindNearestBreakIndex_RespectsTolerance()
    {
        TextToColumnsFixedWidthRulerPlanner.FindNearestBreakIndex([4, 8, 12], x: 178, tolerance: 5, rulerWidth: 440, maxLength: 20)
            .Should()
            .Be(1);
        TextToColumnsFixedWidthRulerPlanner.FindNearestBreakIndex([4, 8, 12], x: 178, tolerance: 1, rulerWidth: 440, maxLength: 20)
            .Should()
            .Be(-1);
    }

    [Fact]
    public void MaxLength_UsesAtLeastTwoCharacters()
    {
        TextToColumnsFixedWidthRulerPlanner.MaxLength([])
            .Should()
            .Be(2);
        TextToColumnsFixedWidthRulerPlanner.MaxLength(["A", "ABCDE"])
            .Should()
            .Be(5);
    }
}
