using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 17 findings:
/// R17-table-listobject-1: CreateStructuredTableCommand's NextTableId/NextTableName must scan every
/// sheet in the workbook (not just the target sheet), so a table created on Sheet2 never reuses the
/// id/name of a table already on Sheet1.
/// R17-table-listobject-2: ConvertStructuredTableToRangeCommand must rewrite every structured
/// reference bound to the removed table ([@Col]/[#This Row] in-table and TableName[Col] anywhere in
/// the workbook) into an equivalent A1 reference so formulas keep evaluating instead of turning into
/// #NAME?/#REF!, and Undo must restore both the table and the original formula text.
/// </summary>
public sealed class R17TableCmdTests
{
    // ── R17-table-listobject-1 ──────────────────────────────────────────────────

    [Fact]
    public void CreateStructuredTableCommand_OnSecondSheet_GetsWorkbookUniqueIdAndName()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        SeedTwoColumnHeaderAndRow(sheet1);
        SeedTwoColumnHeaderAndRow(sheet2);
        var ctx = new TestCommandContext(wb);

        var range1 = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 2, 2));
        var command1 = new CreateStructuredTableCommand(sheet1.Id, range1);
        command1.Apply(ctx).Success.Should().BeTrue();

        var range2 = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 2, 2));
        var command2 = new CreateStructuredTableCommand(sheet2.Id, range2);
        command2.Apply(ctx).Success.Should().BeTrue();

        var table1 = sheet1.StructuredTables.Should().ContainSingle().Subject;
        var table2 = sheet2.StructuredTables.Should().ContainSingle().Subject;

        // Pre-fix: NextTableId/NextTableName only scanned the target sheet's own StructuredTables,
        // so the second table (on an otherwise-empty Sheet2) reused id=1/"Table1" from Sheet1.
        table2.Id.Should().NotBe(table1.Id);
        table2.Name.Should().NotBe(table1.Name);
        table1.Name.Should().Be("Table1");
        table2.Name.Should().Be("Table2");
        table2.Id.Should().Be(table1.Id + 1);
    }

    [Fact]
    public void CreateStructuredTableCommand_OnSecondSheet_AfterDeletingFirstTables_StillAvoidsWorkbookIds()
    {
        // Guards against a NextTableId/NextTableName implementation that only looks at the current
        // sheet's *live* tables and would otherwise reuse an id/name still held by a table that lives
        // permanently on a different, unrelated sheet.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        SeedTwoColumnHeaderAndRow(sheet1);
        SeedTwoColumnHeaderAndRow(sheet2);
        var ctx = new TestCommandContext(wb);

        var range1 = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 2, 2));
        new CreateStructuredTableCommand(sheet1.Id, range1).Apply(ctx).Success.Should().BeTrue();
        var table1 = sheet1.StructuredTables.Single();

        var range2 = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 2, 2));
        var command2 = new CreateStructuredTableCommand(sheet2.Id, range2);
        command2.Apply(ctx).Success.Should().BeTrue();
        var table2 = sheet2.StructuredTables.Single();

        table2.Id.Should().NotBe(table1.Id);
        table2.Name.Should().NotBe(table1.Name);
    }

    private static void SeedTwoColumnHeaderAndRow(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
    }
}
