using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Integration.Tests;

public class FindReplaceTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(id => new SimpleCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    [Fact]
    public void Find_MatchesByDisplayText()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello"));

        var results = FindReplaceService.Find(wb, "hello");

        results.Should().HaveCount(1);
        results[0].Address.Should().Be(a1);
        results[0].MatchedText.Should().Be("hello");
    }

    [Fact]
    public void Find_MatchCase_DoesNotMatchWrongCase()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello"));

        var results = FindReplaceService.Find(wb, "HELLO", matchCase: true);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Find_EntireCell_DoesNotMatchPartial()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("hello world"));

        var results = FindReplaceService.Find(wb, "hello", matchEntireCell: true);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Find_SearchFormulas_FindsFormulaText()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");

        var results = FindReplaceService.Find(wb, "SUM", searchFormulas: true);

        results.Should().HaveCount(1);
        results[0].Address.Should().Be(a1);
    }

    [Fact]
    public void ReplaceAll_ReplacesValueCells()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foo"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "foo", "bar");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("bar"));
    }

    [Fact]
    public void ReplaceAll_DoesNotReplaceFormulaCells()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "SUM", "MAX");

        count.Should().Be(0);
        sheet.GetCell(a1)!.FormulaText.Should().Be("SUM(B1:B5)");
    }
}

/// <summary>Minimal ICommandContext for tests.</summary>
file sealed class SimpleCommandContext : ICommandContext
{
    public Workbook Workbook { get; }

    public SimpleCommandContext(Workbook workbook) => Workbook = workbook;

    public Sheet GetSheet(SheetId sheetId) =>
        Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
}
