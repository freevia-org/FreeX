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
    public void HandoutLayoutPlan_ThreeSlidesPerPage_AddsPowerPointStyleWritingLines()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.Handouts,
            new PresentationSlideRangeRequest(PresentationSlideRangeKind.CustomRange,  StartSlideNumber: 2, EndSlideNumber: 4),
            HandoutSlidesPerPage: 3);

        var plan = PresentationExportPlanner.BuildHandoutLayoutPlan(
            request,
            slideCount: 5,
            slideWidth: 16,
            slideHeight: 9);

        plan.PrintPlan.Layout.SlidesPerPage.Should().Be(3);
        plan.PageWidth.Should().Be(PresentationExportPlanner.DefaultPrintPageWidth);
        plan.PageHeight.Should().Be(PresentationExportPlanner.DefaultPrintPageHeight);
        plan.PageCount.Should().Be(1);
        plan.Pages.Should().ContainSingle();
        plan.Pages[0].Slots.Select(slot => slot.SlideNumber).Should().Equal(2, 3, 4);
        plan.Pages[0].Slots.Select(slot => slot.SlideIndex).Should().Equal(1, 2, 3);
        plan.Pages[0].Slots.Should().OnlyContain(slot => slot.NotesOrLinesBounds != null);
        plan.Pages[0].Slots.Should().OnlyContain(slot => slot.BlankLineBounds.Count == 5);
        plan.Pages[0].Slots[0].SlideBounds.Right
            .Should()
            .BeLessThan(plan.Pages[0].Slots[0].NotesOrLinesBounds!.Value.Left);
        plan.Pages[0].Slots[0].SlideBounds.Width.Should().BeGreaterThan(0);
        plan.Pages[0].Slots[0].SlideBounds.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void HandoutLayoutPlan_SixSlidesPerPage_PaginatesAndMapsSlidesWithoutNotesLines()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.Handouts,
            HandoutSlidesPerPage: 6);

        var plan = PresentationExportPlanner.BuildHandoutLayoutPlan(request, slideCount: 8);

        plan.PrintPlan.Layout.SlidesPerPage.Should().Be(6);
        plan.PageCount.Should().Be(2);
        plan.Pages[0].Slots.Should().HaveCount(6);
        plan.Pages[1].Slots.Should().HaveCount(2);
        plan.Pages.SelectMany(page => page.Slots).Select(slot => slot.SlideNumber)
            .Should()
            .Equal(1, 2, 3, 4, 5, 6, 7, 8);
        plan.Pages.SelectMany(page => page.Slots).Should().OnlyContain(slot => slot.NotesOrLinesBounds == null);
        plan.Pages.SelectMany(page => page.Slots).Should().OnlyContain(slot => slot.BlankLineBounds.Count == 0);
        plan.Pages[0].Slots[0].SlideBounds.Top.Should().BeLessThan(plan.Pages[0].Slots[2].SlideBounds.Top);
        plan.Pages[0].Slots[0].SlideBounds.Left.Should().BeLessThan(plan.Pages[0].Slots[1].SlideBounds.Left);
    }

    [Fact]
    public void HandoutLayoutPlan_NormalizesUnsupportedSlidesPerPageToNearestOption()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.Handouts,
            HandoutSlidesPerPage: 8);

        var plan = PresentationExportPlanner.BuildHandoutLayoutPlan(request, slideCount: 9);

        plan.PrintPlan.Layout.SlidesPerPage.Should().Be(9);
        plan.PageCount.Should().Be(1);
        plan.Pages[0].Slots.Should().HaveCount(9);
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
    public void ImageAndVideoPlansShareRangePolicyButOnlyImagesExecute()
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
        image.IsImplemented.Should().BeTrue();
        image.WidthPx.Should().Be(PresentationImageExportExecutor.DefaultWidthPx);
        image.HeightPx.Should().Be(PresentationImageExportExecutor.DefaultHeightPx);
        image.SlideRange.SlideNumbers.Should().Equal(2, 3, 4);

        video.Format.Should().Be(PresentationExportFormat.Video);
        video.CommandId.Should().Be(PresentationExportPlanner.VideoExportCommandId);
        video.DefaultExtensionWithDot.Should().Be(".mp4");
        video.IsImplemented.Should().BeFalse();
        video.SlideRange.SlideNumbers.Should().Equal(image.SlideRange.SlideNumbers);
    }

    [Fact]
    public void ImageExportExecutor_ExportsSelectedSlidesWithSharedNamingAndHostRenderCallback()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"freep-image-export-{Guid.NewGuid():N}");
        try
        {
            var presentation = Presentation.CreateEmpty();
            presentation.Slides.Clear();
            presentation.Slides.Add(new Slide { Title = "One" });
            presentation.Slides.Add(new Slide { Title = "Two" });
            presentation.Slides.Add(new Slide { Title = "Three" });
            var calls = new List<(int SlideIndex, int Width, int Height)>();
            var range = new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.SelectedSlides,
                SelectedSlideNumbers: [3, 1, 3]);

            var result = PresentationImageExportExecutor.Export(
                presentation,
                new PresentationImageExportRequest(
                    outputDirectory,
                    BaseFileName: "Quarter Review.pptx",
                    SlideRange: range,
                    WidthPx: 320,
                    HeightPx: 180),
                (deck, slideIndex, width, height) =>
                {
                    deck.Should().BeSameAs(presentation);
                    calls.Add((slideIndex, width, height));
                    return [0x89, 0x50, 0x4E, 0x47, (byte)slideIndex];
                });

            result.Succeeded.Should().BeTrue();
            result.Plan.CommandId.Should().Be(PresentationExportPlanner.ImageExportCommandId);
            result.Plan.SlideRange.DisplayName.Should().Be("Slides 1, 3");
            result.ExportedSlides.Select(s => s.FileName)
                .Should()
                .Equal("Quarter Review-slide-01.png", "Quarter Review-slide-03.png");
            result.ExportedSlides.Select(s => s.SlideIndex).Should().Equal(0, 2);
            calls.Should().Equal((0, 320, 180), (2, 320, 180));
            foreach (var exported in result.ExportedSlides)
                File.ReadAllBytes(exported.Path).Should().HaveCount(5);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void ImageExportExecutor_EmptyDeckCreatesNoImagesButReturnsImplementedPlan()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"freep-image-export-empty-{Guid.NewGuid():N}");
        try
        {
            var presentation = Presentation.CreateEmpty();
            presentation.Slides.Clear();

            var result = PresentationImageExportExecutor.Export(
                presentation,
                new PresentationImageExportRequest(outputDirectory),
                (_, _, _, _) => throw new InvalidOperationException("No slides should render."));

            result.Succeeded.Should().BeTrue();
            result.Plan.IsImplemented.Should().BeTrue();
            result.Plan.SlideRange.DisplayName.Should().Be("No slides");
            result.ExportedSlides.Should().BeEmpty();
            Directory.EnumerateFiles(outputDirectory).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
