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

    [Theory]
    [InlineData("320x180", true, 320, 180)]
    [InlineData("320 x 180", true, 320, 180)]
    [InlineData("12.5x8.25", true, 12.5, 8.25)]
    [InlineData("320", false, 0, 0)]
    [InlineData("x180", false, 0, 0)]
    [InlineData("320x", false, 0, 0)]
    [InlineData("wide x tall", false, 0, 0)]
    public void TryParseSize_ParsesWidthByHeightText(string text, bool expected, double expectedWidth, double expectedHeight)
    {
        var result = DrawingInputParser.TryParseSize(text, out var width, out var height);

        result.Should().Be(expected);
        width.Should().Be(expectedWidth);
        height.Should().Be(expectedHeight);
    }

    [Theory]
    [InlineData("10, 0, 10, 0", true, 0.1, 0, 0.1, 0)]
    [InlineData("10; 5; 10; 5", true, 0.1, 0.05, 0.1, 0.05)]
    [InlineData("50,0,50,0", false, 0, 0, 0, 0)]
    [InlineData("10,0,10", false, 0, 0, 0, 0)]
    [InlineData("abc", false, 0, 0, 0, 0)]
    public void TryParseCropPercents_ParsesFourVisibleCropPercentages(
        string text,
        bool expected,
        double expectedLeft,
        double expectedTop,
        double expectedRight,
        double expectedBottom)
    {
        var result = DrawingInputParser.TryParseCropPercents(text, out var left, out var top, out var right, out var bottom);

        result.Should().Be(expected);
        left.Should().BeApproximately(expectedLeft, 0.000001);
        top.Should().BeApproximately(expectedTop, 0.000001);
        right.Should().BeApproximately(expectedRight, 0.000001);
        bottom.Should().BeApproximately(expectedBottom, 0.000001);
    }

    [Theory]
    [InlineData("31,119,180; 180,210,240", true, 31, 119, 180, 180, 210, 240)]
    [InlineData("31,119,180", false, 0, 0, 0, 0, 0, 0)]
    [InlineData("red; blue", false, 0, 0, 0, 0, 0, 0)]
    public void TryParseGradientColors_ParsesTwoRgbColorsSeparatedBySemicolon(
        string text,
        bool expected,
        byte startR,
        byte startG,
        byte startB,
        byte endR,
        byte endG,
        byte endB)
    {
        var result = DrawingInputParser.TryParseGradientColors(text, out var startColor, out var endColor);

        result.Should().Be(expected);
        if (expected)
        {
            startColor.Should().Be(new CellColor(startR, startG, startB));
            endColor.Should().Be(new CellColor(endR, endG, endB));
        }
    }

    [Fact]
    public void FormatPictureCellText_MapsScalarValuesToExcelDisplayText()
    {
        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            DrawingInputParser.FormatPictureCellText(BlankValue.Instance).Should().Be("");
            DrawingInputParser.FormatPictureCellText(new NumberValue(12.5)).Should().Be("12.5");
            DrawingInputParser.FormatPictureCellText(new BoolValue(true)).Should().Be("TRUE");
            DrawingInputParser.FormatPictureCellText(new BoolValue(false)).Should().Be("FALSE");
            DrawingInputParser.FormatPictureCellText(new TextValue("East")).Should().Be("East");
            DrawingInputParser.FormatPictureCellText(new ErrorValue("#DIV/0!")).Should().Be("#DIV/0!");
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
        }
    }
}
