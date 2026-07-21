using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R57-formula-subtotal-aggregate-5-2: SUBTOTAL/AGGREGATE with a BARE SINGLE-CELL reference
/// argument (e.g. =SUBTOTAL(109,A5), not =SUBTOTAL(109,A5:A5)) must still apply hidden-row
/// exclusion (for the 101-111 / ignore-hidden AGGREGATE-option series) and nested-subtotal
/// exclusion, exactly as it would for a multi-cell range. Previously SUBTOTAL/AGGREGATE were
/// absent from <c>SingleCellReferenceRangeFunctions</c> (FormulaEvaluator.FunctionClassification.cs),
/// so a bare CellRefNode argument reached the built-in as a plain scalar with no row/sheet
/// provenance, and the hidden-row/nested-aggregate checks (which only run inside the
/// "args[i] is RangeValue rv" branch) were silently skipped.
/// </summary>
public sealed class R57_SubtotalAggregateBareSingleCellRefTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void Subtotal_BareSingleCellRef_HiddenRow_IgnoreHiddenFunctionNumber_ExcludesIt()
    {
        // Row 5 is hidden and A5 = 42. =SUBTOTAL(109,A5) (109 = SUM, ignore-hidden series) must
        // treat the single cell exactly like a 1-cell range: since its row is hidden, it is
        // excluded, so the sum over zero included cells is 0 (matching real Excel).
        var sheet = MakeSheet((5, 1, new NumberValue(42)));
        sheet.GroupHiddenRows.Add(5);

        _eval.Evaluate("=SUBTOTAL(109,A5)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Subtotal_BareSingleCellRef_HiddenRow_NonIgnoreHiddenFunctionNumber_StillIncludesIt()
    {
        // Sibling/no-regression case: func number 9 (plain SUM, no ignore-hidden semantics) must
        // still include the hidden cell's value, proving the fix doesn't over-exclude bare
        // single-cell refs regardless of the function-number series.
        var sheet = MakeSheet((5, 1, new NumberValue(42)));
        sheet.GroupHiddenRows.Add(5);

        _eval.Evaluate("=SUBTOTAL(9,A5)", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Aggregate_BareSingleCellRef_HiddenRow_IgnoreHiddenOption_ExcludesIt()
    {
        // AGGREGATE(9,1,A5) = SUM, option 1 = ignore hidden rows/errors. Row 5 hidden => the
        // single-cell reference must be excluded, yielding 0.
        var sheet = MakeSheet((5, 1, new NumberValue(42)));
        sheet.GroupHiddenRows.Add(5);

        _eval.Evaluate("=AGGREGATE(9,1,A5)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Aggregate_BareSingleCellRef_HiddenRow_KeepHiddenOption_StillIncludesIt()
    {
        // Sibling/no-regression: AGGREGATE(9,0,A5) = SUM, option 0 = keep hidden rows => the
        // single-cell reference's value must still be counted.
        var sheet = MakeSheet((5, 1, new NumberValue(42)));
        sheet.GroupHiddenRows.Add(5);

        _eval.Evaluate("=AGGREGATE(9,0,A5)", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Subtotal_MultiCellRange_HiddenRow_Unchanged()
    {
        // No-regression control: the existing multi-cell-range hidden-row exclusion path (already
        // covered elsewhere) must be untouched by adding SUBTOTAL/AGGREGATE to
        // SingleCellReferenceRangeFunctions.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(100)), // hidden
            (3, 1, new NumberValue(30)));
        sheet.GroupHiddenRows.Add(2);

        _eval.Evaluate("=SUBTOTAL(109,A1:A3)", sheet).Should().Be(new NumberValue(40));
    }
}
