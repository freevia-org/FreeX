using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteSpecialCommandTests
{
    [Fact]
    public void PasteLinkService_CreatesFormulasReferencingSourceCells()
    {
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 2));

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination: new CellAddress(sheetId, 5, 5),
            sourceSheetName: "Sales 2026",
            transpose: false);

        linkedCells.Should().HaveCount(2);
        linkedCells[0].Address.Should().Be(new CellAddress(sheetId, 5, 5));
        linkedCells[0].Cell.FormulaText.Should().Be("'Sales 2026'!A1");
        linkedCells[1].Address.Should().Be(new CellAddress(sheetId, 5, 6));
        linkedCells[1].Cell.FormulaText.Should().Be("'Sales 2026'!B1");
    }

    [Fact]
    public void PasteLinkService_TransposesLinkedCells()
    {
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 2));

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination: new CellAddress(sheetId, 5, 5),
            sourceSheetName: "Sheet1",
            transpose: true);

        linkedCells[0].Address.Should().Be(new CellAddress(sheetId, 5, 5));
        linkedCells[1].Address.Should().Be(new CellAddress(sheetId, 6, 5));
    }

}
