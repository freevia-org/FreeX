using System.Globalization;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSourceRectangleRatioCodecTests
{
    [Theory]
    [InlineData(null, 0d)]
    [InlineData("", 0d)]
    [InlineData("not-a-number", 0d)]
    [InlineData(" 10000 ", 0.1d)]
    [InlineData("1E4", 0.1d)]
    [InlineData("-15000", -0.15d)]
    [InlineData("200000", 1d)]
    [InlineData("-200000", -1d)]
    public void Parse_PreservesSourceRectangleGrammarAndBounds(string? value, double expected)
    {
        XlsxSourceRectangleRatioCodec.Parse(value).Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData(0d, "0")]
    [InlineData(0.1d, "10000")]
    [InlineData(-0.15d, "-15000")]
    [InlineData(2d, "100000")]
    [InlineData(-2d, "-100000")]
    [InlineData(0.000015d, "2")]
    [InlineData(0.000025d, "2")]
    public void Format_PreservesInvariantMidpointRoundingAndBounds(double ratio, string expected)
    {
        XlsxSourceRectangleRatioCodec.Format(ratio).Should().Be(expected);
    }

    [Fact]
    public void ParseAndFormat_PreserveExistingNonFiniteBehavior()
    {
        var nan = double.NaN;

        XlsxSourceRectangleRatioCodec.Parse("Infinity").Should().Be(1);
        XlsxSourceRectangleRatioCodec.Parse("-Infinity").Should().Be(-1);
        double.IsNaN(XlsxSourceRectangleRatioCodec.Parse("NaN")).Should().BeTrue();

        XlsxSourceRectangleRatioCodec.Format(double.PositiveInfinity).Should().Be("100000");
        XlsxSourceRectangleRatioCodec.Format(double.NegativeInfinity).Should().Be("-100000");
        XlsxSourceRectangleRatioCodec.Format(nan).Should().Be(
            unchecked((int)nan).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ProductionDrawingPaths_UseTheCanonicalSourceRectangleCodec()
    {
        var objectWriter = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetDrawingObjectWriter.cs");
        var geometryRewriter = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxSourceDrawingGeometryRewriter.cs");
        var drawingReader = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetDrawingParts.cs");

        objectWriter.Should().Contain("XlsxSourceRectangleRatioCodec.Format")
            .And.Contain("HasPictureCrop(picture)")
            .And.NotContain("ToSourceRectanglePercent")
            .And.NotContain("100000d");
        geometryRewriter.Should().Contain("XlsxSourceRectangleRatioCodec.Format")
            .And.NotContain("ToSourceRectanglePercent")
            .And.NotContain("100000d");
        drawingReader.Should().Contain("XlsxSourceRectangleRatioCodec.Parse")
            .And.NotContain("ReadSourceRectangleRatio")
            .And.NotContain("100000d");
    }
}
