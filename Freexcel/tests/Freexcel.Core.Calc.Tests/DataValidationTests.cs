using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public class DataValidationTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static (Workbook workbook, Sheet sheet) MakeWorkbook()
    {
        var wb = new Workbook("test");
        var sh = wb.AddSheet("Sheet1");
        return (wb, sh);
    }

    private static GridRange MakeSingleCellRange(Sheet sheet, uint row, uint col)
    {
        var addr = new CellAddress(sheet.Id, row, col);
        return new GridRange(addr, addr);
    }

    // ─── List validation ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_List_AcceptsValueInList()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Apple"));

        result.Should().BeNull("Apple is in the allowed list");
    }

    [Fact]
    public void Validate_List_RejectsValueNotInList()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Mango"));

        result.Should().NotBeNull("Mango is not in the allowed list");
    }

    [Fact]
    public void Validate_List_BlankAllowed_WhenAllowBlankTrue()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, BlankValue.Instance);

        result.Should().BeNull("blank is allowed when AllowBlank=true");
    }

    [Fact]
    public void Validate_List_BlankRejected_WhenAllowBlankFalse()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = false,
        };

        var result = DataValidationService.Validate(dv, BlankValue.Instance);

        result.Should().NotBeNull("blank should be rejected when AllowBlank=false");
    }

    [Fact]
    public void Validate_List_CaseInsensitive()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("apple"));

        result.Should().BeNull("matching should be case-insensitive");
    }

    // ─── WholeNumber validation ───────────────────────────────────────────────

    [Fact]
    public void Validate_WholeNumber_Between_AcceptsInRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(5));

        result.Should().BeNull("5 is between 1 and 10");
    }

    [Fact]
    public void Validate_WholeNumber_Between_RejectsOutOfRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(15));

        result.Should().NotBeNull("15 is outside the range 1-10");
    }

    [Fact]
    public void Validate_WholeNumber_GreaterThan_AcceptsLargerValue()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(1));

        result.Should().BeNull("1 > 0");
    }

    [Fact]
    public void Validate_WholeNumber_GreaterThan_RejectsSmallerValue()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(-1));

        result.Should().NotBeNull("-1 is not > 0");
    }

    // ─── TextLength validation ────────────────────────────────────────────────

    [Fact]
    public void Validate_TextLength_LessThan_AcceptsShortText()
    {
        var dv = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThan,
            Formula1 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Hi"));

        result.Should().BeNull("'Hi' has length 2 which is < 10");
    }

    [Fact]
    public void Validate_TextLength_LessThan_RejectsLongText()
    {
        var dv = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThan,
            Formula1 = "5",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Hello World"));

        result.Should().NotBeNull("'Hello World' has length 11 which is not < 5");
    }

    // ─── Any validation ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_Any_AlwaysAccepts()
    {
        var dv = new DataValidation { Type = DvType.Any };

        DataValidationService.Validate(dv, new NumberValue(99)).Should().BeNull();
        DataValidationService.Validate(dv, new TextValue("x")).Should().BeNull();
        DataValidationService.Validate(dv, BlankValue.Instance).Should().BeNull();
    }

    // ─── GetApplicable ────────────────────────────────────────────────────────

    [Fact]
    public void GetApplicable_ReturnsOnlyRulesContainingAddress()
    {
        var (_, sheet) = MakeWorkbook();

        var dv1 = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.List,
            Formula1 = "A,B,C",
        };
        var dv2 = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 2, 2),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
        };
        sheet.DataValidations.Add(dv1);
        sheet.DataValidations.Add(dv2);

        var addr1 = new CellAddress(sheet.Id, 1, 1);
        var addr2 = new CellAddress(sheet.Id, 2, 2);
        var addr3 = new CellAddress(sheet.Id, 3, 3);

        DataValidationService.GetApplicable(sheet, addr1).Should().ContainSingle()
            .Which.Should().Be(dv1, "only dv1 covers A1");
        DataValidationService.GetApplicable(sheet, addr2).Should().ContainSingle()
            .Which.Should().Be(dv2, "only dv2 covers B2");
        DataValidationService.GetApplicable(sheet, addr3).Should().BeEmpty("no rule covers C3");
    }

    // ─── SetDataValidationCommand ─────────────────────────────────────────────

    [Fact]
    public void SetDataValidationCommand_Apply_AddsRule()
    {
        var (wb, sheet) = MakeWorkbook();

        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, dv));

        sheet.DataValidations.Should().ContainSingle().Which.Id.Should().Be(dv.Id);
    }

    [Fact]
    public void SetDataValidationCommand_Revert_RemovesRule()
    {
        var (wb, sheet) = MakeWorkbook();

        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, dv));
        sheet.DataValidations.Should().HaveCount(1);

        bus.Undo(wb.Id);
        sheet.DataValidations.Should().BeEmpty("revert should remove the rule");
    }

    [Fact]
    public void SetDataValidationCommand_Revert_RestoresPreviousRule()
    {
        var (wb, sheet) = MakeWorkbook();

        var range = MakeSingleCellRange(sheet, 1, 1);
        var original = new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "A,B",
        };
        // Pre-seed a rule with the same Id
        sheet.DataValidations.Add(original);

        var replacement = new DataValidation
        {
            Id = original.Id,           // same Id = replace
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, replacement));
        sheet.DataValidations.Should().ContainSingle().Which.Formula1.Should().Be("X,Y,Z");

        bus.Undo(wb.Id);
        sheet.DataValidations.Should().ContainSingle().Which.Formula1.Should().Be("A,B",
            "revert should restore the original rule");
    }

    // ─── Decimal validation ───────────────────────────────────────────────────

    [Fact]
    public void Validate_Decimal_Between_AcceptsInRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "0.5",
            Formula2 = "9.5",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(5.0)).Should().BeNull("5.0 is between 0.5 and 9.5");
        DataValidationService.Validate(dv, new NumberValue(10.0)).Should().NotBeNull("10.0 is outside the range");
    }

    // ─── minimal test helpers ─────────────────────────────────────────────────

    private sealed class TestCommandContext(Workbook wb) : ICommandContext
    {
        public Workbook Workbook => wb;
        public Sheet GetSheet(SheetId id) => wb.GetSheet(id)!;
    }
}
