using FreeP.App.Compositor;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Unit tests for <see cref="SlideCompositor"/>, <see cref="PlaceholderResolver"/>,
/// and <see cref="ThemeColorResolver"/>.
/// </summary>
public sealed class SlideCompositorTests
{
    // â”€â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static PresentationModel MakePresentation(Action<PresentationModel>? configure = null)
    {
        var p = PresentationModel.CreateEmpty();
        configure?.Invoke(p);
        return p;
    }

    private static Slide FirstSlide(PresentationModel p) => p.Slides[0];

    // â”€â”€â”€ Basic composition â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_EmptyPresentation_ReturnsBackgroundOp()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var ops = SlideCompositor.Compose(p, FirstSlide(p));

        ops.Should().HaveCount(1);
        ops[0].Should().BeOfType<DrawOp.Background>();
    }

    [Fact]
    public void Compose_SlideWithOneShape_ReturnsBackgroundPlusShapeOp()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));

        ops.Should().HaveCount(2);
        ops[0].Should().BeOfType<DrawOp.Background>();
        ops[1].Should().BeOfType<DrawOp.Shape>();
    }

    [Fact]
    public void Compose_ZOrderPreserved_ShapesInInputOrder()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        for (uint i = 1; i <= 3; i++)
        {
            p.Slides[0].Shapes.Add(new SlideShape
            {
                Id = i,
                Name = $"Shape{i}",
                OffsetXEmu = i * 100000,
                OffsetYEmu = 100000,
                ExtentCxEmu = 900000,
                ExtentCyEmu = 500000
            });
        }

        var ops = SlideCompositor.Compose(p, FirstSlide(p));

        // Background + 3 shapes
        ops.Should().HaveCount(4);
        var shapeOps = ops.OfType<DrawOp.Shape>().ToList();
        shapeOps.Should().HaveCount(3);
        // bounds x should match the order: Shape1 < Shape2 < Shape3
        shapeOps[0].BoundsDip.X.Should().BeLessThan(shapeOps[1].BoundsDip.X);
        shapeOps[1].BoundsDip.X.Should().BeLessThan(shapeOps[2].BoundsDip.X);
    }

    // â”€â”€â”€ EMU â†’ DIP conversion â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_EmuToDip_ConvertsCorrectly()
    {
        // 1 DIP = 9525 EMU (at 96 DPI)
        const long emuX = 914400;   // = 1 inch = 96 DIP
        const long emuCx = 1828800; // = 2 inches = 192 DIP

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            OffsetXEmu = emuX,
            OffsetYEmu = 0,
            ExtentCxEmu = emuCx,
            ExtentCyEmu = 457200  // 0.5 inch = 48 DIP
        });

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.BoundsDip.X.Should().BeApproximately(96.0, 0.1);
        shapeOp.BoundsDip.Width.Should().BeApproximately(192.0, 0.1);
        shapeOp.BoundsDip.Height.Should().BeApproximately(48.0, 0.1);
    }

    [Fact]
    public void Compose_SlideBoundsMatchPresentationSize()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var bg = ops.OfType<DrawOp.Background>().Single();

        // Default 16:9: 12192000 x 6858000 EMU
        bg.BoundsDip.Width.Should().BeApproximately(p.SlideSizeCxEmu / 9525.0, 0.1);
        bg.BoundsDip.Height.Should().BeApproximately(p.SlideSizeCyEmu / 9525.0, 0.1);
    }

    // â”€â”€â”€ Placeholder inheritance â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void PlaceholderResolver_InheritsFromLayout_WhenShapeHasNoExtents()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Add a layout placeholder with known geometry
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide shape with a placeholder tag but NO geometry (ExtentCx = 0)
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Title",
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            // ExtentCxEmu = 0 (default) â†’ should inherit from layout
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.OffsetXEmu.Should().Be(457200);
        anchor.OffsetYEmu.Should().Be(274320);
        anchor.ExtentCxEmu.Should().Be(8229600);
        anchor.ExtentCyEmu.Should().Be(1143000);
    }

    [Fact]
    public void PlaceholderResolver_FallsBackToMaster_WhenLayoutHasNoMatch()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        // Master has a body placeholder
        master.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200,
            OffsetYEmu = 1600200,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 4525963
        });
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout has NO body placeholder
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            // No geometry â†’ inherit from master
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.ExtentCxEmu.Should().Be(8229600);
        anchor.ExtentCyEmu.Should().Be(4525963);
    }

    [Fact]
    public void PlaceholderResolver_UsesShapeGeometry_WhenPresent()
    {
        var p = PresentationModel.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 10,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 300000,
            ExtentCyEmu = 400000
        };

        var anchor = PlaceholderResolver.ResolveAnchor(shape, p.Slides[0], p);

        // Shape has explicit geometry â†’ it wins over layout.
        anchor.OffsetXEmu.Should().Be(100000);
        anchor.ExtentCxEmu.Should().Be(300000);
    }

    [Fact]
    public void Compose_PlaceholderShape_InheritsPositionFromLayout()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
        };
        titleShape.Text = "Test Title";
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Should have inherited the layout placeholder position
        shapeOp.BoundsDip.X.Should().BeApproximately(457200 / 9525.0, 0.1);
        shapeOp.BoundsDip.Width.Should().BeApproximately(8229600 / 9525.0, 0.1);
    }

    // â”€â”€â”€ Theme color resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ThemeColorResolver_ReturnsPreResolved_WhenNoSchemeRef()
    {
        var color = new ThemeAwareColor(new SrgbColor(0xAB, 0xCD, 0xEF));
        var result = ThemeColorResolver.Resolve(color, null);

        result.R.Should().Be(0xAB);
        result.G.Should().Be(0xCD);
        result.B.Should().Be(0xEF);
    }

    [Fact]
    public void ThemeColorResolver_ResolvesAccent1FromTheme()
    {
        var theme = PresentationTheme.CreateDefault();
        var color = new ThemeAwareColor(
            SrgbColor.Black, // pre-resolved fallback â€” should be overridden
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 1.0, LumOff = 0.0 });

        var result = ThemeColorResolver.Resolve(color, theme);

        // Accent1 in default theme = #4472C4
        result.R.Should().Be(0x44);
        result.G.Should().Be(0x72);
        result.B.Should().Be(0xC4);
    }

    [Fact]
    public void ThemeColorResolver_LumMod_DarkensColor()
    {
        var theme = PresentationTheme.CreateDefault();
        // Accent1 = #4472C4; apply lumMod=0.5 (50% darker)
        var color = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 0.5, LumOff = 0.0 });

        var fullColor = ThemeColorResolver.Resolve(
            new ThemeAwareColor(SrgbColor.Black, new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 1.0 }),
            theme);

        var darkenedColor = ThemeColorResolver.Resolve(color, theme);

        // Darkened should be darker: all channels should be less than or equal
        // (luminance halved â†’ each channel should be smaller)
        var fullLuminance = (fullColor.R + fullColor.G + fullColor.B);
        var darkenedLuminance = (darkenedColor.R + darkenedColor.G + darkenedColor.B);
        darkenedLuminance.Should().BeLessThan(fullLuminance,
            "applying lumMod=0.5 should produce a darker color");
    }

    [Fact]
    public void ThemeColorResolver_LumOff_LightensColor()
    {
        var theme = PresentationTheme.CreateDefault();
        var baseColor = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { Slot = ThemeColorSlot.Dk2, LumMod = 1.0, LumOff = 0.0 });

        var lightenedColor = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { Slot = ThemeColorSlot.Dk2, LumMod = 1.0, LumOff = 0.5 });

        var baseResult = ThemeColorResolver.Resolve(baseColor, theme);
        var lightenedResult = ThemeColorResolver.Resolve(lightenedColor, theme);

        var baseLum = baseResult.R + baseResult.G + baseResult.B;
        var lightenedLum = lightenedResult.R + lightenedResult.G + lightenedResult.B;

        lightenedLum.Should().BeGreaterThan(baseLum,
            "applying lumOff=0.5 should produce a lighter color");
    }

    [Fact]
    public void ThemeColorResolver_WhiteRoundTrips()
    {
        var theme = PresentationTheme.CreateDefault();
        var color = new ThemeAwareColor(SrgbColor.White);

        var result = ThemeColorResolver.Resolve(color, theme);
        result.Should().Be(SrgbColor.White);
    }

    [Fact]
    public void ThemeColorResolver_BlackRoundTrips()
    {
        var theme = PresentationTheme.CreateDefault();
        var color = new ThemeAwareColor(SrgbColor.Black);

        var result = ThemeColorResolver.Resolve(color, theme);
        result.Should().Be(SrgbColor.Black);
    }

    // â”€â”€â”€ Background resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_SlideSolidFillBackground_IsResolvedCorrectly()
    {
        var p = MakePresentation();
        p.Slides[0].Background = new ShapeFill.Solid(new SrgbColor(0xFF, 0x00, 0x00));
        p.Slides[0].Shapes.Clear();

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var bg = ops.OfType<DrawOp.Background>().Single();

        bg.Fill.Should().BeOfType<ResolvedFill.Solid>();
        ((ResolvedFill.Solid)bg.Fill).Color.R.Should().Be(0xFF);
        ((ResolvedFill.Solid)bg.Fill).Color.G.Should().Be(0x00);
    }

    [Fact]
    public void Compose_NoBackgroundSet_DefaultsToWhite()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();
        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);
        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);
        var slide = new Slide { LayoutId = "l1" };
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        var bg = ops.OfType<DrawOp.Background>().Single();

        bg.Fill.Should().BeOfType<ResolvedFill.Solid>();
        var solid = (ResolvedFill.Solid)bg.Fill;
        solid.Color.Should().Be(SrgbColor.White);
    }

    // â”€â”€â”€ Text layout resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_ShapeWithText_IncludesResolvedTextLayout()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600
        };
        var tb = new TextBody();
        var para = new Paragraph { Align = TextAlign.Center };
        para.Runs.Add(new Run
        {
            Text = "Hello World",
            FontSizePt = 24.0,
            Bold = true
        });
        tb.Paragraphs.Add(para);
        shape.TextBody = tb;
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Text.Should().NotBeNull();
        shapeOp.Text!.Paragraphs.Should().HaveCount(1);
        shapeOp.Text.Paragraphs[0].Runs.Should().HaveCount(1);
        shapeOp.Text.Paragraphs[0].Runs[0].Text.Should().Be("Hello World");
        shapeOp.Text.Paragraphs[0].Runs[0].FontSizePt.Should().Be(24.0);
        shapeOp.Text.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        shapeOp.Text.Paragraphs[0].Align.Should().Be(TextAlign.Center);
    }

    [Fact]
    public void Compose_TitlePlaceholder_UsesMajorFontDefault()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000
        };
        shape.Text = "My Title";  // Sets a run with no explicit font
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Title should default to major font (Calibri Light)
        var run = shapeOp.Text!.Paragraphs[0].Runs[0];
        run.FontFamily.Should().Be(p.Theme.FontScheme.MajorLatinFont);
    }

    [Fact]
    public void Compose_TitlePlaceholder_UsesLargeFontSizeDefault()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 1143000
        };
        shape.Text = "Title";
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        var run = shapeOp.Text!.Paragraphs[0].Runs[0];
        run.FontSizePt.Should().BeGreaterThan(30.0, "title font default should be larger than body");
    }

    // â”€â”€â”€ Geometry â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_GeometryBoundsMatchConvertedEmuValues()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,  // 1 inch = 96 DIP
            OffsetYEmu = 0,
            ExtentCxEmu = 1828800, // 2 inches = 192 DIP
            ExtentCyEmu = 914400   // 1 inch = 96 DIP
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Geometry should be non-empty
        shapeOp.Geometry.Contours.Should().NotBeEmpty();

        // The bounds should be the converted values
        shapeOp.BoundsDip.X.Should().BeApproximately(96.0, 0.5);
        shapeOp.BoundsDip.Width.Should().BeApproximately(192.0, 0.5);
        shapeOp.BoundsDip.Height.Should().BeApproximately(96.0, 0.5);
    }

    [Fact]
    public void Compose_Picture_EmitsPictureOp()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
        var picShape = new SlideShape
        {
            Id = 5,
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            Picture = new ImagePart { Bytes = imageBytes, ContentType = "image/png" }
        };
        p.Slides[0].Shapes.Add(picShape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var picOp = ops.OfType<DrawOp.Picture>().Single();

        picOp.Bytes.Should().Equal(imageBytes);
        picOp.ContentType.Should().Be("image/png");
        picOp.DestDip.X.Should().BeApproximately(457200 / 9525.0, 0.1);
    }

    // â”€â”€â”€ Fill / outline â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_SolidFillShape_ResolvedColorMatches()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 500000,
            Fill = new ShapeFill.Solid(new SrgbColor(0x12, 0x34, 0x56))
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Solid>();
        var solid = (ResolvedFill.Solid)shapeOp.Fill;
        solid.Color.R.Should().Be(0x12);
        solid.Color.G.Should().Be(0x34);
        solid.Color.B.Should().Be(0x56);
    }

    [Fact]
    public void Compose_GradientFill_ResolvedToConcreteColors()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 500000,
            Fill = new ShapeFill.Gradient(
                new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                45.0)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Gradient>();
        var grad = (ResolvedFill.Gradient)shapeOp.Fill;
        grad.StartColor.R.Should().Be(0xFF);
        grad.EndColor.B.Should().Be(0xFF);
        grad.AngleDegrees.Should().Be(45.0);
    }

    [Fact]
    public void Compose_VisibleOutline_ResolvedCorrectly()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000,
            OffsetYEmu = 100000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 500000,
            Outline = new ShapeOutline.Visible(
                new SrgbColor(0x00, 0x00, 0x00),
                widthPt: 1.5,
                dash: OutlineDash.Dash)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Outline.Should().BeOfType<ResolvedOutline.Visible>();
        var vis = (ResolvedOutline.Visible)shapeOp.Outline;
        vis.Dash.Should().Be(OutlineDash.Dash);
        // 1.5pt â†’ DIP = 1.5 * 96/72 = 2.0 DIP
        vis.WidthDip.Should().BeApproximately(2.0, 0.05);
    }

    // â”€â”€â”€ Rotation / flip â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_RotatedShape_PreservesRotation()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 200000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 500000,
            RotationDeg = 45.0,
            FlipH = true
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.RotationDeg.Should().Be(45.0);
        shapeOp.FlipH.Should().BeTrue();
        shapeOp.FlipV.Should().BeFalse();
    }

    // â”€â”€â”€ Argument validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Compose_NullPresentation_Throws()
    {
        var slide = new Slide();
        var act = () => SlideCompositor.Compose(null!, slide);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compose_NullSlide_Throws()
    {
        var p = MakePresentation();
        var act = () => SlideCompositor.Compose(p, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // â"€â"€â"€ P0: Placeholder text alignment + anchor inheritance (Wave 1G) â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    [Fact]
    public void Compose_CenteredTitle_NoExplicitAlign_InheritsCenter_FromLayoutLstStyle()
    {
        // Arrange: presentation with a layout placeholder that carries lstStyle algn="ctr".
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutTitleBody = new TextBody { DefaultParaAlign = TextAlign.Center, Anchor = VerticalAnchor.Bottom };
        var layoutTitlePara = new Paragraph();
        layoutTitlePara.Runs.Add(new Run { Text = "Click to edit" });
        layoutTitleBody.Paragraphs.Add(layoutTitlePara);

        var layoutTitle = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600,
            TextBody = layoutTitleBody
        };
        layout.Placeholders.Add(layoutTitle);
        p.Layouts.Add(layout);

        // Slide shape has no explicit geometry, no explicit align.
        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            // No xfrm — inherits from layout.
        };
        var titleBody = new TextBody();  // Anchor = null, DefaultParaAlign = null
        var titlePara = new Paragraph();  // Align = null — should inherit ctr from layout
        titlePara.Runs.Add(new Run { Text = "My Title" });
        titleBody.Paragraphs.Add(titlePara);
        titleShape.TextBody = titleBody;
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: paragraph should be centered (inherited from layout lstStyle).
        shapeOp.Text.Should().NotBeNull();
        shapeOp.Text!.Paragraphs[0].Align.Should().Be(TextAlign.Center,
            "ctrTitle placeholder inherits algn=ctr from layout lstStyle");
    }

    [Fact]
    public void Compose_CenteredTitle_NoLayoutLstStyle_DefaultsToCenter()
    {
        // Arrange: a CenteredTitle placeholder with no layout lstStyle — should still default to Center.
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutTitle = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600
            // No TextBody — so DefaultParaAlign is not set
        };
        layout.Placeholders.Add(layoutTitle);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
        };
        var titleBody = new TextBody();
        var titlePara = new Paragraph();
        titlePara.Runs.Add(new Run { Text = "Title" });
        titleBody.Paragraphs.Add(titlePara);
        titleShape.TextBody = titleBody;
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: ctrTitle placeholder type triggers center default even without explicit lstStyle.
        shapeOp.Text!.Paragraphs[0].Align.Should().Be(TextAlign.Center,
            "ctrTitle placeholder type defaults to Center alignment");
    }

    [Fact]
    public void Compose_CenteredTitle_ExplicitAlignWins_OverInherited()
    {
        // Arrange: layout says ctr, but slide paragraph has explicit Left — explicit wins.
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutBody = new TextBody { DefaultParaAlign = TextAlign.Center };
        var layoutPara = new Paragraph();
        layoutPara.Runs.Add(new Run { Text = "placeholder" });
        layoutBody.Paragraphs.Add(layoutPara);
        var layoutTitle = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600,
            TextBody = layoutBody
        };
        layout.Placeholders.Add(layoutTitle);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
        };
        var titleBody = new TextBody();
        var titlePara = new Paragraph { Align = TextAlign.Left }; // Explicit Left
        titlePara.Runs.Add(new Run { Text = "Title" });
        titleBody.Paragraphs.Add(titlePara);
        titleShape.TextBody = titleBody;
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: explicit Left wins over inherited Center.
        shapeOp.Text!.Paragraphs[0].Align.Should().Be(TextAlign.Left,
            "explicit paragraph alignment overrides inherited default");
    }

    [Fact]
    public void Compose_CenteredTitle_VerticalAnchor_InheritedFromLayout()
    {
        // Arrange: layout placeholder has anchor=Bottom in its bodyPr.
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        var layoutBody = new TextBody { Anchor = VerticalAnchor.Bottom };
        var layoutPara = new Paragraph();
        layoutPara.Runs.Add(new Run { Text = "ph" });
        layoutBody.Paragraphs.Add(layoutPara);
        var layoutTitle = new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            OffsetXEmu = 1524000, OffsetYEmu = 1122363,
            ExtentCxEmu = 9144000, ExtentCyEmu = 2387600,
            TextBody = layoutBody
        };
        layout.Placeholders.Add(layoutTitle);
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        var titleShape = new SlideShape
        {
            Id = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
        };
        var titleBody = new TextBody(); // Anchor = null → inherit
        var titlePara = new Paragraph();
        titlePara.Runs.Add(new Run { Text = "Title" });
        titleBody.Paragraphs.Add(titlePara);
        titleShape.TextBody = titleBody;
        slide.Shapes.Add(titleShape);
        p.Slides.Add(slide);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Assert: vertical anchor comes from the layout placeholder.
        shapeOp.Text!.Anchor.Should().Be(VerticalAnchor.Bottom,
            "vertical anchor is inherited from layout placeholder bodyPr");
    }
}


