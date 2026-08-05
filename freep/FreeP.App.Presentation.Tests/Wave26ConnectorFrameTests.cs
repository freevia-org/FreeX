using System.IO;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Wave 26 — per-shape connection sites, elbow auto-routing, and picture frame presets.
/// </summary>
public sealed class Wave26ConnectorFrameTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static SlideShape MakeShape(uint id, DrawingShapeKind kind, long x, long y, long cx, long cy) => new()
    {
        Id            = id,
        Name          = $"Shape{id}",
        Kind          = SlideShapeKind.AutoShape,
        AutoShapeKind = kind,
        OffsetXEmu    = x,
        OffsetYEmu    = y,
        ExtentCxEmu   = cx,
        ExtentCyEmu   = cy,
    };

    private static SlideShape MakeConnector(uint id,
        DrawingShapeKind connKind = DrawingShapeKind.ElbowConnector,
        ConnectorAttachment? start = null,
        ConnectorAttachment? end   = null) => new()
    {
        Id              = id,
        Name            = $"Connector{id}",
        Kind            = SlideShapeKind.Connector,
        AutoShapeKind   = connKind,
        OffsetXEmu      = 0,
        OffsetYEmu      = 0,
        ExtentCxEmu     = 100,
        ExtentCyEmu     = 100,
        ConnectionStart = start,
        ConnectionEnd   = end,
    };

    private static byte[] Minimal1x1Png() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    private static bool SegmentCrossesInterior(
        (long X, long Y) first,
        (long X, long Y) second,
        (long L, long T, long R, long B) obstacle) =>
        first.Y == second.Y
            ? first.Y > obstacle.T && first.Y < obstacle.B
                && Math.Max(first.X, second.X) > obstacle.L
                && Math.Min(first.X, second.X) < obstacle.R
            : first.X == second.X
                && first.X > obstacle.L && first.X < obstacle.R
                && Math.Max(first.Y, second.Y) > obstacle.T
                && Math.Min(first.Y, second.Y) < obstacle.B;

    // ════════════════════════════════════════════════════════════════════════════
    // Part 1 — Per-shape connection sites
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Diamond_SiteIndices_ReturnFourVertices()
    {
        // Diamond at (0, 0, 400, 200) — vertices: left=(0,100), top=(200,0), right=(400,100), bottom=(200,200)
        var shape = MakeShape(1, DrawingShapeKind.Diamond, 0, 0, 400, 200);

        var (lx, ly) = ConnectionSiteHelper.Resolve(shape, 0); // left vertex
        lx.Should().Be(0);
        ly.Should().Be(100);

        var (tx, ty) = ConnectionSiteHelper.Resolve(shape, 1); // top vertex
        tx.Should().Be(200);
        ty.Should().Be(0);

        var (rx, ry) = ConnectionSiteHelper.Resolve(shape, 2); // right vertex
        rx.Should().Be(400);
        ry.Should().Be(100);

        var (bx, by) = ConnectionSiteHelper.Resolve(shape, 3); // bottom vertex
        bx.Should().Be(200);
        by.Should().Be(200);
    }

    [Fact]
    public void Ellipse_Sites_ReturnCardinalPoints()
    {
        // Ellipse at (100, 50, 400, 200) — N/E/S/W = mid-edges of bbox
        var shape = MakeShape(1, DrawingShapeKind.Ellipse, 100, 50, 400, 200);

        var (wx, wy) = ConnectionSiteHelper.Resolve(shape, 0); // West
        wx.Should().Be(100);
        wy.Should().Be(150); // 50 + 100

        var (nx, ny) = ConnectionSiteHelper.Resolve(shape, 1); // North
        nx.Should().Be(300); // 100 + 200
        ny.Should().Be(50);

        var (ex, ey) = ConnectionSiteHelper.Resolve(shape, 2); // East
        ex.Should().Be(500); // 100 + 400
        ey.Should().Be(150);

        var (sx, sy) = ConnectionSiteHelper.Resolve(shape, 3); // South
        sx.Should().Be(300);
        sy.Should().Be(250); // 50 + 200
    }

    [Fact]
    public void Triangle_Site1_ReturnsApex()
    {
        // Triangle at (0, 0, 600, 400) — apex at site 1 = top-mid = (300, 0)
        var shape = MakeShape(1, DrawingShapeKind.Triangle, 0, 0, 600, 400);
        var (x, y) = ConnectionSiteHelper.Resolve(shape, 1);
        x.Should().Be(300); // midX
        y.Should().Be(0);   // top
    }

    [Fact]
    public void Triangle_Site4_ReturnsBaseLeft()
    {
        // Triangle base-left corner = (0, bottom) = site index 4
        var shape = MakeShape(1, DrawingShapeKind.Triangle, 0, 0, 600, 400);
        var (x, y) = ConnectionSiteHelper.Resolve(shape, 4);
        x.Should().Be(0);
        y.Should().Be(400);
    }

    [Fact]
    public void Rectangle_Sites_UsesBboxUnchanged()
    {
        // Rectangle — bbox 8-site behaviour preserved
        var shape = MakeShape(1, DrawingShapeKind.Rectangle, 200, 100, 600, 400);
        var (x0, y0) = ConnectionSiteHelper.Resolve(shape, 0);
        x0.Should().Be(200);  // left-mid
        y0.Should().Be(300);  // 100 + 200

        var (x4, y4) = ConnectionSiteHelper.Resolve(shape, 4);
        x4.Should().Be(200);  // top-left corner
        y4.Should().Be(100);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Part 2 — Elbow auto-routing
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ElbowRouter_HorizontalShapes_ProducesZRoute()
    {
        // Two shapes side by side separated by a gap
        // ShapeA: (0,0,200,100) — right-mid site = (200,50)
        // ShapeB: (400,0,200,100) — left-mid site = (400,50)
        // Route should be: start=(200,50), mid=(300,50), mid=(300,50), end=(400,50)
        // but since S.Y == E.Y, it's a straight horizontal shot.
        var startPt = (X: 200L, Y: 50L);
        var endPt   = (X: 400L, Y: 50L);
        var rectA   = (L: 0L, T: 0L, R: 200L, B: 100L);
        var rectB   = (L: 400L, T: 0L, R: 600L, B: 100L);

        var route = ElbowRouter.Route(startPt, endPt, rectA, rectB);

        route.Should().HaveCountGreaterThan(1);
        route.First().Should().Be(startPt);
        route.Last().Should().Be(endPt);

        // All segments must be horizontal or vertical (Manhattan)
        for (int i = 1; i < route.Count; i++)
        {
            bool horizontal = route[i].Y == route[i - 1].Y;
            bool vertical   = route[i].X == route[i - 1].X;
            (horizontal || vertical).Should().BeTrue($"segment {i - 1}→{i} must be orthogonal");
        }
    }

    [Fact]
    public void ElbowRouter_DiagonalShapes_ProducesLRoute()
    {
        // Start site on right edge of shape A, end site on top of shape B (diagonal)
        // Exit: right → horizontal; Entry: top → vertical → should produce an L-route
        var startPt = (X: 200L, Y: 50L);  // right-mid of shapeA
        var endPt   = (X: 350L, Y: 0L);   // top-mid of shapeB
        var rectA   = (L: 0L, T: 0L, R: 200L, B: 100L);
        var rectB   = (L: 300L, T: 0L, R: 400L, B: 100L);

        var route = ElbowRouter.Route(startPt, endPt, rectA, rectB);

        route.Should().HaveCountGreaterThan(1);
        route.First().Should().Be(startPt);
        route.Last().Should().Be(endPt);

        // All segments orthogonal
        for (int i = 1; i < route.Count; i++)
        {
            bool h = route[i].Y == route[i - 1].Y;
            bool v = route[i].X == route[i - 1].X;
            (h || v).Should().BeTrue($"segment {i - 1}→{i} must be orthogonal");
        }

        // L-route has exactly 3 points (start, bend, end)
        route.Should().HaveCount(3, "a diagonal exit→vertical entry should produce one bend point");
    }

    [Fact]
    public void ElbowRouter_DetoursAroundInterveningObstacle()
    {
        var start = (X: 200L, Y: 50L);
        var end = (X: 600L, Y: 50L);
        var obstacle = (L: 350L, T: 0L, R: 450L, B: 100L);

        var route = ElbowRouter.Route(
            start,
            end,
            (L: 0L, T: 0L, R: 200L, B: 100L),
            (L: 600L, T: 0L, R: 800L, B: 100L),
            [obstacle]);

        route.First().Should().Be(start);
        route.Last().Should().Be(end);
        route.Should().Contain(point => point.Y < obstacle.T || point.Y > obstacle.B,
            "the route must leave the obstacle's vertical band");
        route.Should().OnlyContain(point => point.X >= 200 && point.X <= 600);
        for (var index = 1; index < route.Count; index++)
            SegmentCrossesInterior(route[index - 1], route[index], obstacle).Should().BeFalse();
    }

    [Fact]
    public void ElbowRouter_SamePoint_ReturnsTwoPoints()
    {
        var pt = (X: 100L, Y: 200L);
        var route = ElbowRouter.Route(pt, pt, null, null);
        route.Should().HaveCount(2);
        route[0].Should().Be(pt);
        route[1].Should().Be(pt);
    }

    [Fact]
    public void ConnectorRouter_ElbowConnector_StoresRoute_OnMove()
    {
        // Setup: two shapes + an elbow connector
        var pres = new Presentation();
        var slide = new Slide { Id = "s1" };
        pres.Slides.Add(slide);

        var shapeA = new SlideShape
        {
            Id = 1, Name = "A", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 200, ExtentCyEmu = 100,
        };
        var shapeB = new SlideShape
        {
            Id = 2, Name = "B", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 500, OffsetYEmu = 0, ExtentCxEmu = 200, ExtentCyEmu = 100,
        };
        var connector = MakeConnector(3,
            start: new ConnectorAttachment { ShapeId = 1, SiteIndex = 2 },  // right-mid of A
            end:   new ConnectorAttachment { ShapeId = 2, SiteIndex = 0 }); // left-mid of B

        slide.Shapes.Add(shapeA);
        slide.Shapes.Add(shapeB);
        slide.Shapes.Add(connector);

        var bus = new PresentationCommandBus(pres);
        bus.Execute(new MoveShapeCommand(0, 1, 50, 20)); // move shapeA

        // After move, connector should have an elbow route
        var c = slide.Shapes.First(s => s.Id == 3);
        c.ElbowRoute.Should().NotBeNull("elbow route must be computed on move");
        c.ElbowRoute!.Count.Should().BeGreaterThanOrEqualTo(2, "route must have at least 2 waypoints");

        // Route starts at start site and ends at end site
        var startSite = ConnectionSiteHelper.Resolve(new ConnectorAttachment { ShapeId = 1, SiteIndex = 2 }, slide);
        var endSite   = ConnectionSiteHelper.Resolve(new ConnectorAttachment { ShapeId = 2, SiteIndex = 0 }, slide);
        c.ElbowRoute.First().Should().Be(startSite, "route must start at the start site");
        c.ElbowRoute.Last().Should().Be(endSite,    "route must end at the end site");
    }

    [Fact]
    public void ConnectorRouter_ElbowConnector_RoutesAroundOtherShapes()
    {
        var pres = new Presentation();
        var slide = new Slide { Id = "s1" };
        pres.Slides.Add(slide);

        var shapeA = MakeShape(1, DrawingShapeKind.Rectangle, 0, 0, 200, 100);
        var shapeB = MakeShape(2, DrawingShapeKind.Rectangle, 600, 0, 200, 100);
        var obstacle = MakeShape(4, DrawingShapeKind.Rectangle, 350, 0, 100, 100);
        var connector = MakeConnector(3,
            start: new ConnectorAttachment { ShapeId = 1, SiteIndex = 2 },
            end: new ConnectorAttachment { ShapeId = 2, SiteIndex = 0 });

        slide.Shapes.Add(shapeA);
        slide.Shapes.Add(shapeB);
        slide.Shapes.Add(obstacle);
        slide.Shapes.Add(connector);

        var bus = new PresentationCommandBus(pres);
        bus.Execute(new MoveShapeCommand(0, 1, 50, 20));

        var route = slide.Shapes.First(s => s.Id == 3).ElbowRoute;
        route.Should().NotBeNull();
        route!.Should().Contain(point => point.Y < obstacle.OffsetYEmu
            || point.Y > obstacle.OffsetYEmu + obstacle.ExtentCyEmu);
        for (var index = 1; index < route.Count; index++)
            SegmentCrossesInterior(
                route[index - 1],
                route[index],
                (obstacle.OffsetXEmu,
                 obstacle.OffsetYEmu,
                 obstacle.OffsetXEmu + obstacle.ExtentCxEmu,
                 obstacle.OffsetYEmu + obstacle.ExtentCyEmu)).Should().BeFalse();
    }

    [Fact]
    public void ConnectorRouter_ElbowConnector_Undo_ClearsRoute()
    {
        var pres = new Presentation();
        var slide = new Slide { Id = "s1" };
        pres.Slides.Add(slide);

        var shapeA = new SlideShape
        {
            Id = 1, Name = "A", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 200, ExtentCyEmu = 100,
        };
        var shapeB = new SlideShape
        {
            Id = 2, Name = "B", Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 500, OffsetYEmu = 0, ExtentCxEmu = 200, ExtentCyEmu = 100,
        };
        var connector = MakeConnector(3,
            start: new ConnectorAttachment { ShapeId = 1, SiteIndex = 2 },
            end:   new ConnectorAttachment { ShapeId = 2, SiteIndex = 0 });

        slide.Shapes.Add(shapeA);
        slide.Shapes.Add(shapeB);
        slide.Shapes.Add(connector);

        // No route initially
        connector.ElbowRoute.Should().BeNull("route is null before any move");

        var bus = new PresentationCommandBus(pres);
        bus.Execute(new MoveShapeCommand(0, 1, 100, 0));

        var routeAfterMove = slide.Shapes.First(s => s.Id == 3).ElbowRoute;
        routeAfterMove.Should().NotBeNull("route computed after move");

        bus.Undo();

        var routeAfterUndo = slide.Shapes.First(s => s.Id == 3).ElbowRoute;
        routeAfterUndo.Should().BeNull("route reverted to null after undo");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Part 3 — Picture frame round-trip
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_PictureFrame_RoundRect_Preserved()
    {
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var pres = new Presentation { SlideSizeCxEmu = 9144000, SlideSizeCyEmu = 6858000 };
        pres.Slides.Add(new Slide { Id = "s1" });

        var shape = new SlideShape
        {
            Id = 1, Name = "Pic1",
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 914400, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1828800,
            Picture = img,
            PictureFrameGeometry = "roundRect",
        };
        pres.Slides[0].Shapes.Add(shape);

        // Write + read back
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            PptxPackageWriter.Write(pres, ms);
            bytes = ms.ToArray();
        }

        Presentation reloaded;
        using (var ms = new MemoryStream(bytes))
            reloaded = PptxPackageReader.Read(ms);

        var reloadedShape = reloaded.Slides[0].Shapes[0];
        reloadedShape.Kind.Should().Be(SlideShapeKind.Picture);
        reloadedShape.PictureFrameGeometry.Should().Be("roundRect",
            "roundRect frame geometry must survive round-trip");
    }

    [Fact]
    public void RoundTrip_PictureFrame_Rect_IsNullOrAbsent()
    {
        // Plain picture with no frame geometry set — PictureFrameGeometry should be null after reload
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };

        var pres = new Presentation { SlideSizeCxEmu = 9144000, SlideSizeCyEmu = 6858000 };
        pres.Slides.Add(new Slide { Id = "s1" });
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Pic1",
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 914400, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1828800,
            Picture = img,
        });

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            PptxPackageWriter.Write(pres, ms);
            bytes = ms.ToArray();
        }

        Presentation reloaded;
        using (var ms = new MemoryStream(bytes))
            reloaded = PptxPackageReader.Read(ms);

        var reloadedShape = reloaded.Slides[0].Shapes[0];
        // "rect" is the default — the reader does not store it (null is equivalent)
        (reloadedShape.PictureFrameGeometry is null
         || reloadedShape.PictureFrameGeometry == "rect")
            .Should().BeTrue("plain rect picture should not carry a frame geometry override");
    }

    [Fact]
    public void Compositor_PictureFrameRoundRect_CarriedOntoDrawOp()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Shapes.Clear();
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Pic1",
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 914400, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1828800,
            Picture = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" },
            PictureFrameGeometry = "roundRect",
        });

        var ops   = SlideCompositor.Compose(pres, pres.Slides[0]);
        var picOp = ops.OfType<DrawOp.Picture>().FirstOrDefault();

        picOp.Should().NotBeNull("compositor must emit a Picture draw op");
        picOp!.PictureFrameGeometry.Should().Be("roundRect");
        picOp.HasFrameClip.Should().BeTrue();
    }

    [Fact]
    public void SlideCloner_PictureFrameGeometry_IsCopied()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, Name = "Pic1",
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 914400, OffsetYEmu = 457200,
            ExtentCxEmu = 2743200, ExtentCyEmu = 1828800,
            Picture = new ImagePart { Bytes = Minimal1x1Png() },
            PictureFrameGeometry = "ellipse",
        });

        var clone = SlideCloner.CloneSlide(slide);
        clone.Shapes[0].PictureFrameGeometry.Should().Be("ellipse");
    }
}
