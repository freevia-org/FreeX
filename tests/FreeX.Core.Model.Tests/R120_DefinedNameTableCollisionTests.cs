using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-120 finding: table names and defined names (named ranges/formulas, workbook-global or
/// sheet-scoped) share ONE unified namespace in Excel -- Name Manager's "New Name"/"Edit Name"
/// refuses any text already used by a table on ANY sheet, and a table's own Table Name box refuses
/// any text already used by a defined name of any kind/scope. A sibling fix
/// (StructuredTableDesignCommandHelpers.ValidateTableName, see R120_TableAutoNameDefinedNameCollisionTests)
/// already covers the table-rename/auto-name direction. This test file covers the OTHER, previously
/// entirely unguarded direction: Workbook.ValidateNamedRangeName -- the sole validation performed by
/// DefineNamedRangeCommand/DefineNamedFormulaCommand, the real entry points behind both the WPF
/// NamedRangeDialog and the Avalonia Define-Name dialog -- never inspected any sheet's
/// StructuredTables at all. Before this fix, a brand-new defined name (range or formula, global or
/// sheet-scoped) could be created with text identical to an existing table's Name/DisplayName,
/// leaving workbook.xml's &lt;definedNames&gt; and a table part's &lt;table name="..."/&gt; carrying
/// the same identifier -- a state real Excel treats as needing repair on open.
/// </summary>
public sealed class R120_DefinedNameTableCollisionTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) CreateWorkbookWithTable()
    {
        var wb = new Workbook("defined-name-table-collision-test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Row1"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        var ctx = new TestCommandContext(wb);
        var createOutcome = new CreateStructuredTableCommand(sheet.Id, range).Apply(ctx);
        createOutcome.Success.Should().BeTrue();
        sheet.StructuredTables.Should().ContainSingle(t => t.Name == "Table1");

        return (wb, sheet, ctx);
    }

    // ── Core fix: Workbook.ValidateNamedRangeName rejects a candidate colliding with a table ──

    [Fact]
    public void ValidateNamedRangeName_CollidingWithExistingTableName_IsRejected()
    {
        var (wb, _, _) = CreateWorkbookWithTable();

        wb.ValidateNamedRangeName("Table1").Should().NotBeNull();
    }

    [Fact]
    public void ValidateNamedRangeName_CaseInsensitiveCollisionWithTableName_IsRejected()
    {
        var (wb, _, _) = CreateWorkbookWithTable();

        wb.ValidateNamedRangeName("table1").Should().NotBeNull();
    }

    // ── Real entry points: DefineNamedRangeCommand / DefineNamedFormulaCommand must refuse it
    //    too, not just the raw validator, since those commands are what the Name Manager /
    //    Define Name dialogs (both shells) actually invoke. ──────────────────────────────────

    [Fact]
    public void DefineNamedRangeCommand_WorkbookGlobal_CollidingWithTableName_Fails_AndNameIsNotCreated()
    {
        var (wb, sheet, ctx) = CreateWorkbookWithTable();
        var unrelated = new GridRange(new CellAddress(sheet.Id, 10, 10), new CellAddress(sheet.Id, 10, 10));

        var outcome = new DefineNamedRangeCommand("Table1", unrelated).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
        wb.NamedRanges.Should().NotContainKey("Table1");
    }

    [Fact]
    public void DefineNamedFormulaCommand_WorkbookGlobal_CollidingWithTableName_Fails_AndNameIsNotCreated()
    {
        var (wb, _, ctx) = CreateWorkbookWithTable();

        var outcome = new DefineNamedFormulaCommand("Table1", "1+1").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
        wb.NamedFormulas.Should().NotContainKey("Table1");
    }

    [Fact]
    public void DefineNamedRangeCommand_SheetScopedOnSameSheet_CollidingWithTableName_Fails()
    {
        var (wb, sheet, ctx) = CreateWorkbookWithTable();
        var unrelated = new GridRange(new CellAddress(sheet.Id, 10, 10), new CellAddress(sheet.Id, 10, 10));

        var outcome = new DefineNamedRangeCommand("Table1", unrelated, scopeSheetId: sheet.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
        wb.ScopedNamedRanges.Should().NotContainKey(("Table1", sheet.Id));
    }

    /// <summary>
    /// Cross-sheet case: Excel's table namespace is workbook-wide, not per-sheet, so a
    /// sheet-scoped defined name on a DIFFERENT sheet than the table still collides -- exactly
    /// mirroring ValidateTableName's own cross-sheet scan (<c>foreach (var sheet in workbook.Sheets)</c>).
    /// </summary>
    [Fact]
    public void DefineNamedRangeCommand_SheetScopedOnDifferentSheet_CollidingWithTableName_Fails()
    {
        var (wb, _, ctx) = CreateWorkbookWithTable();
        var sheet2 = wb.AddSheet("Sheet2");
        var unrelated = new GridRange(new CellAddress(sheet2.Id, 0, 0), new CellAddress(sheet2.Id, 0, 0));

        var outcome = new DefineNamedRangeCommand("Table1", unrelated, scopeSheetId: sheet2.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
        wb.ScopedNamedRanges.Should().NotContainKey(("Table1", sheet2.Id));
    }

    // ── No-regression sibling coverage: ordinary defined-name creation/redefinition that does
    //    NOT collide with any table must keep working exactly as before this fix. ─────────────

    [Fact]
    public void DefineNamedRangeCommand_NoCollision_StillSucceeds()
    {
        var (wb, sheet, ctx) = CreateWorkbookWithTable();
        var range = new GridRange(new CellAddress(sheet.Id, 20, 20), new CellAddress(sheet.Id, 20, 20));

        var outcome = new DefineNamedRangeCommand("Revenue", range).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        wb.NamedRanges.Should().ContainKey("Revenue");
    }

    [Fact]
    public void DefineNamedFormulaCommand_NoCollision_StillSucceeds()
    {
        var (wb, _, ctx) = CreateWorkbookWithTable();

        var outcome = new DefineNamedFormulaCommand("TaxRate", "0.08").Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        wb.NamedFormulas.Should().ContainKey("TaxRate");
    }

    [Fact]
    public void ValidateNamedRangeName_NoWorkbookTables_StillAcceptsOrdinaryNames()
    {
        var wb = new Workbook("no-tables");
        wb.AddSheet("Sheet1");

        wb.ValidateNamedRangeName("Revenue").Should().BeNull();
    }

    /// <summary>
    /// Redefining an EXISTING named range (allowRedefine: true, the dialogs' edit-in-place path)
    /// must still work when the name itself does not collide with any table -- this exercises the
    /// same ValidateNamedRangeName choke point a second time in the same Apply call sequence and
    /// confirms the new table check does not somehow trip on a name it already validated once.
    /// </summary>
    [Fact]
    public void DefineNamedRangeCommand_RedefineExistingNonCollidingName_StillSucceeds()
    {
        var (wb, sheet, ctx) = CreateWorkbookWithTable();
        var firstRange = new GridRange(new CellAddress(sheet.Id, 20, 20), new CellAddress(sheet.Id, 20, 20));
        new DefineNamedRangeCommand("Revenue", firstRange).Apply(ctx).Success.Should().BeTrue();

        var secondRange = new GridRange(new CellAddress(sheet.Id, 21, 21), new CellAddress(sheet.Id, 21, 21));
        var outcome = new DefineNamedRangeCommand("Revenue", secondRange, allowRedefine: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        wb.NamedRanges["Revenue"].Should().Be(secondRange);
    }
}
