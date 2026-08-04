using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Free.Shared.Drawing;
using Free.Shared.Pdf;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationExportPlannerTests
{
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xFF, 0xFF, 0x3F,
        0x00, 0x05, 0xFE, 0x02, 0xFE, 0xDC, 0xCC, 0x59,
        0xE7, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    [Fact]
    public void BackstageExportPlan_exposes_only_actions_with_real_export_routes()
    {
        var plan = PresentationExportPlanner.BuildBackstageExportPlan();
        var actions = plan.FixedLayoutActions.Concat(plan.DeferredActions).ToArray();

        actions.Select(action => action.CommandId).Should().BeEquivalentTo(
            PresentationExportPlanner.PdfExportCommandId,
            PresentationExportPlanner.NotesPagePdfExportCommandId,
            PresentationExportPlanner.ImageExportCommandId,
            PresentationExportPlanner.VideoExportCommandId);
        actions.Should().NotContain(action => action.Format == PresentationExportFormat.Print,
            "Print has its own Backstage pane and is not an export action");
    }

    [Fact]
    public void BackstageExportPlan_ReflectsHostVideoCapability()
    {
        var deferred = PresentationExportPlanner.BuildBackstageExportPlan();
        deferred.DeferredActions.Single(action =>
                action.Format == PresentationExportFormat.Video)
            .IsEnabled.Should().BeFalse();

        var available = PresentationExportPlanner.BuildBackstageExportPlan(
            videoExportAvailable: true);
        available.DeferredActions.Single(action =>
                action.Format == PresentationExportFormat.Video)
            .IsEnabled.Should().BeTrue();
    }

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

    private static Presentation BuildEllipseDeck()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        var slide = new Slide { Title = "Ellipse evidence" };
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ellipse,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x70AD47)),
            Outline = new ShapeOutline.Visible(SrgbColor.FromRgb(0x2F5597), widthPt: 1.75),
            Text = "Oval callout",
        });
        slide.Notes = MakeTextBody("Ellipse notes evidence.");
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static Presentation BuildCustomGeometryDeck()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        var path = new CustomGeometryPath { PathW = 100, PathH = 100, Fill = true, Stroke = true };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 50, Y: 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        var shape = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x70AD47)),
            Outline = new ShapeOutline.Visible(SrgbColor.FromRgb(0x2F5597), widthPt: 1.75),
            Text = "Freeform",
        };
        shape.CustomGeometry.Add(path);

        var slide = new Slide { Title = "Custom geometry evidence" };
        slide.Shapes.Add(shape);
        slide.Notes = MakeTextBody("Custom geometry notes evidence.");
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static Presentation BuildEffectDeck()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        var slide = new Slide { Title = "Effect evidence" };
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x70AD47)),
            Effects = new ShapeEffects
            {
                HasOuterShadow = true,
                OuterShadowColor = SrgbColor.FromRgb(0x222222),
                OuterShadowAlpha = 96,
                OuterShadowDistEmu = DrawingMlCoordinateUnits.PointsToEmu(12),
                OuterShadowDirDeg = 0,
            },
            Text = "Shadowed",
        });
        slide.Notes = MakeTextBody("Effect notes evidence.");
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static Presentation BuildTransparentShapeDeck()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        var slide = new Slide { Title = "Shape opacity evidence" };
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4), alpha: 128)),
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(SrgbColor.FromRgb(0xC00000), alpha: 64),
                widthPt: 1.5),
            Text = "Transparent",
        });
        slide.Notes = MakeTextBody("Shape opacity notes evidence.");
        presentation.Slides.Add(slide);
        return presentation;
    }

    private static Presentation BuildGradientDeck()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        var slide = new Slide
        {
            Title = "Gradient evidence",
            Background = new ShapeFill.Gradient(
                [
                    new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0x10, 0x20, 0x30))),
                    new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0xD0, 0xE0, 0xF0))),
                ],
                GradientKind.Linear,
                angleDegrees: 0),
        };
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            OffsetYEmu = DrawingMlCoordinateUnits.PointsToEmu(90),
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(144),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Fill = new ShapeFill.Gradient(
                [
                    new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0x44, 0x72, 0xC4))),
                    new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0x70, 0xAD, 0x47))),
                ],
                GradientKind.Linear,
                angleDegrees: 90),
            Text = "Gradient shape",
        });
        slide.Notes = MakeTextBody("Gradient notes evidence.");
        presentation.Slides.Add(slide);
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
            PrintHiddenSlides: true,
            Copies: 1000,
            Collate: false,
            ColorMode: PresentationPrintColorMode.Grayscale,
            FrameSlides: true,
            IncludeCommentsAndInkMarkup: true);

        var plan = PresentationExportPlanner.BuildPrintPlan(request, slideCount: 6);

        plan.CommandId.Should().Be(PresentationExportPlanner.PrintCommandId);
        plan.IsImplemented.Should().BeTrue();
        plan.PrintHiddenSlides.Should().BeTrue();
        plan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.Handouts);
        plan.Layout.SlidesPerPage.Should().Be(4);
        plan.Layout.IsHandout.Should().BeTrue();
        plan.Layout.IncludesSpeakerNotes.Should().BeFalse();
        plan.SlideRange.Kind.Should().Be(PresentationSlideRangeKind.SelectedSlides);
        plan.SlideRange.SlideNumbers.Should().Equal(2, 4);
        plan.SlideRange.DisplayName.Should().Be("Slides 2, 4");
        plan.Options.Copies.Should().Be(999);
        plan.Options.Collate.Should().BeFalse();
        plan.Options.ColorMode.Should().Be(PresentationPrintColorMode.Grayscale);
        plan.Options.FrameSlides.Should().BeTrue();
        plan.Options.IncludeCommentsAndInkMarkup.Should().BeTrue();
        plan.Options.DisplaySummary.Should().Be(
            "999 copies, Uncollated, Grayscale, Print hidden slides, Frame slides, Print comments and ink markup");
        plan.Options.SummaryLines.Should().Equal(
            "999 copies",
            "Uncollated",
            "Grayscale",
            "Print hidden slides",
            "Frame slides",
            "Print comments and ink markup");
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
    public void CustomSlideRangeParser_ExpandsRangesPreservesOrderAndDeduplicates()
    {
        var result = PresentationExportPlanner.ParseCustomSlideRange("5, 2-4; 3", slideCount: 6);

        result.IsValid.Should().BeTrue();
        result.SlideNumbers.Should().Equal(5, 2, 3, 4);
        result.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("2, 7")]
    [InlineData("4-2")]
    [InlineData("1-")]
    public void CustomSlideRangeParser_RejectsInvalidInput(string text)
    {
        var result = PresentationExportPlanner.ParseCustomSlideRange(text, slideCount: 6);

        result.IsValid.Should().BeFalse();
        result.SlideNumbers.Should().BeEmpty();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CustomSlideRangeText_FlowsThroughHiddenSlideFilteringAndPackageValidation()
    {
        var presentation = BuildHandoutDeck(6);
        presentation.Slides[2].IsHidden = true;
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.NotesPages,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CustomRange,
                CustomRangeText: "2-5"));

        var plan = PresentationExportPlanner.BuildPrintPlan(request, presentation);
        var package = PresentationPrintOutputPackageExecutor.BuildPackagePlan(request, presentation);

        plan.SlideRange.SlideNumbers.Should().Equal(2, 4, 5);
        plan.SlideRange.CustomRangeText.Should().Be("2-5");
        plan.SlideRange.ValidationMessage.Should().BeNull();
        package.PrintPlan.SlideRange.CustomRangeText.Should().Be("2-5");
        package.SlideRangeSummary.Should().Be("Slides 2, 4, 5");
        package.CanBuildPackage.Should().BeTrue();
    }

    [Fact]
    public void InvalidCustomSlideRange_DisablesPackageWithValidationMessage()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.FullPageSlides,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CustomRange,
                CustomRangeText: "2, 9"));

        var package = PresentationPrintOutputPackageExecutor.BuildPackagePlan(request, slideCount: 4);

        package.CanBuildPackage.Should().BeFalse();
        package.SlideRangeSummary.Should().Be("Invalid custom range");
        package.DisabledReason.Should().Be("Slide '9' is outside slides 1-4.");
    }

    [Fact]
    public void PresentationAwarePrintPlan_ExcludesHiddenSlidesUnlessRequested()
    {
        var presentation = BuildHandoutDeck(4);
        presentation.Slides[1].IsHidden = true;
        presentation.Slides[3].IsHidden = true;

        var hiddenExcluded = PresentationExportPlanner.BuildPrintPlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
            presentation);
        var hiddenIncluded = PresentationExportPlanner.BuildPrintPlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.FullPageSlides,
                PrintHiddenSlides: true),
            presentation);

        hiddenExcluded.SlideRange.SlideNumbers.Should().Equal(1, 3);
        hiddenExcluded.SlideRange.DisplayName.Should().Be("Slides 1, 3");
        hiddenIncluded.SlideRange.SlideNumbers.Should().Equal(1, 2, 3, 4);
        hiddenIncluded.SlideRange.DisplayName.Should().Be("All slides");
    }

    [Fact]
    public void PresentationAwareNotesAndHandoutPackages_UseFilteredHiddenSlideRange()
    {
        var presentation = BuildNotesDeck();
        presentation.Slides.Add(new Slide { Title = "Slide 4", IsHidden = true });

        var notes = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages),
            presentation);
        var handouts = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                HandoutSlidesPerPage: 1),
            presentation);

        notes.PrintPlan.SlideRange.SlideNumbers.Should().Equal(1, 2, 3);
        notes.PageCount.Should().Be(3);
        handouts.PrintPlan.SlideRange.SlideNumbers.Should().Equal(1, 2, 3);
        handouts.PageCount.Should().Be(3);
    }

    [Fact]
    public void PresentationAwareHandoutLayout_ExcludesHiddenSlidesUnlessRequested()
    {
        var presentation = BuildHandoutDeck(5);
        presentation.Slides[1].IsHidden = true;
        presentation.Slides[3].IsHidden = true;

        var hiddenExcluded = PresentationExportPlanner.BuildHandoutLayoutPlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                HandoutSlidesPerPage: 3),
            presentation);

        hiddenExcluded.Pages.SelectMany(page => page.Slots)
            .Select(slot => slot.SlideNumber)
            .Should().Equal(1, 3, 5);

        var withHidden = PresentationExportPlanner.BuildHandoutLayoutPlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                HandoutSlidesPerPage: 3,
                PrintHiddenSlides: true),
            presentation);

        withHidden.Pages.SelectMany(page => page.Slots)
            .Select(slot => slot.SlideNumber)
            .Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void PrintOutputPackagePlan_SelectsSharedRoutesAndBuildsNativeHandoffPlan()
    {
        var fullPage = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.FullPageSlides,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CurrentSlide,
                    CurrentSlideNumber: 2)),
            slideCount: 3);
        var notes = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages),
            slideCount: 3);
        var handouts = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                HandoutSlidesPerPage: 3),
            slideCount: 3);

        fullPage.Route.Should().Be(PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf);
        fullPage.PrintPlan.SlideRange.SlideNumbers.Should().Equal(2);
        fullPage.PageCount.Should().Be(1);
        fullPage.SlideRangeSummary.Should().Be("Slide 2");
        fullPage.LayoutSummary.Should().Be("Full Page Slides - Slide 2, 1 page");
        fullPage.Options.DisplaySummary.Should().Be("1 copy, Collated, Color");
        fullPage.PreviewPlan.PageCount.Should().Be(1);
        fullPage.PreviewPlan.PageCountText.Should().Be("1 printable page");
        fullPage.PreviewPlan.Pages.Should().ContainSingle()
            .Which.Should().Match<PresentationPrintPreviewPage>(page =>
                page.PageIndex == 0 &&
                page.PageNumber == 1 &&
                page.Kind == PresentationPrintPreviewPageKind.FullPageSlide &&
                page.SlideNumbers.SequenceEqual(new[] { 2 }) &&
                page.ThumbnailLabel == "Slide 2" &&
                page.Detail == "Full-page slide 2");
        fullPage.CanBuildPackage.Should().BeTrue();
        fullPage.NativePrinterDialogDeferred.Should().BeFalse();
        fullPage.DisabledReason.Should().BeNull();
        fullPage.PrintPlan.IsImplemented.Should().BeTrue("shared package handoff is implemented before host-specific printer execution");
        var handoff = PresentationPrintOutputPackageExecutor.BuildNativePrintHandoffPlan(fullPage, suggestedBaseFileName: "Quarter Review.pptx");
        handoff.Status.Should().Be(PresentationNativePrintHandoffStatus.PackageReadyHostHandoffRequired);
        handoff.IsPackageReady.Should().BeTrue();
        handoff.RequiresHostHandoff.Should().BeTrue();
        handoff.CanOpenNativePrintDialog.Should().BeTrue();
        handoff.SuggestedTempFileName.Should().Be("Quarter Review-print.pdf");
        handoff.Route.Should().Be(PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf);
        handoff.ContentType.Should().Be(PresentationPrintOutputPackageExecutor.PdfContentType);
        handoff.LayoutSummary.Should().Be(fullPage.LayoutSummary);
        handoff.SlideRangeSummary.Should().Be("Slide 2");
        handoff.OptionsSummary.Should().Be("1 copy, Collated, Color");
        handoff.Reason.Should().Be(PresentationPrintOutputPackageExecutor.NativePrintPackageReadyReason);
        var deferredByHost = PresentationPrintOutputPackageExecutor.BuildNativePrintHandoffPlan(
            fullPage,
            PresentationNativePrintHandoffHostCapabilities.Deferred("Unit test host", "No OS printer dialog in tests."));
        deferredByHost.Status.Should().Be(PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost);
        deferredByHost.CanOpenNativePrintDialog.Should().BeFalse();
        deferredByHost.Reason.Should().Contain("No OS printer dialog in tests.");
        notes.Route.Should().Be(PresentationPrintOutputPackageRoute.NotesPagePdf);
        notes.PageCount.Should().Be(3);
        notes.LayoutSummary.Should().Be("Notes Pages - All slides, 3 pages");
        handouts.Route.Should().Be(PresentationPrintOutputPackageRoute.HandoutPdf);
        handouts.PageCount.Should().Be(1);
        handouts.LayoutSummary.Should().Be("Handouts (3 slides per page) - All slides, 1 page");
    }

    [Fact]
    public void PrintOutputPackagePlan_WithPresentationCountsNotesOverflowContinuationPages()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Title = "Overflow notes" });
        presentation.Slides[0].Notes = MakeTextBody(
            Enumerable.Range(1, 60)
                .Select(i => $"Speaker note line number {i} with enough words to be realistic.")
                .ToArray());

        var renderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);
        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages),
            presentation);
        var backstagePlan = PresentationPrintBackstagePlanner.Build(
            new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages),
            presentation,
            currentSlideNumber: 1);

        renderPlan.Pages.Count.Should().BeGreaterThan(1);
        packagePlan.PageCount.Should().Be(renderPlan.Pages.Count);
        packagePlan.LayoutSummary.Should().Be($"Notes Pages - All slides, {renderPlan.Pages.Count} pages");
        backstagePlan.PageCount.Should().Be(renderPlan.Pages.Count);
        backstagePlan.SelectedLayout.PackagePlan.PageCount.Should().Be(renderPlan.Pages.Count);
    }

    [Fact]
    public void PrintOutputPackagePlan_ExposesPreviewMetadataForHandoutRangesAndEmptyDecks()
    {
        var handouts = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    StartSlideNumber: 2,
                    EndSlideNumber: 8),
                HandoutSlidesPerPage: 3,
                PrintHiddenSlides: true),
            slideCount: 10);
        var empty = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts, HandoutSlidesPerPage: 6),
            slideCount: 0);

        handouts.Route.Should().Be(PresentationPrintOutputPackageRoute.HandoutPdf);
        handouts.PageCount.Should().Be(3);
        handouts.SlideRangeSummary.Should().Be("Slides 2-8");
        handouts.LayoutSummary.Should().Be("Handouts (3 slides per page) - Slides 2-8, 3 pages including hidden slides");
        handouts.PreviewPlan.PageCountText.Should().Be("3 printable pages");
        handouts.PreviewPlan.Pages.Select(page => string.Join(",", page.SlideNumbers))
            .Should()
            .Equal("2,3,4", "5,6,7", "8");
        handouts.PreviewPlan.Pages.Select(page => page.Detail)
            .Should()
            .Equal(
                "Handout with slides 2, 3, 4",
                "Handout with slides 5, 6, 7",
                "Handout with slide 8");
        handouts.CanBuildPackage.Should().BeTrue();
        handouts.DisabledReason.Should().BeNull();

        empty.PageCount.Should().Be(0);
        empty.SlideRangeSummary.Should().Be("No slides");
        empty.LayoutSummary.Should().Be("Handouts (6 slides per page) - No slides, 0 pages");
        empty.PreviewPlan.CanPreview.Should().BeFalse();
        empty.PreviewPlan.DisabledReason.Should().Be("Print output requires at least one slide.");
        empty.PreviewPlan.Pages.Should().BeEmpty();
        empty.CanBuildPackage.Should().BeFalse();
        empty.DisabledReason.Should().Be("Print output requires at least one slide.");
        var emptyHandoff = PresentationPrintOutputPackageExecutor.BuildNativePrintHandoffPlan(empty);
        emptyHandoff.Status.Should().Be(PresentationNativePrintHandoffStatus.NoSlides);
        emptyHandoff.DisabledReason.Should().Be("Print output requires at least one slide.");
        emptyHandoff.IsPackageReady.Should().BeFalse();
    }

    [Fact]
    public void PrintOutputPackage_FullPageSlides_UsesRasterRendererCallbackAndWriter()
    {
        var calls = new List<(int SlideIndex, int WidthPx, int HeightPx)>();
        PdfRasterDocument? capturedDocument = null;
        var deck = BuildHandoutDeck(4);

        var package = PresentationPrintOutputPackageExecutor.BuildPackage(
            deck,
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.FullPageSlides,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    StartSlideNumber: 2,
                    EndSlideNumber: 3)),
            (_, slideIndex, widthPx, heightPx) =>
            {
                calls.Add((slideIndex, widthPx, heightPx));
                return TinyPng;
            },
            document =>
            {
                capturedDocument = document;
                return Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");
            });

        package.Plan.Route.Should().Be(PresentationPrintOutputPackageRoute.FullPageSlidesRasterPdf);
        package.Plan.ContentType.Should().Be(PresentationPrintOutputPackageExecutor.PdfContentType);
        package.Bytes.Length.Should().BeGreaterThan(20);
        Encoding.ASCII.GetString(package.Bytes, 0, 5).Should().Be("%PDF-");
        calls.Should().Equal((1, 1280, 720), (2, 1280, 720));
        capturedDocument.Should().NotBeNull();
        capturedDocument!.Pages.Should().HaveCount(2);
        capturedDocument.Pages.Should().OnlyContain(page => page.ImageBytes.SequenceEqual(TinyPng));
    }

    [Fact]
    public void PrintOutputPackage_NotesAndHandouts_RouteThroughSharedPdfExporters()
    {
        var notesPackage = PresentationPrintOutputPackageExecutor.BuildPackage(
            BuildNotesDeck(),
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.SelectedSlides,
                    SelectedSlideNumbers: [1, 3])),
            (_, _, _, _) => throw new InvalidOperationException("Notes route must not rasterize slides through the host."),
            _ => throw new InvalidOperationException("Notes route must not use the host raster PDF writer."));

        var handoutPackage = PresentationPrintOutputPackageExecutor.BuildPackage(
            BuildHandoutDeck(4),
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    StartSlideNumber: 2,
                    EndSlideNumber: 4),
                HandoutSlidesPerPage: 3),
            (_, _, _, _) => throw new InvalidOperationException("Handout route must not rasterize slides through the host."),
            _ => throw new InvalidOperationException("Handout route must not use the host raster PDF writer."));

        notesPackage.Plan.Route.Should().Be(PresentationPrintOutputPackageRoute.NotesPagePdf);
        notesPackage.Plan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(1, 3);
        notesPackage.Bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(notesPackage.Bytes, 0, 5).Should().Be("%PDF-");
        Encoding.Latin1.GetString(notesPackage.Bytes).Should().Contain("%%EOF");

        handoutPackage.Plan.Route.Should().Be(PresentationPrintOutputPackageRoute.HandoutPdf);
        handoutPackage.Plan.PrintPlan.Layout.SlidesPerPage.Should().Be(3);
        handoutPackage.Plan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(2, 3, 4);
        handoutPackage.Bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(handoutPackage.Bytes, 0, 5).Should().Be("%PDF-");
        Encoding.Latin1.GetString(handoutPackage.Bytes).Should().Contain("%%EOF");
    }

    [Fact]
    public void NotesAndHandoutPdfRenderPlans_PreserveEllipseSlideOps()
    {
        var deck = BuildEllipseDeck();

        var notesPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(deck);
        var handoutPlan = PresentationHandoutPdfExporter.BuildRenderPlan(
            deck,
            new PresentationHandoutPdfExportRequest(
                new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts, HandoutSlidesPerPage: 1)));

        notesPlan.Pages[0].Ops.OfType<PdfFillEllipse>().Should().ContainSingle();
        notesPlan.Pages[0].Ops.OfType<PdfStrokeEllipse>().Should().ContainSingle();
        handoutPlan.Pages[0].Ops.OfType<PdfFillEllipse>().Should().ContainSingle();
        handoutPlan.Pages[0].Ops.OfType<PdfStrokeEllipse>().Should().ContainSingle();
    }

    [Fact]
    public void PrintMarkupOption_ControlsCommentCalloutsAndInkStrokesOnNotesAndHandouts()
    {
        var presentation = BuildHandoutDeck(1);
        var slide = presentation.Slides[0];
        slide.Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Review this slide",
            Xemu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Yemu = DrawingMlCoordinateUnits.PointsToEmu(72),
            Idx = 1,
        });

        var ink = new SlideShape
        {
            Kind = SlideShapeKind.Ink,
            ExtentCxEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            ExtentCyEmu = DrawingMlCoordinateUnits.PointsToEmu(72),
            PreservedObject = new PreservedObjectInfo { ObjectKind = PreservedObjectKind.Ink },
        };
        const string inkXml = "<ink xmlns=\"http://www.w3.org/2003/InkML\"><traceFormat><channel name=\"X\" units=\"pt\"/><channel name=\"Y\" units=\"pt\"/></traceFormat><trace>10 10 20 20</trace></ink>";
        ink.PreservedObject.Parts["ppt/ink/ink1.xml"] = Encoding.UTF8.GetBytes(inkXml);
        ink.PreservedObject.PartContentTypes["ppt/ink/ink1.xml"] = "application/inkml+xml";
        slide.Shapes.Add(ink);

        var disabledRequest = new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts, HandoutSlidesPerPage: 1);
        var enabledRequest = disabledRequest with { IncludeCommentsAndInkMarkup = true };
        var disabledNotes = PresentationNotesPagePdfExporter.BuildRenderPlan(
            presentation,
            new PresentationNotesPagePdfExportRequest(disabledRequest));
        var enabledNotes = PresentationNotesPagePdfExporter.BuildRenderPlan(
            presentation,
            new PresentationNotesPagePdfExportRequest(enabledRequest));
        var disabledHandouts = PresentationHandoutPdfExporter.BuildRenderPlan(
            presentation,
            new PresentationHandoutPdfExportRequest(disabledRequest));
        var enabledHandouts = PresentationHandoutPdfExporter.BuildRenderPlan(
            presentation,
            new PresentationHandoutPdfExportRequest(enabledRequest));

        foreach (var page in new[] { disabledNotes.Pages[0], disabledHandouts.Pages[0] })
        {
            page.Ops.OfType<PdfFillEllipse>().Should().BeEmpty();
            page.Ops.OfType<PdfLine>().Should().BeEmpty();
            page.Ops.OfType<PdfText>().Select(text => text.Text).Should().NotContain("Review this slide");
        }

        foreach (var page in new[] { enabledNotes.Pages[0], enabledHandouts.Pages[0] })
        {
            page.Ops.OfType<PdfFillEllipse>().Should().ContainSingle();
            page.Ops.OfType<PdfLine>().Should().ContainSingle(line => line.Color == PdfColor.Black);
            page.Ops.OfType<PdfText>().Select(text => text.Text).Should().Contain("Review this slide");
        }
    }

    [Fact]
    public void NotesAndHandoutPdfRenderPlans_PreserveCustomGeometrySlideOps()
    {
        var deck = BuildCustomGeometryDeck();

        var notesPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(deck);
        var handoutPlan = PresentationHandoutPdfExporter.BuildRenderPlan(
            deck,
            new PresentationHandoutPdfExportRequest(
                new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts, HandoutSlidesPerPage: 1)));

        notesPlan.Pages[0].Ops.OfType<PdfPath>().Should().ContainSingle(path =>
            path.FillColor == new PdfColor(0x70, 0xAD, 0x47) &&
            path.StrokeColor == new PdfColor(0x2F, 0x55, 0x97) &&
            path.StrokeWidth > 0);
        handoutPlan.Pages[0].Ops.OfType<PdfPath>().Should().ContainSingle(path =>
            path.FillColor == new PdfColor(0x70, 0xAD, 0x47) &&
            path.StrokeColor == new PdfColor(0x2F, 0x55, 0x97) &&
            path.StrokeWidth > 0);
    }

    [Fact]
    public void NotesAndHandoutPdfRenderPlans_PreserveEffectOpacityGroups()
    {
        var deck = BuildEffectDeck();

        var notesPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(deck);
        var handoutPlan = PresentationHandoutPdfExporter.BuildRenderPlan(
            deck,
            new PresentationHandoutPdfExportRequest(
                new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts, HandoutSlidesPerPage: 1)));

        var notesGroup = notesPlan.Pages[0].Ops.OfType<PdfOpacityGroup>().Should().ContainSingle().Subject;
        var handoutGroup = handoutPlan.Pages[0].Ops.OfType<PdfOpacityGroup>().Should().ContainSingle().Subject;

        notesGroup.Opacity.Should().BeApproximately(96 / 255.0, 0.0001);
        handoutGroup.Opacity.Should().BeApproximately(96 / 255.0, 0.0001);
        notesGroup.Ops.OfType<PdfFillRect>().Should().ContainSingle(fill =>
            fill.Color == new PdfColor(0x22, 0x22, 0x22) &&
            fill.Width > 0 &&
            fill.Height > 0);
        handoutGroup.Ops.OfType<PdfFillRect>().Should().ContainSingle(fill =>
            fill.Color == new PdfColor(0x22, 0x22, 0x22) &&
            fill.Width > 0 &&
            fill.Height > 0);
    }

    [Fact]
    public void NotesAndHandoutPdfRenderPlans_PreserveShapeFillAndOutlineOpacityGroups()
    {
        var deck = BuildTransparentShapeDeck();

        var notesPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(deck);
        var handoutPlan = PresentationHandoutPdfExporter.BuildRenderPlan(
            deck,
            new PresentationHandoutPdfExportRequest(
                new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts, HandoutSlidesPerPage: 1)));

        var notesGroups = notesPlan.Pages[0].Ops.OfType<PdfOpacityGroup>().ToArray();
        var handoutGroups = handoutPlan.Pages[0].Ops.OfType<PdfOpacityGroup>().ToArray();

        notesGroups.Should().HaveCount(2);
        handoutGroups.Should().HaveCount(2);
        notesGroups.Should().Contain(group => Math.Abs(group.Opacity - (128 / 255.0)) < 0.0001);
        notesGroups.Should().Contain(group => Math.Abs(group.Opacity - (64 / 255.0)) < 0.0001);
        handoutGroups.Should().Contain(group => Math.Abs(group.Opacity - (128 / 255.0)) < 0.0001);
        handoutGroups.Should().Contain(group => Math.Abs(group.Opacity - (64 / 255.0)) < 0.0001);
        notesGroups.Should().Contain(group => group.Ops.OfType<PdfFillRect>().Any(fill =>
            fill.Color == new PdfColor(0x44, 0x72, 0xC4) &&
            fill.Width > 0 &&
            fill.Height > 0));
        notesGroups.Should().Contain(group => group.Ops.OfType<PdfStrokeRect>().Any(stroke =>
            stroke.Color == new PdfColor(0xC0, 0x00, 0x00) &&
            stroke.LineWidth > 0));
        handoutGroups.Should().Contain(group => group.Ops.OfType<PdfFillRect>().Any(fill =>
            fill.Color == new PdfColor(0x44, 0x72, 0xC4) &&
            fill.Width > 0 &&
            fill.Height > 0));
        handoutGroups.Should().Contain(group => group.Ops.OfType<PdfStrokeRect>().Any(stroke =>
            stroke.Color == new PdfColor(0xC0, 0x00, 0x00) &&
            stroke.LineWidth > 0));
    }

    [Fact]
    public void NotesAndHandoutPdfRenderPlans_PreserveLinearGradientSlideOps()
    {
        var deck = BuildGradientDeck();

        var notesPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(deck);
        var handoutPlan = PresentationHandoutPdfExporter.BuildRenderPlan(
            deck,
            new PresentationHandoutPdfExportRequest(
                new PresentationPrintRequest(PresentationPrintLayoutKind.Handouts, HandoutSlidesPerPage: 1)));

        var notesGradients = notesPlan.Pages[0].Ops.OfType<PdfFillRectLinearGradient>().ToArray();
        var handoutGradients = handoutPlan.Pages[0].Ops.OfType<PdfFillRectLinearGradient>().ToArray();

        notesGradients.Should().HaveCount(2);
        handoutGradients.Should().HaveCount(2);
        notesGradients[0].Gradient.Stops.Select(stop => stop.Color).Should().Equal(
            new PdfColor(0x10, 0x20, 0x30),
            new PdfColor(0xD0, 0xE0, 0xF0));
        notesGradients[0].Gradient.StartX.Should().BeLessThan(notesGradients[0].Gradient.EndX);
        notesGradients[0].Gradient.StartY.Should().BeApproximately(notesGradients[0].Gradient.EndY, 0.001);
        notesGradients[1].Gradient.StartX.Should().BeApproximately(notesGradients[1].Gradient.EndX, 0.001);
        notesGradients[1].Gradient.StartY.Should().BeGreaterThan(notesGradients[1].Gradient.EndY);
        handoutGradients.Select(op => op.FallbackColor).Should().Contain([
            new PdfColor(0x10, 0x20, 0x30),
            new PdfColor(0x44, 0x72, 0xC4),
        ]);
    }

    [Fact]
    public void PrintOutputPackageExecutionDescriptor_ValidatesAndMaterializesHostReadyPdf()
    {
        var package = PresentationPrintOutputPackageExecutor.BuildPackage(
            BuildHandoutDeck(4),
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    StartSlideNumber: 2,
                    EndSlideNumber: 4),
                HandoutSlidesPerPage: 3,
                Copies: 2,
                Collate: false),
            (_, _, _, _) => throw new InvalidOperationException("Handout route must stay shared."),
            _ => throw new InvalidOperationException("Handout route must stay shared."));
        var targetPath = Path.Combine(Path.GetTempPath(), $"freep-print-package-{Guid.NewGuid():N}.pdf");

        try
        {
            var descriptor = PresentationPrintOutputPackageExecutor.BuildExecutionDescriptor(
                package,
                PresentationNativePrintHandoffHostCapabilities.Deferred(
                    "Unit test print host",
                    "Unit tests do not open native print UI."),
                "Quarter Review.pptx");

            descriptor.PackageKind.Should().Be(PresentationPrintOutputPackageExecutor.PrintOutputPackageKind);
            descriptor.PackagePlan.Should().BeSameAs(package.Plan);
            descriptor.HandoffPlan.Status.Should().Be(PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost);
            descriptor.HandoffPlan.SuggestedDocumentName.Should().Be("Quarter Review");
            descriptor.HandoffPlan.SuggestedPrintJobName.Should().Be("Quarter Review - Handouts (3 slides per page) - Slides 2-4, 1 page");
            descriptor.SuggestedFileName.Should().Be("Quarter Review-print.pdf");
            descriptor.SuggestedDocumentName.Should().Be("Quarter Review");
            descriptor.SuggestedPrintJobName.Should().Be(descriptor.HandoffPlan.SuggestedPrintJobName);
            descriptor.ByteCount.Should().Be(package.Bytes.Length);
            descriptor.Validation.Should().Be(new PresentationPrintOutputPackageValidation(
                package.Bytes.Length,
                HasBytes: true,
                HasPdfHeader: true,
                HasPdfEofMarker: true,
                PlanCanBuildPackage: true,
                IsValid: true,
                FailureReason: null));
            descriptor.IsHostReadyPdfPackage.Should().BeTrue();
            descriptor.CanMaterialize.Should().BeTrue();
            descriptor.DisabledReason.Should().BeNull();

            var result = PresentationPrintOutputPackageExecutor.MaterializePackageForHandoff(
                package,
                targetPath,
                PresentationNativePrintHandoffHostCapabilities.Deferred(
                    "Unit test print host",
                    "Unit tests do not open native print UI."),
                "Quarter Review.pptx");

            result.Succeeded.Should().BeTrue();
            result.FailureReason.Should().BeNull();
            result.Descriptor.Validation.IsValid.Should().BeTrue();
            File.ReadAllBytes(targetPath).Should().Equal(package.Bytes);
        }
        finally
        {
            if (File.Exists(targetPath))
                File.Delete(targetPath);
        }
    }

    [Fact]
    public void PrintOutputPackageExecutionDescriptor_BlocksMaterializationForInvalidPdfBytes()
    {
        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
            slideCount: 1);
        var package = new PresentationPrintOutputPackage(packagePlan, Encoding.ASCII.GetBytes("not a pdf"));
        var targetPath = Path.Combine(Path.GetTempPath(), $"freep-invalid-print-package-{Guid.NewGuid():N}.pdf");

        var result = PresentationPrintOutputPackageExecutor.MaterializePackageForHandoff(
            package,
            targetPath,
            suggestedBaseFileName: "Broken.pptx");

        result.Succeeded.Should().BeFalse();
        result.Descriptor.CanMaterialize.Should().BeFalse();
        result.Descriptor.IsHostReadyPdfPackage.Should().BeFalse();
        result.Descriptor.Validation.IsValid.Should().BeFalse();
        result.Descriptor.Validation.FailureReason.Should().Be("Printable PDF package does not start with a PDF header.");
        result.FailureReason.Should().Be(result.Descriptor.DisabledReason);
        File.Exists(targetPath).Should().BeFalse();
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
        plan.NotesPlaceholder.Should().Be(new PresentationNotesPageNotesPlaceholder(
            PlaceholderType.Body,
            PresentationNotesPagePreviewPlanner.EmptyNotesPlaceholder,
            plan.NotesText,
            plan.NotesBounds,
            IsVisible: true,
            HasContent: true));
        plan.NotesPlaceholder.ShouldShowPlaceholder.Should().BeFalse();
        plan.SlideBounds.Top.Should().BeGreaterThan(plan.PageBounds.Top);
        plan.NotesBounds.Top.Should().BeGreaterThan(plan.SlideBounds.Bottom);
        plan.NotesBounds.Bottom.Should().BeLessThanOrEqualTo(plan.PageBounds.Bottom);
    }

    [Fact]
    public void CommentsNotesCorpus_UsesPowerPointEmptyNotesMasterPagination()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "21-comments-notes.pptx");
        var presentation = PptxPackageReader.Read(deckPath);

        presentation.NotesMasterXml.Should().NotBeNull();
        presentation.NotesMasterPlaceholders.Should().BeEmpty();

        var plans = presentation.Slides
            .Select((_, index) => PresentationNotesPagePreviewPlanner.Build(presentation, index))
            .ToArray();

        plans.Should().OnlyContain(plan => plan.UsesEmptyNativeNotesMaster);
        plans[0].RenderedPageCount.Should().Be(2);
        plans[1].RenderedPageCount.Should().Be(1);
        PresentationNotesPagePdfExporter.BuildRenderPlan(presentation).Pages.Should().HaveCount(3);
    }

    [Fact]
    public void NotesPagePreviewPlan_ExposesNotesPlaceholderStateForEmptyNotes()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Title = "Status";

        var plan = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 0);

        plan.HasNotes.Should().BeFalse();
        plan.NotesPlaceholder.Should().Be(new PresentationNotesPageNotesPlaceholder(
            PlaceholderType.Body,
            PresentationNotesPagePreviewPlanner.EmptyNotesPlaceholder,
            PresentationNotesPagePreviewPlanner.EmptyNotesPlaceholder,
            plan.NotesBounds,
            IsVisible: true,
            HasContent: false));
        plan.NotesPlaceholder.ShouldShowPlaceholder.Should().BeTrue();
    }

    [Fact]
    public void NotesPagePreviewPlan_ExposesHeaderFooterPlaceholderMetadata()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        var slide = new Slide
        {
            Title = "Roadmap",
            HfVisibility = new HfFlags
            {
                ShowDate = true,
                ShowFooter = false,
                ShowSlideNum = true,
                ShowHeader = false
            }
        };
        slide.Notes = MakeTextBody("Talk track");
        slide.Shapes.Add(MakeHeaderFooterPlaceholder(
            PlaceholderType.DateTime,
            "July 3, 2026",
            "datetime1",
            "July 3, 2026"));
        slide.Shapes.Add(MakeHeaderFooterPlaceholder(
            PlaceholderType.Footer,
            "Confidential",
            "footer",
            "Confidential"));
        presentation.Slides.Add(slide);

        var plan = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 0);

        plan.HeaderFooterPlaceholders.Select(placeholder => placeholder.Kind)
            .Should()
            .Equal(
                PresentationNotesPagePlaceholderKind.DateTime,
                PresentationNotesPagePlaceholderKind.Footer,
                PresentationNotesPagePlaceholderKind.SlideNumber);
        plan.HeaderFooterPlaceholders
            .Single(placeholder => placeholder.Kind == PresentationNotesPagePlaceholderKind.DateTime)
            .Should()
            .Match<PresentationNotesPagePlaceholder>(placeholder =>
                placeholder.Text == "July 3, 2026" &&
                placeholder.IsVisible &&
                placeholder.Bounds.Top < plan.SlideBounds.Top);
        plan.HeaderFooterPlaceholders
            .Single(placeholder => placeholder.Kind == PresentationNotesPagePlaceholderKind.Footer)
            .Should()
            .Match<PresentationNotesPagePlaceholder>(placeholder =>
                placeholder.Text == "Confidential" &&
                !placeholder.IsVisible &&
                placeholder.Bounds.Bottom <= plan.PageBounds.Bottom);
        plan.HeaderFooterPlaceholders
            .Single(placeholder => placeholder.Kind == PresentationNotesPagePlaceholderKind.SlideNumber)
            .Text.Should().Be("1", "visible slide-number intent gets deterministic metadata even before notes-master IO is modeled");
    }

    [Fact]
    public void NotesPagePreviewPlan_ResolvesUncachedDateAndSlideNumberFields()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        var slide = new Slide
        {
            Title = "Field notes",
            HfVisibility = new HfFlags
            {
                ShowDate = true,
                ShowSlideNum = true
            }
        };
        slide.Notes = MakeTextBody("Talk track");
        slide.Shapes.Add(MakeHeaderFooterPlaceholder(
            PlaceholderType.DateTime,
            string.Empty,
            "datetime3",
            cachedText: string.Empty));
        slide.Shapes.Add(MakeHeaderFooterPlaceholder(
            PlaceholderType.SlideNumber,
            string.Empty,
            "slidenum",
            cachedText: string.Empty));
        presentation.Slides.Add(slide);

        var plan = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 0);

        plan.HeaderFooterPlaceholders
            .Single(placeholder => placeholder.Kind == PresentationNotesPagePlaceholderKind.DateTime)
            .Text.Should().Be(HeaderFooterDateTimeFormatter.Format("datetime3", DateTime.Now));
        plan.HeaderFooterPlaceholders
            .Single(placeholder => placeholder.Kind == PresentationNotesPagePlaceholderKind.SlideNumber)
            .Text.Should().Be("1");
    }

    [Theory]
    [InlineData("datetime1", "7/6/2026")]
    [InlineData("datetime2", "Monday, July 6, 2026")]
    [InlineData("datetime3", "6 July 2026")]
    [InlineData("datetime4", "July 6, 2026")]
    public void NotesPagePreviewPlan_UsesAutomaticDateFieldFormat(string fieldType, string expected)
    {
        HeaderFooterDateTimeFormatter.Format(fieldType, new DateTime(2026, 7, 6))
            .Should().Be(expected);
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
    public void NotesPagePreviewPlan_UsesModeledNotesPageSize()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.NotesPageSizeCxEmu = DrawingMlCoordinateUnits.PointsToEmu(360);
        presentation.NotesPageSizeCyEmu = DrawingMlCoordinateUnits.PointsToEmu(720);
        presentation.Slides[0].Notes = MakeTextBody("Custom paper note");

        var preview = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 0);
        var renderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);

        preview.PageBounds.Width.Should().Be(360);
        preview.PageBounds.Height.Should().Be(720);
        preview.NotesBounds.Bottom.Should().BeLessThanOrEqualTo(720);
        renderPlan.Pages.Should().ContainSingle();
        renderPlan.Pages[0].WidthPoints.Should().Be(360);
        renderPlan.Pages[0].HeightPoints.Should().Be(720);
        renderPlan.PreviewPlans[0].PageBounds.Should().Be(preview.PageBounds);
    }

    [Fact]
    public void NotesPageSize_RoundTripsThroughPptxPresentationXml()
    {
        var presentation = BuildNotesDeck();
        presentation.NotesPageSizeCxEmu = 5_486_400;
        presentation.NotesPageSizeCyEmu = 7_315_200;

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var entryStream = archive.GetEntry("ppt/presentation.xml")!.Open();
            var xml = XDocument.Load(entryStream);
            var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            var notesSz = xml.Root!.Element(p + "notesSz")!;

            notesSz.Attribute("cx")!.Value.Should().Be("5486400");
            notesSz.Attribute("cy")!.Value.Should().Be("7315200");
        }

        stream.Position = 0;
        var reloaded = PptxPackageReader.Read(stream);

        reloaded.NotesPageSizeCxEmu.Should().Be(5_486_400);
        reloaded.NotesPageSizeCyEmu.Should().Be(7_315_200);
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
        plan.NotesPlaceholder.ShouldShowPlaceholder.Should().BeTrue();
        plan.NotesPlaceholder.Bounds.Should().Be(plan.NotesBounds);
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
            HandoutSlidesPerPage: 3,
            FrameSlides: true);

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
            HandoutSlidesPerPage: 6,
            FrameSlides: true);

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
    public void HandoutPdfRenderPlan_WithoutFrameSlides_OmitsSlideBorders()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.Handouts,
            HandoutSlidesPerPage: 2,
            FrameSlides: false);

        var plan = PresentationHandoutPdfExporter.BuildRenderPlan(
            BuildHandoutDeck(2),
            new PresentationHandoutPdfExportRequest(request));

        plan.Pages.Should().ContainSingle();
        plan.Pages[0].Ops.OfType<PdfStrokeRect>().Should().BeEmpty();
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
    public void RasterPdfRenderPlan_CustomRange_CallsRendererPerSlideInOrder()
    {
        var calls = new List<(int SlideIndex, int WidthPx, int HeightPx)>();
        var request = new PresentationRasterPdfExportRequest(
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CustomRange,
                StartSlideNumber: 2,
                EndSlideNumber: 3),
            WidthPx: 960);

        var plan = PresentationRasterPdfExporter.BuildRenderPlan(
            BuildHandoutDeck(4),
            request,
            (_, slideIndex, widthPx, heightPx) =>
            {
                calls.Add((slideIndex, widthPx, heightPx));
                return [(byte)(slideIndex + 1)];
            });

        plan.SlideRange.SlideNumbers.Should().Equal(2, 3);
        calls.Should().Equal((1, 960, 540), (2, 960, 540));
        plan.Pages.Should().HaveCount(2);
        plan.Pages.Select(page => page.ImageBytes[0]).Should().Equal(2, 3);
        plan.Pages.Should().OnlyContain(page => page.WidthPoints == 960 && page.HeightPoints == 540);
    }

    [Fact]
    public void RasterPdfRenderPlan_SelectedSlides_UsesExistingRangePolicyAndModeledSlideSize()
    {
        var deck = BuildHandoutDeck(5);
        deck.SlideSizeCxEmu = DrawingMlCoordinateUnits.PointsToEmu(576);
        deck.SlideSizeCyEmu = DrawingMlCoordinateUnits.PointsToEmu(432);
        var calls = new List<int>();

        var plan = PresentationRasterPdfExporter.BuildRenderPlan(
            deck,
            new PresentationRasterPdfExportRequest(
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.SelectedSlides,
                    SelectedSlideNumbers: [5, 2, 5, 99]),
                WidthPx: 1024),
            (_, slideIndex, _, _) =>
            {
                calls.Add(slideIndex);
                return TinyPng;
            });

        plan.SlideRange.SlideNumbers.Should().Equal(2, 5);
        calls.Should().Equal(1, 4);
        plan.WidthPx.Should().Be(1024);
        plan.HeightPx.Should().Be(768);
        plan.Pages.Should().HaveCount(2);
        plan.Pages.Should().OnlyContain(page => page.WidthPoints == 576 && page.HeightPoints == 432);
    }

    [Fact]
    public void RasterPdfExporter_ProducesPdfBytesThroughWriterPlan()
    {
        PdfRasterDocument? captured = null;

        var bytes = PresentationRasterPdfExporter.ExportToBytes(
            BuildHandoutDeck(2),
            request: null,
            (_, slideIndex, _, _) => [(byte)(0xA0 + slideIndex)],
            document =>
            {
                captured = document;
                return Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");
            });

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        Encoding.ASCII.GetString(bytes).Should().Contain("%%EOF");
        captured.Should().NotBeNull();
        captured!.Pages.Should().HaveCount(2);
        captured.Properties!.Creator.Should().Be("FreeP");
        captured.Properties.Title.Should().Be("Handout Deck");
    }

    [Fact]
    public void RasterPdfExporter_RichSlide_UsesRenderedRasterInsteadOfPortablePlaceholders()
    {
        var deck = Presentation.CreateEmpty();
        deck.Slides.Clear();
        var slide = new Slide { Title = "Rich slide" };
        slide.Shapes.Add(new SlideShape { Kind = SlideShapeKind.Picture });
        deck.Slides.Add(slide);

        var bytes = PresentationRasterPdfExporter.ExportToBytes(
            deck,
            request: null,
            (_, _, _, _) => Encoding.ASCII.GetBytes("rendered-rich-slide"),
            document => document.Pages.Single().ImageBytes);

        var text = Encoding.ASCII.GetString(bytes);
        text.Should().Be("rendered-rich-slide");
        text.Should().NotContain("[Picture]");
        PresentationPdfExporter.BuildDocument(deck).Pages[0].Ops.OfType<PdfText>().Select(op => op.Text)
            .Should().Contain("[Picture]");
    }

    [Fact]
    public void NotesPagePdfRenderPlan_SelectedSlides_UsesSharedPreviewGeometryAndSpeakerNotes()
    {
        var request = new PresentationPrintRequest(
            PresentationPrintLayoutKind.NotesPages,
            new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.SelectedSlides,
                SelectedSlideNumbers: [3, 1, 3]));

        var deck = BuildNotesDeck();
        var plan = PresentationNotesPagePdfExporter.BuildRenderPlan(
            deck,
            new PresentationNotesPagePdfExportRequest(request));

        plan.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
        plan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(1, 3);
        plan.PreviewPlans.Select(preview => preview.SlideNumber).Should().Equal(1, 3);
        plan.Pages.Should().HaveCount(2);
        plan.Pages.Should().OnlyContain(page =>
            page.WidthPoints == PresentationNotesPagePreviewPlanner.ResolveNotesPageWidthPoints(deck) &&
            page.HeightPoints == PresentationNotesPagePreviewPlanner.ResolveNotesPageHeightPoints(deck));

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
    public void NotesPagePdfRenderPlan_RendersVisibleHeaderFooterPlaceholdersFromSharedPlan()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        var slide = new Slide
        {
            Title = "Roadmap",
            HfVisibility = new HfFlags
            {
                ShowDate = true,
                ShowFooter = false,
                ShowSlideNum = true
            }
        };
        slide.Notes = MakeTextBody("Talk track");
        slide.Shapes.Add(MakeHeaderFooterPlaceholder(
            PlaceholderType.DateTime,
            "July 3, 2026",
            "datetime1",
            "July 3, 2026"));
        slide.Shapes.Add(MakeHeaderFooterPlaceholder(
            PlaceholderType.Footer,
            "Confidential",
            "footer",
            "Confidential"));
        presentation.Slides.Add(slide);

        var renderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);

        renderPlan.PreviewPlans.Should().ContainSingle();
        renderPlan.PreviewPlans[0].HeaderFooterPlaceholders
            .Single(placeholder => placeholder.Kind == PresentationNotesPagePlaceholderKind.Footer)
            .IsVisible.Should().BeFalse();
        var pdfText = renderPlan.Pages[0].Ops.OfType<PdfText>().Select(text => text.Text).ToArray();
        pdfText.Should().Contain("July 3, 2026");
        pdfText.Should().Contain("1");
        pdfText.Should().NotContain("Confidential");
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
    public void NotesPagePdfRenderPlan_BulletedAndNumberedNotes_PreserveListPrefixes()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Title = "Launch review" });
        var notes = new TextBody();
        notes.Paragraphs.Add(MakeParagraph("Launch checklist", paragraph =>
        {
            paragraph.BulletKind = BulletKind.Char;
            paragraph.BulletChar = "\u2022";
        }));
        notes.Paragraphs.Add(MakeParagraph("Nested risk", paragraph =>
        {
            paragraph.Level = 1;
            paragraph.BulletKind = BulletKind.Char;
            paragraph.BulletChar = "-";
        }));
        notes.Paragraphs.Add(MakeParagraph("Confirm owner", paragraph =>
        {
            paragraph.BulletKind = BulletKind.Auto;
            paragraph.AutoNumType = AutoNumType.AlphaUcParenR;
            paragraph.AutoNumStartAt = 3;
        }));
        notes.Paragraphs.Add(MakeParagraph("Publish recap", paragraph =>
        {
            paragraph.BulletKind = BulletKind.Auto;
            paragraph.AutoNumType = AutoNumType.AlphaUcParenR;
        }));
        presentation.Slides[0].Notes = notes;

        var preview = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 0);
        var renderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);

        preview.NotesText
            .Should()
            .Be($"Launch checklist{Environment.NewLine}Nested risk{Environment.NewLine}Confirm owner{Environment.NewLine}Publish recap",
                "the editable notes text remains plain while the preview lines carry render prefixes");
        preview.NoteLines.Should().Equal(
            "\u2022 Launch checklist",
            "  - Nested risk",
            "C) Confirm owner",
            "D) Publish recap");

        renderPlan.PreviewPlans[0].NoteLines.Should().Equal(preview.NoteLines);
        var pdfText = renderPlan.Pages[0].Ops.OfType<PdfText>().Select(text => text.Text).ToArray();
        pdfText.Should().Contain(preview.NoteLines);
    }

    [Fact]
    public void NotesPagePdfRenderPlan_RichSpeakerNoteRuns_PreserveStyledFacesAndColor()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Title = "Styled notes" });
        var notes = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "Normal " });
        paragraph.Runs.Add(new Run
        {
            Text = "critical",
            Bold = true,
            Color = new ThemeAwareColor(new SrgbColor(0xC0, 0x00, 0x00))
        });
        paragraph.Runs.Add(new Run { Text = " review", Italic = true });
        paragraph.Runs.Add(new Run { Text = " decision", Bold = true, Italic = true });
        paragraph.Runs.Add(new Run { Text = " closeout" });
        notes.Paragraphs.Add(paragraph);
        presentation.Slides[0].Notes = notes;

        var preview = PresentationNotesPagePreviewPlanner.Build(presentation, currentSlideIndex: 0);
        var renderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);

        preview.NotesText.Should().Be("Normal critical review decision closeout");
        preview.NoteLines.Should().Equal("Normal critical review decision closeout");
        preview.StyledNoteLines.Should().ContainSingle();
        preview.StyledNoteLines[0].Runs.Should().Equal(
            new PresentationNotesPageNoteTextRun("Normal ", Bold: false, Italic: false, Color: null),
            new PresentationNotesPageNoteTextRun(
                "critical",
                Bold: true,
                Italic: false,
                Color: new SrgbColor(0xC0, 0x00, 0x00)),
            new PresentationNotesPageNoteTextRun(" review", Bold: false, Italic: true, Color: null),
            new PresentationNotesPageNoteTextRun(" decision", Bold: true, Italic: true, Color: null),
            new PresentationNotesPageNoteTextRun(" closeout", Bold: false, Italic: false, Color: null));

        var noteTextOps = renderPlan.Pages[0].Ops
            .OfType<PdfText>()
            .Where(text => text.Text is "Normal " or "critical" or " review" or " decision" or " closeout")
            .ToArray();

        noteTextOps.Should().HaveCount(5);
        noteTextOps.Select(text => text.Text).Should().Equal("Normal ", "critical", " review", " decision", " closeout");
        noteTextOps[0].Face.Should().Be(PdfFontFace.Regular);
        noteTextOps[0].Color.Should().Be(new PdfColor(0x20, 0x20, 0x20));
        noteTextOps[1].Face.Should().Be(PdfFontFace.Bold);
        noteTextOps[1].Color.Should().Be(new PdfColor(0xC0, 0x00, 0x00));
        noteTextOps[2].Face.Should().Be(PdfFontFace.Italic);
        noteTextOps[2].Color.Should().Be(new PdfColor(0x20, 0x20, 0x20));
        noteTextOps[3].Face.Should().Be(PdfFontFace.BoldItalic);
        noteTextOps[3].Color.Should().Be(new PdfColor(0x20, 0x20, 0x20));
        noteTextOps[4].Face.Should().Be(PdfFontFace.Regular);
        noteTextOps[4].Color.Should().Be(new PdfColor(0x20, 0x20, 0x20));
        noteTextOps.Select(text => text.Y).Should().OnlyContain(y => y == noteTextOps[0].Y);
        noteTextOps[1].X.Should().BeGreaterThan(noteTextOps[0].X);
        noteTextOps[2].X.Should().BeGreaterThan(noteTextOps[1].X);
        noteTextOps[3].X.Should().BeGreaterThan(noteTextOps[2].X);
        noteTextOps[4].X.Should().BeGreaterThan(noteTextOps[3].X);
    }

    [Fact]
    public void NotesPagePdfExporter_RichSpeakerNoteItalicRuns_EmitPortablePdfFacesAndOperators()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Title = "Italic notes" });
        var notes = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "italic", Italic = true });
        paragraph.Runs.Add(new Run { Text = " bolditalic", Bold = true, Italic = true });
        notes.Paragraphs.Add(paragraph);
        presentation.Slides[0].Notes = notes;

        var bytes = PresentationNotesPagePdfExporter.ExportToBytes(presentation);
        var pdf = Encoding.ASCII.GetString(bytes);

        pdf.Should().Contain("/F3 5 0 R");
        pdf.Should().Contain("/F4 6 0 R");
        pdf.Should().Contain("/BaseFont /Helvetica-Oblique");
        pdf.Should().Contain("/BaseFont /Helvetica-BoldOblique");
        pdf.Should().Contain("/F3 12 Tf");
        pdf.Should().Contain("(italic) Tj");
        pdf.Should().Contain("/F4 12 Tf");
        pdf.Should().Contain("( bolditalic) Tj");
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

        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages),
            presentation);

        packagePlan.PreviewPlan.Pages.Should().HaveCount(renderPlan.Pages.Count);
        packagePlan.PreviewPlan.Pages[0].ThumbnailLabel.Should().Be("Slide 1 notes");
        packagePlan.PreviewPlan.Pages.Skip(1).Should().OnlyContain(page =>
            page.ThumbnailLabel == "Slide 1 notes continued" &&
            page.Detail == "Notes continuation page for slide 1");
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
        video.Storyboard.SlideRange.SlideNumbers.Should().Equal(image.SlideRange.SlideNumbers);
        video.Storyboard.Segments.Select(segment => segment.SlideNumber).Should().Equal(2, 3, 4);
        video.Storyboard.Segments.Select(segment => segment.StartTime)
            .Should()
            .Equal(TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
        video.Storyboard.Segments.Should().OnlyContain(segment =>
            segment.Duration == TimeSpan.FromSeconds(5) &&
            segment.TimingSource == PresentationVideoTimingSource.DefaultDuration);
        video.Storyboard.OutputWidthPx.Should().Be(1920);
        video.Storyboard.OutputHeightPx.Should().Be(1080);
        video.Storyboard.FrameRateHint.Should().Be(30);
        video.Storyboard.TotalDuration.Should().Be(video.EstimatedDuration);
        video.SlideRange.SlideNumbers.Should().Equal(image.SlideRange.SlideNumbers);
    }

    [Fact]
    public void VideoExportPlan_UsesHostEncoderCapabilityForExecutionState()
    {
        var host = new PresentationVideoExportHandoffHostCapabilities(
            "Windows MediaComposition",
            CanEncodeMp4: true,
            CanCaptureNarration: false,
            CanCaptureCameraAndMedia: false,
            "ready");

        var plan = PresentationExportPlanner.BuildVideoExportPlan(
            new PresentationVideoExportRequest(
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    StartSlideNumber: 2,
                    EndSlideNumber: 3)),
            slideCount: 4,
            host);

        plan.IsImplemented.Should().BeTrue();
        plan.CanExecute.Should().BeTrue();
        plan.DisabledReason.Should().BeNull();
        plan.SlideRange.SlideNumbers.Should().Equal(2, 3);
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
        plan.Storyboard.OutputWidthPx.Should().Be(3840);
        plan.Storyboard.OutputHeightPx.Should().Be(2160);
        plan.Storyboard.PixelsPerSecondHint.Should().Be(60);
        plan.Storyboard.FrameRateHint.Should().Be(60);
        plan.Storyboard.UseRecordedTimings.Should().BeFalse();
        plan.Storyboard.IncludeNarration.Should().BeFalse();
        plan.Storyboard.Segments.Select(segment => segment.SlideNumber).Should().Equal(1, 2, 5);
        plan.Storyboard.Segments.Select(segment => segment.StartTime)
            .Should()
            .Equal(TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        plan.Storyboard.Segments.Should().OnlyContain(segment =>
            segment.Duration == TimeSpan.FromSeconds(1) &&
            segment.TimingSource == PresentationVideoTimingSource.DefaultDuration);
        plan.IsImplemented.Should().BeFalse();
        plan.CanExecute.Should().BeFalse();
        plan.DisabledReason.Should().Be(PresentationExportPlanner.VideoExportDeferredMessage);

        empty.SlideRange.DisplayName.Should().Be("No slides");
        empty.EstimatedDuration.Should().Be(TimeSpan.Zero);
        empty.Storyboard.Segments.Should().BeEmpty();
        empty.Storyboard.TotalDuration.Should().Be(TimeSpan.Zero);
        empty.DisabledReason.Should().Be("Video export requires at least one slide.");
    }

    [Fact]
    public void VideoStoryboardPlan_UsesRecordedTransitionAdvanceWhenAvailableAndDefaultsMissingTimings()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide
        {
            Title = "Opening",
            Transition = new SlideTransition { AdvanceAfterMs = 2500 },
        });
        presentation.Slides.Add(new Slide { Title = "Agenda" });
        presentation.Slides.Add(new Slide
        {
            Title = "Decision",
            Transition = new SlideTransition { AdvanceAfterMs = 7000 },
        });

        var plan = PresentationExportPlanner.BuildVideoExportPlan(
            new PresentationVideoExportRequest(SecondsPerSlide: 4, UseRecordedTimings: true),
            presentation);

        plan.Storyboard.Segments.Select(segment => segment.SlideTitle)
            .Should()
            .Equal("Opening", "Agenda", "Decision");
        plan.Storyboard.Segments.Select(segment => segment.Duration)
            .Should()
            .Equal(TimeSpan.FromMilliseconds(2500), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(7));
        plan.Storyboard.Segments.Select(segment => segment.TimingSource)
            .Should()
            .Equal(
                PresentationVideoTimingSource.RecordedTransitionAdvance,
                PresentationVideoTimingSource.DefaultDuration,
                PresentationVideoTimingSource.RecordedTransitionAdvance);
        plan.Storyboard.Segments.Select(segment => segment.StartTime)
            .Should()
            .Equal(TimeSpan.Zero, TimeSpan.FromMilliseconds(2500), TimeSpan.FromMilliseconds(6500));
        plan.EstimatedDuration.Should().Be(TimeSpan.FromMilliseconds(13500));
        plan.Storyboard.TotalDuration.Should().Be(plan.EstimatedDuration);
    }

    [Fact]
    public void VideoStoryboardPlan_IgnoresRecordedTransitionAdvanceWhenRecordedTimingsAreDisabled()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide
        {
            Title = "Timed",
            Transition = new SlideTransition { AdvanceAfterMs = 9000 },
        });

        var plan = PresentationExportPlanner.BuildVideoExportPlan(
            new PresentationVideoExportRequest(SecondsPerSlide: 3, UseRecordedTimings: false),
            presentation);

        plan.Storyboard.Segments.Should().ContainSingle();
        plan.Storyboard.Segments[0].Duration.Should().Be(TimeSpan.FromSeconds(3));
        plan.Storyboard.Segments[0].TimingSource.Should().Be(PresentationVideoTimingSource.DefaultDuration);
        plan.EstimatedDuration.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void VideoFramePackageExecutor_RendersStoryboardFramesAndManifest()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide
        {
            Title = "Intro",
            Transition = new SlideTransition { AdvanceAfterMs = 2500 },
        });
        presentation.Slides.Add(new Slide { Title = "Agenda" });
        presentation.Slides.Add(new Slide
        {
            Title = "Decision",
            Transition = new SlideTransition { AdvanceAfterMs = 6000 },
        });

        var calls = new List<(int SlideIndex, int Width, int Height)>();
        var package = PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            new PresentationVideoExportRequest(
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.SelectedSlides,
                    SelectedSlideNumbers: [3, 1, 2, 2]),
                PresentationVideoQualityKind.Hd,
                SecondsPerSlide: 4,
                UseRecordedTimings: true,
                IncludeNarration: true),
            (deck, slideIndex, width, height) =>
            {
                deck.Should().BeSameAs(presentation);
                calls.Add((slideIndex, width, height));
                return TinyPng.Concat([(byte)slideIndex]).ToArray();
            });

        package.Plan.CanBuildPackage.Should().BeTrue();
        package.Plan.ContentType.Should().Be(PresentationVideoFramePackageExecutor.PackageContentType);
        package.Plan.DefaultExtensionWithDot.Should().Be(PresentationVideoFramePackageExecutor.PackageExtension);
        package.Plan.DeferredCapabilities.Should().Contain(PresentationVideoFramePackageExecutor.EncoderDeferred);
        package.Plan.DeferredCapabilities.Should().Contain(PresentationVideoFramePackageExecutor.Mp4EncoderDeferred);
        package.Plan.ExportPlan.IsImplemented.Should().BeFalse();
        package.Plan.ExportPlan.CanExecute.Should().BeFalse();
        package.Plan.ExportPlan.DisabledReason.Should().Be(PresentationExportPlanner.VideoExportDeferredMessage);
        package.Frames.Select(frame => frame.FileName)
            .Should()
            .Equal(
                "frames/slide-01-frame-0001.png",
                "frames/slide-02-frame-0002.png",
                "frames/slide-03-frame-0003.png");
        package.Frames.Select(frame => frame.SlideTitle).Should().Equal("Intro", "Agenda", "Decision");
        package.Frames.Select(frame => frame.Duration)
            .Should()
            .Equal(TimeSpan.FromMilliseconds(2500), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6));
        package.Frames.Select(frame => frame.TimingSource)
            .Should()
            .Equal(
                PresentationVideoTimingSource.RecordedTransitionAdvance,
                PresentationVideoTimingSource.DefaultDuration,
                PresentationVideoTimingSource.RecordedTransitionAdvance);
        calls.Should().Equal((0, 1280, 720), (1, 1280, 720), (2, 1280, 720));

        using var archive = new ZipArchive(new MemoryStream(package.Bytes), ZipArchiveMode.Read);
        archive.Entries.Select(entry => entry.FullName)
            .Should()
            .Equal(
                "manifest.json",
                "encoder-deferred.txt",
                "frames/slide-01-frame-0001.png",
                "frames/slide-02-frame-0002.png",
                "frames/slide-03-frame-0003.png");
        ReadZipText(archive, "encoder-deferred.txt").Should().Contain("MP4 encoding");

        using var manifest = JsonDocument.Parse(ReadZipText(archive, "manifest.json"));
        var root = manifest.RootElement;
        root.GetProperty("PackageKind").GetString().Should().Be(PresentationVideoFramePackageExecutor.EncoderInputPackageKind);
        root.GetProperty("DeferredCapabilities")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should()
            .Contain([PresentationVideoFramePackageExecutor.EncoderDeferred, PresentationVideoFramePackageExecutor.Mp4EncoderDeferred]);
        root.GetProperty("Mp4ExportPlanImplemented").GetBoolean().Should().BeFalse();
        root.GetProperty("Mp4ExportCanExecute").GetBoolean().Should().BeFalse();
        root.GetProperty("SlideRange").GetProperty("SlideNumbers")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .Should()
            .Equal(1, 2, 3);
        root.GetProperty("Quality").GetProperty("Quality").GetString().Should().Be("Hd");
        root.GetProperty("Quality").GetProperty("WidthPx").GetInt32().Should().Be(1280);
        root.GetProperty("Quality").GetProperty("HeightPx").GetInt32().Should().Be(720);
        root.GetProperty("Quality").GetProperty("FrameRateHint").GetDouble().Should().Be(30);

        var frames = root.GetProperty("Frames").EnumerateArray().ToArray();
        frames.Should().HaveCount(3);
        frames[0].GetProperty("SlideTitle").GetString().Should().Be("Intro");
        frames[0].GetProperty("FileName").GetString().Should().Be("frames/slide-01-frame-0001.png");
        TimeSpan.Parse(frames[0].GetProperty("Duration").GetString()!).Should().Be(TimeSpan.FromMilliseconds(2500));
        frames[0].GetProperty("TimingSource").GetString().Should().Be(nameof(PresentationVideoTimingSource.RecordedTransitionAdvance));
        frames[1].GetProperty("SlideTitle").GetString().Should().Be("Agenda");
        TimeSpan.Parse(frames[1].GetProperty("StartTime").GetString()!).Should().Be(TimeSpan.FromMilliseconds(2500));
        TimeSpan.Parse(frames[1].GetProperty("Duration").GetString()!).Should().Be(TimeSpan.FromSeconds(4));
        frames[1].GetProperty("TimingSource").GetString().Should().Be(nameof(PresentationVideoTimingSource.DefaultDuration));
    }

    [Fact]
    public void VideoFramePackageExecutor_UsesHostCapabilityWhenBuildingHostPackage()
    {
        var presentation = BuildHandoutDeck(1);
        var host = new PresentationVideoExportHandoffHostCapabilities(
            "WPF video export host",
            CanEncodeMp4: true,
            CanCaptureNarration: true,
            CanCaptureCameraAndMedia: false,
            UnavailableReason: string.Empty);

        var package = PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            new PresentationVideoExportRequest(IncludeNarration: true),
            (_, _, _, _) => TinyPng.ToArray(),
            host);

        package.Plan.ExportPlan.IsImplemented.Should().BeTrue();
        package.Plan.ExportPlan.CanExecute.Should().BeTrue();
        package.Plan.ExportPlan.DisabledReason.Should().BeNull();

        var handoff = PresentationVideoFramePackageExecutor.BuildExecutionDescriptor(
            package,
            host);
        handoff.HandoffPlan.Status.Should().Be(PresentationVideoExportHandoffStatus.HostEncoderReady);
        handoff.HandoffPlan.CanOpenHostEncoder.Should().BeTrue();
        handoff.HandoffPlan.Mp4EncoderDeferredByHost.Should().BeFalse();
    }

    [Fact]
    public void VideoFramePackageExecutionDescriptor_ValidatesAndMaterializesEncoderInputZip()
    {
        var package = PresentationVideoFramePackageExecutor.BuildPackage(
            BuildHandoutDeck(2),
            new PresentationVideoExportRequest(
                Quality: PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 2,
                UseRecordedTimings: false,
                IncludeNarration: false),
            (_, _, _, _) => TinyPng.ToArray());
        var targetPath = Path.Combine(Path.GetTempPath(), $"freep-video-encoder-input-{Guid.NewGuid():N}.zip");

        try
        {
            var descriptor = PresentationVideoFramePackageExecutor.BuildExecutionDescriptor(
                package,
                PresentationVideoExportHandoffHostCapabilities.Deferred(
                    "Unit test video host",
                    "Unit tests do not open MP4 encoders."),
                "Quarter Review.pptx");

            descriptor.PackageKind.Should().Be(PresentationVideoFramePackageExecutor.EncoderInputPackageKind);
            descriptor.PackagePlan.Should().BeSameAs(package.Plan);
            descriptor.HandoffPlan.Status.Should().Be(PresentationVideoExportHandoffStatus.EncoderInputPackageReadyHostDeferred);
            descriptor.HandoffPlan.Mp4EncoderDeferredByHost.Should().BeTrue();
            descriptor.ContentType.Should().Be(PresentationVideoFramePackageExecutor.PackageContentType);
            descriptor.DefaultExtensionWithDot.Should().Be(PresentationVideoFramePackageExecutor.PackageExtension);
            descriptor.SuggestedPackageName.Should().Be("Quarter Review-video-encoder-input.zip");
            descriptor.FrameCount.Should().Be(2);
            descriptor.ByteCount.Should().Be(package.Bytes.Length);
            descriptor.IsEncoderInputPackage.Should().BeTrue();
            descriptor.CanMaterialize.Should().BeTrue();
            descriptor.DisabledReason.Should().BeNull();
            descriptor.Validation.Should().Match<PresentationVideoFramePackageValidation>(validation =>
                validation.IsValid &&
                validation.HasBytes &&
                validation.HasZipContainer &&
                validation.HasManifest &&
                validation.HasEncoderDeferredMarker &&
                validation.ExpectedFrameCount == 2 &&
                validation.ManifestFrameCount == 2 &&
                validation.ZipFrameEntryCount == 2 &&
                validation.FrameCountMatchesPackage &&
                validation.ContentTypeIsZip &&
                validation.ExtensionIsZip &&
                validation.ByteCount == package.Bytes.Length);

            var result = PresentationVideoFramePackageExecutor.MaterializePackageForHandoff(
                package,
                targetPath,
                suggestedBaseFileName: "Quarter Review.pptx");

            result.Succeeded.Should().BeTrue();
            result.FailureReason.Should().BeNull();
            result.Descriptor.CanMaterialize.Should().BeTrue();
            File.ReadAllBytes(targetPath).Should().Equal(package.Bytes);
        }
        finally
        {
            if (File.Exists(targetPath))
                File.Delete(targetPath);
        }
    }

    [Fact]
    public void VideoFramePackageExecutionDescriptor_BlocksMaterializationForEmptyOrInvalidZipBytes()
    {
        var validPackage = PresentationVideoFramePackageExecutor.BuildPackage(
            BuildHandoutDeck(1),
            request: null,
            (_, _, _, _) => TinyPng.ToArray());
        var emptyTargetPath = Path.Combine(Path.GetTempPath(), $"freep-empty-video-encoder-input-{Guid.NewGuid():N}.zip");
        var invalidTargetPath = Path.Combine(Path.GetTempPath(), $"freep-invalid-video-encoder-input-{Guid.NewGuid():N}.zip");
        var emptyPackage = validPackage with { Bytes = [] };
        var invalidPackage = validPackage with { Bytes = Encoding.ASCII.GetBytes("not a zip") };

        try
        {
            var empty = PresentationVideoFramePackageExecutor.MaterializePackageForHandoff(
                emptyPackage,
                emptyTargetPath,
                suggestedBaseFileName: "Empty.pptx");
            var invalid = PresentationVideoFramePackageExecutor.MaterializePackageForHandoff(
                invalidPackage,
                invalidTargetPath,
                suggestedBaseFileName: "Broken.pptx");

            empty.Succeeded.Should().BeFalse();
            empty.Descriptor.CanMaterialize.Should().BeFalse();
            empty.Descriptor.Validation.HasBytes.Should().BeFalse();
            empty.FailureReason.Should().Be("Video encoder-input package contains no bytes.");
            File.Exists(emptyTargetPath).Should().BeFalse();

            invalid.Succeeded.Should().BeFalse();
            invalid.Descriptor.CanMaterialize.Should().BeFalse();
            invalid.Descriptor.Validation.HasZipContainer.Should().BeFalse();
            invalid.FailureReason.Should().Be("Video encoder-input package is not a valid ZIP archive.");
            File.Exists(invalidTargetPath).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(emptyTargetPath))
                File.Delete(emptyTargetPath);
            if (File.Exists(invalidTargetPath))
                File.Delete(invalidTargetPath);
        }
    }

    [Fact]
    public void VideoExportHandoffPlan_ReportsHostDeferredEncoderOverFramePackage()
    {
        var presentation = Presentation.CreateEmpty();
        var package = PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            request: null,
            (_, _, _, _) => TinyPng.ToArray());
        var host = PresentationVideoExportHandoffHostCapabilities.Deferred(
            "Unit test video host",
            "Unit test host has no MP4 encoder adapter.");

        var handoff = PresentationVideoFramePackageExecutor.BuildHandoffPlan(package.Plan, host);

        handoff.PackagePlan.Should().BeSameAs(package.Plan);
        handoff.HostCapabilities.Should().Be(host);
        handoff.Status.Should().Be(PresentationVideoExportHandoffStatus.EncoderInputPackageReadyHostDeferred);
        handoff.IsFramePackageReady.Should().BeTrue();
        handoff.RequiresHostEncoder.Should().BeTrue();
        handoff.CanOpenHostEncoder.Should().BeFalse();
        handoff.Mp4EncoderDeferredByHost.Should().BeTrue();
        handoff.StatusText.Should().Be("Unit test video host: MP4 encoder deferred; frame package ready");
        handoff.Reason.Should().Be("Unit test host has no MP4 encoder adapter.");
        handoff.Capabilities.Should().Contain(capability =>
            capability.Name == "Frame package" &&
            capability.IsAvailable &&
            !capability.IsDeferred);
        handoff.Capabilities.Should().Contain(capability =>
            capability.Name == "MP4 encoder" &&
            !capability.IsAvailable &&
            capability.IsDeferred &&
            capability.StatusText == "Unit test host has no MP4 encoder adapter.");
    }

    [Fact]
    public void VideoFramePackageExecutor_EmptyDeckBuildsNoFrames()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var package = PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            request: null,
            (_, _, _, _) => throw new InvalidOperationException("Empty decks should not render frames."));

        package.Plan.CanBuildPackage.Should().BeFalse();
        package.Plan.DisabledReason.Should().Be("Video frame package requires at least one slide.");
        package.Plan.ExportPlan.DisabledReason.Should().Be("Video export requires at least one slide.");
        package.Frames.Should().BeEmpty();
        package.Bytes.Should().BeEmpty();

        var handoff = PresentationVideoFramePackageExecutor.BuildHandoffPlan(
            package.Plan,
            PresentationVideoExportHandoffHostCapabilities.Deferred("Unit test video host", "No encoder."));
        handoff.Status.Should().Be(PresentationVideoExportHandoffStatus.NoSlides);
        handoff.IsFramePackageReady.Should().BeFalse();
        handoff.RequiresHostEncoder.Should().BeFalse();
        handoff.CanOpenHostEncoder.Should().BeFalse();
        handoff.Mp4EncoderDeferredByHost.Should().BeFalse();
        handoff.StatusText.Should().Be("Video export requires at least one slide.");
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

    private static SlideShape MakeHeaderFooterPlaceholder(
        PlaceholderType type,
        string text,
        string? fieldType = null,
        string? cachedText = null)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Text = text,
            Field = fieldType is null
                ? null
                : new FieldRun
                {
                    FieldType = fieldType,
                    CachedText = cachedText ?? text
                }
        });
        body.Paragraphs.Add(paragraph);

        return new SlideShape
        {
            Placeholder = new Placeholder { Type = type },
            TextBody = body
        };
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

    private static Paragraph MakeParagraph(string text, Action<Paragraph>? configure = null)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        configure?.Invoke(paragraph);
        return paragraph;
    }

    private static string FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "FreeP.RenderCompare", "corpus");
            if (File.Exists(Path.Combine(candidate, "21-comments-notes.pptx")))
                return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate tools/FreeP.RenderCompare/corpus.");
    }

    private static string ReadZipText(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
