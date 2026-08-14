using System.Globalization;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationMediaPaneSessionTests
{
    [Fact]
    public void SharedMediaPanePolicyOwnsRendererGeometryAndInputBounds()
    {
        PresentationMediaPaneVisualMetrics.PaneWidth.Should().Be(320);
        PresentationMediaPaneVisualMetrics.PaneBorderThickness.Should().Be(1);
        PresentationMediaPaneVisualMetrics.HeadingFontSize.Should().Be(15);
        PresentationMediaPaneVisualMetrics.BodyFontSize.Should().Be(12);
        PresentationMediaPaneVisualMetrics.ContentSideMargin.Should().Be(12);
        PresentationMediaPaneVisualMetrics.HeadingTopMargin.Should().Be(12);
        PresentationMediaPaneVisualMetrics.HeadingBottomMargin.Should().Be(4);
        PresentationMediaPaneVisualMetrics.MessageBottomMargin.Should().Be(8);
        PresentationMediaPaneVisualMetrics.TrackBottomMargin.Should().Be(6);
        PresentationMediaPaneVisualMetrics.LabelTopMargin.Should().Be(6);
        PresentationMediaPaneVisualMetrics.LabelBottomMargin.Should().Be(2);
        PresentationMediaPaneVisualMetrics.FieldBottomMargin.Should().Be(4);
        PresentationMediaPaneVisualMetrics.CheckBoxTopMargin.Should().Be(2);
        PresentationMediaPaneVisualMetrics.ActionRowTopMargin.Should().Be(8);
        PresentationMediaPaneVisualMetrics.ActionRowBottomMargin.Should().Be(12);
        PresentationMediaPaneVisualMetrics.CompactControlHeight.Should().Be(28);
        PresentationMediaPaneVisualMetrics.TranscriptMinimumHeight.Should().Be(128);
        PresentationMediaPaneVisualMetrics.TranscriptMaximumHeight.Should().Be(180);
        PresentationMediaPaneVisualMetrics.FieldHorizontalPadding.Should().Be(6);
        PresentationMediaPaneVisualMetrics.FieldVerticalPadding.Should().Be(4);
        PresentationMediaPaneVisualMetrics.ActionButtonMinimumWidth.Should().Be(72);
        PresentationMediaPaneVisualMetrics.ActionButtonHorizontalPadding.Should().Be(10);
        PresentationMediaPaneVisualMetrics.ActionButtonVerticalPadding.Should().Be(4);
        PresentationMediaPaneVisualMetrics.ActionButtonRightMargin.Should().Be(6);
        PresentationMediaPaneVisualMetrics.ActionButtonBottomMargin.Should().Be(6);

        PresentationMediaPaneSession.MinimumVolumePercent.Should().Be(0);
        PresentationMediaPaneSession.MaximumVolumePercent.Should().Be(100);
        PresentationMediaPaneSession.VolumeTickFrequency.Should().Be(10);
        PresentationMediaPaneSession.SnapVolumeToTicks.Should().BeTrue();
        PresentationMediaPaneSession.DefaultVolumePercent.Should().Be(80);
        PresentationMediaPaneSession.DefaultStopAfterSlides.Should().Be(1);
        PresentationMediaPaneHostSnapshotPlanner.DefaultVolumePercent
            .Should().Be(PresentationMediaPaneSession.DefaultVolumePercent);
    }

    [Fact]
    public void TimingPlans_UseCurrentCultureAndNormalizeInvalidValues()
    {
        var formatted = PresentationMediaPaneSession.FormatTiming(12.34567);

        formatted.Should().Be(12.34567.ToString("0.####", CultureInfo.CurrentCulture));
        PresentationMediaPaneSession.ParseTiming(formatted).Should().BeApproximately(12.3457, 0.00001);
        PresentationMediaPaneSession.ParseTiming("-25").Should().Be(0);
        PresentationMediaPaneSession.ParseTiming("not a time").Should().Be(0);
        PresentationMediaPaneSession.ParseTiming("NaN").Should().Be(0);

        var input = PresentationMediaPaneSession.BuildTimingInputPlan(10, 20.5, -5, 7.25);
        input.TrimStartText.Should().Be(PresentationMediaPaneSession.FormatTiming(10));
        input.TrimEndText.Should().Be(PresentationMediaPaneSession.FormatTiming(20.5));
        input.FadeInText.Should().Be(PresentationMediaPaneSession.FormatTiming(0));
        input.FadeOutText.Should().Be(PresentationMediaPaneSession.FormatTiming(7.25));
        PresentationMediaPaneSession.NormalizeVolumePercent(24.6).Should().Be(25);
        PresentationMediaPaneSession.NormalizeVolumePercent(150).Should().Be(100);
        PresentationMediaPaneSession.GetPlaybackStartModeIndex(MediaPlaybackStartMode.Automatically).Should().Be(1);
        PresentationMediaPaneSession.GetPlaybackStartMode(0).Should().Be(MediaPlaybackStartMode.InClickSequence);

        var playback = PresentationMediaPaneSession.BuildPlaybackInputPlan(
            MediaPlaybackStartMode.Automatically,
            loop: true,
            showWhenStopped: false,
            rewindAfterPlaying: true,
            playFullScreen: true,
            stopAfterSlides: -4);
        playback.StartModeIndex.Should().Be(1);
        playback.StopAfterSlides.Should().Be(1);
        playback.StopAfterSlidesText.Should().Be((1).ToString(CultureInfo.CurrentCulture));
        PresentationMediaPaneSession.ParseStopAfterSlides("8").Should().Be(8);
        PresentationMediaPaneSession.ParseStopAfterSlides("invalid").Should().Be(1);
    }

    [Fact]
    public void Projection_NormalizesBookmarkSelectionAndProvidesRendererReadyState()
    {
        var (editor, media) = CreateSelectedMediaEditor();
        media.VolumePercent = 35;
        media.PlaybackStartMode = MediaPlaybackStartMode.Automatically;
        media.Loop = true;
        media.ShowWhenStopped = false;
        media.RewindAfterPlaying = true;
        media.PlayFullScreen = true;
        media.StopAfterSlides = 3;
        media.TrimStartMilliseconds = 125;
        media.Bookmarks.Add(new MediaBookmarkInfo { Name = "Intro", TimeMilliseconds = 400 });
        media.Bookmarks.Add(new MediaBookmarkInfo { Name = "Demo", TimeMilliseconds = 900 });
        var session = CreateSession(editor);
        session.SelectBookmark(42);

        var plan = session.BuildProjection();

        plan.HasMedia.Should().BeTrue();
        plan.VolumePercent.Should().Be(35);
        plan.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.Automatically);
        plan.Loop.Should().BeTrue();
        plan.ShowWhenStopped.Should().BeFalse();
        plan.RewindAfterPlaying.Should().BeTrue();
        plan.PlayFullScreen.Should().BeTrue();
        plan.StopAfterSlides.Should().Be(3);
        plan.CanPlayFullScreen.Should().BeTrue();
        plan.CanStopAfterSlides.Should().BeFalse();
        plan.Timing.TrimStartText.Should().Be(PresentationMediaPaneSession.FormatTiming(125));
        plan.Bookmarks.Select(bookmark => bookmark.DisplayText)
            .Should().Equal("1. Intro", "2. Demo");
        plan.SelectedBookmarkIndex.Should().Be(0);
        plan.BookmarkName.Should().Be("Intro");
        plan.BookmarkTimeText.Should().Be(PresentationMediaPaneSession.FormatTiming(400));
        session.SelectedBookmarkIndex.Should().Be(0);
    }

    [Fact]
    public void BookmarkMutationPlans_CloneValidateAndNormalizeSelection()
    {
        var media = new MediaInfo();
        media.Bookmarks.Add(new MediaBookmarkInfo { Name = "Intro", TimeMilliseconds = 100 });
        media.Bookmarks.Add(new MediaBookmarkInfo { Name = "Middle", TimeMilliseconds = 200 });

        var create = PresentationMediaPaneSession.BuildBookmarkMutationPlan(
            media,
            PresentationMediaBookmarkMutationIntentKind.Create,
            selectedBookmarkIndex: 0,
            "  End  ",
            PresentationMediaPaneSession.FormatTiming(300));
        var replace = PresentationMediaPaneSession.BuildBookmarkMutationPlan(
            media,
            PresentationMediaBookmarkMutationIntentKind.Replace,
            selectedBookmarkIndex: 1,
            "Updated",
            PresentationMediaPaneSession.FormatTiming(250));
        var delete = PresentationMediaPaneSession.BuildBookmarkMutationPlan(
            media,
            PresentationMediaBookmarkMutationIntentKind.Delete,
            selectedBookmarkIndex: 1,
            null,
            null);
        var invalid = PresentationMediaPaneSession.BuildBookmarkMutationPlan(
            media,
            PresentationMediaBookmarkMutationIntentKind.Replace,
            selectedBookmarkIndex: 99,
            "Missing",
            "10");

        create.ShouldApply.Should().BeTrue();
        create.SelectedBookmarkIndex.Should().Be(2);
        create.Bookmarks[2].Should().Match<MediaBookmarkInfo>(bookmark =>
            bookmark.Name == "End" && bookmark.TimeMilliseconds == 300);
        replace.ShouldApply.Should().BeTrue();
        replace.Bookmarks[1].Should().Match<MediaBookmarkInfo>(bookmark =>
            bookmark.Name == "Updated" && bookmark.TimeMilliseconds == 250);
        delete.ShouldApply.Should().BeTrue();
        delete.SelectedBookmarkIndex.Should().Be(0);
        delete.Bookmarks.Should().ContainSingle().Which.Name.Should().Be("Intro");
        invalid.ShouldApply.Should().BeFalse();
        invalid.SelectedBookmarkIndex.Should().Be(0);
        media.Bookmarks.Select(bookmark => bookmark.Name).Should().Equal("Intro", "Middle");
    }

    [Fact]
    public void ApplyMethods_CommitThroughEditorAndRunHostCallbacks()
    {
        var (editor, media) = CreateSelectedMediaEditor();
        var callbackCount = 0;
        var session = CreateSession(editor, () => callbackCount++);

        session.ApplyVolume(135).Should().BeTrue();
        session.ApplyPlayback(
            MediaPlaybackStartMode.Automatically,
            loop: true,
            showWhenStopped: false,
            rewindAfterPlaying: true,
            playFullScreen: true,
            stopAfterSlides: 3).Should().BeTrue();
        session.ApplyTiming("125", "250", "500", "750").Should().BeTrue();
        session.ApplyBookmark(
            PresentationMediaBookmarkMutationIntentKind.Create,
            "Chapter",
            "900").Should().BeTrue();

        media.VolumePercent.Should().Be(100);
        media.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.Automatically);
        media.Loop.Should().BeTrue();
        media.ShowWhenStopped.Should().BeFalse();
        media.RewindAfterPlaying.Should().BeTrue();
        media.PlayFullScreen.Should().BeTrue();
        media.StopAfterSlides.Should().Be(3);
        media.TrimStartMilliseconds.Should().Be(125);
        media.TrimEndMilliseconds.Should().Be(250);
        media.FadeInMilliseconds.Should().Be(500);
        media.FadeOutMilliseconds.Should().Be(750);
        media.Bookmarks.Should().ContainSingle().Which.Should().Match<MediaBookmarkInfo>(bookmark =>
            bookmark.Name == "Chapter" && bookmark.TimeMilliseconds == 900);
        session.SelectedBookmarkIndex.Should().Be(0);
        callbackCount.Should().Be(12);
        editor.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void Projection_ExposesAudioOnlyAcrossSlideCapability()
    {
        var (editor, media) = CreateSelectedMediaEditor();
        media.IsVideo = false;

        var plan = CreateSession(editor).BuildProjection();

        plan.CanPlayFullScreen.Should().BeFalse();
        plan.CanStopAfterSlides.Should().BeTrue();
    }

    [Fact]
    public void CaptionAuthoring_OwnsMutationPlanResultAndSelectionLifecycle()
    {
        var (editor, media) = CreateSelectedMediaEditor();
        var callbackCount = 0;
        var session = CreateSession(editor, () => callbackCount++);

        var created = session.ApplyCaptionAuthoring(
            PresentationMediaCaptionAuthoringIntentKind.Create,
            "English",
            "en-US",
            "captions.vtt",
            "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nHello");

        created.Succeeded.Should().BeTrue();
        session.LastCaptionAuthoringMutationPlan.Should().NotBeNull();
        session.LastCaptionTrackMutationResult.Should().BeSameAs(created);
        session.SelectedCaptionTrackIndex.Should().Be(0);
        media.CaptionTracks.Should().ContainSingle();

        session.SelectCaptionTrack(0);
        var deleted = session.ApplyCaptionAuthoring(
            PresentationMediaCaptionAuthoringIntentKind.Delete,
            null,
            null,
            null,
            null);

        deleted.Succeeded.Should().BeTrue();
        media.CaptionTracks.Should().BeEmpty();
        session.SelectedCaptionTrackIndex.Should().BeNull();
        callbackCount.Should().Be(6);
    }

    [Fact]
    public void HostCoordinator_OwnsCaptionVolumePlaybackTimingAndBookmarkTransitions()
    {
        var (editor, media) = CreateSelectedMediaEditor();
        var view = new RecordingMediaPaneHostView();
        var panes = new PresentationWorkareaPaneSession();
        var coordinator = new PresentationMediaPaneHostCoordinator(CreateSession(editor), panes, view);

        var opened = coordinator.Show();
        coordinator.SetCaptionInput(new(
            "English",
            "en-US",
            "captions.vtt",
            "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nHello"));
        coordinator.ApplyCaption(PresentationMediaCaptionAuthoringIntentKind.Create)
            .Succeeded.Should().BeTrue();
        coordinator.SetVolumeInput(135);
        coordinator.ApplyVolume().Should().BeTrue();
        coordinator.SetPlaybackInput(
            MediaPlaybackStartMode.Automatically,
            loop: true,
            showWhenStopped: false,
            rewindAfterPlaying: true,
            playFullScreen: true,
            stopAfterSlides: 3);
        coordinator.ApplyPlayback().Should().BeTrue();
        coordinator.SetTimingInput(125, 250, 500, 750);
        coordinator.ApplyTiming().Should().BeTrue();
        coordinator.SetBookmarkInput("Chapter", 900);
        coordinator.ApplyBookmark(PresentationMediaBookmarkMutationIntentKind.Create).Should().BeTrue();

        opened.ShapeId.Should().Be(42);
        opened.Tracks.Should().BeEmpty();
        coordinator.LastCaptionAuthoringPanePlan!.Tracks.Should().ContainSingle();
        panes.IsVisible(PresentationWorkareaPane.MediaCaption).Should().BeTrue();
        view.IsPaneVisible.Should().BeTrue();
        view.LastRender.Should().NotBeNull();
        view.LastRender!.Playback.StartModeIndex.Should().Be(1);
        media.CaptionTracks.Should().ContainSingle();
        media.VolumePercent.Should().Be(100);
        media.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.Automatically);
        media.Loop.Should().BeTrue();
        media.ShowWhenStopped.Should().BeFalse();
        media.RewindAfterPlaying.Should().BeTrue();
        media.PlayFullScreen.Should().BeTrue();
        media.StopAfterSlides.Should().Be(3);
        media.TrimStartMilliseconds.Should().Be(125);
        media.TrimEndMilliseconds.Should().Be(250);
        media.FadeInMilliseconds.Should().Be(500);
        media.FadeOutMilliseconds.Should().Be(750);
        media.Bookmarks.Should().ContainSingle().Which.Name.Should().Be("Chapter");
    }

    [Fact]
    public void HostCoordinator_OwnsVisibilityAndSuppressesNestedRefreshDuringViewUpdates()
    {
        var (editor, _) = CreateSelectedMediaEditor();
        var view = new RecordingMediaPaneHostView();
        var panes = new PresentationWorkareaPaneSession();
        var coordinator = new PresentationMediaPaneHostCoordinator(CreateSession(editor), panes, view);
        var nestedRefreshes = 0;
        view.DuringRender = () =>
        {
            nestedRefreshes++;
            coordinator.Refresh().Should().BeNull();
            coordinator.SelectCaptionTrack(99);
        };

        coordinator.Show();

        view.RenderCount.Should().Be(1);
        nestedRefreshes.Should().Be(1);
        coordinator.SelectedCaptionTrackIndex.Should().BeNull();
        coordinator.IsUpdating.Should().BeFalse();
        view.Events.Take(3).Should().Equal("render", "visible:true", "accessibility");

        coordinator.Hide();

        panes.IsRequested(PresentationWorkareaPane.MediaCaption).Should().BeFalse();
        view.IsPaneVisible.Should().BeFalse();

        coordinator.SetVolumeInput(45);

        panes.IsVisible(PresentationWorkareaPane.MediaCaption).Should().BeTrue();
        view.IsPaneVisible.Should().BeTrue();
        view.Volume.VolumePercent.Should().Be(45);
        view.RenderCount.Should().Be(2);
        nestedRefreshes.Should().Be(2);
    }

    [Fact]
    public void HostSnapshotPlanner_NormalizesNativeDefaultsAndTriStatePlayback()
    {
        PresentationMediaPaneHostSnapshotPlanner.CaptureVolume(null)
            .NormalizedVolumePercent.Should().Be(80);

        var playback = PresentationMediaPaneHostSnapshotPlanner.CapturePlayback(
            startModeIndex: null,
            loop: null,
            showWhenStopped: null,
            rewindAfterPlaying: null,
            playFullScreen: null,
            stopAfterSlidesText: null);

        playback.StartMode.Should().Be(MediaPlaybackStartMode.InClickSequence);
        playback.Loop.Should().BeFalse();
        playback.ShowWhenStopped.Should().BeTrue();
        playback.RewindAfterPlaying.Should().BeFalse();
        playback.PlayFullScreen.Should().BeFalse();
        playback.StopAfterSlides.Should().Be(1);
    }

    [Fact]
    public void CaptionPanePlan_ResolvesRequiredActionsByStableCommandId()
    {
        var (editor, _) = CreateSelectedMediaEditor();
        var plan = CreateSession(editor).RefreshCaptionAuthoringPanePlan(null, null, null, null);

        plan.GetRequiredAction(PresentationMediaTranscriptPlanner.CaptionAuthoringPaneCloseCommandId)
            .Intent.Should().Be(PresentationMediaCaptionAuthoringIntentKind.Close);
        var missing = () => plan.GetRequiredAction("missing.command");
        missing.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MainWindowSourceGuards_KeepMediaTransitionsInHostCoordinator()
    {
        var root = FindWorkspaceRoot();
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var hostViewAdapter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationMediaPaneControlSurface.cs"));
        var eventRouter = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationMainWindowMediaNativeAdapter.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationMediaPaneHostCoordinator.cs"));
        var mediaSession = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationMediaPaneSession.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("private readonly PresentationMediaPaneHostCoordinator _mediaPaneHostCoordinator;");
            source.Should().Contain("private readonly PresentationMediaPaneHostViewAdapter ");
            source.Should().Contain("DelegatingPresentationMediaPaneControlSurface");
            source.Should().Contain("PresentationMediaPaneFormEventBinder.Bind(");
            source.Should().Contain("new PresentationMediaPaneFormEventRouter(_mediaPaneHostCoordinator)");
            source.Should().Contain("CaptureMediaCaptionHostSnapshot()");
            source.Should().Contain("CaptureMediaVolumeHostSnapshot()");
            source.Should().Contain("CaptureMediaPlaybackHostSnapshot()");
            source.Should().Contain("CaptureMediaTimingHostSnapshot()");
            source.Should().Contain("CaptureMediaBookmarkHostSnapshot()");
            source.Should().Contain("PresentationMediaPaneVisualMetrics.PaneWidth");
            source.Should().Contain("PresentationMediaPaneVisualMetrics.ContentSideMargin");
            source.Should().Contain("PresentationMediaPaneVisualMetrics.CompactControlHeight");
            source.Should().Contain("PresentationMediaPaneVisualMetrics.ActionButtonMinimumWidth");
            source.Should().Contain("PresentationMediaPaneSession.MinimumVolumePercent");
            source.Should().Contain("PresentationMediaPaneSession.MaximumVolumePercent");
            source.Should().Contain("PresentationMediaPaneSession.VolumeTickFrequency");
            source.Should().Contain("IsSnapToTickEnabled = PresentationMediaPaneSession.SnapVolumeToTicks");
            source.Should().NotContain("PresentationMediaPaneSession.DefaultStopAfterSlides");
            source.Should().NotContain("PresentationMediaPaneHostSnapshotPlanner.CaptureCaption(");
            source.Should().NotContain("PresentationMediaPaneHostSnapshotPlanner.CaptureVolume(");
            source.Should().NotContain("PresentationMediaPaneHostSnapshotPlanner.CapturePlayback(");
            source.Should().NotContain("PresentationMediaPaneHostSnapshotPlanner.CaptureTiming(");
            source.Should().NotContain("PresentationMediaPaneHostSnapshotPlanner.CaptureBookmark(");
            source.Should().NotContain("_mediaPaneHostCoordinator.ApplyCaption(");
            source.Should().NotContain("_mediaPaneHostCoordinator.ApplyVolume(");
            source.Should().NotContain("_mediaPaneHostCoordinator.ApplyPlayback(");
            source.Should().NotContain("_mediaPaneHostCoordinator.ApplyTiming(");
            source.Should().NotContain("_mediaPaneHostCoordinator.ApplyBookmark(");
            source.Should().NotContain("plan.GetRequiredAction(");
            source.Should().NotContain("GetMediaCaptionPaneAction(");
            source.Should().NotContain("ShowMediaCaptionPane(");
            source.Should().NotContain("HideMediaCaptionPane(");
            source.Should().NotContain("SetMediaCaptionPaneInput(");
            source.Should().NotContain("SetMediaVolumePaneInput(");
            source.Should().NotContain("SetMediaPlaybackPaneInput(");
            source.Should().NotContain("ApplyMediaCaptionPane(");
            source.Should().NotContain("ApplyMediaVolumePane(");
            source.Should().NotContain("ApplyMediaPlaybackPane(");
            source.Should().NotContain("SetMediaTimingPaneInput(");
            source.Should().NotContain("ApplyMediaTimingPane(");
            source.Should().NotContain("SetMediaBookmarkPaneInput(");
            source.Should().NotContain("ApplyMediaBookmarkCreatePane(");
            source.Should().NotContain("ApplyMediaBookmarkReplacePane(");
            source.Should().NotContain("ApplyMediaBookmarkDeletePane(");
            source.Should().NotContain("_mediaPaneSession");
            source.Should().NotContain("_mediaCaptionPaneRefreshing");
            source.Should().NotContain("_mediaPaneHostCoordinator.BuildRenderPlan(");
            source.Should().NotContain("_workareaSession.Panes.Show(PresentationWorkareaPane.MediaCaption)");
            source.Should().NotContain("_workareaSession.Panes.Hide(PresentationWorkareaPane.MediaCaption)");
            source.Should().NotContain("PresentationMediaPaneSession.BuildPlaybackInputPlan(");
            source.Should().NotContain("PresentationMediaPaneSession.ParseStopAfterSlides(");
            source.Should().NotContain("RenderMediaCaptionPane(");
            source.Should().NotContain("RenderMediaBookmarkOptions(");
            source.Should().NotContain("private static double ParseMediaTiming(");
            source.Should().NotContain("private static string FormatMediaTiming(");
            source.Should().NotContain("CloneMediaBookmarksForPane(");
            source.Should().NotContain("NormalizeMediaCaptionSelectionAfterMutation(");
            source.Should().NotContain("Editor.SetSelectedMediaVolume(");
            source.Should().NotContain("Editor.SetSelectedMediaPlaybackOptions(");
            source.Should().NotContain("Editor.SetSelectedMediaTiming(");
            source.Should().NotContain("Editor.SetSelectedMediaBookmarks(");
            source.Should().NotContain("Editor.ApplyMediaCaptionAuthoring(");
        }

        hostViewAdapter.Should().Contain("PresentationMediaPaneHostViewAdapter : IPresentationMediaPaneHostView")
            .And.Contain("PresentationMediaPaneHostSnapshotPlanner.CaptureCaption(")
            .And.Contain("PresentationMediaPaneHostSnapshotPlanner.CaptureVolume(")
            .And.Contain("PresentationMediaPaneHostSnapshotPlanner.CapturePlayback(")
            .And.Contain("PresentationMediaPaneHostSnapshotPlanner.CaptureTiming(")
            .And.Contain("PresentationMediaPaneHostSnapshotPlanner.CaptureBookmark(")
            .And.Contain("_surface.RenderCaptionTracks(caption)")
            .And.Contain("_surface.RenderCaptionField(")
            .And.Contain("_surface.RenderBookmarks(media)")
            .And.Contain("caption.GetRequiredAction(");

        eventRouter.Should().Contain("PresentationMediaPaneFormEventRouter : IPresentationMediaPaneFormEventRouter")
            .And.Contain("_coordinator.ApplyCaption(")
            .And.Contain("_coordinator.ApplyVolume()")
            .And.Contain("_coordinator.ApplyPlayback()")
            .And.Contain("_coordinator.ApplyTiming()")
            .And.Contain("_coordinator.ApplyBookmark(");

        coordinator.Should().Contain("private readonly IPresentationMediaPaneHostView _view;")
            .And.Contain("public PresentationMediaPaneHostRenderPlan BuildRenderPlan(")
            .And.Contain("public PresentationMediaCaptionTrackMutationResult ApplyCaption(")
            .And.Contain("public bool ApplyVolume()")
            .And.Contain("public bool ApplyPlayback()")
            .And.Contain("public bool ApplyTiming()")
            .And.Contain("public bool ApplyBookmark(")
            .And.NotContain("using System.Windows")
            .And.NotContain("using Avalonia");

        mediaSession.Should().Contain("public const int MinimumVolumePercent = 0;")
            .And.Contain("public const int MaximumVolumePercent = 100;")
            .And.Contain("public const int VolumeTickFrequency = 10;")
            .And.Contain("public const bool SnapVolumeToTicks = true;")
            .And.Contain("public const int DefaultStopAfterSlides = 1;");
    }

    private static PresentationMediaPaneSession CreateSession(
        EditingSession editor,
        Action? callback = null)
    {
        callback ??= () => { };
        return new PresentationMediaPaneSession(
            () => editor,
            new PresentationMediaPaneSessionCallbacks(callback, callback, callback));
    }

    private static (EditingSession Editor, MediaInfo Media) CreateSelectedMediaEditor()
    {
        var presentation = Presentation.CreateEmpty();
        var media = new MediaInfo { IsVideo = true };
        var shape = new SlideShape
        {
            Id = 42,
            Name = "Video",
            Kind = SlideShapeKind.Media,
            Media = media
        };
        presentation.Slides[0].Shapes.Add(shape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(shape.Id);
        return (editor, media);
    }

    private sealed class RecordingMediaPaneHostView : IPresentationMediaPaneHostView
    {
        public bool IsPaneVisible { get; private set; }

        public PresentationMediaCaptionHostSnapshot Caption { get; private set; } =
            new(null, null, null, null);

        public PresentationMediaVolumeHostSnapshot Volume { get; private set; } = new(80);

        public PresentationMediaPlaybackHostSnapshot Playback { get; private set; } =
            new(0, false, true, false, false, "1");

        public PresentationMediaTimingHostSnapshot Timing { get; private set; } =
            new("0", "0", "0", "0");

        public PresentationMediaBookmarkHostSnapshot Bookmark { get; private set; } =
            new(string.Empty, "0");

        public PresentationMediaPaneHostRenderPlan? LastRender { get; private set; }

        public int RenderCount { get; private set; }

        public Action? DuringRender { get; set; }

        public List<string> Events { get; } = [];

        public PresentationMediaCaptionHostSnapshot CaptureCaption() => Caption;

        public PresentationMediaVolumeHostSnapshot CaptureVolume() => Volume;

        public PresentationMediaPlaybackHostSnapshot CapturePlayback() => Playback;

        public PresentationMediaTimingHostSnapshot CaptureTiming() => Timing;

        public PresentationMediaBookmarkHostSnapshot CaptureBookmark() => Bookmark;

        public void SetPaneVisible(bool visible)
        {
            IsPaneVisible = visible;
            Events.Add($"visible:{visible.ToString().ToLowerInvariant()}");
        }

        public void SetCaptionInput(PresentationMediaCaptionHostSnapshot input) => Caption = input;

        public void SetVolumeInput(PresentationMediaVolumeInputPlan input) =>
            Volume = new(input.VolumePercent);

        public void SetPlaybackInput(PresentationMediaPlaybackInputPlan input) =>
            Playback = new(
                input.StartModeIndex,
                input.Loop,
                input.ShowWhenStopped,
                input.RewindAfterPlaying,
                input.PlayFullScreen,
                input.StopAfterSlidesText);

        public void SetTimingInput(PresentationMediaTimingInputPlan input) =>
            Timing = new(input.TrimStartText, input.TrimEndText, input.FadeInText, input.FadeOutText);

        public void SetBookmarkInput(PresentationMediaBookmarkInputPlan input) =>
            Bookmark = new(input.Name, input.TimeText);

        public void Render(PresentationMediaPaneHostRenderPlan plan)
        {
            LastRender = plan;
            RenderCount++;
            Events.Add("render");
            Caption = new(
                plan.Caption.Label.Value,
                plan.Caption.Language.Value,
                plan.Caption.Source.Value,
                plan.Caption.TranscriptText.Value);
            Volume = new(plan.Media.VolumePercent);
            Playback = new(
                plan.Playback.StartModeIndex,
                plan.Playback.Loop,
                plan.Playback.ShowWhenStopped,
                plan.Playback.RewindAfterPlaying,
                plan.Playback.PlayFullScreen,
                plan.Playback.StopAfterSlidesText);
            Timing = new(
                plan.Media.Timing.TrimStartText,
                plan.Media.Timing.TrimEndText,
                plan.Media.Timing.FadeInText,
                plan.Media.Timing.FadeOutText);
            Bookmark = new(plan.Media.BookmarkName, plan.Media.BookmarkTimeText);
            DuringRender?.Invoke();
        }

        public void RefreshAccessibilityMetadata() => Events.Add("accessibility");
    }

    private static string FindWorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
}
