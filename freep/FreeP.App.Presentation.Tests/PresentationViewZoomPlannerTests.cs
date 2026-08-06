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

    [Fact]
    public void Zoom_border_gradient_normalizes_two_colors_and_angle()
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderGradient(
                "#4472c4", " ffffff ", "135.5", enabled: true, out var normalized)
            .Should().BeTrue();

        normalized.Should().Be(new ZoomFrameBorderGradient(
            "4472C4", "FFFFFF", 8_130_000));
        ZoomObjectPropertiesPlanner.IsFrameBorderEnabled(
                new ZoomObjectProperties(FrameBorderGradient: normalized))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("GGGGGG", "FFFFFF", "0")]
    [InlineData("4472C4", "FFFFFF", "361")]
    [InlineData("4472C4", "FFFFFF", "not-an-angle")]
    public void Zoom_border_gradient_rejects_invalid_values(
        string start, string end, string angle)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderGradient(
                start, end, angle, enabled: true, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Zoom_border_pattern_normalizes_preset_and_colors()
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderPattern(
                "PCT50", "#4472c4", " ffffff ", enabled: true, out var normalized)
            .Should().BeTrue();

        normalized.Should().Be(new ZoomFrameBorderPattern("pct50", "4472C4", "FFFFFF"));
        ZoomObjectPropertiesPlanner.IsFrameBorderEnabled(
                new ZoomObjectProperties(FrameBorderPattern: normalized))
            .Should().BeTrue();
    }

    [Fact]
    public void Zoom_border_no_fill_is_an_explicit_enabled_state()
    {
        var properties = new ZoomObjectProperties(FrameBorderNoFill: true);

        ZoomObjectPropertiesPlanner.IsFrameBorderEnabled(properties).Should().BeTrue();
        ZoomObjectPropertiesPlanner.IsFrameBorderNoFillEnabled(properties).Should().BeTrue();
        ZoomObjectPropertiesPlanner.IsFrameBorderNoFillEnabled(new ZoomObjectProperties())
            .Should().BeFalse();
    }

    [Fact]
    public void Zoom_border_theme_color_is_an_explicit_enabled_state()
    {
        var properties = new ZoomObjectProperties(FrameBorderThemeColor: ThemeColorSlot.Accent2);

        ZoomObjectPropertiesPlanner.IsFrameBorderEnabled(properties).Should().BeTrue();
        ZoomObjectPropertiesPlanner.IsFrameBorderThemeColorEnabled(properties).Should().BeTrue();
        ZoomObjectPropertiesPlanner.FrameBorderThemeColorOptions
            .Should().Contain(ThemeColorSlot.Accent2);
    }

    [Fact]
    public void Zoom_border_shadow_normalizes_editable_values()
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderShadow(
                "#404040", "50", "4", "3", "45", enabled: true, out var normalized)
            .Should().BeTrue();

        normalized.Should().Be(new ZoomFrameBorderShadow(
            "404040", 50000, 50800, 38100, 2700000));
        ZoomObjectPropertiesPlanner.IsFrameBorderShadowEnabled(
                new ZoomObjectProperties(FrameBorderShadow: normalized))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("GGGGGG", "50", "4", "3", "45")]
    [InlineData("404040", "101", "4", "3", "45")]
    [InlineData("404040", "50", "-1", "3", "45")]
    [InlineData("404040", "50", "4", "3", "361")]
    public void Zoom_border_shadow_rejects_invalid_values(
        string color, string alpha, string blur, string distance, string direction)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderShadow(
                color, alpha, blur, distance, direction, enabled: true, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Zoom_border_glow_normalizes_editable_values()
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderGlow(
                "#00AAFF", "42", "12", enabled: true, out var normalized)
            .Should().BeTrue();

        normalized.Should().Be(new ZoomFrameBorderGlow("00AAFF", 42000, 152400));
        var properties = new ZoomObjectProperties(FrameBorderGlow: normalized);
        ZoomObjectPropertiesPlanner.FormatFrameBorderGlowColor(properties)
            .Should().Be("00AAFF");
        ZoomObjectPropertiesPlanner.FormatFrameBorderGlowAlpha(properties)
            .Should().Be("42");
        ZoomObjectPropertiesPlanner.FormatFrameBorderGlowRadius(properties)
            .Should().Be("12");
        ZoomObjectPropertiesPlanner.IsFrameBorderGlowEnabled(
                properties)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("GGGGGG", "50", "4")]
    [InlineData("00AAFF", "101", "4")]
    [InlineData("00AAFF", "50", "-1")]
    public void Zoom_border_glow_rejects_invalid_values(string color, string alpha, string radius)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderGlow(
                color, alpha, radius, enabled: true, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Zoom_border_soft_edge_normalizes_editable_values()
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderSoftEdge(
                "12.5", enabled: true, out var normalized)
            .Should().BeTrue();

        normalized.Should().Be(new ZoomFrameBorderSoftEdge(158750));
        var properties = new ZoomObjectProperties(FrameBorderSoftEdge: normalized);
        ZoomObjectPropertiesPlanner.FormatFrameBorderSoftEdgeRadius(properties)
            .Should().Be("12.5");
        ZoomObjectPropertiesPlanner.IsFrameBorderSoftEdgeEnabled(properties)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void Zoom_border_soft_edge_rejects_invalid_values(string radius)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderSoftEdge(
                radius, enabled: true, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Zoom_border_reflection_normalizes_editable_values()
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderReflection(
                "42", "3.5", "90", "-75", "2.5", enabled: true, out var normalized)
            .Should().BeTrue();

        normalized.Should().Be(new ZoomFrameBorderReflection(
            42000, 31750, 44450, 5400000, -75000, 100000));
        var properties = new ZoomObjectProperties(FrameBorderReflection: normalized);
        ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionAlpha(properties).Should().Be("42");
        ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionBlur(properties).Should().Be("2.5");
        ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionDistance(properties).Should().Be("3.5");
        ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionDirection(properties).Should().Be("90");
        ZoomObjectPropertiesPlanner.FormatFrameBorderReflectionScale(properties).Should().Be("-75");
        ZoomObjectPropertiesPlanner.IsFrameBorderReflectionEnabled(properties).Should().BeTrue();
    }

    [Theory]
    [InlineData("101", "0", "90", "-100")]
    [InlineData("50", "0", "90", "0")]
    [InlineData("50", "0", "90", "-101")]
    public void Zoom_border_reflection_rejects_invalid_values(
        string alpha, string distance, string direction, string scale)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderReflection(
                alpha, distance, direction, scale, "2", enabled: true, out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void Zoom_border_reflection_rejects_invalid_blur(string blur)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderReflection(
                "50", "0", "90", "-100", blur, enabled: true, out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("not-a-pattern", "4472C4", "FFFFFF")]
    [InlineData("pct50", "GGGGGG", "FFFFFF")]
    [InlineData("pct50", "4472C4", "12345")]
    public void Zoom_border_pattern_rejects_invalid_values(
        string preset, string foreground, string background)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderPattern(
                preset, foreground, background, enabled: true, out _)
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
