using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R99: DuplicateSheetCommand renames a cloned structured table to a workbook-unique name
/// (UniquifyClonedTables) but Sheet.Clone copies every cell formula -- and every table's own
/// CalculatedColumnFormula/TotalsRowFormula metadata -- VERBATIM, including any Table[...]
/// structured reference naming the OLD table name. Table-name resolution
/// (StructuredReferenceResolver) is workbook-global by name, not scoped to "whichever table lives
/// on this sheet", so without a matching formula rewrite (mirroring
/// RenameStructuredTableCommand's manual-rename path) the copy's own summary formula silently
/// keeps resolving to the SOURCE sheet's still-named table instead of the copy's own renamed one.
/// </summary>
public sealed class R99_DuplicateSheetTableFormulaRewriteTests
{
    private static (Workbook wb, Sheet sheet, StructuredTableModel table) CreateSheetWithTable(string tableName = "Table1")
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = tableName,
            DisplayName = tableName,
            Range = range,
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Item"),
                new StructuredTableColumnModel(2, "Price")
            }
        };
        sheet.StructuredTables.Add(table);
        return (wb, sheet, table);
    }

    [Fact]
    public void DuplicateSheetCommand_RewritesCopySheetFormulaToCopysOwnRenamedTable()
    {
        var (wb, sheet, _) = CreateSheetWithTable();

        // A summary cell OUTSIDE the table body referencing it by its (about-to-be-renamed) name.
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), Cell.FromFormula("SUM(Table1[Price])"));

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;
        copiedTable.Name.Should().NotBe("Table1"); // sanity: the uniquify rename actually happened

        // The copy's own summary formula must now reference the COPY's own renamed table, not the
        // source's still-named "Table1".
        var copiedFormulaCell = copy.GetCell(10, 1);
        copiedFormulaCell.Should().NotBeNull();
        copiedFormulaCell!.FormulaText.Should().Be($"SUM({copiedTable.Name}[Price])");

        // The source sheet's own formula must be completely untouched.
        sheet.GetCell(10, 1)!.FormulaText.Should().Be("SUM(Table1[Price])");

        // Undo must remove the whole duplicated sheet (existing behavior, pinned here too).
        command.Revert(ctx);
        wb.Sheets.Should().ContainSingle();
        sheet.GetCell(10, 1)!.FormulaText.Should().Be("SUM(Table1[Price])");
    }

    [Fact]
    public void DuplicateSheetCommand_RewritesClonedTablesOwnTotalsRowFormulaSelfReference()
    {
        var (wb, sheet, table) = CreateSheetWithTable();

        // A totals-row custom aggregate on the table's own column metadata that self-references
        // the table by name (the only way to write a cross-column custom total) -- mirrors the
        // scenario RenameStructuredTableCommand's RewriteTableSelfReferenceFormulas covers for the
        // manual-rename path.
        table.Columns[1] = table.Columns[1] with
        {
            TotalsRowFormula = "Table1[[#Totals],[Price]]/2",
            CalculatedColumnFormula = "Table1[[#This Row],[Price]]*1.1"
        };

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;
        var copiedColumn = copiedTable.Columns[1];

        copiedColumn.TotalsRowFormula.Should().Be($"{copiedTable.Name}[[#Totals],[Price]]/2");
        copiedColumn.CalculatedColumnFormula.Should().Be($"{copiedTable.Name}[[#This Row],[Price]]*1.1");

        // Source table's own metadata must be untouched.
        var sourceColumn = sheet.StructuredTables[0].Columns[1];
        sourceColumn.TotalsRowFormula.Should().Be("Table1[[#Totals],[Price]]/2");
        sourceColumn.CalculatedColumnFormula.Should().Be("Table1[[#This Row],[Price]]*1.1");
    }

    [Fact]
    public void DuplicateSheetCommand_RewritesCopiedScopedNamedFormulaReferencingRenamedTable()
    {
        var (wb, sheet, _) = CreateSheetWithTable();
        wb.DefineNamedFormula("TablePriceTotal", "SUM(Table1[Price])", sheet.Id);

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        wb.ScopedNamedFormulas.TryGetValue(("TablePriceTotal", copy.Id), out var copiedFormula).Should().BeTrue();
        var copiedTableName = copy.StructuredTables[0].Name;
        copiedFormula.Should().Be($"SUM({copiedTableName}[Price])");

        // Source sheet's own scoped named formula must be untouched.
        wb.ScopedNamedFormulas.TryGetValue(("TablePriceTotal", sheet.Id), out var sourceFormula).Should().BeTrue();
        sourceFormula.Should().Be("SUM(Table1[Price])");
    }

    /// <summary>
    /// No-regression sibling: duplicating a sheet with NO structured table at all must not throw
    /// or otherwise misbehave once the rewrite pass was added (renames list is empty, so the
    /// rewrite is a guaranteed no-op) -- and an ordinary formula that has nothing to do with any
    /// table must be copied over completely unchanged.
    /// </summary>
    [Fact]
    public void DuplicateSheetCommand_NoTables_LeavesOrdinaryFormulasUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromFormula("A1*2"));

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.StructuredTables.Should().BeEmpty();
        copy.GetCell(2, 1)!.FormulaText.Should().Be("A1*2");
    }

    /// <summary>
    /// No-regression sibling: a formula on the duplicated sheet that references a DIFFERENT
    /// table -- one that lives on another sheet entirely and was never cloned/renamed -- must be
    /// left completely untouched by the rewrite pass (it never appears in the renames list).
    /// </summary>
    [Fact]
    public void DuplicateSheetCommand_FormulaReferencingUnrelatedTableOnAnotherSheetIsUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        other.StructuredTables.Add(new StructuredTableModel
        {
            Id = 5,
            Name = "Inventory",
            DisplayName = "Inventory",
            Range = new GridRange(new CellAddress(other.Id, 1, 1), new CellAddress(other.Id, 2, 2))
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("SUM(Inventory[Column1])"));

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.GetCell(1, 1)!.FormulaText.Should().Be("SUM(Inventory[Column1])");
    }
}
