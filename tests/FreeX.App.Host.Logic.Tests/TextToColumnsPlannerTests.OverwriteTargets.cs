using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class TextToColumnsPlannerTests
{
    [Fact]
    public void FindOverwriteTargets_ReportsExistingDestinationCells()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A,B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Existing"));
        var edits = TextToColumnsPlanner.BuildEdits(
            sheet,
            sourceRange,
            new CellAddress(sheet.Id, 1, 2),
            ',');

        TextToColumnsPlanner.FindOverwriteTargets(sheet, edits, sourceRange)
            .Should()
            .Equal(new CellAddress(sheet.Id, 1, 3));
    }

    [Fact]
    public void FindOverwriteTargets_DoesNotWarnForOriginalSourceCellsInPlace()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A,B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Existing"));
        var edits = TextToColumnsPlanner.BuildEdits(sheet, sourceRange, ',');

        TextToColumnsPlanner.FindOverwriteTargets(sheet, edits, sourceRange)
            .Should()
            .Equal(new CellAddress(sheet.Id, 1, 2));
    }

    [Fact]
    public void FindOverwriteTargets_IgnoresEmptyDestinationCells()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A,B"));
        var edits = TextToColumnsPlanner.BuildEdits(
            sheet,
            sourceRange,
            new CellAddress(sheet.Id, 2, 1),
            ',');

        TextToColumnsPlanner.FindOverwriteTargets(sheet, edits, sourceRange)
            .Should()
            .BeEmpty();
    }
}
