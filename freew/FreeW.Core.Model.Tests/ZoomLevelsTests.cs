namespace FreeW.Core.Model.Tests;

public class ZoomLevelsTests
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(2.0, 2.0)]
    [InlineData(0.1, 0.5)]   // below min clamps up
    [InlineData(5.0, 2.0)]   // above max clamps down
    [InlineData(double.NaN, 1.0)]
    public void Clamp_KeepsFactorInRange(double input, double expected)
    {
        ZoomLevels.Clamp(input).Should().Be(expected);
    }

    [Fact]
    public void StepUp_AddsOneStep_AndStopsAtMax()
    {
        ZoomLevels.StepUp(1.0).Should().BeApproximately(1.1, 1e-9);
        ZoomLevels.StepUp(ZoomLevels.Max).Should().Be(ZoomLevels.Max);
    }

    [Fact]
    public void StepDown_SubtractsOneStep_AndStopsAtMin()
    {
        ZoomLevels.StepDown(1.0).Should().BeApproximately(0.9, 1e-9);
        ZoomLevels.StepDown(ZoomLevels.Min).Should().Be(ZoomLevels.Min);
    }

    [Theory]
    [InlineData(1.0, 100)]
    [InlineData(0.5, 50)]
    [InlineData(2.0, 200)]
    [InlineData(1.25, 125)]
    public void ToPercent_RoundsToWholePercent(double factor, int expected)
    {
        ZoomLevels.ToPercent(factor).Should().Be(expected);
    }

    [Theory]
    [InlineData(1.0, "100%")]
    [InlineData(0.5, "50%")]
    [InlineData(2.0, "200%")]
    [InlineData(double.NaN, "100%")]
    public void FormatPercent_UsesCanonicalClampedEditorText(double factor, string expected)
    {
        ZoomLevels.FormatPercent(factor).Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 1.0)]
    [InlineData(50, 0.5)]
    [InlineData(200, 2.0)]
    [InlineData(300, 2.0)]   // clamped
    public void FromPercent_ConvertsAndClamps(double percent, double expected)
    {
        ZoomLevels.FromPercent(percent).Should().Be(expected);
    }
}
