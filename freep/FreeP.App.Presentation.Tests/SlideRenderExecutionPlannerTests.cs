using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideRenderExecutionPlannerTests
{
    [Fact]
    public void Plan_PreservesPainterOrderAcrossFeatureHeavyOperations()
    {
        DrawOp[] operations =
        [
            new DrawOp.Background(),
            new DrawOp.Shape { ShapeId = 1 },
            new DrawOp.Picture { ShapeId = 2 },
            new DrawOp.Table { ShapeId = 3 },
            new DrawOp.Chart { ShapeId = 4 },
        ];

        var commands = SlideRenderExecutionPlanner.Plan(operations);

        commands.Select(command => command.Operation).Should().Equal(operations);
        commands.Should().OnlyContain(command => !command.SuppressShapeText);
    }

    [Fact]
    public void Plan_AppliesPreviewBeforeSuppressionWithoutMovingTheOperation()
    {
        var source = new DrawOp.Shape { ShapeId = 7 };
        var preview = new DrawOp.Shape
        {
            ShapeId = 7,
            BoundsDip = new LayoutRect(10, 20, 30, 40),
        };
        DrawOp[] operations =
        [
            new DrawOp.Picture { ShapeId = 1 },
            source,
            new DrawOp.Table { ShapeId = 2 },
        ];

        var commands = SlideRenderExecutionPlanner.Plan(
            operations,
            new Dictionary<uint, DrawOp> { [7] = preview });

        commands.Select(command => command.Operation).Should().Equal(
            operations[0],
            preview,
            operations[2]);

        SlideRenderExecutionPlanner.Plan(
                operations,
                new Dictionary<uint, DrawOp> { [7] = preview },
                new HashSet<uint> { 7 })
            .Select(command => command.Operation)
            .Should().Equal(operations[0], operations[2]);
    }

    [Fact]
    public void Plan_SuppressesEveryShapeBackedOperationButNeverTheBackground()
    {
        DrawOp[] operations =
        [
            new DrawOp.Background(),
            new DrawOp.Shape { ShapeId = 1 },
            new DrawOp.Picture { ShapeId = 2 },
            new DrawOp.Table { ShapeId = 3 },
            new DrawOp.Chart { ShapeId = 4 },
        ];

        var commands = SlideRenderExecutionPlanner.Plan(
            operations,
            suppressedShapeIds: new HashSet<uint> { 1, 2, 3, 4 });

        commands.Should().ContainSingle();
        commands[0].Operation.Should().BeOfType<DrawOp.Background>();
    }

    [Fact]
    public void Plan_SuppressesOnlyTheActiveShapesBaseText()
    {
        var active = new DrawOp.Shape { ShapeId = 11 };
        var other = new DrawOp.Shape { ShapeId = 12 };

        var commands = SlideRenderExecutionPlanner.Plan(
            [active, other, new DrawOp.Picture { ShapeId = 11 }],
            activeTextEditShapeId: 11);

        commands[0].SuppressShapeText.Should().BeTrue();
        commands[1].SuppressShapeText.Should().BeFalse();
        commands[2].SuppressShapeText.Should().BeFalse();
    }
}
