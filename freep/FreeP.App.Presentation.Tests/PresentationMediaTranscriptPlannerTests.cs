using System.Text;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationMediaTranscriptPlannerTests
{
    [Fact]
    public void BuildTranscriptPlan_ParsesWebVttAndSrtCaptionBytes()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Training video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/training.vtt",
                        ContentType = "text/vtt",
                        Language = "en-US",
                        Label = "English captions",
                        Bytes = Encoding.UTF8.GetBytes("""
                            WEBVTT

                            NOTE Imported authoring metadata

                            cue-1
                            00:00.000 --> 00:01.500 align:start position:0%
                            <v Speaker>Revenue &amp; margin grew</v>

                            00:02.000 --> 00:03.250
                            <i>Next quarter</i> stays on plan.
                            """)
                    },
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/training.srt",
                        ContentType = "application/x-subrip",
                        Language = "es-ES",
                        Label = "Spanish subtitles",
                        Bytes = Encoding.UTF8.GetBytes("""
                            1
                            00:00:04,000 --> 00:00:05,250
                            Hola <b>equipo</b>.

                            2
                            00:00:05,500 --> 00:00:07,000
                            Linea uno
                            linea dos
                            """)
                    }
                }
            }
        });

        var plan = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation);

        plan.SlideCount.Should().Be(1);
        plan.MediaShapeCount.Should().Be(1);
        plan.TrackCount.Should().Be(2);
        plan.CueCount.Should().Be(4);

        var webVtt = plan.Tracks[0];
        webVtt.Should().Match<PresentationMediaTranscriptTrackDescriptor>(track =>
            track.SlideIndex == 0 &&
            track.ShapeId == 42 &&
            track.ShapeName == "Training video" &&
            track.TrackIndex == 0 &&
            track.Label == "English captions" &&
            track.Language == "en-US" &&
            track.Source == "ppt/media/training.vtt" &&
            track.ContentType == "text/vtt" &&
            track.Status == PresentationMediaTranscriptTrackStatus.Available &&
            track.HasTranscript);
        webVtt.Cues.Select(cue => cue.Text).Should().Equal(
            "Revenue & margin grew",
            "Next quarter stays on plan.");
        webVtt.Cues[0].StartTime.Should().Be(TimeSpan.Zero);
        webVtt.Cues[0].EndTime.Should().Be(TimeSpan.FromMilliseconds(1500));
        webVtt.Cues[1].TimeRangeText.Should().Be("0:02.000 - 0:03.250");

        var srt = plan.Tracks[1];
        srt.Status.Should().Be(PresentationMediaTranscriptTrackStatus.Available);
        srt.Cues.Select(cue => cue.Text).Should().Equal(
            "Hola equipo.",
            "Linea uno linea dos");
        srt.Cues[0].StartTime.Should().Be(TimeSpan.FromSeconds(4));
        srt.Cues[0].EndTime.Should().Be(TimeSpan.FromMilliseconds(5250));
    }

    [Fact]
    public void BuildTranscriptPlan_ClassifiesExternalNoBytesAndUnsupportedTracks()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 77,
            Name = "Linked briefing",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        Source = "https://example.com/captions.vtt",
                        Label = "External English",
                        IsExternal = true
                    },
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/empty.vtt",
                        Label = "Empty bytes",
                        ContentType = "text/vtt"
                    },
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/captions.ttml",
                        ContentType = "application/ttml+xml",
                        Label = "TTML",
                        Bytes = Encoding.UTF8.GetBytes("<tt><body /></tt>")
                    }
                }
            }
        });

        var plan = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation);

        plan.TrackCount.Should().Be(3);
        plan.CueCount.Should().Be(0);
        plan.Tracks.Select(track => track.Status).Should().Equal(
            PresentationMediaTranscriptTrackStatus.External,
            PresentationMediaTranscriptTrackStatus.NoBytes,
            PresentationMediaTranscriptTrackStatus.UnsupportedFormat);
        plan.Tracks[0].StatusMessage.Should().Be("External caption track is not used for transcript planning.");
        plan.Tracks[1].StatusMessage.Should().Be("Caption track has no authored bytes.");
        plan.Tracks[2].StatusMessage.Should().Be("Caption track format is not supported for transcript planning.");
        plan.Tracks.Should().OnlyContain(track => !track.HasTranscript);
    }
}
