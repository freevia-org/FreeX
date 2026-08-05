using FluentAssertions;

namespace Free.Shared.Pdf.Tests;

public sealed class PdfContentPagePlacementTests
{
    private static readonly PdfColor Primary = new(0x10, 0x20, 0x30);
    private static readonly PdfColor Secondary = new(0x40, 0x50, 0x60);
    private static readonly PdfLinearGradient Gradient = new(
        0,
        0,
        10,
        5,
        [new PdfGradientStop(0, Primary), new PdfGradientStop(1, Secondary)]);
    private static readonly PdfDashPattern Dash = new([1, 2], 0.5);
    private static readonly PdfPatternFill Pattern = new(
        PdfPatternKind.Brick,
        Primary,
        Secondary,
        1.5);

    [Fact]
    public void MapOps_FitsAndCentersPrimitiveBoundsAndStyles()
    {
        var mapped = Place(
            new PdfFillRect(1, 2, 3, 4, Primary),
            new PdfFillRectPattern(1, 2, 3, 4, Pattern),
            new PdfFillRectLinearGradient(1, 2, 3, 4, Gradient, Secondary),
            new PdfStrokeRect(1, 2, 3, 4, Primary, 0.5, Dash),
            new PdfStrokeRectLinearGradient(1, 2, 3, 4, Gradient, Secondary, 0.5, Dash),
            new PdfFillEllipse(1, 2, 3, 4, Primary),
            new PdfFillEllipsePattern(1, 2, 3, 4, Pattern),
            new PdfFillEllipseLinearGradient(1, 2, 3, 4, Gradient, Secondary),
            new PdfStrokeEllipse(1, 2, 3, 4, Primary, 0.5, Dash),
            new PdfStrokeEllipseLinearGradient(1, 2, 3, 4, Gradient, Secondary, 0.5, Dash));

        mapped.Should().HaveCount(10);
        mapped.Should().AllSatisfy(op => Bounds(op).Should().Be((12, 134, 6, 8)));

        mapped.OfType<PdfFillRectPattern>().Single().Pattern.UnitScale.Should().Be(3);
        mapped.OfType<PdfFillEllipsePattern>().Single().Pattern.UnitScale.Should().Be(3);
        mapped.OfType<PdfFillRectLinearGradient>().Single().Gradient.Should().Be(MappedGradient());
        mapped.OfType<PdfStrokeRectLinearGradient>().Single().Gradient.Should().Be(MappedGradient());
        mapped.OfType<PdfFillEllipseLinearGradient>().Single().Gradient.Should().Be(MappedGradient());
        mapped.OfType<PdfStrokeEllipseLinearGradient>().Single().Gradient.Should().Be(MappedGradient());

        mapped.OfType<PdfStrokeRect>().Single().Should().Match<PdfStrokeRect>(stroke =>
            stroke.LineWidth == 1 &&
            stroke.Dash!.Segments.SequenceEqual(new double[] { 2, 4 }) &&
            stroke.Dash.Phase == 1);
        mapped.OfType<PdfStrokeEllipse>().Single().Should().Match<PdfStrokeEllipse>(stroke =>
            stroke.LineWidth == 1 &&
            stroke.Dash!.Segments.SequenceEqual(new double[] { 2, 4 }) &&
            stroke.Dash.Phase == 1);
    }

    [Fact]
    public void MapOps_MapsTextLinesTrianglesAndImagesWithoutLosingPayloadProperties()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var crop = new PdfImageSourceCrop(0.1, 0.2, 0.3, 0.4);
        var effects = new PdfImageColorEffects(true, 0.4, 0.2, -0.1);

        var mapped = Place(
            new PdfText(1, 2, 3, PdfFontFace.BoldItalic, Primary, "Text", "Aptos"),
            new PdfLine(1, 2, 3, 4, Primary, 0.5),
            new PdfLineLinearGradient(1, 2, 3, 4, Gradient, Secondary, 0.5),
            new PdfFilledTriangle(1, 2, 3, 4, 5, 6, Primary),
            new PdfImage(
                1,
                2,
                3,
                4,
                bytes,
                "image/png",
                30,
                PdfImageClipKind.Hexagon,
                0.7,
                crop,
                effects));

        mapped.OfType<PdfText>().Single().Should().Be(
            new PdfText(12, 134, 6, PdfFontFace.BoldItalic, Primary, "Text", "Aptos"));
        mapped.OfType<PdfLine>().Single().Should().Be(new PdfLine(12, 134, 16, 138, Primary, 1));
        mapped.OfType<PdfLineLinearGradient>().Single().Should().Be(
            new PdfLineLinearGradient(12, 134, 16, 138, MappedGradient(), Secondary, 1));
        mapped.OfType<PdfFilledTriangle>().Single().Should().Be(
            new PdfFilledTriangle(12, 134, 16, 138, 20, 142, Primary));

        var image = mapped.OfType<PdfImage>().Single();
        image.Should().Be(new PdfImage(
            12,
            134,
            6,
            8,
            bytes,
            "image/png",
            30,
            PdfImageClipKind.Hexagon,
            0.7,
            crop,
            effects));
        image.ImageBytes.Should().BeSameAs(bytes);
    }

    [Fact]
    public void MapOps_MapsEveryPathFamilyAndCubicControlPoint()
    {
        var contours = new[]
        {
            new PdfPathContour(
                new PdfPathPoint(1, 2),
                [
                    PdfPathSegment.LineTo(new PdfPathPoint(3, 4)),
                    PdfPathSegment.BezierTo(
                        new PdfPathPoint(5, 6),
                        new PdfPathPoint(7, 8),
                        new PdfPathPoint(9, 10)),
                ],
                true),
        };

        var mapped = Place(
            new PdfPath(contours, Primary, Secondary, 0.5, Dash),
            new PdfPathPattern(contours, Pattern, Secondary, 0.5, Dash),
            new PdfPathLinearGradient(contours, Gradient, Primary, Gradient, Secondary, 0.5, Dash));

        mapped.Should().HaveCount(3);
        foreach (var contoursResult in mapped.Select(PathContours))
        {
            var contour = contoursResult.Single();
            contour.Start.Should().Be(new PdfPathPoint(12, 134));
            contour.Segments[0].Should().Be(PdfPathSegment.LineTo(new PdfPathPoint(16, 138)));
            contour.Segments[1].Should().Be(PdfPathSegment.BezierTo(
                new PdfPathPoint(20, 142),
                new PdfPathPoint(24, 146),
                new PdfPathPoint(28, 150)));
            contour.Closed.Should().BeTrue();
        }

        mapped.OfType<PdfPath>().Single().StrokeDash.Should().BeEquivalentTo(
            new PdfDashPattern([2, 4], 1));
        mapped.OfType<PdfPathPattern>().Single().Pattern.UnitScale.Should().Be(3);
        mapped.OfType<PdfPathLinearGradient>().Single().Should().Match<PdfPathLinearGradient>(path =>
            path.FillGradient == MappedGradient() &&
            path.StrokeGradient == MappedGradient() &&
            path.StrokeWidth == 1);
    }

    [Fact]
    public void MapOps_MapsNestedGroupsEffectDimensionsAndRotationFlips()
    {
        var parameters = new PdfEffectParameters(
            Primary,
            0.8,
            Radius: 3,
            OffsetX: -2,
            OffsetY: 4,
            ReflectionGap: 5,
            ReflectionScaleX: 0.8,
            ReflectionScaleY: -0.7,
            BevelWidth: 6,
            BevelHeight: 7);
        var source = new PdfRotationGroup(
            5,
            6,
            25,
            [
                new PdfClipGroup(
                    1,
                    2,
                    3,
                    4,
                    [
                        new PdfOpacityGroup(
                            0.6,
                            [
                                new PdfEffectGroup(
                                    PdfEffectKind.Reflection,
                                    1,
                                    2,
                                    3,
                                    4,
                                    parameters,
                                    [new PdfFillRect(1, 2, 3, 4, Primary)]),
                            ]),
                    ]),
            ],
            FlipH: true,
            FlipV: true);

        var rotation = Place(source).OfType<PdfRotationGroup>().Single();
        rotation.Should().Match<PdfRotationGroup>(group =>
            group.CenterX == 20 &&
            group.CenterY == 142 &&
            group.RotationDegrees == 25 &&
            group.FlipH &&
            group.FlipV);

        var clip = rotation.Ops.OfType<PdfClipGroup>().Single();
        (clip.X, clip.Y, clip.Width, clip.Height).Should().Be((12, 134, 6, 8));
        clip.Ops.OfType<PdfOpacityGroup>().Single().Opacity.Should().Be(0.6);

        var effect = clip.Ops.OfType<PdfOpacityGroup>().Single().Ops.OfType<PdfEffectGroup>().Single();
        (effect.BoundsX, effect.BoundsY, effect.BoundsWidth, effect.BoundsHeight)
            .Should().Be((12, 134, 6, 8));
        effect.Parameters.Should().Be(parameters with
        {
            Radius = 6,
            OffsetX = -4,
            OffsetY = 8,
            ReflectionGap = 10,
            BevelWidth = 12,
            BevelHeight = 14,
        });
        effect.Ops.OfType<PdfFillRect>().Single().Should().Be(
            new PdfFillRect(12, 134, 6, 8, Primary));
    }

    [Fact]
    public void MapOps_CentersPortraitContentHorizontallyAndRejectsInvalidBounds()
    {
        var portrait = new PdfContentPage(50, 100, [new PdfFillRect(0, 0, 50, 100, Primary)]);

        var mapped = PdfContentPagePlacement.MapOps(portrait, 10, 20, 200, 200, 300);

        mapped.OfType<PdfFillRect>().Single().Should().Be(
            new PdfFillRect(60, 80, 100, 200, Primary));
        PdfContentPagePlacement.MapOps(portrait, 0, 0, 0, 100, 100).Should().BeEmpty();
        PdfContentPagePlacement.MapOps(
                new PdfContentPage(0, 100, []),
                0,
                0,
                100,
                100,
                100)
            .Should().BeEmpty();
    }

    [Fact]
    public void DrawOpVariantGuard_RequiresPlacementCoverageToStayExhaustive()
    {
        var variants = typeof(PdfDrawOp).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(PdfDrawOp).IsAssignableFrom(type))
            .Select(type => type.Name);

        variants.Should().BeEquivalentTo(
            nameof(PdfFillRect),
            nameof(PdfFillRectPattern),
            nameof(PdfFillRectLinearGradient),
            nameof(PdfStrokeRect),
            nameof(PdfStrokeRectLinearGradient),
            nameof(PdfFillEllipse),
            nameof(PdfFillEllipsePattern),
            nameof(PdfFillEllipseLinearGradient),
            nameof(PdfStrokeEllipse),
            nameof(PdfStrokeEllipseLinearGradient),
            nameof(PdfText),
            nameof(PdfLine),
            nameof(PdfLineLinearGradient),
            nameof(PdfFilledTriangle),
            nameof(PdfPath),
            nameof(PdfPathPattern),
            nameof(PdfPathLinearGradient),
            nameof(PdfRotationGroup),
            nameof(PdfClipGroup),
            nameof(PdfOpacityGroup),
            nameof(PdfEffectGroup),
            nameof(PdfImage));
    }

    private static IReadOnlyList<PdfDrawOp> Place(params PdfDrawOp[] ops) =>
        PdfContentPagePlacement.MapOps(
            new PdfContentPage(100, 50, ops),
            destinationX: 10,
            destinationY: 20,
            destinationWidth: 200,
            destinationHeight: 200,
            destinationPageHeight: 300);

    private static PdfLinearGradient MappedGradient() =>
        Gradient with { StartX = 10, StartY = 130, EndX = 30, EndY = 140 };

    private static (double X, double Y, double Width, double Height) Bounds(PdfDrawOp op) =>
        op switch
        {
            PdfFillRect value => (value.X, value.Y, value.Width, value.Height),
            PdfFillRectPattern value => (value.X, value.Y, value.Width, value.Height),
            PdfFillRectLinearGradient value => (value.X, value.Y, value.Width, value.Height),
            PdfStrokeRect value => (value.X, value.Y, value.Width, value.Height),
            PdfStrokeRectLinearGradient value => (value.X, value.Y, value.Width, value.Height),
            PdfFillEllipse value => (value.X, value.Y, value.Width, value.Height),
            PdfFillEllipsePattern value => (value.X, value.Y, value.Width, value.Height),
            PdfFillEllipseLinearGradient value => (value.X, value.Y, value.Width, value.Height),
            PdfStrokeEllipse value => (value.X, value.Y, value.Width, value.Height),
            PdfStrokeEllipseLinearGradient value => (value.X, value.Y, value.Width, value.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

    private static IReadOnlyList<PdfPathContour> PathContours(PdfDrawOp op) =>
        op switch
        {
            PdfPath value => value.Contours,
            PdfPathPattern value => value.Contours,
            PdfPathLinearGradient value => value.Contours,
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
}
