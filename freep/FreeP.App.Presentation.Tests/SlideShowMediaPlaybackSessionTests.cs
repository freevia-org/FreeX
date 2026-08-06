using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowMediaPlaybackSessionTests
{
    [Fact]
    public void Register_AppliesTrimFadeVolumeAndAutoplayState()
    {
        var session = new SlideShowMediaPlaybackSession();
        var port = new FakePlaybackPort
        {
            Duration = TimeSpan.FromSeconds(10),
        };
        var media = new MediaInfo
        {
            IsVideo = true,
            PlaybackStartMode = MediaPlaybackStartMode.Automatically,
            PlayFullScreen = true,
            VolumePercent = 80,
            TrimStartMilliseconds = 1000,
            FadeInMilliseconds = 2000,
        };

        var handle = session.Register(42, media, port);
        var snapshot = session.Snapshot(handle);

        port.LastSeek.Should().Be(TimeSpan.FromSeconds(1));
        port.PlayCount.Should().Be(1);
        port.VolumePercent.Should().Be(0);
        snapshot.IsPlaying.Should().BeTrue();
        snapshot.ShowVisual.Should().BeTrue();
        snapshot.UseFullScreen.Should().BeTrue();
        snapshot.BaseVolumePercent.Should().Be(80);
        session.RequiresPeriodicUpdate(handle).Should().BeTrue();
    }

    [Fact]
    public void ClickSeekBookmarkAndVolume_UseOneSharedStateMachine()
    {
        var session = new SlideShowMediaPlaybackSession();
        var port = new FakePlaybackPort { Duration = TimeSpan.FromSeconds(10) };
        var media = new MediaInfo
        {
            IsVideo = true,
            ShowWhenStopped = true,
            TrimStartMilliseconds = 1000,
            TrimEndMilliseconds = 2000,
            VolumePercent = 70,
            Bookmarks = { new MediaBookmarkInfo { Name = "Cue", TimeMilliseconds = 4000 } },
        };
        session.Register(7, media, port);

        session.TryHandleClick(7, out var playing).Should().BeTrue();
        playing!.IsPlaying.Should().BeTrue();
        session.TryHandleClick(7, out var paused).Should().BeTrue();
        paused!.IsPlaying.Should().BeFalse();
        paused.ShowVisual.Should().BeTrue();

        session.TrySeek(7, TimeSpan.Zero, out _).Should().BeTrue();
        port.LastSeek.Should().Be(TimeSpan.FromSeconds(1));
        session.TrySeek(7, TimeSpan.FromSeconds(20), out _).Should().BeTrue();
        port.LastSeek.Should().Be(TimeSpan.FromSeconds(8));
        session.TrySeekToBookmark(7, " cue ", out _).Should().BeTrue();
        port.LastSeek.Should().Be(TimeSpan.FromSeconds(4));
        session.TrySeekToBookmark(7, "missing", out _).Should().BeFalse();

        session.TrySetVolume(7, 150, out var volume).Should().BeTrue();
        volume!.BaseVolumePercent.Should().Be(100);
        port.VolumePercent.Should().Be(100);
        session.TrySeek(999, TimeSpan.Zero, out _).Should().BeFalse();
        session.TrySetVolume(999, 50, out _).Should().BeFalse();
    }

    [Fact]
    public void EnterSlide_RetainsOnlyContiguousMultiSlideAudio()
    {
        var session = new SlideShowMediaPlaybackSession();
        session.EnterSlide(0).IsContiguous.Should().BeFalse();
        var audioPort = new FakePlaybackPort();
        var videoPort = new FakePlaybackPort();
        var audio = session.Register(1, new MediaInfo
        {
            IsVideo = false,
            StopAfterSlides = 3,
        }, audioPort);
        var video = session.Register(2, new MediaInfo
        {
            IsVideo = true,
            StopAfterSlides = 3,
        }, videoPort);

        var next = session.EnterSlide(1);

        next.IsContiguous.Should().BeTrue();
        next.Retained.Should().Equal(audio);
        next.Released.Should().Equal(video);
        audio.RemainingSlides.Should().Be(2);
        videoPort.StopCount.Should().Be(1);

        session.EnterSlide(2).Retained.Should().Equal(audio);
        var expired = session.EnterSlide(3);
        expired.Released.Should().Equal(audio);
        audioPort.StopCount.Should().Be(1);

        var replacementPort = new FakePlaybackPort();
        session.Register(3, new MediaInfo { IsVideo = false, StopAfterSlides = 5 }, replacementPort);
        var discontinuous = session.EnterSlide(9);
        discontinuous.IsContiguous.Should().BeFalse();
        discontinuous.Released.Should().ContainSingle();
        replacementPort.StopCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true, false, SlideShowMediaEndAction.Loop)]
    [InlineData(false, true, SlideShowMediaEndAction.Rewind)]
    [InlineData(false, false, SlideShowMediaEndAction.Stop)]
    public void HandleEnded_AppliesAuthoredEndAction(
        bool loop,
        bool rewind,
        SlideShowMediaEndAction expected)
    {
        var session = new SlideShowMediaPlaybackSession();
        var port = new FakePlaybackPort
        {
            Duration = TimeSpan.FromSeconds(10),
        };
        var handle = session.Register(8, new MediaInfo
        {
            IsVideo = true,
            Loop = loop,
            RewindAfterPlaying = rewind,
            TrimStartMilliseconds = 1000,
        }, port);
        port.Position = port.Duration;
        port.IsPlaying = true;

        var snapshot = session.HandleEnded(handle);

        snapshot.EndAction.Should().Be(expected);
        if (expected == SlideShowMediaEndAction.Loop)
        {
            port.PlayCount.Should().Be(1);
            snapshot.IsPlaying.Should().BeTrue();
        }
        else
        {
            port.PauseCount.Should().Be(1);
            snapshot.IsPlaying.Should().BeFalse();
        }
        if (expected == SlideShowMediaEndAction.Rewind)
            port.LastSeek.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void EnforcePlaybackState_StopsAtTrimEndAndTeardownIsIdempotent()
    {
        var session = new SlideShowMediaPlaybackSession();
        session.EnterSlide(0);
        var port = new FakePlaybackPort
        {
            Duration = TimeSpan.FromSeconds(10),
        };
        session.Register(9, new MediaInfo
        {
            IsVideo = true,
            TrimEndMilliseconds = 5000,
        }, port);
        port.Position = TimeSpan.FromSeconds(6);
        port.IsPlaying = true;

        var updates = session.EnforcePlaybackState();

        updates.Should().ContainSingle().Which.EndAction.Should().Be(SlideShowMediaEndAction.Stop);
        port.PauseCount.Should().Be(1);
        session.Teardown().Should().ContainSingle();
        port.StopCount.Should().Be(1);
        session.Teardown().Should().BeEmpty();
        port.StopCount.Should().Be(1);
    }

    private sealed class FakePlaybackPort : IMediaPlaybackPort
    {
        public bool IsPlaying { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(1);
        public int VolumePercent { get; set; } = 100;
        public int PlayCount { get; private set; }
        public int PauseCount { get; private set; }
        public int StopCount { get; private set; }
        public TimeSpan? LastSeek { get; private set; }

        public void Play()
        {
            PlayCount++;
            IsPlaying = true;
        }

        public void Pause()
        {
            PauseCount++;
            IsPlaying = false;
        }

        public void Stop()
        {
            StopCount++;
            IsPlaying = false;
        }

        public bool Seek(TimeSpan position)
        {
            LastSeek = position;
            Position = position;
            return true;
        }
    }
}
