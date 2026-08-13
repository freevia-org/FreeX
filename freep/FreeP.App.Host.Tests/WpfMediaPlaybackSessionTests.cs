using System.IO;
using System.Windows.Controls;
using FreeP.App.Host;
using FreeP.App.Media;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class WpfMediaPlaybackSessionTests
{
    [StaFact]
    public void Session_ReopenAndDispose_ReleaseEachMaterializedSourceExactlyOnce()
    {
        var store = new RecordingSourceStore();
        var session = new WpfMediaPlaybackSession(CreateElement(), store);
        var source = MediaPlaybackSource.FromUri(
            new Uri("https://example.test/video.mp4"),
            "video/mp4",
            isVideo: true);

        session.Open(source);
        session.Open(source);

        store.Released.Should().Equal(store.Materialized[0]);

        session.Dispose();
        session.Dispose();

        store.Released.Should().Equal(store.Materialized);
        store.Released.Should().OnlyHaveUniqueItems();
    }

    [StaFact]
    public void Session_ProjectsCommandsAndNativeCompletionIntoPortableState()
    {
        var store = new RecordingSourceStore();
        var session = new WpfMediaPlaybackSession(CreateElement(), store);
        var states = new List<MediaPlaybackState>();
        var endedCount = 0;
        session.StateChanged += (_, state) => states.Add(state);
        session.Ended += (_, _) => endedCount++;

        session.Open(MediaPlaybackSource.FromUri(
            new Uri("https://example.test/audio.wav"),
            "audio/wav",
            isVideo: false));
        session.HandleMediaOpened();
        session.Play();
        session.Pause();
        session.HandleMediaEnded();

        session.State.Should().Be(MediaPlaybackState.Ended);
        endedCount.Should().Be(1);
        states.Should().ContainInOrder(
            MediaPlaybackState.Opening,
            MediaPlaybackState.Stopped,
            MediaPlaybackState.Playing,
            MediaPlaybackState.Paused,
            MediaPlaybackState.Ended);

        session.Dispose();
        session.HandleMediaEnded();

        session.State.Should().Be(MediaPlaybackState.Stopped);
        endedCount.Should().Be(1, "disposed native callbacks must not re-enter slideshow orchestration");
    }

    [StaFact]
    public void Session_Open_PreservesSourceMaterializationCancellation()
    {
        using var session = new WpfMediaPlaybackSession(
            CreateElement(),
            new CancelingSourceStore());
        var source = MediaPlaybackSource.FromUri(
            new Uri("https://example.test/video.mp4"),
            "video/mp4",
            isVideo: true);

        var act = () => session.Open(source);

        act.Should().Throw<OperationCanceledException>();
        session.State.Should().Be(MediaPlaybackState.Idle);
    }

    [StaFact]
    public void Controller_ContiguousSlideEntry_ReleasesVideoSourcesExactlyOnce()
    {
        var writer = new AbsoluteRecordingFileWriter();
        var controller = new SlideShowMediaController(new Canvas(), writer);

        controller.EnterSlide(
            CreateMediaSlide(shapeId: 1, isVideo: true),
            960,
            720,
            960,
            720,
            presentationSlideIndex: 0);
        controller.EnterSlide(
            CreateMediaSlide(shapeId: 2, isVideo: true),
            960,
            720,
            960,
            720,
            presentationSlideIndex: 1);

        writer.Written.Should().HaveCount(2);
        writer.Deleted.Should().ContainSingle().Which.Should().Be(writer.Written[0]);

        controller.Teardown();
        controller.Teardown();

        writer.Deleted.Should().BeEquivalentTo(writer.Written);
        writer.Deleted.Should().OnlyHaveUniqueItems();
    }

    [StaFact]
    public void Controller_ContiguousSlideEntry_RetainsAudioUntilItsSlideBudgetExpires()
    {
        var writer = new AbsoluteRecordingFileWriter();
        var controller = new SlideShowMediaController(new Canvas(), writer);

        controller.EnterSlide(
            CreateMediaSlide(shapeId: 7, isVideo: false, stopAfterSlides: 2),
            960,
            720,
            960,
            720,
            presentationSlideIndex: 0);
        controller.EnterSlide(
            new Slide(),
            960,
            720,
            960,
            720,
            presentationSlideIndex: 1);

        writer.Deleted.Should().BeEmpty();

        controller.EnterSlide(
            new Slide(),
            960,
            720,
            960,
            720,
            presentationSlideIndex: 2);

        writer.Deleted.Should().ContainSingle().Which.Should().Be(writer.Written.Single());
        controller.Teardown();
        writer.Deleted.Should().ContainSingle();
    }

    private static MediaElement CreateElement() => new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Stop,
        ScrubbingEnabled = true,
    };

    private static Slide CreateMediaSlide(
        uint shapeId,
        bool isVideo,
        int stopAfterSlides = 1)
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = shapeId,
            Name = $"Media {shapeId}",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 6858000,
            Media = new MediaInfo
            {
                IsVideo = isVideo,
                Bytes = [1, 2, 3],
                ContentType = isVideo ? "video/mp4" : "audio/wav",
                StopAfterSlides = stopAfterSlides,
            },
        });
        return slide;
    }

    private sealed class RecordingSourceStore : IMediaPlaybackSourceStore
    {
        public List<Uri> Materialized { get; } = [];
        public List<Uri> Released { get; } = [];

        public Uri Materialize(MediaPlaybackSource source)
        {
            var uri = new Uri($"https://example.test/materialized/{Materialized.Count}");
            Materialized.Add(uri);
            return uri;
        }

        public void Release(Uri uri) => Released.Add(uri);
    }

    private sealed class CancelingSourceStore : IMediaPlaybackSourceStore
    {
        public Uri Materialize(MediaPlaybackSource source) =>
            throw new OperationCanceledException("Canceled by the source owner.");

        public void Release(Uri uri) => throw new InvalidOperationException("No source was materialized.");
    }

    private sealed class AbsoluteRecordingFileWriter : ITempMediaFileWriter
    {
        private int _nextId;

        public List<string> Written { get; } = [];
        public List<string> Deleted { get; } = [];

        public string Write(byte[] bytes, string contentType)
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"freep_wpf_session_test_{_nextId++}.tmp");
            Written.Add(path);
            return path;
        }

        public void Delete(string path) => Deleted.Add(path);
    }
}
