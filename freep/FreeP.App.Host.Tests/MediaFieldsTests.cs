using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round-trip and compositor tests for media (audio/video) shapes and
/// header/footer/date/slide-number field runs (13A).
/// </summary>
public sealed class MediaFieldsTests
{
    // ── Media tests ───────────────────────────────────────────────────────────

    [Fact]
    public void Media_RoundTrip_PreservesKindAndBytes()
    {
        var pres = new Presentation();
        var slide = new Slide();

        var posterBytes = CreateMinimal1x1Png();
        var videoBytes  = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }; // mp4 ftyp box

        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "Video 1",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = posterBytes, ContentType = "image/png" },
            Media   = new MediaInfo { IsVideo = true, Bytes = videoBytes, ContentType = "video/mp4" },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var pres2 = PptxPackageReader.Read(ms);

        var shape2 = pres2.Slides[0].Shapes[0];
        Assert.Equal(SlideShapeKind.Media, shape2.Kind);
        Assert.NotNull(shape2.Picture);
        Assert.Equal(posterBytes.Length, shape2.Picture!.Bytes.Length);
        Assert.NotNull(shape2.Media);
        Assert.True(shape2.Media!.IsVideo);
        Assert.Equal(videoBytes.Length, shape2.Media.Bytes.Length);
        Assert.Equal("video/mp4", shape2.Media.ContentType);
    }

    [Fact]
    public void Media_AutomaticPlayback_RoundTripsThroughPresentationTiming()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Name = "Automatically playing video",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Media = new MediaInfo
            {
                IsVideo = true,
                PlaybackStartMode = MediaPlaybackStartMode.Automatically,
                Bytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70],
                ContentType = "video/mp4",
            },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            var mediaTiming = slideXml.Descendants(p + "video").Single();
            mediaTiming.Descendants(p + "cond").Single().Attribute("evt")!.Value.Should().Be("onBegin");
            mediaTiming.Descendants(p + "spTgt").Single().Attribute("spid")!.Value.Should().Be("7");
            mediaTiming.Descendants(p + "cMediaNode").Single()
                .Attribute("showWhenStopped").Should().BeNull();
        }

        ms.Position = 0;
        var reopened = PptxPackageReader.Read(ms);
        reopened.Slides[0].Shapes[0].Media!.PlaybackStartMode
            .Should().Be(MediaPlaybackStartMode.Automatically);
    }

    [Fact]
    public void Media_StopAfterSlides_RoundTripsNativeAudioTiming()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 71,
            Name = "Across-slide audio",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = false,
                StopAfterSlides = 2,
                Bytes = [0x52, 0x49, 0x46, 0x46],
                ContentType = "audio/wav",
            },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            slideXml.Descendants(p + "audio").Single()
                .Element(p + "cMediaNode")!.Attribute("numSld")!.Value.Should().Be("2");
        }

        ms.Position = 0;
        var reopened = PptxPackageReader.Read(ms);
        reopened.Slides[0].Shapes[0].Media!.StopAfterSlides.Should().Be(2);
    }

    [Fact]
    public void Media_PlayFullScreen_RoundTripsThroughVideoFile()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 70,
            Name = "Full-screen video",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Media = new MediaInfo
            {
                IsVideo = true,
                PlayFullScreen = true,
                Bytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70],
                ContentType = "video/mp4",
            },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
            ReadXml(zip, "ppt/slides/slide1.xml")
                .Descendants(a + "videoFile")
                .Single()
                .Attribute("fullScrn")!.Value.Should().Be("1");
        }

        ms.Position = 0;
        PptxPackageReader.Read(ms).Slides[0].Shapes[0].Media!
            .PlayFullScreen.Should().BeTrue();
    }

    [Fact]
    public void Media_LoopPlayback_RoundTripsThroughPresentationTiming()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 8,
            Name = "Looping click-sequence video",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Media = new MediaInfo
            {
                IsVideo = true,
                Loop = true,
                Bytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70],
                ContentType = "video/mp4",
            },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            var mediaTiming = slideXml.Descendants(p + "video").Single();
            mediaTiming.Descendants(p + "cTn").Single()
                .Attribute("repeatCount")!.Value.Should().Be("indefinite");
            mediaTiming.Descendants(p + "cond").Single()
                .Attribute("evt")!.Value.Should().Be("onClick");
        }

        ms.Position = 0;
        var reopened = PptxPackageReader.Read(ms);
        reopened.Slides[0].Shapes[0].Media!.Loop.Should().BeTrue();
        reopened.Slides[0].Shapes[0].Media!.PlaybackStartMode
            .Should().Be(MediaPlaybackStartMode.InClickSequence);
    }

    [Fact]
    public void Media_Volume_RoundTripsThroughPresentationTiming()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 9,
            Name = "Quiet video",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Media = new MediaInfo
            {
                IsVideo = true,
                VolumePercent = 35,
                Bytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70],
                ContentType = "video/mp4",
            },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            slideXml.Descendants(p + "cMediaNode").Single()
                .Attribute("vol")!.Value.Should().Be("35000");
        }

        ms.Position = 0;
        var reopened = PptxPackageReader.Read(ms);
        reopened.Slides[0].Shapes[0].Media!.VolumePercent.Should().Be(35);
    }

    [Fact]
    public void Media_ShowWhenStoppedFalse_RoundTripsThroughPresentationTiming()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 11,
            Name = "Hidden-until-play video",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Media = new MediaInfo
            {
                IsVideo = true,
                ShowWhenStopped = false,
                Bytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70],
                ContentType = "video/mp4",
            },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            slideXml.Descendants(p + "cMediaNode").Single()
                .Attribute("showWhenStopped")!.Value.Should().Be("0");
        }

        ms.Position = 0;
        var reopened = PptxPackageReader.Read(ms);
        reopened.Slides[0].Shapes[0].Media!.ShowWhenStopped.Should().BeFalse();
    }

    [Fact]
    public void Media_RewindAfterPlaying_RoundTripsThroughPresentationTiming()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 12,
            Name = "Rewinding video",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Media = new MediaInfo
            {
                IsVideo = true,
                RewindAfterPlaying = true,
                Bytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70],
                ContentType = "video/mp4",
            },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            slideXml.Descendants(p + "cTn")
                .Single(element => element.Attribute("autoRev") is not null)
                .Attribute("autoRev")!.Value.Should().Be("1");
        }

        ms.Position = 0;
        var reopened = PptxPackageReader.Read(ms);
        reopened.Slides[0].Shapes[0].Media!.RewindAfterPlaying.Should().BeTrue();
    }

    [Fact]
    public void Media_TrimAndFade_RoundTripThroughP14Extension()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 10,
            Name = "Trimmed video",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Media = new MediaInfo
            {
                IsVideo = true,
                TrimStartMilliseconds = 18374.0515,
                TrimEndMilliseconds = 29596.7072,
                FadeInMilliseconds = 1000,
                FadeOutMilliseconds = 250.5,
                Bookmarks =
                {
                    new MediaBookmarkInfo { Name = "Intro", TimeMilliseconds = 1250.25 },
                    new MediaBookmarkInfo { Name = "Demo", TimeMilliseconds = 9375 },
                },
                Bytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70],
                ContentType = "video/mp4",
            },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
            var p14 = XNamespace.Get("http://schemas.microsoft.com/office/powerpoint/2010/main");
            var media = slideXml.Descendants(p14 + "media").Single();
            media.Parent!.Attribute("uri")!.Value
                .Should().Be("{DAA4B4D4-6D71-4841-9C94-3DE7FCFB9230}");
            media.Element(p14 + "trim")!.Attribute("st")!.Value.Should().Be("18374.0515");
            media.Element(p14 + "trim")!.Attribute("end")!.Value.Should().Be("29596.7072");
            media.Element(p14 + "fade")!.Attribute("in")!.Value.Should().Be("1000");
            media.Element(p14 + "fade")!.Attribute("out")!.Value.Should().Be("250.5");
            media.Descendants(p14 + "bmk").Select(element =>
                    (element.Attribute("name")!.Value, element.Attribute("time")!.Value))
                .Should().Equal(("Intro", "1250.25"), ("Demo", "9375"));
            slideXml.Descendants(p + "pic").Should().ContainSingle();
        }

        ms.Position = 0;
        var reopened = PptxPackageReader.Read(ms);
        var timing = reopened.Slides[0].Shapes[0].Media!;
        timing.TrimStartMilliseconds.Should().BeApproximately(18374.0515, 0.0001);
        timing.TrimEndMilliseconds.Should().BeApproximately(29596.7072, 0.0001);
        timing.FadeInMilliseconds.Should().Be(1000);
        timing.FadeOutMilliseconds.Should().Be(250.5);
        timing.Bookmarks.Select(bookmark => (bookmark.Name, bookmark.TimeMilliseconds))
            .Should().Equal(("Intro", 1250.25), ("Demo", 9375d));
    }

    [Fact]
    public void GroupedMedia_LoopPlayback_RoundTripsThroughPresentationTiming()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var media = new SlideShape
        {
            Id = 8,
            Name = "Grouped looping video",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Media = new MediaInfo
            {
                IsVideo = true,
                Loop = true,
                Bytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70],
                ContentType = "video/mp4",
            },
        };
        var group = new SlideShape { Id = 80, Name = "Media group", Kind = SlideShapeKind.Group };
        group.Children.Add(media);
        slide.Shapes.Add(group);
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        var reopened = PptxPackageReader.Read(ms);
        var reopenedMedia = reopened.Slides[0].Shapes.Single().Children.Single();

        reopenedMedia.Id.Should().Be(8u);
        reopenedMedia.Media.Should().NotBeNull();
        reopenedMedia.Media!.Loop.Should().BeTrue();
        reopenedMedia.Media.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.InClickSequence);
    }

    [Fact]
    public void Media_ReadsCaptionTrackMetadataFromSlideRelationships()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "Captioned video",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
                ContentType = "video/mp4"
            }
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        AddCaptionTrack(ms);

        ms.Position = 0;
        var pres2 = PptxPackageReader.Read(ms);

        var track = pres2.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        track.RelationshipId.Should().Be("rIdCaption1");
        track.Source.Should().Be("ppt/media/captions1.vtt");
        track.ContentType.Should().Be("text/vtt");
        track.Language.Should().Be("en-US");
        track.Label.Should().Be("English captions");
        track.IsExternal.Should().BeFalse();
        Encoding.UTF8.GetString(track.Bytes).Should().Contain("Demo caption");

        var transcript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(pres2);
        transcript.Tracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(descriptor =>
                descriptor.ShapeId == 1 &&
                descriptor.Label == "English captions" &&
                descriptor.Language == "en-US" &&
                descriptor.Source == "ppt/media/captions1.vtt" &&
                descriptor.Status == PresentationMediaTranscriptTrackStatus.Available &&
                descriptor.CueCount == 1 &&
                descriptor.Cues[0].Text == "Demo caption");
    }

    [Fact]
    public void Media_WriterEmitsModeledCaptionTrackRelationshipsAndParts()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id          = 1,
            Name        = "Captioned video",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
                ContentType = "video/mp4",
            }
        };
        var authoring = PresentationMediaTranscriptPlanner.CreateInternalCaptionTrack(
            shape.Media,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                Label: "English captions",
                Language: "en-US",
                Source: "ppt/media/authored-captions.vtt",
                TranscriptText: null,
                Cues:
                [
                    new PresentationMediaTranscriptCueDescriptor(
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(1),
                        "Authored caption")
                ]));
        authoring.Succeeded.Should().BeTrue();
        var captionBytes = authoring.Track!.Bytes;

        slide.Shapes.Add(shape);
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            ReadText(zip, "ppt/media/slide1_caption1.vtt").Should().Contain("Authored caption");

            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var captionRel = rels.Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption");
            captionRel.Attribute("Id")!.Value.Should().Be("rIdCaption1");
            captionRel.Attribute("Target")!.Value.Should().Be("../media/slide1_caption1.vtt");
            captionRel.Attribute("TargetMode").Should().BeNull();

            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            var captionEl = slideXml.Descendants().Single(e => e.Name.LocalName == "caption");
            captionEl.Attribute(r + "embed")!.Value.Should().Be("rIdCaption1");
            captionEl.Attribute("lang")!.Value.Should().Be("en-US");
            captionEl.Attribute("label")!.Value.Should().Be("English captions");

            var contentTypes = ReadXml(zip, "[Content_Types].xml");
            var ct = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
            contentTypes.Root!.Elements(ct + "Default")
                .Single(e => e.Attribute("Extension")?.Value == "vtt")
                .Attribute("ContentType")!.Value.Should().Be("text/vtt");
        }

        ms.Position = 0;
        var roundTripped = PptxPackageReader.Read(ms);
        var track = roundTripped.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        track.RelationshipId.Should().Be("rIdCaption1");
        track.Source.Should().Be("ppt/media/slide1_caption1.vtt");
        track.ContentType.Should().Be("text/vtt");
        track.Language.Should().Be("en-US");
        track.Label.Should().Be("English captions");
        track.Bytes.Should().Equal(captionBytes);
    }

    [Fact]
    public void Media_PowerPointNativeCaptionPackage_ReadSaveReopen_PreservesBytesAndRelationshipContract()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "PowerPoint captioned video",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
                ContentType = "video/mp4"
            }
        });
        pres.Slides.Add(slide);

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        AddCaptionTrack(source);
        var expectedCaptionBytes = Encoding.UTF8.GetBytes("WEBVTT\r\n\r\n00:00.000 --> 00:01.000\r\nDemo caption\r\n");

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        loaded.RecordingMediaArtifacts.Should().BeEmpty(
            "PowerPoint-native media captions must stay separate from FreeP generated recording artifact manifests");
        loaded.PackageSnapshot.Should().NotBeNull();
        loaded.PackageSnapshot!.TryGetEntry("ppt/media/captions1.vtt", out var snapshotCaptionBytes)
            .Should().BeTrue("the original PowerPoint caption sidecar should be captured in the package snapshot");
        snapshotCaptionBytes.Should().Equal(expectedCaptionBytes);

        var loadedTrack = loaded.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        loadedTrack.RelationshipId.Should().Be("rIdCaption1");
        loadedTrack.Source.Should().Be("ppt/media/captions1.vtt");
        loadedTrack.ContentType.Should().Be("text/vtt");
        loadedTrack.Language.Should().Be("en-US");
        loadedTrack.Label.Should().Be("English captions");
        loadedTrack.IsExternal.Should().BeFalse();
        loadedTrack.Bytes.Should().Equal(expectedCaptionBytes);

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 3657600,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Text = "edit"
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            zip.GetEntry("ppt/media/recordingArtifacts.xml").Should().BeNull(
                "native PowerPoint caption packages are not FreeP generated recording artifacts");
            zip.Entries.Should().NotContain(entry =>
                entry.FullName.StartsWith("ppt/media/recording-captions/", StringComparison.OrdinalIgnoreCase));

            ReadBytes(zip, "ppt/media/captions1.vtt").Should().Equal(expectedCaptionBytes,
                "the native caption sidecar bytes should survive a modeled slide edit at the original PowerPoint package path");
            zip.GetEntry("ppt/media/slide1_caption1.vtt").Should().BeNull(
                "read/native caption tracks should not be unnecessarily renamed during save");

            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var captionRel = rels.Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption");
            captionRel.Attribute("Id")!.Value.Should().Be("rIdCaption1");
            captionRel.Attribute("Target")!.Value.Should().Be("../media/captions1.vtt");
            captionRel.Attribute("TargetMode").Should().BeNull();

            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            var captionEl = slideXml.Descendants().Single(e => e.Name.LocalName == "caption");
            captionEl.Name.NamespaceName.Should().Be("http://schemas.microsoft.com/office/powerpoint/2020/media");
            captionEl.Attribute(r + "embed")!.Value.Should().Be("rIdCaption1");
            captionEl.Attribute("lang")!.Value.Should().Be("en-US");
            captionEl.Attribute("label")!.Value.Should().Be("English captions");

            var contentTypes = ReadXml(zip, "[Content_Types].xml");
            var ct = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
            contentTypes.Root!.Elements(ct + "Default")
                .Single(e => string.Equals(e.Attribute("Extension")?.Value, "vtt", StringComparison.OrdinalIgnoreCase))
                .Attribute("ContentType")!.Value.Should().Be("text/vtt");
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        reopened.RecordingMediaArtifacts.Should().BeEmpty();
        var reopenedTrack = reopened.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        reopenedTrack.Source.Should().Be("ppt/media/captions1.vtt");
        reopenedTrack.ContentType.Should().Be("text/vtt");
        reopenedTrack.Language.Should().Be("en-US");
        reopenedTrack.Label.Should().Be("English captions");
        reopenedTrack.Bytes.Should().Equal(expectedCaptionBytes);
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == "Modeled edit");
    }

    [Fact]
    public void Media_PowerPointExternalCaptionTrack_ReadSaveReopen_PreservesRelationshipContract()
    {
        const string externalCaptionTarget = "captions/external-en.vtt";

        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "PowerPoint externally captioned video",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
                ContentType = "video/mp4"
            }
        });
        pres.Slides.Add(slide);

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        AddExternalCaptionTrack(source, externalCaptionTarget);

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        loaded.RecordingMediaArtifacts.Should().BeEmpty(
            "external PowerPoint caption relationships are media metadata, not FreeP recording artifacts");
        loaded.PackageSnapshot!.TryGetEntry("ppt/slides/captions/external-en.vtt", out _).Should().BeFalse(
            "TargetMode=External is authoritative even when the target string is relative");

        var loadedTrack = loaded.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        loadedTrack.RelationshipId.Should().Be("rIdCaptionExternal1");
        loadedTrack.Source.Should().Be(externalCaptionTarget);
        loadedTrack.ContentType.Should().Be("text/vtt");
        loadedTrack.Language.Should().Be("en-US");
        loadedTrack.Label.Should().Be("External English captions");
        loadedTrack.IsExternal.Should().BeTrue();
        loadedTrack.Bytes.Should().BeEmpty();

        var transcript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(loaded);
        transcript.Tracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(descriptor =>
                descriptor.ShapeId == 1 &&
                descriptor.Label == "External English captions" &&
                descriptor.Language == "en-US" &&
                descriptor.Source == externalCaptionTarget &&
                descriptor.Status == PresentationMediaTranscriptTrackStatus.External &&
                descriptor.CueCount == 0);

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 3657600,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Text = "edit"
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            zip.GetEntry("ppt/media/recordingArtifacts.xml").Should().BeNull(
                "external captions must not be converted to generated recording artifacts");
            zip.Entries.Should().NotContain(entry =>
                entry.FullName.StartsWith("ppt/media/recording-captions/", StringComparison.OrdinalIgnoreCase));
            zip.Entries.Should().NotContain(entry =>
                entry.FullName.Contains("external-en.vtt", StringComparison.OrdinalIgnoreCase),
                "external caption links must remain relationship metadata instead of authored package sidecars");

            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var captionRel = rels.Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption");
            captionRel.Attribute("Target")!.Value.Should().Be(externalCaptionTarget);
            captionRel.Attribute("TargetMode")!.Value.Should().Be("External");

            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            var captionEl = slideXml.Descendants().Single(e => e.Name.LocalName == "caption");
            captionEl.Name.NamespaceName.Should().Be("http://schemas.microsoft.com/office/powerpoint/2020/media");
            captionEl.Attribute(r + "link")!.Value.Should().Be(captionRel.Attribute("Id")!.Value);
            captionEl.Attribute(r + "embed").Should().BeNull();
            captionEl.Attribute("lang")!.Value.Should().Be("en-US");
            captionEl.Attribute("label")!.Value.Should().Be("External English captions");
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        reopened.RecordingMediaArtifacts.Should().BeEmpty();
        var reopenedTrack = reopened.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        reopenedTrack.Source.Should().Be(externalCaptionTarget);
        reopenedTrack.IsExternal.Should().BeTrue();
        reopenedTrack.Bytes.Should().BeEmpty();
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == "Modeled edit");
    }

    [Fact]
    public void Media_ReplacingExternalCaptionTrack_EmitsEmbeddedRelationshipAndPart()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Externally captioned video",
            Kind = SlideShapeKind.Media,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70],
                ContentType = "video/mp4",
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        RelationshipId = "rIdCaptionExternal1",
                        Source = "https://cdn.example.com/external.vtt",
                        Language = "en-US",
                        Label = "Remote captions",
                        IsExternal = true
                    }
                }
            }
        });
        pres.Slides.Add(slide);

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        var result = PresentationMediaTranscriptPlanner.ReplaceInternalCaptionTrack(
            loaded.Slides[0].Shapes[0].Media,
            0,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                "Local captions",
                null,
                "https://cdn.example.com/external.vtt",
                "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nEmbedded cue"));

        result.Succeeded.Should().BeTrue();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var captionRel = rels.Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption");
            captionRel.Attribute("TargetMode").Should().BeNull();
            captionRel.Attribute("Target")!.Value.Should().Be("../media/slide1_caption1.vtt");
            ReadText(zip, "ppt/media/slide1_caption1.vtt").Should().Contain("Embedded cue");
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        var reopenedTrack = reopened.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        reopenedTrack.IsExternal.Should().BeFalse();
        reopenedTrack.Source.Should().Be("ppt/media/slide1_caption1.vtt");
        Encoding.UTF8.GetString(reopenedTrack.Bytes).Should().Contain("Embedded cue");
    }

    [Fact]
    public void Media_PowerPointNativeCaptionPackage_WithMultipleCaptionTracks_PreservesCorpusRelationshipSet()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "PowerPoint multilingual captioned video",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
                ContentType = "video/mp4"
            }
        });
        pres.Slides.Add(slide);

        var tracks = new[]
        {
            new CaptionTrackFixture(
                "rIdCaption1",
                "ppt/media/captions1.vtt",
                "en-US",
                "English captions",
                "English demo caption"),
            new CaptionTrackFixture(
                "rIdCaption2",
                "ppt/media/captions-es.vtt",
                "es-ES",
                "Spanish subtitles",
                "Subtitulo de demostracion")
        };

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        AddCaptionTracks(source, tracks);

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        loaded.RecordingMediaArtifacts.Should().BeEmpty(
            "native caption tracks are PowerPoint media metadata, not FreeP recording artifacts");

        foreach (var fixture in tracks)
        {
            loaded.PackageSnapshot!.TryGetEntry(fixture.PackagePath, out var snapshotCaptionBytes)
                .Should().BeTrue($"caption sidecar {fixture.PackagePath} should be captured in the package snapshot");
            snapshotCaptionBytes.Should().Equal(CaptionPayload(fixture.Text));
        }

        var loadedTracks = loaded.Slides[0].Shapes[0].Media!.CaptionTracks;
        loadedTracks.Select(track => (track.RelationshipId, track.Source, track.ContentType, track.Language, track.Label, track.IsExternal))
            .Should().Equal(tracks.Select(fixture => (
                fixture.RelationshipId,
                fixture.PackagePath,
                "text/vtt",
                fixture.Language,
                fixture.Label,
                false)));
        loadedTracks.Zip(tracks).Should().OnlyContain(pair =>
            pair.First.Bytes.SequenceEqual(CaptionPayload(pair.Second.Text)));

        var transcript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(loaded);
        transcript.Tracks.Select(track => (track.Label, track.Language, track.Source, track.CueCount, CueText: track.Cues[0].Text))
            .Should().Equal(tracks.Select(fixture => (
                fixture.Label,
                fixture.Language,
                fixture.PackagePath,
                1,
                fixture.Text)));

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 3657600,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Text = "edit"
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            zip.GetEntry("ppt/media/recordingArtifacts.xml").Should().BeNull(
                "native PowerPoint caption packages must remain separate from FreeP generated recording artifacts");
            zip.Entries.Should().NotContain(entry =>
                entry.FullName.StartsWith("ppt/media/recording-captions/", StringComparison.OrdinalIgnoreCase));

            ReadBytes(zip, "ppt/media/captions1.vtt").Should().Equal(CaptionPayload(tracks[0].Text));
            ReadBytes(zip, "ppt/media/captions-es.vtt").Should().Equal(CaptionPayload(tracks[1].Text));
            zip.GetEntry("ppt/media/slide1_caption1.vtt").Should().BeNull();
            zip.GetEntry("ppt/media/slide1_caption2.vtt").Should().BeNull();

            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var captionRels = rels.Root!.Elements(relNs + "Relationship")
                .Where(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption")
                .ToArray();
            captionRels.Select(e => (Id: e.Attribute("Id")!.Value, Target: e.Attribute("Target")!.Value, TargetMode: e.Attribute("TargetMode")?.Value))
                .Should().Equal(
                    ("rIdCaption1", "../media/captions1.vtt", null),
                    ("rIdCaption2", "../media/captions-es.vtt", null));

            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            var captionEls = slideXml.Descendants()
                .Where(e => e.Name.LocalName == "caption")
                .ToArray();
            captionEls.Select(e => (
                    Namespace: e.Name.NamespaceName,
                    Embed: e.Attribute(r + "embed")!.Value,
                    Language: e.Attribute("lang")!.Value,
                    Label: e.Attribute("label")!.Value))
                .Should().Equal(
                    ("http://schemas.microsoft.com/office/powerpoint/2020/media", "rIdCaption1", "en-US", "English captions"),
                    ("http://schemas.microsoft.com/office/powerpoint/2020/media", "rIdCaption2", "es-ES", "Spanish subtitles"));
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        reopened.RecordingMediaArtifacts.Should().BeEmpty();
        reopened.Slides[0].Shapes[0].Media!.CaptionTracks
            .Select(track => (track.Source, track.Language, track.Label, Text: Encoding.UTF8.GetString(track.Bytes)))
            .Should().Equal(
                ("ppt/media/captions1.vtt", "en-US", "English captions", CaptionText(tracks[0].Text)),
                ("ppt/media/captions-es.vtt", "es-ES", "Spanish subtitles", CaptionText(tracks[1].Text)));
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == "Modeled edit");
    }

    [Fact]
    public void Media_PowerPointNativeCaptionPackage_NestedSidecar_PreservesOriginalPathRelationshipIdAndTranscript()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "PowerPoint nested captioned video",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
                ContentType = "video/mp4"
            }
        });
        pres.Slides.Add(slide);

        var fixture = new CaptionTrackFixture(
            "rIdPowerPointCaption42",
            "ppt/media/captionTracks/en-US/native-captions.vtt",
            "en-US",
            "Native nested captions",
            "Nested package caption");

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        AddCaptionTracks(source, [fixture]);

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        var loadedTrack = loaded.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        loadedTrack.RelationshipId.Should().Be("rIdPowerPointCaption42");
        loadedTrack.Source.Should().Be("ppt/media/captionTracks/en-US/native-captions.vtt");
        loaded.PackageSnapshot!.TryGetEntry(fixture.PackagePath, out var snapshotCaptionBytes).Should().BeTrue();
        snapshotCaptionBytes.Should().Equal(CaptionPayload(fixture.Text));

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            ReadBytes(zip, fixture.PackagePath).Should().Equal(CaptionPayload(fixture.Text));
            zip.GetEntry("ppt/media/slide1_caption1.vtt").Should().BeNull();

            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var captionRel = rels.Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption");
            captionRel.Attribute("Id")!.Value.Should().Be("rIdPowerPointCaption42");
            captionRel.Attribute("Target")!.Value.Should().Be("../media/captionTracks/en-US/native-captions.vtt");
            captionRel.Attribute("TargetMode").Should().BeNull();

            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            var captionEl = slideXml.Descendants().Single(e => e.Name.LocalName == "caption");
            captionEl.Attribute(r + "embed")!.Value.Should().Be("rIdPowerPointCaption42");
            captionEl.Attribute("lang")!.Value.Should().Be("en-US");
            captionEl.Attribute("label")!.Value.Should().Be("Native nested captions");
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        var transcript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(reopened);
        transcript.Tracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(descriptor =>
                descriptor.Source == fixture.PackagePath &&
                descriptor.Status == PresentationMediaTranscriptTrackStatus.Available &&
                descriptor.CueCount == 1 &&
                descriptor.Cues[0].Text == fixture.Text);
    }

    [Fact]
    public void Media_PowerPointNativeCaptionPackage_PreservesCaptionSidecarContentTypeOverride()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "PowerPoint override captioned video",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
                ContentType = "video/mp4"
            }
        });
        pres.Slides.Add(slide);

        var fixture = new CaptionTrackFixture(
            "rIdPowerPointCaptionOverride",
            "ppt/media/powerpoint-native-captions.vtt",
            "en-US",
            "PowerPoint override captions",
            "Content type override caption");
        const string captionOverrideContentType = "application/vnd.ms-powerpoint.media.caption+vtt";

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        AddCaptionTracks(source, [fixture]);
        AddContentTypeOverride(source, "/" + fixture.PackagePath, captionOverrideContentType);

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot!.TryGetEntry(fixture.PackagePath, out var snapshotBytes).Should().BeTrue();
        snapshotBytes.Should().Equal(CaptionPayload(fixture.Text));
        var loadedTrack = loaded.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        loadedTrack.Source.Should().Be(fixture.PackagePath);
        loadedTrack.ContentType.Should().Be(captionOverrideContentType,
            "PowerPoint-authored caption sidecar content-type overrides should feed the shared media-caption model");

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 3657600,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Text = "edit"
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            ReadBytes(zip, fixture.PackagePath).Should().Equal(CaptionPayload(fixture.Text));

            var contentTypes = ReadXml(zip, "[Content_Types].xml");
            var ct = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
            var captionOverrides = contentTypes.Root!.Elements(ct + "Override")
                .Where(e =>
                    string.Equals(e.Attribute("PartName")?.Value, "/" + fixture.PackagePath, StringComparison.OrdinalIgnoreCase) &&
                    e.Attribute("ContentType")?.Value == captionOverrideContentType)
                .ToArray();
            captionOverrides.Should().ContainSingle();

            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            rels.Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption")
                .Attribute("Target")!.Value.Should().Be("../media/powerpoint-native-captions.vtt");
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        var reopenedTrack = reopened.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        reopenedTrack.ContentType.Should().Be(captionOverrideContentType);
        var transcript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(reopened);
        transcript.Tracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(descriptor =>
                descriptor.Source == fixture.PackagePath &&
                descriptor.ContentType == captionOverrideContentType &&
                descriptor.Status == PresentationMediaTranscriptTrackStatus.Available &&
                descriptor.Cues[0].Text == fixture.Text);
    }

    [Fact]
    public void Media_PowerPointNativeTtmlCaptionPackage_RoundTripsAndPlansTranscriptMetadata()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "PowerPoint TTML captioned video",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
                ContentType = "video/mp4"
            }
        });
        pres.Slides.Add(slide);

        var fixture = new CaptionTrackFixture(
            "rIdPowerPointTtmlCaption",
            "ppt/media/ttml/native-caption.ttml",
            "en-US",
            "PowerPoint TTML captions",
            "TTML caption text");
        const string captionContentType = "application/ttml+xml";
        const string ttmlText = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tt xmlns="http://www.w3.org/ns/ttml">
              <body>
                <div>
                  <p begin="00:00:00.000" end="00:00:01.000">TTML caption text</p>
                </div>
              </body>
            </tt>
            """;
        var ttmlBytes = Encoding.UTF8.GetBytes(ttmlText);

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        AddCaptionTracks(source, [fixture]);
        AddContentTypeOverride(source, "/" + fixture.PackagePath, captionContentType);
        source.Position = 0;
        using (var sourceZip = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteBytes(sourceZip, fixture.PackagePath, ttmlBytes);
        }

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        loaded.RecordingMediaArtifacts.Should().BeEmpty(
            "PowerPoint-authored TTML captions are native media metadata, not FreeP generated recording artifacts");
        loaded.PackageSnapshot!.TryGetEntry(fixture.PackagePath, out var snapshotBytes).Should().BeTrue();
        snapshotBytes.Should().Equal(ttmlBytes);

        var loadedTrack = loaded.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        loadedTrack.RelationshipId.Should().Be(fixture.RelationshipId);
        loadedTrack.Source.Should().Be(fixture.PackagePath);
        loadedTrack.ContentType.Should().Be(captionContentType);
        loadedTrack.Language.Should().Be(fixture.Language);
        loadedTrack.Label.Should().Be(fixture.Label);
        loadedTrack.Bytes.Should().Equal(ttmlBytes);

        var loadedTranscript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(loaded);
        loadedTranscript.Tracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(descriptor =>
                descriptor.Source == fixture.PackagePath &&
                descriptor.ContentType == captionContentType &&
                descriptor.Status == PresentationMediaTranscriptTrackStatus.Available &&
                descriptor.CueCount == 1 &&
                descriptor.Cues[0].Text == "TTML caption text" &&
                descriptor.Cues[0].StartTime == TimeSpan.Zero &&
                descriptor.Cues[0].EndTime == TimeSpan.FromSeconds(1));

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 3657600,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Text = "edit"
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            zip.GetEntry("ppt/media/recordingArtifacts.xml").Should().BeNull();
            zip.Entries.Should().NotContain(entry =>
                entry.FullName.StartsWith("ppt/media/recording-captions/", StringComparison.OrdinalIgnoreCase));
            ReadBytes(zip, fixture.PackagePath).Should().Equal(ttmlBytes);
            zip.GetEntry("ppt/media/slide1_caption1.ttml").Should().BeNull();

            var contentTypes = ReadXml(zip, "[Content_Types].xml");
            var ct = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
            contentTypes.Root!.Elements(ct + "Override")
                .Where(e =>
                    string.Equals(e.Attribute("PartName")?.Value, "/" + fixture.PackagePath, StringComparison.OrdinalIgnoreCase) &&
                    e.Attribute("ContentType")?.Value == captionContentType)
                .Should().ContainSingle();

            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var captionRel = rels.Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption");
            captionRel.Attribute("Id")!.Value.Should().Be(fixture.RelationshipId);
            captionRel.Attribute("Target")!.Value.Should().Be("../media/ttml/native-caption.ttml");
            captionRel.Attribute("TargetMode").Should().BeNull();

            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            var captionEl = slideXml.Descendants().Single(e => e.Name.LocalName == "caption");
            captionEl.Attribute(r + "embed")!.Value.Should().Be(fixture.RelationshipId);
            captionEl.Attribute("lang")!.Value.Should().Be(fixture.Language);
            captionEl.Attribute("label")!.Value.Should().Be(fixture.Label);
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        reopened.RecordingMediaArtifacts.Should().BeEmpty();
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == "Modeled edit");
        var reopenedTrack = reopened.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        reopenedTrack.Source.Should().Be(fixture.PackagePath);
        reopenedTrack.ContentType.Should().Be(captionContentType);
        reopenedTrack.Bytes.Should().Equal(ttmlBytes);

        var transcript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(reopened);
        transcript.Tracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(descriptor =>
                descriptor.Source == fixture.PackagePath &&
                descriptor.Status == PresentationMediaTranscriptTrackStatus.Available &&
                descriptor.CueCount == 1 &&
                descriptor.Cues[0].Text == "TTML caption text" &&
                descriptor.Cues[0].StartTime == TimeSpan.Zero &&
                descriptor.Cues[0].EndTime == TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Media_PowerPointNativeCaptionPackage_SharedSidecarAcrossSlides_WritesOnePackagePart()
    {
        var pres = new Presentation();
        pres.Slides.Add(CreateCaptionedMediaSlide(1, "Shared caption video 1"));
        pres.Slides.Add(CreateCaptionedMediaSlide(2, "Shared caption video 2"));

        var slide1Track = new CaptionTrackFixture(
            "rIdSlide1SharedCaption",
            "ppt/media/shared-captions/native-en.vtt",
            "en-US",
            "Shared English captions",
            "Shared native caption");
        var slide2Track = slide1Track with { RelationshipId = "rIdSlide2SharedCaption" };

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        AddCaptionTracks(source, slideIndex: 1, [slide1Track]);
        AddCaptionTracks(source, slideIndex: 2, [slide2Track]);

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        loaded.Slides.Should().HaveCount(2);
        loaded.PackageSnapshot!.TryGetEntry(slide1Track.PackagePath, out var snapshotCaptionBytes).Should().BeTrue();
        snapshotCaptionBytes.Should().Equal(CaptionPayload(slide1Track.Text));
        loaded.Slides.Select(slide => slide.Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject)
            .Select(track => (track.Source, track.Label, Text: Encoding.UTF8.GetString(track.Bytes)))
            .Should().Equal(
                (slide1Track.PackagePath, slide1Track.Label, CaptionText(slide1Track.Text)),
                (slide1Track.PackagePath, slide1Track.Label, CaptionText(slide1Track.Text)));

        loaded.Slides[1].Shapes.Add(new SlideShape
        {
            Id = 99,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 3657600,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Text = "edit"
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            zip.Entries.Count(entry => string.Equals(entry.FullName, slide1Track.PackagePath, StringComparison.OrdinalIgnoreCase))
                .Should().Be(1, "a shared native caption sidecar should be materialized once in the PPTX package");
            ReadBytes(zip, slide1Track.PackagePath).Should().Equal(CaptionPayload(slide1Track.Text));
            zip.GetEntry("ppt/media/slide1_caption1.vtt").Should().BeNull();
            zip.GetEntry("ppt/media/slide2_caption1.vtt").Should().BeNull();

            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels").Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption")
                .Should().Match<XElement>(e =>
                    e.Attribute("Id")!.Value == "rIdSlide1SharedCaption" &&
                    e.Attribute("Target")!.Value == "../media/shared-captions/native-en.vtt" &&
                    e.Attribute("TargetMode") == null);
            ReadXml(zip, "ppt/slides/_rels/slide2.xml.rels").Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption")
                .Should().Match<XElement>(e =>
                    e.Attribute("Id")!.Value == "rIdSlide2SharedCaption" &&
                    e.Attribute("Target")!.Value == "../media/shared-captions/native-en.vtt" &&
                    e.Attribute("TargetMode") == null);
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        var transcript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(reopened);
        transcript.Tracks.Select(track => (track.SlideIndex, track.Source, track.CueCount, CueText: track.Cues[0].Text))
            .Should().Equal(
                (0, slide1Track.PackagePath, 1, slide1Track.Text),
                (1, slide1Track.PackagePath, 1, slide1Track.Text));
        reopened.Slides[1].Shapes.Should().Contain(shape => shape.Name == "Modeled edit");
    }

    [Fact]
    public void Media_PowerPointNativeMediaAndCaptionPackage_SemanticEdit_PreservesAuthoredSidecarPaths()
    {
        const string nativeMediaPath = "ppt/media/powerpoint/native-video.mp4";
        var nativeCaptionTrack = new CaptionTrackFixture(
            "rIdPowerPointNativeCaption",
            "ppt/media/powerpoint/native-video-en.vtt",
            "en-US",
            "Native video captions",
            "Native media caption");
        var mediaBytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };

        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "PowerPoint native media",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = mediaBytes,
                ContentType = "video/mp4"
            }
        });
        pres.Slides.Add(slide);

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        MoveMediaPackagePart(source, slideIndex: 1, nativeMediaPath);
        AddCaptionTracks(source, [nativeCaptionTrack]);

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot!.TryGetEntry(nativeMediaPath, out var snapshotMediaBytes)
            .Should().BeTrue("the original PowerPoint media sidecar should be captured in the package snapshot");
        snapshotMediaBytes.Should().Equal(mediaBytes);
        loaded.PackageSnapshot.TryGetEntry(nativeCaptionTrack.PackagePath, out var snapshotCaptionBytes)
            .Should().BeTrue("the original PowerPoint caption sidecar should be captured in the package snapshot");
        snapshotCaptionBytes.Should().Equal(CaptionPayload(nativeCaptionTrack.Text));

        var loadedMedia = loaded.Slides[0].Shapes[0].Media!;
        loadedMedia.SourcePackagePath.Should().Be(nativeMediaPath);
        loadedMedia.Bytes.Should().Equal(mediaBytes);
        loadedMedia.CaptionTracks.Should().ContainSingle()
            .Which.Should().Match<MediaCaptionTrackInfo>(track =>
                track.RelationshipId == nativeCaptionTrack.RelationshipId &&
                track.Source == nativeCaptionTrack.PackagePath &&
                track.Language == nativeCaptionTrack.Language &&
                track.Label == nativeCaptionTrack.Label &&
                track.Bytes.SequenceEqual(CaptionPayload(nativeCaptionTrack.Text)));

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 3657600,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Text = "edit"
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            ReadBytes(zip, nativeMediaPath).Should().Equal(mediaBytes,
                "the native PowerPoint media sidecar should survive a modeled slide edit at the original package path");
            ReadBytes(zip, nativeCaptionTrack.PackagePath).Should().Equal(CaptionPayload(nativeCaptionTrack.Text));
            zip.GetEntry("ppt/media/slide1_video1.mp4").Should().BeNull(
                "read/native media files should not be unnecessarily renamed during save");
            zip.GetEntry("ppt/media/slide1_caption1.vtt").Should().BeNull(
                "read/native caption tracks should not be unnecessarily renamed during save");

            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            rels.Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/video")
                .Attribute("Target")!.Value.Should().Be("../media/powerpoint/native-video.mp4");
            rels.Root!.Elements(relNs + "Relationship")
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption")
                .Attribute("Target")!.Value.Should().Be("../media/powerpoint/native-video-en.vtt");
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        reopened.Slides[0].Shapes[0].Media!.SourcePackagePath.Should().Be(nativeMediaPath);
        reopened.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle()
            .Which.Source.Should().Be(nativeCaptionTrack.PackagePath);
        var transcript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(reopened);
        transcript.Tracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(descriptor =>
                descriptor.Source == nativeCaptionTrack.PackagePath &&
                descriptor.Status == PresentationMediaTranscriptTrackStatus.Available &&
                descriptor.Cues[0].Text == nativeCaptionTrack.Text);
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == "Modeled edit");
    }

    [Fact]
    public void Media_PowerPointNativeCaptionPackage_CollidingRelationshipId_RetargetsCaptionMetadata()
    {
        var nativeCaptionTrack = new CaptionTrackFixture(
            "rIdMedia1",
            "ppt/media/collision/native-video-en.vtt",
            "en-US",
            "Collision captions",
            "Relationship collision caption");

        var pres = new Presentation();
        pres.Slides.Add(CreateCaptionedMediaSlide(1, "Relationship collision video"));

        using var source = new MemoryStream();
        PptxPackageWriter.Write(pres, source);
        RenamePosterImageRelationshipId(source, slideIndex: 1, "rIdPosterOriginal");
        AddCaptionTracks(source, [nativeCaptionTrack]);

        source.Position = 0;
        var loaded = PptxPackageReader.Read(source);
        var loadedTrack = loaded.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        loadedTrack.RelationshipId.Should().Be("rIdMedia1",
            "the source package can use an id that later collides with FreeP's generated poster relationship id");
        loadedTrack.Source.Should().Be(nativeCaptionTrack.PackagePath);
        loadedTrack.Bytes.Should().Equal(CaptionPayload(nativeCaptionTrack.Text));

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 3657600,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Text = "edit"
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        saved.Position = 0;
        using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            ReadBytes(zip, nativeCaptionTrack.PackagePath).Should().Equal(CaptionPayload(nativeCaptionTrack.Text));

            var rels = ReadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            var relationships = rels.Root!.Elements(relNs + "Relationship").ToArray();
            relationships.Select(e => e.Attribute("Id")!.Value).Should().OnlyHaveUniqueItems();

            relationships
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image")
                .Attribute("Id")!.Value.Should().Be("rIdMedia1");

            var captionRel = relationships
                .Single(e => e.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/mediaCaption");
            captionRel.Attribute("Id")!.Value.Should().Be("rIdCaption1",
                "the native caption relationship id must move away from the writer-owned poster id");
            captionRel.Attribute("Target")!.Value.Should().Be("../media/collision/native-video-en.vtt");
            captionRel.Attribute("TargetMode").Should().BeNull();

            var slideXml = ReadXml(zip, "ppt/slides/slide1.xml");
            var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            var captionEl = slideXml.Descendants().Single(e => e.Name.LocalName == "caption");
            captionEl.Attribute(r + "embed")!.Value.Should().Be("rIdCaption1",
                "the p20media:caption metadata must point at the remapped caption relationship");
            captionEl.Attribute("lang")!.Value.Should().Be(nativeCaptionTrack.Language);
            captionEl.Attribute("label")!.Value.Should().Be(nativeCaptionTrack.Label);
        }

        saved.Position = 0;
        var reopened = PptxPackageReader.Read(saved);
        var reopenedTrack = reopened.Slides[0].Shapes[0].Media!.CaptionTracks.Should().ContainSingle().Subject;
        reopenedTrack.RelationshipId.Should().Be("rIdCaption1");
        reopenedTrack.Source.Should().Be(nativeCaptionTrack.PackagePath);
        reopenedTrack.Bytes.Should().Equal(CaptionPayload(nativeCaptionTrack.Text));

        var transcript = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(reopened);
        transcript.Tracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(descriptor =>
                descriptor.Source == nativeCaptionTrack.PackagePath &&
                descriptor.Status == PresentationMediaTranscriptTrackStatus.Available &&
                descriptor.Cues[0].Text == nativeCaptionTrack.Text);
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == "Modeled edit");
    }

    [Fact]
    public void Media_SlideCloner_ClonesMedia()
    {
        var shape = new SlideShape
        {
            Id      = 1,
            Kind    = SlideShapeKind.Media,
            Picture = new ImagePart { Bytes = new byte[] { 1, 2, 3 }, ContentType = "image/png" },
            Media   = new MediaInfo { IsVideo = true, Loop = true, Bytes = new byte[] { 4, 5, 6 }, ContentType = "video/mp4" },
        };
        var slide = new Slide();
        slide.Shapes.Add(shape);

        var cloned = SlideCloner.CloneSlide(slide);
        var cs     = cloned.Shapes[0];

        Assert.Equal(SlideShapeKind.Media, cs.Kind);
        Assert.Same(shape.Picture, cs.Picture);  // bytes shared (immutable)
        Assert.NotSame(shape.Media, cs.Media);   // mutable caption state is isolated
        Assert.NotSame(shape.Media!.Bytes, cs.Media!.Bytes);
        Assert.Equal(shape.Media.Bytes, cs.Media.Bytes);
        Assert.Equal(shape.Media.ContentType, cs.Media.ContentType);
        Assert.Equal(shape.Media.IsVideo, cs.Media.IsVideo);
        Assert.Equal(shape.Media.Loop, cs.Media.Loop);
    }

    [Fact]
    public void Media_Compositor_EmitsPictureOpWithIsMedia()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        var posterBytes = CreateMinimal1x1Png();

        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = posterBytes, ContentType = "image/png" },
            Media   = new MediaInfo { IsVideo = true, Bytes = new byte[] { 0x00 }, ContentType = "video/mp4" },
        });
        pres.Slides.Add(slide);

        var ops   = SlideCompositor.Compose(pres, slide, slideIndex: 0);
        var picOp = ops.OfType<DrawOp.Picture>().FirstOrDefault();

        Assert.NotNull(picOp);
        Assert.True(picOp!.IsMedia);
    }

    // ── Field tests ───────────────────────────────────────────────────────────

    [Fact]
    public void Field_SlideNum_ResolvesToSlideIndex()
    {
        var pres  = new Presentation();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "1",
            Field = new FieldRun { FieldType = "slidenum", CachedText = "1" },
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);

        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 6400000,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 457200,
            Placeholder = new Placeholder { Type = PlaceholderType.SlideNumber },
            TextBody    = body,
        });
        pres.Slides.Add(slide);

        // Compose as slide index 2 (0-based) → should show "3"
        var ops      = SlideCompositor.Compose(pres, slide, slideIndex: 2);
        var shapeOp  = ops.OfType<DrawOp.Shape>().FirstOrDefault();

        Assert.NotNull(shapeOp);
        var resolvedPara = shapeOp!.Text?.Paragraphs.FirstOrDefault();
        Assert.NotNull(resolvedPara);
        var runText = string.Concat(resolvedPara!.Runs.Select(r => r.Text));
        Assert.Contains("3", runText);
    }

    [Fact]
    public void Field_DateTime_UsesCachedText()
    {
        var pres  = new Presentation();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "1/1/2026",
            Field = new FieldRun { FieldType = "datetime1", CachedText = "1/1/2026" },
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);

        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 6400000,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 457200,
            Placeholder = new Placeholder { Type = PlaceholderType.DateTime },
            TextBody    = body,
        });
        pres.Slides.Add(slide);

        var ops     = SlideCompositor.Compose(pres, slide, slideIndex: 0);
        var shapeOp = ops.OfType<DrawOp.Shape>().FirstOrDefault();

        Assert.NotNull(shapeOp);
        var runText = string.Concat(
            shapeOp!.Text?.Paragraphs.SelectMany(p => p.Runs.Select(r => r.Text)) ?? []);
        Assert.Contains("1/1/2026", runText);
    }

    // II2: p:hf is NOT allowed on p:sld (CT_Slide schema). Verify the writer never emits it.
    [Fact]
    public void Slide_HfVisibility_DoesNotEmitHfOnSld()
    {
        var pres  = new Presentation();
        var slide = new Slide
        {
            HfVisibility = new HfFlags
            {
                ShowFooter   = true,
                ShowDate     = false,
                ShowSlideNum = true,
            }
        };
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        // Verify the written slide XML has NO p:hf child of p:sld (schema-invalid)
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.GetEntry("ppt/slides/slide1.xml")!;
        using var sr = slideEntry.Open();
        var doc = XDocument.Load(sr);
        var P = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var hfEl = doc.Root!.Element(P + "hf");
        Assert.Null(hfEl); // must NOT be present on p:sld
    }

    [Fact]
    public void Field_FieldRun_RoundTrips()
    {
        var pres  = new Presentation();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "5",
            Field = new FieldRun { FieldType = "slidenum", CachedText = "5" },
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);

        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 6400000,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 457200,
            TextBody    = body,
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var pres2 = PptxPackageReader.Read(ms);

        var body2 = pres2.Slides[0].Shapes[0].TextBody;
        Assert.NotNull(body2);
        var run2 = body2!.Paragraphs[0].Runs[0];
        Assert.NotNull(run2.Field);
        Assert.Equal("slidenum", run2.Field!.FieldType);
        Assert.Equal("5", run2.Field.CachedText);
    }

    [Fact]
    public void Field_FieldRun_PreservesNativeIdentityAndDirtyState()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "2",
                    Field = new FieldRun
                    {
                        FieldType = "slidenum",
                        Id = "{AUTHORED-FIELD-ID}",
                        Dirty = true,
                        CachedText = "2",
                    },
                },
                new Run
                {
                    Text = "3",
                    Field = new FieldRun
                    {
                        FieldType = "slidenum",
                        Id = "{EXPLICIT-CLEAN-FIELD}",
                        Dirty = false,
                        CachedText = "3",
                    },
                },
            },
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 457200,
            TextBody = body,
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var reopenedRuns = PptxPackageReader.Read(ms).Slides[0].Shapes[0].TextBody!
            .Paragraphs[0].Runs;

        var authored = reopenedRuns[0].Field!;
        var clean = reopenedRuns[1].Field!;
        authored.Id.Should().Be("{AUTHORED-FIELD-ID}");
        authored.Dirty.Should().BeTrue();
        clean.Id.Should().Be("{EXPLICIT-CLEAN-FIELD}");
        clean.Dirty.Should().BeFalse();
    }

    [Fact]
    public void Run_PreservesNativeLanguageAndAlternateLanguageAndDirtyState()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "Bonjour",
                    Language = "fr-FR",
                    AlternateLanguage = "en-US",
                    Dirty = true,
                    NoProof = false,
                    Error = true,
                },
            },
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 457200,
            TextBody = body,
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var reopened = PptxPackageReader.Read(ms).Slides[0].Shapes[0].TextBody!
            .Paragraphs[0].Runs.Single();

        reopened.Language.Should().Be("fr-FR");
        reopened.AlternateLanguage.Should().Be("en-US");
        reopened.Dirty.Should().BeTrue();
        reopened.NoProof.Should().BeFalse();
        reopened.Error.Should().BeTrue();
    }

    [Fact]
    public void Field_FieldRun_PreservesFontAndColor()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "2",
                    Field = new FieldRun
                    {
                        FieldType = "PAGE",
                        CachedText = "2",
                        FontFamily = "Calibri",
                        FontSizePt = 14,
                        Bold = true,
                        Color = new SrgbColor(31, 78, 121),
                    },
                },
            },
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 457200,
            TextBody = body,
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var run = PptxPackageReader.Read(ms).Slides[0].Shapes[0].TextBody!
            .Paragraphs[0].Runs[0];

        run.Field.Should().NotBeNull();
        run.Field!.FontFamily.Should().Be("Calibri");
        run.Field.FontSizePt.Should().Be(14);
        run.Field.Bold.Should().BeTrue();
        run.Field.Color.Should().Be(new SrgbColor(31, 78, 121));
    }

    // II1: embedded mp4 media → [Content_Types].xml must have Default Extension="mp4"
    [Fact]
    public void ContentTypes_MediaShape_HasVideoExtensionDefault()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "Video 1",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media   = new MediaInfo { IsVideo = true, Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }, ContentType = "video/mp4" },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var ctEntry = zip.GetEntry("[Content_Types].xml")!;
        using var ctStream = ctEntry.Open();
        var ct = XDocument.Load(ctStream);
        var CT = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
        var mp4Default = ct.Root!.Elements(CT + "Default")
            .FirstOrDefault(e => string.Equals(e.Attribute("Extension")?.Value, "mp4", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mp4Default); // must have Default Extension="mp4" for video/mp4
        Assert.Equal("video/mp4", mp4Default!.Attribute("ContentType")?.Value);
    }

    // HH1: picture-fill-only deck → [Content_Types].xml must have Default for fill image extension
    [Fact]
    public void ContentTypes_PictureFillOnly_HasImageExtensionDefault()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        // AutoShape with a jpeg picture fill — no Picture shape — only fill contributes extension
        slide.Shapes.Add(new SlideShape
        {
            Id          = 2,
            Name        = "Rect 1",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Fill = new ShapeFill.Picture(imageBytes: CreateMinimal1x1Png(), contentType: "image/png", tile: false),
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var ctEntry = zip.GetEntry("[Content_Types].xml")!;
        using var ctStream = ctEntry.Open();
        var ct = XDocument.Load(ctStream);
        var CT = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
        var pngDefault = ct.Root!.Elements(CT + "Default")
            .FirstOrDefault(e => string.Equals(e.Attribute("Extension")?.Value, "png", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(pngDefault); // picture-fill image must register its extension
        Assert.Equal("image/png", pngDefault!.Attribute("ContentType")?.Value);
    }

    // II4: media shape with no poster bytes → no dangling rIdMedia1 in blipFill
    [Fact]
    public void MediaShape_NoPoster_NoDanglingBlipRef()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 3,
            Name        = "Audio 1",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = null, // no poster
            Media   = new MediaInfo { IsVideo = false, Bytes = new byte[] { 0xFF, 0xFB, 0x90, 0x00 }, ContentType = "audio/mpeg" },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.GetEntry("ppt/slides/slide1.xml")!;
        using var sr = slideEntry.Open();
        var doc = XDocument.Load(sr);
        var P = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var A = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var R = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        // KK1: CT_Picture requires a:blipFill (minOccurs=1); it must always be present.
        var blipFill = doc.Descendants(P + "blipFill").FirstOrDefault();
        Assert.NotNull(blipFill); // schema-required — must always be emitted
        // When there is no poster the blipFill must NOT carry a dangling r:embed relationship.
        var embedVal = blipFill!.Descendants(A + "blip")
            .Select(b => b.Attribute(R + "embed")?.Value)
            .FirstOrDefault();
        Assert.Null(embedVal); // no-poster path: either no a:blip or blip has no r:embed attribute
    }

    // HH2: out-of-order gradient stops are sorted on write (ascending pos)
    // HH3: single-stop gradient is synthesised to 2 stops
    [Fact]
    public void Gradient_OutOfOrder_WrittenSorted()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        // Two stops in reverse order (1.0 before 0.0) → writer must sort to ascending
        var stops = new System.Collections.Generic.List<GradientStop>
        {
            new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0, 0, 0))),   // black at end
            new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(255, 255, 255))), // white at start
        };
        slide.Shapes.Add(new SlideShape
        {
            Id          = 4,
            Name        = "Rect 2",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Fill = new ShapeFill.Gradient(stops, GradientKind.Linear, angleDegrees: 0),
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.GetEntry("ppt/slides/slide1.xml")!;
        using var sr = slideEntry.Open();
        var doc = XDocument.Load(sr);
        var A = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var gsElements = doc.Descendants(A + "gs").ToList();
        Assert.True(gsElements.Count >= 2, "gradient must have at least 2 stops");
        var positions = gsElements.Select(e => int.Parse(e.Attribute("pos")?.Value ?? "0")).ToList();
        for (int i = 1; i < positions.Count; i++)
            Assert.True(positions[i] >= positions[i - 1], $"stop {i} pos {positions[i]} must be >= stop {i-1} pos {positions[i-1]}");
    }

    [Fact]
    public void Gradient_SingleStop_SynthesisedToTwoStops()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        var stops = new System.Collections.Generic.List<GradientStop>
        {
            new GradientStop(0.5, new ThemeAwareColor(new SrgbColor(128, 0, 0))),
        };
        slide.Shapes.Add(new SlideShape
        {
            Id          = 5,
            Name        = "Rect 3",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Fill = new ShapeFill.Gradient(stops, GradientKind.Linear, angleDegrees: 45),
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.GetEntry("ppt/slides/slide1.xml")!;
        using var sr = slideEntry.Open();
        var doc = XDocument.Load(sr);
        var A = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var gsElements = doc.Descendants(A + "gs").ToList();
        Assert.True(gsElements.Count >= 2, "1-stop gradient must be synthesised to >=2 stops");
    }

    [Fact]
    public void Gradient_ZeroStops_SynthesisedToTwoStops()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        var stops = new System.Collections.Generic.List<GradientStop>();  // empty
        slide.Shapes.Add(new SlideShape
        {
            Id          = 6,
            Name        = "Rect 4",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Fill = new ShapeFill.Gradient(stops, GradientKind.Linear, angleDegrees: 90),
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);

        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.GetEntry("ppt/slides/slide1.xml")!;
        using var sr = slideEntry.Open();
        var doc = XDocument.Load(sr);
        var A = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var gsElements = doc.Descendants(A + "gs").ToList();
        Assert.True(gsElements.Count >= 2, "0-stop gradient must be synthesised to >=2 stops");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static byte[] CreateMinimal1x1Png() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    private sealed record CaptionTrackFixture(
        string RelationshipId,
        string PackagePath,
        string Language,
        string Label,
        string Text);

    private static byte[] CaptionPayload(string text)
        => Encoding.UTF8.GetBytes(CaptionText(text));

    private static string CaptionText(string text)
        => $"WEBVTT\r\n\r\n00:00.000 --> 00:01.000\r\n{text}\r\n";

    private static void AddCaptionTrack(MemoryStream package)
        => AddCaptionTracks(
            package,
            [
                new CaptionTrackFixture(
                    "rIdCaption1",
                    "ppt/media/captions1.vtt",
                    "en-US",
                    "English captions",
                    "Demo caption")
            ]);

    private static void AddExternalCaptionTrack(MemoryStream package, string externalCaptionTarget)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var rels = ReadXml(archive, "ppt/slides/_rels/slide1.xml.rels");
        var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
        rels.Root!.Add(new XElement(
            relNs + "Relationship",
            new XAttribute("Id", "rIdCaptionExternal1"),
            new XAttribute("Type", "http://schemas.microsoft.com/office/2011/relationships/mediaCaption"),
            new XAttribute("Target", externalCaptionTarget),
            new XAttribute("TargetMode", "External")));
        WriteXml(archive, "ppt/slides/_rels/slide1.xml.rels", rels);

        var slide = ReadXml(archive, "ppt/slides/slide1.xml");
        var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var c = XNamespace.Get("http://schemas.microsoft.com/office/powerpoint/2020/media");
        var nvPr = slide.Descendants(p + "nvPr").First(element => element.Element(a + "videoFile") is not null);
        var extLst = nvPr.Element(p + "extLst");
        if (extLst is null)
        {
            extLst = new XElement(p + "extLst");
            nvPr.Add(extLst);
        }

        extLst.Add(new XElement(
            c + "caption",
            new XAttribute(r + "link", "rIdCaptionExternal1"),
            new XAttribute("lang", "en-US"),
            new XAttribute("label", "External English captions")));
        WriteXml(archive, "ppt/slides/slide1.xml", slide);
    }

    private static void AddCaptionTracks(MemoryStream package, IReadOnlyList<CaptionTrackFixture> tracks)
        => AddCaptionTracks(package, slideIndex: 1, tracks);

    private static void AddCaptionTracks(MemoryStream package, int slideIndex, IReadOnlyList<CaptionTrackFixture> tracks)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var slidePath = $"ppt/slides/slide{slideIndex}.xml";
        var relsPath = $"ppt/slides/_rels/slide{slideIndex}.xml.rels";
        var rels = ReadXml(archive, relsPath);
        var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
        foreach (var track in tracks)
        {
            rels.Root!.Add(new XElement(
                relNs + "Relationship",
                new XAttribute("Id", track.RelationshipId),
                new XAttribute("Type", "http://schemas.microsoft.com/office/2011/relationships/mediaCaption"),
                new XAttribute("Target", CaptionRelationshipTarget(track.PackagePath))));
        }
        WriteXml(archive, relsPath, rels);

        var slide = ReadXml(archive, slidePath);
        var p = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var c = XNamespace.Get("http://schemas.microsoft.com/office/powerpoint/2020/media");
        var nvPr = slide.Descendants(p + "nvPr").First(element => element.Element(a + "videoFile") is not null);
        var extLst = nvPr.Element(p + "extLst");
        if (extLst is null)
        {
            extLst = new XElement(p + "extLst");
            nvPr.Add(extLst);
        }

        foreach (var track in tracks)
        {
            extLst.Add(new XElement(
                c + "caption",
                new XAttribute(r + "embed", track.RelationshipId),
                new XAttribute("lang", track.Language),
                new XAttribute("label", track.Label)));
        }
        WriteXml(archive, slidePath, slide);

        foreach (var track in tracks)
        {
            WriteText(archive, track.PackagePath, CaptionText(track.Text));
        }
    }

    private static void MoveMediaPackagePart(MemoryStream package, int slideIndex, string destinationPath)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var relsPath = $"ppt/slides/_rels/slide{slideIndex}.xml.rels";
        var rels = ReadXml(archive, relsPath);
        var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
        var mediaRel = rels.Root!.Elements(relNs + "Relationship")
            .Single(e => e.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/video");
        var oldPath = "ppt/media/" + Path.GetFileName(mediaRel.Attribute("Target")!.Value);
        var mediaBytes = ReadBytes(archive, oldPath);
        archive.GetEntry(oldPath)!.Delete();
        WriteBytes(archive, destinationPath, mediaBytes);
        mediaRel.SetAttributeValue("Target", CaptionRelationshipTarget(destinationPath));
        WriteXml(archive, relsPath, rels);
    }

    private static void AddContentTypeOverride(MemoryStream package, string partName, string contentType)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var contentTypes = ReadXml(archive, "[Content_Types].xml");
        var ct = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
        contentTypes.Root!.Elements(ct + "Override")
            .Where(e => string.Equals(e.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase))
            .Remove();
        contentTypes.Root!.Add(new XElement(
            ct + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
        WriteXml(archive, "[Content_Types].xml", contentTypes);
    }

    private static void RenamePosterImageRelationshipId(MemoryStream package, int slideIndex, string replacementRelId)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var relsPath = $"ppt/slides/_rels/slide{slideIndex}.xml.rels";
        var rels = ReadXml(archive, relsPath);
        var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
        var imageRel = rels.Root!.Elements(relNs + "Relationship")
            .Single(e => e.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image");
        var originalRelId = imageRel.Attribute("Id")!.Value;
        imageRel.SetAttributeValue("Id", replacementRelId);
        WriteXml(archive, relsPath, rels);

        var slidePath = $"ppt/slides/slide{slideIndex}.xml";
        var slide = ReadXml(archive, slidePath);
        var a = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var r = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var blip = slide.Descendants(a + "blip")
            .Single(e => e.Attribute(r + "embed")?.Value == originalRelId);
        blip.SetAttributeValue(r + "embed", replacementRelId);
        WriteXml(archive, slidePath, slide);
    }

    private static byte[] ReadBytes(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static void WriteXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void WriteText(ZipArchive archive, string path, string text)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static void WriteBytes(ZipArchive archive, string path, byte[] bytes)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static Slide CreateCaptionedMediaSlide(uint shapeId, string shapeName)
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = shapeId,
            Name        = shapeName,
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 914400,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 2743200,
            Picture = new ImagePart { Bytes = CreateMinimal1x1Png(), ContentType = "image/png" },
            Media = new MediaInfo
            {
                IsVideo = true,
                Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
                ContentType = "video/mp4"
            }
        });

        return slide;
    }

    private static string ReadText(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string CaptionRelationshipTarget(string packagePath)
    {
        const string mediaPrefix = "ppt/media/";
        var normalized = packagePath.Replace('\\', '/');
        normalized.Should().StartWith(mediaPrefix);
        return "../media/" + normalized[mediaPrefix.Length..];
    }
}
