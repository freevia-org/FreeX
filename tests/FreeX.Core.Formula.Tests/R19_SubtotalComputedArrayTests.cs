using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R19-formula-functions-edge-1: SUBTOTAL indexes into the CURRENT sheet's hidden-row state and
/// cell-formula-text using the RangeValue's StartRow/StartCol. That is correct for a genuine
/// worksheet reference, but a computed/virtual array (e.g. FILTER's result) has no real position
/// on the sheet, so RangeValue defaults StartRow/StartCol to 1 and leaves SheetName null — SUBTOTAL
/// then mistook a computed array's first element for "row 1 of the current sheet" and silently
/// dropped it whenever row 1 happened to be hidden, even though the element's true source could be
/// any row of the underlying range. Fixed in BuiltInFunctions.Subtotal.cs by only applying the
/// hidden-row/nested-formula exclusions when the RangeValue is a genuine reference (real SheetName,
/// or a start row/col other than the (1,1) virtual-array default).
/// </summary>
public sealed class R19_subtotal_coords_Tests
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
    public void Subtotal_ComputedArrayOperand_IncludesAllElements_DespiteHiddenSheetRow1()
    {
        // A1:A5 = {10,20,30,40,50}; B1:B5 all 1 so FILTER keeps every row unchanged. Sheet row 1
        // (which has nothing to do with the computed array's own positions) is hidden.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(1)),
            (2, 1, new NumberValue(20)), (2, 2, new NumberValue(1)),
            (3, 1, new NumberValue(30)), (3, 2, new NumberValue(1)),
            (4, 1, new NumberValue(40)), (4, 2, new NumberValue(1)),
            (5, 1, new NumberValue(50)), (5, 2, new NumberValue(1)));
        sheet.GroupHiddenRows.Add(1);

        // Pre-fix: SUBTOTAL mistook the computed array's element 0 for "sheet row 1", which is
        // hidden, and dropped it -> 20+30+40+50 = 140. Real Excel / post-fix: a virtual array has
        // no sheet position, so hidden-row exclusion must not apply -> 150.
        var result = _eval.Evaluate("=SUBTOTAL(109,FILTER(A1:A5,B1:B5>0))", sheet);

        result.Should().Be(new NumberValue(150));
    }

    [Fact]
    public void Subtotal_ComputedArrayOperand_NestedSubtotalTextAtHiddenDefaultCoordDoesNotExclude()
    {
        // Cell A1 itself literally holds the text of a SUBTOTAL(...) formula (simulating the
        // nested-SUBTOTAL exclusion trigger), but the computed array passed to the outer SUBTOTAL
        // is a virtual array (SEQUENCE) whose values are unrelated to A1 and which only coincidentally
        // shares A1's default (1,1) coordinate. The nested-formula exclusion must not fire for it.
        var sheet = MakeSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(999));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "=SUBTOTAL(9,B1:B2)");

        // SEQUENCE(3) => {1;2;3}; SUBTOTAL(9, ...) should sum all three => 6, not skip element 1
        // because it happens to land on default coordinate (1,1) which holds a nested SUBTOTAL formula.
        var result = _eval.Evaluate("=SUBTOTAL(9,SEQUENCE(3))", sheet);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Subtotal_GenuineCrossSheetReference_StillExcludesHiddenRow_ForStatisticalFunction()
    {
        // Statistical SUBTOTAL function numbers (107 = STDEV.S here) are not handled by the direct-
        // range fast path, so a genuine reference reaches the same coordinate-aware slow path that
        // computed arrays go through. This must still correctly exclude a genuinely hidden row,
        // proving the fix distinguishes real references from computed arrays rather than disabling
        // the exclusion outright.
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var data = workbook.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 1), new NumberValue(2));
        data.SetCell(new CellAddress(data.Id, 2, 1), new NumberValue(100)); // hidden outlier
        data.SetCell(new CellAddress(data.Id, 3, 1), new NumberValue(4));
        data.SetCell(new CellAddress(data.Id, 4, 1), new NumberValue(4));
        data.GroupHiddenRows.Add(2);

        var withHiddenExcluded = _eval.Evaluate("=SUBTOTAL(107,Data!A1:A4)", sheet1, workbook);
        var withHiddenIncluded = _eval.Evaluate("=SUBTOTAL(7,Data!A1:A4)", sheet1, workbook);

        // Excluding the hidden 100 leaves {2,4,4}: a small, tight sample stdev.
        withHiddenExcluded.Should().BeOfType<NumberValue>()
            .Which.Value.Should().BeApproximately(1.1547005383792515, 1e-9);
        // Including the hidden 100 must differ (and be much larger), proving the hidden row was
        // genuinely read for the non-100-prefixed variant while excluded for the 100-prefixed one.
        withHiddenIncluded.Should().BeOfType<NumberValue>()
            .Which.Value.Should().NotBeApproximately(1.1547005383792515, 1e-6);
    }
}
