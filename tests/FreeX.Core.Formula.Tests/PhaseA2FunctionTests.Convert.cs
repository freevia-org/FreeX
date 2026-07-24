using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseA2FunctionTests
{
    // ── CONVERT ──────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_KgToG_Multiplies()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"kg\",\"g\")", sheet, wb);
        result.Should().Be(new NumberValue(1000));
    }

    [Fact]
    public void Convert_MeterToCentimeter()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"m\",\"cm\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void Convert_HoursToSeconds()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"hr\",\"sec\")", sheet, wb).Should().Be(new NumberValue(3600));
    }

    [Fact]
    public void Convert_CelsiusToFahrenheit()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(100,\"C\",\"F\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(212, 1e-9);
    }

    [Fact]
    public void Convert_FahrenheitToCelsius()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(32,\"F\",\"C\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void Convert_CelsiusToKelvin()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(0,\"C\",\"K\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(273.15, 1e-9);
    }

    [Fact]
    public void Convert_IncompatibleCategories_ReturnsNA()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"kg\",\"m\")", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Convert_UnknownUnit_ReturnsNA()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"foo\",\"g\")", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Convert_BytesToBits()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"byte\",\"bit\")", sheet, wb).Should().Be(new NumberValue(8));
    }

    // ── R82: Magnetism unit category (was entirely missing) ─────────────────

    [Fact]
    public void Convert_TeslaToGauss()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"T\",\"ga\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(10000, 1e-9);
    }

    [Fact]
    public void Convert_GaussToTesla_RoundTrips()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(10000,\"ga\",\"T\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(1, 1e-9);
    }

    // ── R82: Area "acre" must be Excel's us_acre/uk_acre, not a bare "acre" ──

    [Fact]
    public void Convert_UkAcreToSquareMeters_UsesUkAcreConstant()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"uk_acre\",\"m2\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(4046.8564224, 1e-6);
    }

    [Fact]
    public void Convert_UsAcreToSquareMeters_UsesUsAcreConstant()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"us_acre\",\"m2\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(4046.8726099, 1e-6);
    }

    [Fact]
    public void Convert_BareAcre_ReturnsNA_MatchingRealExcel()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"acre\",\"m2\")", sheet, wb).Should().Be(ErrorValue.NA);
    }

    // ── R82: Volume UK-imperial units (uk_gal/uk_pt/uk_qt) ───────────────────

    [Fact]
    public void Convert_UkGallonToLiters()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"uk_gal\",\"l\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(4.54609, 1e-9);
    }

    [Fact]
    public void Convert_UsGallonToLiters_StillUsesUsConstant()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"gal\",\"l\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(3.785412, 1e-9);
    }
}
