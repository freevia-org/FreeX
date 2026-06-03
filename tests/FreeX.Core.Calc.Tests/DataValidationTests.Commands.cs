using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class DataValidationTests
{
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
    public void SetDataValidationCommand_Apply_ReplacesExistingRuleOnSameRange()
    {
        var (wb, sheet) = MakeWorkbook();
        var range = MakeSingleCellRange(sheet, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "A,B",
        });
        var replacement = new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, replacement));

        sheet.DataValidations.Should().ContainSingle()
            .Which.Formula1.Should().Be("X,Y,Z",
                "applying validation to the same range should replace the previous rule instead of stacking rules");
    }

    [Fact]
    public void SetDataValidationCommand_Revert_RestoresRuleReplacedBySameRange()
    {
        var (wb, sheet) = MakeWorkbook();
        var range = MakeSingleCellRange(sheet, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "A,B",
        });
        var replacement = new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, replacement));
        bus.Undo(wb.Id);

        sheet.DataValidations.Should().ContainSingle()
            .Which.Formula1.Should().Be("A,B",
                "undo should restore the rule that was replaced for the same range");
    }

    [Fact]
    public void ClearDataValidationCommand_Apply_RemovesRulesIntersectingSelection()
    {
        var (wb, sheet) = MakeWorkbook();
        var targetRange = MakeSingleCellRange(sheet, 1, 1);
        var unrelatedRange = MakeSingleCellRange(sheet, 3, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = targetRange,
            Type = DvType.List,
            Formula1 = "A,B",
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = unrelatedRange,
            Type = DvType.List,
            Formula1 = "X,Y",
        });

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new ClearDataValidationCommand(sheet.Id, targetRange));

        sheet.DataValidations.Should().ContainSingle()
            .Which.Formula1.Should().Be("X,Y",
                "Clear All should remove validation from the selected range without touching unrelated validation rules");
    }

    [Fact]
    public void ClearDataValidationCommand_Revert_RestoresClearedRules()
    {
        var (wb, sheet) = MakeWorkbook();
        var targetRange = MakeSingleCellRange(sheet, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = targetRange,
            Type = DvType.List,
            Formula1 = "A,B",
        });

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new ClearDataValidationCommand(sheet.Id, targetRange));
        bus.Undo(wb.Id);

        sheet.DataValidations.Should().ContainSingle()
            .Which.Formula1.Should().Be("A,B",
                "undo should restore validation rules cleared from the selection");
    }

    [Fact]
    public void ClearDataValidationCommand_Apply_PreservesUnselectedPartsOfLargerRule()
    {
        var (wb, sheet) = MakeWorkbook();
        var originalRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var clearRange = MakeSingleCellRange(sheet, 2, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = originalRange,
            Type = DvType.List,
            Formula1 = "A,B",
        });

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new ClearDataValidationCommand(sheet.Id, clearRange));

        sheet.DataValidations.Should().HaveCount(2);
        sheet.DataValidations.Select(rule => rule.AppliesTo.ToString())
            .Should().BeEquivalentTo(["A1:A1", "A3:A3"],
                "only the selected middle cell should lose validation");
        sheet.DataValidations.Should().OnlyContain(rule => rule.Formula1 == "A,B");
    }

    [Fact]
    public void ClearDataValidationCommand_Revert_RestoresPartiallyClearedRule()
    {
        var (wb, sheet) = MakeWorkbook();
        var originalRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var clearRange = MakeSingleCellRange(sheet, 2, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = originalRange,
            Type = DvType.List,
            Formula1 = "A,B",
        });

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new ClearDataValidationCommand(sheet.Id, clearRange));
        bus.Undo(wb.Id);

        sheet.DataValidations.Should().ContainSingle()
            .Which.AppliesTo.Should().Be(originalRange,
                "undo should remove split fragments and restore the original validation range");
    }
}
