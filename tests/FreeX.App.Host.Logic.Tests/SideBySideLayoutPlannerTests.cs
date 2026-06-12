using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class SideBySideLayoutPlannerTests
{
    [Fact]
    public void Tile_SplitsTheWorkAreaIntoTwoEqualHalvesLeftAndRight()
    {
        var (primary, secondary) = SideBySideLayoutPlanner.Tile(workAreaWidth: 1920, workAreaHeight: 1080);

        primary.Left.Should().Be(0);
        primary.Top.Should().Be(0);
        primary.Width.Should().BeApproximately(960, 0.001);
        primary.Height.Should().BeApproximately(1080, 0.001);

        secondary.Left.Should().BeApproximately(960, 0.001);
        secondary.Top.Should().Be(0);
        secondary.Width.Should().BeApproximately(960, 0.001);
        secondary.Height.Should().BeApproximately(1080, 0.001);
    }

    [Fact]
    public void Tile_CoversTheFullWorkAreaWidthWithoutOverlap()
    {
        var (primary, secondary) = SideBySideLayoutPlanner.Tile(1366, 768);

        (primary.Left + primary.Width).Should().BeApproximately(secondary.Left, 0.001,
            "the two halves should abut without a gap or overlap");
        (secondary.Left + secondary.Width).Should().BeApproximately(1366, 0.001);
        primary.Height.Should().BeApproximately(768, 0.001);
        secondary.Height.Should().BeApproximately(768, 0.001);
    }

    [Fact]
    public void Tile_NonPositiveWorkArea_FallsBackToPositiveSizes()
    {
        var (primary, secondary) = SideBySideLayoutPlanner.Tile(0, -5);

        primary.Width.Should().BeGreaterThan(0);
        primary.Height.Should().BeGreaterThan(0);
        secondary.Width.Should().BeGreaterThan(0);
        secondary.Height.Should().BeGreaterThan(0);
        secondary.Left.Should().Be(primary.Width);
    }
}
