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
}
