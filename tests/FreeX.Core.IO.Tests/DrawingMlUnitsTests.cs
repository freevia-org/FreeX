using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip and formula verification for <see cref="DrawingMlUnits"/>.
/// All expectations are derived from the ECMA-376 definitions, not from the
/// implementation — so a wrong formula would produce a wrong answer and fail.
/// </summary>
public sealed class DrawingMlUnitsTests
{
    // ── Constants ────────────────────────────────────────────────────────────

    [Fact]
    public void EmuPerPoint_Is12700()
        => DrawingMlUnits.EmuPerPoint.Should().Be(12700L);

    [Fact]
    public void EmuPerInch_Is914400()
        => DrawingMlUnits.EmuPerInch.Should().Be(914400L);

    [Fact]
    public void EmuPerInch_Is72xEmuPerPoint()
        => DrawingMlUnits.EmuPerInch.Should().Be(72L * DrawingMlUnits.EmuPerPoint);

    // ── EMU ↔ points ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,      0L)]
    [InlineData(1,      12700L)]
    [InlineData(0.5,    6350L)]
    [InlineData(10,     127000L)]
    [InlineData(1.5,    19050L)]
    public void PointsToEmu_ReturnsExpected(double points, long expectedEmu)
        => DrawingMlUnits.PointsToEmu(points).Should().Be(expectedEmu);

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
        => DrawingMlUnits.EmuToPoints(emuText).Should().BeApproximately(expectedPoints, 1e-9);

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(0.75)]
    public void EmuRoundTrip_Points(double points)
    {
        var emu = DrawingMlUnits.PointsToEmu(points);
        var back = DrawingMlUnits.EmuToPoints(emu.ToString());
        back.Should().BeApproximately(points, 1e-9);
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
        => DrawingMlUnits.DxaToPoints(dxaText).Should().BeApproximately(expectedPoints, 1e-9);

    [Theory]
    [InlineData(0,    0)]
    [InlineData(1,    20)]
    [InlineData(0.5,  10)]
    [InlineData(12,   240)]
    [InlineData(72,   1440)]
    public void PointsToDxa_ReturnsExpected(double points, int expectedDxa)
        => DrawingMlUnits.PointsToDxa(points).Should().Be(expectedDxa);

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(12.0)]
    [InlineData(72.0)]
    public void DxaRoundTrip_Points(double points)
    {
        var dxa = DrawingMlUnits.PointsToDxa(points);
        var back = DrawingMlUnits.DxaToPoints(dxa.ToString());
        back.Should().BeApproximately(points, 1e-9);
    }

    // ── half-points ↔ points ─────────────────────────────────────────────────

    [Theory]
    [InlineData("24",  12.0)]
    [InlineData("11",  5.5)]
    [InlineData("0",   null)]
    [InlineData(null,  null)]
    [InlineData("",    null)]
    [InlineData("abc", null)]
    public void HalfPointsToPoints_ReturnsExpected(string? halfPt, double? expectedPoints)
        => DrawingMlUnits.HalfPointsToPoints(halfPt).Should().Be(expectedPoints);

    [Theory]
    [InlineData(1.0,  2)]
    [InlineData(0.5,  1)]
    [InlineData(12.0, 24)]
    [InlineData(5.5,  11)]
    public void PointsToHalfPoints_ReturnsExpected(double points, int expectedHalfPt)
        => DrawingMlUnits.PointsToHalfPoints(points).Should().Be(expectedHalfPt);

    [Theory]
    [InlineData(1.0)]
    [InlineData(12.0)]
    [InlineData(5.5)]
    public void HalfPointRoundTrip_Points(double points)
    {
        var hp = DrawingMlUnits.PointsToHalfPoints(points);
        var back = DrawingMlUnits.HalfPointsToPoints(hp.ToString());
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
        => DrawingMlUnits.EighthPointsToPoints(eighthPt).Should().BeApproximately(expectedPoints, 1e-9);

    [Theory]
    [InlineData(1.0,  8)]
    [InlineData(0.5,  4)]
    [InlineData(0.0,  1)]   // minimum is 1
    [InlineData(0.001,1)]
    [InlineData(2.0,  16)]
    public void PointsToEighthPoints_ReturnsExpected(double points, int expectedEighthPt)
        => DrawingMlUnits.PointsToEighthPoints(points).Should().Be(expectedEighthPt);

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void EighthPointRoundTrip_Points(double points)
    {
        var ep = DrawingMlUnits.PointsToEighthPoints(points);
        var back = DrawingMlUnits.EighthPointsToPoints(ep.ToString());
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
        => DrawingMlUnits.ParseInt(text).Should().Be(expected);
}
