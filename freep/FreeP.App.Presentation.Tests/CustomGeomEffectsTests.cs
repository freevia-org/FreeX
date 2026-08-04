using FreeP.App.Compositor;
using FreeP.Core.Model;
using Free.Shared.Drawing;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Tests for Wave 7A: custom geometry (BuildCustom) and shape effects (round-trip + compositor).
/// </summary>
public sealed class CustomGeomEffectsTests
{
    // ── CustomGeometryBuilder ──────────────────────────────────────────────────

    [Fact]
    public void BuildCustom_EmptyPaths_ReturnsEmpty()
    {
        var geo = CustomGeometryBuilder.BuildCustom([], new LayoutRect(0, 0, 100, 100));
        geo.Contours.Should().BeEmpty();
    }

    [Fact]
    public void BuildCustom_TrianglePath_ProducesThreeLineSegments()
    {
        // Triangle in path-space 200x200: moveTo(100,0) lnTo(200,200) lnTo(0,200) close
        var path = new CustomGeometryPath { PathW = 200, PathH = 200, Fill = true, Stroke = true };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 100, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 200, Y: 200));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 0,   Y: 200));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));

        var bounds = new LayoutRect(0, 0, 100, 100);  // 100×100 DIP render target
        var geo = CustomGeometryBuilder.BuildCustom([path], bounds);

        geo.Contours.Should().HaveCount(1);
        var contour = geo.Contours[0];
        contour.Closed.Should().BeTrue();
        contour.Segments.Should().HaveCount(2, "moveTo → lnTo → lnTo → close = 2 line segments");

        // Start should be at top-center: x=50, y=0 (scaled from 100/200*100, 0/200*100)
        contour.Start.X.Should().BeApproximately(50.0, 0.1);
        contour.Start.Y.Should().BeApproximately(0.0, 0.1);

        // First segment end: x=100, y=100
        contour.Segments[0].End.X.Should().BeApproximately(100.0, 0.1);
        contour.Segments[0].End.Y.Should().BeApproximately(100.0, 0.1);

        // Second segment end: x=0, y=100
        contour.Segments[1].End.X.Should().BeApproximately(0.0, 0.1);
        contour.Segments[1].End.Y.Should().BeApproximately(100.0, 0.1);
    }

    [Fact]
    public void BuildCustom_QuadBez_ElevatedToCubic()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 50));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.QuadBezTo, X: 50, Y: 0, X1: 100, Y1: 50));

        var geo = CustomGeometryBuilder.BuildCustom([path], new LayoutRect(0, 0, 100, 100));

        geo.Contours.Should().HaveCount(1);
        geo.Contours[0].Segments.Should().HaveCount(1);
        geo.Contours[0].Segments[0].Kind.Should().Be(ShapeSegmentKind.CubicBezier,
            "quadratic Bézier should be elevated to cubic");
    }

    [Fact]
    public void BuildCustom_OffsetBounds_ScalesCorrectly()
    {
        // Path space 100×100; bounds at (50,50) 200×200 DIP
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0,   Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 100));

        var bounds = new LayoutRect(50, 50, 200, 200);
        var geo = CustomGeometryBuilder.BuildCustom([path], bounds);

        var contour = geo.Contours[0];
        // Start: (0,0) → (50+0*2, 50+0*2) = (50, 50)
        contour.Start.X.Should().BeApproximately(50.0, 0.1);
        contour.Start.Y.Should().BeApproximately(50.0, 0.1);
        // End: (100,100) → (50+100*2, 50+100*2) = (250, 250)
        contour.Segments[0].End.X.Should().BeApproximately(250.0, 0.1);
        contour.Segments[0].End.Y.Should().BeApproximately(250.0, 0.1);
    }

    // ── ShapeEffects round-trip ───────────────────────────────────────────────

    [Fact]
    public void ShapeEffects_OuterShadow_RoundTrips()
    {
        var effects = new ShapeEffects
        {
            HasOuterShadow       = true,
            OuterShadowColor     = new SrgbColor(0x10, 0x20, 0x30),
            OuterShadowAlpha     = 0x60,
            OuterShadowBlurRadEmu = 63500,
            OuterShadowDistEmu   = 38100,
            OuterShadowDirDeg    = 45.0
        };

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

        // Write and read back
        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var pres2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var shape2 = pres2.Slides[0].Shapes[0];
        shape2.Effects.Should().NotBeNull();
        shape2.Effects!.HasOuterShadow.Should().BeTrue();
        shape2.Effects.OuterShadowDirDeg.Should().BeApproximately(45.0, 0.5);
        shape2.Effects.OuterShadowBlurRadEmu.Should().Be(63500);
        shape2.Effects.OuterShadowDistEmu.Should().Be(38100);
    }

    [Fact]
    public void ShapeEffects_Glow_RoundTrips()
    {
        var effects = new ShapeEffects
        {
            HasGlow      = true,
            GlowColor    = new SrgbColor(0xFF, 0xA5, 0x00),
            GlowAlpha    = 0xA0,
            GlowRadiusEmu = 114300
        };

        var shape = new SlideShape
        {
            Id = 2, Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ellipse,
            OffsetXEmu = 914400, OffsetYEmu = 457200,
            ExtentCxEmu = 1828800, ExtentCyEmu = 1828800,
            Effects = effects
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(shape);

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var pres2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var shape2 = pres2.Slides[0].Shapes[0];
        shape2.Effects.Should().NotBeNull();
        shape2.Effects!.HasGlow.Should().BeTrue();
        shape2.Effects.GlowRadiusEmu.Should().Be(114300);
        shape2.Effects.GlowColor.R.Should().Be(0xFF);
        shape2.Effects.GlowColor.G.Should().Be(0xA5);
    }

    [Fact]
    public void ShapeEffects_SoftEdge_RoundTrips()
    {
        var shape = new SlideShape
        {
            Id = 3, Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1371600,
            Effects = new ShapeEffects
            {
                HasSoftEdge = true,
                SoftEdgeRadEmu = 152400,
            }
        };

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(shape);

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var pres2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var effects = pres2.Slides[0].Shapes[0].Effects;
        effects.Should().NotBeNull();
        effects!.HasSoftEdge.Should().BeTrue();
        effects.SoftEdgeRadEmu.Should().Be(152400);
    }

    // ── Compositor emits Effects ───────────────────────────────────────────────

    [Fact]
    public void Compositor_ShapeWithEffects_EmitsResolvedEffects()
    {
        var effects = new ShapeEffects
        {
            HasOuterShadow       = true,
            OuterShadowColor     = new SrgbColor(0x00, 0x00, 0x00),
            OuterShadowAlpha     = 0x80,
            OuterShadowBlurRadEmu = 63500,
            OuterShadowDistEmu   = 38100,
            OuterShadowDirDeg    = 135.0
        };

        var p = PresentationModel.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1371600,
            Effects = effects
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        shapeOp.Effects.Should().NotBeNull();
        shapeOp.Effects!.HasOuterShadow.Should().BeTrue();
        shapeOp.Effects.OuterShadowDirDeg.Should().BeApproximately(135.0, 0.1);
        shapeOp.Effects.OuterShadowBlurDip.Should().BeApproximately(63500.0 / 9525.0, 0.1);
    }

    [Fact]
    public void Compositor_CustomGeometry_OverridesPreset()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 50, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 0, Y: 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));

        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle, // would be rectangle if no custom geom
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1371600
        };
        shape.CustomGeometry.Add(path);

        var p = PresentationModel.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(shape);

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var shapeOp = ops.OfType<DrawOp.Shape>().Single();

        // Custom triangle has 1 contour with 2 segments; Rectangle would have 1 contour with 3 segments
        shapeOp.Geometry.Contours.Should().HaveCount(1);
        shapeOp.Geometry.Contours[0].Closed.Should().BeTrue();
        shapeOp.Geometry.Contours[0].Segments.Should().HaveCount(2,
            "custom triangle path: lnTo + lnTo (close doesn't add a segment)");
    }

    [Fact]
    public void CustGeom_RoundTrip_PreservesContoursAndSegments()
    {
        var path = new CustomGeometryPath { PathW = 200, PathH = 200, Fill = true, Stroke = true };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 200, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 200, Y: 200));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 0, Y: 200));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));

        var shape = new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1371600
        };
        shape.CustomGeometry.Add(path);

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(shape);

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var pres2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var shape2 = pres2.Slides[0].Shapes[0];
        shape2.CustomGeometry.Should().HaveCount(1);
        var path2 = shape2.CustomGeometry[0];
        path2.PathW.Should().Be(200);
        path2.PathH.Should().Be(200);

        // MoveTo + 3 LineTo + Close = 5 segments
        path2.Segments.Should().HaveCount(5);
        path2.Segments[0].Kind.Should().Be(CustomSegmentKind.MoveTo);
        path2.Segments[0].X.Should().Be(0);
        path2.Segments[0].Y.Should().Be(0);
        path2.Segments[1].X.Should().Be(200);
        path2.Segments[1].Y.Should().Be(0);
        path2.Segments[4].Kind.Should().Be(CustomSegmentKind.Close);
    }

    [Fact]
    public void CustGeom_RoundTrip_PreservesAuthoredConnectionSites()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 100));

        var shape = new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        };
        shape.CustomGeometry.Add(path);
        shape.CustomConnectionSites.Add(new CustomGeometryConnectionSite
        {
            X = "75", Y = "20", Angle = "5400000"
        });
        shape.CustomConnectionSites.Add(new CustomGeometryConnectionSite
        {
            X = "hc", Y = "b"
        });

        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(shape);

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var roundTripped = FreeP.Core.IO.PptxPackageReader.Read(ms).Slides[0].Shapes[0];

        roundTripped.CustomConnectionSites.Should().HaveCount(2);
        roundTripped.CustomConnectionSites[0].X.Should().Be("75");
        roundTripped.CustomConnectionSites[0].Y.Should().Be("20");
        roundTripped.CustomConnectionSites[0].Angle.Should().Be("5400000");
        roundTripped.CustomConnectionSites[1].X.Should().Be("hc");
        roundTripped.CustomConnectionSites[1].Y.Should().Be("b");
    }

    [Fact]
    public void CustGeom_RoundTrip_PreservesCurveControlCoordinates()
    {
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 50));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.CubicBezTo,
            X: 20, Y: 0, X1: 80, Y1: 0, X2: 100, Y2: 50));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.QuadBezTo,
            X: 50, Y: 100, X1: 0, Y1: 50));

        var shape = new SlideShape
        {
            Id = 1, Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400
        };
        shape.CustomGeometry.Add(path);
        var pres = PresentationModel.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(shape);

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var roundTripped = FreeP.Core.IO.PptxPackageReader.Read(ms).Slides[0].Shapes[0];
        var result = roundTripped.CustomGeometry[0].Segments;

        result[1].Kind.Should().Be(CustomSegmentKind.CubicBezTo);
        result[1].X.Should().Be(20);
        result[1].Y.Should().Be(0);
        result[1].X1.Should().Be(80);
        result[1].Y1.Should().Be(0);
        result[1].X2.Should().Be(100);
        result[1].Y2.Should().Be(50);
        result[2].Kind.Should().Be(CustomSegmentKind.QuadBezTo);
        result[2].X.Should().Be(50);
        result[2].Y.Should().Be(100);
        result[2].X1.Should().Be(0);
        result[2].Y1.Should().Be(50);
    }
}
