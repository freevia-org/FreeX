namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r567: a WordArt warp's amplitude comes from a custom-geometry ADJUST GUIDE
/// (<c>&lt;a:gd name="adj" fmla="val 12500"/&gt;</c>), read verbatim from the file, so the token
/// after <c>val</c> is file-controlled. <c>TryReadGuideValue</c> parses it with no finite check.
///
/// <para>The consumer looks like it defends: <c>Math.Clamp(guideValue / 50000.0, 0.1, 2.0)</c>.
/// It bounds Infinity correctly -- but Math.Clamp PASSES NaN THROUGH, so <c>fmla="val NaN"</c>
/// yields a NaN amplitude scale that reaches warp geometry. That is the fourth appearance of this
/// exact trap, after r547's ParseVmlOpacity, r548's colour alpha and r555's ink stroke width, and
/// it is why "there is a clamp here" is never an answer to "is this guarded".</para>
///
/// <para>Direct sibling of r560, which guarded connection-site guide tokens in the same custom
/// geometry -- a different guide list, in a different project, reached by reading the r565 census
/// rather than by following that fix.</para>
/// </summary>
public sealed class R567_WarpAdjustGuidesAreFiniteTests
{
    private static double ScaleFor(string formula) =>
        WordArtWarpPlanner.GetAdjustAmplitudeScale([("adj", formula)]);

    [Theory]
    [InlineData("val NaN")]
    [InlineData("val Infinity")]
    [InlineData("val -Infinity")]
    [InlineData("val 1e400")]
    public void An_unusable_adjust_guide_yields_a_usable_amplitude(string formula)
    {
        double.IsFinite(ScaleFor(formula)).Should().BeTrue("the scale was " + ScaleFor(formula));
    }

    [Fact]
    public void An_ordinary_adjust_guide_still_sets_the_amplitude()
    {
        // Non-vacuity: 12500/50000 is 0.25, inside the clamp band, so a guard that rejected every
        // guide would fall back to 1.0 and fail here rather than silently flattening every warp.
        ScaleFor("val 12500").Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void An_unreadable_guide_still_falls_back_as_it_always_did()
    {
        // Pins the pre-existing contract the fix reuses: a formula that is not "val <number>"
        // leaves the amplitude at its default.
        ScaleFor("pin 0 adj 10000").Should().Be(1.0);
        ScaleFor("val notanumber").Should().Be(1.0);
    }
}
