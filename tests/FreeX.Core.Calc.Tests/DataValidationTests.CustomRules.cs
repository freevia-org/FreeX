using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class DataValidationTests
{
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
}
