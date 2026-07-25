using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R86-formula-logical-info-5-2: ISFORMULA/FORMULATEXT must spill one result per cell for a
/// multi-cell reference reached through a defined name (or OFFSET/INDEX/INDIRECT/CHOOSE), exactly
/// the same as they already do for a literal A1:A3 range (see
/// R27_IsFormulaFormulaText_MultiCellSpill) -- a name is a pure reference alias in Excel, so
/// ISFORMULA(Data) must behave identically to ISFORMULA(A1:A3) when Data = Sheet1!$A$1:$A$3,
/// instead of collapsing to the name's top-left cell only.
/// </summary>
public sealed class R86_IsFormulaFormulaText_NamedRangeSpill
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void IsFormula_SpillsOneResultPerCell_ForNamedMultiCellRange()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Sheet1");
        // A1 is a formula; A2 is a plain constant; A3 is text -- exactly the failure scenario.
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("hi"));

        wb.DefineNamedRange("Data", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        var result = _eval.Evaluate("=ISFORMULA(Data)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new BoolValue(true));
        result.At(2, 1).Should().Be(new BoolValue(false));
        result.At(3, 1).Should().Be(new BoolValue(false));

        // Same underlying reference reached via the literal range must give the identical result.
        var direct = _eval.Evaluate("=ISFORMULA(A1:A3)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;
        direct.At(1, 1).Should().Be(result.At(1, 1));
        direct.At(2, 1).Should().Be(result.At(2, 1));
        direct.At(3, 1).Should().Be(result.At(3, 1));
    }

    [Fact]
    public void FormulaText_SpillsOneResultPerCell_ForNamedMultiCellRange()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("hi"));

        wb.DefineNamedRange("Data", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        var result = _eval.Evaluate("=FORMULATEXT(Data)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new TextValue("=1+1"));
        result.At(2, 1).Should().Be(ErrorValue.NA);
        result.At(3, 1).Should().Be(ErrorValue.NA);
    }

    // --- Sibling already-working cases, unchanged by this fix ---

    [Fact]
    public void IsFormula_StillReturnsScalar_ForSingleCellNamedRange()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");

        wb.DefineNamedRange("Single", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1)));

        _eval.Evaluate("=ISFORMULA(Single)", sheet, wb).Should().Be(new BoolValue(true));
        _eval.Evaluate("=FORMULATEXT(Single)", sheet, wb).Should().Be(new TextValue("=1+1"));
    }

    [Fact]
    public void IsFormula_SpillsOneResultPerCell_ForOffsetMultiCellReference()
    {
        // OFFSET/INDEX/INDIRECT/CHOOSE are pure reference aliases too -- the same collapse bug
        // applied to them (see finding EVIDENCE), so cover the OFFSET case explicitly alongside
        // the named-range case above.
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("hi"));

        var result = _eval.Evaluate("=ISFORMULA(OFFSET(A1,0,0,3,1))", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new BoolValue(true));
        result.At(2, 1).Should().Be(new BoolValue(false));
        result.At(3, 1).Should().Be(new BoolValue(false));
    }
}
