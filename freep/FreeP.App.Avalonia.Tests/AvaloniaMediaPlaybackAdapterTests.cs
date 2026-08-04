using Avalonia.Controls;
using Free.Shared.Drawing;
using FreeP.App.Avalonia;
using FreeP.App.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class AvaloniaMediaPlaybackAdapterTests
{
    [Fact]
    public void DefaultFactory_ProbesNativeRuntimeWithoutThrowing()
    {
        var availability = new LibVlcMediaPlaybackBackendFactory().Probe();

        availability.Capabilities.BackendName.Should().Be("LibVLC");
        if (availability.IsAvailable)
        {
            availability.Capabilities.Audio.Should().BeTrue();
            availability.Capabilities.Video.Should().BeTrue();
            availability.Capabilities.VideoSurface.Should().BeTrue();
            availability.Capabilities.Seek.Should().BeTrue();
            availability.Capabilities.Volume.Should().BeTrue();
        }
        else
        {
            availability.Capabilities.Audio.Should().BeFalse();
            availability.Capabilities.Video.Should().BeFalse();
            availability.Capabilities.VideoSurface.Should().BeFalse();
            availability.Capabilities.Seek.Should().BeFalse();
            availability.Capabilities.Volume.Should().BeFalse();
            availability.FailureReason.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Controller_UsesInjectedBackendForMediaAndTransitionSoundLifecycle()
    {
        var factory = new FakeBackendFactory();
        var overlay = new Canvas();
        var controller = new AvaloniaSlideShowMediaController(overlay, factory);
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 6858000,
            Media = new MediaInfo
            {
                IsVideo = false,
                PlaybackStartMode = MediaPlaybackStartMode.Automatically,
                Loop = true,
                Bytes = new byte[] { 1, 2, 3 },
                ContentType = "audio/wav",
            },
        });

        controller.EnterSlide(slide, 960, 720, 960, 720);
        overlay.Width.Should().Be(960);
        overlay.Height.Should().Be(720);
        controller.SetCanvasBounds(1280, 720);
        overlay.Width.Should().Be(1280);
        overlay.Height.Should().Be(720);

        var mediaSession = factory.Backend.Sessions[0];
        mediaSession.OpenCount.Should().Be(1);
        mediaSession.PlayCount.Should().Be(1);
        mediaSession.LastSource!.Loop.Should().BeTrue();
        controller.Availability!.IsAvailable.Should().BeTrue();

        controller.TryHandleClick(slide, 960, 720, 960, 720, 480, 360).Should().BeTrue();
        mediaSession.PauseCount.Should().Be(1);
        controller.TryHandleClick(slide, 960, 720, 960, 720, 480, 360).Should().BeTrue();
        mediaSession.PlayCount.Should().Be(2);
        controller.TrySeek(42, TimeSpan.FromSeconds(3)).Should().BeTrue();
        controller.TrySeek(42, TimeSpan.FromSeconds(-1)).Should().BeFalse();
        controller.TrySetVolume(42, 35).Should().BeTrue();
        mediaSession.LastSeek.Should().Be(TimeSpan.FromSeconds(3));
        mediaSession.Volume.Should().Be(35);

        controller.PlayTransitionSound(new TransitionSound
        {
            AudioBytes = new byte[] { 4, 5, 6 },
            ContentType = "audio/wav",
            Loop = true,
        }).Should().BeTrue();
        factory.Backend.Sessions.Should().HaveCount(2);
        factory.Backend.Sessions[1].PlayCount.Should().Be(1);
        factory.Backend.Sessions[1].LastSource!.Loop.Should().BeTrue();

        controller.Teardown();
        mediaSession.Disposed.Should().BeTrue();
        factory.Backend.Sessions[1].Disposed.Should().BeTrue();
        factory.Backend.Disposed.Should().BeTrue();
        overlay.Children.Should().BeEmpty();
        controller.Active.Should().BeEmpty();
    }

    [Fact]
    public void Controller_ResolvesGroupedMediaForPlaybackAndResize()
    {
        var factory = new FakeBackendFactory();
        var controller = new AvaloniaSlideShowMediaController(new Canvas(), factory);
        var slide = new Slide();
        var group = new SlideShape { Id = 10, Kind = SlideShapeKind.Group };
        group.Children.Add(new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 6858000,
            Media = new MediaInfo
            {
                IsVideo = false,
                PlaybackStartMode = MediaPlaybackStartMode.Automatically,
                Loop = true,
                Bytes = [1, 2, 3],
                ContentType = "audio/wav",
            },
        });
        slide.Shapes.Add(group);

        controller.EnterSlide(slide, 960, 720, 960, 720);

        controller.Active.Should().ContainSingle(plan => plan.ShapeId == 42);
        factory.Backend.Sessions.Should().ContainSingle();
        factory.Backend.Sessions[0].PlayCount.Should().Be(1);

        controller.UpdateLayout(slide, 960, 720, 1280, 720);
        factory.Backend.Sessions[0].OpenCount.Should().Be(1);
    }

    [Fact]
    public void Controller_DoesNotAutoPlayClickSequenceMedia()
    {
        var factory = new FakeBackendFactory();
        var controller = new AvaloniaSlideShowMediaController(new Canvas(), factory);
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 6858000,
            Media = new MediaInfo
            {
                IsVideo = true,
                Loop = true,
                Bytes = [1, 2, 3],
                ContentType = "video/mp4",
            },
        });

        controller.EnterSlide(slide, 960, 720, 960, 720);
        var mediaSession = factory.Backend.Sessions.Single();
        mediaSession.PlayCount.Should().Be(0);

        controller.TryHandleClick(slide, 960, 720, 960, 720, 480, 360).Should().BeTrue();
        mediaSession.PlayCount.Should().Be(1);
        mediaSession.LastSource!.Loop.Should().BeTrue();
    }

    [Fact]
    public void Controller_UsesTopmostOverlappingMediaShapeForClicks()
    {
        var factory = new FakeBackendFactory();
        var overlay = new Canvas();
        var controller = new AvaloniaSlideShowMediaController(overlay, factory);
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.Media,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 6858000,
            Media = new MediaInfo { IsVideo = true, Bytes = [1, 2, 3], ContentType = "video/mp4" },
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 20,
            Kind = SlideShapeKind.Media,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 6858000,
            Media = new MediaInfo { IsVideo = true, Bytes = [4, 5, 6], ContentType = "video/mp4" },
        });

        controller.EnterSlide(slide, 960, 720, 960, 720, Array.Empty<PresentationMediaTranscriptTrackDescriptor>());

        controller.TryHandleClick(slide, 960, 720, 960, 720, 480, 360).Should().BeTrue();
        controller.LastClick.Media!.ShapeId.Should().Be(20);
    }

    [Fact]
    public void Controller_RefreshesCaptionOverlayFromPlaybackPosition()
    {
        var factory = new FakeBackendFactory();
        var overlay = new Canvas();
        var controller = new AvaloniaSlideShowMediaController(overlay, factory);
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 6858000,
            Media = new MediaInfo
            {
                IsVideo = true,
                PlaybackStartMode = MediaPlaybackStartMode.Automatically,
                Bytes = new byte[] { 1, 2, 3 },
                ContentType = "video/mp4",
            },
        });
        var track = new PresentationMediaTranscriptTrackDescriptor(
            SlideIndex: 0,
            ShapeId: 42,
            ShapeName: "Video",
            TrackIndex: 0,
            Label: "English",
            Language: "en-US",
            Source: "captions.vtt",
            ContentType: "text/vtt",
            Status: PresentationMediaTranscriptTrackStatus.Available,
            StatusMessage: string.Empty,
            Cues: [new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hello from the video")]);

        controller.EnterSlide(slide, 960, 720, 960, 720, [track]);
        var session = factory.Backend.Sessions[0];
        session.Seek(TimeSpan.FromMilliseconds(500));
        controller.RefreshCaptionsForTest();
        controller.CaptionTextForTest(42).Should().Be("Hello from the video");
        overlay.Children.OfType<Border>().Should().Contain(border => border.IsVisible);

        session.Seek(TimeSpan.FromSeconds(2));
        controller.RefreshCaptionsForTest();
        controller.CaptionTextForTest(42).Should().BeEmpty();
        overlay.Children.OfType<Border>().Should().NotContain(border => border.IsVisible);

        controller.Teardown();
    }

    [Fact]
    public void Controller_UsesAuthoredWebVttCuePlacement()
    {
        var factory = new FakeBackendFactory();
        var overlay = new Canvas();
        var controller = new AvaloniaSlideShowMediaController(overlay, factory);
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 6858000,
            Media = new MediaInfo
            {
                IsVideo = true,
                PlaybackStartMode = MediaPlaybackStartMode.Automatically,
                Bytes = [1, 2, 3],
                ContentType = "video/mp4",
            },
        });
        var track = new PresentationMediaTranscriptTrackDescriptor(
            SlideIndex: 0,
            ShapeId: 42,
            ShapeName: "Video",
            TrackIndex: 0,
            Label: "English",
            Language: "en-US",
            Source: "captions.vtt",
            ContentType: "text/vtt",
            Status: PresentationMediaTranscriptTrackStatus.Available,
            StatusMessage: string.Empty,
            Cues:
            [
                new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Positioned")
                {
                    PositionPercent = 25,
                    LinePercent = 30,
                    SizePercent = 50,
                    Alignment = PresentationMediaTranscriptCueAlignment.Start
                }
            ]);

        controller.EnterSlide(slide, 960, 720, 960, 720, [track]);
        factory.Backend.Sessions.Single().Seek(TimeSpan.FromMilliseconds(500));
        controller.RefreshCaptionsForTest();

        var caption = overlay.Children.OfType<Border>().Single();
        Canvas.GetLeft(caption).Should().Be(240);
        Canvas.GetTop(caption).Should().Be(216);
        caption.Width.Should().Be(480);
        caption.Height.Should().Be(86);
    }

    [Fact]
    public void Controller_UpdateLayout_RepositionsCaptionOverlayAfterCanvasResize()
    {
        var factory = new FakeBackendFactory();
        var overlay = new Canvas();
        var controller = new AvaloniaSlideShowMediaController(overlay, factory);
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 6858000,
            Media = new MediaInfo
            {
                IsVideo = true,
                PlaybackStartMode = MediaPlaybackStartMode.Automatically,
                Bytes = [1, 2, 3],
                ContentType = "video/mp4",
            },
        });
        var track = new PresentationMediaTranscriptTrackDescriptor(
            SlideIndex: 0,
            ShapeId: 42,
            ShapeName: "Video",
            TrackIndex: 0,
            Label: "English",
            Language: "en-US",
            Source: "captions.vtt",
            ContentType: "text/vtt",
            Status: PresentationMediaTranscriptTrackStatus.Available,
            StatusMessage: string.Empty,
            Cues: [new(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Resize me")]);

        controller.EnterSlide(slide, 960, 720, 960, 720, [track]);
        factory.Backend.Sessions.Single().State.Should().Be(MediaPlaybackState.Playing);
        var caption = overlay.Children.OfType<Border>().Single();
        Canvas.GetLeft(caption).Should().Be(0);

        controller.UpdateLayout(slide, 960, 720, 1280, 720);

        Canvas.GetLeft(caption).Should().Be(160);
        caption.Width.Should().Be(960);

        controller.UpdateLayout(new Slide(), 960, 720, 1280, 720);
        overlay.Children.Should().BeEmpty(
            "a size event for a new slide must not leave old media overlays behind");
    }

    private sealed class FakeBackendFactory : IMediaPlaybackBackendFactory
    {
        public FakeBackend Backend { get; } = new();

        public MediaPlaybackBackendAvailability Probe() =>
            new(true, Backend.Capabilities);

        public bool TryCreate(out IMediaPlaybackBackend? backend, out MediaPlaybackFailure? failure)
        {
            backend = Backend;
            failure = null;
            return true;
        }
    }

    private sealed class FakeBackend : IMediaPlaybackBackend
    {
        public List<FakeSession> Sessions { get; } = new();
        public bool Disposed { get; private set; }
        public MediaPlaybackCapabilities Capabilities { get; } = new(
            true, true, true, true, true, "fake");

        public IMediaPlaybackSession CreateSession()
        {
            var session = new FakeSession(Capabilities);
            Sessions.Add(session);
            return session;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSession : IMediaPlaybackSession
    {
        public FakeSession(MediaPlaybackCapabilities capabilities) => Capabilities = capabilities;

        event EventHandler? IMediaPlaybackSession.Ended
        {
            add { }
            remove { }
        }

        event EventHandler<MediaPlaybackFailure>? IMediaPlaybackSession.Failed
        {
            add { }
            remove { }
        }
        public event EventHandler<MediaPlaybackState>? StateChanged;
        public MediaPlaybackCapabilities Capabilities { get; }
        public MediaPlaybackState State { get; private set; } = MediaPlaybackState.Idle;
        public TimeSpan Position { get; private set; }
        public TimeSpan Duration { get; } = TimeSpan.FromMinutes(1);
        public int Volume { get; set; } = 100;
        public int OpenCount { get; private set; }
        public int PlayCount { get; private set; }
        public int PauseCount { get; private set; }
        public TimeSpan LastSeek { get; private set; }
        public MediaPlaybackSource? LastSource { get; private set; }
        public bool Disposed { get; private set; }

        public void Open(MediaPlaybackSource source)
        {
            LastSource = source;
            OpenCount++;
            SetState(MediaPlaybackState.Opening);
        }

        public void Play()
        {
            PlayCount++;
            SetState(MediaPlaybackState.Playing);
        }

        public void Pause()
        {
            PauseCount++;
            SetState(MediaPlaybackState.Paused);
        }

        public void Stop() => SetState(MediaPlaybackState.Stopped);

        public bool Seek(TimeSpan position)
        {
            LastSeek = position;
            Position = position;
            return true;
        }

        public void Dispose() => Disposed = true;

        private void SetState(MediaPlaybackState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }
}
