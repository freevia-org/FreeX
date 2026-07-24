namespace FreeP.App.Compositor.Tests;

public sealed class PictureCropAuthoringPlannerTests
{
    [Fact]
    public void TryPlan_AcceptsVisibleCropAndPreservesFractions()
    {
        PictureCropAuthoringPlanner.TryPlan(0.1, 0.2, 0.3, 0.05, out var values).Should().BeTrue();
        values.Should().Be(new PictureCropValues(0.1, 0.2, 0.3, 0.05));
    }

    [Theory]
    [InlineData(-0.01, 0, 0, 0)]
    [InlineData(0.6, 0, 0.4, 0)]
    [InlineData(0, 0.75, 0, 0.25)]
    [InlineData(double.NaN, 0, 0, 0)]
    [InlineData(double.PositiveInfinity, 0, 0, 0)]
    public void TryPlan_RejectsInvalidOrEmptySource(double left, double top, double right, double bottom)
    {
        PictureCropAuthoringPlanner.TryPlan(left, top, right, bottom, out _).Should().BeFalse();
    }

    [Fact]
    public void Presets_ExposeResetAndInset()
    {
        PictureCropAuthoringPlanner.Reset().IsDefault.Should().BeTrue();
        PictureCropAuthoringPlanner.Inset().Should().Be(new PictureCropValues(0.1, 0.1, 0.1, 0.1));
    }
}
