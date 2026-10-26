using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r485: an animation scale read from a file must be a usable number.
///
/// <para>FromX/ToX/ByX are copied verbatim out of the animation XML by PptxPackageReader, so their
/// values are chosen by the FILE. TryParseScaleValue ended with <c>scale >= 0</c>, which rejected
/// NaN and negatives only as a side effect of how those compare, and let POSITIVE infinity through:
/// .NET parses "Infinity" and overflows "1e999" to it. A deck carrying either produced an infinite
/// scale, which ResolveScaleAxes passed to the slide-show frame planner - the code that builds the
/// per-frame transform while a presentation is actually running.</para>
///
/// <para>Same family as r468 and r469, a third boundary where a non-finite double was accepted
/// because nothing had asked the question. Rejecting it makes the value fall back to the preset's
/// own default, which is how every other unreadable attribute is already handled.</para>
/// </summary>
public sealed class R485_AnimationScaleRejectsNonFiniteTests
{
    [Theory]
    [InlineData("120%", 1.2)]
    [InlineData("80%", 0.8)]
    [InlineData("120000", 1.2)]   // the raw OOXML form: 100000 == 100%
    [InlineData("25000", 0.25)]
    public void OrdinaryScalesStillParse(string raw, double expected)
    {
        // Narrowness first: the guard must not disturb either accepted spelling.
        AnimationAmountSemantics.TryParseScaleValue(raw, out var scale).Should().BeTrue();
        scale.Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("Infinity%")]
    [InlineData("1e999")]        // overflows to infinity rather than failing to parse
    [InlineData("1e999%")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    [InlineData("NaN%")]
    [InlineData("-50%")]
    public void AValueThatCannotBeAScaleIsRejected(string raw)
    {
        AnimationAmountSemantics.TryParseScaleValue(raw, out _).Should().BeFalse(
            "this value reaches the slide-show frame planner, which builds a live transform from it");
    }

    [Fact]
    public void ANonFiniteBehaviourFallsBackToThePresetDefault()
    {
        // The consequence that matters: a hostile or corrupt deck must not put an infinite scale
        // into playback. Falling back is what happens for any other unreadable attribute.
        var behavior = new AnimationScaleBehavior { ToX = "1e999", ToY = "1e999" };

        var (x, y) = AnimationAmountSemantics.ResolveScaleAxes(AnimationPreset.Grow, behavior);

        double.IsFinite(x).Should().BeTrue();
        double.IsFinite(y).Should().BeTrue();
        x.Should().BeApproximately(1.2, 1e-9, "Grow's own default stands in for the unusable value");
    }

    [Fact]
    public void AReadableBehaviourIsStillHonoured()
    {
        // The other half of narrowness: a real value must still win over the default.
        var behavior = new AnimationScaleBehavior { ToX = "400%", ToY = "400%" };

        var (x, y) = AnimationAmountSemantics.ResolveScaleAxes(AnimationPreset.Grow, behavior);

        x.Should().BeApproximately(4.0, 1e-9);
        y.Should().BeApproximately(4.0, 1e-9);
    }
}
