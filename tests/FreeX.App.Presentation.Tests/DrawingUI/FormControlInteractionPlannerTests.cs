using FluentAssertions;
using FreeX.App.Presentation.Drawing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class FormControlInteractionPlannerTests
{
    private static readonly LayoutRect Bounds = new(10, 20, 80, 30);

    [Theory]
    [InlineData(FormControlKind.CheckBox)]
    [InlineData(FormControlKind.OptionButton)]
    [InlineData(FormControlKind.Button)]
    [InlineData(FormControlKind.DropDown)]
    public void PlanInteraction_UsesBodyGestureForWholeControlKinds(FormControlKind kind)
    {
        var control = new FormControlModel { Kind = kind };

        var plan = FormControlRenderPlanner.PlanInteraction(
            control,
            Bounds,
            new LayoutPoint(40, 35),
            spinnerButtonWidth: Bounds.Width);

        plan.Should().Be(new FormControlInteractionPlan(FormControlGesture.Body, 0));
    }

    [Theory]
    [InlineData(15, 25, FormControlGesture.StepUp)]
    [InlineData(15, 45, FormControlGesture.StepDown)]
    [InlineData(50, 25, FormControlGesture.StepDown)]
    public void PlanInteraction_PreservesConfiguredSpinnerHitExtent(
        double x,
        double y,
        FormControlGesture expected)
    {
        var control = new FormControlModel { Kind = FormControlKind.Spinner };

        var plan = FormControlRenderPlanner.PlanInteraction(
            control,
            Bounds,
            new LayoutPoint(x, y),
            spinnerButtonWidth: 17);

        plan.Gesture.Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 10, 5, FormControlGesture.StepUp)]
    [InlineData(100, 10, 85, FormControlGesture.StepDown)]
    [InlineData(10, 80, 5, FormControlGesture.StepUp)]
    [InlineData(10, 80, 75, FormControlGesture.StepDown)]
    public void PlanInteraction_ClassifiesHorizontalAndVerticalScrollBars(
        double width,
        double height,
        double alongAxis,
        FormControlGesture expected)
    {
        var rect = new LayoutRect(0, 0, width, height);
        var point = width >= height
            ? new LayoutPoint(alongAxis, height / 2)
            : new LayoutPoint(width / 2, alongAxis);
        var control = new FormControlModel { Kind = FormControlKind.ScrollBar };

        FormControlRenderPlanner.PlanInteraction(control, rect, point, width)
            .Gesture.Should().Be(expected);
    }

    [Theory]
    [InlineData(21, 1)]
    [InlineData(35, 2)]
    [InlineData(64, 3)]
    public void PlanInteraction_ResolvesOneBasedListItem(double y, int expectedIndex)
    {
        var control = new FormControlModel { Kind = FormControlKind.ListBox };

        var plan = FormControlRenderPlanner.PlanInteraction(
            control,
            Bounds,
            new LayoutPoint(20, y),
            spinnerButtonWidth: Bounds.Width);

        plan.Gesture.Should().Be(FormControlGesture.Body);
        plan.ListItemIndex.Should().Be(expectedIndex);
    }

    [Theory]
    [InlineData(FormControlKind.CheckBox, true)]
    [InlineData(FormControlKind.ListBox, true)]
    [InlineData(FormControlKind.GroupBox, false)]
    [InlineData(FormControlKind.Label, false)]
    [InlineData(FormControlKind.Unknown, false)]
    public void IsInteractive_ExcludesDisplayOnlyKinds(FormControlKind kind, bool expected)
    {
        FormControlRenderPlanner.IsInteractive(kind).Should().Be(expected);
    }
}
