using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ShapeGradientPlannerTests
{
    [Fact]
    public void DialogSizeConstants_MatchVisualEvidenceCaptureContract()
    {
        ShapeGradientPlanner.DialogWidth.Should().Be(500);
        ShapeGradientPlanner.DialogHeight.Should().Be(300);
    }

    [Fact]
    public void Capture_ReusesExistingFillAndGradientEnd()
    {
        var shape = new DrawingShapeModel
        {
            FillColor = new CellColor(10, 20, 30),
            GradientFillEndColor = new CellColor(40, 50, 60),
            GradientFillDirection = DrawingShapeGradientDirection.Vertical,
        };

        var values = ShapeGradientPlanner.Capture(shape);

        values.StartColor.Should().Be(new CellColor(10, 20, 30));
        values.EndColor.Should().Be(new CellColor(40, 50, 60));
        values.Direction.Should().Be(DrawingShapeGradientDirection.Vertical);
    }

    [Fact]
    public void Capture_FallsBackToDefaults_WhenShapeHasNoGradient()
    {
        var shape = new DrawingShapeModel { FillColor = null, GradientFillEndColor = null };

        var values = ShapeGradientPlanner.Capture(shape);

        values.StartColor.Should().Be(ShapeGradientPlanner.DefaultStartColor);
        values.EndColor.Should().Be(ShapeGradientPlanner.DefaultEndColor);
    }

    [Fact]
    public void CreateDirectionOptions_CoversEveryDirection()
    {
        ShapeGradientPlanner.CreateDirectionOptions().Select(o => o.Direction)
            .Should().BeEquivalentTo(Enum.GetValues<DrawingShapeGradientDirection>());
    }

    [Fact]
    public void FindDirectionIndex_RoundTrips()
    {
        var options = ShapeGradientPlanner.CreateDirectionOptions();
        var index = ShapeGradientPlanner.FindDirectionIndex(options, DrawingShapeGradientDirection.DiagonalUp);
        options[index].Direction.Should().Be(DrawingShapeGradientDirection.DiagonalUp);
        ShapeGradientPlanner.DirectionAt(options, index).Should().Be(DrawingShapeGradientDirection.DiagonalUp);
    }

    [Fact]
    public void DirectionAt_ClampsOutOfRangeToDiagonalDown()
    {
        var options = ShapeGradientPlanner.CreateDirectionOptions();
        ShapeGradientPlanner.DirectionAt(options, 99).Should().Be(DrawingShapeGradientDirection.DiagonalDown);
        ShapeGradientPlanner.DirectionAt(options, -1).Should().Be(DrawingShapeGradientDirection.DiagonalDown);
    }

    [Fact]
    public void CreateResult_NormalizesDirection()
    {
        var result = ShapeGradientPlanner.CreateResult(
            new CellColor(1, 2, 3), new CellColor(4, 5, 6), (DrawingShapeGradientDirection)42);
        result.Direction.Should().Be(DrawingShapeGradientDirection.DiagonalDown);
    }

    [Fact]
    public void PreviewVector_HorizontalRunsLeftToRight()
    {
        ShapeGradientPlanner.PreviewVector(DrawingShapeGradientDirection.Horizontal, 100, 50)
            .Should().Be((0.0, 0.5, 1.0, 0.5));
    }

    [Fact]
    public void PreviewVector_VerticalRunsTopToBottom()
    {
        ShapeGradientPlanner.PreviewVector(DrawingShapeGradientDirection.Vertical, 100, 50)
            .Should().Be((0.5, 0.0, 0.5, 1.0));
    }

    [Fact]
    public void BuildCommand_MapsPortableResultToCoreCommand()
    {
        var result = ShapeGradientPlanner.CreateResult(
            new CellColor(1, 2, 3),
            new CellColor(4, 5, 6),
            DrawingShapeGradientDirection.Horizontal);

        ShapeGradientPlanner.BuildCommand(SheetId.New(), Guid.NewGuid(), result)
            .Should().BeOfType<SetDrawingShapeGradientCommand>();
    }
}
