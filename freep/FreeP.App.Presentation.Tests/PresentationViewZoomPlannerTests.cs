using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationViewZoomPlannerTests
{
    [Fact]
    public void Built_in_plans_expose_zoom_and_fit_to_window_command_ids()
    {
        PresentationViewZoomPlanner.BuiltInPlans
            .Select(plan => plan.CommandId)
            .Should()
            .Equal(
                PresentationViewZoomPlanner.ZoomCommandId,
                PresentationViewZoomPlanner.FitToWindowCommandId);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(10, 10)]
    [InlineData(125, 125)]
    [InlineData(401, 400)]
    public void Normalize_zoom_percent_clamps_to_powerpoint_style_bounds(int input, int expected)
    {
        PresentationViewZoomPlanner.NormalizeZoomPercent(input)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("125%", 125)]
    [InlineData(" 66 ", 66)]
    [InlineData("999", 400)]
    public void Try_parse_zoom_percent_accepts_percent_text_and_clamps(string input, int expected)
    {
        PresentationViewZoomPlanner.TryParseZoomPercent(input, out var percent)
            .Should()
            .BeTrue();

        percent.Should().Be(expected);
    }

    [Fact]
    public void Zoom_command_sets_explicit_percent_state_and_multiplier()
    {
        var plan = PresentationViewZoomPlanner.BuiltInPlans
            .Single(plan => plan.Kind == PresentationViewZoomCommandKind.Zoom);

        var result = PresentationViewZoomPlanner.Execute(
            PresentationViewZoomState.FitToWindow,
            plan,
            "150%");

        result.State.Mode.Should().Be(PresentationViewZoomMode.Percent);
        result.State.ZoomPercent.Should().Be(150);
        result.StageScaleMultiplier.Should().Be(1.5);
        result.RequestsZoomDialog.Should().BeTrue();
    }

    [Fact]
    public void Fit_to_window_keeps_zoom_percent_but_uses_fit_multiplier()
    {
        var state = new PresentationViewZoomState(PresentationViewZoomMode.Percent, 175);
        var plan = PresentationViewZoomPlanner.BuiltInPlans
            .Single(plan => plan.Kind == PresentationViewZoomCommandKind.FitToWindow);

        var result = PresentationViewZoomPlanner.Execute(state, plan);

        result.State.Mode.Should().Be(PresentationViewZoomMode.FitToWindow);
        result.State.ZoomPercent.Should().Be(175);
        result.StageScaleMultiplier.Should().Be(1.0);
        result.RequestsZoomDialog.Should().BeFalse();
    }

    [Fact]
    public void Unknown_command_id_does_not_execute()
    {
        PresentationViewZoomPlanner.TryExecute(
                PresentationViewZoomState.FitToWindow,
                "freep.view.ruler",
                null,
                out _)
            .Should()
            .BeFalse();
    }
}
