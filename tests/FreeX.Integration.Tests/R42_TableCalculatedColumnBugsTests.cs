using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Round-42 structured-table calculated-column/totals-formula fixes:
/// <list type="bullet">
/// <item>R42-io-table-calculated-column-3-1: renaming a table must rewrite its own columns'
/// self-referencing CalculatedColumnFormula/TotalsRowFormula (Table[Col] -&gt; new name), or that
/// metadata goes stale and re-corrupts the totals cell on the next totals refresh / re-save.</item>
/// <item>R42-io-table-calculated-column-3-2: the "Inconsistent Calculated Column Formula" audit
/// must row-shift the anchor-row CalculatedColumnFormula to each row before comparing, or every
/// ordinary-relative-reference calculated column is falsely flagged on every row but the anchor.</item>
/// <item>R42-io-table-calculated-column-3-3: a formula typed into a table's ONLY data row must
/// still be recorded as CalculatedColumnFormula, so a later grow auto-fills the new row.</item>
/// </list>
/// </summary>
public sealed class R42_TableCalculatedColumnBugsTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static StructuredTableModel LiveTable(Sheet sheet, int tableId) =>
        sheet.StructuredTables.Single(t => t.Id == tableId);

    // ── R42-io-table-calculated-column-3-1 ──────────────────────────────────────────────────

    // Table1 A1:C3 (header row 1; data rows 2-3): column C ("Profit") has a self-referencing
    // TotalsRowFormula written the only way a cross-column custom total can be ("Table1[[#Totals],
    // [Revenue]]-Table1[[#Totals],[Cost]]"), and column B ("Revenue") has a self-referencing
    // CalculatedColumnFormula ("Table1[Cost]*2" -- an unusual but legal fully-qualified self-ref).
    // Renaming Table1 -> SalesData must rewrite both, not just the sheet-cell formulas.
    [Fact]
    public void RenameStructuredTable_RewritesSelfReferencingCalculatedColumnAndTotalsRowFormulas()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cost"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Profit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "Table1[Cost]*2");
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 2), "Table1[Cost]*2");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Cost"),
                new StructuredTableColumnModel(2, "Revenue", CalculatedColumnFormula: "Table1[Cost]*2"),
                new StructuredTableColumnModel(
                    3, "Profit",
                    TotalsRowFunction: "custom",
                    TotalsRowFormula: "Table1[[#Totals],[Revenue]]-Table1[[#Totals],[Cost]]")
            }
        };
        sheet.StructuredTables.Add(table);

        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "SalesData");
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var renamed = LiveTable(sheet, 1);
        renamed.Name.Should().Be("SalesData");

        // The fix under test: both formulas must now reference the NEW table name, not the dead
        // old one.
        renamed.Columns[1].CalculatedColumnFormula.Should().Be("SalesData[Cost]*2",
            "a calculated-column formula that self-references its own table must follow a rename");
        renamed.Columns[2].TotalsRowFormula.Should().Be(
            "SalesData[[#Totals],[Revenue]]-SalesData[[#Totals],[Cost]]",
            "a totals-row formula that self-references its own table must follow a rename");

        // A subsequent totals refresh (e.g. from growing the table) must write the totals cell
        // using the RENAMED self-reference, not the stale dead table name -- this is the concrete
        // corruption the bug caused (RefreshStructuredTableTotalsCommand.ResolveTotalsCell reads
        // TotalsRowFormula verbatim).
        var totalsRefresh = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id);
        totalsRefresh.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(new CellAddress(sheet.Id, 3, 3))!.FormulaText.Should().Be(
            "SalesData[[#Totals],[Revenue]]-SalesData[[#Totals],[Cost]]");

        // Sibling case (regression guard): the sheet-cell formulas that already worked before this
        // fix must still be correctly renamed too.
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Be("SalesData[Cost]*2");

        // Undo must restore the original (pre-rename) table wholesale, including the old formulas.
        command.Revert(ctx);
        var reverted = LiveTable(sheet, 1);
        reverted.Name.Should().Be("Table1");
        reverted.Columns[1].CalculatedColumnFormula.Should().Be("Table1[Cost]*2");
        reverted.Columns[2].TotalsRowFormula.Should().Be(
            "Table1[[#Totals],[Revenue]]-Table1[[#Totals],[Cost]]");
    }

    // Sibling case: a column with no self-referencing formula metadata at all (the common case)
    // must be entirely unaffected by the rewrite pass.
    [Fact]
    public void RenameStructuredTable_LeavesColumnsWithoutSelfReferencesUnchanged()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);

        var command = new RenameStructuredTableCommand(sheet.Id, table.Id, "SalesData");
        command.Apply(ctx).Success.Should().BeTrue();

        var renamed = LiveTable(sheet, 1);
        renamed.Columns[0].CalculatedColumnFormula.Should().BeNull();
        renamed.Columns[1].TotalsRowFormula.Should().BeNull();
        renamed.Columns[1].TotalsRowFunction.Should().Be("sum");
    }

    // ── R42-io-table-calculated-column-3-2 ──────────────────────────────────────────────────

    // Table1 A1:B4 (header row 1; data rows 2-4). Column B's CalculatedColumnFormula is stored
    // anchored to the first data row ("A2*2"), and every cell is correctly row-shifted from that
    // anchor (B2="A2*2", B3="A3*2", B4="A4*2") -- a fully consistent calculated column using
    // ordinary relative references. Real Excel shows zero warnings for this.
    [Fact]
    public void FindFormulaErrorIssues_DoesNotFlagConsistentCalculatedColumnUsingOrdinaryRelativeRefs()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Double"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "A2*2");
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 2), "A3*2");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(3));
        sheet.SetFormula(new CellAddress(sheet.Id, 4, 2), "A4*2");

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            HeaderRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Value"),
                new StructuredTableColumnModel(2, "Double", CalculatedColumnFormula: "A2*2")
            }
        });

        var issues = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id);

        issues.Should().NotContain(
            i => i.ErrorCode == FormulaAuditingService.InconsistentCalculatedColumnFormulaErrorCode,
            "every row is correctly row-shifted from the anchor, so real Excel would show no warning");
    }

    // Sibling/regression case: the original structured-self-reference scenario (the anchor formula
    // has no row literal at all, e.g. "[@Sales]*2") must still correctly flag a genuinely
    // inconsistent row.
    [Fact]
    public void FindFormulaErrorIssues_StillFlagsGenuinelyInconsistentStructuredSelfReferenceRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sales");
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.InconsistentFormulaErrorCode);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            HeaderRowCount = 1
        });
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(1, "Region"));
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        sheet.StructuredTables[0].Columns.Add(new StructuredTableColumnModel(3, "Double", CalculatedColumnFormula: "[@Sales]*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromFormula("[@Sales]*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromFormula("[@Sales]*3"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), Cell.FromFormula("[@Sales]*2"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.InconsistentCalculatedColumnFormulaErrorCode)
            .Subject;

        issue.Cell.Should().Be("C3");
    }

    // ── R42-io-table-calculated-column-3-3 ──────────────────────────────────────────────────

    // Table1 A1:B2 (header row 1; the minimum valid table size -- exactly ONE data row, row 2).
    // Typing a formula into that lone data row's calculated column must still record
    // CalculatedColumnFormula, so a later grow (auto-expand into row 3) fills it in.
    [Fact]
    public void EditingLoneDataRowFormula_RecordsCalculatedColumnFormulaSoItSurvivesLaterGrowth()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Double"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Value"),
                new StructuredTableColumnModel(2, "Double")
            }
        };
        sheet.StructuredTables.Add(table);

        // Type "=A2*2" into the lone data row's column B -- the edit path EditCellsCommand drives
        // (StructuredTableEditEffects.Apply -> TryCreateCalculatedColumnPropagation).
        var edit = new EditCellsCommand(sheet.Id, [(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"))]);
        edit.Apply(ctx).Success.Should().BeTrue();

        // The fix under test: even though there was no sibling row to propagate into or verify
        // consistency against, the column must now be recorded as a calculated column.
        LiveTable(sheet, 1).Columns[1].CalculatedColumnFormula.Should().Be("A2*2",
            "Excel recognizes a calculated column the instant a formula is typed into a table's only data row");

        // Now grow the table by typing a value into A3 (the auto-expand gesture) -- the previously
        // recorded formula must auto-fill into the newly-grown row 3.
        var growEdit = new EditCellsCommand(sheet.Id, [(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(2)))]);
        var growOutcome = growEdit.Apply(ctx);
        growOutcome.Success.Should().BeTrue();

        LiveTable(sheet, 1).Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.FormulaText.Should().Be("A3*2",
            "the calculated-column formula recorded from the lone-row edit must auto-fill the newly grown row");

        // Undo the grow, then undo the original edit: both must cleanly restore prior state.
        growEdit.Revert(ctx);
        LiveTable(sheet, 1).Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2)).Should().BeNull();

        edit.Revert(ctx);
        LiveTable(sheet, 1).Columns[1].CalculatedColumnFormula.Should().BeNull();
    }

    // Sibling/regression case: editing a formula into a table with MULTIPLE existing data rows
    // must be unaffected by this fix -- the ordinary otherDataRows-based consistency check still
    // runs exactly as before.
    [Fact]
    public void EditingFormulaWithExistingSiblingRows_StillRequiresConsistentShapeBeforeRecording()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Double"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        // Row 3's column B holds an unrelated, independent value -- not blank and not a matching
        // row-shifted formula -- so typing a formula into row 2 must NOT be treated as a calculated
        // column (Excel would not silently overwrite row 3's independent value).
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(99));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Value"),
                new StructuredTableColumnModel(2, "Double")
            }
        };
        sheet.StructuredTables.Add(table);

        var edit = new EditCellsCommand(sheet.Id, [(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"))]);
        edit.Apply(ctx).Success.Should().BeTrue();

        LiveTable(sheet, 1).Columns[1].CalculatedColumnFormula.Should().BeNull(
            "row 3's independent value means this is not a consistent calculated column, so nothing should be recorded");
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.Value.Should().Be(new NumberValue(99),
            "the independent sibling value must be left untouched");
    }
}
