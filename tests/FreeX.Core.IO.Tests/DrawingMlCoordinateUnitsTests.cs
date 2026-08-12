using FluentAssertions;
using DrawingMlCoordinateUnits = Free.Shared.Drawing.DrawingMlCoordinateUnits;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip and formula verification for <see cref="DrawingMlCoordinateUnits"/>.
/// All expectations are derived from the ECMA-376 definitions, not from the
/// implementation — so a wrong formula would produce a wrong answer and fail.
/// </summary>
public sealed class DrawingMlCoordinateUnitsTests
{
    // ── Constants ────────────────────────────────────────────────────────────

    [Fact]
    public void EmuPerPoint_Is12700()
        => DrawingMlCoordinateUnits.EmuPerPoint.Should().Be(12700L);

    [Fact]
    public void EmuPerInch_Is914400()
        => DrawingMlCoordinateUnits.EmuPerInch.Should().Be(914400L);

    [Fact]
    public void EmuPerInch_Is72xEmuPerPoint()
        => DrawingMlCoordinateUnits.EmuPerInch.Should().Be(72L * DrawingMlCoordinateUnits.EmuPerPoint);

    [Fact]
    public void EmuPerPixel_Is9525()
        => DrawingMlCoordinateUnits.EmuPerPixel.Should().Be(9525L);

    [Fact]
    public void AngleUnitsPerDegree_Is60000()
        => DrawingMlCoordinateUnits.AngleUnitsPerDegree.Should().Be(60000L);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(60000, 1)]
    [InlineData(5400000, 90)]
    [InlineData(-5400000, -90)]
    public void AngleToDegrees_ReturnsExpected(double angleUnits, double expectedDegrees)
        => DrawingMlCoordinateUnits.AngleToDegrees(angleUnits).Should().BeApproximately(expectedDegrees, 1e-9);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5400000, Math.PI / 2)]
    [InlineData(10800000, Math.PI)]
    public void AngleToRadians_ReturnsExpected(double angleUnits, double expectedRadians)
        => DrawingMlCoordinateUnits.AngleToRadians(angleUnits).Should().BeApproximately(expectedRadians, 1e-9);

    // ── EMU ↔ points ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,      0L)]
    [InlineData(1,      12700L)]
    [InlineData(0.5,    6350L)]
    [InlineData(10,     127000L)]
    [InlineData(1.5,    19050L)]
    public void PointsToEmu_ReturnsExpected(double points, long expectedEmu)
        => DrawingMlCoordinateUnits.PointsToEmu(points).Should().Be(expectedEmu);

    [Theory]
    [InlineData("12700",  1.0)]
    [InlineData("0",      0.0)]
    [InlineData("6350",   0.5)]
    [InlineData("19050",  1.5)]
    [InlineData("127000", 10.0)]
    [InlineData(null,     0.0)]
    [InlineData("",       0.0)]
    [InlineData("abc",    0.0)]
    public void EmuToPoints_ReturnsExpected(string? emuText, double expectedPoints)
        => DrawingMlCoordinateUnits.EmuToPoints(emuText).Should().BeApproximately(expectedPoints, 1e-9);

    [Theory]
    [InlineData(12700,  1.0)]
    [InlineData(0,      0.0)]
    [InlineData(6350,   0.5)]
    [InlineData(19050,  1.5)]
    [InlineData(127000, 10.0)]
    public void EmuToPoints_Numeric_ReturnsExpected(double emu, double expectedPoints)
        => DrawingMlCoordinateUnits.EmuToPoints(emu).Should().BeApproximately(expectedPoints, 1e-9);

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(0.75)]
    public void EmuRoundTrip_Points(double points)
    {
        var emu = DrawingMlCoordinateUnits.PointsToEmu(points);
        var back = DrawingMlCoordinateUnits.EmuToPoints(emu.ToString());
        back.Should().BeApproximately(points, 1e-9);
    }

    [Theory]
    [InlineData(0, 0L)]
    [InlineData(1, 9525L)]
    [InlineData(0.5, 4762L)]
    [InlineData(10, 95250L)]
    [InlineData(-5, 0L)]
    public void PixelsToEmu_ReturnsExpected(double pixels, long expectedEmu)
        => DrawingMlCoordinateUnits.PixelsToEmu(pixels).Should().Be(expectedEmu);

    [Theory]
    [InlineData("9525", 1.0)]
    [InlineData("0", 0.0)]
    [InlineData("4762.5", 0.5)]
    [InlineData("95250", 10.0)]
    [InlineData(null, 0.0)]
    [InlineData("", 0.0)]
    [InlineData("abc", 0.0)]
    public void EmuToPixels_ReturnsExpected(string? emuText, double expectedPixels)
        => DrawingMlCoordinateUnits.EmuToPixels(emuText).Should().BeApproximately(expectedPixels, 1e-9);

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(10.0)]
    public void EmuRoundTrip_Pixels(double pixels)
    {
        var emu = DrawingMlCoordinateUnits.PixelsToEmu(pixels);
        var back = DrawingMlCoordinateUnits.EmuToPixels(emu.ToString());
        back.Should().BeApproximately(pixels, 1e-9);
    }

    [Fact]
    public void EmuRoundTrip_FractionalPixels_PreservesRoundedEmuBehavior()
    {
        var emu = DrawingMlCoordinateUnits.PixelsToEmu(1.5);
        var back = DrawingMlCoordinateUnits.EmuToPixels(emu.ToString());

        emu.Should().Be(14288L);
        back.Should().BeApproximately(14288d / DrawingMlCoordinateUnits.EmuPerPixel, 1e-9);
    }

    // ── dxa ↔ points ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("20",  1.0)]
    [InlineData("0",   0.0)]
    [InlineData("10",  0.5)]
    [InlineData("1440", 72.0)]
    [InlineData(null,  0.0)]
    [InlineData("",    0.0)]
    [InlineData("abc", 0.0)]
    public void DxaToPoints_ReturnsExpected(string? dxaText, double expectedPoints)
        => DrawingMlCoordinateUnits.DxaToPoints(dxaText).Should().BeApproximately(expectedPoints, 1e-9);

    [Theory]
    [InlineData(0,    0)]
    [InlineData(1,    20)]
    [InlineData(0.5,  10)]
    [InlineData(12,   240)]
    [InlineData(72,   1440)]
    public void PointsToDxa_ReturnsExpected(double points, int expectedDxa)
        => DrawingMlCoordinateUnits.PointsToDxa(points).Should().Be(expectedDxa);

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(12.0)]
    [InlineData(72.0)]
    public void DxaRoundTrip_Points(double points)
    {
        var dxa = DrawingMlCoordinateUnits.PointsToDxa(points);
        var back = DrawingMlCoordinateUnits.DxaToPoints(dxa.ToString());
        back.Should().BeApproximately(points, 1e-9);
    }

    // ── half-points ↔ points ─────────────────────────────────────────────────

    [Theory]
    [InlineData("24",  12.0)]
    [InlineData("11",  5.5)]
    [InlineData("0",   0.0)]
    [InlineData(null,  null)]
    [InlineData("",    null)]
    [InlineData("abc", null)]
    public void HalfPointsToPoints_ReturnsExpected(string? halfPt, double? expectedPoints)
        => DrawingMlCoordinateUnits.HalfPointsToPoints(halfPt).Should().Be(expectedPoints);

    /// <summary>
    /// Regression: an explicit OOXML half-points value of <c>0</c> (e.g. a literal <c>w:val="0"</c>) must
    /// be preserved as 0.0, not folded into "attribute absent" (null). Before the fix, HalfPointsToPoints
    /// used "parsed value != 0" as its absence signal, so a real, explicit 0 read back as null — identical
    /// to a genuinely missing attribute — and callers using `?? someDefault` silently substituted the
    /// default in place of the caller's explicit zero.
    /// </summary>
    [Fact]
    public void HalfPointsToPoints_DistinguishesExplicitZeroFromAbsent()
    {
        DrawingMlCoordinateUnits.HalfPointsToPoints("0").Should().Be(0.0,
            "an explicit w:val=\"0\" is a real value, not an absent attribute");
        DrawingMlCoordinateUnits.HalfPointsToPoints(null).Should().BeNull(
            "a null value (attribute genuinely absent) must still map to null");
        DrawingMlCoordinateUnits.HalfPointsToPoints("0").Should().NotBe(DrawingMlCoordinateUnits.HalfPointsToPoints(null),
            "explicit-0 and absent are distinct states and must not collapse to the same result");
    }

    [Theory]
    [InlineData(1.0,  2)]
    [InlineData(0.5,  1)]
    [InlineData(12.0, 24)]
    [InlineData(5.5,  11)]
    public void PointsToHalfPoints_ReturnsExpected(double points, int expectedHalfPt)
        => DrawingMlCoordinateUnits.PointsToHalfPoints(points).Should().Be(expectedHalfPt);

    [Theory]
    [InlineData(1.0)]
    [InlineData(12.0)]
    [InlineData(5.5)]
    public void HalfPointRoundTrip_Points(double points)
    {
        var hp = DrawingMlCoordinateUnits.PointsToHalfPoints(points);
        var back = DrawingMlCoordinateUnits.HalfPointsToPoints(hp.ToString());
        back.Should().BeApproximately(points, 1e-9);
    }

    // ── eighth-points ↔ points ───────────────────────────────────────────────

    [Theory]
    [InlineData("8",  1.0)]
    [InlineData("4",  0.5)]
    [InlineData("0",  0.0)]
    [InlineData(null, 0.0)]
    [InlineData("",   0.0)]
    [InlineData("abc",0.0)]
    public void EighthPointsToPoints_ReturnsExpected(string? eighthPt, double expectedPoints)
        => DrawingMlCoordinateUnits.EighthPointsToPoints(eighthPt).Should().BeApproximately(expectedPoints, 1e-9);

    [Theory]
    [InlineData(1.0,  8)]
    [InlineData(0.5,  4)]
    [InlineData(0.0,  1)]   // minimum is 1
    [InlineData(0.001,1)]
    [InlineData(2.0,  16)]
    public void PointsToEighthPoints_ReturnsExpected(double points, int expectedEighthPt)
        => DrawingMlCoordinateUnits.PointsToEighthPoints(points).Should().Be(expectedEighthPt);

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void EighthPointRoundTrip_Points(double points)
    {
        var ep = DrawingMlCoordinateUnits.PointsToEighthPoints(points);
        var back = DrawingMlCoordinateUnits.EighthPointsToPoints(ep.ToString());
        back.Should().BeApproximately(points, 1e-9);
    }

    // ── ParseInt ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0",    0)]
    [InlineData("42",   42)]
    [InlineData("-1",   -1)]
    [InlineData(null,   0)]
    [InlineData("",     0)]
    [InlineData("abc",  0)]
    [InlineData("1.5",  0)]
    public void ParseInt_ReturnsExpected(string? text, int expected)
        => DrawingMlCoordinateUnits.ParseInt(text).Should().Be(expected);
}
