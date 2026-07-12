using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Regression coverage for R31-datetime-serial-format-deep-1 (DATEVALUE portion):
// DATEVALUE must resolve two-digit years using Excel's pivot (00-29 -> 2000-2029,
// 30-99 -> 1930-1999), not .NET's default calendar cutoff (00-49 -> 2000-2049,
// 50-99 -> 1950-1999).
public sealed class DatevalueTwoDigitYearTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Datevalue_TwoDigitYearAbovePivot_ResolvesToNineteenHundreds()
    {
        // "45" is above Excel's two-digit-year pivot of 29, so it must resolve to 1945,
        // not 2045 (which is what .NET's default TwoDigitYearMax of 2049 would produce).
        var expected = new DateTime(1945, 6, 15).ToOADate();
        _eval.Evaluate("=DATEVALUE(\"6/15/45\")", Sheet())
            .Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Datevalue_TwoDigitYearAtOrBelowPivot_ResolvesToTwoThousands()
    {
        // "20" is at/below the pivot, so it resolves to 2020 under both Excel's and
        // .NET's default rule - sibling case that must keep working.
        var expected = new DateTime(2020, 6, 15).ToOADate();
        _eval.Evaluate("=DATEVALUE(\"6/15/20\")", Sheet())
            .Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Datevalue_FourDigitYear_Unaffected()
    {
        // Explicit four-digit years must be unaffected by the two-digit pivot change.
        var expected = new DateTime(2020, 6, 15).ToOADate();
        _eval.Evaluate("=DATEVALUE(\"6/15/2020\")", Sheet())
            .Should().Be(new NumberValue(expected));
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
