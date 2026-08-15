namespace FreeP.App.Compositor.Tests;

public sealed class PresentationMediaPaneHostViewAdapterTests
{
    [Fact]
    public void Captures_all_native_input_through_portable_snapshot_planners()
    {
        var surface = new RecordingSurface
        {
            IsPaneVisible = true,
            CaptionLabel = "English",
            CaptionLanguage = "en-US",
            CaptionSource = "captions.vtt",
            CaptionTranscript = "Hello",
            VolumePercent = 74.6,
            PlaybackStartModeIndex = 1,
            Loop = true,
            ShowWhenStopped = false,
            RewindAfterPlaying = true,
            PlayFullScreen = true,
            StopAfterSlides = "3",
            TrimStart = "100",
            TrimEnd = "200",
            FadeIn = "20",
            FadeOut = "30",
            BookmarkName = "Intro",
            BookmarkTime = "450",
        };
        var adapter = new PresentationMediaPaneHostViewAdapter(surface);

        adapter.IsPaneVisible.Should().BeTrue();
        adapter.CaptureCaption().Should().Be(new PresentationMediaCaptionHostSnapshot(
            "English", "en-US", "captions.vtt", "Hello"));
        adapter.CaptureVolume().NormalizedVolumePercent.Should().Be(75);
        adapter.CapturePlayback().Should().Be(new PresentationMediaPlaybackHostSnapshot(
            1, true, false, true, true, "3"));
        adapter.CaptureTiming().Should().Be(new PresentationMediaTimingHostSnapshot(
            "100", "200", "20", "30"));
        adapter.CaptureBookmark().Should().Be(new PresentationMediaBookmarkHostSnapshot("Intro", "450"));
    }

    [Fact]
    public void Applies_renderer_ready_media_plan_to_semantic_surface()
    {
        var surface = new RecordingSurface();
        var adapter = new PresentationMediaPaneHostViewAdapter(surface);
        var caption = PresentationMediaTranscriptPlanner.BuildCaptionAuthoringPanePlan(
            slide: null,
            slideIndex: 0,
            selectedShapeIds: null,
            selectedTrackIndex: null,
            proposedLabel: null,
            proposedLanguage: null,
            proposedSource: null,
            proposedTranscriptText: null);
        var media = new PresentationMediaPaneProjection(
            HasMedia: false,
            VolumePercent: 63,
            PlaybackStartMode: MediaPlaybackStartMode.Automatically,
            Loop: true,
            ShowWhenStopped: false,
            RewindAfterPlaying: true,
            PlayFullScreen: false,
            StopAfterSlides: 2,
            CanPlayFullScreen: false,
            CanStopAfterSlides: false,
            Timing: new PresentationMediaTimingInputPlan("1", "2", "3", "4"),
            Bookmarks: Array.Empty<PresentationMediaBookmarkPaneItemPlan>(),
            SelectedBookmarkIndex: null,
            BookmarkName: string.Empty,
            BookmarkTimeText: string.Empty);
        var playback = PresentationMediaPaneSession.BuildPlaybackInputPlan(
            MediaPlaybackStartMode.Automatically,
            loop: true,
            showWhenStopped: false,
            rewindAfterPlaying: true,
            playFullScreen: false,
            stopAfterSlides: 2);

        adapter.Render(new PresentationMediaPaneHostRenderPlan(caption, media, playback));

        surface.VolumePercent.Should().Be(63);
        surface.PlaybackStartModeIndex.Should().Be(playback.StartModeIndex);
        surface.Loop.Should().BeTrue();
        surface.TrimStart.Should().Be("1");
        surface.RenderedFields.Should().BeEquivalentTo(Enum.GetValues<PresentationMediaPaneCaptionField>());
        surface.RenderedActions.Should().BeEquivalentTo(Enum.GetValues<PresentationMediaPaneCaptionAction>());
        surface.RenderedBookmarks.Should().BeSameAs(media);
        surface.PlaybackApplyEnabled.Should().BeFalse();
    }

    [Fact]
    public void Native_composition_maps_renderer_controls_to_the_shared_adapter()
    {
        var controls = CreateNativeControls();
        controls.Pane.Visible = true;
        controls.CaptionLabel.Text = "English";
        controls.VolumePercent.Value = 64.6;
        controls.PlaybackStartMode.Index = 2;
        controls.Loop.Checked = true;
        controls.StopAfterSlides.Text = "4";
        var refreshed = 0;

        var adapter = PresentationMediaPaneNativeComposition.Compose(
            controls,
            new PresentationMediaPaneNativeAccessors<FakeControl>(
                control => control.Visible,
                (control, value) => control.Visible = value,
                control => control.Text,
                (control, value) => control.Text = value,
                control => control.Value,
                (control, value) => control.Value = value,
                control => control.Index,
                (control, value) => control.Index = value,
                control => control.Checked,
                (control, value) => control.Checked = value,
                (control, value) => control.Enabled = value),
            _ => { },
            (_, _) => { },
            (_, _) => { },
            _ => { },
            () => refreshed++);

        adapter.IsPaneVisible.Should().BeTrue();
        adapter.CaptureCaption().Label.Should().Be("English");
        adapter.CaptureVolume().NormalizedVolumePercent.Should().Be(65);
        adapter.CapturePlayback().Should().Be(new PresentationMediaPlaybackHostSnapshot(
            2, true, true, false, false, "4"));

        adapter.SetPaneVisible(false);
        adapter.SetCaptionInput(new("French", "fr-FR", "captions.vtt", "Bonjour"));
        adapter.SetVolumeInput(new(30));
        adapter.RefreshAccessibilityMetadata();

        controls.Pane.Visible.Should().BeFalse();
        controls.CaptionLabel.Text.Should().Be("French");
        controls.CaptionLanguage.Text.Should().Be("fr-FR");
        controls.VolumePercent.Value.Should().Be(30);
        refreshed.Should().Be(1);
    }

    private static PresentationMediaPaneNativeControls<FakeControl> CreateNativeControls() => new(
        new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(),
        new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new());

    private sealed class FakeControl
    {
        public bool Visible { get; set; }
        public bool Enabled { get; set; }
        public string? Text { get; set; }
        public double? Value { get; set; }
        public int? Index { get; set; }
        public bool? Checked { get; set; }
    }

    private sealed class RecordingSurface : IPresentationMediaPaneControlSurface
    {
        public bool IsPaneVisible { get; set; }
        public string? CaptionLabel { get; set; }
        public string? CaptionLanguage { get; set; }
        public string? CaptionSource { get; set; }
        public string? CaptionTranscript { get; set; }
        public double? VolumePercent { get; set; }
        public int? PlaybackStartModeIndex { get; set; }
        public bool? Loop { get; set; }
        public bool? ShowWhenStopped { get; set; }
        public bool? RewindAfterPlaying { get; set; }
        public bool? PlayFullScreen { get; set; }
        public string? StopAfterSlides { get; set; }
        public string? TrimStart { get; set; }
        public string? TrimEnd { get; set; }
        public string? FadeIn { get; set; }
        public string? FadeOut { get; set; }
        public string? BookmarkName { get; set; }
        public string? BookmarkTime { get; set; }
        public string Heading { private get; set; } = string.Empty;
        public string Message { private get; set; } = string.Empty;
        public bool PlaybackStartModeEnabled { private get; set; }
        public bool LoopEnabled { private get; set; }
        public bool ShowWhenStoppedEnabled { private get; set; }
        public bool RewindAfterPlayingEnabled { private get; set; }
        public bool PlayFullScreenEnabled { private get; set; }
        public bool StopAfterSlidesEnabled { private get; set; }
        public bool PlaybackApplyEnabled { get; set; }
        public bool VolumeEnabled { private get; set; }
        public bool VolumeApplyEnabled { private get; set; }
        public bool TimingApplyEnabled { private get; set; }
        public List<PresentationMediaPaneCaptionField> RenderedFields { get; } = [];
        public List<PresentationMediaPaneCaptionAction> RenderedActions { get; } = [];
        public PresentationMediaPaneProjection? RenderedBookmarks { get; private set; }

        public void RenderCaptionTracks(PresentationMediaCaptionAuthoringPanePlan plan) { }

        public void RenderCaptionField(
            PresentationMediaPaneCaptionField field,
            PresentationMediaCaptionAuthoringFieldPlan plan) => RenderedFields.Add(field);

        public void RenderCaptionAction(
            PresentationMediaPaneCaptionAction action,
            PresentationMediaCaptionAuthoringActionPlan plan) => RenderedActions.Add(action);

        public void RenderBookmarks(PresentationMediaPaneProjection plan) => RenderedBookmarks = plan;

        public void RefreshAccessibilityMetadata() { }
    }
}
