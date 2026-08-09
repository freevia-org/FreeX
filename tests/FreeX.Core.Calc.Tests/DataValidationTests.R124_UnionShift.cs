using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// R124-formula-shift-1 (DV path): DataValidationService.ValidateCustom calls the shared
// FormulaEvaluator.ShiftFormulaForCell to re-anchor a Custom-formula rule's relative references
// from AppliesTo.Start to the cell actually being validated. Before this fix, a relative reference
// nested inside a UnionNode ("(A1,C1)") was never detected as relative by HasRelativeReferences,
// so the rule silently validated every cell in AppliesTo against the anchor cell's OWN literal
// formula instead of each cell's own re-anchored formula.
public partial class DataValidationTests
{
    [Fact]
    public void Validate_CustomFormula_UnionInsideFormula_ShiftsToTheValidatedCell()
    {
        var (workbook, sheet) = MakeWorkbook();

        var anchorAddr = new CellAddress(sheet.Id, 1, 1);    // A1 (rule anchor)
        var validatedAddr = new CellAddress(sheet.Id, 2, 1); // A2 (cell actually being validated)

        // Anchor row holds values that would satisfy the rule if (due to the bug) the literal,
        // unshifted anchor formula kept being evaluated for every validated cell.
        sheet.SetCell(anchorAddr, Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(0))); // C1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new NumberValue(0))); // C2

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(anchorAddr, new CellAddress(sheet.Id, 3, 1)),
            Type = DvType.Custom,
            Formula1 = "SUM((A1,C1))>5", // relative union; shifts to SUM((A2,C2))>5 when validating A2
            ErrorMessage = "Must satisfy the union rule."
        };

        // Validating A2 with candidate 10: correctly-shifted rule is SUM((A2,C2))>5 with the
        // candidate written into A2 -> 10+0=10>5 -> TRUE -> accepted (null).
        // Bug: the union was never detected as relative, so the literal SUM((A1,C1))>5 kept being
        // evaluated against the REAL (untouched) A1=0/C1=0 -> 0>5 -> FALSE -> wrongly rejected.
        DataValidationService.Validate(dv, new NumberValue(10), sheet, validatedAddr, workbook)
            .Should().BeNull("the union must re-anchor to (A2,C2) and see the candidate value written into A2");
    }

    // ── No-regression sibling ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_CustomFormula_UnionOfAbsoluteReferences_NeverShifts_NoRegression()
    {
        var (workbook, sheet) = MakeWorkbook();

        var anchorAddr = new CellAddress(sheet.Id, 1, 1);    // A1
        var validatedAddr = new CellAddress(sheet.Id, 2, 1); // A2

        sheet.SetCell(anchorAddr, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(0))); // C1

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(anchorAddr, new CellAddress(sheet.Id, 3, 1)),
            Type = DvType.Custom,
            Formula1 = "SUM(($A$1,$C$1))>5", // absolute union — must stay anchored to A1/C1 always
            ErrorMessage = "Must satisfy the union rule."
        };

        // Validating A2 (candidate value is irrelevant here since the absolute union never reads
        // A2 at all) still evaluates SUM($A$1,$C$1)=10>5=TRUE regardless of position.
        DataValidationService.Validate(dv, new NumberValue(-999), sheet, validatedAddr, workbook)
            .Should().BeNull("$A$1/$C$1 are absolute, so the rule always evaluates the same literal union");
    }
}
