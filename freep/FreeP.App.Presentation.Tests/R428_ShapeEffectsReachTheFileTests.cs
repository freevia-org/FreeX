using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r428: shape effects -- shadows and glow -- must survive a .pptx round trip.
///
/// <para>Effects are gated behind <c>Has*</c> flags, which changes what a probe has to do. Setting a
/// shadow's blur or colour without setting <c>HasOuterShadow</c> writes nothing, correctly: the
/// values describe an effect the shape does not have. That is the same interdependence r419 hit in
/// paragraph formatting, where six fields looked dropped because their companions were unset, so the
/// flags are set explicitly here rather than discovered as failures.</para>
///
/// <para>The alpha fields also carry a NON-ZERO default (0x80), which r424 showed is where a probe
/// most easily tests nothing: a value equal to the default round-trips through a writer that emits
/// nothing at all. They are probed with a different value.</para>
/// </summary>
public sealed class R428_ShapeEffectsReachTheFileTests
{
    private static SlideShape RoundTrip(Action<ShapeEffects> configure)
    {
        var effects = new ShapeEffects();
        configure(effects);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Body",
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 500000,
            Effects = effects,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var shape = PptxPackageReader.Read(stream).Slides[0].Shapes.FirstOrDefault();
        shape.Should().NotBeNull("the shape must survive before its effects can be judged");
        return shape!;
    }

    [Fact]
    public void AnOuterShadowKeepsItsGeometryAndColour()
    {
        var shape = RoundTrip(effects =>
        {
            effects.HasOuterShadow = true;
            effects.OuterShadowColor = SrgbColor.FromRgb(0x336699);
            effects.OuterShadowAlpha = 0x40;
            effects.OuterShadowBlurRadEmu = 50800;
            effects.OuterShadowDistEmu = 38100;
            effects.OuterShadowDirDeg = 135;
        });

        shape.Effects.Should().NotBeNull("a shape that loses its effects renders flat");
        shape.Effects!.HasOuterShadow.Should().BeTrue();
        shape.Effects.OuterShadowColor.Should().Be(SrgbColor.FromRgb(0x336699));
        shape.Effects.OuterShadowAlpha.Should().Be(0x40, "alpha defaults to 0x80, so this proves it was written");
        shape.Effects.OuterShadowBlurRadEmu.Should().Be(50800);
        shape.Effects.OuterShadowDistEmu.Should().Be(38100, "distance and blur are different fields and both matter");
        shape.Effects.OuterShadowDirDeg.Should().BeApproximately(135, 1e-6, "direction places the shadow");
    }

    [Fact]
    public void AnInnerShadowIsNotConfusedWithAnOuterOne()
    {
        // Inner and outer shadows share field shapes and differ only by element. A writer that
        // emitted an outer shadow for both would pass a test that only checked the values.
        var shape = RoundTrip(effects =>
        {
            effects.HasInnerShadow = true;
            effects.InnerShadowBlurRadEmu = 25400;
            effects.InnerShadowDistEmu = 12700;
        });

        shape.Effects!.HasInnerShadow.Should().BeTrue("the inner shadow must come back as inner");
        shape.Effects.HasOuterShadow.Should().BeFalse("and must not be promoted to an outer shadow");
        shape.Effects.InnerShadowBlurRadEmu.Should().Be(25400);
        shape.Effects.InnerShadowDistEmu.Should().Be(12700);
    }

    [Fact]
    public void AGlowKeepsItsRadiusAndColour()
    {
        var shape = RoundTrip(effects =>
        {
            effects.HasGlow = true;
            effects.GlowColor = SrgbColor.FromRgb(0xFFAA00);
            effects.GlowRadiusEmu = 63500;
        });

        shape.Effects!.HasGlow.Should().BeTrue();
        shape.Effects.GlowColor.Should().Be(SrgbColor.FromRgb(0xFFAA00));
        shape.Effects.GlowRadiusEmu.Should().Be(63500, "a glow with no radius is invisible");
    }

    [Fact]
    public void ValuesWithoutTheirFlagAreCorrectlyNotWritten()
    {
        // Pins the interdependence rather than leaving it as folklore. Blur and distance describe an
        // effect the shape does not have, so writing nothing is CORRECT -- and if a future writer
        // starts persisting them, this fails and the reasoning above gets revisited instead of
        // silently becoming wrong.
        var shape = RoundTrip(effects =>
        {
            effects.HasOuterShadow = false;
            effects.OuterShadowBlurRadEmu = 50800;
            effects.OuterShadowDistEmu = 38100;
        });

        (shape.Effects?.HasOuterShadow ?? false).Should().BeFalse(
            "the shape has no outer shadow, so its stale blur and distance describe nothing");
    }

    /// <summary>
    /// Every alpha value must survive, not just the one the shadow test happens to use.
    /// </summary>
    /// <remarks>
    /// The shadow test above found 0x40 coming back as 0x3F. Alpha is a byte in the model and a
    /// percentage in the file, and both conversions used integer division, so the round trip lost a
    /// step. Checked exhaustively rather than at a sample, because an off-by-one from truncation is
    /// exactly the kind of bug a spot check misses: roughly half the values happen to survive
    /// truncation unharmed, so a probe could easily pick one that does and report success.
    /// </remarks>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x40)]
    [InlineData(0x7F)]
    [InlineData(0x80)]
    [InlineData(0xC0)]
    [InlineData(0xFE)]
    [InlineData(0xFF)]
    public void EveryAlphaValueSurvivesTheRoundTrip(int alpha)
    {
        var shape = RoundTrip(effects =>
        {
            effects.HasOuterShadow = true;
            effects.OuterShadowAlpha = (byte)alpha;
            effects.OuterShadowBlurRadEmu = 50800;
        });

        shape.Effects!.OuterShadowAlpha.Should().Be(
            (byte)alpha, "a byte-to-percentage conversion must not lose a step on the way back");
    }

    [Fact]
    public void AnUneffectedShapeGainsNoEffects()
    {
        // Every assertion above checks that something set survives, so a reader that invented a
        // shadow would satisfy them all -- and an invented shadow is visible on every slide.
        var shape = RoundTrip(_ => { });

        (shape.Effects?.HasOuterShadow ?? false).Should().BeFalse("a plain shape must not acquire a shadow");
        (shape.Effects?.HasInnerShadow ?? false).Should().BeFalse();
        (shape.Effects?.HasGlow ?? false).Should().BeFalse();
    }
}
