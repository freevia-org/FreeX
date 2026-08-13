using FreeP.App.Compositor;
using FreeP.Core.Model;
using FreeP.Core.IO;
using Free.Shared.Drawing;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Tests for Wave 8B: Bevel / 3-D shape effects (a:sp3d / a:bevelT / a:scene3d).
/// </summary>
public sealed class Bevel3dTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Model: bevel properties retain their defaults
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BevelInfo_DefaultValues_AreCorrect()
    {
        var b = new BevelInfo();
        b.WidthEmu.Should().Be(76200);
        b.HeightEmu.Should().Be(76200);
        b.PresetName.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Round-trip: sp3d bevelT + bevelB + extrusion + contour colours
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sp3d_BevelTop_RoundTrips()
    {
        var effects = new ShapeEffects
        {
            BevelTop = new BevelInfo
            {
                WidthEmu   = 152400,
                HeightEmu  = 228600,
                PresetName = "relaxedInset"
            }
        };

        var shape2 = RoundTripShape(effects);
        shape2.Effects.Should().NotBeNull();
        shape2.Effects!.BevelTop.Should().NotBeNull();
        shape2.Effects.BevelTop!.WidthEmu.Should().Be(152400);
        shape2.Effects.BevelTop.HeightEmu.Should().Be(228600);
        shape2.Effects.BevelTop.PresetName.Should().Be("relaxedInset");
        shape2.Effects.BevelBottom.Should().BeNull();
    }

    [Fact]
    public void Sp3d_BevelBottom_RoundTrips()
    {
        var effects = new ShapeEffects
        {
            BevelBottom = new BevelInfo
            {
                WidthEmu   = 76200,
                HeightEmu  = 76200,
                PresetName = "cross"
            }
        };

        var shape2 = RoundTripShape(effects);
        shape2.Effects!.BevelBottom.Should().NotBeNull();
        shape2.Effects.BevelBottom!.PresetName.Should().Be("cross");
        shape2.Effects.BevelTop.Should().BeNull();
    }

    [Fact]
    public void Sp3d_ExtrusionAndContour_RoundTrip()
    {
        var effects = new ShapeEffects
        {
            ExtrusionHeightEmu = 457200,
            ContourWidthEmu    = 38100,
            PrstMaterial       = "matte",
            ExtrusionColor     = new SrgbColor(0xFF, 0x00, 0x00),
            ContourColor       = new SrgbColor(0x00, 0xFF, 0x00)
        };

        var shape2 = RoundTripShape(effects);
        shape2.Effects.Should().NotBeNull();
        shape2.Effects!.ExtrusionHeightEmu.Should().Be(457200);
        shape2.Effects.ContourWidthEmu.Should().Be(38100);
        shape2.Effects.PrstMaterial.Should().Be("matte");
        shape2.Effects.ExtrusionColor.Should().NotBeNull();
        shape2.Effects.ExtrusionColor!.Value.R.Should().Be(0xFF);
        shape2.Effects.ContourColor.Should().NotBeNull();
        shape2.Effects.ContourColor!.Value.G.Should().Be(0xFF);
    }

    [Fact]
    public void Sp3d_BevelWithExtrusion_BothRoundTrip()
    {
        var effects = new ShapeEffects
        {
            BevelTop = new BevelInfo { WidthEmu = 228600, HeightEmu = 114300, PresetName = "angle" },
            ExtrusionHeightEmu = 914400,
        };

        var shape2 = RoundTripShape(effects);
        shape2.Effects!.BevelTop!.WidthEmu.Should().Be(228600);
        shape2.Effects.ExtrusionHeightEmu.Should().Be(914400);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Round-trip: scene3d camera + lightRig
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scene3d_CameraAndLightRig_RoundTrip()
    {
        var effects = new ShapeEffects
        {
            Scene3d = new Scene3dInfo
            {
                CameraPreset = "perspectiveRelaxed",
                LightRig     = "threePt",
                LightRigDir  = "t"
            }
        };

        var shape2 = RoundTripShape(effects);
        shape2.Effects.Should().NotBeNull();
        shape2.Effects!.Scene3d.Should().NotBeNull();
        shape2.Effects.Scene3d!.CameraPreset.Should().Be("perspectiveRelaxed");
        shape2.Effects.Scene3d.LightRig.Should().Be("threePt");
        shape2.Effects.Scene3d.LightRigDir.Should().Be("t");
    }

    [Fact]
    public void Scene3d_AndBevel_BothRoundTrip()
    {
        var effects = new ShapeEffects
        {
            BevelTop = new BevelInfo { WidthEmu = 76200, HeightEmu = 76200, PresetName = "circle" },
            Scene3d  = new Scene3dInfo { CameraPreset = "orthographicFront", LightRig = "flat", LightRigDir = "tl" }
        };

        var shape2 = RoundTripShape(effects);
        shape2.Effects!.BevelTop!.PresetName.Should().Be("circle");
        shape2.Effects.Scene3d!.CameraPreset.Should().Be("orthographicFront");
        shape2.Effects.Scene3d.LightRigDir.Should().Be("tl");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Compositor: emits bevel info on the draw op
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compositor_ShapeWithBevelTop_EmitsBevelOnDrawOp()
    {
        var effects = new ShapeEffects
        {
            BevelTop = new BevelInfo { WidthEmu = 152400, HeightEmu = 76200, PresetName = "circle" }
        };

        var shapeOp = ComposeShapeOp(effects);

        shapeOp.Effects.Should().NotBeNull("bevel-only effects must still be emitted");
        shapeOp.Effects!.BevelTop.Should().NotBeNull();
        shapeOp.Effects.BevelTop!.WidthDip.Should().BeApproximately(152400.0 / 9525.0, 0.1);
        shapeOp.Effects.BevelTop.HeightDip.Should().BeApproximately(76200.0  / 9525.0, 0.1);
        shapeOp.Effects.BevelTop.PresetName.Should().Be("circle");
    }

    [Fact]
    public void Compositor_ShapeWithExtrusion_EmitsFaceColorOnDrawOp()
    {
        var shapeOp = ComposeShapeOp(new ShapeEffects
        {
            ExtrusionHeightEmu = 457200,
            ExtrusionColor = new SrgbColor(0xA0, 0x30, 0x70),
        });

        shapeOp.Effects.Should().NotBeNull();
        shapeOp.Effects!.ExtrusionDepthDip.Should().BeApproximately(457200.0 / 9525.0, 0.1);
        shapeOp.Effects.ExtrusionColor.Should().Be(new SrgbColor(0xA0, 0x30, 0x70));
    }

    [Fact]
    public void Compositor_ShapeWithScene3d_EmitsLightDirDeg()
    {
        var effects = new ShapeEffects
        {
            BevelTop = new BevelInfo { WidthEmu = 76200, HeightEmu = 76200 },
            Scene3d  = new Scene3dInfo { CameraPreset = "orthographicFront", LightRig = "flat", LightRigDir = "tl" }
        };

        var shapeOp = ComposeShapeOp(effects);

        // "tl" should resolve to 315 degrees
        shapeOp.Effects!.LightDirDeg.Should().BeApproximately(315.0, 0.1);
    }

    [Fact]
    public void Compositor_Scene3d_SolidFaceColorGetsMaterialLift()
    {
        var shapeOp = ComposeShapeOp(
            new ShapeEffects
            {
                Scene3d = new Scene3dInfo
                {
                    CameraPreset = "orthographicFront",
                    LightRig = "flat",
                    LightRigDir = "t"
                }
            },
            new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x15, 0x60, 0x82), alpha: 128)));

        var fill = shapeOp.Fill.Should().BeOfType<ResolvedFill.Solid>().Subject;
        fill.Color.Should().Be(new SrgbColor(0x19, 0x68, 0x8C));
        fill.Alpha.Should().Be(128);
    }

    [Fact]
    public void Compositor_NoScene3d_LightDirIsMinusOne()
    {
        var effects = new ShapeEffects
        {
            BevelTop = new BevelInfo { WidthEmu = 76200, HeightEmu = 76200 }
        };

        var shapeOp = ComposeShapeOp(effects);

        // No scene3d → sentinel -1
        shapeOp.Effects!.LightDirDeg.Should().BeApproximately(-1.0, 0.1);
    }

    [Fact]
    public void Compositor_ContourColor_PropagatesToDrawOp()
    {
        var effects = new ShapeEffects
        {
            ContourWidthEmu = 76200,
            ContourColor    = new SrgbColor(0xAA, 0xBB, 0xCC)
        };

        var shapeOp = ComposeShapeOp(effects);

        shapeOp.Effects.Should().NotBeNull();
        shapeOp.Effects!.ContourWidthDip.Should().BeApproximately(76200.0 / 9525.0, 0.1);
        shapeOp.Effects.ContourColor.Should().NotBeNull();
        shapeOp.Effects.ContourColor!.Value.R.Should().Be(0xAA);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bevel geometry helper: ComputeBevelRegions
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeBevelRegions_DefaultLightDir_TopLeftHighlight()
    {
        // Default light dir = -1 → 315 degrees (top-left) → top+left highlight, bottom+right shade
        var bevel  = new ResolvedBevel { WidthDip = 8, HeightDip = 6, PresetName = "circle" };
        var bounds = new LayoutRect(10, 20, 100, 80);

        var (highlight, shade) = BevelGeometryHelper.ComputeBevelRegions(bounds, bevel, -1);

        highlight.Top.Should().BeTrue("light at 315° → top edge faces light");
        highlight.Left.Should().BeTrue("light at 315° → left edge faces light");
        shade.Bottom.Should().BeTrue("bottom edge faces away from light");
        shade.Right.Should().BeTrue("right edge faces away from light");
    }

    [Fact]
    public void ComputeBevelRegions_LightFromBelow_BottomHighlight()
    {
        // dir "b" → 90° → light comes from below → bottom highlight
        var bevel  = new ResolvedBevel { WidthDip = 8, HeightDip = 6 };
        var bounds = new LayoutRect(0, 0, 100, 80);

        var (highlight, shade) = BevelGeometryHelper.ComputeBevelRegions(bounds, bevel, 90);

        highlight.Bottom.Should().BeTrue("light from bottom → bottom edge is highlight");
        shade.Top.Should().BeTrue("top edge is in shade");
    }

    [Fact]
    public void ComputeBevelRegions_LightFromRight_RightHighlight()
    {
        var bevel  = new ResolvedBevel { WidthDip = 8, HeightDip = 6 };
        var bounds = new LayoutRect(0, 0, 100, 80);

        var (highlight, shade) = BevelGeometryHelper.ComputeBevelRegions(bounds, bevel, 180);

        highlight.Right.Should().BeTrue("light from right → right edge is highlight");
        shade.Left.Should().BeTrue("left edge is in shade");
    }

    [Fact]
    public void ComputeBevelRegions_BoundsPassedThrough()
    {
        var bevel  = new ResolvedBevel { WidthDip = 5, HeightDip = 5 };
        var bounds = new LayoutRect(50, 60, 200, 150);

        var (highlight, _) = BevelGeometryHelper.ComputeBevelRegions(bounds, bevel, -1);

        highlight.Bounds.X.Should().BeApproximately(50.0, 0.01);
        highlight.Bounds.Y.Should().BeApproximately(60.0, 0.01);
        highlight.Bounds.Width.Should().BeApproximately(200.0, 0.01);
        highlight.Bounds.Height.Should().BeApproximately(150.0, 0.01);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Existing effects still work alongside new 3D data
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OuterShadow_PlusBevel_BothRoundTrip()
    {
        var effects = new ShapeEffects
        {
            HasOuterShadow       = true,
            OuterShadowColor     = new SrgbColor(0x20, 0x20, 0x20),
            OuterShadowAlpha     = 0x80,
            OuterShadowBlurRadEmu = 63500,
            OuterShadowDistEmu   = 38100,
            OuterShadowDirDeg    = 45.0,
            BevelTop             = new BevelInfo { WidthEmu = 152400, HeightEmu = 76200, PresetName = "circle" }
        };

        var shape2 = RoundTripShape(effects);
        shape2.Effects!.HasOuterShadow.Should().BeTrue();
        shape2.Effects.OuterShadowDirDeg.Should().BeApproximately(45.0, 0.5);
        shape2.Effects.BevelTop.Should().NotBeNull();
        shape2.Effects.BevelTop!.PresetName.Should().Be("circle");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static SlideShape RoundTripShape(ShapeEffects effects)
    {
        var shape = new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1371600,
            Effects = effects
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(shape);

        using var ms = new System.IO.MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var pres2 = PptxPackageReader.Read(ms);
        return pres2.Slides[0].Shapes[0];
    }

    private static DrawOp.Shape ComposeShapeOp(ShapeEffects effects, ShapeFill? fill = null)
    {
        var p = PresentationModel.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1371600,
            Fill = fill,
            Effects = effects
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        return ops.OfType<DrawOp.Shape>().Single();
    }
}
