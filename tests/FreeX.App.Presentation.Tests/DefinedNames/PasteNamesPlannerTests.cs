using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DefinedNames;

public sealed class PasteNamesPlannerTests
{
    private static (Workbook Workbook, Sheet Sheet) NewWorkbook()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    [Fact]
    public void BuildItems_SortsNamesCaseInsensitivelyAndFormatsRange()
    {
        var (workbook, sheet) = NewWorkbook();
        workbook.NamedRanges["Beta"] = Range(sheet, 1, 1, 1, 1);
        workbook.NamedRanges["alpha"] = Range(sheet, 2, 2, 3, 3);

        var items = PasteNamesPlanner.BuildItems(workbook, range => range.ToString());

        items.Select(i => i.Name).Should().Equal("alpha", "Beta");
        items[0].RefersTo.Should().Be("B2:C3");
    }

    [Fact]
    public void TryBuildPasteListEdits_NoNames_ReturnsNoNamesError()
    {
        var (_, sheet) = NewWorkbook();

        var ok = PasteNamesPlanner.TryBuildPasteListEdits(
            new CellAddress(sheet.Id, 1, 1), [], out var edits, out var error);

        ok.Should().BeFalse();
        error.Should().Be(PasteNamesListError.NoNames);
        edits.Should().BeEmpty();
    }

    [Fact]
    public void TryBuildPasteListEdits_BuildsTwoColumnBlockDownward()
    {
        var (_, sheet) = NewWorkbook();
        var items = new[]
        {
            new PasteNamesItem("alpha", "Sheet1!A1"),
            new PasteNamesItem("beta", "Sheet1!B2:C3"),
        };

        var ok = PasteNamesPlanner.TryBuildPasteListEdits(
            new CellAddress(sheet.Id, 5, 2), items, out var edits, out var error);

        ok.Should().BeTrue();
        error.Should().Be(PasteNamesListError.None);
        edits.Should().HaveCount(4);

        edits[0].Address.Should().Be(new CellAddress(sheet.Id, 5, 2));
        ((TextValue)edits[0].NewCell.Value).Value.Should().Be("alpha");
        edits[1].Address.Should().Be(new CellAddress(sheet.Id, 5, 3));
        ((TextValue)edits[1].NewCell.Value).Value.Should().Be("Sheet1!A1");
        edits[2].Address.Should().Be(new CellAddress(sheet.Id, 6, 2));
        ((TextValue)edits[2].NewCell.Value).Value.Should().Be("beta");
        edits[3].Address.Should().Be(new CellAddress(sheet.Id, 6, 3));
    }

    [Fact]
    public void TryBuildPasteListEdits_LastColumn_ReturnsNotEnoughColumns()
    {
        var (_, sheet) = NewWorkbook();
        var items = new[] { new PasteNamesItem("alpha", "A1") };

        var ok = PasteNamesPlanner.TryBuildPasteListEdits(
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol), items, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(PasteNamesListError.NotEnoughColumns);
    }

    [Fact]
    public void TryBuildPasteListEdits_PastLastRow_ReturnsNotEnoughRows()
    {
        var (_, sheet) = NewWorkbook();
        var items = new[]
        {
            new PasteNamesItem("a", "A1"),
            new PasteNamesItem("b", "A2"),
        };

        var ok = PasteNamesPlanner.TryBuildPasteListEdits(
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1), items, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(PasteNamesListError.NotEnoughRows);
    }
}
