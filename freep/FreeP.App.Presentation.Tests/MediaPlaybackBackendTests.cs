using FreeP.App.Media;

namespace FreeP.App.Compositor.Tests;

public sealed class MediaPlaybackBackendTests
{
    [Fact]
    public void LibVlcFactory_ReportsDeterministicFallbackWhenNativeBootstrapFails()
    {
        var factory = new LibVlcMediaPlaybackBackendFactory(initialize: () => false);

        var availability = factory.Probe();

        availability.IsAvailable.Should().BeFalse();
        availability.Capabilities.Audio.Should().BeFalse();
        availability.Capabilities.VideoSurface.Should().BeFalse();
        availability.FailureReason.Should().Contain("native");
        factory.TryCreate(out var backend, out var failure).Should().BeFalse();
        backend.Should().BeNull();
        failure!.Kind.Should().Be(MediaPlaybackFailureKind.NativeLibraryUnavailable);
    }

    [Fact]
    public void SourceFactory_UsesEmbeddedAndSafeHttpSourcesOnly()
    {
        MediaPlaybackSourceFactory.TryCreate(
            new byte[] { 1, 2, 3 },
            null,
            "video/mp4",
            true,
            out var embedded).Should().BeTrue();
        embedded!.EmbeddedBytes.Should().Equal(1, 2, 3);
        embedded.IsVideo.Should().BeTrue();

        MediaPlaybackSourceFactory.TryCreate(
            new byte[] { 4, 5, 6 },
            null,
            "audio/wav",
            false,
            out var looping,
            loop: true).Should().BeTrue();
        looping!.Loop.Should().BeTrue();

        MediaPlaybackSourceFactory.TryCreate(
            null,
            "https://example.test/video.mp4",
            "video/mp4",
            true,
            out var linked).Should().BeTrue();
        linked!.Uri!.Scheme.Should().Be("https");

        MediaPlaybackSourceFactory.TryCreate(
            null,
            "file:///unsafe/video.mp4",
            "video/mp4",
            true,
            out _).Should().BeFalse();
    }

    [Fact]
    public void LoopPolicy_ReplaysOnlyWhileTheSessionIsAlive()
    {
        MediaPlaybackLoopPolicy.ShouldReplay(loop: true, disposed: false).Should().BeTrue();
        MediaPlaybackLoopPolicy.ShouldReplay(loop: false, disposed: false).Should().BeFalse();
        MediaPlaybackLoopPolicy.ShouldReplay(loop: true, disposed: true).Should().BeFalse();
    }

    [Fact]
    public void TempSourceStore_ReleasesEmbeddedPayload()
    {
        var store = new TempMediaPlaybackSourceStore();
        var uri = store.Materialize(MediaPlaybackSource.FromBytes(
            new byte[] { 7, 8, 9 },
            "audio/wav",
            false));

        File.Exists(uri.LocalPath).Should().BeTrue();
        store.Release(uri);
        File.Exists(uri.LocalPath).Should().BeFalse();
    }

    [Fact]
    public void HostSourceGuards_KeepNativeSurfacesBehindPortablePlaybackContracts()
    {
        var root = FindWorkspaceRoot();
        var avaloniaController = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Avalonia", "AvaloniaSlideShowMediaController.cs"));
        var avaloniaWindow = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Avalonia", "SlideShowWindow.cs"));
        var avaloniaProject = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"));
        var mediaProject = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Media", "FreeP.App.Media.csproj"));
        var wpfProject = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Host", "FreeP.App.Host.csproj"));
        var wpfController = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Host", "SlideShowMediaController.cs"));
        var wpfSession = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Host", "WpfMediaPlaybackSession.cs"));
        var linuxDockerfile = File.ReadAllText(Path.Combine(
            root, "tools", "LinuxInteractiveDocker", "Dockerfile"));
        var linuxProbe = File.ReadAllText(Path.Combine(
            root, "tools", "FreeP.MediaRuntimeProbe", "Program.cs"));

        avaloniaController.Should().Contain("LibVlcMediaPlaybackBackendFactory");
        avaloniaController.Should().Contain("LibVlcMediaPlaybackSession");
        avaloniaController.Should().Contain("AvaloniaMediaPlaybackPort");
        avaloniaController.Should().Contain("SlideShowMediaPlaybackSession");
        avaloniaController.Should().Contain("VideoView");
        avaloniaController.Should().Contain("PlayTransitionSound");
        avaloniaController.Should().Contain("TrySeek");
        avaloniaController.Should().Contain("TrySetVolume");
        avaloniaController.Should().Contain("SetCanvasBounds");
        avaloniaController.Should().Contain("Canvas.SetLeft(view, bounds.X)");
        avaloniaController.Should().Contain("Canvas.SetTop(view, bounds.Y)");
        avaloniaController.Should().Contain("Width = Math.Max(1, bounds.Width)");
        avaloniaController.Should().Contain("Height = Math.Max(1, bounds.Height)");
        avaloniaController.Should().NotContain("playback is deferred");
        avaloniaWindow.Should().Contain("_mediaController.PlayTransitionSound");
        avaloniaWindow.Should().Contain("SyncMediaOverlayBounds");
        avaloniaWindow.Should().Contain("SizeChanged +=");
        avaloniaWindow.Should().NotContain("Sound playback on the Avalonia host is deferred");
        avaloniaProject.Should().Contain("LibVLCSharp.Avalonia");
        avaloniaProject.Should().Contain("VideoLAN.LibVLC.Windows");
        mediaProject.Should().Contain("LibVLCSharp");
        wpfProject.Should().Contain("FreeP.App.Media");
        wpfProject.Should().NotContain("LibVLCSharp");
        wpfProject.Should().NotContain("VideoLAN.LibVLC.Windows");
        wpfController.Should().Contain("MediaPlaybackSourceFactory.TryCreate")
            .And.Contain("IMediaPlaybackSession")
            .And.Contain("WpfMediaPlaybackSession")
            .And.NotContain("ResolveSource(");
        wpfSession.Should().Contain("IMediaPlaybackSession")
            .And.Contain("IMediaPlaybackSourceStore")
            .And.Contain("MediaPlaybackState")
            .And.Contain("MediaElement")
            .And.NotContain("LibVLC");
        linuxDockerfile.Should().Contain("libvlc5");
        linuxDockerfile.Should().Contain("libvlccore9");
        linuxDockerfile.Should().Contain("vlc-plugin-base");
        linuxDockerfile.Should().Contain("vlc-plugin-video-output");
        linuxProbe.Should().Contain("CreateWav");
        linuxProbe.Should().Contain("session.Play()");
        linuxProbe.Should().Contain("session.Seek");
        linuxProbe.Should().Contain("session.Stop()");
        linuxProbe.Should().Contain("sessionFailure");
    }

    private static string FindWorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
}
