namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowMediaEntryPlannerTests
{
    [Fact]
    public void PlanSlideEntry_OwnsEligibilityBoundsActiveSlotsAndCaptionSelection()
    {
        var slide = new Slide();
        slide.Shapes.Add(MediaShape(10, isVideo: true, hasSource: true));
        slide.Shapes.Add(MediaShape(20, isVideo: false, hasSource: true));
        slide.Shapes.Add(MediaShape(30, isVideo: true, hasSource: false));
        var tracks = new[]
        {
            CaptionTrack(slideIndex: 4, shapeId: 10, trackIndex: 0, "Default"),
            CaptionTrack(slideIndex: 4, shapeId: 10, trackIndex: 2, "Preferred"),
            CaptionTrack(slideIndex: 4, shapeId: 20, trackIndex: 0, "Narration"),
        };

        var plan = SlideShowMediaInteractionPlanner.PlanSlideEntry(
            slide,
            slideDipW: 10,
            slideDipH: 10,
            canvasW: 20,
            canvasH: 10,
            tracks,
            preferredCaptionShapeId: 10,
            preferredCaptionTrackIndex: 2,
            captionSlideIndex: 4,
            preferredCaptionSlideIndex: 4,
            showMediaControls: false,
            showNarration: false);

        plan.Items.Select(item => item.ShapeId).Should().Equal(10, 30);
        plan.Active.Should().Equal(plan.Items.Select(item => item.Surface));
        plan.Active[0].Bounds.Should().Be(new LayoutRect(5, 0, 4, 4));
        plan.Active.Should().OnlyContain(item => !item.ShowMediaControls);
        plan.Items[0].CaptionTrack!.Label.Should().Be("Preferred");
        plan.Items[1].CaptionTrack.Should().BeNull();
        plan.HasPlayableSource.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunPeriodicUpdates_CombinesCaptionAndPlaybackTimingPolicy()
    {
        var playbackSession = new SlideShowMediaPlaybackSession();
        var untimed = playbackSession.Register(1, new MediaInfo(), new FakePlaybackPort());
        var timed = playbackSession.Register(
            2,
            new MediaInfo { FadeInMilliseconds = 250 },
            new FakePlaybackPort());

        SlideShowMediaInteractionPlanner.ShouldRunPeriodicUpdates(
            [new SlideShowMediaActiveSlotMonitorPlan(null, untimed)],
            playbackSession).Should().BeFalse();
        SlideShowMediaInteractionPlanner.ShouldRunPeriodicUpdates(
            [new SlideShowMediaActiveSlotMonitorPlan(CaptionTrack(0, 1, 0, "English"), null)],
            playbackSession).Should().BeTrue();
        SlideShowMediaInteractionPlanner.ShouldRunPeriodicUpdates(
            [new SlideShowMediaActiveSlotMonitorPlan(null, timed)],
            playbackSession).Should().BeTrue();
    }

    private static SlideShape MediaShape(uint id, bool isVideo, bool hasSource) =>
        new()
        {
            Id = id,
            Kind = SlideShapeKind.Media,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 4 * 9525,
            ExtentCyEmu = 4 * 9525,
            Media = new MediaInfo
            {
                IsVideo = isVideo,
                Bytes = hasSource ? [1, 2, 3] : [],
            },
        };

    private static PresentationMediaTranscriptTrackDescriptor CaptionTrack(
        int slideIndex,
        uint shapeId,
        int trackIndex,
        string label) =>
        new(
            slideIndex,
            shapeId,
            $"Shape {shapeId}",
            trackIndex,
            label,
            "en-US",
            "captions.vtt",
            "text/vtt",
            PresentationMediaTranscriptTrackStatus.Available,
            "Available",
            [new PresentationMediaTranscriptCueDescriptor(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Caption")]);

    private sealed class FakePlaybackPort : IMediaPlaybackPort
    {
        public bool IsPlaying => false;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.FromMinutes(1);
        public int VolumePercent { private get; set; }
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public bool Seek(TimeSpan position) => true;
    }
}
