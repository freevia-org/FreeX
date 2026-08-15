using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowRendererShellDedupTests
{
    [Fact]
    public void PresenterActionProjection_PreservesToolbarOrderAndDynamicState()
    {
        SlideShowPresenterViewActionProjection.HeaderItems
            .Select(item => item.Kind == SlideShowPresenterViewHeaderItemKind.Action
                ? item.Action!.Value.ToString()
                : item.Kind.ToString())
            .Should()
            .Equal(
                "Previous",
                "Next",
                "SlideNumber",
                "GoToSlide",
                "RecordTimings",
                "RehearseTimings",
                "Narration",
                "NarrationAndMedia",
                "ApplyRecording",
                "ShowScreen",
                "BlackScreen",
                "WhiteScreen",
                "ClearInk",
                "PointerMode");

        var plan = CreatePresenterPlan();
        var states = SlideShowPresenterViewActionProjection.Build(
            plan,
            canGoBack: true,
            canAdvance: false,
            canGoToSlide: true,
            canSetScreenMode: false,
            canClearInk: true).ToDictionary(state => state.Action);

        states[SlideShowPresenterViewAction.Previous].IsEnabled.Should().BeTrue();
        states[SlideShowPresenterViewAction.Next].IsEnabled.Should().BeFalse();
        states[SlideShowPresenterViewAction.RecordTimings].Label.Should().Be("Stop recording");
        states[SlideShowPresenterViewAction.RecordTimings].IsEnabled.Should().BeFalse();
        states[SlideShowPresenterViewAction.Narration].Label.Should().Be("Stop narration");
        states[SlideShowPresenterViewAction.Narration].IsEnabled.Should().BeTrue();
        states[SlideShowPresenterViewAction.ShowScreen].IsEnabled.Should().BeFalse();
        states[SlideShowPresenterViewAction.ClearInk].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void NativePresenterWindowHost_CentralizesOpenRefreshAndCloseLifecycle()
    {
        var created = 0;
        var shown = 0;
        var refreshed = 0;
        var closed = 0;
        var notified = 0;
        var host = new SlideShowNativePresenterWindowHost<FakePresenterWindow>(
            _ =>
            {
                created++;
                return new FakePresenterWindow();
            },
            (window, handler) => window.Closed = handler,
            _ => shown++,
            window =>
            {
                closed++;
                window.Closed?.Invoke();
            },
            _ => refreshed++,
            () => notified++);
        var operations = new SlideShowPresenterViewOperations(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

        host.Open(operations);
        host.Open(operations);
        host.Refresh();
        host.Close();
        host.Refresh();

        created.Should().Be(1);
        shown.Should().Be(1);
        refreshed.Should().Be(2);
        closed.Should().Be(1);
        notified.Should().Be(1);
        host.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void MediaCommandCoordinator_ForwardsCommandSnapshotsToNativeSurface()
    {
        var snapshots = new List<SlideShowMediaPlaybackSnapshot>();
        var session = new SlideShowMediaPlaybackSession();
        var port = new FakePlaybackPort { Duration = TimeSpan.FromSeconds(10) };
        session.Register(12, new MediaInfo
        {
            IsVideo = true,
            VolumePercent = 70,
            Bookmarks = { new MediaBookmarkInfo { Name = "Cue", TimeMilliseconds = 2500 } },
        }, port);
        var coordinator = new SlideShowMediaPlaybackCommandCoordinator(snapshots.Add, session);

        coordinator.TrySeekToBookmark(12, " cue ").Should().BeTrue();
        coordinator.TrySetVolume(12, 35).Should().BeTrue();
        coordinator.TrySeek(99, TimeSpan.Zero).Should().BeFalse();

        port.Position.Should().Be(TimeSpan.FromMilliseconds(2500));
        port.VolumePercent.Should().Be(35);
        snapshots.Should().HaveCount(2);
        snapshots[^1].ShapeId.Should().Be(12);
        snapshots[^1].BaseVolumePercent.Should().Be(35);
    }

    [Fact]
    public void Native_renderer_sources_keep_residual_composition_in_shared_sessions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var chartEndpoints = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "RendererShared",
            "MainWindow.ChartDialogEndpoints.cs"));
        chartEndpoints.Should().Contain("private void OpenChartDialog(");
        chartEndpoints.Should().Contain("private void OnChartPointDoubleClick(");
        foreach (var project in new[] { "FreeP.App.Host", "FreeP.App.Avalonia" })
        {
            var directory = Path.Combine(root, "freep", project);
            var mainWindow = File.ReadAllText(Path.Combine(directory, "MainWindow.cs"));
            var presenter = File.ReadAllText(Path.Combine(directory, "PresenterViewWindow.cs"));
            var customShows = File.ReadAllText(Path.Combine(directory, "CustomShowDialog.cs"));
            var selectionPane = File.ReadAllText(Path.Combine(directory, "SelectionPane.cs"));

            mainWindow.Should().Contain("PresentationMediaPaneNativeComposition.Compose(");
            mainWindow.Should().NotContain("new DelegatingPresentationMediaPaneControlSurface");
            mainWindow.Should().NotContain("internal void OpenChartDataDialog(");
            mainWindow.Should().NotContain("private void OnChartPointDoubleClick(");
            presenter.Should().Contain("SlideShowPresenterViewNativeBinding<");
            presenter.Should().Contain("SlideShowPresenterViewHeaderComposition.Compose(");
            customShows.Should().Contain("SlideShowCustomShowDialogNativeComposition<");
            selectionPane.Should().Contain("PresentationSelectionPaneFormSession<");
            selectionPane.Should().Contain("PresentationSelectionPaneItemFormSession(");
        }
    }

    [Fact]
    public void Native_media_controllers_consume_shared_projection_plans()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        foreach (var relativePath in new[]
                 {
                     Path.Combine("freep", "FreeP.App.Host", "SlideShowMediaController.cs"),
                     Path.Combine("freep", "FreeP.App.Avalonia", "AvaloniaSlideShowMediaController.cs"),
                 })
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            source.Should().Contain("SlideShowMediaInteractionPlanner.PlanPlaybackProjection(");
            source.Should().Contain("SlideShowMediaInteractionPlanner.PlanCaptionProjection(");
            source.Should().NotContain("PresentationMediaTranscriptPlanner.FindActiveCue(");
            source.Should().NotContain("SlideShapeTraversal.FindById(");
        }
    }

    [Fact]
    public void Slideshow_portable_surface_is_source_shared_by_both_renderers()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var portableSurface = File.ReadAllText(Path.Combine(
            root, "freep", "RendererShared", "SlideShowWindow.PortableSurface.cs"));

        portableSurface.Should().Contain("public SlideShowWindow(Presentation presentation, int startIndex = 0)");
        portableSurface.Should().Contain("private void CloseSlideShow(DateTimeOffset nowUtc)");
        portableSurface.Should().Contain("private void DisplayCurrentSlide(");
        portableSurface.Should().Contain("BuildAnimationTargetAvailability()");

        foreach (var project in new[] { "FreeP.App.Host", "FreeP.App.Avalonia" })
        {
            var projectDirectory = Path.Combine(root, "freep", project);
            var projectSource = File.ReadAllText(Path.Combine(projectDirectory, $"{project}.csproj"));
            var windowSource = File.ReadAllText(Path.Combine(projectDirectory, "SlideShowWindow.cs"));

            projectSource.Should().Contain("RendererShared\\SlideShowWindow.PortableSurface.cs");
            windowSource.Should().NotContain("public SlideShowWindow(Presentation presentation, int startIndex = 0)");
            windowSource.Should().NotContain("private void CloseSlideShow(DateTimeOffset nowUtc)");
            windowSource.Should().NotContain("private void DisplayCurrentSlide(");
            windowSource.Should().NotContain("_animationTargets.BuildAvailability()");
        }
    }

    private static SlideShowPresenterViewPlan CreatePresenterPlan() =>
        new(
            StatusText: "Slide 1 of 2",
            CurrentSlideLabel: "Slide 1",
            NextSlideLabel: "Slide 2",
            NotesText: string.Empty,
            ElapsedText: "00:01",
            CurrentSlideNumber: 1,
            CurrentSlide: null,
            NextSlide: null,
            CanGoBack: true,
            CanAdvance: false,
            PointerMode: SlideShowPresenterPointerMode.Arrow,
            IsRecordingTimings: true,
            IsRehearsingTimings: false,
            RecordTimingsButtonText: "Stop recording",
            RehearseTimingsButtonText: "Rehearse timings",
            NarrationButtonText: "Stop narration",
            NarrationAndMediaButtonText: "Narration + camera",
            RecordingStatusText: string.Empty,
            CanSetTimingIntent: false,
            CanSetMediaIntent: true,
            CanApplyRecording: true);

    private sealed class FakePresenterWindow
    {
        public Action? Closed { get; set; }
    }

    private sealed class FakePlaybackPort : IMediaPlaybackPort
    {
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; private set; }
        public TimeSpan Duration { get; init; }
        public int VolumePercent { get; set; }

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;

        public bool Seek(TimeSpan position)
        {
            Position = position;
            return true;
        }
    }
}
