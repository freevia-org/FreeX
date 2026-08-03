using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R120: CreateStructuredTableCommand's auto-namer (NextTableName) only scanned
/// sheet.StructuredTables across the workbook for a free "TableN" slot -- it never consulted
/// workbook.NamedRanges/NamedFormulas or the sheet-scoped equivalents. Excel unifies the
/// table-name and defined-name namespaces: Name Manager's "New Name" refuses a name already used
/// by a table, and Excel's own "TableN" auto-namer likewise skips any identifier already taken by
/// a defined name. Before this fix, a workbook-global defined name literally called "Table1" (100%
/// legal today since Workbook.ValidateNamedRangeName never reserves the "TableN" pattern) meant the
/// very first Insert Table / Format as Table in that workbook silently produced a table ALSO named
/// "Table1" -- workbook.xml's &lt;definedNames&gt; and the table part's &lt;table name="Table1"/&gt;
/// would carry the identical identifier, a state real Excel treats as needing repair on open.
///
/// The fix routes NextTableName through the same choke point RenameStructuredTableCommand already
/// used (StructuredTableDesignCommandHelpers.ValidateTableName), and that helper itself is widened
/// to also check workbook.NamedFormulas and the sheet-scoped ScopedNamedRanges/ScopedNamedFormulas
/// dictionaries (it previously checked only the workbook-global NamedRanges dictionary), so the
/// rename path gets the same widened protection for free.
/// </summary>
public sealed class R120_TableAutoNameDefinedNameCollisionTests
{
    private static Workbook BuildWorkbookWithHeaderAndData(out Sheet sheet, out GridRange range)
    {
        var wb = new Workbook("test");
        sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Row1"));
        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        return wb;
    }

    [Fact]
    public void CreateStructuredTableCommand_SkipsAutoNameCollidingWithWorkbookGlobalNamedRange()
    {
        var wb = BuildWorkbookWithHeaderAndData(out var sheet, out var range);

        // A workbook-global defined name literally called "Table1", pointing at an unrelated cell.
        wb.DefineNamedRange("Table1", new GridRange(new CellAddress(sheet.Id, 10, 10), new CellAddress(sheet.Id, 10, 10)));

        var ctx = new TestCommandContext(wb);
        var command = new CreateStructuredTableCommand(sheet.Id, range);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var table = sheet.StructuredTables.Should().ContainSingle().Subject;
        table.Name.Should().NotBe("Table1", "the workbook already has a defined name called 'Table1'");
        table.DisplayName.Should().NotBe("Table1");
        // Confirms the auto-namer actually stepped past the collision to the next free slot,
        // rather than merely avoiding "Table1" by accident.
        table.Name.Should().Be("Table2");
    }

    [Fact]
    public void CreateStructuredTableCommand_SkipsAutoNameCollidingWithSheetScopedNamedFormula()
    {
        var wb = BuildWorkbookWithHeaderAndData(out var sheet, out var range);

        // A sheet-scoped named FORMULA (not a range) called "Table1" -- this is the surface the
        // original ValidateTableName also missed even before this defect's NextTableName fix.
        wb.DefineNamedFormula("Table1", "1+1", sheet.Id);

        var ctx = new TestCommandContext(wb);
        var outcome = new CreateStructuredTableCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue();
        var table = sheet.StructuredTables.Should().ContainSingle().Subject;
        table.Name.Should().NotBe("Table1");
    }

    [Fact]
    public void CreateStructuredTableCommand_NoRegression_StillAutoNamesTable1WhenNothingCollides()
    {
        // No-regression sibling: with no colliding defined name or table anywhere in the workbook,
        // the very first table must still be named "Table1" exactly as before this fix.
        var wb = BuildWorkbookWithHeaderAndData(out var sheet, out var range);

        var ctx = new TestCommandContext(wb);
        var outcome = new CreateStructuredTableCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue();
        var table = sheet.StructuredTables.Should().ContainSingle().Subject;
        table.Name.Should().Be("Table1");
        table.DisplayName.Should().Be("Table1");
    }

    [Fact]
    public void RenameStructuredTableCommand_RejectsNameCollidingWithWorkbookGlobalNamedFormula()
    {
        // Sibling coverage on the OTHER caller of the same widened ValidateTableName choke point:
        // renaming a table to a name already used by a workbook-global named FORMULA (not a range)
        // must be rejected, same as colliding with a named range already was.
        var wb = BuildWorkbookWithHeaderAndData(out var sheet, out var range);
        var createOutcome = new CreateStructuredTableCommand(sheet.Id, range).Apply(new TestCommandContext(wb));
        createOutcome.Success.Should().BeTrue();
        var tableId = sheet.StructuredTables[0].Id;

        wb.NamedFormulas["Revenue"] = "1+1";

        var ctx = new TestCommandContext(wb);
        var outcome = new RenameStructuredTableCommand(sheet.Id, tableId, "Revenue").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("already exists");
        sheet.StructuredTables[0].Name.Should().Be("Table1");
    }
}
