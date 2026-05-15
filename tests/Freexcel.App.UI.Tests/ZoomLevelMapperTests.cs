using FluentAssertions;
using Freexcel.App.UI;

namespace Freexcel.App.UI.Tests;

public sealed class ZoomLevelMapperTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(10, 10)]
    [InlineData(25, 25)]
    [InlineData(400, 400)]
    [InlineData(500, 400)]
    public void ClampZoomPercent_UsesExcelZoomRange(double input, double expected)
    {
        ZoomLevelMapper.ClampZoomPercent(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(400)]
    public void SliderMapping_RoundTripsCommonExcelZoomPresets(double zoomPercent)
    {
        var slider = ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);

        ZoomLevelMapper.SliderToZoomPercent(slider).Should().BeApproximately(zoomPercent, 0.0001);
    }

    [Theory]
    [InlineData("25%", 25)]
    [InlineData(" 150 ", 150)]
    [InlineData("400", 400)]
    public void TryParseZoomPercent_AcceptsValidExcelPercentText(string text, double expected)
    {
        ZoomLevelMapper.TryParseZoomPercent(text, out var zoomPercent).Should().BeTrue();
        zoomPercent.Should().Be(expected);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("401")]
    [InlineData("abc")]
    public void TryParseZoomPercent_RejectsOutOfRangeOrInvalidText(string text)
    {
        ZoomLevelMapper.TryParseZoomPercent(text, out _).Should().BeFalse();
    }
}
