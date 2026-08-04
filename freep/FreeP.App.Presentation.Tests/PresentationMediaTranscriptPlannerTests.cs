using System.Text;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationMediaTranscriptPlannerTests
{
    [Fact]
    public void FindActiveCue_UsesHalfOpenTimeIntervals()
    {
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
                new(TimeSpan.Zero, TimeSpan.FromSeconds(1), "First"),
                new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Second")
            ]);

        PresentationMediaTranscriptPlanner.FindActiveCue(track, TimeSpan.Zero)!.Text.Should().Be("First");
        PresentationMediaTranscriptPlanner.FindActiveCue(track, TimeSpan.FromSeconds(1))!.Text.Should().Be("Second");
        PresentationMediaTranscriptPlanner.FindActiveCue(track, TimeSpan.FromSeconds(2)).Should().BeNull();
        PresentationMediaTranscriptPlanner.FindActiveCue(track, TimeSpan.FromMilliseconds(-1)).Should().BeNull();
    }

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
    public void WebVttCueSettings_AreParsedAndPlacedWithoutAffectingDefaultCues()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 46,
            Name = "Positioned video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/positioned.vtt",
                        ContentType = "text/vtt",
                        Bytes = Encoding.UTF8.GetBytes("""
                            WEBVTT

                            00:00.000 --> 00:02.000 position:25% line:30% size:50% align:start
                            Positioned

                            00:02.000 --> 00:04.000
                            Default
                            """)
                    }
                }
            }
        });

        var cues = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation)
            .Tracks.Should().ContainSingle().Subject.Cues;

        cues[0].PositionPercent.Should().Be(25);
        cues[0].LinePercent.Should().Be(30);
        cues[0].SizePercent.Should().Be(50);
        cues[0].Alignment.Should().Be(PresentationMediaTranscriptCueAlignment.Start);
        cues[1].PositionPercent.Should().BeNull();
        cues[1].SizePercent.Should().BeNull();

        var placement = PresentationMediaTranscriptPlanner.ComputeCaptionPlacement(
            cues[0], 800, 400, 80);
        placement.Should().Be(new PresentationMediaCaptionPlacement(200, 120, 400, 80));
    }

    [Fact]
    public void WebVttVerticalCueSettings_ArePreservedAuthoredAndPlacedInWritingDirection()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 47,
            Name = "Vertical video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/vertical.vtt",
                        ContentType = "text/vtt",
                        Bytes = Encoding.UTF8.GetBytes("""
                            WEBVTT

                            00:00.000 --> 00:02.000 position:75% line:10% size:40% align:end vertical:rl
                            Vertical cue
                            """)
                    }
                }
            }
        });

        var cue = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation)
            .Tracks.Should().ContainSingle().Subject.Cues.Should().ContainSingle().Subject;

        cue.WritingMode.Should().Be(PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft);
        var placement = PresentationMediaTranscriptPlanner.ComputeCaptionPlacement(cue, 800, 400, 80);
        placement.Should().Be(new PresentationMediaCaptionPlacement(0, 140, 80, 160, 90));

        var media = new MediaInfo { IsVideo = true };
        var result = PresentationMediaTranscriptPlanner.CreateInternalCaptionTrack(
            media,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                "Vertical captions",
                "ja-JP",
                "ppt/media/vertical-authored.vtt",
                null,
                [new PresentationMediaTranscriptCueDescriptor(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    "Vertical text")
                {
                    WritingMode = PresentationMediaTranscriptCueWritingMode.VerticalLeftToRight
                }]));

        result.Succeeded.Should().BeTrue();
        Encoding.UTF8.GetString(media.CaptionTracks.Single().Bytes)
            .Should().Contain("vertical:lr");
    }

    [Fact]
    public void BuildTranscriptPlan_ParsesTtmlClockAndUnitTiming()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 43,
            Name = "TTML video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/training.ttml",
                        ContentType = "application/ttml+xml",
                        Language = "en-US",
                        Label = "English TTML",
                        Bytes = Encoding.UTF8.GetBytes("""
                            <?xml version="1.0" encoding="utf-8"?>
                            <tt xmlns="http://www.w3.org/ns/ttml">
                              <body><div>
                                <p begin="00:00:00.500" end="00:00:01.500">Hello <span>world</span>.</p>
                                <p begin="2s" dur="750ms">Second
                            cue.</p>
                              </div></body>
                            </tt>
                            """)
                    }
                }
            }
        });

        var track = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation)
            .Tracks.Should().ContainSingle().Subject;

        track.Status.Should().Be(PresentationMediaTranscriptTrackStatus.Available);
        track.Cues.Select(cue => cue.Text).Should().Equal("Hello world.", "Second cue.");
        track.Cues[0].StartTime.Should().Be(TimeSpan.FromMilliseconds(500));
        track.Cues[0].EndTime.Should().Be(TimeSpan.FromMilliseconds(1500));
        track.Cues[1].StartTime.Should().Be(TimeSpan.FromSeconds(2));
        track.Cues[1].EndTime.Should().Be(TimeSpan.FromMilliseconds(2750));
    }

    [Fact]
    public void BuildTranscriptPlan_ParsesDfxpInheritedOffsetsAndFrameClock()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 44,
            Name = "DFXP video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/captions.dfxp",
                        ContentType = "application/ttaf+xml",
                        Bytes = Encoding.UTF8.GetBytes("""
                            <?xml version="1.0" encoding="utf-8"?>
                            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttp="http://www.w3.org/ns/ttml#parameter"
                                ttp:frameRate="25">
                              <body begin="00:00:00.500"><div begin="00:00:01:00">
                                <p begin="00:00:00:10" dur="00:00:00:15">Frame based <span>DFXP</span> cue.</p>
                              </div></body>
                            </tt>
                            """)
                    }
                }
            }
        });

        var track = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation)
            .Tracks.Should().ContainSingle().Subject;

        track.Status.Should().Be(PresentationMediaTranscriptTrackStatus.Available);
        track.Cues.Should().ContainSingle();
        track.Cues[0].Text.Should().Be("Frame based DFXP cue.");
        track.Cues[0].StartTime.Should().Be(TimeSpan.FromMilliseconds(1900));
        track.Cues[0].EndTime.Should().Be(TimeSpan.FromMilliseconds(2500));
    }

    [Fact]
    public void BuildTranscriptPlan_ClampsTtmlCueToInheritedAncestorEnd()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 45,
            Name = "Bounded TTML video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/bounded.ttml",
                        ContentType = "application/ttml+xml",
                        Bytes = Encoding.UTF8.GetBytes("""
                            <tt xmlns="http://www.w3.org/ns/ttml">
                              <body begin="500ms" dur="1000ms"><div begin="250ms" end="400ms">
                                <p begin="100ms" dur="1000ms">Bounded cue.</p>
                              </div></body>
                            </tt>
                            """)
                    }
                }
            }
        });

        var cue = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation)
            .Tracks.Should().ContainSingle().Subject.Cues.Should().ContainSingle().Subject;

        cue.StartTime.Should().Be(TimeSpan.FromMilliseconds(850));
        cue.EndTime.Should().Be(TimeSpan.FromMilliseconds(900));
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
                        Source = "ppt/media/captions.ass",
                        ContentType = "text/x-ass",
                        Label = "ASS",
                        Bytes = Encoding.UTF8.GetBytes("[Events]\nDialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,Unsupported")
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

    [Fact]
    public void CreateInternalCaptionTrack_FromTypedCues_AppendsWebVttBytesAndPreservesExternalTracks()
    {
        var media = new MediaInfo
        {
            IsVideo = true,
            CaptionTracks =
            {
                new MediaCaptionTrackInfo
                {
                    Source = "https://cdn.example.com/demo.vtt",
                    Label = "External captions",
                    Language = "en-US",
                    IsExternal = true
                }
            }
        };

        var result = PresentationMediaTranscriptPlanner.CreateInternalCaptionTrack(
            media,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                Label: " Product demo captions ",
                Language: " en-US ",
                Source: "ppt/media/product-demo.vtt",
                TranscriptText: null,
                Cues:
                [
                    new PresentationMediaTranscriptCueDescriptor(
                        TimeSpan.Zero,
                        TimeSpan.FromMilliseconds(1500),
                        "Revenue & margin <grew>")
                ]));

        result.Succeeded.Should().BeTrue();
        result.TrackIndex.Should().Be(1);
        media.CaptionTracks.Should().HaveCount(2);
        media.CaptionTracks[0].Should().Match<MediaCaptionTrackInfo>(track =>
            track.IsExternal &&
            track.Source == "https://cdn.example.com/demo.vtt" &&
            track.Label == "External captions");

        var track = media.CaptionTracks[1];
        track.Should().Match<MediaCaptionTrackInfo>(caption =>
            !caption.IsExternal &&
            caption.Source == "ppt/media/product-demo.vtt" &&
            caption.ContentType == "text/vtt" &&
            caption.Language == "en-US" &&
            caption.Label == "Product demo captions");

        var text = Encoding.UTF8.GetString(track.Bytes);
        text.Should().StartWith("WEBVTT\r\n\r\n");
        text.Should().Contain("00:00:00.000 --> 00:00:01.500");
        text.Should().Contain("Revenue &amp; margin &lt;grew&gt;");

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 91,
            Name = "Product demo",
            Kind = SlideShapeKind.Media,
            Media = media
        });

        var plan = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation);

        plan.TrackCount.Should().Be(2);
        plan.CueCount.Should().Be(1);
        plan.Tracks[0].Status.Should().Be(PresentationMediaTranscriptTrackStatus.External);
        plan.Tracks[1].Should().Match<PresentationMediaTranscriptTrackDescriptor>(descriptor =>
            descriptor.Status == PresentationMediaTranscriptTrackStatus.Available &&
            descriptor.Label == "Product demo captions" &&
            descriptor.Cues[0].Text == "Revenue & margin <grew>");
    }

    [Fact]
    public void ReplaceInternalCaptionTrack_FromTranscriptText_PreservesExistingSrtFormat()
    {
        var media = new MediaInfo
        {
            IsVideo = true,
            CaptionTracks =
            {
                new MediaCaptionTrackInfo
                {
                    Source = "ppt/media/legacy.srt",
                    ContentType = "application/x-subrip",
                    RelationshipId = "rIdLegacyCaption",
                    Label = "Legacy captions",
                    Language = "es-ES",
                    Bytes = Encoding.UTF8.GetBytes("""
                        1
                        00:00:00,000 --> 00:00:01,000
                        Legacy cue
                        """)
                }
            }
        };

        var result = PresentationMediaTranscriptPlanner.ReplaceInternalCaptionTrack(
            media,
            0,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                Label: null,
                Language: "fr-FR",
                Source: null,
                TranscriptText: """
                    1
                    00:00:02,000 --> 00:00:03,250
                    Bonjour <b>equipe</b>.
                    """));

        result.Succeeded.Should().BeTrue();
        result.TrackIndex.Should().Be(0);

        var track = media.CaptionTracks.Should().ContainSingle().Subject;
        track.Source.Should().Be("ppt/media/legacy.srt");
        track.ContentType.Should().Be("application/x-subrip");
        track.RelationshipId.Should().Be("rIdLegacyCaption");
        track.Language.Should().Be("fr-FR");
        track.Label.Should().Be("Legacy captions");
        track.IsExternal.Should().BeFalse();

        var text = Encoding.UTF8.GetString(track.Bytes);
        text.Should().Contain("00:00:02,000 --> 00:00:03,250");
        text.Should().Contain("Bonjour equipe.");
    }

    [Fact]
    public void ReplaceInternalCaptionTrack_FromTypedCues_PreservesNativeTtmlFormatAndPackageIdentity()
    {
        var media = new MediaInfo
        {
            IsVideo = true,
            CaptionTracks =
            {
                new MediaCaptionTrackInfo
                {
                    RelationshipId = "rIdNativeTtml",
                    Source = "ppt/media/native-caption.ttml",
                    ContentType = "application/ttml+xml",
                    Label = "Native captions",
                    Language = "en-US",
                    Bytes = Encoding.UTF8.GetBytes("""
                        <tt xmlns="http://www.w3.org/ns/ttml"><body><div>
                          <p begin="00:00:00.000" end="00:00:01.000">Old cue.</p>
                        </div></body></tt>
                        """)
                }
            }
        };

        var result = PresentationMediaTranscriptPlanner.ReplaceInternalCaptionTrack(
            media,
            0,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                Label: null,
                Language: null,
                Source: null,
                TranscriptText: null,
                Cues:
                [
                    new PresentationMediaTranscriptCueDescriptor(
                        TimeSpan.FromMilliseconds(500),
                        TimeSpan.FromMilliseconds(1750),
                        "New <caption> cue")
                ]));

        result.Succeeded.Should().BeTrue();

        var track = media.CaptionTracks.Should().ContainSingle().Subject;
        track.Source.Should().Be("ppt/media/native-caption.ttml");
        track.ContentType.Should().Be("application/ttml+xml");
        track.RelationshipId.Should().Be("rIdNativeTtml");
        var text = Encoding.UTF8.GetString(track.Bytes);
        text.Should().Contain("<tt xmlns=\"http://www.w3.org/ns/ttml\">");
        text.Should().Contain("begin=\"00:00:00.500\"");
        text.Should().Contain("New &lt;caption&gt; cue");
        text.Should().NotContain("WEBVTT");

        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 92,
            Name = "Native caption video",
            Kind = SlideShapeKind.Media,
            Media = media
        });

        var cue = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation)
            .Tracks.Should().ContainSingle().Subject.Cues.Should().ContainSingle().Subject;
        cue.StartTime.Should().Be(TimeSpan.FromMilliseconds(500));
        cue.EndTime.Should().Be(TimeSpan.FromMilliseconds(1750));
        cue.Text.Should().Be("New <caption> cue");
    }

    [Fact]
    public void CaptionAuthoringPanePlan_ExposesSharedCreateReplaceDeleteState()
    {
        var presentation = Presentation.CreateEmpty();
        var media = new MediaInfo
        {
            IsVideo = true,
            CaptionTracks =
            {
                new MediaCaptionTrackInfo
                {
                    Source = "https://cdn.example.com/external.vtt",
                    Label = "External captions",
                    IsExternal = true
                },
                new MediaCaptionTrackInfo
                {
                    Source = "ppt/media/internal.vtt",
                    ContentType = "text/vtt",
                    Label = "Internal captions",
                    Language = "en-US",
                    Bytes = Encoding.UTF8.GetBytes("WEBVTT\r\n\r\n00:00:00.000 --> 00:00:01.000\r\nExisting cue\r\n")
                }
            }
        };
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 401,
            Name = "Captioned media",
            Kind = SlideShapeKind.Media,
            Media = media
        });

        var externalPlan = PresentationMediaTranscriptPlanner.BuildCaptionAuthoringPanePlan(
            presentation.Slides[0],
            0,
            [401],
            selectedTrackIndex: 0,
            proposedLabel: null,
            proposedLanguage: null,
            proposedSource: null,
            proposedTranscriptText: null);

        externalPlan.ShapeId.Should().Be(401);
        externalPlan.Tracks.Should().HaveCount(2);
        externalPlan.SelectedTrackIndex.Should().Be(0);
        externalPlan.Message.Should().Be(PresentationMediaTranscriptPlanner.CaptionAuthoringExternalTrackMessage);
        externalPlan.Actions.Single(action =>
                action.CommandId == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneReplaceCommandId)
            .DisabledReason.Should().Be(PresentationMediaTranscriptPlanner.ExternalCaptionTrackMessage);
        externalPlan.Actions.Single(action =>
                action.CommandId == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneDeleteCommandId)
            .IsEnabled.Should().BeTrue();

        var internalPlan = PresentationMediaTranscriptPlanner.BuildCaptionAuthoringPanePlan(
            presentation.Slides[0],
            0,
            [401],
            selectedTrackIndex: 1,
            proposedLabel: null,
            proposedLanguage: null,
            proposedSource: null,
            proposedTranscriptText: null);

        internalPlan.Label.Value.Should().Be("Internal captions");
        internalPlan.Language.Value.Should().Be("en-US");
        internalPlan.Source.Value.Should().Be("ppt/media/internal.vtt");
        internalPlan.TranscriptText.Value.Should().Contain("Existing cue");
        internalPlan.Actions.Single(action =>
                action.CommandId == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneReplaceCommandId)
            .IsEnabled.Should().BeTrue();
        internalPlan.Actions.Single(action =>
                action.CommandId == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneDeleteCommandId)
            .IsEnabled.Should().BeTrue();

        var missingSelection = PresentationMediaTranscriptPlanner.BuildCaptionAuthoringPanePlan(
            presentation.Slides[0],
            0,
            [],
            selectedTrackIndex: null,
            proposedLabel: null,
            proposedLanguage: null,
            proposedSource: null,
            proposedTranscriptText: null);

        missingSelection.HasSelectedMedia.Should().BeFalse();
        missingSelection.Actions.Should().Contain(action =>
            action.CommandId == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneCreateCommandId &&
            action.DisabledReason == PresentationMediaTranscriptPlanner.MissingSelectedMediaMessage);
    }

    [Fact]
    public void CaptionTrackAuthoring_RejectsInvalidCuesAndDeletesExternalLinksWithoutTouchingTheResource()
    {
        var media = new MediaInfo
        {
            IsVideo = true,
            CaptionTracks =
            {
                new MediaCaptionTrackInfo
                {
                    Source = "https://cdn.example.com/captions.vtt",
                    Label = "External captions",
                    IsExternal = true
                },
                new MediaCaptionTrackInfo
                {
                    Source = "ppt/media/internal.vtt",
                    ContentType = "text/vtt",
                    Label = "Internal captions",
                    Bytes = Encoding.UTF8.GetBytes("WEBVTT\r\n\r\n00:00:00.000 --> 00:00:01.000\r\nInternal cue\r\n")
                }
            }
        };

        var invalidCreate = PresentationMediaTranscriptPlanner.CreateInternalCaptionTrack(
            media,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                Label: "Broken",
                Language: "en-US",
                Source: "ppt/media/broken.vtt",
                TranscriptText: null,
                Cues:
                [
                    new PresentationMediaTranscriptCueDescriptor(
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(1),
                        "Backwards cue")
                ]));

        invalidCreate.Succeeded.Should().BeFalse();
        invalidCreate.ErrorMessage.Should().Be(PresentationMediaTranscriptPlanner.InvalidCaptionCueTimingMessage);
        media.CaptionTracks.Should().HaveCount(2);

        var externalDelete = PresentationMediaTranscriptPlanner.DeleteInternalCaptionTrack(media, 0);

        externalDelete.Succeeded.Should().BeTrue();
        externalDelete.TrackIndex.Should().Be(0);
        externalDelete.Track.Should().NotBeNull();
        externalDelete.Track!.IsExternal.Should().BeTrue();
        externalDelete.Track.Source.Should().Be("https://cdn.example.com/captions.vtt");
        media.CaptionTracks.Should().ContainSingle()
            .Which.IsExternal.Should().BeFalse();

        var internalDelete = PresentationMediaTranscriptPlanner.DeleteInternalCaptionTrack(media, 0);

        internalDelete.Succeeded.Should().BeTrue();
        internalDelete.TrackIndex.Should().Be(0);
        media.CaptionTracks.Should().BeEmpty();
    }

    [Fact]
    public void EditingSessionCaptionAuthoring_DeletesExternalTrackThroughUndoBus()
    {
        var presentation = Presentation.CreateEmpty();
        var mediaShape = new SlideShape
        {
            Id = 902,
            Name = "Recorded video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        RelationshipId = "rIdCaption1",
                        Source = "https://cdn.example.com/captions.vtt",
                        Label = "Remote captions",
                        IsExternal = true,
                    }
                }
            }
        };
        presentation.Slides[0].Shapes.Add(mediaShape);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(mediaShape.Id);
        var plan = PresentationMediaTranscriptPlanner.BuildCaptionAuthoringMutationPlan(
            mediaShape.Media,
            PresentationMediaCaptionAuthoringIntentKind.Delete,
            trackIndex: 0,
            descriptor: null);

        var result = editor.ApplyMediaCaptionAuthoring(plan);

        result.Succeeded.Should().BeTrue();
        result.Track.Should().Match<MediaCaptionTrackInfo>(track =>
            track.IsExternal &&
            track.RelationshipId == "rIdCaption1" &&
            track.Source == "https://cdn.example.com/captions.vtt");
        mediaShape.Media.CaptionTracks.Should().BeEmpty();
        editor.CanUndo.Should().BeTrue();

        editor.Undo();
        mediaShape.Media.CaptionTracks.Should().ContainSingle()
            .Which.Should().Match<MediaCaptionTrackInfo>(track =>
                track.IsExternal &&
                track.RelationshipId == "rIdCaption1" &&
                track.Source == "https://cdn.example.com/captions.vtt");

        editor.Redo();
        mediaShape.Media.CaptionTracks.Should().BeEmpty();
    }
}
