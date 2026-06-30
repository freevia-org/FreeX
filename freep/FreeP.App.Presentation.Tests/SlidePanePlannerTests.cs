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
}
