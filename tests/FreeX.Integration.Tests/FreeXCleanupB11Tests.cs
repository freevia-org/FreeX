using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for cleanup-high batch group 11, finding P61: Formulas-mode Replace must not
/// silently skip constant (non-formula) cells that Find reports as matches. Excel's "Look in:
/// Formulas" is the only replace mode it offers and it replaces constants too, so Find and
/// Replace must agree on what counts as a match.
/// </summary>
public class FreeXCleanupB11Tests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(id => new TestCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    [Fact]
    public void ReplaceAll_FormulasLookIn_ReplacesConstantCell_ThatFindAlreadyMatched()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foo"));

        var found = FindReplaceService.Find(wb, "foo", new FindOptions(LookIn: FindLookIn.Formulas));
        found.Should().ContainSingle(result => result.Address == a1);

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "foo",
            "bar",
            new FindOptions(LookIn: FindLookIn.Formulas));

        count.Should().Be(1, "Excel's Look in: Formulas replaces constant cells too, not just formula cells");
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("bar"));
        sheet.GetCell(a1)!.HasFormula.Should().BeFalse();
    }

    [Fact]
    public void ReplaceAll_FormulasLookIn_ReplacesBothFormulaAndConstantMatches_InSameSheet()
    {
        var (wb, sheet, commandBus) = Setup();
        var formulaAddress = new CellAddress(sheet.Id, 1, 1);
        var constantAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetFormula(formulaAddress, "SUM(B1:B5)");
        sheet.SetCell(constantAddress, new TextValue("SUM literal"));

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "SUM",
            "MAX",
            new FindOptions(LookIn: FindLookIn.Formulas));

        count.Should().Be(2);
        sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("MAX(B1:B5)");
        sheet.GetCell(constantAddress)!.Value.Should().Be(new TextValue("MAX literal"));
        sheet.GetCell(constantAddress)!.HasFormula.Should().BeFalse();
    }

    [Fact]
    public void ReplaceAll_FormulasLookIn_ConstantNumericCell_ReparsesAsNumber()
    {
        // Mirrors how Values-mode replacement re-parses replacement text the same way Excel
        // re-parses manually typed cell text, so a numeric match round-trips to a NumberValue.
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(123));

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "123",
            "456",
            new FindOptions(LookIn: FindLookIn.Formulas));

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(456));
    }
}
