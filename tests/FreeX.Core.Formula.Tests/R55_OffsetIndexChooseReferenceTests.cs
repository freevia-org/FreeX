using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R55-formula-lookup-offset-indirect-5-1/-2: OFFSET's base-reference argument switch (and the
/// same switch shared by ISREF / CELL's reference argument) only recognized OFFSET/INDIRECT/
/// ANCHORARRAY as reference-returning function calls. INDEX and CHOOSE both return a genuine Excel
/// reference when their source arguments are references (e.g. INDEX(A1:A5,3) is a reference to A3,
/// used pervasively to build dynamic ranges), so OFFSET(INDEX(...),...), ISREF(INDEX(...)), and
/// CELL("address",INDEX(...)) all wrongly failed before this fix.
/// </summary>
public sealed class R55_OffsetIndexChooseReferenceTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet SheetWithColumn()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(50));
        return sheet;
    }

    [Fact]
    public void Offset_WithIndexBaseReference_ShiftsFromTheIndexedCell()
    {
        // INDEX(A1:A5,3) is a reference to A3; OFFSET shifts it down 1 row to A4 (=40).
        var sheet = SheetWithColumn();

        _eval.Evaluate("=OFFSET(INDEX(A1:A5,3),1,0)", sheet).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Offset_WithChooseBaseReference_ShiftsFromTheChosenCellReference()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1)); // A1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2)); // B1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99)); // B2

        // CHOOSE(2,A1,B1) picks the B1 reference; OFFSET shifts it down 1 row to B2 (=99).
        _eval.Evaluate("=OFFSET(CHOOSE(2,A1,B1),1,0)", sheet).Should().Be(new NumberValue(99));
    }

    [Fact]
    public void Offset_WithOffsetBaseReference_StillWorks_SiblingNoRegression()
    {
        var sheet = SheetWithColumn();

        // Pre-existing nested-OFFSET reference idiom, unaffected by adding INDEX/CHOOSE.
        _eval.Evaluate("=OFFSET(OFFSET(A1,0,0),1,0)", sheet).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void IsRef_OfIndexResult_ReturnsTrue()
    {
        var sheet = SheetWithColumn();

        _eval.Evaluate("=ISREF(INDEX(A1:A5,3))", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsRef_OfPlainCellReference_StillTrue_SiblingNoRegression()
    {
        _eval.Evaluate("=ISREF(A1)", new Sheet(SheetId.New(), "S")).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Cell_AddressOfIndexResult_ReturnsTargetCellAddress()
    {
        var sheet = SheetWithColumn();

        _eval.Evaluate("=CELL(\"address\",INDEX(A1:A5,3))", sheet).Should().Be(new TextValue("$A$3"));
    }
}
