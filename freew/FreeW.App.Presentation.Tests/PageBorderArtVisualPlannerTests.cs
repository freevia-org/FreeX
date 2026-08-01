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
    public void TinyFrame_IsRecognizedButProducesNoMotifs()
    {
        PageBorderArtVisualPlanner.TryBuildApplesFrame(1, 3, 40, 40, 20, out var motifs)
            .Should().BeTrue();
        motifs.Should().BeEmpty();
    }
}
