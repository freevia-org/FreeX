using Avalonia.Controls;
using Free.Shared.Drawing;
using FreeP.App.Avalonia;
using FreeP.App.Media;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class AvaloniaMediaPlaybackAdapterTests
{
    [Fact]
    public void DefaultFactory_ProbesNativeRuntimeWithoutThrowing()
    {
        var availability = new LibVlcMediaPlaybackBackendFactory().Probe();

        availability.Capabilities.BackendName.Should().Be("LibVLC");
        if (!availability.IsAvailable)
            availability.FailureReason.Should().NotBeNullOrWhiteSpace();
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
                Bytes = new byte[] { 1, 2, 3 },
                ContentType = "audio/wav",
            },
        });

        controller.EnterSlide(slide, 960, 720, 960, 720);

        var mediaSession = factory.Backend.Sessions[0];
        mediaSession.OpenCount.Should().Be(1);
        mediaSession.PlayCount.Should().Be(1);
        controller.Availability!.IsAvailable.Should().BeTrue();

        controller.TryHandleClick(slide, 960, 720, 960, 720, 480, 360).Should().BeTrue();
        mediaSession.PauseCount.Should().Be(1);
        controller.TryHandleClick(slide, 960, 720, 960, 720, 480, 360).Should().BeTrue();
        mediaSession.PlayCount.Should().Be(2);
        controller.TrySeek(42, TimeSpan.FromSeconds(3)).Should().BeTrue();
        controller.TrySetVolume(42, 35).Should().BeTrue();
        mediaSession.LastSeek.Should().Be(TimeSpan.FromSeconds(3));
        mediaSession.Volume.Should().Be(35);

        controller.PlayTransitionSound(new TransitionSound
        {
            AudioBytes = new byte[] { 4, 5, 6 },
            ContentType = "audio/wav",
        }).Should().BeTrue();
        factory.Backend.Sessions.Should().HaveCount(2);
        factory.Backend.Sessions[1].PlayCount.Should().Be(1);

        controller.Teardown();
        mediaSession.Disposed.Should().BeTrue();
        factory.Backend.Sessions[1].Disposed.Should().BeTrue();
        factory.Backend.Disposed.Should().BeTrue();
        overlay.Children.Should().BeEmpty();
        controller.Active.Should().BeEmpty();
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
        public bool Disposed { get; private set; }

        public void Open(MediaPlaybackSource source)
        {
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
