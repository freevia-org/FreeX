namespace FreeW.Core.Model.Tests;

public class PresetShapeGeometryTests
{
    public static IEnumerable<object[]> AllDedicatedPresets() =>
        PresetShapeGeometry.DedicatedPresets.OrderBy(name => name, StringComparer.Ordinal).Select(name => new object[] { name });

    private static IEnumerable<CustomPoint> AllPoints(CustomGeometry geometry) =>
        geometry.Segments.SelectMany(segment => new[] { segment.Point, segment.ControlPoint1, segment.ControlPoint2 })
            .Where(point => point is not null)
            .Select(point => point!);

    [Fact]
    public void Engine_CoversTheWordShapeGalleryFamilies()
    {
        PresetShapeGeometry.DedicatedPresets.Should().Contain(
        [
            "line", "straightConnector1", "bentConnector3", "curvedConnector3",
            "snip1Rect", "round2DiagRect", "triangle", "rtTriangle", "parallelogram", "trapezoid", "diamond",
            "pentagon", "hexagon", "octagon", "plus", "donut", "arc", "chord", "pie", "frame", "corner", "can",
            "cube", "heart", "lightningBolt", "sun", "moon", "cloud", "smileyFace", "leftBracket", "rightBrace",
            "rightArrow", "leftArrow", "upArrow", "downArrow", "leftRightArrow", "upDownArrow", "quadArrow",
            "bentArrow", "uturnArrow", "chevron", "homePlate", "notchedRightArrow",
            "flowChartDecision", "flowChartInputOutput", "flowChartPredefinedProcess", "flowChartDocument",
            "flowChartTerminator", "flowChartPreparation", "flowChartManualInput", "flowChartConnector",
            "flowChartOffpageConnector", "flowChartDelay", "flowChartOnlineStorage",
            "star4", "star5", "star6", "star8", "star10", "star12", "star16", "star24", "star32",
            "irregularSeal1", "wave", "wedgeRectCallout", "wedgeRoundRectCallout", "wedgeEllipseCallout",
            "cloudCallout", "borderCallout1", "mathPlus", "mathMinus", "mathMultiply", "mathDivide", "mathEqual",
        ]);
        PresetShapeGeometry.DedicatedPresets.Count.Should().BeGreaterThan(120);
    }

    [Theory]
    [MemberData(nameof(AllDedicatedPresets))]
    public void EveryDedicatedPreset_BuildsAFiniteOutlineNearItsBox(string preset)
    {
        foreach (var (width, height) in new[] { (120.0, 80.0), (40.0, 200.0), (300.0, 30.0) })
        {
            var geometry = PresetShapeGeometry.Build(preset, width, height);

            geometry.Should().NotBeNull();
            geometry!.Segments.Should().NotBeEmpty();
            geometry.Segments[0].Kind.Should().Be(CustomSegmentKind.MoveTo);
            geometry.Width.Should().Be((long)(width * 12700));
            geometry.Height.Should().Be((long)(height * 12700));
            foreach (var point in AllPoints(geometry))
            {
                // Callout tips and Bézier control points legitimately leave the box, but not by far.
                point.X.Should().BeInRange(-2 * geometry.Width, 3 * geometry.Width, $"{preset} x at {width}x{height}");
                point.Y.Should().BeInRange(-2 * geometry.Height, 3 * geometry.Height, $"{preset} y at {width}x{height}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllDedicatedPresets))]
    public void EveryDedicatedPreset_SurvivesHostileAdjustValues(string preset)
    {
        foreach (var formula in new[] { "val 99999999999", "val -99999999999", "val NaN", "val 1e308", "garbage", "val 0", "val 100000" })
        {
            ShapeAdjustment[] adjustments =
            [
                new("adj", formula), new("adj1", formula), new("adj2", formula), new("adj3", formula),
                new("adj4", formula), new("adj5", formula), new("adj6", formula),
            ];

            var geometry = PresetShapeGeometry.Build(preset, 120, 80, adjustments);

            geometry.Should().NotBeNull();
            AllPoints(geometry!).Should().OnlyContain(
                point => Math.Abs(point.X) < 100_000_000_000 && Math.Abs(point.Y) < 100_000_000_000,
                $"{preset} with '{formula}'");
        }
    }

    [Theory]
    [InlineData(double.NaN, 80)]
    [InlineData(120, double.PositiveInfinity)]
    [InlineData(-5, 80)]
    [InlineData(0, 80)]
    [InlineData(1e9, 80)]
    public void UnusableSize_BuildsNothing(double width, double height) =>
        PresetShapeGeometry.Build("star5", width, height).Should().BeNull();

    [Theory]
    [InlineData("rect")]
    [InlineData("roundRect")]
    [InlineData("ellipse")]
    [InlineData("gear6")]
    [InlineData(null)]
    public void NamedKindsAndUnknownPresets_HaveNoSynthesizedOutline(string? preset) =>
        PresetShapeGeometry.Build(preset, 120, 80).Should().BeNull();

    [Fact]
    public void Triangle_HonoursItsApexAdjustValue()
    {
        var centred = PresetShapeGeometry.Build("triangle", 100, 100)!;
        var leftApex = PresetShapeGeometry.Build("triangle", 100, 100, [new ShapeAdjustment("adj", "val 0")])!;

        centred.Segments[1].Point!.X.Should().Be(centred.Width / 2);
        leftApex.Segments[1].Point!.X.Should().Be(0);
    }

    [Fact]
    public void RightArrow_AdjustValuesAreMeasuredAgainstTheShorterSide()
    {
        // adj2 = 50000 puts the head base half the SHORTER side (40pt) in from the tip.
        var geometry = PresetShapeGeometry.Build("rightArrow", 200, 80)!;
        var headBaseX = geometry.Segments[1].Point!.X;

        headBaseX.Should().Be((long)((200 - 40) * 12700));
    }

    [Fact]
    public void Star5_HasTenVertices_AndTouchesAllFourEdges()
    {
        var geometry = PresetShapeGeometry.Build("star5", 100, 100)!;
        var points = geometry.Segments.Where(segment => segment.Point is not null).Select(segment => segment.Point!).ToList();

        points.Should().HaveCount(10);
        points.Min(point => point.X).Should().BeCloseTo(0, 200);
        points.Max(point => point.X).Should().BeCloseTo(geometry.Width, 200);
        points.Min(point => point.Y).Should().BeCloseTo(0, 200);
        points.Max(point => point.Y).Should().BeCloseTo(geometry.Height, 200);
    }

    [Fact]
    public void Donut_IsTwoClosedContours_SoTheHoleStaysOpenUnderEvenOddFill()
    {
        var geometry = PresetShapeGeometry.Build("donut", 100, 100)!;

        geometry.Segments.Count(segment => segment.Kind == CustomSegmentKind.MoveTo).Should().Be(2);
        geometry.Segments.Count(segment => segment.Kind == CustomSegmentKind.Close).Should().Be(2);
    }

    [Fact]
    public void Connector_IsAnUnfilledStroke_RetracedToItsStart()
    {
        var geometry = PresetShapeGeometry.Build("bentConnector3", 100, 60)!;

        geometry.Segments.Should().NotContain(segment => segment.Kind == CustomSegmentKind.Close);
        geometry.Segments[^1].Point.Should().Be(geometry.Segments[0].Point, "a detail stroke encloses no area");
    }

    [Fact]
    public void HorizontalLine_WithZeroHeight_StillBuilds()
    {
        var geometry = PresetShapeGeometry.Build("line", 100, 0);

        geometry.Should().NotBeNull();
        geometry!.Height.Should().Be(1);
    }

    [Theory]
    [InlineData("triangle")]
    [InlineData("stealth")]
    [InlineData("arrow")]
    [InlineData("diamond")]
    [InlineData("oval")]
    public void LineEnds_AddDecorationStrokes(string type)
    {
        var plain = PresetShapeGeometry.Build("line", 100, 50)!;
        var decorated = PresetShapeGeometry.Build("line", 100, 50, tailEnd: new ShapeLineEnd(type), outlineWidthPt: 2)!;

        decorated.Segments.Count.Should().BeGreaterThan(plain.Segments.Count);
        decorated.Segments.Should().NotContain(segment => segment.Kind == CustomSegmentKind.Close);
    }

    [Fact]
    public void LineEndOfTypeNone_AddsNothing()
    {
        var plain = PresetShapeGeometry.Build("line", 100, 50)!;
        var none = PresetShapeGeometry.Build("line", 100, 50, headEnd: new ShapeLineEnd("none"))!;

        PresetShapeGeometry.Signature(none).Should().Be(PresetShapeGeometry.Signature(plain));
    }

    [Fact]
    public void AppliedPreset_IsIntact_UntilItsOutlineIsEdited()
    {
        var shape = PresetShapeFactory.Create("hexagon", 120, 80);

        shape.Kind.Should().Be(ShapeKind.Rectangle);
        shape.PresetName.Should().Be("hexagon");
        shape.HasIntactPresetGeometry.Should().BeTrue();

        shape.CustomGeometry!.Segments.RemoveAt(shape.CustomGeometry.Segments.Count - 2);
        shape.HasIntactPresetGeometry.Should().BeFalse();
    }

    [Fact]
    public void RenderGeometry_FollowsAResize_WhileTheStoredOutlineStaysIntact()
    {
        var shape = PresetShapeFactory.Create("rightArrow", 120, 80);
        var stored = PresetShapeGeometry.Signature(shape.CustomGeometry!);
        shape.WidthPt = 300;

        var rendered = PresetShapeGeometry.ResolveRenderGeometry(shape)!;

        rendered.Width.Should().Be((long)(300 * 12700));
        shape.HasIntactPresetGeometry.Should().BeTrue();
        PresetShapeGeometry.Signature(shape.CustomGeometry!).Should().Be(stored);
    }

    [Fact]
    public void RenderGeometry_OfAnEditedOutline_IsTheEditedOutlineItself()
    {
        var shape = PresetShapeFactory.Create("rightArrow", 120, 80);
        shape.CustomGeometry!.Segments.RemoveAt(2);

        PresetShapeGeometry.ResolveRenderGeometry(shape).Should().BeSameAs(shape.CustomGeometry);
    }

    [Theory]
    [InlineData("rect", ShapeKind.Rectangle)]
    [InlineData("roundRect", ShapeKind.RoundedRectangle)]
    [InlineData("ellipse", ShapeKind.Ellipse)]
    public void NamedKindPresets_MapOntoShapeKind(string preset, ShapeKind expected)
    {
        var shape = PresetShapeFactory.Create(preset, 120, 80);

        shape.Kind.Should().Be(expected);
        shape.PresetName.Should().BeNull();
        shape.HasCustomGeometry.Should().BeFalse();
    }

    [Fact]
    public void Clone_KeepsThePresetIdentity()
    {
        var shape = PresetShapeFactory.Create("line", 120, 60);
        shape.TailEnd = new ShapeLineEnd("triangle", "med", "lg");
        shape.PresetAdjustments.Add(new ShapeAdjustment("adj1", "val 1"));
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(paragraph);

        var clone = DocumentModelCloner.CloneParagraph(paragraph, RevisionClonePolicy.Preserve).Runs.Single().Shape!;

        clone.Should().NotBeSameAs(shape);
        clone.PresetName.Should().Be("line");
        clone.TailEnd.Should().Be(shape.TailEnd);
        clone.PresetAdjustments.Should().Equal(shape.PresetAdjustments);
        clone.HasIntactPresetGeometry.Should().BeTrue();
    }
}
