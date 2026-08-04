using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationViewZoomPlannerTests
{
    [Theory]
    [InlineData(null, true, "1000")]
    [InlineData(" 01250 ", true, "1250")]
    [InlineData("2500", false, null)]
    public void Zoom_transition_control_normalizes_enabled_and_disabled_values(
        string? input,
        bool enabled,
        string? expected)
    {
        ZoomObjectPropertiesPlanner.TryParseTransitionDuration(
                input,
                enabled,
                out var normalized)
            .Should()
            .BeTrue();

        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-duration")]
    public void Zoom_transition_control_rejects_invalid_enabled_values(string input)
    {
        ZoomObjectPropertiesPlanner.TryParseTransitionDuration(
                input,
                enabled: true,
                out _)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("#4472c4", true, "4472C4")]
    [InlineData(" 00aaFF ", true, "00AAFF")]
    [InlineData("4472C4", false, "")]
    public void Zoom_border_control_normalizes_enabled_and_disabled_values(
        string input,
        bool enabled,
        string expected)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderColor(
                input,
                enabled,
                out var normalized)
            .Should()
            .BeTrue();

        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("GGGGGG")]
    public void Zoom_border_control_rejects_invalid_enabled_values(string input)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderColor(
                input,
                enabled: true,
                out _)
            .Should()
                .BeFalse();
    }

    [Theory]
    [InlineData("2", true, 25400)]
    [InlineData(" 2.5 ", true, 31750)]
    [InlineData("2", false, null)]
    public void Zoom_border_width_normalizes_enabled_and_disabled_values(
        string input,
        bool enabled,
        int? expected)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderWidth(
                input,
                enabled,
                out var normalized)
            .Should()
            .BeTrue();

        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData(25400, "2")]
    [InlineData(null, "")]
    public void Zoom_border_width_formats_points(int? widthEmu, string expected)
    {
        ZoomObjectPropertiesPlanner.FormatFrameBorderWidth(
                new ZoomObjectProperties(FrameBorderWidthEmu: widthEmu))
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1585")]
    [InlineData("not-a-width")]
    public void Zoom_border_width_rejects_invalid_enabled_values(string input)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderWidth(
                input,
                enabled: true,
                out _)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("DashDot", OutlineDash.DashDot)]
    [InlineData("dot", OutlineDash.Dot)]
    [InlineData("", null)]
    public void Zoom_border_dash_normalizes_values(string input, OutlineDash? expected)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderDash(input, out var normalized)
            .Should().BeTrue();

        normalized.Should().Be(expected);
    }

    [Fact]
    public void Zoom_border_dash_rejects_unknown_values()
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderDash("customDash", out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("rect", "rect")]
    [InlineData("ROUNDRECT", "roundRect")]
    [InlineData("ellipse", "ellipse")]
    [InlineData("", null)]
    public void Zoom_frame_geometry_normalizes_supported_values(
        string input,
        string? expected)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameGeometry(input, out var normalized)
            .Should().BeTrue();

        normalized.Should().Be(expected);
    }

    [Fact]
    public void Zoom_frame_geometry_rejects_unrendered_presets()
    {
        ZoomObjectPropertiesPlanner.TryParseFrameGeometry("hexagon", out _)
            .Should().BeFalse();
    }

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
