using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlidePanePlannerTests
{
    [Fact]
    public void BuildEntries_WithoutSections_ReturnsSlideRowsOnly()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" }
        };

        var entries = SlidePanePlanner.BuildEntries(slides, Array.Empty<PresentationSection>());

        entries.Should().Equal(
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 0, "1"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 1, "2"));
    }

    [Fact]
    public void BuildEntries_InsertsSectionHeadersBeforeFirstMemberSlide()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" },
            new Slide { Id = "rId4" }
        };
        var intro = new PresentationSection { Name = "Intro" };
        intro.SlideIds.Add("rId2");
        var body = new PresentationSection { Name = "Body" };
        body.SlideIds.Add("rId3");
        body.SlideIds.Add("rId4");

        var entries = SlidePanePlanner.BuildEntries(slides, new[] { intro, body });

        entries.Should().Equal(
            new SlidePaneEntry(SlidePaneEntryKind.SectionHeader, 0, "Intro  (1)", 1),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 0, "1"),
            new SlidePaneEntry(SlidePaneEntryKind.SectionHeader, 1, "Body  (2)", 2),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 1, "2"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 2, "3"));
    }

    [Fact]
    public void BuildEntries_UsesEarliestKnownMemberAndIgnoresUnknownSlideIds()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" },
            new Slide { Id = "rId4" }
        };
        var section = new PresentationSection { Name = "Mixed" };
        section.SlideIds.Add("missing");
        section.SlideIds.Add("rId4");
        section.SlideIds.Add("rId3");

        var entries = SlidePanePlanner.BuildEntries(slides, new[] { section });

        entries.Should().Equal(
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 0, "1"),
            new SlidePaneEntry(SlidePaneEntryKind.SectionHeader, 1, "Mixed  (2)", 2),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 1, "2"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 2, "3"));
    }

    [Fact]
    public void DefaultThumbnailMetrics_UseSixteenByNineSlideAspect()
    {
        SlidePanePlanner.DefaultThumbnailWidth.Should().Be(150.0);
        SlidePanePlanner.DefaultThumbnailHeight.Should().BeApproximately(84.375, 0.0001);
    }

    [Fact]
    public void HitTestInsertionPoint_SkipsNonSlideRows()
    {
        var layout = new[] { false, true, true, false };

        SlidePanePlanner.HitTestInsertionPoint(layout, y: 10, slideItemHeight: 100).Should().Be(0);
        SlidePanePlanner.HitTestInsertionPoint(layout, y: 90, slideItemHeight: 100).Should().Be(1);
        SlidePanePlanner.HitTestInsertionPoint(layout, y: 190, slideItemHeight: 100).Should().Be(2);
    }

    [Fact]
    public void ComputeInsertionIndicatorOffset_AccumulatesRowsBeforeTargetSlide()
    {
        var layout = new[] { false, true, true, false };

        SlidePanePlanner.ComputeInsertionIndicatorOffset(layout, 0, slideItemHeight: 100).Should().Be(0);
        SlidePanePlanner.ComputeInsertionIndicatorOffset(layout, 1, slideItemHeight: 100).Should().Be(130);
        SlidePanePlanner.ComputeInsertionIndicatorOffset(layout, 2, slideItemHeight: 100).Should().Be(230);
    }

    [Fact]
    public void BuildContextActions_ForValidSlide_ReturnsSharedMenuOrderAndTargets()
    {
        var actions = SlidePanePlanner.BuildContextActions(slideCount: 3, slideIndex: 1);

        actions.Should().Equal(
            new SlidePaneActionPlan(SlidePaneActionKind.InsertAfterSlide, "New Slide", 1, 2, true),
            new SlidePaneActionPlan(SlidePaneActionKind.DuplicateSlide, "Duplicate Slide", 1, 2, true),
            new SlidePaneActionPlan(SlidePaneActionKind.DeleteSlide, "Delete Slide", 1, 1, true));
    }

    [Fact]
    public void BuildContextActions_DisablesInvalidAndSingleSlideDeletes()
    {
        var singleSlideActions = SlidePanePlanner.BuildContextActions(slideCount: 1, slideIndex: 0);
        singleSlideActions.Single(a => a.Kind == SlidePaneActionKind.DeleteSlide).IsEnabled.Should().BeFalse();

        var invalidActions = SlidePanePlanner.BuildContextActions(slideCount: 2, slideIndex: 5);
        invalidActions.Should().OnlyContain(a => !a.IsEnabled);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(0, 2, true)]
    [InlineData(2, 0, true)]
    [InlineData(2, 3, false)]
    [InlineData(2, 4, false)]
    public void PlanMoveAction_EnablesOnlyRealInsertionMoves(
        int sourceSlideIndex,
        int targetInsertionIndex,
        bool expectedEnabled)
    {
        var action = SlidePanePlanner.PlanMoveAction(3, sourceSlideIndex, targetInsertionIndex);

        action.Kind.Should().Be(SlidePaneActionKind.MoveSlide);
        action.SourceSlideIndex.Should().Be(sourceSlideIndex);
        action.TargetSlideIndex.Should().Be(targetInsertionIndex);
        action.IsEnabled.Should().Be(expectedEnabled);
    }

    [Fact]
    public void TryApplyAction_Duplicate_UsesSharedSelectionAndCommandRouting()
    {
        var editor = CreateEditingSession(2);
        var action = SlidePanePlanner.BuildContextActions(2, 0)
            .Single(a => a.Kind == SlidePaneActionKind.DuplicateSlide);

        SlidePanePlanner.TryApplyAction(editor, action).Should().BeTrue();

        editor.Presentation.Slides.Should().HaveCount(3);
        editor.CurrentSlideIndex.Should().Be(1);
    }

    [Fact]
    public void TryApplyAction_Move_UsesSharedSelectionAndCommandRouting()
    {
        var editor = CreateEditingSession(3);
        var action = SlidePanePlanner.PlanMoveAction(3, sourceSlideIndex: 0, targetInsertionIndex: 3);

        SlidePanePlanner.TryApplyAction(editor, action).Should().BeTrue();

        editor.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("Slide 2", "Slide 3", "Slide 1");
        editor.CurrentSlideIndex.Should().Be(2);
    }

    [Fact]
    public void TryApplyAction_DisabledAction_DoesNotMutateSlides()
    {
        var editor = CreateEditingSession(1);
        var action = SlidePanePlanner.BuildContextActions(1, 0)
            .Single(a => a.Kind == SlidePaneActionKind.DeleteSlide);

        SlidePanePlanner.TryApplyAction(editor, action).Should().BeFalse();

        editor.Presentation.Slides.Should().HaveCount(1);
        editor.CurrentSlideIndex.Should().Be(0);
    }

    private static EditingSession CreateEditingSession(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        for (var i = 0; i < slideCount; i++)
        {
            presentation.Slides.Add(new Slide
            {
                Id = $"slide{i + 1}",
                Title = $"Slide {i + 1}",
            });
        }

        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }
}
