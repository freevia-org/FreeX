using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public sealed class DataValidationCirclePlannerTests
{
    [Fact]
    public void FindInvalidDataCells_ReturnsOnlyOccupiedCellsThatFailValidation()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(15));
        sheet.SetCell(a2, new NumberValue(5));
        sheet.SetCell(a3, new TextValue("not covered"));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(a1, a2),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        });

        DataValidationCirclePlanner.FindInvalidDataCells(workbook, sheet)
            .Should()
            .Equal(a1);
    }

    [Fact]
    public void FindInvalidDataCells_UsesRangeBackedListValidation()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var source1 = new CellAddress(sheet.Id, 1, 4);
        var source2 = new CellAddress(sheet.Id, 2, 4);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var c2 = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(source1, new TextValue("Apple"));
        sheet.SetCell(source2, new TextValue("Banana"));
        sheet.SetCell(c1, new TextValue("Mango"));
        sheet.SetCell(c2, new TextValue("Banana"));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(c1, c2),
            Type = DvType.List,
            Formula1 = "=$D$1:$D$2"
        });

        DataValidationCirclePlanner.FindInvalidDataCells(workbook, sheet)
            .Should()
            .Equal(c1);
    }

    [Fact]
    public void FindInvalidDataCells_ValidatesFormulaResultCells()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var formulaCell = Cell.FromFormula("10+5");
        formulaCell.Value = new NumberValue(15);
        sheet.SetCell(a1, formulaCell);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(a1, a1),
            Type = DvType.Decimal,
            Operator = DvOperator.LessThan,
            Formula1 = "10"
        });

        DataValidationCirclePlanner.FindInvalidDataCells(workbook, sheet)
            .Should()
            .Equal(a1);
    }
}
