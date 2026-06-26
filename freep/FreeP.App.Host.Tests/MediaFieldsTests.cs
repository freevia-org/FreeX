using System.IO;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
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

    [Fact]
    public void Field_Hf_RoundTrips()
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
        ms.Position = 0;
        var pres2 = PptxPackageReader.Read(ms);

        var hf = pres2.Slides[0].HfVisibility;
        Assert.NotNull(hf);
        Assert.True(hf!.ShowFooter);
        Assert.False(hf.ShowDate);
        Assert.True(hf.ShowSlideNum);
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

    // ── Helper ────────────────────────────────────────────────────────────────

    private static byte[] CreateMinimal1x1Png() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");
}
