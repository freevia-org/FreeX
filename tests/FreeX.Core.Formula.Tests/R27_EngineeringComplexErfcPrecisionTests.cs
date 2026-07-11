using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R27-engineering-functions-deep-1: ERFC/ERFC.PRECISE used to compute 1-erf(x), which loses all
/// precision (and eventually rounds to exactly 0) for x beyond ~5.9 due to catastrophic
/// cancellation against the ~1.5e-7 absolute error bound of the erf approximation. The fix reuses
/// the codebase's existing cancellation-free complementary error function (already used by
/// NORMSDIST/NORMSCDF).
/// </summary>
public sealed class R27_EngineeringComplexErfcPrecisionTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    // Bug case: previously collapsed to exactly 0 (true value ~2.15e-17).
    [InlineData("=ERFC(6)", 2.1519736712498913e-17, 1e-19)]
    [InlineData("=ERFC.PRECISE(6)", 2.1519736712498913e-17, 1e-19)]
    // Bug case: previously ~0.28% relative error (true value ~1.5417258025785e-08).
    [InlineData("=ERFC(4)", 1.5417257900280018e-08, 1e-14)]
    // Sibling already-working case: small-x values must remain correct after the change.
    [InlineData("=ERFC(0)", 1.0, 1e-9)]
    [InlineData("=ERFC(1)", 0.15729920705028513, 1e-9)]
    [InlineData("=ERFC.PRECISE(1)", 0.15729920705028513, 1e-9)]
    [InlineData("=ERFC(-1)", 1.8427007929497148, 1e-9)]
    public void Erfc_MatchesExcelAcrossMagnitudes(string formula, double expected, double tolerance)
    {
        var result = _eval.Evaluate(formula, MakeSheet());
        var number = result.Should().BeOfType<NumberValue>().Subject;
        number.Value.Should().BeApproximately(expected, tolerance);
    }

    private static Sheet MakeSheet(params (uint Row, uint Col, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
        return sheet;
    }
}
