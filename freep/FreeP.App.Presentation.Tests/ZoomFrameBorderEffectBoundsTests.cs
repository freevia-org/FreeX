using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// The Zoom Format dialog's effect distances are converted to EMU by a <c>checked</c> multiply by
/// 12700. Only a lower bound was validated, so a large-but-finite entry passed every check and then
/// threw OverflowException straight out of the OK-click handler. Out-of-range input has to take the
/// dialog's ordinary invalid-value path instead.
/// </summary>
public sealed class ZoomFrameBorderEffectBoundsTests
{
    private const string HugeButFinite = "1e20";

    [Fact]
    public void TryParseFrameBorderShadow_BlurFarOutOfRange_ReportsInvalidInsteadOfThrowing()
    {
        var parsed = ZoomObjectPropertiesPlanner.TryParseFrameBorderShadow(
            "FF0000", "50", HugeButFinite, "0", "0", enabled: true, out var normalized);

        parsed.Should().BeFalse();
        normalized.Should().BeNull();
    }

    [Fact]
    public void TryParseFrameBorderShadow_DistanceFarOutOfRange_ReportsInvalidInsteadOfThrowing()
    {
        var parsed = ZoomObjectPropertiesPlanner.TryParseFrameBorderShadow(
            "FF0000", "50", "0", HugeButFinite, "0", enabled: true, out var normalized);

        parsed.Should().BeFalse();
        normalized.Should().BeNull();
    }

    [Fact]
    public void TryParseFrameBorderGlow_RadiusFarOutOfRange_ReportsInvalidInsteadOfThrowing()
    {
        var parsed = ZoomObjectPropertiesPlanner.TryParseFrameBorderGlow(
            "FF0000", "50", HugeButFinite, enabled: true, out var normalized);

        parsed.Should().BeFalse();
        normalized.Should().BeNull();
    }

    [Fact]
    public void TryParseFrameBorderSoftEdge_RadiusFarOutOfRange_ReportsInvalidInsteadOfThrowing()
    {
        var parsed = ZoomObjectPropertiesPlanner.TryParseFrameBorderSoftEdge(
            HugeButFinite, enabled: true, out var normalized);

        parsed.Should().BeFalse();
        normalized.Should().BeNull();
    }

    [Fact]
    public void TryParseFrameBorderReflection_BlurFarOutOfRange_ReportsInvalidInsteadOfThrowing()
    {
        var parsed = ZoomObjectPropertiesPlanner.TryParseFrameBorderReflection(
            "50", "0", "0", "50", HugeButFinite, "100", enabled: true, out var normalized);

        parsed.Should().BeFalse();
        normalized.Should().BeNull();
    }

    [Fact]
    public void TryParseFrameBorderReflection_DistanceFarOutOfRange_ReportsInvalidInsteadOfThrowing()
    {
        var parsed = ZoomObjectPropertiesPlanner.TryParseFrameBorderReflection(
            "50", HugeButFinite, "0", "50", "0", "100", enabled: true, out var normalized);

        parsed.Should().BeFalse();
        normalized.Should().BeNull();
    }

    [Fact]
    public void TryParseFrameBorderShadow_OrdinaryValues_StillParse()
    {
        // The bound must not reject effects a user would actually author.
        var parsed = ZoomObjectPropertiesPlanner.TryParseFrameBorderShadow(
            "FF0000", "50", "8", "4", "45", enabled: true, out var normalized);

        parsed.Should().BeTrue();
        normalized.Should().NotBeNull();
    }
}
