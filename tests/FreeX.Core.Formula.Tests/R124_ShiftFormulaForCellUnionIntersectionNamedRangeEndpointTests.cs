using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R124-formula-shift-1: <see cref="FormulaEvaluator.ShiftFormulaForCell"/> re-anchors a
/// multi-cell conditional-format/data-validation/relative-named-formula formula's relative
/// references from an anchor cell to the cell actually being evaluated. Before this fix,
/// HasRelativeReferences and ShiftAst (FormulaEvaluator.Shifting.cs) only matched CellRefNode,
/// RangeRefNode, FullColumnRangeRefNode, FullRowRangeRefNode, BinaryOpNode, UnaryOpNode, and
/// FunctionCallNode -- every UnionNode ("(A1:A5,C1:C5)"), IntersectionNode ("A1:C10 A5:E5"), and
/// NamedRangeEndpointNode ("A1:SomeName") fell into the `_ => false` / `_ => node` catch-all, so
/// HasRelativeReferences silently reported "no relative references" for a formula whose ONLY
/// relative reference was nested inside one of those three node kinds, and ShiftAst left that
/// sub-tree completely untouched even when some sibling reference did trigger a shift.
/// </summary>
public sealed class R124_ShiftFormulaForCellUnionIntersectionNamedRangeEndpointTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void UnionNode_RelativeReferencesInsideUnion_ShiftPerCell()
    {
        // "=SUM((A2,C2))" authored at anchor B2; when re-anchored to B3 (dr=+1) it must
        // become the equivalent of "=SUM((A3,C3))" -- summing row 3, not row 2.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));   // A2
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(2));   // C2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100)); // A3
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(200)); // C3

        var ast = FormulaEvaluator.ParseFormula("=SUM((A2,C2))");
        var anchor = new CellAddress(sheet.Id, 2, 2);  // B2
        var current = new CellAddress(sheet.Id, 3, 2); // B3

        var shifted = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, current);

        // The bug: HasRelativeReferences never looked inside the UnionNode, so ShiftFormulaForCell
        // returned the ORIGINAL ast unchanged and this evaluates against row 2 (=3) instead of row 3.
        _eval.Evaluate(shifted, sheet, workbook, currentCell: current)
            .Should().Be(new NumberValue(300), "the union's A2/C2 refs must re-anchor to A3/C3 for the shifted cell");
    }

    [Fact]
    public void IntersectionNode_RelativeReferencesInsideIntersection_ShiftPerCell()
    {
        // "=SUM(A1:C10 A5:E5)" (space = intersection operator) authored at anchor A1; re-anchored
        // to A2 (dr=+1) both operands must shift to "A2:C11 A6:E6", matching how
        // FormulaRewriter.Rewrite shifts the identical shape for Insert Rows (R66).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 6; r++)
            for (uint c = 1; c <= 5; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new NumberValue(r * 10 + c));

        var ast = FormulaEvaluator.ParseFormula("=SUM(A1:C10 A5:E5)");
        var anchor = new CellAddress(sheet.Id, 1, 1);  // A1
        var current = new CellAddress(sheet.Id, 2, 1); // A2

        var shifted = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, current);

        // Correctly shifted: A2:C11 (clamped by real data to rows 2-6) intersected with A6:E6
        // overlaps only at row 6, columns A-C: 61+62+63 = 186.
        // Bug: IntersectionNode fell through `_ => false`, so the ast was returned unshifted and
        // evaluates the anchor's own A1:C10 ∩ A5:E5 = row 5, columns A-C = 51+52+53 = 156.
        _eval.Evaluate(shifted, sheet, workbook, currentCell: current)
            .Should().Be(new NumberValue(61 + 62 + 63));
    }

    [Fact]
    public void NamedRangeEndpointNode_RelativeCellRefEndpointShifts_NameEndpointStaysAtItsCorner()
    {
        // "=SUM(A2:CornerName)" authored at anchor B2; CornerName is a defined name (resolves to
        // C4 and is NEVER shifted -- only literal cell refs shift). Re-anchoring to B4 (dr=+2)
        // must shift the CellRefNode endpoint A2 -> A4, producing the range A4:C4.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var c4 = new CellAddress(sheet.Id, 4, 3);
        workbook.DefineNamedRange("CornerName", new GridRange(c4, c4));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(999)); // A2 - must NOT be counted once shifted
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(111)); // A4 - the correctly-shifted start
        sheet.SetCell(c4, new NumberValue(222));                             // C4 - the fixed named-range corner

        var ast = FormulaEvaluator.ParseFormula("=SUM(A2:CornerName)");
        var anchor = new CellAddress(sheet.Id, 2, 2);  // B2
        var current = new CellAddress(sheet.Id, 4, 2); // B4

        var shifted = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, current);

        // Correct: SUM(A4:C4) = 111 + 0 + 222 = 333.
        // Bug: NamedRangeEndpointNode fell through `_ => false`, so the ast stayed unshifted and
        // evaluates SUM(A2:C4) = 999 + ... + 111 + ... + 222 = 1332.
        _eval.Evaluate(shifted, sheet, workbook, currentCell: current)
            .Should().Be(new NumberValue(333));
    }

    [Fact]
    public void PartialFormula_SiblingCellRefShiftsButUnionSubtree_MustAlsoShift()
    {
        // "=A1+SUM((B1,D1))" -- the bare A1 term already makes HasRelativeReferences true (so
        // ShiftAst runs), but before this fix the nested UnionNode sub-tree was still left
        // completely untouched by ShiftAst's `_ => node` default, freezing B1/D1 at the anchor's
        // literal text for every row while the sibling A1 term shifted normally.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));   // A1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));   // B1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(3));   // D1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100)); // A2
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(200)); // B2
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(300)); // D2

        var ast = FormulaEvaluator.ParseFormula("=A1+SUM((B1,D1))");
        var anchor = new CellAddress(sheet.Id, 1, 1);  // A1
        var current = new CellAddress(sheet.Id, 2, 1); // A2

        var shifted = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, current);

        // Fully shifted: A2 + SUM((B2,D2)) = 100 + 200 + 300 = 600.
        // Bug (partial shift): A2 + SUM((B1,D1)) [union frozen at anchor] = 100 + 2 + 3 = 105.
        _eval.Evaluate(shifted, sheet, workbook, currentCell: current)
            .Should().Be(new NumberValue(600));
    }

    // ── No-regression sibling ────────────────────────────────────────────────────────────────

    [Fact]
    public void UnionNode_AllAbsoluteReferences_NeverShifts_NoRegression()
    {
        // "=SUM(($A$2,$C$2))" -- every reference inside the union is absolute, so
        // HasRelativeReferences must still correctly report false and the formula must evaluate
        // identically at every cell, exactly like a plain absolute CellRefNode already does.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));  // A2
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(7));  // C2

        var ast = FormulaEvaluator.ParseFormula("=SUM(($A$2,$C$2))");
        var anchor = new CellAddress(sheet.Id, 2, 2);   // B2
        var current = new CellAddress(sheet.Id, 10, 9); // I10 -- far away

        var shifted = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, current);

        ReferenceEquals(shifted, ast).Should().BeTrue(
            "an all-absolute union must be recognized as having no relative references and returned unchanged");
        _eval.Evaluate(shifted, sheet, workbook, currentCell: current).Should().Be(new NumberValue(12));
    }
}
