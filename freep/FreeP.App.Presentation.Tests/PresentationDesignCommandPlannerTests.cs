using FreeP.App.Compositor;
using FreeP.Core.IO;
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

    [Theory]
    [InlineData("freep.background-white", 0xFFFFFF)]
    [InlineData("freep.background-black", 0x000000)]
    [InlineData("freep.background-blue", 0xD9EAF7)]
    public void TryPlan_MapsBackgroundCommandIdsToSolidFillIntents(string commandId, int expectedRgb)
    {
        PresentationDesignCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.Intent.Should().Be(PresentationDesignCommandIntentKind.SetSlideBackground);
        plan.BackgroundRgb.Should().Be(expectedRgb);
    }

    [Fact]
    public void TryPlan_MapsBackgroundResetToInheritanceIntent()
    {
        PresentationDesignCommandPlanner.TryPlan("freep.background-reset", out var plan).Should().BeTrue();

        plan.Intent.Should().Be(PresentationDesignCommandIntentKind.SetSlideBackground);
        plan.BackgroundRgb.Should().BeNull();
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
            MasterId = presentation.Masters[0].Id,
            Placeholders =
            {
                new SlideShape { Id = 10, Placeholder = new Placeholder { Type = PlaceholderType.Title } },
            }
        });

        var plan = PresentationDesignCommandPlanner.BuildLayoutPickerPlan(
            presentation,
            editor.CurrentSlideIndex);

        plan.CommandId.Should().Be(PresentationDesignCommandPlanner.LayoutCommandId);
        plan.HasCurrentSlide.Should().BeTrue();
        plan.CanApply.Should().BeTrue();
        plan.CurrentLayoutId.Should().Be("rId1");
        plan.Groups.Should().ContainSingle();
        plan.Groups[0].Heading.Should().Be("Master 1");
        plan.Groups[0].Choices.Select(choice => choice.LayoutId).Should().Equal("rId1", "rId2");
        var current = plan.Choices.Single(choice => choice.LayoutId == "rId1");
        current.DisplayName.Should().Be("Title Slide");
        current.LayoutType.Should().Be(SlideLayoutType.Title);
        current.IsCurrent.Should().BeTrue();
        current.MasterId.Should().Be("rId1");
        current.MasterDisplayName.Should().Be("Master 1");
        current.PlaceholderCount.Should().Be(0);
        current.DisplayOrder.Should().Be(0);
        current.AutomationId.Should().Be("layout-rId1");
        current.Chrome.State.Should().Be(PresentationLayoutChoiceChromeState.Current);

        var blank = plan.Choices.Single(choice => choice.LayoutId == "rId2");
        blank.DisplayName.Should().Be("Blank");
        blank.LayoutType.Should().Be(SlideLayoutType.Blank);
        blank.IsCurrent.Should().BeFalse();
        blank.MasterId.Should().Be("rId1");
        blank.MasterDisplayName.Should().Be("Master 1");
        blank.PlaceholderCount.Should().Be(1);
        blank.DisplayOrder.Should().Be(1);
        blank.AutomationId.Should().Be("layout-rId2");
        blank.Chrome.State.Should().Be(PresentationLayoutChoiceChromeState.Available);
        blank.ThumbnailPlaceholders.Should().ContainSingle(slot =>
            slot.PlaceholderType == PlaceholderType.Title &&
            slot.Bounds.Width > 0 &&
            slot.Bounds.Height > 0);
    }

    [Fact]
    public void BuildLayoutPickerPlan_PreservesOrderAndMasterEvidenceForDuplicateNamedLayouts()
    {
        var editor = MakeSession(out var presentation);
        var secondMaster = new SlideMaster { Id = "rIdMaster2" };
        presentation.Masters.Add(secondMaster);

        presentation.Layouts.Add(new SlideLayout
        {
            Id = "rId2",
            Name = "Title Slide",
            LayoutType = SlideLayoutType.Title,
            MasterId = secondMaster.Id,
            Placeholders =
            {
                new SlideShape { Id = 10, Placeholder = new Placeholder { Type = PlaceholderType.Title } },
                new SlideShape { Id = 11, Placeholder = new Placeholder { Type = PlaceholderType.Body } },
            }
        });
        presentation.Layouts.Add(new SlideLayout
        {
            Id = "rId3",
            Name = string.Empty,
            LayoutType = SlideLayoutType.TwoContent,
            MasterId = presentation.Masters[0].Id,
            Placeholders =
            {
                new SlideShape { Id = 12, Placeholder = new Placeholder { Type = PlaceholderType.Title } },
            }
        });
        presentation.Layouts.Add(new SlideLayout
        {
            Id = "rId3",
            Name = "Duplicate relationship id",
            LayoutType = SlideLayoutType.Blank,
            MasterId = presentation.Masters[0].Id
        });

        var plan = PresentationDesignCommandPlanner.BuildLayoutPickerPlan(
            presentation,
            editor.CurrentSlideIndex);

        plan.Choices.Select(choice => choice.LayoutId).Should().Equal("rId1", "rId2", "rId3");
        plan.Choices.Select(choice => choice.DisplayOrder).Should().Equal(0, 1, 2);
        plan.Groups.Select(group => group.Heading).Should().Equal("Master 1", "Master 2");
        plan.Groups[0].Choices.Select(choice => choice.LayoutId).Should().Equal("rId1", "rId3");
        plan.Groups[1].Choices.Select(choice => choice.LayoutId).Should().Equal("rId2");
        plan.Choices[1].LayoutId.Should().Be("rId2");
        plan.Choices[1].DisplayName.Should().Be("Title Slide");
        plan.Choices[1].LayoutType.Should().Be(SlideLayoutType.Title);
        plan.Choices[1].MasterId.Should().Be("rIdMaster2");
        plan.Choices[1].MasterDisplayName.Should().Be("Master 2");
        plan.Choices[1].PlaceholderCount.Should().Be(2);
        plan.Choices[1].DisplayOrder.Should().Be(1);
        plan.Choices[2].LayoutId.Should().Be("rId3");
        plan.Choices[2].DisplayName.Should().Be("Two Content");
        plan.Choices[2].LayoutType.Should().Be(SlideLayoutType.TwoContent);
        plan.Choices[2].MasterId.Should().Be("rId1");
        plan.Choices[2].MasterDisplayName.Should().Be("Master 1");
        plan.Choices[2].PlaceholderCount.Should().Be(1);
        plan.Choices[2].DisplayOrder.Should().Be(2);
    }

    [Theory]
    [InlineData(false, 0, "Title and Content\nMaster 1 - 0 placeholders")]
    [InlineData(false, 1, "Title and Content\nMaster 1 - 1 placeholder")]
    [InlineData(false, 2, "Title and Content\nMaster 1 - 2 placeholders")]
    [InlineData(true, 2, "Current - Title and Content\nMaster 1 - 2 placeholders")]
    public void LayoutChoiceDisplayLabel_ProjectsCurrentStateAndPlaceholderCount(
        bool isCurrent,
        int placeholderCount,
        string expected)
    {
        var choice = new PresentationLayoutChoice(
            "rId1",
            "Title and Content",
            SlideLayoutType.TitleContent,
            isCurrent,
            "rIdMaster1",
            "Master 1",
            placeholderCount,
            0);

        choice.DisplayLabel.Should().Be(expected);
    }

    [Fact]
    public void BuildLayoutPickerPlan_ReportsChoicesButDisablesApplyWithoutCurrentSlide()
    {
        var editor = MakeSession(out var presentation);

        var plan = PresentationDesignCommandPlanner.BuildLayoutPickerPlan(
            presentation,
            currentSlideIndex: editor.CurrentSlideIndex + presentation.Slides.Count);

        plan.HasCurrentSlide.Should().BeFalse();
        plan.CanApply.Should().BeFalse();
        plan.CurrentLayoutId.Should().BeNull();
        plan.Choices.Should().ContainSingle();
        plan.Choices[0].IsCurrent.Should().BeFalse();
        plan.Choices[0].Chrome.State.Should().Be(PresentationLayoutChoiceChromeState.Disabled);
        plan.Choices[0].Chrome.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BuildLayoutPickerPlan_AddsFallbackPowerPointStyleThumbnailsForKnownLayouts()
    {
        var editor = MakeSession(out var presentation);
        presentation.Layouts.Add(new SlideLayout
        {
            Id = "rId2",
            LayoutType = SlideLayoutType.TwoContent,
            MasterId = presentation.Masters[0].Id
        });
        presentation.Layouts.Add(new SlideLayout
        {
            Id = "rId3",
            LayoutType = SlideLayoutType.Blank,
            MasterId = presentation.Masters[0].Id
        });

        var plan = PresentationDesignCommandPlanner.BuildLayoutPickerPlan(
            presentation,
            editor.CurrentSlideIndex);

        var twoContent = plan.Choices.Single(choice => choice.LayoutId == "rId2");
        twoContent.ThumbnailPlaceholders.Should().HaveCount(3);
        twoContent.ThumbnailPlaceholders.Select(slot => slot.PlaceholderType)
            .Should()
            .Equal(PlaceholderType.Title, PlaceholderType.Body, PlaceholderType.Body);
        plan.Choices.Single(choice => choice.LayoutId == "rId3").ThumbnailPlaceholders
            .Should()
            .BeEmpty("PowerPoint's blank layout thumbnail is intentionally empty");
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

        choice.Should().NotBeNull();
        choice!.LayoutId.Should().Be("rId2");
        choice.DisplayName.Should().Be("Two Content");
        choice.LayoutType.Should().Be(SlideLayoutType.TwoContent);
        choice.IsCurrent.Should().BeFalse();
        choice.MasterId.Should().Be("rId1");
        choice.MasterDisplayName.Should().Be("Master 1");
        choice.PlaceholderCount.Should().Be(0);
        choice.DisplayOrder.Should().Be(1);
        choice.Chrome.State.Should().Be(PresentationLayoutChoiceChromeState.Available);
        editor.CurrentSlide!.LayoutId.Should().Be("rId2");

        editor.Undo();
        editor.CurrentSlide.LayoutId.Should().Be("rId1");
    }

    [Fact]
    public void TryApplyLayoutChoice_ReconcilesPlaceholderGeometryAndAddsMissingPlaceholders()
    {
        var editor = MakeSession(out var presentation);
        var title = editor.CurrentSlide!.Shapes.Single(shape =>
            shape.Placeholder?.Type == PlaceholderType.Title);
        title.OffsetXEmu = 100;
        title.OffsetYEmu = 200;
        title.ExtentCxEmu = 300;
        title.ExtentCyEmu = 400;
        title.Text = "Authored title";

        presentation.Layouts.Add(new SlideLayout
        {
            Id = "rId2",
            Name = "Title and Content",
            LayoutType = SlideLayoutType.TitleContent,
            MasterId = presentation.Masters[0].Id,
            Placeholders =
            {
                new SlideShape
                {
                    Id = 10,
                    Name = "Title Placeholder",
                    Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
                    OffsetXEmu = 1_000,
                    OffsetYEmu = 2_000,
                    ExtentCxEmu = 3_000,
                    ExtentCyEmu = 4_000,
                },
                new SlideShape
                {
                    Id = 11,
                    Name = "Content Placeholder",
                    Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
                    OffsetXEmu = 5_000,
                    OffsetYEmu = 6_000,
                    ExtentCxEmu = 7_000,
                    ExtentCyEmu = 8_000,
                },
            }
        });

        PresentationDesignCommandPlanner.TryApplyLayoutChoice(editor, "rId2", out _)
            .Should().BeTrue();

        title.OffsetXEmu.Should().Be(1_000);
        title.OffsetYEmu.Should().Be(2_000);
        title.ExtentCxEmu.Should().Be(3_000);
        title.ExtentCyEmu.Should().Be(4_000);
        title.PlainText.Should().Be("Authored title");
        editor.CurrentSlide.Shapes.Count(shape =>
            shape.Placeholder is not null &&
            shape.Placeholder.Type == PlaceholderType.Body &&
            shape.OffsetXEmu == 5_000 && shape.ExtentCyEmu == 8_000)
            .Should().Be(1);

        editor.Undo();
        title.OffsetXEmu.Should().Be(100);
        title.ExtentCxEmu.Should().Be(300);
        editor.CurrentSlide.Shapes.Count(shape =>
            shape.Placeholder is not null && shape.Placeholder.Type == PlaceholderType.Body)
            .Should().Be(0);

        editor.Redo();
        title.OffsetXEmu.Should().Be(1_000);
        editor.CurrentSlide.Shapes.Count(shape =>
            shape.Placeholder is not null && shape.Placeholder.Type == PlaceholderType.Body)
            .Should().Be(1);
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
    public void TryApply_SetSlideBackgroundCommand_IsUndoableAndResetRestoresInheritance()
    {
        var editor = MakeSession(out var presentation);
        var slide = presentation.Slides[0];

        PresentationDesignCommandPlanner.TryPlan("freep.background-blue", out var blue).Should().BeTrue();
        PresentationDesignCommandPlanner.TryApply(editor, blue).Should().BeTrue();
        slide.Background.Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0xD9EAF7));

        editor.Undo();
        slide.Background.Should().BeNull();

        PresentationDesignCommandPlanner.TryApply(editor, blue).Should().BeTrue();
        PresentationDesignCommandPlanner.TryPlan("freep.background-reset", out var reset).Should().BeTrue();
        PresentationDesignCommandPlanner.TryApply(editor, reset).Should().BeTrue();
        slide.Background.Should().BeNull();
    }

    [Fact]
    public void SetSlideBackgroundCommand_PersistsThroughPptxRoundTrip()
    {
        var editor = MakeSession(out var presentation);
        PresentationDesignCommandPlanner.TryPlan("freep.background-blue", out var plan).Should().BeTrue();
        PresentationDesignCommandPlanner.TryApply(editor, plan).Should().BeTrue();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var reopened = PptxPackageReader.Read(stream);
        reopened.Slides[0].Background.Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0xD9EAF7));
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
