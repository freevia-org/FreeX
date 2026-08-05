using FreeP.Core.Model;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class ZoomFrameBorderPatternCatalogTests
{
    [Theory]
    [InlineData("pct0")]
    [InlineData("PCT0")]
    [InlineData("pct100")]
    [InlineData("PCT100")]
    public void PowerPointExtremePatternPresets_AreAcceptedAndCanonicalized(string value)
    {
        ZoomFrameBorderPatternCatalog.IsSupported(value).Should().BeTrue();
        ZoomFrameBorderPatternCatalog.Normalize(value).Should().Be(value.ToLowerInvariant());
    }

    [Theory]
    [InlineData("pct0")]
    [InlineData("pct100")]
    public void ZoomPatternPlanner_AcceptsExtremePresets(string preset)
    {
        ZoomObjectPropertiesPlanner.TryParseFrameBorderPattern(
            preset, "112233", "445566", enabled: true, out var pattern).Should().BeTrue();
        pattern.Should().Be(new ZoomFrameBorderPattern(preset, "112233", "445566"));
    }
}
