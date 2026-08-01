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
    public void TinyFrame_IsRecognizedButProducesNoMotifs()
    {
        PageBorderArtVisualPlanner.TryBuildApplesFrame(1, 3, 40, 40, 20, out var motifs)
            .Should().BeTrue();
        motifs.Should().BeEmpty();
    }
}
