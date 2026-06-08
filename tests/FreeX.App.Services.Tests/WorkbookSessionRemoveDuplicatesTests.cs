using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionRemoveDuplicatesTests
{
    [Fact]
    public void ExecuteRemoveDuplicatesPlan_RemovesDuplicateRowsAndUndoRedoRestores()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedRows(sheet, "Region", "Rep", "North", "Ada", "South", "Ben", "North", "Ada");
        var session = CreateSession(workbook);
        var range = Range(sheet, 1, 1, 4, 2);
        session.SelectRange(range);
        var plan = CreateReadyPlan(sheet, range, hasHeaders: true);

        var result = session.ExecuteRemoveDuplicatesPlan(plan);

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.RemovedRowCount.Should().Be(1);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("Region"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("South"));
        sheet.GetValue(4, 1).Should().BeOfType<BlankValue>();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.CanRedo.Should().BeTrue();
        sheet.GetValue(4, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(4, 2).Should().Be(new TextValue("Ada"));

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        sheet.GetValue(3, 1).Should().Be(new TextValue("South"));
        sheet.GetValue(4, 1).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void ExecuteRemoveDuplicatesPlan_RejectsPlansWithoutSelectedColumns()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedRows(sheet, "Region", "Rep", "North", "Ada", "North", "Ada");
        var session = CreateSession(workbook);
        var range = Range(sheet, 1, 1, 3, 2);
        var plan = new RemoveDuplicatesPlan(
            range,
            RemoveDuplicatesPlanner.ExcludeHeaderRow(range, hasHeaders: true),
            HasHeaders: true,
            [
                new RemoveDuplicateColumnChoice(0, "Region", false),
                new RemoveDuplicateColumnChoice(1, "Rep", false),
            ]);

        var result = session.ExecuteRemoveDuplicatesPlan(plan);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Select at least one column.");
        result.RemovedRowCount.Should().Be(0);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        sheet.GetValue(3, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Ada"));
    }

    [Fact]
    public void ExecuteRemoveDuplicatesPlan_PropagatesAcrossGroupedVisibleSheetsOnly()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        SeedRows(summary, "Region", "Rep", "North", "Ada", "South", "Ben", "North", "Ada");
        SeedRows(details, "Region", "Rep", "East", "Cora", "West", "Drew", "East", "Cora");
        SeedRows(hidden, "Region", "Rep", "Hidden", "One", "Hidden", "One");
        var session = CreateSession(workbook);
        session.SelectAllVisibleSheets();
        var range = Range(summary, 1, 1, 4, 2);
        session.SelectRange(range);
        var plan = CreateReadyPlan(summary, range, hasHeaders: true);

        var result = session.ExecuteRemoveDuplicatesPlan(plan);

        result.Success.Should().BeTrue();
        result.RemovedRowCount.Should().Be(1);
        session.IsWorkbookGrouped.Should().BeTrue();
        summary.GetValue(3, 1).Should().Be(new TextValue("South"));
        summary.GetValue(4, 1).Should().BeOfType<BlankValue>();
        details.GetValue(3, 1).Should().Be(new TextValue("West"));
        details.GetValue(4, 1).Should().BeOfType<BlankValue>();
        hidden.GetValue(2, 1).Should().Be(new TextValue("Hidden"));
        hidden.GetValue(3, 1).Should().Be(new TextValue("Hidden"));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetValue(4, 1).Should().Be(new TextValue("North"));
        details.GetValue(4, 1).Should().Be(new TextValue("East"));
        hidden.GetValue(3, 1).Should().Be(new TextValue("Hidden"));
    }

    [Fact]
    public void ExecuteRemoveDuplicatesPlan_RejectsProtectedGroupedTargetsWithoutMutation()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        SeedRows(summary, "Region", "Rep", "North", "Ada", "North", "Ada");
        SeedRows(details, "Region", "Rep", "East", "Cora", "East", "Cora");
        details.IsProtected = true;
        var session = CreateSession(workbook);
        session.SelectAllVisibleSheets();
        var range = Range(summary, 1, 1, 3, 2);
        var plan = CreateReadyPlan(summary, range, hasHeaders: true);

        var result = session.ExecuteRemoveDuplicatesPlan(plan);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        result.RemovedRowCount.Should().Be(0);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        summary.GetValue(3, 1).Should().Be(new TextValue("North"));
        details.GetValue(3, 1).Should().Be(new TextValue("East"));
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static RemoveDuplicatesPlan CreateReadyPlan(Sheet sheet, GridRange range, bool hasHeaders) =>
        RemoveDuplicatesPlanner.CreatePlan(
                range,
                hasHeaders,
                RemoveDuplicatesPlanner.BuildColumnChoices(sheet, range, hasHeaders))
            .Plan!;

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static void SeedRows(Sheet sheet, params string[] values)
    {
        for (var index = 0; index < values.Length; index += 2)
        {
            var row = (uint)(index / 2) + 1;
            sheet.SetCell(Address(sheet, row, 1), new TextValue(values[index]));
            sheet.SetCell(Address(sheet, row, 2), new TextValue(values[index + 1]));
        }
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));
}
