using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Wave 23 — connector attachment / routing tests.
/// Covers:
///  1. Connection-site geometry — ConnectionSiteHelper resolves standard sites.
///  2. Round-trip — stCxn/endCxn survive write → read.
///  3. Routing — moving an attached shape updates the connector's bounds (same undo step).
///  4. SlideCloner copies attachments.
/// </summary>
public sealed class ConnectorAttachmentTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static SlideShape MakeRect(uint id, long x, long y, long cx, long cy) => new()
    {
        Id           = id,
        Name         = $"Shape{id}",
        Kind         = SlideShapeKind.AutoShape,
        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
        OffsetXEmu   = x,
        OffsetYEmu   = y,
        ExtentCxEmu  = cx,
        ExtentCyEmu  = cy,
    };

    private static SlideShape MakeConnector(uint id,
        ConnectorAttachment? start = null,
        ConnectorAttachment? end   = null) => new()
    {
        Id           = id,
        Name         = $"Connector{id}",
        Kind         = SlideShapeKind.Connector,
        AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.ElbowConnector,
        OffsetXEmu   = 0,
        OffsetYEmu   = 0,
        ExtentCxEmu  = 100,
        ExtentCyEmu  = 100,
        ConnectionStart = start,
        ConnectionEnd   = end,
    };

    private static (Presentation p, PresentationCommandBus bus, Slide slide) MakePresentation()
    {
        var p   = new Presentation();
        var sl  = new Slide { Id = "rId1" };
        p.Slides.Add(sl);
        var bus = new PresentationCommandBus(p);
        return (p, bus, sl);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 1. Connection-site geometry
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConnectionSiteHelper_Site0_ReturnsLeftMid()
    {
        // Shape at (200, 400), 600×200 => left-mid = (200, 500)
        var shape = MakeRect(1, 200, 400, 600, 200);
        var (x, y) = ConnectionSiteHelper.Resolve(shape, 0);
        x.Should().Be(200);
        y.Should().Be(500); // top(400) + cy/2(100)
    }

    [Fact]
    public void ConnectionSiteHelper_Site1_ReturnsTopMid()
    {
        var shape = MakeRect(1, 200, 400, 600, 200);
        var (x, y) = ConnectionSiteHelper.Resolve(shape, 1);
        x.Should().Be(500); // left(200) + cx/2(300)
        y.Should().Be(400);
    }

    [Fact]
    public void ConnectionSiteHelper_Site2_ReturnsRightMid()
    {
        var shape = MakeRect(1, 200, 400, 600, 200);
        var (x, y) = ConnectionSiteHelper.Resolve(shape, 2);
        x.Should().Be(800); // left(200) + cx(600)
        y.Should().Be(500);
    }

    [Fact]
    public void ConnectionSiteHelper_Site3_ReturnsBottomMid()
    {
        var shape = MakeRect(1, 200, 400, 600, 200);
        var (x, y) = ConnectionSiteHelper.Resolve(shape, 3);
        x.Should().Be(500);
        y.Should().Be(600); // top(400) + cy(200)
    }

    [Fact]
    public void ConnectionSiteHelper_RotationFollowsVisibleShapeTransform()
    {
        var shape = MakeRect(1, 200, 400, 600, 200);
        shape.RotationDeg = 90;

        var (x, y) = ConnectionSiteHelper.Resolve(shape, 1);

        // Top-mid rotates clockwise around the shape centre.
        x.Should().Be(600);
        y.Should().Be(500);
    }

    [Fact]
    public void ConnectionSiteHelper_HorizontalFlipMirrorsConnectionSite()
    {
        var shape = MakeRect(1, 200, 400, 600, 200);
        shape.FlipH = true;

        var (x, y) = ConnectionSiteHelper.Resolve(shape, 0);

        // Left-mid mirrors to right-mid while preserving the vertical coordinate.
        x.Should().Be(800);
        y.Should().Be(500);
    }

    [Theory]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Pentagon, 0, 0, 38)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Pentagon, 1, 50, 0)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Hexagon, 0, 0, 50)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Octagon, 2, 100, 50)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Cross, 3, 50, 100)]
    public void ConnectionSiteHelper_PolygonSitesFollowVisibleOutline(
        Free.Shared.Drawing.DrawingShapeKind kind,
        int siteIndex,
        int expectedX,
        int expectedY)
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        shape.AutoShapeKind = kind;

        var (x, y) = ConnectionSiteHelper.Resolve(shape, siteIndex);

        x.Should().Be(expectedX);
        y.Should().Be(expectedY);
    }

    [Fact]
    public void ConnectionSiteHelper_Star5SitesFollowOuterVertices()
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        shape.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Star5;

        ConnectionSiteHelper.Resolve(shape, 0).Should().Be((21L, 90L));
        ConnectionSiteHelper.Resolve(shape, 1).Should().Be((50L, 0L));
        ConnectionSiteHelper.Resolve(shape, 2).Should().Be((98L, 35L));
        ConnectionSiteHelper.Resolve(shape, 3).Should().Be((50L, 71L));
    }

    [Fact]
    public void ConnectionSiteHelper_CustomGeometrySitesFollowAuthoredOutline()
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        var path = new CustomGeometryPath { PathW = 200, PathH = 200 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, 100, 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, 200, 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, 150, 200));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, 0, 200));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        shape.CustomGeometry.Add(path);

        ConnectionSiteHelper.Resolve(shape, 0).Should().Be((0L, 100L));
        ConnectionSiteHelper.Resolve(shape, 1).Should().Be((50L, 0L));
        ConnectionSiteHelper.Resolve(shape, 2).Should().Be((100L, 0L));
        ConnectionSiteHelper.Resolve(shape, 3).Should().Be((75L, 100L));
    }

    [Fact]
    public void ConnectionSiteHelper_CustomGeometryUsesAuthoredConnectionSites()
    {
        var shape = MakeRect(1, 1000, 2000, 2000, 1000);
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, 0, 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, 100, 100));
        shape.CustomGeometry.Add(path);
        shape.CustomConnectionSites.Add(new CustomGeometryConnectionSite
        {
            X = "75", Y = "20", Angle = "5400000"
        });
        shape.CustomConnectionSites.Add(new CustomGeometryConnectionSite
        {
            X = "hc", Y = "b"
        });
        shape.CustomConnectionSites.Add(new CustomGeometryConnectionSite
        {
            X = "r", Y = "vc"
        });

        ConnectionSiteHelper.Resolve(shape, 0).Should().Be((2500L, 2200L));
        ConnectionSiteHelper.Resolve(shape, 1).Should().Be((2000L, 3000L));
        ConnectionSiteHelper.Resolve(shape, 2).Should().Be((3000L, 2500L));
    }

    [Fact]
    public void MoveShape_ReroutesAttachedConnectorToCustomGeometryOutline()
    {
        var (_, bus, slide) = MakePresentation();
        var custom = MakeRect(1, 1000, 1000, 2000, 1000);
        var path = new CustomGeometryPath { PathW = 200, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, 50, 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, 200, 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, 150, 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, 0, 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        custom.CustomGeometry.Add(path);
        var target = MakeRect(2, 6000, 1000, 2000, 1000);
        var connector = MakeConnector(3,
            start: new ConnectorAttachment { ShapeId = 1, SiteIndex = 0 },
            end: new ConnectorAttachment { ShapeId = 2, SiteIndex = 0 });

        slide.Shapes.Add(custom);
        slide.Shapes.Add(target);
        slide.Shapes.Add(connector);

        bus.Execute(new MoveShapeCommand(0, 1, 500, 200));

        var movedConnector = slide.Shapes.First(shape => shape.Id == 3);
        movedConnector.OffsetXEmu.Should().Be(1500);
        movedConnector.OffsetYEmu.Should().Be(1500);
        movedConnector.ExtentCxEmu.Should().Be(4500);
        movedConnector.ExtentCyEmu.Should().Be(700);
    }

    [Fact]
    public void MoveShape_ReroutesConnectorNestedWithAttachedShapes()
    {
        var (_, bus, slide) = MakePresentation();
        var start = MakeRect(1, 1000, 1000, 2000, 1000);
        var end = MakeRect(2, 6000, 1000, 2000, 1000);
        var connector = MakeConnector(3,
            start: new ConnectorAttachment { ShapeId = start.Id, SiteIndex = 2 },
            end: new ConnectorAttachment { ShapeId = end.Id, SiteIndex = 0 });
        var group = new SlideShape { Id = 10, Kind = SlideShapeKind.Group };
        group.Children.Add(start);
        group.Children.Add(end);
        group.Children.Add(connector);
        slide.Shapes.Add(group);

        bus.Execute(new MoveShapeCommand(0, start.Id, 500, 0));

        connector.OffsetXEmu.Should().Be(3500);
        connector.OffsetYEmu.Should().Be(1500);
        connector.ExtentCxEmu.Should().Be(2500);
        connector.ExtentCyEmu.Should().Be(1);

        bus.Undo();
        connector.OffsetXEmu.Should().Be(0);
        connector.OffsetYEmu.Should().Be(0);
        connector.ExtentCxEmu.Should().Be(100);
        connector.ExtentCyEmu.Should().Be(100);
    }

    [Theory]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Parallelogram, 0, 10, 50)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Parallelogram, 2, 90, 50)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Trapezoid, 0, 10, 50)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Trapezoid, 2, 90, 50)]
    public void ConnectionSiteHelper_SlantedShapesFollowVisibleEdges(
        Free.Shared.Drawing.DrawingShapeKind kind,
        int siteIndex,
        int expectedX,
        int expectedY)
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        shape.AutoShapeKind = kind;

        ConnectionSiteHelper.Resolve(shape, siteIndex).Should().Be((expectedX, expectedY));
    }

    [Fact]
    public void ConnectionSiteHelper_AuthoredSlantAdjustmentsChangeAttachmentSites()
    {
        var shape = MakeRect(1, 0, 0, 200, 100);
        shape.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Trapezoid;
        shape.PresetGeometryAdjustments["adj"] = 50000;

        ConnectionSiteHelper.Resolve(shape, 0).Should().Be((25L, 50L));
        ConnectionSiteHelper.Resolve(shape, 2).Should().Be((175L, 50L));
    }

    [Fact]
    public void ConnectionSiteHelper_ChevronAndHomePlateAttachToNotchAndTip()
    {
        var chevron = MakeRect(1, 0, 0, 100, 100);
        chevron.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Chevron;
        ConnectionSiteHelper.Resolve(chevron, 0).Should().Be((24L, 50L));
        ConnectionSiteHelper.Resolve(chevron, 2).Should().Be((100L, 50L));

        var homePlate = MakeRect(2, 0, 0, 100, 100);
        homePlate.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.HomePlate;
        ConnectionSiteHelper.Resolve(homePlate, 1).Should().Be((38L, 0L));
        ConnectionSiteHelper.Resolve(homePlate, 2).Should().Be((100L, 50L));
    }

    // Wave 137 finding A: the notch depth used to scale unconditionally by shape WIDTH, but
    // ShapeGeometryBuilder.Chevron/HomePlate only does that for the *unauthored* fallback -- once
    // an "adj" depth guide is authored, the rendered outline scales the notch off the SHORTER
    // side. On a non-square shape those two bases disagree, so the old site drifted off the
    // visible outline. Assert against the actual rendered polygon, not a hard-coded number, across
    // several aspect ratios so a reintroduced width/min-dimension mismatch is caught regardless of
    // which axis is shorter.
    [Theory]
    [InlineData(200, 100)]
    [InlineData(240, 60)]
    [InlineData(100, 200)]
    public void ConnectionSiteHelper_ChevronNotchMatchesRenderedOutlineForAuthoredAdjustment(long width, long height)
    {
        var shape = MakeRect(1, 0, 0, width, height);
        shape.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Chevron;
        shape.PresetGeometryAdjustments["adj"] = 35000;

        var rendered = Free.Shared.Drawing.ShapeGeometryBuilder.Build(
            Free.Shared.Drawing.DrawingShapeKind.Chevron,
            new Free.Shared.Drawing.LayoutRect(0, 0, width, height),
            shape.PresetGeometryAdjustments);

        // Vertex order is [(0,0),(x2,0),(1,0.5),(x2,1),(0,1),(x1,0.5)]; the inward notch tip
        // (site 0, the west attachment) is the 5th LineTo -- segment index 4.
        var notch = rendered.Contours[0].Segments[4].End;

        var site = ConnectionSiteHelper.Resolve(shape, 0);
        site.X.Should().Be((long)Math.Round(notch.X));
        site.Y.Should().Be((long)Math.Round(notch.Y));
    }

    [Theory]
    [InlineData(200, 100)]
    [InlineData(240, 60)]
    [InlineData(100, 200)]
    public void ConnectionSiteHelper_HomePlateTopAndBottomSitesMatchRenderedOutlineForAuthoredAdjustment(long width, long height)
    {
        var shape = MakeRect(1, 0, 0, width, height);
        shape.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.HomePlate;
        shape.PresetGeometryAdjustments["adj"] = 35000;

        var rendered = Free.Shared.Drawing.ShapeGeometryBuilder.Build(
            Free.Shared.Drawing.DrawingShapeKind.HomePlate,
            new Free.Shared.Drawing.LayoutRect(0, 0, width, height),
            shape.PresetGeometryAdjustments);

        // Vertex order is [(0,0),(x1,0),(1,0.5),(x1,1),(0,1)]; the top-flat edge runs from the
        // origin to x1 (segment index 0), so its midpoint is what site 1 (top-mid) should land on.
        // ConnectionSiteHelper computes that midpoint with integer (truncating) division on a
        // rounded-to-long x1 -- mirror that exactly rather than rounding the continuous midpoint,
        // which disagrees by one DIP whenever x1 is odd (e.g. round-to-even turns 109.5 into 110,
        // but long division truncates 219/2 to 109).
        var x1 = (long)Math.Round(rendered.Contours[0].Segments[0].End.X);
        var expectedTopMidX = x1 / 2;

        var topMid = ConnectionSiteHelper.Resolve(shape, 1);
        topMid.X.Should().Be(expectedTopMidX);
        topMid.Y.Should().Be(0L);
    }

    // Sibling no-regression guard: the *unauthored* fallback outline is a fixed 24% of WIDTH
    // (ShapeGeometryBuilder's hard-coded polygon), not of the shorter side -- unlike the
    // authored-adj case above. A fix that switched to min-dimension unconditionally would break
    // this case even though it never exhibited the original bug.
    [Theory]
    [InlineData(300, 120)]
    [InlineData(120, 300)]
    public void ConnectionSiteHelper_ChevronAndHomePlateDefaultNotchStaysWidthBasedWithoutAuthoredAdjustment(long width, long height)
    {
        var chevron = MakeRect(1, 0, 0, width, height);
        chevron.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Chevron;
        var chevronSite = ConnectionSiteHelper.Resolve(chevron, 0);
        chevronSite.X.Should().Be((long)Math.Round(width * 0.24));
        chevronSite.Y.Should().Be(height / 2);

        var homePlate = MakeRect(2, 0, 0, width, height);
        homePlate.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.HomePlate;
        var homePlateSite = ConnectionSiteHelper.Resolve(homePlate, 1);
        var expectedTopEdgeEnd = width - (long)Math.Round(width * 0.24);
        homePlateSite.X.Should().Be(expectedTopEdgeEnd / 2);
        homePlateSite.Y.Should().Be(0L);
    }

    [Theory]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.RectangularCallout, 3, 45, 100)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.RoundedRectangularCallout, 3, 45, 100)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.OvalCallout, 3, 47, 100)]
    public void ConnectionSiteHelper_CalloutSitesReachVisibleTail(
        Free.Shared.Drawing.DrawingShapeKind kind,
        int siteIndex,
        int expectedX,
        int expectedY)
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        shape.AutoShapeKind = kind;

        ConnectionSiteHelper.Resolve(shape, siteIndex).Should().Be((expectedX, expectedY));
    }

    [Fact]
    public void ConnectionSiteHelper_HeartUsesNotchAndBottomExtrema()
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        shape.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Heart;

        ConnectionSiteHelper.Resolve(shape, 1).Should().Be((50L, 22L));
        ConnectionSiteHelper.Resolve(shape, 3).Should().Be((50L, 100L));
    }

    [Fact]
    public void ConnectionSiteHelper_FlowchartDataSitesFollowSlantedOutline()
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        shape.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.FlowchartData;

        ConnectionSiteHelper.Resolve(shape, 0).Should().Be((11L, 50L));
        ConnectionSiteHelper.Resolve(shape, 1).Should().Be((61L, 0L));
        ConnectionSiteHelper.Resolve(shape, 2).Should().Be((89L, 50L));
        ConnectionSiteHelper.Resolve(shape, 3).Should().Be((39L, 100L));
    }

    [Fact]
    public void ConnectionSiteHelper_DirectionalArrowSitesFollowVisibleTipAndGuides()
    {
        var arrow = MakeRect(1, 0, 0, 100, 100);
        arrow.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.RightArrow;

        ConnectionSiteHelper.Resolve(arrow, 0).Should().Be((0L, 50L));
        ConnectionSiteHelper.Resolve(arrow, 2).Should().Be((100L, 50L));
        ConnectionSiteHelper.Resolve(arrow, 1).Should().Be((62L, 0L));
        ConnectionSiteHelper.Resolve(arrow, 3).Should().Be((62L, 100L));

        arrow.PresetGeometryAdjustments["adj1"] = 25000;
        arrow.PresetGeometryAdjustments["adj2"] = 75000;
        ConnectionSiteHelper.Resolve(arrow, 1).Should().Be((25L, 0L));
        ConnectionSiteHelper.Resolve(arrow, 3).Should().Be((25L, 100L));
    }

    [Theory]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.LeftArrow, 0, 0, 50)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.UpArrow, 1, 50, 0)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.DownArrow, 3, 50, 100)]
    public void ConnectionSiteHelper_DirectionalArrowTipSitesStayOnVisiblePoint(
        Free.Shared.Drawing.DrawingShapeKind kind,
        int siteIndex,
        int expectedX,
        int expectedY)
    {
        var arrow = MakeRect(1, 0, 0, 100, 100);
        arrow.AutoShapeKind = kind;

        ConnectionSiteHelper.Resolve(arrow, siteIndex).Should().Be((expectedX, expectedY));
    }

    [Fact]
    public void ConnectionSiteHelper_CompoundArrowSitesCoverBothVisibleTips()
    {
        var horizontal = MakeRect(1, 0, 0, 100, 100);
        horizontal.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.LeftRightArrow;
        ConnectionSiteHelper.Resolve(horizontal, 0).Should().Be((0L, 50L));
        ConnectionSiteHelper.Resolve(horizontal, 2).Should().Be((100L, 50L));

        var vertical = MakeRect(2, 0, 0, 100, 100);
        vertical.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.UpDownArrow;
        ConnectionSiteHelper.Resolve(vertical, 1).Should().Be((50L, 0L));
        ConnectionSiteHelper.Resolve(vertical, 3).Should().Be((50L, 100L));
    }

    [Fact]
    public void MoveShape_ReroutesAttachedArrowConnectorToVisibleTip()
    {
        var (_, bus, slide) = MakePresentation();
        var arrow = MakeRect(1, 1000, 1000, 2000, 1000);
        arrow.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.RightArrow;
        var target = MakeRect(2, 6000, 1000, 2000, 1000);
        var connector = MakeConnector(3,
            start: new ConnectorAttachment { ShapeId = 1, SiteIndex = 2 },
            end: new ConnectorAttachment { ShapeId = 2, SiteIndex = 0 });

        slide.Shapes.Add(arrow);
        slide.Shapes.Add(target);
        slide.Shapes.Add(connector);

        bus.Execute(new MoveShapeCommand(0, 1, 500, 200));

        // Right-arrow site 2 is its point, not the old bbox midpoint.
        var movedConnector = slide.Shapes.First(shape => shape.Id == 3);
        movedConnector.OffsetXEmu.Should().Be(3500);
        movedConnector.OffsetYEmu.Should().Be(1500);
        movedConnector.ExtentCxEmu.Should().Be(2500);
        movedConnector.ExtentCyEmu.Should().Be(200);
    }

    [Fact]
    public void ConnectionSiteHelper_Star8SitesFollowCardinalOuterVertices()
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        shape.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Star8;

        ConnectionSiteHelper.Resolve(shape, 0).Should().Be((0L, 50L));
        ConnectionSiteHelper.Resolve(shape, 1).Should().Be((50L, 0L));
        ConnectionSiteHelper.Resolve(shape, 2).Should().Be((100L, 50L));
        ConnectionSiteHelper.Resolve(shape, 3).Should().Be((50L, 100L));
    }

    [Theory]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Ribbon, 0, 0, 76)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Ribbon, 1, 50, 22)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Ribbon, 2, 100, 24)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Ribbon, 3, 50, 78)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Wave, 0, 0, 45)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Wave, 1, 22, 12)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Wave, 2, 100, 36)]
    [InlineData(Free.Shared.Drawing.DrawingShapeKind.Wave, 3, 58, 88)]
    public void ConnectionSiteHelper_RibbonAndWaveSitesFollowVisibleOutline(
        Free.Shared.Drawing.DrawingShapeKind kind,
        int siteIndex,
        int expectedX,
        int expectedY)
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        shape.AutoShapeKind = kind;

        ConnectionSiteHelper.Resolve(shape, siteIndex).Should().Be((expectedX, expectedY));
    }

    [Fact]
    public void ConnectionSiteHelper_RibbonAndWaveSitesHonorShapeTransform()
    {
        var shape = MakeRect(1, 0, 0, 100, 100);
        shape.AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Wave;
        shape.RotationDeg = 90;

        // The wave crest site rotates clockwise around the shape centre.
        ConnectionSiteHelper.Resolve(shape, 1).Should().Be((88L, 22L));
    }

    [Fact]
    public void ConnectionSiteHelper_OutOfRange_ReturnsCentre()
    {
        var shape = MakeRect(1, 0, 0, 200, 100);
        var (x, y) = ConnectionSiteHelper.Resolve(shape, 99);
        x.Should().Be(100); // centre x
        y.Should().Be(50);  // centre y
    }

    [Fact]
    public void ConnectionSiteHelper_ResolveFromSlide_FindsAttachedShape()
    {
        var (_, _, slide) = MakePresentation();
        var shapeA = MakeRect(5, 1000, 2000, 4000, 2000);
        slide.Shapes.Add(shapeA);

        var attachment = new ConnectorAttachment { ShapeId = 5, SiteIndex = 3 };
        var (x, y) = ConnectionSiteHelper.Resolve(attachment, slide);
        // bottom-mid: x = 1000+2000=3000, y = 2000+2000=4000
        x.Should().Be(3000);
        y.Should().Be(4000);
    }

    [Fact]
    public void ConnectionSiteHelper_MissingShape_ReturnsZero()
    {
        var (_, _, slide) = MakePresentation();
        var attachment = new ConnectorAttachment { ShapeId = 99, SiteIndex = 0 };
        var (x, y) = ConnectionSiteHelper.Resolve(attachment, slide);
        x.Should().Be(0);
        y.Should().Be(0);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 2. Round-trip — stCxn / endCxn survive write → read
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_ConnectorAttachment_Preserved()
    {
        // Build a minimal presentation with a shape + connector.
        var pres = new Presentation
        {
            SlideSizeCxEmu = 9144000,
            SlideSizeCyEmu = 6858000,
        };
        var slide = new Slide { Id = "rId1" };
        pres.Slides.Add(slide);

        // Shape at arbitrary position.
        var shape = MakeRect(5, 914400, 914400, 914400, 914400);
        slide.Shapes.Add(shape);

        // Connector attached: start → shape(5) site 3, end → shape(7) site 1.
        var connector = MakeConnector(9,
            start: new ConnectorAttachment { ShapeId = 5, SiteIndex = 3 },
            end:   new ConnectorAttachment { ShapeId = 7, SiteIndex = 1 });
        connector.ExtentCxEmu = 914400;
        connector.ExtentCyEmu = 914400;
        slide.Shapes.Add(connector);

        // Write to pptx bytes.
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            PptxPackageWriter.Write(pres, ms);
            bytes = ms.ToArray();
        }

        // Read back.
        Presentation reloaded;
        using (var ms = new MemoryStream(bytes))
            reloaded = PptxPackageReader.Read(ms);

        var reloadedSlide = reloaded.Slides.Should().HaveCountGreaterThan(0).And.Subject.First();
        var reloadedConnector = reloadedSlide.Shapes
            .FirstOrDefault(s => s.Kind == SlideShapeKind.Connector);

        reloadedConnector.Should().NotBeNull("connector must survive round-trip");
        reloadedConnector!.ConnectionStart.Should().NotBeNull();
        reloadedConnector.ConnectionStart!.ShapeId.Should().Be(5u);
        reloadedConnector.ConnectionStart.SiteIndex.Should().Be(3);
        reloadedConnector.ConnectionEnd.Should().NotBeNull();
        reloadedConnector.ConnectionEnd!.ShapeId.Should().Be(7u);
        reloadedConnector.ConnectionEnd.SiteIndex.Should().Be(1);
    }

    [Fact]
    public void RoundTrip_FreeConnector_NoAttachmentElements()
    {
        // A connector with no attachments should not emit stCxn/endCxn.
        var pres = new Presentation { SlideSizeCxEmu = 9144000, SlideSizeCyEmu = 6858000 };
        var slide = new Slide { Id = "rId1" };
        pres.Slides.Add(slide);

        var connector = MakeConnector(3); // no attachments
        connector.ExtentCxEmu = 914400;
        connector.ExtentCyEmu = 914400;
        slide.Shapes.Add(connector);

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            PptxPackageWriter.Write(pres, ms);
            bytes = ms.ToArray();
        }

        // Inspect the raw XML — should not contain stCxn or endCxn.
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var slideEntry = archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("ppt/slides/slide")
                                                          && e.FullName.EndsWith(".xml"));
        slideEntry.Should().NotBeNull();
        using var stream = slideEntry!.Open();
        var doc = XDocument.Load(stream);
        var xml = doc.ToString();
        xml.Should().NotContain("stCxn", "free connector must not emit stCxn");
        xml.Should().NotContain("endCxn", "free connector must not emit endCxn");

        // Read back — no attachment.
        Presentation reloaded;
        using (var ms2 = new MemoryStream(bytes))
            reloaded = PptxPackageReader.Read(ms2);

        var reloadedConnector = reloaded.Slides.First().Shapes
            .FirstOrDefault(s => s.Kind == SlideShapeKind.Connector);
        reloadedConnector.Should().NotBeNull();
        reloadedConnector!.ConnectionStart.Should().BeNull();
        reloadedConnector.ConnectionEnd.Should().BeNull();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 3. Routing on move — MoveShapeCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveShape_ReroutesAttachedConnector()
    {
        var (p, bus, slide) = MakePresentation();

        // Two shapes + connector between them.
        // shapeA: (1000, 1000, 2000, 1000) → site 2 (right-mid) = (3000, 1500)
        // shapeB: (6000, 1000, 2000, 1000) → site 0 (left-mid)  = (6000, 1500)
        var shapeA = MakeRect(1, 1000, 1000, 2000, 1000);
        var shapeB = MakeRect(2, 6000, 1000, 2000, 1000);
        var connector = MakeConnector(3,
            start: new ConnectorAttachment { ShapeId = 1, SiteIndex = 2 },
            end:   new ConnectorAttachment { ShapeId = 2, SiteIndex = 0 });

        slide.Shapes.Add(shapeA);
        slide.Shapes.Add(shapeB);
        slide.Shapes.Add(connector);

        // Move shapeA 500 EMU right and 200 EMU down.
        bus.Execute(new MoveShapeCommand(0, 1, 500, 200));

        // shapeA is now at (1500, 1200, 2000, 1000).
        // site 2 of shapeA = right-mid = (3500, 1700)
        // site 0 of shapeB = left-mid  = (6000, 1500)
        // Connector bounding box: x=min(3500,6000)=3500, y=min(1700,1500)=1500
        //                         cx=abs(6000-3500)=2500, cy=abs(1500-1700)=200
        var c = slide.Shapes.First(s => s.Id == 3);
        c.OffsetXEmu.Should().Be(3500);
        c.OffsetYEmu.Should().Be(1500);
        c.ExtentCxEmu.Should().Be(2500);
        c.ExtentCyEmu.Should().Be(200);
    }

    [Fact]
    public void MoveShape_Undo_RestoresConnectorBounds()
    {
        var (p, bus, slide) = MakePresentation();

        var shapeA = MakeRect(1, 1000, 1000, 2000, 1000);
        var shapeB = MakeRect(2, 6000, 1000, 2000, 1000);
        var connector = MakeConnector(3,
            start: new ConnectorAttachment { ShapeId = 1, SiteIndex = 2 },
            end:   new ConnectorAttachment { ShapeId = 2, SiteIndex = 0 });

        // Manually set connector initial bounds to match initial site positions.
        // site2(shapeA) = (3000, 1500), site0(shapeB) = (6000, 1500)
        connector.OffsetXEmu  = 3000;
        connector.OffsetYEmu  = 1500;
        connector.ExtentCxEmu = 3000;
        connector.ExtentCyEmu = 1;

        slide.Shapes.Add(shapeA);
        slide.Shapes.Add(shapeB);
        slide.Shapes.Add(connector);

        long origX  = connector.OffsetXEmu;
        long origY  = connector.OffsetYEmu;
        long origCx = connector.ExtentCxEmu;
        long origCy = connector.ExtentCyEmu;

        bus.Execute(new MoveShapeCommand(0, 1, 500, 200));
        bus.Undo();

        var c = slide.Shapes.First(s => s.Id == 3);
        c.OffsetXEmu.Should().Be(origX);
        c.OffsetYEmu.Should().Be(origY);
        c.ExtentCxEmu.Should().Be(origCx);
        c.ExtentCyEmu.Should().Be(origCy);
    }

    [Fact]
    public void MoveShape_NoAttachedConnectors_MovesCleanly()
    {
        var (p, bus, slide) = MakePresentation();
        var shape = MakeRect(1, 1000, 1000, 500, 500);
        // A free connector (no attachments) — should NOT be rerouted.
        var freeConnector = MakeConnector(2);
        freeConnector.OffsetXEmu  = 200;
        freeConnector.OffsetYEmu  = 200;
        freeConnector.ExtentCxEmu = 800;
        freeConnector.ExtentCyEmu = 600;

        slide.Shapes.Add(shape);
        slide.Shapes.Add(freeConnector);

        bus.Execute(new MoveShapeCommand(0, 1, 100, 100));

        // Free connector stays put.
        var fc = slide.Shapes.First(s => s.Id == 2);
        fc.OffsetXEmu.Should().Be(200);
        fc.OffsetYEmu.Should().Be(200);
    }

    [Fact]
    public void ResizeShape_ReroutesAttachedConnector()
    {
        var (p, bus, slide) = MakePresentation();

        // shapeA at (0, 0, 1000, 1000); connector start = site 2 (right-mid) = (1000, 500)
        // shapeB at (3000, 0, 1000, 1000); connector end = site 0 (left-mid) = (3000, 500)
        var shapeA = MakeRect(1, 0, 0, 1000, 1000);
        var shapeB = MakeRect(2, 3000, 0, 1000, 1000);
        var connector = MakeConnector(3,
            start: new ConnectorAttachment { ShapeId = 1, SiteIndex = 2 },
            end:   new ConnectorAttachment { ShapeId = 2, SiteIndex = 0 });

        slide.Shapes.Add(shapeA);
        slide.Shapes.Add(shapeB);
        slide.Shapes.Add(connector);

        // Resize shapeA to (0, 0, 2000, 1000): right-mid → (2000, 500)
        bus.Execute(new ResizeShapeCommand(0, 1, 0, 0, 2000, 1000));

        var c = slide.Shapes.First(s => s.Id == 3);
        // start = (2000, 500), end = (3000, 500) → bbox: x=2000, y=500, cx=1000, cy=1
        c.OffsetXEmu.Should().Be(2000);
        c.OffsetYEmu.Should().Be(500);
        c.ExtentCxEmu.Should().Be(1000);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 4. SlideCloner clones attachments
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SlideCloner_ClonesConnectorAttachments()
    {
        var slide = new Slide { Id = "rId1" };
        var connector = MakeConnector(9,
            start: new ConnectorAttachment { ShapeId = 5, SiteIndex = 3 },
            end:   new ConnectorAttachment { ShapeId = 7, SiteIndex = 1 });
        slide.Shapes.Add(connector);

        var clonedSlide = SlideCloner.CloneSlide(slide);
        var clonedConnector = clonedSlide.Shapes.First(s => s.Kind == SlideShapeKind.Connector);

        clonedConnector.ConnectionStart.Should().NotBeNull();
        clonedConnector.ConnectionStart!.ShapeId.Should().Be(5u);
        clonedConnector.ConnectionStart.SiteIndex.Should().Be(3);

        clonedConnector.ConnectionEnd.Should().NotBeNull();
        clonedConnector.ConnectionEnd!.ShapeId.Should().Be(7u);
        clonedConnector.ConnectionEnd.SiteIndex.Should().Be(1);

        // Mutating the clone must not affect the original.
        clonedConnector.ConnectionStart.ShapeId = 99;
        connector.ConnectionStart!.ShapeId.Should().Be(5u, "original must be independent");
    }

    [Fact]
    public void SlideCloner_NullAttachments_StaysNull()
    {
        var connector = MakeConnector(1); // no attachments
        var copy = SlideCloner.CloneShape(connector);
        copy.ConnectionStart.Should().BeNull();
        copy.ConnectionEnd.Should().BeNull();
    }
}
