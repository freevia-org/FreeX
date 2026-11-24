using Free.Shared.Shell;
using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectNudgePlannerTests
{
    private static readonly Guid ObjectId = Guid.Parse("4c1f1aae-3a4a-4897-b9ee-d4c45dc11054");

    [Theory]
    [InlineData(DrawingObjectNudgeDirection.Up, 0, -3)]
    [InlineData(DrawingObjectNudgeDirection.Down, 0, 3)]
    [InlineData(DrawingObjectNudgeDirection.Left, -3, 0)]
    [InlineData(DrawingObjectNudgeDirection.Right, 3, 0)]
    public void TryPlan_ResolvesStandardDirectionalDelta(
        DrawingObjectNudgeDirection direction,
        double expectedX,
        double expectedY)
    {
        DrawingObjectNudgePlanner.TryPlan(
                direction,
                FreeKeyModifiers.None,
                SelectionPaneObjectKind.Shape,
                ObjectId,
                out var plan)
            .Should().BeTrue();

        plan.Should().Be(new DrawingObjectNudgePlan(
            SelectionPaneObjectKind.Shape,
            ObjectId,
            expectedX,
            expectedY));
    }

    [Fact]
    public void TryPlan_ControlUsesFineStep()
    {
        DrawingObjectNudgePlanner.TryPlan(
                DrawingObjectNudgeDirection.Right,
                FreeKeyModifiers.Control,
                SelectionPaneObjectKind.Chart,
                ObjectId,
                out var plan)
            .Should().BeTrue();

        plan.DeltaX.Should().Be(DrawingObjectNudgePlanner.FineStep);
        plan.DeltaY.Should().Be(0);
    }

    [Theory]
    [InlineData(FreeKeyModifiers.Shift)]
    [InlineData(FreeKeyModifiers.Alt)]
    [InlineData(FreeKeyModifiers.Meta)]
    [InlineData(FreeKeyModifiers.Control | FreeKeyModifiers.Shift)]
    public void TryPlan_RejectsModifierCombinationsOwnedByOtherInteractions(
        FreeKeyModifiers modifiers) =>
        DrawingObjectNudgePlanner.TryPlan(
                DrawingObjectNudgeDirection.Left,
                modifiers,
                SelectionPaneObjectKind.Picture,
                ObjectId,
                out _)
            .Should().BeFalse();

    [Theory]
    [InlineData(null, SelectionPaneObjectKind.Shape, "4c1f1aae-3a4a-4897-b9ee-d4c45dc11054")]
    [InlineData(DrawingObjectNudgeDirection.Up, null, "4c1f1aae-3a4a-4897-b9ee-d4c45dc11054")]
    [InlineData(DrawingObjectNudgeDirection.Up, SelectionPaneObjectKind.Shape, "00000000-0000-0000-0000-000000000000")]
    public void TryPlan_RejectsIncompleteSelection(
        DrawingObjectNudgeDirection? direction,
        SelectionPaneObjectKind? kind,
        string objectId) =>
        DrawingObjectNudgePlanner.TryPlan(
                direction,
                FreeKeyModifiers.None,
                kind,
                Guid.Parse(objectId),
                out _)
            .Should().BeFalse();
}
