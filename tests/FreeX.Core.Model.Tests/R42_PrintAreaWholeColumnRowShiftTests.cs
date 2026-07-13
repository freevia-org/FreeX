using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

// Regression tests for R42-io-print-titles-area-structural-3-1: a whole-column (or whole-row)
// print area is modeled as a plain bounded GridRange whose End.Row (or End.Col) sits at
// CellAddress.MaxRow/MaxCol. A structural edit on the *perpendicular* axis (e.g. deleting a
// row from a whole-column print area A:C) must leave the "full" extent untouched instead of
// eroding it via the ordinary overlap-shrink math. A bounded print area must keep adjusting
// normally.
public class R42_PrintAreaWholeColumnRowShiftTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void WholeColumnPrintArea_SurvivesRowDelete_Unchanged()
    {
        var (_, sheet, ctx) = Setup();
        // A:C, whole column - spans every row on the sheet.
        var wholeColumnArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 3));
        sheet.PrintArea = wholeColumnArea;

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 2);
        cmd.Apply(ctx);

        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintAreas[0].Should().Be(wholeColumnArea,
            "a whole-column print area already spans every row and a row delete is a " +
            "perpendicular-axis edit for it, so its full extent must not shrink");
    }

    [Fact]
    public void WholeColumnPrintArea_SurvivesRowInsert_Unchanged()
    {
        var (_, sheet, ctx) = Setup();
        var wholeColumnArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 3));
        sheet.PrintArea = wholeColumnArea;

        // Insert at the very top row: previously this nudged Start.Row away from 1.
        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 3);
        cmd.Apply(ctx);

        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintAreas[0].Should().Be(wholeColumnArea,
            "a whole-column print area must remain A1:C(MaxRow) regardless of where rows are inserted");
    }

    [Fact]
    public void WholeRowPrintArea_SurvivesColumnDelete_Unchanged()
    {
        var (_, sheet, ctx) = Setup();
        // Rows 1:3, whole row - spans every column on the sheet.
        var wholeRowArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, CellAddress.MaxCol));
        sheet.PrintArea = wholeRowArea;

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 2);
        cmd.Apply(ctx);

        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintAreas[0].Should().Be(wholeRowArea,
            "a whole-row print area already spans every column and a column delete is a " +
            "perpendicular-axis edit for it, so its full extent must not shrink");
    }

    [Fact]
    public void BoundedPrintArea_StillShrinksOnRowDelete()
    {
        // Sibling no-regression case: an ordinary bounded print area must keep adjusting exactly
        // as before when rows overlapping it are deleted.
        var (_, sheet, ctx) = Setup();
        var boundedArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 3));
        sheet.PrintArea = boundedArea;

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        var expected = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 8, 3));
        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintAreas[0].Should().Be(expected);
    }

    [Fact]
    public void BoundedPrintArea_StillShrinksOnColumnDelete()
    {
        // Sibling no-regression case for the column axis.
        var (_, sheet, ctx) = Setup();
        var boundedArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 6));
        sheet.PrintArea = boundedArea;

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        var expected = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 4));
        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintAreas[0].Should().Be(expected);
    }
}
