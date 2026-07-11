using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R27-information-functions-deep-2: ISFORMULA and FORMULATEXT officially support a multi-cell
/// reference argument and must return one result per cell, matching the reference's shape (e.g.
/// ISFORMULA(A1:A3) spills TRUE/FALSE/FALSE), instead of always collapsing to the top-left cell.
/// </summary>
public sealed class R27_IsFormulaFormulaText_MultiCellSpill
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void IsFormula_SpillsOneResultPerCell_ForMultiCellColumnRange()
    {
        var sheet = Sheet();
        // A1 is a formula; A2 is a plain constant; A3 is text — exactly the failure scenario.
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("hi"));

        var result = _eval.Evaluate("=ISFORMULA(A1:A3)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new BoolValue(true));
        result.At(2, 1).Should().Be(new BoolValue(false));
        result.At(3, 1).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void FormulaText_SpillsOneResultPerCell_ForMultiCellColumnRange()
    {
        var sheet = Sheet();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("hi"));

        var result = _eval.Evaluate("=FORMULATEXT(A1:A3)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new TextValue("=1+1"));
        result.At(2, 1).Should().Be(ErrorValue.NA);
        result.At(3, 1).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void IsFormula_SpillsAcrossBothDimensions_ForTwoByTwoRange()
    {
        var sheet = Sheet();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1"); // A1: formula
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));   // B1: constant
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "2+2"); // B2: formula
        // A2 left blank.

        var result = _eval.Evaluate("=ISFORMULA(A1:B2)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new BoolValue(true));  // A1
        result.At(1, 2).Should().Be(new BoolValue(false)); // B1
        result.At(2, 1).Should().Be(new BoolValue(false)); // A2 (blank)
        result.At(2, 2).Should().Be(new BoolValue(true));  // B2
    }

    // --- Sibling already-working cases, unchanged by this fix ---

    [Fact]
    public void IsFormula_StillReturnsScalar_ForSingleCellReference()
    {
        var sheet = Sheet();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));

        _eval.Evaluate("=ISFORMULA(A1)", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=ISFORMULA(A2)", sheet).Should().Be(new BoolValue(false));
        // A single-cell "range" (A1:A1) must also stay scalar, not spill as a 1x1 array.
        _eval.Evaluate("=ISFORMULA(A1:A1)", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void FormulaText_StillReturnsScalar_ForSingleCellReference()
    {
        var sheet = Sheet();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");

        _eval.Evaluate("=FORMULATEXT(A1)", sheet).Should().Be(new TextValue("=1+1"));
        _eval.Evaluate("=FORMULATEXT(A1:A1)", sheet).Should().Be(new TextValue("=1+1"));
    }

    [Fact]
    public void IsFormula_StillCollapsesToTopLeftCell_ForFullColumnAndRowReferences()
    {
        // Full row/column references deliberately keep the top-left-collapse behaviour (spilling
        // over a full 1,048,576-row column is impractical) — this fix is scoped to plain bounded
        // ranges only, matching FormulaPredicates_UseTopLeftCellForFullRowAndColumnReferences.
        var sheet = Sheet();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "2+3");

        _eval.Evaluate("=ISFORMULA(A:A)", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=ISFORMULA(1:1)", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=FORMULATEXT(A:A)", sheet).Should().Be(new TextValue("=2+3"));
        _eval.Evaluate("=FORMULATEXT(1:1)", sheet).Should().Be(new TextValue("=2+3"));
    }

    [Fact]
    public void IsFormula_StillReturnsScalar_ForOffsetAndIndirectSingleCellReferences()
    {
        var sheet = Sheet();
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "1+1"); // B2

        _eval.Evaluate("=ISFORMULA(OFFSET(A1,1,1))", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=ISFORMULA(INDIRECT(\"B2\"))", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=FORMULATEXT(OFFSET(A1,1,1))", sheet).Should().Be(new TextValue("=1+1"));
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
