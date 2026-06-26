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

    // â"€â"€â"€ MM2: placeholder type-compatibility matching â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    /// <summary>
    /// PowerPoint "Title and Content" layouts declare their content placeholder as
    /// ph type="obj" idx="1", while the slide’s placeholder has no explicit type
    /// and therefore defaults to Body.  Body and Object are in the same content group
    /// so the match must succeed and the layout geometry must be inherited.
    /// </summary>
    [Fact]
    public void PlaceholderResolver_BodySlide_MatchesObjectLayout_InheritsGeometry()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout declares the content placeholder as type=Object idx=1
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Object, Idx = 1 },
            OffsetXEmu  = 457200,
            OffsetYEmu  = 1371600,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 4525963
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide shape has no explicit type â†’ defaults to Body; idx=1
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            // No geometry â†’ must inherit from layout
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.OffsetXEmu.Should().Be(457200,   "should inherit layout X");
        anchor.OffsetYEmu.Should().Be(1371600,  "should inherit layout Y");
        anchor.ExtentCxEmu.Should().Be(8229600, "should inherit layout width");
        anchor.ExtentCyEmu.Should().Be(4525963, "should inherit layout height");
    }

    /// <summary>
    /// A slide ctrTitle placeholder must match a layout title placeholder (and vice-versa);
    /// they are interchangeable within the Title group.
    /// </summary>
    [Fact]
    public void PlaceholderResolver_CtrTitle_MatchesLayoutTitle_InheritsGeometry()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout declares a plain Title placeholder (idx 0)
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu  = 1524000,
            OffsetYEmu  = 1122363,
            ExtentCxEmu = 9144000,
            ExtentCyEmu = 2387600
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide uses CenteredTitle (idx 0) â€" must match the layout Title
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            // No geometry â†’ must inherit from layout
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.ExtentCxEmu.Should().Be(9144000, "ctrTitle slide ph must inherit geometry from layout title ph");
        anchor.ExtentCyEmu.Should().Be(2387600);
    }

    /// <summary>
    /// Negative test: a Body placeholder at idx=1 must NOT match a Footer placeholder at idx=1.
    /// Body and Footer are in different groups; idx alone is not enough.
    /// </summary>
    [Fact]
    public void PlaceholderResolver_Body_DoesNotMatch_Footer_SameIdx()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        // Layout only has a Footer placeholder at idx=1 (no body placeholder)
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Footer, Idx = 1 },
            OffsetXEmu  = 0,
            OffsetYEmu  = 6400000,
            ExtentCxEmu = 9999999,
            ExtentCyEmu = 457200
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide has a Body placeholder at idx=1 â€" must NOT match the layout Footer
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            // No geometry â†’ should fall through to zero (no match found)
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        // No match â†’ falls back to the shape’s own (zero) extents
        anchor.ExtentCxEmu.Should().Be(0, "Body ph must not match Footer ph at the same idx");
        anchor.ExtentCyEmu.Should().Be(0);
    }

    /// <summary>
    /// Exact type+idx match must still work (regression guard for existing behavior).
    /// </summary>
    [Fact]
    public void PlaceholderResolver_ExactTypeAndIdx_StillMatches()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.SubTitle, Idx = 2 },
            OffsetXEmu  = 914400,
            OffsetYEmu  = 4000000,
            ExtentCxEmu = 7315200,
            ExtentCyEmu = 1143000
        });
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.SubTitle, Idx = 2 },
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.ExtentCxEmu.Should().Be(7315200, "exact type+idx match must still resolve geometry");
    }

    /// <summary>
    /// Master fallback via the same matching: an Object(idx=1) on the slide should fall through
    /// to the master when the layout has no content placeholder, matching a master Body(idx=1)
    /// via content-group compatibility.
    /// </summary>
    [Fact]
    public void PlaceholderResolver_ObjectSlide_FallsThrough_ToMasterBodyPlaceholder()
    {
        var p = new PresentationModel();

        var master = new SlideMaster { Id = "m1" };
        // Master has Body idx=1
        master.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu  = 457200,
            OffsetYEmu  = 1600200,
            ExtentCxEmu = 8229600,
            ExtentCyEmu = 4525963
        });
        p.Masters.Add(master);

        // Layout has no content placeholder at all
        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "l1" };
        // Slide has Object idx=1 â†’ should match master’s Body idx=1 via content-group compat
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Placeholder = new Placeholder { Type = PlaceholderType.Object, Idx = 1 },
        });
        p.Slides.Add(slide);

        var anchor = PlaceholderResolver.ResolveAnchor(slide.Shapes[0], slide, p);

        anchor.ExtentCxEmu.Should().Be(8229600, "Object ph must fall through to master Body ph via content-group compat");
        anchor.ExtentCyEmu.Should().Be(4525963);
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

    // ─── MM1: clrMap indirection (ECMA-376 §19.3.1.20) ───────────────────────────────────────────

    /// <summary>
    /// A master with an inverted clrMap (tx1→lt1, bg1→dk1) must cause schemeClr val="tx1"
    /// to resolve to the theme's Lt1 color, NOT the default Dk1.
    /// </summary>
    [Fact]
    public void ThemeColorResolver_InvertedClrMap_Tx1ResolvesToLt1()
    {
        var theme = PresentationTheme.CreateDefault(); // Lt1=#FFFFFF, Dk1=#000000

        var color = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "tx1", Slot = ThemeColorSlot.Dk1, LumMod = 1.0 });

        // Without a clrMap: tx1 default → Dk1 → #000000
        var withoutMap = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: null);
        withoutMap.Should().Be(new SrgbColor(0, 0, 0),
            "without clrMap, tx1 maps to Dk1 = black");

        // Inverted master clrMap: tx1 → lt1 → Lt1 → #FFFFFF
        var invertedClrMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "dk1", ["tx1"] = "lt1", ["bg2"] = "dk2", ["tx2"] = "lt2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };

        var withMap = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: invertedClrMap);
        withMap.Should().Be(new SrgbColor(0xFF, 0xFF, 0xFF),
            "inverted clrMap (tx1→lt1) must resolve tx1 to Lt1 = white");
    }

    /// <summary>
    /// The default (no clrMap) mapping: bg1→Lt1. Regression guard.
    /// </summary>
    [Fact]
    public void ThemeColorResolver_DefaultClrMap_Bg1ResolvesToLt1()
    {
        var theme = PresentationTheme.CreateDefault(); // Lt1=#FFFFFF

        var color = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "bg1", Slot = ThemeColorSlot.Lt1, LumMod = 1.0 });

        var result = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: null);
        result.Should().Be(new SrgbColor(0xFF, 0xFF, 0xFF),
            "default map: bg1 → lt1 slot = #FFFFFF");
    }

    /// <summary>
    /// accent1 resolves identically with and without a clrMap (maps identity).
    /// </summary>
    [Fact]
    public void ThemeColorResolver_Accent1_ResolvesCorrectly_ThroughClrMap()
    {
        var theme = PresentationTheme.CreateDefault(); // Accent1=#4472C4

        var color = new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "accent1", Slot = ThemeColorSlot.Accent1, LumMod = 1.0 });

        var withoutMap = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: null);
        withoutMap.R.Should().Be(0x44);
        withoutMap.G.Should().Be(0x72);
        withoutMap.B.Should().Be(0xC4);

        var clrMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "dk1", ["tx1"] = "lt1", ["bg2"] = "dk2", ["tx2"] = "lt2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };

        var withMap = ThemeColorResolver.Resolve(color, theme, effectiveClrMap: clrMap);
        withMap.R.Should().Be(0x44, "accent1 must resolve the same with any clrMap that keeps accent1→accent1");
        withMap.G.Should().Be(0x72);
        withMap.B.Should().Be(0xC4);
    }

    /// <summary>
    /// MM5: A slide with ColorMapOverride (inverted) must override the master map during Compose().
    /// Validates that SlideCompositor.Compose threads the override correctly.
    /// </summary>
    [Fact]
    public void Compose_SlideColorMapOverride_OverridesMasterClrMapForShapeFill()
    {
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault(); // Lt1=#FFFFFF, Dk1=#000000

        var master = new SlideMaster { Id = "m1" };
        master.ColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        p.Layouts.Add(layout);

        // Shape fill references tx1 (parsed from XML with RoleName set).
        var shapeFill = new ShapeFill.Solid(new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "tx1", Slot = ThemeColorSlot.Dk1, LumMod = 1.0 }));

        // Slide without override: tx1 → master map (dk1) → Dk1 → black
        var slidePlain = new Slide { LayoutId = "l1" };
        slidePlain.Shapes.Add(new SlideShape
        {
            Id = 1, OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            Fill = shapeFill
        });
        p.Slides.Add(slidePlain);

        var opsPlain = SlideCompositor.Compose(p, slidePlain);
        ((ResolvedFill.Solid)opsPlain.OfType<DrawOp.Shape>().Single().Fill).Color
            .Should().Be(new SrgbColor(0, 0, 0),
            "plain slide: tx1 via master map → Dk1 = black");

        // Slide with inverted ColorMapOverride: tx1 → lt1 → Lt1 → white
        var slideOvr = new Slide { LayoutId = "l1" };
        slideOvr.ColorMapOverride = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "dk1", ["tx1"] = "lt1", ["bg2"] = "dk2", ["tx2"] = "lt2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };
        slideOvr.Shapes.Add(new SlideShape
        {
            Id = 1, OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            Fill = shapeFill
        });
        p.Slides.Add(slideOvr);

        var opsOvr = SlideCompositor.Compose(p, slideOvr);
        ((ResolvedFill.Solid)opsOvr.OfType<DrawOp.Shape>().Single().Fill).Color
            .Should().Be(new SrgbColor(0xFF, 0xFF, 0xFF),
            "override slide: tx1 via ColorMapOverride (inverted: tx1→lt1) → Lt1 = white");
    }

    //â”€â”€â”€ Background resolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // ─── II3: slidenum field always shows correct slide number ────────────────

    /// <summary>
    /// A 3-slide deck with a slidenum field on each slide: compositing slide at
    /// index 2 (0-based) must render "3", not "1".
    /// Regression guard for the EnsureOps bug that left slideIndex=0 (default).
    /// </summary>
    [Fact]
    public void Compose_SlidenuFieldOnThirdSlide_RendersThree()
    {
        // Arrange: 3-slide presentation; each slide has a slidenum field.
        var p = new PresentationModel();
        p.Theme = PresentationTheme.CreateDefault();

        for (int i = 0; i < 3; i++)
        {
            var slide = new Slide();
            var para  = new Paragraph();
            para.Runs.Add(new Run
            {
                Text  = (i + 1).ToString(),
                Field = new FieldRun { FieldType = "slidenum", CachedText = (i + 1).ToString() }
            });
            var body = new TextBody();
            body.Paragraphs.Add(para);
            slide.Shapes.Add(new SlideShape
            {
                Id          = (uint)(i + 1),
                Kind        = SlideShapeKind.AutoShape,
                OffsetXEmu  = 914400,
                OffsetYEmu  = 6400000,
                ExtentCxEmu = 4572000,
                ExtentCyEmu = 457200,
                TextBody    = body
            });
            p.Slides.Add(slide);
        }

        // Act: compose slide at 0-based index 2 (third slide).
        var thirdSlide = p.Slides[2];
        var ops        = SlideCompositor.Compose(p, thirdSlide, slideIndex: 2);
        var shapeOp    = ops.OfType<DrawOp.Shape>().Single();

        // Assert: rendered text must be "3", not "1".
        var runText = string.Concat(shapeOp.Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));
        runText.Should().Be("3",
            "slidenum field on slide index 2 must render slide number 3");
    }

    [Fact]
    public void Compose_SlidenuField_IndexZeroGivesOne_IndexTwoGivesThree()
    {
        // Confirm that the slideIndex parameter is the only difference between
        // "shows 1" (old bug: always index 0) and "shows 3" (correct: index 2).
        var p     = new PresentationModel();
        p.Theme   = PresentationTheme.CreateDefault();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "?",
            Field = new FieldRun { FieldType = "slidenum", CachedText = "" }
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            TextBody = body
        });
        p.Slides.Add(slide);

        string TextAt(int idx) =>
            string.Concat(SlideCompositor.Compose(p, slide, idx)
                .OfType<DrawOp.Shape>().Single()
                .Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));

        TextAt(0).Should().Be("1", "index 0 → slide 1");
        TextAt(2).Should().Be("3", "index 2 → slide 3");
    }

    // ─── II6: empty-cache field fallback ─────────────────────────────────────

    [Fact]
    public void ResolveField_DatetimeEmptyCache_RendersDateString_NotTypeToken()
    {
        // A datetime field with no cached text must NOT render the literal token
        // "datetime1" — it should render something date-like.
        var p     = new PresentationModel();
        p.Theme   = PresentationTheme.CreateDefault();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "",
            Field = new FieldRun { FieldType = "datetime1", CachedText = "" }
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            TextBody = body
        });
        p.Slides.Add(slide);

        var ops    = SlideCompositor.Compose(p, slide, slideIndex: 0);
        var runText = string.Concat(ops.OfType<DrawOp.Shape>().Single()
            .Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));

        runText.Should().NotBe("datetime1",
            "empty-cache datetime field must not render the raw field-type token");
        runText.Should().MatchRegex(@"\d",
            "empty-cache datetime field should contain at least one digit (a date)");
    }

    [Fact]
    public void ResolveField_FooterEmptyCache_RendersEmpty_NotTypeToken()
    {
        var p     = new PresentationModel();
        p.Theme   = PresentationTheme.CreateDefault();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "",
            Field = new FieldRun { FieldType = "footer", CachedText = "" }
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            TextBody = body
        });
        p.Slides.Add(slide);

        var ops     = SlideCompositor.Compose(p, slide, slideIndex: 0);
        var runText = string.Concat(ops.OfType<DrawOp.Shape>().Single()
            .Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));

        runText.Should().BeEmpty(
            "empty-cache footer field must render empty, not the raw type token 'footer'");
    }

    [Fact]
    public void ResolveField_WithCachedText_AlwaysUsesCacheOverFallback()
    {
        // A field that HAS cached text must always render the cache, regardless of type.
        var p     = new PresentationModel();
        p.Theme   = PresentationTheme.CreateDefault();
        var slide = new Slide();

        var para = new Paragraph();
        para.Runs.Add(new Run
        {
            Text  = "Custom Footer",
            Field = new FieldRun { FieldType = "footer", CachedText = "Custom Footer" }
        });
        var body = new TextBody();
        body.Paragraphs.Add(para);
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            TextBody = body
        });
        p.Slides.Add(slide);

        var ops     = SlideCompositor.Compose(p, slide, slideIndex: 0);
        var runText = string.Concat(ops.OfType<DrawOp.Shape>().Single()
            .Text!.Paragraphs.SelectMany(par => par.Runs.Select(r => r.Text)));

        runText.Should().Be("Custom Footer",
            "fields with non-empty cached text must always render the cache");
    }
}
