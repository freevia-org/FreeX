using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 17 fixes:
/// - R17-meta-1: Redo re-mints a fresh SheetId in the sheet-creating commands round-16 did not
///   fix (DuplicateSheetCommand, AddChartSheetCommand, MoveChartToNewSheetCommand,
///   ForecastSheetCommand). A later redo-stack command that captured the original id must not
///   throw "Sheet {id} not found" after an undo/redo cycle.
/// - R17-table-listobject-3: Duplicating a sheet must give each cloned structured table a
///   workbook-unique Id and Name/DisplayName instead of copying the source table's identity
///   verbatim (which corrupts the saved XLSX and makes Table1[...] references ambiguous).
/// </summary>
public sealed class R17_sheet_create_Tests
{
    [Fact]
    public void DuplicateSheetCommand_ClonedTableGetsWorkbookUniqueIdAndName()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = range,
            HasAutoFilter = true
        });

        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        // Source sheet's table identity must be untouched.
        sheet.StructuredTables.Should().ContainSingle();
        sheet.StructuredTables[0].Id.Should().Be(1);
        sheet.StructuredTables[0].Name.Should().Be("Table1");

        var copy = wb.Sheets[1];
        copy.StructuredTables.Should().ContainSingle();
        var copiedTable = copy.StructuredTables[0];

        // R17-table-listobject-3: the copy's table must NOT share the source's Id or Name --
        // two tables with the same Id/Name in one workbook corrupt the saved XLSX (Excel repairs
        // by dropping a table) and make Table1[...] formula references ambiguous.
        copiedTable.Id.Should().NotBe(1);
        copiedTable.Name.Should().NotBe("Table1");
        copiedTable.DisplayName.Should().Be(copiedTable.Name);

        // Undo must restore the source's table identity untouched (already true, but pin it).
        command.Revert(ctx);
        sheet.StructuredTables.Should().ContainSingle();
        sheet.StructuredTables[0].Id.Should().Be(1);
        sheet.StructuredTables[0].Name.Should().Be("Table1");
    }

    [Fact]
    public void DuplicateSheetCommand_RedoRecreatesCopyWithSameSheetIdSoLaterEditStillTargetsIt()
    {
        var wb = new Workbook("test");
        wb.AddSheet("Sheet1");
        var bus = new CommandBus(_ => new TestCommandContext(wb));

        var duplicate = new DuplicateSheetCommand(wb.Sheets[0].Id);
        bus.Execute(wb.Id, duplicate).Success.Should().BeTrue();

        var copySheetId = wb.Sheets[1].Id;
        var edit = EditCellsCommand.ForValue(copySheetId, new CellAddress(copySheetId, 1, 1), new NumberValue(42));
        bus.Execute(wb.Id, edit).Success.Should().BeTrue();

        bus.Undo(wb.Id).Success.Should().BeTrue(); // undo edit
        bus.Undo(wb.Id).Success.Should().BeTrue(); // undo duplicate
        wb.Sheets.Should().HaveCount(1);

        bus.Redo(wb.Id).Success.Should().BeTrue(); // redo duplicate
        wb.Sheets.Should().HaveCount(2);
        wb.Sheets[1].Id.Should().Be(copySheetId);

        // R17-meta-1: the edit command captured `copySheetId` from the FIRST duplicate; if redo
        // had re-minted a fresh SheetId, this would fail with "Sheet {id} not found".
        var redoEditOutcome = bus.Redo(wb.Id);
        redoEditOutcome.Success.Should().BeTrue();
        wb.Sheets[1].GetValue(1, 1).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void AddChartSheetCommand_RedoRecreatesChartSheetWithSameSheetId()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 4, 3));
        var bus = new CommandBus(_ => new TestCommandContext(wb));

        var command = new AddChartSheetCommand(source.Id, range, ChartType.Column, "Chart");
        bus.Execute(wb.Id, command).Success.Should().BeTrue();

        var createdSheetId = command.CreatedSheetId!.Value;

        bus.Undo(wb.Id).Success.Should().BeTrue();
        wb.Sheets.Should().ContainSingle();

        // R17-meta-1: Workbook.AddSheet always mints a brand-new SheetId; without the fix, redo
        // would re-create the chart sheet under a DIFFERENT id than captured on the first Apply.
        bus.Redo(wb.Id).Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(2);
        wb.Sheets[1].Id.Should().Be(createdSheetId);
        wb.Sheets[1].Charts.Should().ContainSingle();
    }

    [Fact]
    public void MoveChartToNewSheetCommand_RedoRecreatesTargetSheetWithSameSheetId()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 4, 3));
        new AddChartCommand(source.Id, range, ChartType.Line, "Sales").Apply(ctx);
        var chart = source.Charts[0];

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        var command = new MoveChartToNewSheetCommand(source.Id, chart.Id, "Sales Chart");
        bus.Execute(wb.Id, command).Success.Should().BeTrue();

        var chartSheet = wb.Sheets.Single(sheet => sheet.Name == "Sales Chart");
        var createdSheetId = chartSheet.Id;

        bus.Undo(wb.Id).Success.Should().BeTrue();
        wb.Sheets.Should().NotContain(sheet => sheet.Name == "Sales Chart");

        // R17-meta-1: without the fix, redo re-creates "Sales Chart" under a fresh SheetId
        // instead of the one captured on the first Apply.
        bus.Redo(wb.Id).Success.Should().BeTrue();
        var redoneSheet = wb.Sheets.Single(sheet => sheet.Name == "Sales Chart");
        redoneSheet.Id.Should().Be(createdSheetId);
        redoneSheet.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);
    }

    [Fact]
    public void ForecastSheetCommand_RedoRecreatesForecastSheetWithSameSheetId()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var bus = new CommandBus(_ => new TestCommandContext(wb));
        var command = new ForecastSheetCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            forecastPeriods: 2);
        bus.Execute(wb.Id, command).Success.Should().BeTrue();

        var forecastSheetId = wb.Sheets[1].Id;

        bus.Undo(wb.Id).Success.Should().BeTrue();
        wb.Sheets.Should().ContainSingle();

        // R17-meta-1: without the fix, redo re-creates the forecast sheet under a fresh
        // SheetId instead of the one captured on the first Apply.
        bus.Redo(wb.Id).Success.Should().BeTrue();
        wb.Sheets.Should().HaveCount(2);
        wb.Sheets[1].Id.Should().Be(forecastSheetId);
        wb.Sheets[1].GetCell(5, 3)!.FormulaText.Should().Be("FORECAST.LINEAR(A5,B2:B4,A2:A4)");
    }
}
