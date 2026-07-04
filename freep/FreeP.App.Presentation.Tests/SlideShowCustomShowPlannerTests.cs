using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowCustomShowPlannerTests
{
    [Fact]
    public void BuildCustomShowRoute_UsesNamedOrderedSlideIds()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "Appendix");
        var sequence = new SlideShowCustomSlideSequence(
            "Executive review",
            new[]
            {
                presentation.Slides[2].Id,
                presentation.Slides[0].Id
            });

        var route = SlideShowCustomShowPlanner.BuildCustomShowRoute(
            presentation,
            sequence,
            startIndex: 8);

        route.CustomShowName.Should().Be("Executive review");
        route.Slides.Select(slide => slide.Title).Should().Equal("Appendix", "Intro");
        route.SourceSlideIndices.Should().Equal(2, 0);
        route.StartIndex.Should().Be(1);
    }

    [Fact]
    public void TryBuildNamedCustomShowRoute_SelectsShowByNameCaseInsensitively()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "Appendix");
        var sequences = new[]
        {
            new SlideShowCustomSlideSequence(
                "Training",
                new[] { presentation.Slides[0].Id }),
            new SlideShowCustomSlideSequence(
                "Executive Review",
                new[] { presentation.Slides[1].Id, presentation.Slides[2].Id })
        };

        var found = SlideShowCustomShowPlanner.TryBuildNamedCustomShowRoute(
            presentation,
            sequences,
            "executive review",
            startIndex: 0,
            out var route);

        found.Should().BeTrue();
        route.CustomShowName.Should().Be("Executive Review");
        route.Slides.Select(slide => slide.Title).Should().Equal("Deep dive", "Appendix");
        route.SourceSlideIndices.Should().Equal(1, 2);
    }

    [Fact]
    public void TryBuildNamedCustomShowRoute_SelectsStoredPresentationCustomShow()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "Appendix");
        var customShow = new PresentationCustomShow { Name = "Board review" };
        customShow.SlideIds.Add(presentation.Slides[2].Id);
        customShow.SlideIds.Add("missing-slide");
        customShow.SlideIds.Add(presentation.Slides[0].Id);
        presentation.CustomShows.Add(customShow);

        var found = SlideShowCustomShowPlanner.TryBuildNamedCustomShowRoute(
            presentation,
            "board REVIEW",
            startIndex: 4,
            out var route);

        found.Should().BeTrue();
        route.CustomShowName.Should().Be("Board review");
        route.Slides.Select(slide => slide.Title).Should().Equal("Appendix", "Intro");
        route.SourceSlideIndices.Should().Equal(2, 0);
        route.StartIndex.Should().Be(1);
    }

    [Fact]
    public void BuildCustomShowRoute_AcceptsStoredPresentationCustomShow()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "Appendix");
        var customShow = new PresentationCustomShow { Name = "Training" };
        customShow.SlideIds.Add(presentation.Slides[1].Id);

        var route = SlideShowCustomShowPlanner.BuildCustomShowRoute(
            presentation,
            customShow);

        route.CustomShowName.Should().Be("Training");
        route.Slides.Select(slide => slide.Title).Should().Equal("Deep dive");
        route.SourceSlideIndices.Should().Equal(1);
    }

    [Fact]
    public void TryBuildNamedCustomShowRoute_FallsBackToFullDeckWhenNameIsMissing()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "Appendix");

        var found = SlideShowCustomShowPlanner.TryBuildNamedCustomShowRoute(
            presentation,
            Array.Empty<SlideShowCustomSlideSequence>(),
            "Missing",
            startIndex: 2,
            out var route);

        found.Should().BeFalse();
        route.CustomShowName.Should().BeNull();
        route.Slides.Should().Equal(presentation.Slides);
        route.SourceSlideIndices.Should().Equal(0, 1, 2);
        route.StartIndex.Should().Be(2);
    }

    [Fact]
    public void BuildLaunchPlan_ListsFullCurrentAndCustomShowChoices()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "Appendix");
        var customShow = new PresentationCustomShow { Name = "Executive review" };
        customShow.SlideIds.Add(presentation.Slides[2].Id);
        customShow.SlideIds.Add("missing-slide");
        customShow.SlideIds.Add(presentation.Slides[0].Id);
        presentation.CustomShows.Add(customShow);
        presentation.CustomShows.Add(new PresentationCustomShow { Name = "Empty show" });

        var plan = SlideShowCustomShowPlanner.BuildLaunchPlan(presentation, currentSlideIndex: 1);

        plan.TotalSlideCount.Should().Be(3);
        plan.CurrentSlideIndex.Should().Be(1);
        plan.DefaultChoice!.ChoiceId.Should().Be(SlideShowCustomShowPlanner.FullPresentationChoiceId);
        plan.Choices.Should().HaveCount(4);
        plan.Choices[0].Should().Be(new SlideShowLaunchChoice(
            SlideShowCustomShowPlanner.FullPresentationChoiceId,
            "From Beginning",
            SlideShowLaunchChoiceKind.FullPresentation,
            SlideCount: 3,
            StartIndex: 0,
            CustomShowName: null,
            IsEnabled: true,
            DisabledReason: null));
        plan.Choices[1].Should().Be(new SlideShowLaunchChoice(
            SlideShowCustomShowPlanner.FromCurrentSlideChoiceId,
            "From Current Slide",
            SlideShowLaunchChoiceKind.FromCurrentSlide,
            SlideCount: 3,
            StartIndex: 1,
            CustomShowName: null,
            IsEnabled: true,
            DisabledReason: null));
        plan.Choices[2].Should().Be(new SlideShowLaunchChoice(
            SlideShowCustomShowPlanner.CustomShowChoicePrefix + "0",
            "Executive review",
            SlideShowLaunchChoiceKind.CustomShow,
            SlideCount: 2,
            StartIndex: 0,
            CustomShowName: "Executive review",
            IsEnabled: true,
            DisabledReason: null));
        plan.Choices[3].Should().Be(new SlideShowLaunchChoice(
            SlideShowCustomShowPlanner.CustomShowChoicePrefix + "1",
            "Empty show",
            SlideShowLaunchChoiceKind.CustomShow,
            SlideCount: 0,
            StartIndex: 0,
            CustomShowName: "Empty show",
            IsEnabled: false,
            DisabledReason: SlideShowCustomShowPlanner.EmptyCustomShowMessage));
    }

    [Fact]
    public void TryBuildRouteForLaunchChoice_UsesSharedChoicesForFullCurrentAndCustomShows()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "Appendix");
        var customShow = new PresentationCustomShow { Name = "Board review" };
        customShow.SlideIds.Add(presentation.Slides[2].Id);
        customShow.SlideIds.Add(presentation.Slides[0].Id);
        presentation.CustomShows.Add(customShow);

        var foundFull = SlideShowCustomShowPlanner.TryBuildRouteForLaunchChoice(
            presentation,
            SlideShowCustomShowPlanner.FullPresentationChoiceId,
            currentSlideIndex: 7,
            out var fullRoute);
        var foundCurrent = SlideShowCustomShowPlanner.TryBuildRouteForLaunchChoice(
            presentation,
            SlideShowCustomShowPlanner.FromCurrentSlideChoiceId,
            currentSlideIndex: 7,
            out var currentRoute);
        var foundCustom = SlideShowCustomShowPlanner.TryBuildRouteForLaunchChoice(
            presentation,
            SlideShowCustomShowPlanner.CustomShowChoicePrefix + "0",
            currentSlideIndex: 1,
            out var customRoute);

        foundFull.Should().BeTrue();
        fullRoute.CustomShowName.Should().BeNull();
        fullRoute.Slides.Select(slide => slide.Title).Should().Equal("Intro", "Deep dive", "Appendix");
        fullRoute.StartIndex.Should().Be(0);

        foundCurrent.Should().BeTrue();
        currentRoute.CustomShowName.Should().BeNull();
        currentRoute.StartIndex.Should().Be(2);

        foundCustom.Should().BeTrue();
        customRoute.CustomShowName.Should().Be("Board review");
        customRoute.Slides.Select(slide => slide.Title).Should().Equal("Appendix", "Intro");
        customRoute.SourceSlideIndices.Should().Equal(2, 0);
    }

    [Fact]
    public void BuildLaunchPlan_DisablesAllChoicesWhenDeckIsEmpty()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var plan = SlideShowCustomShowPlanner.BuildLaunchPlan(presentation, currentSlideIndex: 0);

        plan.TotalSlideCount.Should().Be(0);
        plan.DefaultChoice.Should().BeNull();
        plan.Choices.Should().HaveCount(2);
        plan.Choices.Should().OnlyContain(choice =>
            !choice.IsEnabled &&
            choice.DisabledReason == SlideShowCustomShowPlanner.NoSlidesMessage);
    }

    [Fact]
    public void BuildAuthoringPlan_ListsExistingShowsAndAvailableSlidesWithoutMutatingModel()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "");
        var customShow = new PresentationCustomShow { Id = 7, Name = "  Executive review  " };
        customShow.SlideIds.Add(presentation.Slides[2].Id);
        customShow.SlideIds.Add("missing-slide");
        customShow.SlideIds.Add(presentation.Slides[0].Id);
        presentation.CustomShows.Add(customShow);

        var plan = SlideShowCustomShowPlanner.BuildAuthoringPlan(presentation);

        plan.AvailableSlides.Select(slide => (slide.Index, slide.SlideId, slide.Title)).Should().Equal(
            (0, presentation.Slides[0].Id, "Intro"),
            (1, presentation.Slides[1].Id, "Deep dive"),
            (2, presentation.Slides[2].Id, "Slide 3"));
        var summary = plan.CustomShows.Should().ContainSingle().Subject;
        summary.Index.Should().Be(0);
        summary.Id.Should().Be(7);
        summary.Name.Should().Be("Executive review");
        summary.SlideIds.Should().Equal(presentation.Slides[2].Id, presentation.Slides[0].Id);
        presentation.CustomShows[0].Name.Should().Be("  Executive review  ");
        presentation.CustomShows[0].SlideIds.Should().Equal(
            presentation.Slides[2].Id,
            "missing-slide",
            presentation.Slides[0].Id);
    }

    [Fact]
    public void CreateCustomShow_ValidatesNameAllocatesIdAndNormalizesSlides()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "Appendix");
        presentation.CustomShows.Add(new PresentationCustomShow { Id = 1, Name = "Training" });
        presentation.CustomShows.Add(new PresentationCustomShow { Id = 3, Name = "Board review" });

        var result = SlideShowCustomShowPlanner.CreateCustomShow(
            presentation,
            "  Executive review  ",
            new[]
            {
                presentation.Slides[2].Id,
                "missing-slide",
                " ",
                presentation.Slides[0].Id
            });

        result.Succeeded.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.CustomShowIndex.Should().Be(2);
        result.CustomShow.Should().BeSameAs(presentation.CustomShows[2]);
        result.CustomShow!.Id.Should().Be(2);
        result.CustomShow.Name.Should().Be("Executive review");
        result.CustomShow.SlideIds.Should().Equal(presentation.Slides[2].Id, presentation.Slides[0].Id);
    }

    [Fact]
    public void CreateCustomShow_RejectsBlankAndDuplicateNamesCaseInsensitively()
    {
        var presentation = MakePresentation("Intro");
        presentation.CustomShows.Add(new PresentationCustomShow { Name = "Executive review" });

        var blank = SlideShowCustomShowPlanner.CreateCustomShow(
            presentation,
            " ",
            Array.Empty<string>());
        var duplicate = SlideShowCustomShowPlanner.CreateCustomShow(
            presentation,
            " executive REVIEW ",
            Array.Empty<string>());

        blank.Succeeded.Should().BeFalse();
        blank.ErrorMessage.Should().Be(SlideShowCustomShowPlanner.EmptyCustomShowNameMessage);
        duplicate.Succeeded.Should().BeFalse();
        duplicate.ErrorMessage.Should().Be(SlideShowCustomShowPlanner.DuplicateCustomShowNameMessage);
        presentation.CustomShows.Should().ContainSingle();
    }

    [Fact]
    public void RenameDeleteAndUpdateCustomShowSlides_MutateExistingShowsOnly()
    {
        var presentation = MakePresentation("Intro", "Deep dive", "Appendix");
        var customShow = new PresentationCustomShow { Id = 9, Name = "Training" };
        customShow.SlideIds.Add(presentation.Slides[0].Id);
        presentation.CustomShows.Add(customShow);

        var rename = SlideShowCustomShowPlanner.RenameCustomShow(
            presentation,
            0,
            "  Executive review  ");
        var updateSlides = SlideShowCustomShowPlanner.UpdateCustomShowSlides(
            presentation,
            0,
            new[] { presentation.Slides[2].Id, "missing-slide", presentation.Slides[1].Id });
        var missing = SlideShowCustomShowPlanner.UpdateCustomShowSlides(
            presentation,
            5,
            Array.Empty<string>());

        rename.Succeeded.Should().BeTrue();
        rename.CustomShow!.Id.Should().Be(9);
        rename.CustomShow.Name.Should().Be("Executive review");
        updateSlides.Succeeded.Should().BeTrue();
        presentation.CustomShows[0].SlideIds.Should().Equal(presentation.Slides[2].Id, presentation.Slides[1].Id);
        missing.Succeeded.Should().BeFalse();
        missing.ErrorMessage.Should().Be(SlideShowCustomShowPlanner.MissingCustomShowMessage);

        var delete = SlideShowCustomShowPlanner.DeleteCustomShow(presentation, 0);

        delete.Succeeded.Should().BeTrue();
        delete.CustomShow!.Name.Should().Be("Executive review");
        presentation.CustomShows.Should().BeEmpty();
    }

    private static Presentation MakePresentation(params string[] titles)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        foreach (var title in titles)
        {
            presentation.Slides.Add(new Slide { Title = title });
        }

        return presentation;
    }
}
