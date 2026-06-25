using System.IO;
using Free.Shared.Drawing;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 1B+1C round-trip unit tests: write a Presentation → .pptx → read back → assert structural equality.
/// </summary>
public sealed class PptxRoundTripTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.PptxTests", Guid.NewGuid().ToString("N"));

    public PptxRoundTripTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. Slide count and size
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SlideCount_Preserved()
    {
        var pres = BuildTestPresentation();
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides.Should().HaveCount(2, "we wrote 2 slides");
    }

    [Fact]
    public void RoundTrip_SlideSize_Preserved()
    {
        var pres = new Presentation { SlideSizeCxEmu = 9144000, SlideSizeCyEmu = 6858000 };
        pres.Slides.Add(new Slide());

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.SlideSizeCxEmu.Should().Be(9144000);
        reloaded.SlideSizeCyEmu.Should().Be(6858000);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. Shape anchor / kind
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Rectangle_AnchorAndKind()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 10,
            Name = "Rect1",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "Rect1");
        s.Kind.Should().Be(SlideShapeKind.AutoShape);
        s.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
        s.OffsetXEmu.Should().Be(914400);
        s.OffsetYEmu.Should().Be(457200);
        s.ExtentCxEmu.Should().Be(2743200);
        s.ExtentCyEmu.Should().Be(1828800);
    }

    [Fact]
    public void RoundTrip_VariousShapeKinds()
    {
        var kinds = new[]
        {
            DrawingShapeKind.Ellipse,
            DrawingShapeKind.Triangle,
            DrawingShapeKind.Diamond,
            DrawingShapeKind.RoundedRectangle,
            DrawingShapeKind.Chevron,
            DrawingShapeKind.Pentagon,
            DrawingShapeKind.Star5
        };

        var pres = new Presentation();
        var slide = new Slide();
        uint id = 1;
        foreach (var k in kinds)
        {
            slide.Shapes.Add(new SlideShape
            {
                Id = id++,
                Name = k.ToString(),
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = k,
                ExtentCxEmu = 914400,
                ExtentCyEmu = 914400
            });
        }
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        foreach (var k in kinds)
        {
            var shape = reloaded.Slides[0].Shapes.FirstOrDefault(s => s.Name == k.ToString());
            shape.Should().NotBeNull($"shape {k} should survive round-trip");
            shape!.AutoShapeKind.Should().Be(k, $"kind {k} should be preserved");
        }
    }

    [Fact]
    public void RoundTrip_Rotation_And_Flip()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "RotShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            RotationDeg = 45.0,
            FlipH = true,
            FlipV = false,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "RotShape");
        s.RotationDeg.Should().BeApproximately(45.0, 0.001);
        s.FlipH.Should().BeTrue();
        s.FlipV.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Fill round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SolidFill_SrgbColor()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "FilledRect",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Solid(new SrgbColor(0x44, 0x72, 0xC4)), // accent1 blue
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "FilledRect");
        s.Fill.Should().BeOfType<ShapeFill.Solid>();
        var solid = (ShapeFill.Solid)s.Fill!;
        solid.Color.Resolved.R.Should().Be(0x44);
        solid.Color.Resolved.G.Should().Be(0x72);
        solid.Color.Resolved.B.Should().Be(0xC4);
    }

    [Fact]
    public void RoundTrip_SolidFill_SchemeColor()
    {
        var pres = new Presentation();
        var slide = new Slide();
        var schemeRef = new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 0.75, LumOff = 0.0 };
        var color = new ThemeAwareColor(SrgbColor.FromRgb(0x305496), schemeRef);

        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "SchemeShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ellipse,
            Fill = new ShapeFill.Solid(color),
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "SchemeShape");
        s.Fill.Should().BeOfType<ShapeFill.Solid>();
        var solid = (ShapeFill.Solid)s.Fill!;
        solid.Color.SchemeColor.Should().NotBeNull("scheme color ref should be preserved");
        solid.Color.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent1);
        solid.Color.SchemeColor.LumMod.Should().BeApproximately(0.75, 0.0001);
    }

    [Fact]
    public void RoundTrip_NoFill()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "NoFillShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = ShapeFill.None.Instance,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "NoFillShape");
        s.Fill.Should().BeOfType<ShapeFill.None>();
    }

    [Fact]
    public void RoundTrip_GradientFill()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "GradShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Fill = new ShapeFill.Gradient(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                angleDegrees: 90.0),
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "GradShape");
        s.Fill.Should().BeOfType<ShapeFill.Gradient>();
        var grad = (ShapeFill.Gradient)s.Fill!;
        grad.StartColor.Resolved.R.Should().Be(0xFF);
        grad.EndColor.Resolved.B.Should().Be(0xFF);
        grad.AngleDegrees.Should().BeApproximately(90.0, 0.1);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. Outline round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Outline_Visible()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "OutlineShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                widthPt: 2.0,
                dash: OutlineDash.Dash),
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "OutlineShape");
        s.Outline.Should().BeOfType<ShapeOutline.Visible>();
        var vis = (ShapeOutline.Visible)s.Outline!;
        vis.WidthPt.Should().BeApproximately(2.0, 0.01);
        vis.Dash.Should().Be(OutlineDash.Dash);
        vis.Color.Resolved.R.Should().Be(0xFF);
    }

    [Fact]
    public void RoundTrip_Outline_None()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "NoOutline",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Outline = ShapeOutline.None.Instance,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "NoOutline");
        s.Outline.Should().BeOfType<ShapeOutline.None>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 5. Text / TextBody round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_TextBody_TwoRuns()
    {
        var pres = new Presentation();
        var slide = new Slide();

        var shape = new SlideShape
        {
            Id = 1, Name = "TextShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 3048000, ExtentCyEmu = 1524000
        };

        var body = new TextBody { Anchor = VerticalAnchor.Middle };
        var para = new Paragraph { Align = TextAlign.Center };
        para.Runs.Add(new Run
        {
            Text = "Hello",
            Bold = true,
            FontSizePt = 24.0,
            FontFamily = "Arial",
            Color = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))
        });
        para.Runs.Add(new Run
        {
            Text = " World",
            Italic = true,
            FontSizePt = 18.0
        });
        body.Paragraphs.Add(para);
        shape.TextBody = body;
        slide.Shapes.Add(shape);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "TextShape");
        s.TextBody.Should().NotBeNull();
        s.TextBody!.Anchor.Should().Be(VerticalAnchor.Middle);
        s.TextBody.Paragraphs.Should().HaveCount(1);

        var p0 = s.TextBody.Paragraphs[0];
        p0.Align.Should().Be(TextAlign.Center);
        p0.Runs.Should().HaveCount(2);

        var r0 = p0.Runs[0];
        r0.Text.Should().Be("Hello");
        r0.Bold.Should().BeTrue();
        r0.FontSizePt.Should().BeApproximately(24.0, 0.01);
        r0.FontFamily.Should().Be("Arial");
        r0.Color.Should().NotBeNull();
        r0.Color!.Resolved.R.Should().Be(0xFF);

        var r1 = p0.Runs[1];
        r1.Text.Should().Be(" World");
        r1.Italic.Should().BeTrue();
        r1.FontSizePt.Should().BeApproximately(18.0, 0.01);
    }

    [Fact]
    public void RoundTrip_PlaceholderShape()
    {
        var pres = new Presentation();
        var slide = new Slide();

        var titleShape = new SlideShape
        {
            Id = 1, Name = "Title 1",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            ExtentCxEmu = 8229600, ExtentCyEmu = 1143000
        };
        titleShape.Text = "My Title";
        slide.Shapes.Add(titleShape);
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.FirstOrDefault(x => x.Placeholder?.Type == PlaceholderType.Title);
        s.Should().NotBeNull("title placeholder should survive round-trip");
        s!.PlainText.Should().Be("My Title");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 6. Picture round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Picture_BytesPreserved()
    {
        // Minimal valid 1×1 PNG
        var pngBytes = CreateMinimalPng();

        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Pic1",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = pngBytes, ContentType = "image/png" },
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.FirstOrDefault(x => x.Kind == SlideShapeKind.Picture);
        s.Should().NotBeNull("picture shape should survive");
        s!.Picture.Should().NotBeNull();
        s.Picture!.Bytes.Should().BeEquivalentTo(pngBytes, "image bytes must be preserved exactly");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 7. Theme round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ThemeColors()
    {
        var pres = new Presentation();
        pres.Theme.Name = "TestTheme";
        pres.Theme.ColorScheme[ThemeColorSlot.Accent1] = new SrgbColor(0x12, 0x34, 0x56);
        pres.Theme.ColorScheme[ThemeColorSlot.Accent6] = new SrgbColor(0xAB, 0xCD, 0xEF);
        pres.Theme.FontScheme.MajorLatinFont = "Trebuchet MS";
        pres.Theme.FontScheme.MinorLatinFont = "Georgia";
        pres.Slides.Add(new Slide());

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Theme.ColorScheme[ThemeColorSlot.Accent1].R.Should().Be(0x12);
        reloaded.Theme.ColorScheme[ThemeColorSlot.Accent1].G.Should().Be(0x34);
        reloaded.Theme.ColorScheme[ThemeColorSlot.Accent1].B.Should().Be(0x56);
        reloaded.Theme.ColorScheme[ThemeColorSlot.Accent6].R.Should().Be(0xAB);
        reloaded.Theme.FontScheme.MajorLatinFont.Should().Be("Trebuchet MS");
        reloaded.Theme.FontScheme.MinorLatinFont.Should().Be("Georgia");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 8. Core properties round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_CoreProperties()
    {
        var pres = new Presentation();
        pres.Properties.Title = "Q3 Review";
        pres.Properties.Author = "Jane Smith";
        pres.Properties.Subject = "Finance";
        pres.Properties.Keywords = "quarterly, revenue";
        pres.Slides.Add(new Slide());

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Properties.Title.Should().Be("Q3 Review");
        reloaded.Properties.Author.Should().Be("Jane Smith");
        reloaded.Properties.Subject.Should().Be("Finance");
        reloaded.Properties.Keywords.Should().Be("quarterly, revenue");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 9. Connector shape
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ConnectorShape()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Conn1",
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = DrawingShapeKind.ElbowConnector,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 1828800, ExtentCyEmu = 914400
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var s = reloaded.Slides[0].Shapes.First(x => x.Name == "Conn1");
        s.Kind.Should().Be(SlideShapeKind.Connector);
        s.AutoShapeKind.Should().Be(DrawingShapeKind.ElbowConnector);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 10. Full composite round-trip (kitchen sink)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_KitchenSink_FullPresentation()
    {
        var pres = BuildTestPresentation();
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides.Should().HaveCount(2);
        reloaded.SlideSizeCxEmu.Should().Be(pres.SlideSizeCxEmu);
        reloaded.SlideSizeCyEmu.Should().Be(pres.SlideSizeCyEmu);

        // Slide 0: title + rect with solid fill
        var slide0 = reloaded.Slides[0];
        slide0.Shapes.Should().NotBeEmpty();
        var rectShape = slide0.Shapes.FirstOrDefault(s => s.Name == "TheRect");
        rectShape.Should().NotBeNull("the rectangle shape should survive");
        rectShape!.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
        rectShape.Fill.Should().BeOfType<ShapeFill.Solid>();

        // Slide 1: textbox
        var slide1 = reloaded.Slides[1];
        var textShape = slide1.Shapes.FirstOrDefault(s => s.Name == "TheText");
        textShape.Should().NotBeNull();
        textShape!.TextBody.Should().NotBeNull();
        textShape.TextBody!.Paragraphs.Should().HaveCount(1);
        textShape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("Bold run");
        textShape.TextBody.Paragraphs[0].Runs[1].Text.Should().Be(" normal run");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 11. .pptx file is a valid zip (PowerPoint-openable)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Written_Pptx_IsValidZip()
    {
        var pres = BuildTestPresentation();
        var path = WriteToPptx(pres);

        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        archive.Entries.Should().Contain(e => e.FullName == "[Content_Types].xml",
            "every valid .pptx must contain [Content_Types].xml");
        archive.Entries.Should().Contain(e => e.FullName == "ppt/presentation.xml",
            "every valid .pptx must contain ppt/presentation.xml");
        archive.Entries.Should().Contain(e => e.FullName == "_rels/.rels");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private static Presentation BuildTestPresentation()
    {
        var pres = new Presentation
        {
            SlideSizeCxEmu = 12192000,
            SlideSizeCyEmu = 6858000
        };
        pres.Properties.Title = "Test Deck";
        pres.Properties.Author = "Test Author";

        // Slide 0: a rectangle with solid blue fill + dashed red outline
        var slide0 = new Slide();
        var rect = new SlideShape
        {
            Id = 2, Name = "TheRect",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1828800,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x44, 0x72, 0xC4))),
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                widthPt: 1.5,
                dash: OutlineDash.Dash)
        };
        slide0.Shapes.Add(rect);
        pres.Slides.Add(slide0);

        // Slide 1: a textbox with two runs
        var slide1 = new Slide();
        var textShape = new SlideShape
        {
            Id = 3, Name = "TheText",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 5486400, ExtentCyEmu = 1828800,
            Fill = ShapeFill.None.Instance
        };
        var body = new TextBody { Anchor = VerticalAnchor.Top };
        var para = new Paragraph { Align = TextAlign.Left };
        para.Runs.Add(new Run { Text = "Bold run", Bold = true, FontSizePt = 20.0 });
        para.Runs.Add(new Run { Text = " normal run", FontSizePt = 16.0 });
        body.Paragraphs.Add(para);
        textShape.TextBody = body;
        slide1.Shapes.Add(textShape);
        pres.Slides.Add(slide1);

        return pres;
    }

    /// <summary>Creates a minimal valid 1×1 white PNG (67 bytes).</summary>
    private static byte[] CreateMinimalPng()
    {
        // PNG signature + IHDR + IDAT (1x1 white pixel) + IEND
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    }
}
