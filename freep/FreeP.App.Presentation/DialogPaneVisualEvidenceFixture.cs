using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record DialogPaneVisualEvidenceFixture(
    Presentation Presentation,
    uint TextShapeId,
    uint ChartShapeId,
    uint MediaShapeId,
    uint SmartArtShapeId)
{
    public uint SelectionForRoute(string routeId) => routeId switch
    {
        "chart.edit-data" => ChartShapeId,
        "accessibility.media-caption-pane" => MediaShapeId,
        "context.smartart-text-pane" => SmartArtShapeId,
        _ => TextShapeId,
    };
}

public static class DialogPaneVisualEvidenceFixtureFactory
{
    public const uint TextShapeId = 10;
    public const uint ChartShapeId = 20;
    public const uint MediaShapeId = 30;
    public const uint SmartArtShapeId = 40;
    public const int RichEditorSelectionStart = 10;
    public const int RichEditorSelectionEnd = 35;
    public const int RichEditorCaretPosition = 67;
    public const string RichEditorSelectedText = "revenue review highlights";

    public static DialogPaneVisualEvidenceFixture Create()
    {
        var presentation = Presentation.CreateEmpty();
        var first = presentation.Slides[0];
        first.Id = "visual-evidence-slide-1";
        first.Title = "Quarterly Review";
        first.Notes = Body("Review the revenue trend and confirm the launch date.");

        var textShape = new SlideShape
        {
            Id = TextShapeId,
            Name = "Review summary",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RoundedRectangle,
            OffsetXEmu = 914_400,
            OffsetYEmu = 1_500_000,
            ExtentCxEmu = 4_100_000,
            ExtentCyEmu = 1_200_000,
            AlternativeTextTitle = "Quarterly review summary",
            AlternativeText = "Summary callout for the quarterly review.",
            Text = "Qick revenue review",
        };
        first.Shapes.Add(textShape);

        var chart = new ChartShape { Title = "Revenue", Legend = LegendPosition.Bottom };
        chart.Categories.AddRange(["Q1", "Q2", "Q3"]);
        var actual = new ChartSeries { Name = "Actual" };
        actual.Values.AddRange([12d, 18d, 24d]);
        var plan = new ChartSeries { Name = "Plan" };
        plan.Values.AddRange([10d, 20d, 22d]);
        chart.Series.Add(actual);
        chart.Series.Add(plan);
        first.Shapes.Add(new SlideShape
        {
            Id = ChartShapeId,
            Name = "Revenue chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 5_300_000,
            OffsetYEmu = 1_300_000,
            ExtentCxEmu = 5_500_000,
            ExtentCyEmu = 3_400_000,
            Chart = chart,
        });

        var media = new MediaInfo
        {
            IsVideo = true,
            ContentType = "video/mp4",
            Bytes = [0, 0, 0, 24, 102, 116, 121, 112],
        };
        media.CaptionTracks.Add(new MediaCaptionTrackInfo
        {
            RelationshipId = "rIdCaption1",
            Source = "captions/review-en.vtt",
            ContentType = "text/vtt",
            Language = "en-US",
            Label = "English",
            Bytes = "WEBVTT\n\n00:00.000 --> 00:02.000\nWelcome to the review.\n"u8.ToArray(),
        });
        first.Shapes.Add(new SlideShape
        {
            Id = MediaShapeId,
            Name = "Review video",
            Kind = SlideShapeKind.Media,
            OffsetXEmu = 914_400,
            OffsetYEmu = 3_000_000,
            ExtentCxEmu = 3_000_000,
            ExtentCyEmu = 1_700_000,
            Media = media,
        });

        var smartArtData = new SmartArtData { Family = SmartArtFamily.Process };
        smartArtData.Nodes.Add(new SmartArtNode { ModelId = "phase-1", Text = "Discover", Level = 0 });
        smartArtData.Nodes.Add(new SmartArtNode { ModelId = "phase-2", Text = "Design", Level = 0 });
        smartArtData.Nodes.Add(new SmartArtNode { ModelId = "phase-3", Text = "Deliver", Level = 0 });
        first.Shapes.Add(new SlideShape
        {
            Id = SmartArtShapeId,
            Name = "Delivery process",
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = 4_100_000,
            OffsetYEmu = 4_900_000,
            ExtentCxEmu = 6_800_000,
            ExtentCyEmu = 1_200_000,
            SmartArt = new SmartArtShape { Data = smartArtData },
        });

        first.Comments.Add(new SlideComment
        {
            Author = "Alex Morgan",
            Initials = "AM",
            Text = "Confirm the Q3 figure with Finance.",
            Xemu = 6_000_000,
            Yemu = 2_000_000,
            Idx = 1,
        });
        first.Comments.Add(new SlideComment
        {
            Author = "Sam Lee",
            Initials = "SL",
            Text = "The launch date needs an owner.",
            Xemu = 2_000_000,
            Yemu = 4_000_000,
            Idx = 2,
        });
        first.Animations.Add(new ShapeAnimation
        {
            ShapeId = TextShapeId,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 600,
        });
        first.Animations.Add(new ShapeAnimation
        {
            ShapeId = ChartShapeId,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.WithPrevious,
            DurationMs = 900,
        });

        var second = new Slide { Id = "visual-evidence-slide-2", LayoutId = first.LayoutId };
        second.Title = "Next Steps";
        second.Notes = Body("Assign owners before Friday.");
        presentation.Slides.Add(second);

        var third = new Slide { Id = "visual-evidence-slide-3", LayoutId = first.LayoutId };
        third.Title = "Appendix";
        presentation.Slides.Add(third);

        var section = new PresentationSection { Name = "Review" };
        section.SlideIds.Add(first.Id);
        section.SlideIds.Add(second.Id);
        presentation.Sections.Add(section);
        var customShow = new PresentationCustomShow { Id = 1, Name = "Executive review" };
        customShow.SlideIds.Add(first.Id);
        customShow.SlideIds.Add(second.Id);
        presentation.CustomShows.Add(customShow);

        return new DialogPaneVisualEvidenceFixture(
            presentation,
            TextShapeId,
            ChartShapeId,
            MediaShapeId,
            SmartArtShapeId);
    }

    public static TextBody CreateRichEditorBody()
    {
        // Keep this deterministic mixed-font raster on one logical line. Wrapped
        // pointer behavior is covered by the physical Linux grouped-child fixture.
        var body = new TextBody { Wrap = false };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Text = "Quarterly ",
            FontFamily = "Arial",
            FontSizePt = 18,
            Bold = true,
            BoldSet = true,
        });
        paragraph.Runs.Add(new Run
        {
            Text = RichEditorSelectedText,
            FontFamily = "Calibri",
            FontSizePt = 14,
            Italic = true,
            ItalicSet = true,
            Color = new ThemeAwareColor(new SrgbColor(0x2F, 0x55, 0x97)),
        });
        paragraph.Runs.Add(new Run
        {
            Text = " and next-step ownership need a careful second-line check before Friday.",
            FontFamily = "Arial",
            FontSizePt = 12,
            Underline = true,
        });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static TextBody Body(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return body;
    }
}
