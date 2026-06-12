using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PasteNamesPlannerTests
{
    [Fact]
    public void BuildItems_SortsNamesAndFormatsReferences()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange(
            "Total",
            new GridRange(new CellAddress(sheet.Id, 4, 2), new CellAddress(sheet.Id, 4, 2)));
        workbook.DefineNamedRange(
            "Sales",
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)));

        var items = PasteNamesPlanner.BuildItems(
            workbook,
            range => $"{range.Start.ToA1()}:{range.End.ToA1()}");

        items.Should().Equal(
            new PasteNamesDialogItem("Sales", "A1:A3"),
            new PasteNamesDialogItem("Total", "B4:B4"));
    }

    [Fact]
    public void TryBuildPasteListEdits_WritesNamesAndReferencesInTwoColumns()
    {
        var sheetId = SheetId.New();
        var items = new[]
        {
            new PasteNamesDialogItem("Sales", "Sheet1!A1:A3"),
            new PasteNamesDialogItem("Total", "Sheet1!B4")
        };

        PasteNamesPlanner.TryBuildPasteListEdits(
            new CellAddress(sheetId, 5, 3),
            items,
            out var edits,
            out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheetId, 5, 3),
            new CellAddress(sheetId, 5, 4),
            new CellAddress(sheetId, 6, 3),
            new CellAddress(sheetId, 6, 4));
        edits.Select(edit => ((TextValue)edit.NewCell.Value!).Value)
            .Should().Equal("Sales", "Sheet1!A1:A3", "Total", "Sheet1!B4");
    }

    [Fact]
    public void TryBuildPasteListEdits_RejectsColumnOverflow()
    {
        PasteNamesPlanner.TryBuildPasteListEdits(
            new CellAddress(SheetId.New(), 1, CellAddress.MaxCol),
            [new PasteNamesDialogItem("Name", "A1")],
            out var edits,
            out var error)
            .Should().BeFalse();

        edits.Should().BeEmpty();
        error.Should().Be(UiText.Get("PasteNames_NotEnoughColumnsMessage"));
    }

    [Fact]
    public void TryBuildPasteListEdits_RejectsRowOverflow()
    {
        PasteNamesPlanner.TryBuildPasteListEdits(
            new CellAddress(SheetId.New(), CellAddress.MaxRow, 1),
            [
                new PasteNamesDialogItem("First", "A1"),
                new PasteNamesDialogItem("Second", "A2")
            ],
            out var edits,
            out var error)
            .Should().BeFalse();

        edits.Should().BeEmpty();
        error.Should().Be(UiText.Get("PasteNames_NotEnoughRowsMessage"));
    }
}
