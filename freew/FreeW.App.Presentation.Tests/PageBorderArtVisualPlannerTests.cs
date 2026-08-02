using FluentAssertions;
using FreeW.App.Presentation.DocumentView;
using Xunit;

namespace FreeW.App.Presentation.Tests;

public sealed class PageBorderArtVisualPlannerTests
{
    [Fact]
    public void Apples_UsesWordArtSizeAndStretchesMotifsAcrossEachEdge()
    {
        PageBorderArtVisualPlanner.TryBuildApplesFrame(1, 3, 816, 1056, 32, out var motifs)
            .Should().BeTrue();

        motifs.Should().HaveCount(102);
        motifs[0].Should().Be(new PageBorderAppleMotif(32, 32, 32));
        motifs[22].Should().Be(new PageBorderAppleMotif(752, 32, 32));
        motifs[23].Should().Be(new PageBorderAppleMotif(32, 992, 32));
        motifs[45].Should().Be(new PageBorderAppleMotif(752, 992, 32));
        motifs.Should().Contain(motif =>
            motif.Xdip == 32 && Math.Abs(motif.Ydip - 65.103448) < 0.0001 && motif.SizeDip == 32);
        motifs.Should().Contain(motif =>
            motif.Xdip == 752 && Math.Abs(motif.Ydip - 958.896552) < 0.0001 && motif.SizeDip == 32);
    }

    [Fact]
    public void UnsupportedArtStyle_FallsBackToHostLineRendering()
    {
        PageBorderArtVisualPlanner.TryBuildApplesFrame(84, 3, 816, 1056, 32, out var motifs)
            .Should().BeFalse();
        motifs.Should().BeEmpty();
    }

    [Fact]
    public void ShadowedSquares_UsesTheSameWordArtCadence()
    {
        PageBorderArtVisualPlanner.TryBuildShadowedSquaresFrame(57, 3, 816, 1056, 32, out var motifs)
            .Should().BeTrue();

        motifs.Should().HaveCount(102);
        motifs[0].Should().Be(new PageBorderShadowedSquareMotif(32, 32, 32));
        motifs[22].Should().Be(new PageBorderShadowedSquareMotif(752, 32, 32));
        motifs[45].Should().Be(new PageBorderShadowedSquareMotif(752, 992, 32));
    }

    [Fact]
    public void MapleMuffins_UsesWordCadenceAndSharedOrangeWrapperGeometry()
    {
        PageBorderArtVisualPlanner.TryBuildMapleMuffinsFrame(2, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().BeEmpty();
        plan.Polygons.Should().HaveCount(816);
        plan.Polygons[0].Points.Take(4).Should().Equal(
            new PageBorderArtPoint(37, 45),
            new PageBorderArtPoint(35, 44),
            new PageBorderArtPoint(34, 41),
            new PageBorderArtPoint(35, 38));
        plan.Polygons[1].Red.Should().Be(0xFF);
        plan.Polygons[1].Green.Should().Be(0x80);
        plan.Polygons[2].Red.Should().Be(0xBF);
        plan.Polygons[184].Points[0].Should().Be(new PageBorderArtPoint(37, 1005));
    }

    [Fact]
    public void CakeSlice_UsesWordCadenceAndSharedCreamPinkLayerGeometry()
    {
        PageBorderArtVisualPlanner.TryBuildCakeSliceFrame(3, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().BeEmpty();
        plan.Polygons.Should().HaveCount(510);
        plan.Polygons[0].Points.Take(4).Should().Equal(
            new PageBorderArtPoint(39, 36),
            new PageBorderArtPoint(44, 34),
            new PageBorderArtPoint(49, 36),
            new PageBorderArtPoint(54, 32));
        plan.Polygons[1].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xFF && polygon.Green == 0xEE && polygon.Blue == 0xCA);
        plan.Polygons[2].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xFF && polygon.Green == 0x99 && polygon.Blue == 0xC2);
        plan.Polygons[115].Points[0].Should().Be(new PageBorderArtPoint(39, 996));
    }

    [Fact]
    public void BirdsFlight_UsesWordCadenceAndSharedNavySilhouette()
    {
        PageBorderArtVisualPlanner.TryBuildBirdsFlightFrame(35, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().BeEmpty();
        plan.Polygons.Should().HaveCount(102);
        plan.Polygons[0].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0x04 && polygon.Green == 0x07 && polygon.Blue == 0x50);
        plan.Polygons[0].Points.Take(4).Should().Equal(
            new PageBorderArtPoint(34, 35),
            new PageBorderArtPoint(39, 37),
            new PageBorderArtPoint(46, 48),
            new PageBorderArtPoint(49, 44));
        plan.Polygons[23].Points[0].Should().Be(new PageBorderArtPoint(34, 995));
    }

    [Fact]
    public void PaintedEggs_UsesWordCadenceAndOrderedMottledEggGeometry()
    {
        PageBorderArtVisualPlanner.TryBuildPaintedEggsFrame(66, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().BeEmpty();
        plan.Polygons.Should().HaveCount(918);
        plan.Polygons[0].Points[0].Should().Be(new PageBorderArtPoint(38, 56));
        plan.Polygons[1].Points.Take(3).Should().Equal(
            new PageBorderArtPoint(43, 32),
            new PageBorderArtPoint(50, 30),
            new PageBorderArtPoint(56, 34));
        plan.Polygons[2].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xFF && polygon.Green == 0xFF && polygon.Blue == 0xFF);
        plan.Polygons[207].Points[0].Should().Be(new PageBorderArtPoint(38, 1016));
    }

    [Fact]
    public void CandyCorn_UsesWordStaggeredCadenceAndExactSourcePalette()
    {
        PageBorderArtVisualPlanner.TryBuildCandyCornFrame(4, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().BeEmpty();
        plan.Polygons.Should().HaveCount(1272);
        plan.Polygons[0].Points[0].Should().Be(new PageBorderArtPoint(56, 33));
        plan.Polygons[1].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xF5 && polygon.Green == 0xC6 && polygon.Blue == 0x0A);
        plan.Polygons[2].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xFE && polygon.Green == 0x45 && polygon.Blue == 0x01);
        plan.Polygons[3].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xFF && polygon.Green == 0xFF && polygon.Blue == 0xFF);
    }

    [Fact]
    public void IceCreamCones_UsesWordCadenceAndExactSourcePalette()
    {
        PageBorderArtVisualPlanner.TryBuildIceCreamConesFrame(5, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().BeEmpty();
        plan.Polygons.Should().HaveCount(510);
        plan.Polygons[0].Points[0].Should().Be(new PageBorderArtPoint(41, 43));
        plan.Polygons[1].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0x60 && polygon.Green == 0x40 && polygon.Blue == 0x20);
        plan.Polygons[3].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xFF && polygon.Green == 0x80 && polygon.Blue == 0xFF);
        plan.Polygons[4].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xFF && polygon.Green == 0xFF && polygon.Blue == 0x80);
    }

    [Fact]
    public void People_UsesWordCadenceAndOrderedOutlineInteriorGeometry()
    {
        PageBorderArtVisualPlanner.TryBuildPeopleFrame(84, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().BeEmpty();
        plan.Polygons.Should().HaveCount(408);
        plan.Polygons[0].Points[0].Should().Be(new PageBorderArtPoint(48, 33));
        plan.Polygons[0].Red.Should().Be(0);
        plan.Polygons[1].Red.Should().Be(0xFF);
        plan.Polygons[2].Points[0].Should().Be(new PageBorderArtPoint(46, 41));
        plan.Polygons[3].Red.Should().Be(0xFF);
    }

    [Fact]
    public void FlowersRoses_UsesWordCadenceAndMeasuredSourcePalette()
    {
        PageBorderArtVisualPlanner.TryBuildFlowersRosesFrame(38, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().BeEmpty();
        plan.Polygons.Should().HaveCount(1326);
        plan.Polygons[0].Points[0].Should().Be(new PageBorderArtPoint(45, 46));
        plan.Polygons[3].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0x1A && polygon.Green == 0xB3 && polygon.Blue == 0);
        plan.Polygons[6].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xE9 && polygon.Green == 0x6A && polygon.Blue == 0xD3);
        plan.Polygons[7].Should().Match<PageBorderArtPolygon>(polygon =>
            polygon.Red == 0xA0 && polygon.Green == 0x49 && polygon.Blue == 0x91);
    }

    [Fact]
    public void Handmade2_UsesMeasuredDoubleWobbledFrame()
    {
        PageBorderArtVisualPlanner.TryBuildHandmade2Frame(160, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().BeEmpty();
        plan.Strokes.Should().HaveCount(8);
        plan.Strokes[0].StartXDip.Should().Be(36);
        plan.Strokes[0].StartYDip.Should().Be(37);
        plan.Strokes[0].EndXDip.Should().Be(779);
        plan.Strokes[0].WidthDip.Should().Be(3);
        plan.Strokes[4].StartXDip.Should().Be(44);
        plan.Strokes[4].StartYDip.Should().Be(45);
        plan.Strokes[4].EndXDip.Should().Be(772);
        plan.Strokes[4].WidthDip.Should().Be(2);
    }

    [Fact]
    public void ShorebirdTracks_UsesMeasuredAlternatingFootprintCadenceAndSharedSegments()
    {
        PageBorderArtVisualPlanner.TryBuildShorebirdTracksFrame(83, 3, 816, 1056, 32, out var motifs)
            .Should().BeTrue();

        motifs.Should().HaveCount(72);
        motifs[0].Should().Be(new PageBorderShorebirdTrackMotif(88, 54.5, 32, 0));
        motifs[15].CenterXDip.Should().BeApproximately(731.2, 0.0001);
        motifs[16].Should().Be(new PageBorderShorebirdTrackMotif(88, 1001.5, 32, 2));
        motifs[32].Should().Be(new PageBorderShorebirdTrackMotif(54.5, 86, 32, 3));
        motifs[52].Should().Be(new PageBorderShorebirdTrackMotif(761.5, 86, 32, 1));

        PageBorderArtVisualPlanner.BuildShorebirdTrackSegments(motifs[0]).Should().Equal(
            new PageBorderArtLineSegment(72, 54.5, 81, 54.5),
            new PageBorderArtLineSegment(88, 54.5, 104, 54.5),
            new PageBorderArtLineSegment(88, 54.5, 99, 46.5),
            new PageBorderArtLineSegment(88, 54.5, 99, 62.5));
    }

    [Fact]
    public void DecorativeArch_UsesMeasuredRailsAndSharedCornerStrokes()
    {
        PageBorderArtVisualPlanner.TryBuildDecorativeArchFrame(89, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().HaveCount(25);
        plan.Strokes.Should().HaveCount(16);
        plan.Fills[0].Should().Be(new PageBorderArtFillRectangle(48, 40, 720, 1, 0x33, 0x33, 0x33));
        plan.Fills[5].Should().Be(new PageBorderArtFillRectangle(48, 1000, 720, 1, 0x20, 0x20, 0x20));
        plan.Fills[11].Should().Be(new PageBorderArtFillRectangle(37, 48, 1, 960, 0, 0, 0));
        plan.Fills[21].Should().Be(new PageBorderArtFillRectangle(37, 32, 21, 32, 0, 0, 0));
        plan.Strokes[0].Should().Be(new PageBorderArtCubicStroke(
            38, 62,
            38, 40,
            58, 40,
            58, 62,
            10,
            0, 0, 0));
    }

    [Fact]
    public void Bats_UsesWordCadenceAndMeasuredSharedSilhouette()
    {
        PageBorderArtVisualPlanner.TryBuildBatsFrame(37, 3, 816, 1056, 32, out var motifs)
            .Should().BeTrue();

        motifs.Should().HaveCount(102);
        motifs[0].Should().Be(new PageBorderBatMotif(32, 32, 32));
        motifs[22].Should().Be(new PageBorderBatMotif(752, 32, 32));
        motifs[45].Should().Be(new PageBorderBatMotif(752, 992, 32));
        PageBorderArtVisualPlanner.BuildBatPolygon(motifs[0]).Take(3).Should().Equal(
            new PageBorderArtPoint(36, 39),
            new PageBorderArtPoint(35, 44),
            new PageBorderArtPoint(36, 47));
    }

    [Fact]
    public void WeavingRibbon_UsesContinuousRailsAndAlternatingDiagonalStripes()
    {
        PageBorderArtVisualPlanner.TryBuildWeavingRibbonFrame(95, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().Equal(
            new PageBorderArtFillRectangle(32, 32, 752, 32, 0, 0, 0),
            new PageBorderArtFillRectangle(32, 992, 752, 32, 0, 0, 0),
            new PageBorderArtFillRectangle(31, 32, 32, 992, 0, 0, 0),
            new PageBorderArtFillRectangle(752, 32, 32, 992, 0, 0, 0));
        plan.Polygons.Should().HaveCount(224);
        plan.Polygons[0].Points.Should().Equal(
            new PageBorderArtPoint(44, 63),
            new PageBorderArtPoint(44, 64),
            new PageBorderArtPoint(55, 64),
            new PageBorderArtPoint(76, 43),
            new PageBorderArtPoint(76, 32),
            new PageBorderArtPoint(65, 32));
        plan.Polygons[104].Points.Should().Equal(
            new PageBorderArtPoint(31, 136),
            new PageBorderArtPoint(42, 136),
            new PageBorderArtPoint(63, 167),
            new PageBorderArtPoint(63, 168),
            new PageBorderArtPoint(52, 168),
            new PageBorderArtPoint(31, 147));
        plan.Polygons[168].Points.Should().Equal(
            new PageBorderArtPoint(752, 149),
            new PageBorderArtPoint(763, 149),
            new PageBorderArtPoint(784, 180),
            new PageBorderArtPoint(784, 181),
            new PageBorderArtPoint(773, 181),
            new PageBorderArtPoint(752, 160));
    }

    [Fact]
    public void Papyrus_UsesMeasuredRailsCrossCadenceAndIsolatedCornerOrnaments()
    {
        PageBorderArtVisualPlanner.TryBuildPapyrusFrame(92, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().Equal(
            new PageBorderArtFillRectangle(32, 39, 752, 17, 0, 0, 0),
            new PageBorderArtFillRectangle(32, 1000, 752, 17, 0, 0, 0),
            new PageBorderArtFillRectangle(39, 32, 17, 992, 0, 0, 0),
            new PageBorderArtFillRectangle(760, 32, 17, 992, 0, 0, 0),
            new PageBorderArtFillRectangle(64, 43, 688, 9, 0xFF, 0xFF, 0xFF),
            new PageBorderArtFillRectangle(64, 1004, 688, 9, 0xFF, 0xFF, 0xFF),
            new PageBorderArtFillRectangle(43, 64, 9, 928, 0xFF, 0xFF, 0xFF),
            new PageBorderArtFillRectangle(764, 64, 9, 928, 0xFF, 0xFF, 0xFF));
        plan.Polygons.Should().HaveCount(208);
        plan.Polygons[0].Red.Should().Be(0x7F);
        plan.Polygons[0].Points.Should().HaveCount(10);
        plan.Polygons[0].Points[0].Should().Be(new PageBorderArtPoint(68, 47.5));
        plan.Polygons[0].Points[5].Should().Be(new PageBorderArtPoint(92, 47.5));
        plan.Polygons[1].Red.Should().Be(0);
        plan.Polygons[1].Points.Should().HaveCount(6);
        plan.Polygons[200].Points.Should().HaveCount(20);
        plan.Polygons[201].Points.Should().Equal(
            new PageBorderArtPoint(48, 41),
            new PageBorderArtPoint(55, 48),
            new PageBorderArtPoint(48, 55),
            new PageBorderArtPoint(41, 48));
    }

    [Fact]
    public void Vine_UsesBlackRailsDistributedLeafCellsAndIsolatedFlowerCorners()
    {
        PageBorderArtVisualPlanner.TryBuildVineFrame(47, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Should().Equal(
            new PageBorderArtFillRectangle(32, 32, 752, 32, 0, 0, 0),
            new PageBorderArtFillRectangle(32, 992, 752, 32, 0, 0, 0),
            new PageBorderArtFillRectangle(32, 32, 32, 992, 0, 0, 0),
            new PageBorderArtFillRectangle(752, 32, 32, 992, 0, 0, 0));
        plan.Polygons.Should().HaveCount(284);
        plan.Polygons[0].Points.Take(4).Should().Equal(
            new PageBorderArtPoint(64, 56),
            new PageBorderArtPoint(71, 56),
            new PageBorderArtPoint(77, 53),
            new PageBorderArtPoint(83, 47));
        plan.Polygons[0].Red.Should().Be(0xFF);
        plan.Polygons[264].Points.Should().Equal(
            new PageBorderArtPoint(48, 48),
            new PageBorderArtPoint(43, 42),
            new PageBorderArtPoint(48, 34),
            new PageBorderArtPoint(53, 42));
        plan.Polygons[268].Red.Should().Be(0xB2);
    }

    [Fact]
    public void TinyFrame_IsRecognizedButProducesNoMotifs()
    {
        PageBorderArtVisualPlanner.TryBuildApplesFrame(1, 3, 40, 40, 20, out var motifs)
            .Should().BeTrue();
        motifs.Should().BeEmpty();
    }
}
