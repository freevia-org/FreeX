using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R20-data-validation-eval-2: numeric/date/time/text-length DV bound
/// formulas must be anchor-shifted per cell for a multi-cell rule, exactly like the List/Custom
/// path already does.
///
/// Before the fix, DataValidationBoundsParser.TryEvaluateBoundFormula evaluated the raw
/// Formula1/Formula2 text (e.g. "=A1") with `currentCell: address` only — `currentCell` affects
/// implicit-intersection resolution, not relative-reference rebasing, so the bound was always
/// resolved against the literal cell in the formula text (A1) rather than being shifted
/// row-for-row to match the cell actually being validated (A2 for B2, A3 for B3, etc.).
/// </summary>
public class R20_dv_bounds_anchor_Tests
{
    private static (Workbook workbook, Sheet sheet) MakeSheetWithAnchors()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10)); // A1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20)); // A2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(5));  // A3

        return (workbook, sheet);
    }

    private static DataValidation MakeWholeNumberLessThanOrEqualRule(Sheet sheet)
    {
        var appliesTo = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 3, 2)); // B1:B3

        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.LessThanOrEqual,
            Formula1 = "=A1",
            AppliesTo = appliesTo,
        };

        sheet.DataValidations.Add(dv);
        return dv;
    }

    [Fact]
    public void Validate_WholeNumberBound_ShiftsRelativeReferenceFromAnchorToEachCell()
    {
        var (workbook, sheet) = MakeSheetWithAnchors();
        var dv = MakeWholeNumberLessThanOrEqualRule(sheet);

        var b2 = new CellAddress(sheet.Id, 2, 2);
        var b3 = new CellAddress(sheet.Id, 3, 2);

        // B2's bound must resolve against A2 (=20), not the literal A1 (=10) in the formula
        // text: 15 <= 20 is true, so this must be ACCEPTED.
        DataValidationService.Validate(dv, new NumberValue(15), sheet, b2, workbook)
            .Should().BeNull("15 <= A2 (20) should pass once the bound is anchor-shifted to row 2");

        // B3's bound must resolve against A3 (=5), not A1 (=10): 8 <= 5 is false, so this must
        // be REJECTED. Pre-fix, this wrongly passed against the un-shifted bound A1=10.
        DataValidationService.Validate(dv, new NumberValue(8), sheet, b3, workbook)
            .Should().NotBeNull("8 <= A3 (5) should fail once the bound is anchor-shifted to row 3");
    }

    [Fact]
    public void Validate_WholeNumberBound_AnchorCellItselfStillUsesUnshiftedFormula()
    {
        var (workbook, sheet) = MakeSheetWithAnchors();
        var dv = MakeWholeNumberLessThanOrEqualRule(sheet);

        var b1 = new CellAddress(sheet.Id, 1, 2); // The rule's own anchor cell.

        // At the anchor cell (B1), the formula "=A1" needs no shift at all: bound is A1 (=10).
        DataValidationService.Validate(dv, new NumberValue(10), sheet, b1, workbook)
            .Should().BeNull("10 <= A1 (10) should pass at the anchor cell");

        DataValidationService.Validate(dv, new NumberValue(11), sheet, b1, workbook)
            .Should().NotBeNull("11 <= A1 (10) should fail at the anchor cell");
    }
}
