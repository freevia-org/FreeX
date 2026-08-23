namespace FreeP.App.Compositor.Tests;

public sealed class PresentationCaptionPaintPlannerTests
{
    [Fact]
    public void Resolve_ParsesCaptionColorAndUsesOpaqueAlphaWhenOpacityIsMissing()
    {
        PresentationCaptionPaintPlanner.Resolve("123456", null, fallbackToWhite: true)
            .Should().Be(new PresentationCaptionPaint(255, 0x12, 0x34, 0x56));
    }

    [Theory]
    [InlineData(-1.0, 0)]
    [InlineData(0.5, 128)]
    [InlineData(2.0, 255)]
    public void Resolve_ClampsAndRoundsOpacity(double opacity, byte expectedAlpha)
    {
        PresentationCaptionPaintPlanner.Resolve("ABCDEF", opacity, fallbackToWhite: false)
            .Should().Be(new PresentationCaptionPaint(expectedAlpha, 0xAB, 0xCD, 0xEF));
    }

    [Fact]
    public void Resolve_UsesWhiteOnlyForBlankForegroundWithExplicitOpacity()
    {
        PresentationCaptionPaintPlanner.Resolve(" ", 0.25, fallbackToWhite: true)
            .Should().Be(new PresentationCaptionPaint(64, 255, 255, 255));
        PresentationCaptionPaintPlanner.Resolve(" ", null, fallbackToWhite: true)
            .Should().BeNull();
        PresentationCaptionPaintPlanner.Resolve(null, 0.25, fallbackToWhite: false)
            .Should().BeNull();
    }

    [Fact]
    public void Resolve_InvalidColorReturnsNullSoTheRendererKeepsInheritedCaptionPaint()
    {
        PresentationCaptionPaintPlanner.Resolve("not-a-color", 0.5, fallbackToWhite: true)
            .Should().BeNull();
    }
}
