using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class TextToColumnsPlannerTests
{
    [Fact]
    public void BuildEdits_SplitsTextFromFirstColumnAcrossColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("East, 42, Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("West, 7, Closed"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ',');

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 4),
            new CellAddress(sheet.Id, 2, 5),
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 3, 4),
            new CellAddress(sheet.Id, 3, 5));
        edits.Select(edit => edit.NewCell.Value).Should().Equal(
            new TextValue("East"),
            new NumberValue(42),
            new TextValue("Open"),
            new TextValue("West"),
            new NumberValue(7),
            new TextValue("Closed"));
    }

    [Fact]
    public void BuildEdits_IgnoresNonTextCellsInSourceColumn()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A;B"));

        var edits = TextToColumnsPlanner.BuildEdits(sheet, range, ';');

        edits.Should().HaveCount(2);
        edits[0].Address.Should().Be(new CellAddress(sheet.Id, 2, 1));
        edits[0].NewCell.Value.Should().Be(new TextValue("A"));
        edits[1].Address.Should().Be(new CellAddress(sheet.Id, 2, 2));
        edits[1].NewCell.Value.Should().Be(new TextValue("B"));
    }
}
