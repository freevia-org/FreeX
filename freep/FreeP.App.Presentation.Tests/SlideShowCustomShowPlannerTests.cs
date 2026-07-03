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
