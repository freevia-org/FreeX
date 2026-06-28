using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Tests.TextToColumns;

public sealed class TextToColumnsFixedWidthBreakPlannerTests
{
    [Fact]
    public void ParseBreakPositions_NormalizesPositiveBreaks()
    {
        TextToColumnsFixedWidthBreakPlanner.ParseBreakPositions("12, 4; x 8 4")
            .Should()
            .Equal(4, 8, 12);
    }

    [Fact]
    public void TryParseBreakPositions_RequiresValidPositionsInsidePreview()
    {
        TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions("8, 4; 4", 12, out var parsed)
            .Should()
            .BeTrue();
        parsed.Should().Equal(4, 8);

        TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions("8, 12", 12, out _)
            .Should()
            .BeFalse();
        TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions("1", 1, out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void AddMoveAndRemoveBreakPositions_ClampAndNormalize()
    {
        TextToColumnsFixedWidthBreakPlanner.AddBreakPosition([8, 4], 99, maxLength: 20)
            .Should()
            .Equal(4, 8, 19);

        TextToColumnsFixedWidthBreakPlanner.MoveBreakPosition([4, 8, 12], index: 1, position: 10, maxLength: 20)
            .Should()
            .Equal(4, 10, 12);

        TextToColumnsFixedWidthBreakPlanner.RemoveBreakPosition([4, 8, 12], index: 1)
            .Should()
            .Equal(4, 12);
    }
}
