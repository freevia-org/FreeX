using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class RendererUtilityPolicyTests
{
    [Theory]
    [InlineData("#1F4E79", "#FFFFFF")]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData(null, "#FFFFFF")]
    public void WordArtForegroundPolicyChoosesAReadableContrastColor(
        string? fillHex,
        string expectedForegroundHex)
    {
        var fill = SolidFill(fillHex);

        WordArtForegroundPolicy.ResolveColorHex(WordArtStyle.FillBlue, fill)
            .Should().Be(expectedForegroundHex);
    }

    [Fact]
    public void WordArtForegroundPolicyPreservesTheGlowGoldMaterialColor()
    {
        WordArtForegroundPolicy.ResolveColorHex(WordArtStyle.GlowGold, SolidFill("#000000"))
            .Should().Be("#D8BA66");
    }

    [Fact]
    public void SmartArtArrowheadPlannerReturnsPortableWingPoints()
    {
        var arrowhead = SmartArtConnectorArrowheadPlanner.Calculate(
            new SmartArtLayoutPoint(2, 5),
            new SmartArtLayoutPoint(12, 5));

        arrowhead.Should().Be(new SmartArtConnectorArrowheadPlan(
            IsVisible: true,
            Tip: new SmartArtLayoutPoint(12, 5),
            Left: new SmartArtLayoutPoint(6, 9),
            Right: new SmartArtLayoutPoint(6, 1)));
    }

    [Fact]
    public void SmartArtArrowheadPlannerSuppressesTheMinimumLengthSegment()
    {
        SmartArtConnectorArrowheadPlanner.Calculate(
                new SmartArtLayoutPoint(0, 0),
                new SmartArtLayoutPoint(0.001, 0))
            .IsVisible.Should().BeFalse();
    }

    private static DrawingObjectFillPlan SolidFill(string? colorHex) => new(
        DrawingObjectFillKind.Solid,
        colorHex,
        GradientAngle: 0,
        GradientStops: [],
        PatternPreset: null,
        PatternForegroundColorHex: null,
        PatternBackgroundColorHex: null);
}
