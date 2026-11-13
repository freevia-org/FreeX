using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class WordArtWarpGeometryTests
{
    [Theory]
    [InlineData(WordArtWarp.Wave1)]
    [InlineData(WordArtWarp.Wave2)]
    [InlineData(WordArtWarp.Inflate)]
    [InlineData(WordArtWarp.Deflate)]
    [InlineData(WordArtWarp.InflateBottom)]
    [InlineData(WordArtWarp.ChevronUp)]
    [InlineData(WordArtWarp.ChevronDown)]
    [InlineData(WordArtWarp.FadeRight)]
    [InlineData(WordArtWarp.FadeLeft)]
    [InlineData(WordArtWarp.SlantUp)]
    [InlineData(WordArtWarp.SlantDown)]
    [InlineData(WordArtWarp.ArchUp)]
    [InlineData(WordArtWarp.ArchDown)]
    [InlineData(WordArtWarp.Circle)]
    [InlineData(WordArtWarp.Button)]
    public void Map_KeepsEveryPresetInsideTheTextRectangle(WordArtWarp warp)
    {
        WordArtWarpGeometry.Supports(warp).Should().BeTrue();
        for (var u = 0.0; u <= 1.0; u += 0.05)
        {
            for (var v = 0.0; v <= 1.0; v += 0.25)
            {
                var (x, y) = WordArtWarpGeometry.Map(warp, u, v);
                x.Should().BeInRange(-1e-9, 1 + 1e-9);
                y.Should().BeInRange(-1e-9, 1 + 1e-9);
            }
        }
    }

    [Fact]
    public void Envelope_FollowsThePresetGuidePathsAtDefaultAdjustValues()
    {
        WordArtWarpGeometry.Supports(WordArtWarp.None).Should().BeFalse();

        // textInflate (adj 18750): the guides bulge outward to the full height at the centre.
        WordArtWarpGeometry.Envelope(WordArtWarp.Inflate, 0).Should().Be((0.1875, 0.8125));
        WordArtWarpGeometry.Envelope(WordArtWarp.Inflate, 0.5).Should().Be((0, 1));

        // textDeflate pinches the centre by the same depth.
        WordArtWarpGeometry.Envelope(WordArtWarp.Deflate, 0.5).Should().Be((0.1875, 0.8125));

        // textChevron (adj 25000): the top guide peaks at the centre, the bottom one rises to meet it.
        WordArtWarpGeometry.Envelope(WordArtWarp.ChevronUp, 0.5).Should().Be((0, 0.75));
        WordArtWarpGeometry.Envelope(WordArtWarp.ChevronUp, 0).Should().Be((0.25, 1));

        // textSlantUp (adj 55556): both guides climb from left to right.
        var (slantLeftTop, _) = WordArtWarpGeometry.Envelope(WordArtWarp.SlantUp, 0);
        var (_, slantRightBottom) = WordArtWarpGeometry.Envelope(WordArtWarp.SlantUp, 1);
        slantLeftTop.Should().BeApproximately(0.55556, 1e-9);
        slantRightBottom.Should().BeApproximately(0.44444, 1e-9);

        // textWave1 (adj1 12500): a cubic wave that starts and ends at the amplitude and is mirrored by textWave2.
        WordArtWarpGeometry.Envelope(WordArtWarp.Wave1, 0).Top.Should().BeApproximately(0.125, 1e-9);
        WordArtWarpGeometry.Envelope(WordArtWarp.Wave1, 1).Top.Should().BeApproximately(0.125, 1e-9);
        WordArtWarpGeometry.Envelope(WordArtWarp.Wave1, 0.2).Top.Should().BeLessThan(0.125);
        WordArtWarpGeometry.Envelope(WordArtWarp.Wave1, 0.8).Top.Should().BeGreaterThan(0.125);
        WordArtWarpGeometry.Envelope(WordArtWarp.Wave2, 0.2).Top.Should().BeGreaterThan(0.125);
    }

    [Fact]
    public void Map_InterpolatesBetweenGuidesAndBendsArcsAroundTheEllipse()
    {
        WordArtWarpGeometry.Map(WordArtWarp.Inflate, 0, 0.5).Should().Be((0, 0.5));
        WordArtWarpGeometry.Map(WordArtWarp.Inflate, 0.5, 0).Should().Be((0.5, 0));

        // textArchDown runs along the bottom half: its middle sits at the bottom of the ellipse, glyph tops inward.
        var (bottomX, bottomY) = WordArtWarpGeometry.Map(WordArtWarp.ArchDown, 0.5, 1);
        bottomX.Should().BeApproximately(0.5, 1e-9);
        bottomY.Should().BeApproximately(1, 1e-9);
        WordArtWarpGeometry.Map(WordArtWarp.ArchDown, 0.5, 0).Y.Should().BeLessThan(1);

        // textArchUp runs along the top half from left to right.
        var (topX, topY) = WordArtWarpGeometry.Map(WordArtWarp.ArchUp, 0.5, 0);
        topX.Should().BeApproximately(0.5, 1e-9);
        topY.Should().BeApproximately(0, 1e-9);
        WordArtWarpGeometry.Map(WordArtWarp.ArchUp, 0, 0).X.Should().BeApproximately(0, 1e-9);
    }
}
