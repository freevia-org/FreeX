using System.Text;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class MediaCaptionCommandTests
{
    [Fact]
    public void EditingSessionCaptionAuthoring_UsesUndoBusForCreate()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 19,
            Name = "Video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = true }
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var bus = new PresentationCommandBus(presentation);
        var editor = new EditingSession(presentation, bus);
        editor.Select(mediaShape.Id);
        var plan = PresentationMediaTranscriptPlanner.BuildCaptionAuthoringMutationPlan(
            mediaShape.Media,
            PresentationMediaCaptionAuthoringIntentKind.Create,
            -1,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                "English",
                "en-US",
                "captions.vtt",
                null,
                new[]
                {
                    new PresentationMediaTranscriptCueDescriptor(
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(1),
                        "Hello")
                }));

        var result = editor.ApplyMediaCaptionAuthoring(plan);

        result.Succeeded.Should().BeTrue();
        mediaShape.Media.CaptionTracks.Should().ContainSingle();
        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        mediaShape.Media.CaptionTracks.Should().BeEmpty();
        editor.Redo();
        mediaShape.Media.CaptionTracks.Should().ContainSingle();
    }

    [Fact]
    public void EditingSessionCaptionAuthoring_ReplacesExternalLinkWithInternalTrackAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 20,
            Name = "Video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        RelationshipId = "rIdExternalCaption",
                        Source = "https://cdn.example.com/captions.vtt",
                        Label = "Remote captions",
                        IsExternal = true
                    }
                }
            }
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(mediaShape.Id);

        var plan = PresentationMediaTranscriptPlanner.BuildCaptionAuthoringMutationPlan(
            mediaShape.Media,
            PresentationMediaCaptionAuthoringIntentKind.Replace,
            0,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                "Local captions",
                null,
                "https://cdn.example.com/captions.vtt",
                "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nLocal cue"));

        var result = editor.ApplyMediaCaptionAuthoring(plan);

        result.Succeeded.Should().BeTrue();
        mediaShape.Media.CaptionTracks.Should().ContainSingle()
            .Which.Should().Match<MediaCaptionTrackInfo>(track =>
                !track.IsExternal &&
                track.RelationshipId == "rIdExternalCaption" &&
                track.Source == "ppt/media/authored-captions1.vtt" &&
                Encoding.UTF8.GetString(track.Bytes).Contains("Local cue", StringComparison.Ordinal));

        editor.Undo();
        mediaShape.Media.CaptionTracks.Should().ContainSingle()
            .Which.IsExternal.Should().BeTrue();

        editor.Redo();
        mediaShape.Media.CaptionTracks.Should().ContainSingle()
            .Which.IsExternal.Should().BeFalse();
    }

    [Fact]
    public void SetMediaCaptionTracksCommand_UndoAndRedoRestoreTrackBytesAndMetadata()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 42,
            Name = "Training video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = true }
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var bus = new PresentationCommandBus(presentation);
        var track = new MediaCaptionTrackInfo
        {
            Source = "ppt/media/training.vtt",
            ContentType = "text/vtt",
            Language = "en-US",
            Label = "English",
            Bytes = Encoding.UTF8.GetBytes("WEBVTT\n\n00:00.000 --> 00:01.000\nHello\n")
        };

        bus.Execute(new SetMediaCaptionTracksCommand(
            slideIndex: 0,
            shapeId: mediaShape.Id,
            before: Array.Empty<MediaCaptionTrackInfo>(),
            after: new[] { track }));

        mediaShape.Media.CaptionTracks.Should().ContainSingle();
        mediaShape.Media.CaptionTracks[0].Bytes.Should().Equal(track.Bytes);

        bus.Undo();
        mediaShape.Media.CaptionTracks.Should().BeEmpty();

        bus.Redo();
        mediaShape.Media.CaptionTracks.Should().ContainSingle();
        mediaShape.Media.CaptionTracks[0].Label.Should().Be("English");
        mediaShape.Media.CaptionTracks[0].Bytes.Should().Equal(track.Bytes);
    }

    [Fact]
    public void SetMediaCaptionTracksCommand_DoesNotCreateUndoEntryForIdenticalState()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo { Source = "captions.vtt", Bytes = [1, 2, 3] }
                }
            }
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var bus = new PresentationCommandBus(presentation);

        var existing = mediaShape.Media.CaptionTracks.ToArray();
        bus.Execute(new SetMediaCaptionTracksCommand(0, mediaShape.Id, existing, existing));

        bus.CanUndo.Should().BeFalse();
        mediaShape.Media.CaptionTracks.Should().ContainSingle();
        mediaShape.Media.CaptionTracks[0].Bytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void SetMediaVolumeCommand_UndoAndRedoClampAndRestoreVolume()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 51,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = true, VolumePercent = 80 }
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetMediaVolumeCommand(0, mediaShape.Id, 80, 125));

        mediaShape.Media.VolumePercent.Should().Be(100);
        bus.Undo();
        mediaShape.Media.VolumePercent.Should().Be(80);
        bus.Redo();
        mediaShape.Media.VolumePercent.Should().Be(100);
    }

    [Fact]
    public void EditingSessionSelectedMediaVolume_UsesUndoBus()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 52,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = false, VolumePercent = 80 }
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(mediaShape.Id);

        editor.SetSelectedMediaVolume(25).Should().BeTrue();
        mediaShape.Media.VolumePercent.Should().Be(25);
        editor.Undo();
        mediaShape.Media.VolumePercent.Should().Be(80);
        editor.Redo();
        mediaShape.Media.VolumePercent.Should().Be(25);
    }

    [Fact]
    public void EditingSessionSelectedMediaPlaybackOptions_UsesUndoBus()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 53,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                PlaybackStartMode = MediaPlaybackStartMode.InClickSequence,
                Loop = false,
                ShowWhenStopped = true,
            }
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(mediaShape.Id);

        editor.SetSelectedMediaPlaybackOptions(MediaPlaybackStartMode.Automatically, true, false).Should().BeTrue();
        mediaShape.Media.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.Automatically);
        mediaShape.Media.Loop.Should().BeTrue();
        mediaShape.Media.ShowWhenStopped.Should().BeFalse();
        editor.Undo();
        mediaShape.Media.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.InClickSequence);
        mediaShape.Media.Loop.Should().BeFalse();
        mediaShape.Media.ShowWhenStopped.Should().BeTrue();
        editor.Redo();
        mediaShape.Media.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.Automatically);
        mediaShape.Media.Loop.Should().BeTrue();
        mediaShape.Media.ShowWhenStopped.Should().BeFalse();
    }

    [Fact]
    public void SetMediaTimingCommand_UndoAndRedoRestoresAllValues()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 54,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = true, TrimStartMilliseconds = 10 },
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetMediaTimingCommand(0, mediaShape.Id, 10, 0, 0, 0, 100, 200, 300, 400));

        mediaShape.Media.TrimStartMilliseconds.Should().Be(100);
        mediaShape.Media.TrimEndMilliseconds.Should().Be(200);
        mediaShape.Media.FadeInMilliseconds.Should().Be(300);
        mediaShape.Media.FadeOutMilliseconds.Should().Be(400);
        bus.Undo();
        mediaShape.Media.TrimStartMilliseconds.Should().Be(10);
        mediaShape.Media.TrimEndMilliseconds.Should().Be(0);
        bus.Redo();
        mediaShape.Media.FadeOutMilliseconds.Should().Be(400);
    }

    [Fact]
    public void EditingSessionSelectedMediaTiming_UsesUndoBus()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 55,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = false },
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(mediaShape.Id);

        editor.SetSelectedMediaTiming(125, 250, 500, 750).Should().BeTrue();
        mediaShape.Media.TrimStartMilliseconds.Should().Be(125);
        editor.Undo();
        mediaShape.Media.TrimStartMilliseconds.Should().Be(0);
        editor.Redo();
        mediaShape.Media.FadeOutMilliseconds.Should().Be(750);
    }

    [Fact]
    public void SetMediaBookmarksCommand_UndoAndRedoRestoresOrderedBookmarks()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 56,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = true },
        };
        mediaShape.Media.Bookmarks.Add(new MediaBookmarkInfo { Name = "Old", TimeMilliseconds = 10 });
        presentation.Slides[0].Shapes.Add(mediaShape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetMediaBookmarksCommand(
            0,
            mediaShape.Id,
            mediaShape.Media.Bookmarks,
            [new MediaBookmarkInfo { Name = "Intro", TimeMilliseconds = 1250.25 }]));

        mediaShape.Media.Bookmarks.Should().ContainSingle()
            .Which.Name.Should().Be("Intro");
        bus.Undo();
        mediaShape.Media.Bookmarks.Should().ContainSingle().Which.Name.Should().Be("Old");
        bus.Redo();
        mediaShape.Media.Bookmarks.Single().TimeMilliseconds.Should().Be(1250.25);
    }

    [Fact]
    public void EditingSessionSelectedMediaBookmarks_UsesUndoBus()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 57,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = false },
        };
        presentation.Slides[0].Shapes.Add(mediaShape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(mediaShape.Id);

        editor.SetSelectedMediaBookmarks([new MediaBookmarkInfo { Name = "Cue", TimeMilliseconds = 800 }])
            .Should().BeTrue();
        mediaShape.Media.Bookmarks.Should().ContainSingle().Which.Name.Should().Be("Cue");
        editor.Undo();
        mediaShape.Media.Bookmarks.Should().BeEmpty();
        editor.Redo();
        mediaShape.Media.Bookmarks.Should().ContainSingle().Which.TimeMilliseconds.Should().Be(800);
    }
}
