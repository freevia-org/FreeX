using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

public sealed class ColorInputParserTests
{
    [Theory]
    [InlineData("#217346", 0x21, 0x73, 0x46)]
    [InlineData("217346", 0x21, 0x73, 0x46)]
    [InlineData("  #Aa10fF  ", 0xAA, 0x10, 0xFF)]
    public void TryParseHexColor_AcceptsHashOrPlainSixDigitHex(string input, byte r, byte g, byte b)
    {
        ColorInputParser.TryParseHexColor(input, out var color).Should().BeTrue();
        color.Should().Be(new CellColor(r, g, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#12GG34")]
    public void TryParseHexColor_RejectsInvalidText(string input)
    {
        ColorInputParser.TryParseHexColor(input, out var color).Should().BeFalse();
        color.Should().BeNull();
    }

    [Theory]
    [InlineData("#217346", 0x21, 0x73, 0x46)]
    [InlineData("217346", 0x21, 0x73, 0x46)]
    [InlineData("33, 115, 70", 33, 115, 70)]
    [InlineData("33,115,70", 33, 115, 70)]
    public void TryParseColorText_AcceptsHexOrRgbTriples(string input, byte r, byte g, byte b)
    {
        ColorInputParser.TryParseColorText(input, out var color).Should().BeTrue();
        color.Should().Be(new CellColor(r, g, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("1,2")]
    [InlineData("1,2,300")]
    [InlineData("red")]
    public void TryParseColorText_RejectsInvalidColorText(string input)
    {
        ColorInputParser.TryParseColorText(input, out var color).Should().BeFalse();
        color.Should().Be(default(CellColor));
    }

    [Theory]
    [InlineData("33, 115, 70", 33, 115, 70)]
    [InlineData("33,115,70", 33, 115, 70)]
    public void TryParseRgbColorText_AcceptsRgbTriplesOnly(string input, byte r, byte g, byte b)
    {
        ColorInputParser.TryParseRgbColorText(input, out var color).Should().BeTrue();
        color.Should().Be(new CellColor(r, g, b));
    }

    [Theory]
    [InlineData("#217346")]
    [InlineData("217346")]
    [InlineData("1,2")]
    [InlineData("1,2,300")]
    public void TryParseRgbColorText_RejectsHexAndInvalidTriples(string input)
    {
        ColorInputParser.TryParseRgbColorText(input, out var color).Should().BeFalse();
        color.Should().Be(default(CellColor));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("clear")]
    [InlineData(" NONE ")]
    public void TryParseOptionalHexColor_TreatsClearKeywordsAsNull(string input)
    {
        ColorInputParser.TryParseOptionalHexColor(input, out var color).Should().BeTrue();
        color.Should().BeNull();
    }

    [Fact]
    public void FormatHexColor_ReturnsUppercaseHashRgb()
    {
        ColorInputParser.FormatHexColor(new CellColor(0x21, 0x73, 0x46)).Should().Be("#217346");
    }

    [Fact]
    public void FormatRgbColor_ReturnsCommaSeparatedDecimalRgb()
    {
        ColorInputParser.FormatRgbColor(new CellColor(0x21, 0x73, 0x46)).Should().Be("33,115,70");
        ColorInputParser.FormatRgbColor(new RgbColor(0x21, 0x73, 0x46)).Should().Be("33,115,70");
    }

    [Theory]
    [InlineData(RgbTripletTextProfile.CellEditor)]
    [InlineData(RgbTripletTextProfile.ConditionalFormatting)]
    [InlineData(RgbTripletTextProfile.DrawingInteraction)]
    public void TryParseRgbColorText_ProfilesShareByteTripletGrammar(RgbTripletTextProfile profile)
    {
        ColorInputParser.TryParseRgbColorText(" 1, 22, 255 ", profile, out CellColor cellColor)
            .Should().BeTrue();
        ColorInputParser.TryParseRgbColorText(" 1, 22, 255 ", profile, out RgbColor rgbColor)
            .Should().BeTrue();

        cellColor.Should().Be(new CellColor(1, 22, 255));
        rgbColor.Should().Be(new RgbColor(1, 22, 255));
    }

    [Theory]
    [InlineData(RgbTripletTextProfile.CellEditor)]
    [InlineData(RgbTripletTextProfile.ConditionalFormatting)]
    [InlineData(RgbTripletTextProfile.DrawingInteraction)]
    public void TryParseRgbColorText_ProfilesRejectNonTripletSyntax(RgbTripletTextProfile profile)
    {
        ColorInputParser.TryParseRgbColorText("#0116FF", profile, out CellColor hexColor)
            .Should().BeFalse();
        ColorInputParser.TryParseRgbColorText("1,22,256", profile, out CellColor outOfRangeColor)
            .Should().BeFalse();

        hexColor.Should().Be(default(CellColor));
        outOfRangeColor.Should().Be(default(CellColor));
    }

    [Fact]
    public void TryParseRgbColorText_ProfilesPreserveEditorCultureContracts()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var drawingCulture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        drawingCulture.NumberFormat.PositiveSign = "p";

        try
        {
            CultureInfo.CurrentCulture = drawingCulture;

            ColorInputParser.TryParseRgbColorText(
                    "p1,2,3",
                    RgbTripletTextProfile.DrawingInteraction,
                    out CellColor drawingColor)
                .Should().BeTrue();
            ColorInputParser.TryParseRgbColorText(
                    "p1,2,3",
                    RgbTripletTextProfile.CellEditor,
                    out CellColor cellEditorColor)
                .Should().BeFalse();
            ColorInputParser.TryParseRgbColorText(
                    "p1,2,3",
                    RgbTripletTextProfile.ConditionalFormatting,
                    out RgbColor conditionalColor)
                .Should().BeFalse();

            drawingColor.Should().Be(new CellColor(1, 2, 3));
            cellEditorColor.Should().Be(default(CellColor));
            conditionalColor.Should().Be(default(RgbColor));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void TryParseRgbColorText_ProfilesPreserveNullHandling()
    {
        ColorInputParser.TryParseRgbColorText(
                null,
                RgbTripletTextProfile.ConditionalFormatting,
                out RgbColor conditionalColor)
            .Should().BeFalse();
        conditionalColor.Should().Be(default(RgbColor));

        Func<bool> parseCellEditor = () => ColorInputParser.TryParseRgbColorText(
            null,
            RgbTripletTextProfile.CellEditor,
            out CellColor _);
        Func<bool> parseDrawing = () => ColorInputParser.TryParseRgbColorText(
            null,
            RgbTripletTextProfile.DrawingInteraction,
            out CellColor _);

        parseCellEditor.Should().Throw<NullReferenceException>();
        parseDrawing.Should().Throw<NullReferenceException>();
    }
}
