using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// ── Regression coverage for group B-stale-colnumber (finding J2) ──────────────
//
// FormulaEvaluator.ShiftFormulaForCell shifts a Custom Data Validation formula's
// relative references from the rule's anchor cell to the cell actually being
// validated. The shifted CellRefNode must carry a ColumnNumber that agrees with
// its (possibly new) ColumnName — previously the shift used `cr with { ... }`,
// which left the ColumnNumber backing field stale at the pre-shift value because
// CellRefNode.ColumnNumber is computed via a field initializer that a record
// `with` expression does not re-run. That silently validated the WRONG column's
// value (the anchor's column, not the edited cell's column) for any multi-column
// AppliesTo range with a column-relative reference.
public partial class DataValidationTests
{
    [Fact]
    public void Validate_CustomFormula_ShiftsRelativeColumnReference_WhenValidatingNonAnchorColumn()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Rule authored at anchor A1, applied across A1:C1 (column-relative shift).
        var anchorAddr    = new CellAddress(sheet.Id, 1, 1); // A1
        var validatedAddr = new CellAddress(sheet.Id, 1, 3); // C1

        // A1 holds a value that would satisfy the rule if (due to the bug) the
        // stale ColumnNumber caused column A to be checked instead of column C.
        sheet.SetCell(anchorAddr, Cell.FromValue(new NumberValue(100)));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(anchorAddr, new CellAddress(sheet.Id, 1, 3)),
            Type      = DvType.Custom,
            Formula1  = "A1>10", // relative column; shifts to C1>10 when validating C1
            ErrorMessage = "Must be greater than 10."
        };

        // Validating C1 = 5: correct shifted formula is C1>10 → FALSE → rejected.
        // Under the bug, the stale ColumnNumber kept checking A1 (=100>10 → TRUE),
        // so an invalid value at C1 would be silently accepted.
        DataValidationService.Validate(dv, new NumberValue(5), sheet, validatedAddr, workbook)
            .Should().Be("Must be greater than 10.", "C1=5 fails the shifted C1>10 rule regardless of A1's value");

        // Validating C1 = 20: correct shifted formula is C1>10 → TRUE (20>10) → accepted.
        DataValidationService.Validate(dv, new NumberValue(20), sheet, validatedAddr, workbook)
            .Should().BeNull("C1=20 satisfies the shifted C1>10 rule");
    }

    [Fact]
    public void Validate_CustomFormula_ShiftsRelativeColumnReference_NonComparisonFormula()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Anchor A1, applied across A1:C1. ISNUMBER(A1) is not a simple comparison,
        // so it exercises the general AST-shift path (ShiftCellRefOrError) directly.
        var anchorAddr    = new CellAddress(sheet.Id, 1, 1); // A1
        var validatedAddr = new CellAddress(sheet.Id, 1, 3); // C1

        // A1 is text — if the stale ColumnNumber bug re-checks column A instead of C,
        // ISNUMBER(A1) would evaluate FALSE even when C1 itself holds a number.
        sheet.SetCell(anchorAddr, Cell.FromValue(new TextValue("not a number")));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(anchorAddr, new CellAddress(sheet.Id, 1, 3)),
            Type      = DvType.Custom,
            Formula1  = "ISNUMBER(A1)",
            ErrorMessage = "Numbers only."
        };

        DataValidationService.Validate(dv, new NumberValue(42), sheet, validatedAddr, workbook)
            .Should().BeNull("C1 holds a number, so the shifted ISNUMBER(C1) must evaluate TRUE");

        DataValidationService.Validate(dv, new TextValue("oops"), sheet, validatedAddr, workbook)
            .Should().Be("Numbers only.", "C1 holds text, so the shifted ISNUMBER(C1) must evaluate FALSE");
    }
}
