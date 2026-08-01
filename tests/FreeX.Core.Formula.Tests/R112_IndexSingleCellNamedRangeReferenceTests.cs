using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// EvaluateIndexAsReference (INDEX(ref, row[, col]) resolved to the reference it selects, used
/// whenever INDEX's result flows into a reference-expecting position such as OFFSET's base
/// argument or CELL("address", ...)'s reference argument) only recognized a
/// <see cref="RangeRefNode"/>/<see cref="FullColumnRangeRefNode"/>/<see cref="FullRowRangeRefNode"/>
/// source (via TryAsRangeRef). A bare single-cell reference (CellRefNode, e.g. A1) and a defined
/// name (NamedRangeNode) are ALSO valid INDEX reference sources in Excel -- INDEX(A1,1) returns a
/// reference to A1 itself and INDEX(MyName,1) returns a reference into the named range -- so
/// OFFSET(INDEX(A1,1),1,0) and CELL("address",INDEX(A1,1)) both wrongly returned #VALUE! before
/// this fix. See EvaluateOffsetReference's own base-argument switch (case CellRefNode / case
/// NamedRangeNode), which already handled both shapes for OFFSET's OWN first argument.
/// </summary>
public sealed class R112_IndexSingleCellNamedRangeReferenceTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet SheetWithColumn()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10)); // A1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20)); // A2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30)); // A3
        return sheet;
    }

    [Fact]
    public void Offset_WithIndexOfSingleCellBaseReference_ShiftsFromTheIndexedCell()
    {
        // INDEX(A1,1) is a reference to A1 itself (the sole row of a 1x1 reference); OFFSET
        // shifts it down 1 row to A2 (=20). Before the fix this returned #VALUE! because
        // EvaluateIndexAsReference could not resolve a bare CellRefNode source.
        var sheet = SheetWithColumn();

        _eval.Evaluate("=OFFSET(INDEX(A1,1),1,0)", sheet).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Cell_AddressOfIndexOfSingleCellBaseReference_ReturnsThatCellAddress()
    {
        var sheet = SheetWithColumn();

        _eval.Evaluate("=CELL(\"address\",INDEX(A1,1))", sheet).Should().Be(new TextValue("$A$1"));
    }

    [Fact]
    public void Offset_WithIndexOfNamedSingleCellBaseReference_ShiftsFromTheNamedCell()
    {
        // MyCell names A1; INDEX(MyCell,1) is a reference to A1 itself, and OFFSET shifts it
        // down 1 row to A2 (=20). Before the fix this returned #VALUE! because
        // EvaluateIndexAsReference could not resolve a NamedRangeNode source.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10)); // A1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20)); // A2
        workbook.DefineNamedRange("MyCell", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1)));

        var result = _eval.Evaluate("=OFFSET(INDEX(MyCell,1),1,0)", sheet, workbook);

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Offset_WithIndexOfNamedMultiCellRangeBaseReference_ShiftsFromTheSelectedCell()
    {
        // MyRange names A1:A3; INDEX(MyRange,2) selects A2, and OFFSET shifts it down 1 row
        // to A3 (=30). Exercises the NamedRangeNode source path when INDEX's row argument
        // selects something other than the whole (1x1) name.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10)); // A1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20)); // A2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30)); // A3
        workbook.DefineNamedRange("MyRange", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        var result = _eval.Evaluate("=OFFSET(INDEX(MyRange,2),1,0)", sheet, workbook);

        result.Should().Be(new NumberValue(30));
    }

    [Fact]
    public void IsRef_OfIndexOfSingleCellBaseReference_ReturnsTrue()
    {
        var sheet = SheetWithColumn();

        _eval.Evaluate("=ISREF(INDEX(A1,1))", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Offset_WithIndexOfRangeBaseReference_StillWorks_SiblingNoRegression()
    {
        // Pre-existing R55 idiom (RangeRefNode source, not a bare CellRefNode/NamedRangeNode) --
        // must keep working unchanged after adding the two new source shapes above.
        var sheet = SheetWithColumn();

        _eval.Evaluate("=OFFSET(INDEX(A1:A3,2),1,0)", sheet).Should().Be(new NumberValue(30));
    }
}
