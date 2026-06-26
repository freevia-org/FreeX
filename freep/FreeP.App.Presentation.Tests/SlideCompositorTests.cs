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
    public void Compose_MultiStopGradientFill_AllStopsResolved()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var stops = new[]
        {
            new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))),
            new GradientStop(0.5, new ThemeAwareColor(new SrgbColor(0x00, 0xFF, 0x00))),
            new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF))),
        };
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 900000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Gradient(stops, GradientKind.Linear, angleDegrees: 90.0)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Gradient>();
        var grad = (ResolvedFill.Gradient)shapeOp.Fill;
        grad.Stops.Should().HaveCount(3, "all 3 stops must be resolved");
        grad.Kind.Should().Be(GradientKind.Linear);
        grad.Stops[0].Color.R.Should().Be(0xFF);
        grad.Stops[1].Color.G.Should().Be(0xFF);
        grad.Stops[2].Color.B.Should().Be(0xFF);
        grad.Stops[0].Position.Should().BeApproximately(0.0, 0.001);
        grad.Stops[1].Position.Should().BeApproximately(0.5, 0.001);
        grad.Stops[2].Position.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Compose_RadialGradientFill_KindPreserved()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var stops = new[]
        {
            new GradientStop(0.0, new ThemeAwareColor(new SrgbColor(0xFF, 0xFF, 0xFF))),
            new GradientStop(1.0, new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0x00))),
        };
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 900000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Gradient(stops, GradientKind.Radial, angleDegrees: 0.0)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Gradient>();
        var grad = (ResolvedFill.Gradient)shapeOp.Fill;
        grad.Kind.Should().Be(GradientKind.Radial);
        grad.Stops.Should().HaveCount(2);
    }

    [Fact]
    public void Compose_PictureFill_BytesPassedThrough()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // fake PNG header
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 900000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Picture(imageBytes, "image/png", tile: false)
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.Picture>();
        var pic = (ResolvedFill.Picture)shapeOp.Fill;
        pic.ImageBytes.Should().BeEquivalentTo(imageBytes);
        pic.Tile.Should().BeFalse();
    }

    [Fact]
    public void Compose_PatternFill_ColorsAndPresetResolved()
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 100000, OffsetYEmu = 100000,
            ExtentCxEmu = 900000, ExtentCyEmu = 500000,
            Fill = new ShapeFill.Pattern(
                "cross",
                new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF)),
                new ThemeAwareColor(new SrgbColor(0xFF, 0xFF, 0xFF)))
        };
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, FirstSlide(p));
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Fill.Should().BeOfType<ResolvedFill.PatternFill>();
        var pat = (ResolvedFill.PatternFill)shapeOp.Fill;
        pat.Preset.Should().Be("cross");
        pat.ForegroundColor.B.Should().Be(0xFF);
        pat.BackgroundColor.R.Should().Be(0xFF);
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

    // --- Table compositor tests -------------------------------------------

    private static (PresentationModel pres, Slide slide, SlideShape shape)
        MakeTableShape(TableShape table, long offX = 0, long offY = 0, long cx = 9144000, long cy = 4572000)
    {
        var p = MakePresentation();
        var slide = p.Slides[0];
        var shape = new SlideShape
        {
            Id = 1,
            Name = "Table 1",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = offX,
            OffsetYEmu = offY,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            Table = table
        };
        slide.Shapes.Add(shape);
        return (p, slide, shape);
    }

    [Fact]
    public void ComposeTable_EmptyTable_ProducesTableOp()
    {
        // Arrange: empty table (no rows)
        var table = new TableShape();
        var (p, slide, _) = MakeTableShape(table);

        // Act
        var ops = SlideCompositor.Compose(p, slide);

        // Assert: should get a Background op + a Table op
        ops.Should().ContainSingle(o => o is DrawOp.Table, "a Table draw op should be emitted");
    }

    [Fact]
    public void ComposeTable_CellRects_MatchCumulativeWidthsAndHeights()
    {
        // Arrange: 2-col x 2-row table
        // col0=3048000 EMU (320 DIP), col1=3048000 EMU (320 DIP)
        // row0=914400 EMU (96 DIP), row1=914400 EMU (96 DIP)
        const long colW  = 3048000L;
        const long rowH  = 914400L;
        const long offX  = 914400L;  // 96 DIP
        const long offY  = 914400L;  // 96 DIP

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(colW);
        table.ColumnWidthsEmu.Add(colW);

        for (int r = 0; r < 2; r++)
        {
            var row = new TableRow { HeightEmu = rowH };
            row.Cells.Add(new TableCell());
            row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }

        var (p, slide, _) = MakeTableShape(table, offX, offY, colW * 2, rowH * 2);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var tbl = ops.OfType<DrawOp.Table>().Single();

        // Assert: 4 non-merged cells
        tbl.Cells.Should().HaveCount(4);

        const double emu = 9525.0;
        double x0 = offX / emu;
        double y0 = offY / emu;
        double w  = colW  / emu;
        double h  = rowH  / emu;

        // Row 0, col 0
        tbl.Cells[0].BoundsDip.X.Should().BeApproximately(x0,         0.001);
        tbl.Cells[0].BoundsDip.Y.Should().BeApproximately(y0,         0.001);
        tbl.Cells[0].BoundsDip.Width.Should().BeApproximately(w,       0.001);
        tbl.Cells[0].BoundsDip.Height.Should().BeApproximately(h,      0.001);

        // Row 0, col 1
        tbl.Cells[1].BoundsDip.X.Should().BeApproximately(x0 + w,     0.001);
        tbl.Cells[1].BoundsDip.Y.Should().BeApproximately(y0,         0.001);

        // Row 1, col 0
        tbl.Cells[2].BoundsDip.X.Should().BeApproximately(x0,         0.001);
        tbl.Cells[2].BoundsDip.Y.Should().BeApproximately(y0 + h,     0.001);

        // Row 1, col 1
        tbl.Cells[3].BoundsDip.X.Should().BeApproximately(x0 + w,     0.001);
        tbl.Cells[3].BoundsDip.Y.Should().BeApproximately(y0 + h,     0.001);
    }

    [Fact]
    public void ComposeTable_MergedCells_SkipsCoveredCells()
    {
        // Arrange: 3-col x 1-row; cell 0 has GridSpan=2, cell 1+2 are HMerge.
        const long colW = 3048000L;
        const long rowH = 914400L;

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(colW);
        table.ColumnWidthsEmu.Add(colW);
        table.ColumnWidthsEmu.Add(colW);

        var row = new TableRow { HeightEmu = rowH };
        row.Cells.Add(new TableCell { GridSpan = 2 });
        row.Cells.Add(new TableCell { HMerge = true });
        row.Cells.Add(new TableCell());
        table.Rows.Add(row);

        var (p, slide, _) = MakeTableShape(table, 0, 0, colW * 3, rowH);

        // Act
        var ops = SlideCompositor.Compose(p, slide);
        var tbl = ops.OfType<DrawOp.Table>().Single();

        // Assert: only 2 cells emitted (origin + col2); HMerge cell skipped
        tbl.Cells.Should().HaveCount(2, "HMerge cells should be skipped");

        const double emu = 9525.0;
        // Origin cell should span 2 columns
        tbl.Cells[0].BoundsDip.Width.Should().BeApproximately(colW * 2 / emu, 0.001);
        // The last cell occupies only col 2
        tbl.Cells[1].BoundsDip.X.Should().BeApproximately(colW * 2 / emu, 0.001);
        tbl.Cells[1].BoundsDip.Width.Should().BeApproximately(colW / emu, 0.001);
    }

    [Fact]
    public void TableStyleData_EffectiveFill_FirstRowWins()
    {
        // Arrange: table with FirstRow flag + style that has distinct firstRow vs band fills.
        var wholeFill  = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xAA, 0xAA, 0xAA)));
        var firstFill  = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x1F, 0x4E, 0x79)));
        var band1Fill  = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xDD, 0xEE, 0xFF)));

        var styleData = new TableStyleData
        {
            StyleId  = "{test}",
            WholeTbl = new TableStyleEntry { Fill = wholeFill },
            FirstRow = new TableStyleEntry { Fill = firstFill },
            Band1H   = new TableStyleEntry { Fill = band1Fill }
        };

        var table = new TableShape
        {
            TableStyleId = "{test}",
            StyleData    = styleData
        };
        table.Flags.FirstRow = true;
        table.Flags.BandRow  = true;
        table.ColumnWidthsEmu.Add(914400);
        table.Rows.Add(new TableRow { HeightEmu = 457200 });
        table.Rows[0].Cells.Add(new TableCell());  // row 0, first row
        table.Rows.Add(new TableRow { HeightEmu = 457200 });
        table.Rows[1].Cells.Add(new TableCell());  // row 1, band1

        // Act: effective fill for row 0 (first row) should be firstFill
        var fill0 = table.ComputeEffectiveFill(0, 0, table.Rows[0].Cells[0]);
        fill0.Should().Be(firstFill, "first row flag should pick firstRow style fill over band");

        // row 1 = band 1 (after first row header, row 1 is band index 0 = Band1H)
        var fill1 = table.ComputeEffectiveFill(1, 0, table.Rows[1].Cells[0]);
        fill1.Should().Be(band1Fill, "second row (first data row) should use Band1H fill");
    }
}
