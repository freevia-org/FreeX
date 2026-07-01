using FreeP.App.Compositor;
using FreeP.Core.Model;

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
    public void TryPlan_MapsLayoutToHostCallbackIntent()
    {
        PresentationDesignCommandPlanner.TryPlan(PresentationDesignCommandPlanner.LayoutCommandId, out var plan)
            .Should()
            .BeTrue();

        plan.CommandId.Should().Be(PresentationDesignCommandPlanner.LayoutCommandId);
        plan.Intent.Should().Be(PresentationDesignCommandIntentKind.RequestLayoutPicker);
        plan.ThemeId.Should().BeNull();
        plan.SlideSizeCxEmu.Should().BeNull();
        plan.SlideSizeCyEmu.Should().BeNull();
    }

    [Fact]
    public void BuildLayoutPickerPlan_ExposesConcreteSharedLayoutChoices()
    {
        var editor = MakeSession(out var presentation);
        presentation.Layouts.Add(new SlideLayout
        {
            Id = "rId2",
            Name = "Blank",
            LayoutType = SlideLayoutType.Blank,
            MasterId = presentation.Masters[0].Id
        });

        var plan = PresentationDesignCommandPlanner.BuildLayoutPickerPlan(
            presentation,
            editor.CurrentSlideIndex);

        plan.CommandId.Should().Be(PresentationDesignCommandPlanner.LayoutCommandId);
        plan.HasCurrentSlide.Should().BeTrue();
        plan.CanApply.Should().BeTrue();
        plan.CurrentLayoutId.Should().Be("rId1");
        plan.Choices.Should().ContainEquivalentOf(new PresentationLayoutChoice(
            "rId1",
            "Title Slide",
            SlideLayoutType.Title,
            true));
        plan.Choices.Should().ContainEquivalentOf(new PresentationLayoutChoice(
            "rId2",
            "Blank",
            SlideLayoutType.Blank,
            false));
    }

    [Fact]
    public void TryApplyLayoutChoice_AppliesCurrentSlideLayoutThroughSharedModel()
    {
        var editor = MakeSession(out var presentation);
        presentation.Layouts.Add(new SlideLayout
        {
            Id = "rId2",
            Name = "Two Content",
            LayoutType = SlideLayoutType.TwoContent,
            MasterId = presentation.Masters[0].Id
        });

        PresentationDesignCommandPlanner.TryApplyLayoutChoice(editor, "rId2", out var choice)
            .Should()
            .BeTrue();

        choice.Should().Be(new PresentationLayoutChoice(
            "rId2",
            "Two Content",
            SlideLayoutType.TwoContent,
            false));
        editor.CurrentSlide!.LayoutId.Should().Be("rId2");

        editor.Undo();
        editor.CurrentSlide.LayoutId.Should().Be("rId1");
    }

    [Fact]
    public void TryApplyLayoutChoice_RejectsMissingLayout()
    {
        var editor = MakeSession(out _);

        PresentationDesignCommandPlanner.TryApplyLayoutChoice(editor, "missing", out var choice)
            .Should()
            .BeFalse();

        choice.Should().BeNull();
        editor.CurrentSlide!.LayoutId.Should().Be("rId1");
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

    [Fact]
    public void TryApply_LayoutCommand_InvokesHostCallback()
    {
        var editor = MakeSession(out _);
        PresentationDesignCommandPlanner.TryPlan(PresentationDesignCommandPlanner.LayoutCommandId, out var plan)
            .Should()
            .BeTrue();
        PresentationDesignCommandPlan? callbackPlan = null;

        PresentationDesignCommandPlanner.TryApply(editor, plan, p => callbackPlan = p)
            .Should()
            .BeTrue();

        callbackPlan.Should().Be(plan);
    }

    [Fact]
    public void TryApply_LayoutCommand_RequiresHostCallback()
    {
        var editor = MakeSession(out _);
        PresentationDesignCommandPlanner.TryPlan(PresentationDesignCommandPlanner.LayoutCommandId, out var plan)
            .Should()
            .BeTrue();

        PresentationDesignCommandPlanner.TryApply(editor, plan).Should().BeFalse();
    }
}
