using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationExportPlannerTests
{
    [Fact]
    public void PrintLayouts_CoverSlidesNotesAndPowerPointHandoutOptions()
    {
        var descriptors = PresentationExportPlanner.BuildPrintLayoutDescriptors();

        descriptors.Should().ContainSingle(layout =>
            layout.Layout == PresentationPrintLayoutKind.FullPageSlides &&
            layout.SlidesPerPage == 1 &&
            !layout.IncludesSpeakerNotes &&
            !layout.IsHandout);
        descriptors.Should().ContainSingle(layout =>
            layout.Layout == PresentationPrintLayoutKind.NotesPages &&
            layout.SlidesPerPage == 1 &&
            layout.IncludesSpeakerNotes &&
            !layout.IsHandout);
        descriptors
            .Where(layout => layout.Layout == PresentationPrintLayoutKind.Handouts)
            .Select(layout => layout.SlidesPerPage)
            .Should()
            .Equal(1, 2, 3, 4, 6, 9);
    }

    [Fact]
    public void PrintPlan_NormalizesHandoutSlidesPerPageAndSelectedSlideRange()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.Handouts,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.SelectedSlides,
                SelectedSlideNumbers: [4, 2, 99, 2, 0]),
            HandoutSlidesPerPage: 5,
            PrintHiddenSlides: true);

        var plan = PresentationExportPlanner.BuildPrintPlan(request, slideCount: 6);

        plan.CommandId.Should().Be(PresentationExportPlanner.PrintCommandId);
        plan.IsImplemented.Should().BeFalse();
        plan.PrintHiddenSlides.Should().BeTrue();
        plan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.Handouts);
        plan.Layout.SlidesPerPage.Should().Be(4);
        plan.Layout.IsHandout.Should().BeTrue();
        plan.Layout.IncludesSpeakerNotes.Should().BeFalse();
        plan.SlideRange.Kind.Should().Be(PresentationSlideRangeKind.SelectedSlides);
        plan.SlideRange.SlideNumbers.Should().Equal(2, 4);
        plan.SlideRange.DisplayName.Should().Be("Slides 2, 4");
    }

    [Fact]
    public void PrintPlan_NotesPagesPreserveNotesIntentAndClampCustomRange()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.NotesPages,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CustomRange,
                StartSlideNumber: 7,
                EndSlideNumber: 3));

        var plan = PresentationExportPlanner.BuildPrintPlan(request, slideCount: 5);

        plan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
        plan.Layout.IncludesSpeakerNotes.Should().BeTrue();
        plan.Layout.IsHandout.Should().BeFalse();
        plan.SlideRange.SlideNumbers.Should().Equal(3, 4, 5);
        plan.SlideRange.DisplayName.Should().Be("Slides 3-5");
    }

    [Fact]
    public void SlideRangePlan_NormalizesCurrentAndEmptyDeckRequests()
    {
        var current = PresentationExportPlanner.BuildSlideRangePlan(
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CurrentSlide,
                CurrentSlideNumber: 99),
            slideCount: 3);
        var empty = PresentationExportPlanner.BuildSlideRangePlan(null, slideCount: 0);

        current.SlideNumbers.Should().Equal(3);
        current.DisplayName.Should().Be("Slide 3");
        empty.SlideNumbers.Should().BeEmpty();
        empty.DisplayName.Should().Be("No slides");
    }

    [Fact]
    public void DeferredImageAndVideoPlansShareRangePolicy()
    {
        var range = new PresentationSlideRangeRequest(
            PresentationSlideRangeKind.CustomRange,
            StartSlideNumber: 2,
            EndSlideNumber: 4);

        var image = PresentationExportPlanner.BuildImageExportPlan(range, slideCount: 5);
        var video = PresentationExportPlanner.BuildVideoExportPlan(range, slideCount: 5);

        image.Format.Should().Be(PresentationExportFormat.ImageSequence);
        image.CommandId.Should().Be(PresentationExportPlanner.ImageExportCommandId);
        image.DefaultExtensionWithDot.Should().Be(".png");
        image.IsImplemented.Should().BeFalse();
        image.SlideRange.SlideNumbers.Should().Equal(2, 3, 4);

        video.Format.Should().Be(PresentationExportFormat.Video);
        video.CommandId.Should().Be(PresentationExportPlanner.VideoExportCommandId);
        video.DefaultExtensionWithDot.Should().Be(".mp4");
        video.IsImplemented.Should().BeFalse();
        video.SlideRange.SlideNumbers.Should().Equal(image.SlideRange.SlideNumbers);
    }
}
