using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class DataValidationTests
{
    // ── Custom formula — basic acceptance/rejection ───────────────────────────

    [Fact]
    public void Validate_CustomFormula_AcceptsWhenFormulaEvaluatesTrueForEditedCell()
    {
        var (_, sheet) = MakeWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.Custom,
            Formula1 = "=MOD(A1,2)=0",
            ErrorMessage = "Enter an even number."
        };

        var result = DataValidationService.Validate(dv, new NumberValue(4), sheet, addr);

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_CustomFormula_RejectsWhenFormulaEvaluatesFalseForEditedCell()
    {
        var (_, sheet) = MakeWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.Custom,
            Formula1 = "=MOD(A1,2)=0",
            ErrorMessage = "Enter an even number."
        };

        var result = DataValidationService.Validate(dv, new NumberValue(5), sheet, addr);

        result.Should().Be("Enter an even number.");
    }

    // ── Custom formula — ISNUMBER: text rejected, number accepted ─────────────

    [Fact]
    public void Validate_CustomFormula_IsNumber_AcceptsNumericValue()
    {
        var (workbook, sheet) = MakeWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1); // A1
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.Custom,
            Formula1 = "ISNUMBER(A1)",
            ErrorMessage = "Numbers only."
        };

        // A number value must pass ISNUMBER
        DataValidationService.Validate(dv, new NumberValue(42), sheet, addr, workbook)
            .Should().BeNull("a number value should pass ISNUMBER");
    }

    [Fact]
    public void Validate_CustomFormula_IsNumber_RejectsTextValue()
    {
        var (workbook, sheet) = MakeWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1); // A1
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.Custom,
            Formula1 = "ISNUMBER(A1)",
            ErrorMessage = "Numbers only."
        };

        // A text value must fail ISNUMBER
        DataValidationService.Validate(dv, new TextValue("hello"), sheet, addr, workbook)
            .Should().Be("Numbers only.", "text should fail ISNUMBER");
    }

    // ── Custom formula — relative-reference shift across multi-cell sqref ─────
    //
    // Rule is anchored at A1 (AppliesTo.Start) with Formula1 = "ISNUMBER(A1)".
    // When validating cell A3, the formula must shift to "ISNUMBER(A3)".

    [Fact]
    public void Validate_CustomFormula_ShiftsRelativeReferences_WhenValidatingNonAnchorCell()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Rule covers A1:A5, authored relative to anchor A1
        var anchorAddr    = new CellAddress(sheet.Id, 1, 1); // A1
        var validatedAddr = new CellAddress(sheet.Id, 3, 1); // A3

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(anchorAddr, new CellAddress(sheet.Id, 5, 1)),
            Type      = DvType.Custom,
            Formula1  = "ISNUMBER(A1)",   // authored for anchor; should shift to ISNUMBER(A3)
            ErrorMessage = "Numbers only."
        };

        // Validating A3 with a number: ISNUMBER(A3) → TRUE → passes
        DataValidationService.Validate(dv, new NumberValue(7), sheet, validatedAddr, workbook)
            .Should().BeNull("number at A3 should pass the shifted ISNUMBER(A3) formula");

        // Validating A3 with text: ISNUMBER(A3) → FALSE → fails
        DataValidationService.Validate(dv, new TextValue("oops"), sheet, validatedAddr, workbook)
            .Should().Be("Numbers only.", "text at A3 should fail the shifted ISNUMBER(A3) formula");
    }

    [Fact]
    public void Validate_CustomFormula_ShiftDoesNotAffectAbsoluteReferences()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Seed a fixed helper value in B1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(100)));

        // Rule anchored at A1, uses absolute $B$1 — shift to A5 must NOT move $B$1
        var anchorAddr    = new CellAddress(sheet.Id, 1, 1);
        var validatedAddr = new CellAddress(sheet.Id, 5, 1);

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(anchorAddr, new CellAddress(sheet.Id, 10, 1)),
            Type      = DvType.Custom,
            Formula1  = "A1<=$B$1",   // relative A1 shifts → A5<=$B$1; $B$1 stays fixed at 100
            ErrorMessage = "Must be ≤ 100."
        };

        // 50 ≤ 100 → passes
        DataValidationService.Validate(dv, new NumberValue(50), sheet, validatedAddr, workbook)
            .Should().BeNull("50 ≤ 100 should pass");

        // 150 ≤ 100 → fails
        DataValidationService.Validate(dv, new NumberValue(150), sheet, validatedAddr, workbook)
            .Should().Be("Must be ≤ 100.", "150 > 100 should fail");
    }

    // ── AllowBlank honored by custom formula ──────────────────────────────────

    [Fact]
    public void Validate_CustomFormula_HonorsAllowBlank_WhenTrue()
    {
        var (workbook, sheet) = MakeWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo  = MakeSingleCellRange(sheet, 1, 1),
            Type       = DvType.Custom,
            Formula1   = "ISNUMBER(A1)",
            AllowBlank = true,
            ErrorMessage = "Numbers only."
        };

        // Blank must pass when AllowBlank = true (early-exit before formula evaluation)
        DataValidationService.Validate(dv, BlankValue.Instance, sheet, addr, workbook)
            .Should().BeNull("blank should be allowed when AllowBlank is true");
    }

    [Fact]
    public void Validate_CustomFormula_HonorsAllowBlank_WhenFalse()
    {
        var (workbook, sheet) = MakeWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo  = MakeSingleCellRange(sheet, 1, 1),
            Type       = DvType.Custom,
            Formula1   = "ISNUMBER(A1)",
            AllowBlank = false,
            ErrorMessage = "Numbers only."
        };

        // Blank must be rejected when AllowBlank = false
        DataValidationService.Validate(dv, BlankValue.Instance, sheet, addr, workbook)
            .Should().NotBeNull("blank should be rejected when AllowBlank is false");
    }

    // ── Error/false formula result treated as invalid ─────────────────────────

    [Fact]
    public void Validate_CustomFormula_TreatsFormulaErrorAsInvalid()
    {
        var (workbook, sheet) = MakeWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type      = DvType.Custom,
            Formula1  = "1/0",   // yields #DIV/0! → error → invalid
            ErrorMessage = "Invalid."
        };

        DataValidationService.Validate(dv, new NumberValue(5), sheet, addr, workbook)
            .Should().Be("Invalid.", "a formula error should be treated as invalid");
    }

    // ── No-context overload still returns null for Custom (cannot evaluate) ───

    [Fact]
    public void Validate_CustomFormula_NoContextOverload_ReturnsNull()
    {
        var sheetId = SheetId.New();
        var addr    = new CellAddress(sheetId, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo = new GridRange(addr, addr),
            Type     = DvType.Custom,
            Formula1 = "ISNUMBER(A1)"
        };

        // The context-free overload cannot evaluate formulas; it should not crash or block.
        DataValidationService.Validate(dv, new TextValue("x"))
            .Should().BeNull("without sheet context the rule cannot be evaluated");
    }
}
