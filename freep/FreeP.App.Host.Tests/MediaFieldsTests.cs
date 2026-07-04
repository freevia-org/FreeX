using System.IO;
using System.IO.Compression;
using System.Linq;
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
    }

    [Fact]
    public void Media_SlideCloner_ClonesMedia()
    {
        var shape = new SlideShape
        {
            Id      = 1,
            Kind    = SlideShapeKind.Media,
            Picture = new ImagePart { Bytes = new byte[] { 1, 2, 3 }, ContentType = "image/png" },
            Media   = new MediaInfo { IsVideo = true, Bytes = new byte[] { 4, 5, 6 }, ContentType = "video/mp4" },
        };
        var slide = new Slide();
        slide.Shapes.Add(shape);

        var cloned = SlideCloner.CloneSlide(slide);
        var cs     = cloned.Shapes[0];

        Assert.Equal(SlideShapeKind.Media, cs.Kind);
        Assert.Same(shape.Picture, cs.Picture);  // bytes shared (immutable)
        Assert.Same(shape.Media,   cs.Media);    // MediaInfo shared (immutable)
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

    private static void AddCaptionTrack(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var rels = ReadXml(archive, "ppt/slides/_rels/slide1.xml.rels");
        var relNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
        rels.Root!.Add(new XElement(
            relNs + "Relationship",
            new XAttribute("Id", "rIdCaption1"),
            new XAttribute("Type", "http://schemas.microsoft.com/office/2011/relationships/mediaCaption"),
            new XAttribute("Target", "../media/captions1.vtt")));
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
            new XAttribute(r + "embed", "rIdCaption1"),
            new XAttribute("lang", "en-US"),
            new XAttribute("label", "English captions")));
        WriteXml(archive, "ppt/slides/slide1.xml", slide);

        WriteText(archive, "ppt/media/captions1.vtt", "WEBVTT\r\n\r\n00:00.000 --> 00:01.000\r\nDemo caption\r\n");
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
}
