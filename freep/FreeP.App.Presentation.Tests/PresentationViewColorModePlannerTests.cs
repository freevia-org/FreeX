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
}
