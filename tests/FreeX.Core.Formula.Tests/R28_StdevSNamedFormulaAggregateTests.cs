using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-28 finding R28-compatibility-functions-deep-1: "STDEV.S" was the only member of the
/// whole STDEV/VAR alias family missing from the AggregateFunctions set in
/// FormulaEvaluator.FunctionClassification.cs (it was already present in the sibling
/// DirectTextCoercingAggregates / ReferenceProvenanceAggregates sets). Because
/// FormulaEvaluator.Functions.cs only flattens a RangeValue-producing argument (e.g. a named
/// formula built on OFFSET) into individual scalars when IsAggregateFunction(name) is true,
/// STDEV.S received the whole RangeValue unflattened, CollectNumbers found no numbers, and it
/// returned #DIV/0! -- while the legacy alias STDEV(same argument) computed correctly.
/// </summary>
public sealed class R28_StdevSNamedFormulaAggregateTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static Workbook MakeOneToTenWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("Test");
        sheet = workbook.AddSheet("Sheet1");
        for (var row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));
        // A common "dynamic named range" pattern: a named formula built on OFFSET rather than
        // a literal cell range, which resolves to a RangeValue via TryEvaluateNamedFormula
        // instead of the FastAggregateKind / plain-range fast path.
        workbook.NamedFormulas["MyRange"] = "OFFSET($A$1,0,0,10,1)";
        return workbook;
    }

    [Fact]
    public void StdevS_NamedFormulaOffsetRange_ComputesSampleStdDev_InsteadOfDivByZero()
    {
        var workbook = MakeOneToTenWorkbook(out var sheet);

        var result = _evaluator.Evaluate("=STDEV.S(MyRange)", sheet, workbook);

        // R116: Stdev()/VarS() now round their accumulated mean and sum-of-squared-deviations
        // to 15 significant digits (matching Excel's documented precision and the sibling
        // SUM/AVERAGE/PRODUCT fixes -- see R116_AggregateFunctions15SigRoundingTests), which
        // shifts the last bit of this sqrt(variance) result relative to the previous unrounded
        // computation.
        result.Should().Be(new NumberValue(3.027650354097492));
    }

    [Fact]
    public void Stdev_NamedFormulaOffsetRange_StillComputesSampleStdDev()
    {
        // Sibling already-working case (legacy alias): must remain correct and match STDEV.S.
        var workbook = MakeOneToTenWorkbook(out var sheet);

        var result = _evaluator.Evaluate("=STDEV(MyRange)", sheet, workbook);

        // R116: Stdev()/VarS() now round their accumulated mean and sum-of-squared-deviations
        // to 15 significant digits (matching Excel's documented precision and the sibling
        // SUM/AVERAGE/PRODUCT fixes -- see R116_AggregateFunctions15SigRoundingTests), which
        // shifts the last bit of this sqrt(variance) result relative to the previous unrounded
        // computation.
        result.Should().Be(new NumberValue(3.027650354097492));
    }
}
