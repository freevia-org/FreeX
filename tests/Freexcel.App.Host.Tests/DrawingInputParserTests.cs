using System.Globalization;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class DrawingInputParserTests
{
    [Theory]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.bmp", "image/bmp")]
    [InlineData("photo.gif", "image/gif")]
    [InlineData("photo.png", "image/png")]
    [InlineData("photo.unknown", "image/png")]
    public void GetImageContentType_MapsCommonImageExtensions(string fileName, string expected)
    {
        DrawingInputParser.GetImageContentType(fileName).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.125, "12.5")]
    [InlineData(0.1, "10")]
    [InlineData(0, "0")]
    public void FormatCropPercent_FormatsRatiosAsPercentText(double value, string expected)
    {
        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            DrawingInputParser.FormatCropPercent(value).Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
        }
    }

    [Theory]
    [InlineData("12.5", true, 0.125)]
    [InlineData("12.5%", true, 0.125)]
    [InlineData("99", true, 0.99)]
    [InlineData("100", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("abc", false, 0)]
    public void TryParseCropPercent_ParsesValidCropPercentages(string text, bool expected, double expectedValue)
    {
        var result = DrawingInputParser.TryParseCropPercent(text, out var value);

        result.Should().Be(expected);
        value.Should().BeApproximately(expectedValue, 0.000001);
    }

    [Theory]
    [InlineData("31,119,180", true, 31, 119, 180)]
    [InlineData(" 68, 68, 68 ", true, 68, 68, 68)]
    [InlineData("256,0,0", false, 0, 0, 0)]
    [InlineData("1,2", false, 0, 0, 0)]
    [InlineData("red", false, 0, 0, 0)]
    public void TryParseRgbColor_ParsesByteTriplets(string text, bool expected, byte r, byte g, byte b)
    {
        var result = DrawingInputParser.TryParseRgbColor(text, out var color);

        result.Should().Be(expected);
        if (expected)
            color.Should().Be(new CellColor(r, g, b));
    }
}
