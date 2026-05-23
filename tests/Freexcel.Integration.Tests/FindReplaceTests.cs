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
    public void Find_OptionsLimitScopeOrderAndLookInComments()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var b1 = new CellAddress(sheet1.Id, 1, 2);
        var a2 = new CellAddress(sheet1.Id, 2, 1);
        var sheet2Cell = new CellAddress(sheet2.Id, 1, 1);
        sheet1.SetCell(b1, new TextValue("needle in B1"));
        sheet1.SetCell(a2, new TextValue("needle in A2"));
        sheet2.SetCell(sheet2Cell, new TextValue("needle elsewhere"));
        sheet1.Comments[a2] = "needle note";
        sheet1.ThreadedComments[b1] = new ThreadedComment("needle thread");

        var valueResults = FindReplaceService.Find(
            workbook,
            "needle",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id, SearchOrder: FindSearchOrder.ByColumns));

        valueResults.Select(result => result.Address).Should().Equal(a2, b1);

        var noteResults = FindReplaceService.Find(
            workbook,
            "needle note",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id, LookIn: FindLookIn.Notes));

        noteResults.Should().ContainSingle().Which.Address.Should().Be(a2);

        var commentResults = FindReplaceService.Find(
            workbook,
            "needle thread",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id, LookIn: FindLookIn.Comments));

        commentResults.Should().ContainSingle().Which.Address.Should().Be(b1);
    }

    [Fact]
    public void Find_OptionsCanRequireMatchingCellFormat()
    {
        var (wb, sheet, _) = Setup();
        var boldStyle = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 255, 0) });
        var yellowOnlyStyle = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 255, 0) });
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new TextValue("needle"));
        sheet.SetCell(a2, new TextValue("needle"));
        sheet.SetCell(a3, new TextValue("needle"));
        sheet.GetCell(a1)!.StyleId = boldStyle;
        sheet.GetCell(a2)!.StyleId = yellowOnlyStyle;

        var results = FindReplaceService.Find(
            wb,
            "needle",
            new FindOptions(RequiredFormat: new StyleDiff(Bold: true, FillColor: new CellColor(255, 255, 0))));

        results.Select(result => result.Address).Should().Equal(a1);
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

    [Fact]
    public void ReplaceAll_ReplacesSubstring_InValueCells()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foobar"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "foo", "baz");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("bazbar"));
    }

    [Fact]
    public void ReplaceAll_HonorsSheetScope()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var commandBus = new CommandBus(id => new SimpleCommandContext(workbook));
        var a1 = new CellAddress(sheet1.Id, 1, 1);
        var a2 = new CellAddress(sheet2.Id, 1, 1);
        sheet1.SetCell(a1, new TextValue("foo"));
        sheet2.SetCell(a2, new TextValue("foo"));

        var count = FindReplaceService.ReplaceAll(
            workbook,
            commandBus,
            "foo",
            "bar",
            new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: sheet1.Id));

        count.Should().Be(1);
        sheet1.GetCell(a1)!.Value.Should().Be(new TextValue("bar"));
        sheet2.GetCell(a2)!.Value.Should().Be(new TextValue("foo"));
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
