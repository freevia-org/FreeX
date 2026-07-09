using FreeX.App.Host;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R20-data-validation-eval-1: data validation must be checked against the
/// COMPUTED result of a typed formula, not the placeholder <c>BlankValue.Instance</c> that a freshly
/// parsed <see cref="Cell.FromFormula"/> cell carries until the (asynchronous) calc engine catches up.
///
/// The real bug lived in the private, WPF/STA-only <c>MainWindow.TryCreateCellFromEntryText</c>, which
/// cannot be exercised headlessly without hanging (see round-20 fix guidance). The fix extracted the
/// value-computation decision into <see cref="MainWindow.ComputeValueForValidation"/> — an internal,
/// static, non-UI helper (exposed to this assembly via InternalsVisibleTo) — so these tests drive that
/// exact production decision path directly against <see cref="DataValidationService"/>.
/// </summary>
public sealed class R20_dv_formula_result_Tests
{
    private static (Workbook workbook, Sheet sheet) MakeWorkbook()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    [Fact]
    public void ComputeValueForValidation_EvaluatesTypedFormula_InsteadOfLeavingPlaceholderBlank()
    {
        var (workbook, sheet) = MakeWorkbook();
        var b1 = new CellAddress(sheet.Id, 1, 2);

        var newCell = CellEntryParser.CreateCell("=100", b1, useR1C1ReferenceStyle: false);

        // Root cause: a freshly-parsed formula cell's Value is left at the default blank until the
        // calc engine runs later, asynchronously.
        newCell.HasFormula.Should().BeTrue();
        newCell.Value.Should().Be(BlankValue.Instance);

        var value = MainWindow.ComputeValueForValidation(newCell, sheet, workbook, b1);

        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value).Value.Should().Be(100);
    }

    [Fact]
    public void FormulaEntry_OutOfRange_IsRejected_WhenAllowBlankTrue()
    {
        // Exact failure scenario: B1 has a WholeNumber Between 1-10 rule with AllowBlank=true (the DV
        // dialog default). Typing "=100" must be evaluated (100) and rejected as out of range — not
        // silently allowed because the pre-computed placeholder value is blank.
        var (workbook, sheet) = MakeWorkbook();
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var dv = new DataValidation
        {
            AppliesTo = new GridRange(b1, b1),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };
        sheet.DataValidations.Add(dv);

        var newCell = CellEntryParser.CreateCell("=100", b1, useR1C1ReferenceStyle: false);
        var value = MainWindow.ComputeValueForValidation(newCell, sheet, workbook, b1);

        DataValidationService.Validate(dv, value, sheet, b1, workbook)
            .Should().NotBeNull("100 is outside the 1-10 range and must be rejected, matching Excel");

        // Demonstrates the pre-fix bug this replaces: validating the cell's raw (still-blank) Value
        // instead of the computed formula result silently bypasses the rule because AllowBlank is true.
        DataValidationService.Validate(dv, newCell.Value, sheet, b1, workbook)
            .Should().BeNull("this is the buggy pre-fix behavior being guarded against, not the desired one");
    }

    [Fact]
    public void FormulaEntry_InRange_IsAccepted_WhenAllowBlankFalse()
    {
        // Conversely: AllowBlank=false must not wrongly reject a valid formula result just because the
        // placeholder value looks blank before the formula is evaluated.
        var (workbook, sheet) = MakeWorkbook();
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var dv = new DataValidation
        {
            AppliesTo = new GridRange(b1, b1),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
        };
        sheet.DataValidations.Add(dv);

        var newCell = CellEntryParser.CreateCell("=5", b1, useR1C1ReferenceStyle: false);
        var value = MainWindow.ComputeValueForValidation(newCell, sheet, workbook, b1);

        DataValidationService.Validate(dv, value, sheet, b1, workbook)
            .Should().BeNull("5 satisfies the 1-10 rule and must be accepted, matching Excel");

        // Demonstrates the pre-fix bug this replaces: validating the raw (still-blank) Value with
        // AllowBlank=false wrongly rejects the entry with "A value is required."
        DataValidationService.Validate(dv, newCell.Value, sheet, b1, workbook)
            .Should().NotBeNull("this is the buggy pre-fix behavior being guarded against, not the desired one");
    }
}
