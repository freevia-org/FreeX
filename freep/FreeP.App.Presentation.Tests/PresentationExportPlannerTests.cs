using System.Text;
using Free.Shared.Pdf;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationExportPlannerTests
{
    private static Presentation BuildHandoutDeck(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        for (var i = 1; i <= slideCount; i++)
        {
            var slide = new Slide { Title = $"Slide {i}" };
            slide.Shapes.Add(new SlideShape
            {
                Kind = SlideShapeKind.AutoShape,
                Text = $"Body {i}",
            });
            presentation.Slides.Add(slide);
        }

        presentation.Properties.Title = "Handout Deck";
        presentation.Properties.Author = "Parity";
        return presentation;
    }

    private static Presentation BuildNotesDeck()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        for (var i = 1; i <= 3; i++)
        {
            var slide = new Slide { Title = $"Slide {i}" };
            slide.Shapes.Add(new SlideShape
            {
                Kind = SlideShapeKind.AutoShape,
                Text = $"Body {i}",
            });
            presentation.Slides.Add(slide);
        }

        presentation.Slides[0].Notes = MakeTextBody("Opening note.");
        presentation.Slides[2].Notes = MakeTextBody("First closing note.", "Second closing note.");
        presentation.Properties.Title = "Notes Deck";
        presentation.Properties.Author = "Parity";
        return presentation;
    }

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
    public void NotesPagePreviewPlan_UsesCurrentSlideNotesPageRangeAndGeometry()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Title = "Opening" });
        presentation.Slides.Add(new Slide { Title = "Financial review" });
        presentation.Slides[1].Notes = MakeTextBody("Mention revenue growth.", "Pause for questions.");

        var plan = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 1);

        plan.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
        plan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(2);
        plan.SlideIndex.Should().Be(1);
        plan.SlideNumber.Should().Be(2);
        plan.SlideTitle.Should().Be("Financial review");
        plan.HasNotes.Should().BeTrue();
        plan.NotesText.Should().Be($"Mention revenue growth.{Environment.NewLine}Pause for questions.");
        plan.NoteLines.Should().Equal("Mention revenue growth.", "Pause for questions.");
        plan.SlideBounds.Top.Should().BeGreaterThan(plan.PageBounds.Top);
        plan.NotesBounds.Top.Should().BeGreaterThan(plan.SlideBounds.Bottom);
        plan.NotesBounds.Bottom.Should().BeLessThanOrEqualTo(plan.PageBounds.Bottom);
    }

    [Fact]
    public void NotesPagePreviewPlan_UsesModeledSlideAspectRatio()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.SlideSizeCxEmu = 9_144_000;
        presentation.SlideSizeCyEmu = 6_858_000;
        presentation.Slides[0].Title = "Standard 4:3";
        presentation.Slides[0].Notes = MakeTextBody("Speaker note");

        var plan = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 0);

        (plan.SlideBounds.Width / plan.SlideBounds.Height)
            .Should()
            .BeApproximately(4d / 3d, 0.001, "notes-page thumbnails must match the deck slide size, not always 16:9");
        plan.NotesBounds.Top.Should().BeGreaterThan(plan.SlideBounds.Bottom);
        plan.NotesText.Should().Be("Speaker note");
    }

    [Fact]
    public void NotesPagePreviewPlan_EmptyDeckProducesNoSlidePlan()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var plan = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 4);

        plan.HasSlide.Should().BeFalse();
        plan.HasNotes.Should().BeFalse();
        plan.SlideTitle.Should().Be(PresentationNotesPagePreviewPlanner.EmptyDeckTitle);
        plan.PrintPlan.SlideRange.DisplayName.Should().Be("No slides");
        plan.NoteLines.Should().BeEmpty();
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
    public void HandoutPdfRenderPlan_ThreeSlidesPerPage_EmitsThumbnailsAndWritingLines()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.Handouts,
            new PresentationSlideRangeRequest(PresentationSlideRangeKind.CustomRange, StartSlideNumber: 2, EndSlideNumber: 4),
            HandoutSlidesPerPage: 3);

        var plan = PresentationHandoutPdfExporter.BuildRenderPlan(
            BuildHandoutDeck(5),
            new PresentationHandoutPdfExportRequest(request));

        plan.LayoutPlan.PrintPlan.Layout.SlidesPerPage.Should().Be(3);
        plan.LayoutPlan.Pages[0].Slots.Select(slot => slot.SlideNumber).Should().Equal(2, 3, 4);
        plan.Pages.Should().ContainSingle();
        plan.Pages[0].WidthPoints.Should().Be(PresentationExportPlanner.DefaultPrintPageWidth);
        plan.Pages[0].HeightPoints.Should().Be(PresentationExportPlanner.DefaultPrintPageHeight);

        var texts = plan.Pages[0].Ops.OfType<PdfText>().Select(text => text.Text).ToList();
        texts.Should().Contain(["Slide 2", "Slide 3", "Slide 4"]);
        texts.Should().NotContain("Slide 1");
        texts.Should().NotContain("Slide 5");
        plan.Pages[0].Ops.OfType<PdfLine>().Should().HaveCount(15);
        plan.Pages[0].Ops.OfType<PdfStrokeRect>().Should().HaveCount(3);
    }

    [Fact]
    public void HandoutPdfRenderPlan_SixSlidesPerPage_PaginatesSelectedSlidesWithoutWritingLines()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.Handouts,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.SelectedSlides,
                SelectedSlideNumbers: [8, 1, 4, 4, 2, 9, 7]),
            HandoutSlidesPerPage: 6);

        var plan = PresentationHandoutPdfExporter.BuildRenderPlan(
            BuildHandoutDeck(8),
            new PresentationHandoutPdfExportRequest(request));

        plan.LayoutPlan.PageCount.Should().Be(1);
        plan.LayoutPlan.Pages[0].Slots.Select(slot => slot.SlideNumber).Should().Equal(1, 2, 4, 7, 8);
        plan.Pages[0].Ops.OfType<PdfLine>().Should().BeEmpty();
        plan.Pages[0].Ops.OfType<PdfStrokeRect>().Should().HaveCount(5);
        plan.Pages[0].Ops.OfType<PdfText>().Select(text => text.Text)
            .Should()
            .Contain(["Slide 1", "Slide 2", "Slide 4", "Slide 7", "Slide 8"])
            .And
            .NotContain("Slide 3");
    }

    [Fact]
    public void HandoutPdfExporter_ProducesPortablePdfBytesAndMetadata()
    {
        var bytes = PresentationHandoutPdfExporter.ExportToBytes(
            BuildHandoutDeck(2),
            new PresentationHandoutPdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                HandoutSlidesPerPage: 2)));

        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        Encoding.Latin1.GetString(bytes).Should().Contain("%%EOF");

        var doc = PresentationHandoutPdfExporter.BuildDocument(BuildHandoutDeck(2));
        doc.Properties!.Creator.Should().Be("FreeP");
        doc.Properties.Title.Should().Be("Handout Deck");
        doc.Properties.Author.Should().Be("Parity");
    }

    [Fact]
    public void NotesPagePdfRenderPlan_SelectedSlides_UsesSharedPreviewGeometryAndSpeakerNotes()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.NotesPages,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.SelectedSlides,
                SelectedSlideNumbers: [3, 1, 3]));

        var plan = PresentationNotesPagePdfExporter.BuildRenderPlan(
            BuildNotesDeck(),
            new PresentationNotesPagePdfExportRequest(request));

        plan.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
        plan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(1, 3);
        plan.PreviewPlans.Select(preview => preview.SlideNumber).Should().Equal(1, 3);
        plan.Pages.Should().HaveCount(2);
        plan.Pages.Should().OnlyContain(page =>
            page.WidthPoints == PresentationExportPlanner.DefaultPrintPageWidth &&
            page.HeightPoints == PresentationExportPlanner.DefaultPrintPageHeight);

        var firstPageText = plan.Pages[0].Ops.OfType<PdfText>().Select(text => text.Text).ToList();
        firstPageText.Should().Contain(["Slide 1", "Body 1", "Opening note."]);
        firstPageText.Should().NotContain("Slide 2");
        plan.Pages[0].Ops.OfType<PdfStrokeRect>().Should().HaveCount(2);

        var secondPageText = plan.Pages[1].Ops.OfType<PdfText>().Select(text => text.Text).ToList();
        secondPageText.Should().Contain(["Slide 3", "First closing note.", "Second closing note."]);
        secondPageText.Should().NotContain("Slide 1");
        plan.Pages[1].Ops.OfType<PdfStrokeRect>().Should().HaveCount(2);
    }

    [Fact]
    public void NotesPagePdfRenderPlan_AllSlidesRange_ExportsOneNotesPagePerSlideForWholeDeck()
    {
        // IA1: the Avalonia notes-page PDF export must cover the whole deck (AllSlides), matching
        // the WPF host, which passes no range (null) to FileCommands.ExportNotesPagePdf and so
        // defaults to AllSlides via PresentationExportPlanner.BuildSlideRangePlan. Both requests
        // must therefore produce the same number of pages for the same deck.
        var deck = BuildNotesDeck();

        var allSlidesPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(
            deck,
            new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides))));

        var wpfHostEquivalentPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(
            deck,
            new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                SlideRange: null)));

        allSlidesPlan.PreviewPlans.Should().HaveCount(deck.Slides.Count);
        allSlidesPlan.PreviewPlans.Select(preview => preview.SlideNumber).Should().Equal(1, 2, 3);
        allSlidesPlan.Pages.Should().HaveCount(deck.Slides.Count);

        allSlidesPlan.PrintPlan.SlideRange.SlideNumbers
            .Should().Equal(wpfHostEquivalentPlan.PrintPlan.SlideRange.SlideNumbers);
        allSlidesPlan.Pages.Should().HaveCount(wpfHostEquivalentPlan.Pages.Count);
    }

    [Fact]
    public void NotesPagePdfExportPlan_ExposesSharedCommandAndCurrentSlideRange()
    {
        var plan = PresentationExportPlanner.BuildNotesPagePdfExportPlan(
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CurrentSlide,
                CurrentSlideNumber: 2),
            slideCount: 3);

        plan.Format.Should().Be(PresentationExportFormat.NotesPagePdf);
        plan.CommandId.Should().Be(PresentationExportPlanner.NotesPagePdfExportCommandId);
        plan.DisplayName.Should().Be("Notes Page PDF");
        plan.DefaultExtensionWithDot.Should().Be(PresentationExportPlanner.PdfExportExtension);
        plan.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
        plan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(2);
        plan.IsImplemented.Should().BeTrue();
        plan.CanExecute.Should().BeTrue();
        plan.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void NotesPagePdfExportPlan_EmptyDeckDisablesExecutionButPreservesIntent()
    {
        var plan = PresentationExportPlanner.BuildNotesPagePdfExportPlan(null, slideCount: 0);

        plan.CommandId.Should().Be(PresentationExportPlanner.NotesPagePdfExportCommandId);
        plan.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
        plan.PrintPlan.SlideRange.DisplayName.Should().Be("No slides");
        plan.IsImplemented.Should().BeTrue();
        plan.CanExecute.Should().BeFalse();
        plan.DisabledReason.Should().Be("Notes-page PDF export requires at least one slide.");
    }

    [Fact]
    public void NotesPagePdfRenderPlan_EmptyNotesAndEmptyDeck_EmitPowerPointShapedPlaceholderPages()
    {
        var deck = BuildNotesDeck();

        var slideWithoutNotes = PresentationNotesPagePdfExporter.BuildRenderPlan(
            deck,
            new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CurrentSlide,
                    CurrentSlideNumber: 2))));

        slideWithoutNotes.PreviewPlans.Should().ContainSingle(preview =>
            preview.SlideNumber == 2 &&
            !preview.HasNotes);
        slideWithoutNotes.Pages[0].Ops.OfType<PdfText>().Select(text => text.Text)
            .Should()
            .Contain(PresentationNotesPagePreviewPlanner.EmptyNotesPlaceholder);

        var empty = Presentation.CreateEmpty();
        empty.Slides.Clear();
        var emptyPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(empty);

        emptyPlan.PrintPlan.SlideRange.DisplayName.Should().Be("No slides");
        emptyPlan.PreviewPlans.Should().ContainSingle(preview => !preview.HasSlide);
        emptyPlan.Pages.Should().ContainSingle();
        emptyPlan.Pages[0].Ops.OfType<PdfText>().Select(text => text.Text)
            .Should()
            .Contain(PresentationNotesPagePreviewPlanner.EmptyNotesPlaceholder);
    }

    [Fact]
    public void NotesPagePdfRenderPlan_LongSingleLine_WrapsToNotesBoxWidthWithoutOverrunning()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Title = "Roadmap" });
        var longLine = string.Join(
            " ",
            Enumerable.Range(1, 40).Select(i => $"word{i}"));
        presentation.Slides[0].Notes = MakeTextBody(longLine);

        var plan = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 0);

        plan.NoteLines.Count.Should().BeGreaterThan(1, "a long single line must be wrapped into multiple lines");
        plan.NoteLines.Should().OnlyContain(line => line.Length > 0);
        foreach (var line in plan.NoteLines)
        {
            var estimatedWidth = line.Length * 12 * 0.55;
            estimatedWidth.Should().BeLessThanOrEqualTo(
                plan.NotesBounds.Width,
                "each wrapped line must fit within the notes box width, not overrun the right edge");
        }

        // Re-joining the wrapped lines must reproduce every original word (no silent word loss).
        string.Join(" ", plan.NoteLines).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().Equal(longLine.Split(' '));
    }

    [Fact]
    public void NotesPagePdfRenderPlan_VeryLongNotes_ContinuesOverflowOntoASubsequentPage()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Title = "Deep dive" });
        var paragraphs = Enumerable.Range(1, 60)
            .Select(i => $"Speaker note line number {i} with enough words to be realistic.")
            .ToArray();
        presentation.Slides[0].Notes = MakeTextBody(paragraphs);

        var renderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);

        renderPlan.PreviewPlans.Should().ContainSingle();
        var noteLines = renderPlan.PreviewPlans[0].NoteLines;
        noteLines.Count.Should().Be(60, "each paragraph fits on one line at this length, so no extra wrap lines are expected");

        // The notes box cannot hold 60 lines, so the export must continue onto a following page
        // instead of silently dropping the remaining lines (PowerPoint continues overflow notes
        // onto additional pages).
        renderPlan.Pages.Count.Should().BeGreaterThan(1, "overflowing notes must continue onto a subsequent page, not be dropped");

        var allPageText = renderPlan.Pages
            .SelectMany(page => page.Ops.OfType<PdfText>())
            .Select(text => text.Text)
            .ToList();

        allPageText.Should().Contain("Speaker note line number 1 with enough words to be realistic.");
        allPageText.Should().Contain(
            "Speaker note line number 60 with enough words to be realistic.",
            "the last note line must appear on a continuation page rather than being dropped");

        // Every page for this slide's notes repeats the slide thumbnail/border for context.
        renderPlan.Pages.Should().OnlyContain(page => page.Ops.OfType<PdfStrokeRect>().Count() == 2);
    }

    [Fact]
    public void NotesPagePdfExporter_ProducesPortablePdfBytesAndMetadata()
    {
        var bytes = PresentationNotesPagePdfExporter.ExportToBytes(BuildNotesDeck());

        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        Encoding.Latin1.GetString(bytes).Should().Contain("%%EOF");

        var doc = PresentationNotesPagePdfExporter.BuildDocument(BuildNotesDeck());
        doc.Properties!.Creator.Should().Be("FreeP");
        doc.Properties.Title.Should().Be("Notes Deck");
        doc.Properties.Author.Should().Be("Parity");
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
        video.CanExecute.Should().BeFalse();
        video.DisabledReason.Should().Be(PresentationExportPlanner.VideoExportDeferredMessage);
        video.Quality.Quality.Should().Be(PresentationVideoQualityKind.FullHd);
        video.Quality.WidthPx.Should().Be(1920);
        video.Quality.HeightPx.Should().Be(1080);
        video.SecondsPerSlide.Should().Be(PresentationExportPlanner.DefaultVideoSecondsPerSlide);
        video.UseRecordedTimings.Should().BeTrue();
        video.IncludeNarration.Should().BeTrue();
        video.EstimatedDuration.Should().Be(TimeSpan.FromSeconds(15));
        video.SlideRange.SlideNumbers.Should().Equal(image.SlideRange.SlideNumbers);
    }

    [Fact]
    public void VideoExportPlan_NormalizesPowerPointWorkflowOptionsAndEmptyDeckState()
    {
        var request = new PresentationVideoExportRequest(
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.SelectedSlides,
                SelectedSlideNumbers: [5, 2, 2, 99, 1]),
            PresentationVideoQualityKind.UltraHd,
            SecondsPerSlide: 0.2,
            UseRecordedTimings: false,
            IncludeNarration: false);

        var plan = PresentationExportPlanner.BuildVideoExportPlan(request, slideCount: 5);
        var empty = PresentationExportPlanner.BuildVideoExportPlan(request, slideCount: 0);

        plan.Format.Should().Be(PresentationExportFormat.Video);
        plan.CommandId.Should().Be(PresentationExportPlanner.VideoExportCommandId);
        plan.Description.Should().Contain("PowerPoint-style MP4 export workflow");
        plan.QualityOptions.Select(option => option.Quality)
            .Should()
            .Equal(
                PresentationVideoQualityKind.UltraHd,
                PresentationVideoQualityKind.FullHd,
                PresentationVideoQualityKind.Hd,
                PresentationVideoQualityKind.Standard);
        plan.Quality.DisplayName.Should().Be("Ultra HD (4K)");
        plan.Quality.WidthPx.Should().Be(3840);
        plan.Quality.HeightPx.Should().Be(2160);
        plan.SecondsPerSlide.Should().Be(1);
        plan.UseRecordedTimings.Should().BeFalse();
        plan.IncludeNarration.Should().BeFalse();
        plan.SlideRange.SlideNumbers.Should().Equal(1, 2, 5);
        plan.SlideRange.DisplayName.Should().Be("Slides 1, 2, 5");
        plan.EstimatedDuration.Should().Be(TimeSpan.FromSeconds(3));
        plan.IsImplemented.Should().BeFalse();
        plan.CanExecute.Should().BeFalse();
        plan.DisabledReason.Should().Be(PresentationExportPlanner.VideoExportDeferredMessage);

        empty.SlideRange.DisplayName.Should().Be("No slides");
        empty.EstimatedDuration.Should().Be(TimeSpan.Zero);
        empty.DisabledReason.Should().Be("Video export requires at least one slide.");
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

    private static TextBody MakeTextBody(params string[] paragraphs)
    {
        var body = new TextBody();
        foreach (var text in paragraphs)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = text });
            body.Paragraphs.Add(paragraph);
        }

        return body;
    }
}
