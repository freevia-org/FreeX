using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWViewDepthPlannerTests
{
    [Fact]
    public void Toggle_commands_are_mutually_exclusive()
    {
        var live = new FreeWViewDepthState(FreeWViewDepthMode.LiveEditor);

        var split = FreeWViewDepthPlanner.Plan(live, FreeWViewDepthCommand.ToggleSplit);
        var multiple = FreeWViewDepthPlanner.Plan(new FreeWViewDepthState(split.Mode), FreeWViewDepthCommand.ToggleMultiplePages);
        var sideToSide = FreeWViewDepthPlanner.Plan(new FreeWViewDepthState(multiple.Mode), FreeWViewDepthCommand.ToggleSideToSide);

        split.IsSplitActive.Should().BeTrue();
        split.IsMultiplePagesActive.Should().BeFalse();
        split.IsSideToSideActive.Should().BeFalse();

        multiple.IsSplitActive.Should().BeFalse();
        multiple.IsMultiplePagesActive.Should().BeTrue();
        multiple.IsSideToSideActive.Should().BeFalse();

        sideToSide.IsSplitActive.Should().BeFalse();
        sideToSide.IsMultiplePagesActive.Should().BeFalse();
        sideToSide.IsSideToSideActive.Should().BeTrue();
        sideToSide.PagesAcross.Should().Be(2);
    }

    [Fact]
    public void Repeating_active_toggle_restores_live_editor()
    {
        var active = new FreeWViewDepthState(FreeWViewDepthMode.MultiplePagesPreview);

        var plan = FreeWViewDepthPlanner.Plan(active, FreeWViewDepthCommand.ToggleMultiplePages);

        plan.Mode.Should().Be(FreeWViewDepthMode.LiveEditor);
        plan.SurfaceKind.Should().Be(FreeWViewDepthSurfaceKind.LiveEditor);
        plan.UsesReadOnlySnapshot.Should().BeFalse();
    }

    [Fact]
    public void Preview_plans_are_explicit_about_read_only_limitations()
    {
        var split = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SplitPreview);
        var multiple = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.MultiplePagesPreview);
        var sideToSide = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SideToSidePreview);

        split.UsesReadOnlySnapshot.Should().BeTrue();
        multiple.UsesReadOnlySnapshot.Should().BeTrue();
        sideToSide.UsesReadOnlySnapshot.Should().BeTrue();
        split.Limitation.Should().Contain("read-only");
        multiple.Limitation.Should().Contain("Editing is disabled");
        sideToSide.Limitation.Should().Contain("horizontal page turning remains deferred");
    }

    [Fact]
    public void Side_to_side_preview_scale_accounts_for_two_pages()
    {
        var onePage = FreeWViewDepthPlanner.BuildPreviewScale(
            FreeWViewDepthMode.MultiplePagesPreview,
            viewportWidthDip: 1200,
            viewportHeightDip: 800,
            pageWidthDip: 600,
            pageHeightDip: 800);
        var sideToSide = FreeWViewDepthPlanner.BuildPreviewScale(
            FreeWViewDepthMode.SideToSidePreview,
            viewportWidthDip: 1200,
            viewportHeightDip: 800,
            pageWidthDip: 600,
            pageHeightDip: 800);

        sideToSide.Should().BeLessThan(onePage);
        sideToSide.Should().BeGreaterThan(0);
    }
}
