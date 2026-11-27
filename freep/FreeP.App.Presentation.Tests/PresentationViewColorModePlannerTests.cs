using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationViewColorModePlannerTests
{
    [Fact]
    public void Default_mode_exposes_color_as_the_exclusive_selection()
    {
        PresentationViewColorModePlanner.BuildPlans(PresentationViewColorModeState.Color)
            .Select(plan => (plan.CommandId, plan.IsChecked))
            .Should()
            .Equal(
                (PresentationViewColorModePlanner.ColorCommandId, true),
                (PresentationViewColorModePlanner.GrayscaleCommandId, false),
                (PresentationViewColorModePlanner.BlackAndWhiteCommandId, false));
    }

    [Theory]
    [InlineData(PresentationViewColorMode.Grayscale, PresentationViewColorModePlanner.GrayscaleCommandId)]
    [InlineData(PresentationViewColorMode.BlackAndWhite, PresentationViewColorModePlanner.BlackAndWhiteCommandId)]
    public void Selecting_a_display_treatment_is_non_destructive_and_exclusive(
        PresentationViewColorMode mode,
        string commandId)
    {
        var plan = PresentationViewColorModePlanner.BuildPlan(mode, PresentationViewColorModeState.Color);
        var state = PresentationViewColorModePlanner.Select(PresentationViewColorModeState.Color, plan);

        state.Mode.Should().Be(mode);
        PresentationViewColorModePlanner.BuildPlan(mode, state).CommandId.Should().Be(commandId);
        PresentationViewColorModePlanner.BuildPlan(PresentationViewColorMode.Color, state).IsChecked
            .Should().Be(mode == PresentationViewColorMode.Color);
    }

    [Fact]
    public void BuildColorEffectPlan_maps_black_and_white_to_a_bilevel_threshold_and_the_rest_to_grayscale()
    {
        PresentationViewColorModePlanner.BuildColorEffectPlan(PresentationViewColorMode.BlackAndWhite)
            .Should().Be(new PictureColorEffectPlan(false, 0.5, null, null));
        PresentationViewColorModePlanner.BuildColorEffectPlan(PresentationViewColorMode.Grayscale)
            .Should().Be(new PictureColorEffectPlan(true, null, null, null));
        PresentationViewColorModePlanner.BuildColorEffectPlan(PresentationViewColorMode.Color)
            .Should().Be(new PictureColorEffectPlan(true, null, null, null));
    }

    [Fact]
    public void Slide_canvases_use_the_shared_color_effect_plan_instead_of_a_local_mapping()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "freep", "FreeP.App.Rendering.Wpf")))
        {
            root = Path.GetDirectoryName(root);
        }

        root.Should().NotBeNull();
        foreach (var project in new[] { "FreeP.App.Rendering.Wpf", "FreeP.App.Rendering.Avalonia" })
        {
            var source = File.ReadAllText(Path.Combine(root!, "freep", project, "SlideCanvas.cs"));
            source.Should().Contain("PresentationViewColorModePlanner.BuildColorEffectPlan(");
            source.Should().NotContain("new PictureColorEffectPlan(");
        }
    }
}
