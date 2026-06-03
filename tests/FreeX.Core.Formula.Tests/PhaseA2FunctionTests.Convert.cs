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
}
