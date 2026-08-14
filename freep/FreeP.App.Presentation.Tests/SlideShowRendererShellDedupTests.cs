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
