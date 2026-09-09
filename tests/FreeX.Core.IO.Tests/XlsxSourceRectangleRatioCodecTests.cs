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

    /// <summary>
    /// r577 REPLACES this test's former assertions, which were named
    /// <c>ParseAndFormat_PreserveExistingNonFiniteBehavior</c> and pinned
    /// <c>Parse("Infinity") == 1</c> and <c>Parse("NaN")</c> returning NaN.
    /// <para>
    /// Those assertions were a CHARACTERIZATION snapshot, not a decision. The test was written in
    /// commit 8bdbe0bb07 ("centralize picture crop ratio conversion") whose job was to prove that
    /// folding three duplicated implementations into one codec changed nothing; it therefore
    /// recorded whatever the duplicates happened to do. The tell is the old Format assertion, which
    /// asserted the result equalled <c>unchecked((int)nan).ToString(...)</c> -- an expression the
    /// test computed itself from the same undefined conversion, so it asserted nothing about
    /// correctness. (This is the distinction r576 turned on: the 64 tests that stopped a guard there
    /// encoded a deliberate design -- functions report domain errors -- so the GUARD was narrowed.
    /// These encoded an accident, so the TEST is what changes.)
    /// </para>
    /// <para>
    /// A srcRect percentage is an integer in OOXML (ST_Percentage), so "NaN" and "1e400" are not
    /// values the attribute can express at all; Excel ignores an attribute it cannot read, leaving
    /// the picture uncropped. Cropping a picture to nothing on the strength of unreadable text is
    /// the worse failure, so a non-finite percentage now takes the same no-crop result (0) this
    /// method already gives text it cannot parse.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    public void Parse_NonFinitePercentage_IsReadAsNoCrop(string value)
    {
        var ratio = XlsxSourceRectangleRatioCodec.Parse(value);

        double.IsFinite(ratio).Should().BeTrue($"\"{value}\" produced the ratio {ratio}");
        ratio.Should().Be(0);
    }

    [Fact]
    public void Format_NonFiniteRatio_WritesNoCropRatherThanGarbage()
    {
        // Format is unchanged by r577 and needs no guard: .NET's double->int conversion saturates,
        // so NaN already reaches "0", and the Clamp bounds both infinities to the full-crop edge.
        // Asserted explicitly here because the old test expressed the NaN case as the undefined
        // conversion itself, which could not have caught a change in it.
        XlsxSourceRectangleRatioCodec.Format(double.NaN).Should().Be("0");
        XlsxSourceRectangleRatioCodec.Format(double.PositiveInfinity).Should().Be("100000");
        XlsxSourceRectangleRatioCodec.Format(double.NegativeInfinity).Should().Be("-100000");
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
