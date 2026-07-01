using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationDesignCommandPlannerTests
{
    private static EditingSession MakeSession(out Presentation presentation)
    {
        presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    [Theory]
    [InlineData("freep.theme.office", "Office")]
    [InlineData("freep.theme.berlin", "Berlin")]
    [InlineData("freep.theme.facet", "Facet")]
    [InlineData("freep.theme.ion", "Ion")]
    [InlineData("freep.theme.slice", "Slice")]
    public void TryPlan_MapsThemeCommandIdsToThemeIntents(
        string commandId,
        string expectedThemeId)
    {
        PresentationDesignCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.CommandId.Should().Be(commandId);
        plan.Intent.Should().Be(PresentationDesignCommandIntentKind.SetTheme);
        plan.ThemeId.Should().Be(expectedThemeId);
    }

    [Theory]
    [InlineData("freep.slide-size-16x9", 12192000L, 6858000L)]
    [InlineData("freep.slide-size-4x3", 9144000L, 6858000L)]
    public void TryPlan_MapsSlideSizeCommandIdsToSizeIntents(
        string commandId,
        long expectedCxEmu,
        long expectedCyEmu)
    {
        PresentationDesignCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.CommandId.Should().Be(commandId);
        plan.Intent.Should().Be(PresentationDesignCommandIntentKind.SetSlideSize);
        plan.SlideSizeCxEmu.Should().Be(expectedCxEmu);
        plan.SlideSizeCyEmu.Should().Be(expectedCyEmu);
    }

    [Fact]
    public void TryPlan_MapsCustomSlideSizeToCallbackIntent()
    {
        PresentationDesignCommandPlanner.TryPlan("freep.slide-size-custom", out var plan)
            .Should()
            .BeTrue();

        plan.Intent.Should().Be(PresentationDesignCommandIntentKind.RequestCustomSlideSize);
        plan.ThemeId.Should().BeNull();
        plan.SlideSizeCxEmu.Should().BeNull();
        plan.SlideSizeCyEmu.Should().BeNull();
    }

    [Fact]
    public void TryPlan_RejectsUnknownCommandId()
    {
        PresentationDesignCommandPlanner.TryPlan("freep.design.missing", out var plan)
            .Should()
            .BeFalse();

        plan.Should().BeNull();
    }

    [Fact]
    public void TryApply_SetThemeCommand_UsesSharedThemeOperation()
    {
        var editor = MakeSession(out var presentation);
        PresentationDesignCommandPlanner.TryPlan("freep.theme.ion", out var plan)
            .Should()
            .BeTrue();

        PresentationDesignCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        presentation.Theme.Name.Should().Be("Ion");
    }

    [Fact]
    public void TryApply_SetSlideSizeCommand_UsesSharedSlideSizeOperation()
    {
        var editor = MakeSession(out var presentation);
        PresentationDesignCommandPlanner.TryPlan("freep.slide-size-4x3", out var plan)
            .Should()
            .BeTrue();

        PresentationDesignCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        presentation.SlideSizeCxEmu.Should().Be(PresentationDesignCommandPlanner.SlideSizeStandard4x3CxEmu);
        presentation.SlideSizeCyEmu.Should().Be(PresentationDesignCommandPlanner.SlideSizeStandardCyEmu);
    }

    [Fact]
    public void TryApply_CustomSlideSizeCommand_InvokesHostCallback()
    {
        var editor = MakeSession(out _);
        PresentationDesignCommandPlanner.TryPlan("freep.slide-size-custom", out var plan)
            .Should()
            .BeTrue();
        PresentationDesignCommandPlan? callbackPlan = null;

        PresentationDesignCommandPlanner.TryApply(editor, plan, p => callbackPlan = p)
            .Should()
            .BeTrue();

        callbackPlan.Should().Be(plan);
    }

    [Fact]
    public void TryApply_CustomSlideSizeCommand_RequiresHostCallback()
    {
        var editor = MakeSession(out _);
        PresentationDesignCommandPlanner.TryPlan("freep.slide-size-custom", out var plan)
            .Should()
            .BeTrue();

        PresentationDesignCommandPlanner.TryApply(editor, plan).Should().BeFalse();
    }
}
