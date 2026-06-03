using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class TextToColumnsPlannerTests
{
    [Fact]
    public void TextToColumnsCommandPlanner_CreatesPerSheetGroupedEditsAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var ctx = new SimpleCtx(workbook);
        var range = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("East,42"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("West,7"));

        var command = TextToColumnsCommandPlanner.CreateCommand(
            workbook,
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            range,
            TextToColumnsDialog.CreateResult(TextToColumnsDelimiterKind.Comma));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet1.GetValue(new CellAddress(sheet1.Id, 1, 1)).Should().Be(new TextValue("East"));
        sheet1.GetValue(new CellAddress(sheet1.Id, 1, 2)).Should().Be(new NumberValue(42));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(new TextValue("West"));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 2)).Should().Be(new NumberValue(7));
        outcome.AffectedCells.Should().BeEquivalentTo([
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 1, 2),
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 1, 2)
        ]);

        command.Revert(ctx);

        sheet1.GetValue(new CellAddress(sheet1.Id, 1, 1)).Should().Be(new TextValue("East,42"));
        sheet1.GetCell(new CellAddress(sheet1.Id, 1, 2)).Should().BeNull();
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(new TextValue("West,7"));
        sheet2.GetCell(new CellAddress(sheet2.Id, 1, 2)).Should().BeNull();
    }

    [Fact]
    public void TextToColumnsCommandPlanner_FindsOverwriteTargetsAcrossGroupedSheets()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var range = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("East,42"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("West,7"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), new TextValue("Existing"));

        TextToColumnsCommandPlanner.FindOverwriteTargets(
                workbook,
                [sheet1.Id, sheet2.Id],
                range,
                TextToColumnsDialog.CreateResult(TextToColumnsDelimiterKind.Comma))
            .Should()
            .Equal(new CellAddress(sheet2.Id, 1, 2));
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
