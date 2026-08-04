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
    public void MapleMuffins_UsesWordCadenceAndMeasuredIndexedPalette()
    {
        PageBorderArtVisualPlanner.TryBuildMapleMuffinsFrame(2, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Polygons.Should().BeEmpty();
        plan.Fills.Should().HaveCount(41004);
        plan.Fills[0].Should().Be(new PageBorderArtFillRectangle(42, 33, 2, 1, 0xEF, 0xEF, 0xEF));
        plan.Fills.Should().Contain(fill => fill.Red == 0xFE && fill.Green == 0x7F && fill.Blue == 0);
        plan.Fills.Should().Contain(fill => fill.Red == 0xBE && fill.Green == 0x41 && fill.Blue == 0);
        plan.Fills.Should().Contain(fill => fill.Red == 0x14 && fill.Green == 0x0A && fill.Blue == 0x04);
        plan.Fills.Should().NotContain(fill => fill.Red == 0xFF && fill.Green == 0xFF && fill.Blue == 0xFF);
    }

    [Fact]
    public void CakeSlice_UsesWordCadenceAndMeasuredMaterialMask()
    {
        PageBorderArtVisualPlanner.TryBuildCakeSliceFrame(3, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Polygons.Should().BeEmpty();
        plan.Fills.Should().HaveCount(18972);
        plan.Fills[0].Should().Be(new PageBorderArtFillRectangle(52, 32, 4, 1, 0, 0, 0));
        plan.Fills.Should().Contain(fill => fill.Red == 0xFF && fill.Green == 0xEE && fill.Blue == 0xCA);
        plan.Fills.Should().Contain(fill => fill.Red == 0xFF && fill.Green == 0x99 && fill.Blue == 0xC2);
        plan.Fills.Should().NotContain(fill => fill.Red == 0xFF && fill.Green == 0xFF && fill.Blue == 0xFF);
    }

    [Fact]
    public void BirdsFlight_UsesWordCadenceAndMeasuredNavyMask()
    {
        PageBorderArtVisualPlanner.TryBuildBirdsFlightFrame(35, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Polygons.Should().BeEmpty();
        plan.Fills.Should().HaveCount(10710);
        plan.Fills[0].Should().Be(new PageBorderArtFillRectangle(53, 33, 1, 1, 0xAE, 0xAF, 0xC6));
        plan.Fills.Should().Contain(fill => fill.Red == 0x04 && fill.Green == 0x07 && fill.Blue == 0x50);
        plan.Fills.Should().Contain(fill => fill.Red == 0x62 && fill.Green == 0x64 && fill.Blue == 0x92);
        plan.Fills.Should().NotContain(fill => fill.Red == 0xFF && fill.Green == 0xFF && fill.Blue == 0xFF);
    }

    [Fact]
    public void PaintedEggs_UsesWordCadenceAndMeasuredMaterialMask()
    {
        PageBorderArtVisualPlanner.TryBuildPaintedEggsFrame(66, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Polygons.Should().BeEmpty();
        plan.Fills.Should().HaveCount(23562);
        plan.Fills[0].Should().Be(new PageBorderArtFillRectangle(44, 32, 1, 1, 0x55, 0x55, 0x55));
        plan.Fills.Should().Contain(fill => fill.Red == 0 && fill.Green == 0 && fill.Blue == 0);
        plan.Fills.Should().Contain(fill => fill.Red == 0xAA && fill.Green == 0xAA && fill.Blue == 0xAA);
        plan.Fills.Should().NotContain(fill => fill.Red == 0xFF && fill.Green == 0xFF && fill.Blue == 0xFF);
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
    public void IceCreamCones_UsesWordCadenceAndMeasuredIndexedPalette()
    {
        PageBorderArtVisualPlanner.TryBuildIceCreamConesFrame(5, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Polygons.Should().BeEmpty();
        plan.Fills.Should().HaveCount(13056);
        plan.Fills[0].Should().Be(new PageBorderArtFillRectangle(46, 32, 1, 1, 0xEF, 0xEF, 0xEF));
        plan.Fills.Should().Contain(fill => fill.Red == 0xFE && fill.Green == 0xFE && fill.Blue == 0x7F);
        plan.Fills.Should().Contain(fill => fill.Red == 0xFC && fill.Green == 0x7F && fill.Blue == 0xFC);
        plan.Fills.Should().Contain(fill => fill.Red == 0x57 && fill.Green == 0x3F && fill.Blue == 0x27);
        plan.Fills.Should().NotContain(fill => fill.Red == 0xFF && fill.Green == 0xFF && fill.Blue == 0xFF);
    }

    [Fact]
    public void People_UsesWordCadenceAndMeasuredOpaqueInteriorMask()
    {
        PageBorderArtVisualPlanner.TryBuildPeopleFrame(84, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Polygons.Should().BeEmpty();
        plan.Fills.Should().HaveCount(18462);
        plan.Fills[0].Should().Be(new PageBorderArtFillRectangle(44, 35, 1, 1, 0xEF, 0xEF, 0xEF));
        plan.Fills.Should().Contain(fill => fill.Red == 0 && fill.Green == 0 && fill.Blue == 0);
        plan.Fills.Should().Contain(fill => fill.Red == 0x80 && fill.Green == 0x80 && fill.Blue == 0x80);
        plan.Fills.Should().Contain(fill => fill.Red == 0xFF && fill.Green == 0xFF && fill.Blue == 0xFF);
    }

    [Fact]
    public void FlowersRoses_UsesWordCadenceAndMeasuredIndexedPalette()
    {
        PageBorderArtVisualPlanner.TryBuildFlowersRosesFrame(38, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Polygons.Should().BeEmpty();
        plan.Fills.Should().HaveCount(41208);
        plan.Fills[0].Should().Be(new PageBorderArtFillRectangle(35, 32, 1, 1, 0xB3, 0xB2, 0xB3));
        plan.Fills.Should().Contain(fill => fill.Red == 0xE7 && fill.Green == 0x69 && fill.Blue == 0xD1);
        plan.Fills.Should().Contain(fill => fill.Red == 0x1A && fill.Green == 0xB3 && fill.Blue == 0);
        plan.Fills.Should().Contain(fill => fill.Red == 0xA8 && fill.Green == 0x4D && fill.Blue == 0x98);
        plan.Fills.Should().NotContain(fill => fill.Red == 0xFE && fill.Green == 0xFE && fill.Blue == 0xFE);
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
    public void WeavingRibbon_UsesContinuousRailsAndMeasuredMaterialSprites()
    {
        PageBorderArtVisualPlanner.TryBuildWeavingRibbonFrame(95, 3, 816, 1056, 32, out var plan)
            .Should().BeTrue();

        plan.Fills.Take(4).Should().Equal(
            new PageBorderArtFillRectangle(32, 32, 752, 32, 0, 0, 0),
            new PageBorderArtFillRectangle(32, 992, 752, 32, 0, 0, 0),
            new PageBorderArtFillRectangle(32, 32, 32, 992, 0, 0, 0),
            new PageBorderArtFillRectangle(752, 32, 32, 992, 0, 0, 0));
        plan.Fills.Should().HaveCount(8972);
        plan.Fills[4].Should().Be(new PageBorderArtFillRectangle(67, 32, 1, 1, 0xC0, 0xC0, 0xC0));
        plan.Fills[5].Should().Be(new PageBorderArtFillRectangle(68, 32, 6, 1, 0xFF, 0xFF, 0xFF));
        plan.Polygons.Should().BeEmpty();
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

        plan.Fills.Take(4).Should().Equal(
            new PageBorderArtFillRectangle(32, 32, 752, 32, 0, 0, 0),
            new PageBorderArtFillRectangle(32, 992, 752, 32, 0, 0, 0),
            new PageBorderArtFillRectangle(32, 32, 32, 992, 0, 0, 0),
            new PageBorderArtFillRectangle(752, 32, 32, 992, 0, 0, 0));
        plan.Fills.Should().HaveCount(3573);
        plan.Fills[4].Should().Be(new PageBorderArtFillRectangle(90, 36, 10, 1, 0xFF, 0xFF, 0xFF));
        plan.Fills[3313].Should().Be(new PageBorderArtFillRectangle(52, 36, 1, 1, 0xFF, 0xFF, 0xFF));
        plan.Polygons.Should().BeEmpty();
    }

    [Fact]
    public void TinyFrame_IsRecognizedButProducesNoMotifs()
    {
        PageBorderArtVisualPlanner.TryBuildApplesFrame(1, 3, 40, 40, 20, out var motifs)
            .Should().BeTrue();
        motifs.Should().BeEmpty();
    }
}
