using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R153/K1 remediation: <c>DuplicateSheetCommand.RewriteClonedTableReferences</c> is a second
/// hand-written re-implementation of the same rewrite-loop shape R153 fixed in
/// <see cref="RowColumnShiftHelpers.RewriteAllFormulas"/> -- duplicating a sheet auto-renames a
/// cloned <see cref="StructuredTableModel"/> (<see cref="DuplicateSheetCommand"/>'s
/// UniquifyClonedTables), and the pass that rewrites <c>Table[...]</c> references to the new name
/// used to assign <c>cell.FormulaText</c> directly, discarding the array identity
/// <see cref="Sheet.Clone"/> had just correctly preserved verbatim from the source cell. Fixed by
/// routing through the same <see cref="RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity"/>
/// helper the R153 fix introduced (mirrors
/// <c>R153_LegacyArrayFormulaSurvivesStructuralRewriteTests</c> and
/// <c>R99_DuplicateSheetTableFormulaRewriteTests</c>).
/// </summary>
public sealed class R153_K1_DuplicateSheetTableRenameLegacyArrayTests
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
    public void DuplicateSheetCommand_TableRename_PreservesLegacyArrayFormulaIdentityOnClonedSheet()
    {
        var (wb, sheet, _) = CreateSheetWithTable();

        // A summary cell OUTSIDE the table body, CSE-entered as a 2x1 legacy fixed-extent array
        // formula that references the table by name (about to be renamed on the copy).
        var h1 = new CellAddress(sheet.Id, 10, 8);
        var legacyCell = Cell.FromFormula("SUM(Table1[Price])");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(h1, legacyCell);

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;
        copiedTable.Name.Should().NotBe("Table1"); // sanity: the uniquify rename actually happened

        var copiedFormulaCell = copy.GetCell(h1);
        copiedFormulaCell.Should().NotBeNull();
        copiedFormulaCell!.FormulaText.Should().Be($"SUM({copiedTable.Name}[Price])",
            "the copy's own formula must reference the copy's own renamed table");

        copiedFormulaCell.LegacyArrayRows.Should().Be(2u,
            "the table-rename rewrite on the duplicated sheet is not a fresh user edit and must not " +
            "strip the array's legacy fixed-extent identity Sheet.Clone had just correctly copied");
        copiedFormulaCell.LegacyArrayCols.Should().Be(1u);

        // Recalculate the copy so the legacy extent that survived on the Cell object is also
        // registered as a live spill anchor (TryGetArrayExtent/RejectIfSplitsArray consult the
        // recalc-populated spill overlay, not the Cell's LegacyArrayRows/Cols directly).
        var copyH1 = new CellAddress(copy.Id, 10, 8);
        var copyH2 = new CellAddress(copy.Id, 11, 8);
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [copyH1]);

        copy.TryGetArrayExtent(copyH2, out var anchor, out var rows, out var cols).Should().BeTrue(
            "H2 on the COPY must still be recognized as a declared array member after the rename rewrite");
        anchor.Should().Be(copyH1);
        rows.Should().Be(2u);
        cols.Should().Be(1u);

        CommandGuards.RejectIfSplitsArray(copy, [copyH2]).Should().NotBeNull(
            "'You cannot change part of an array' must still be enforced on the copy's surviving " +
            "non-anchor array member");

        // The SOURCE sheet's own cell must be completely untouched.
        var sourceCell = sheet.GetCell(h1)!;
        sourceCell.FormulaText.Should().Be("SUM(Table1[Price])");
        sourceCell.LegacyArrayRows.Should().Be(2u);
        sourceCell.LegacyArrayCols.Should().Be(1u);
    }

    /// <summary>No-regression sibling: an ordinary (non-array) formula referencing the renamed table
    /// is still rewritten exactly as R99 already pins, unaffected by routing the assignment through
    /// the preserving helper.</summary>
    [Fact]
    public void DuplicateSheetCommand_TableRename_OrdinaryFormulaStillRewritesAndStaysDynamic()
    {
        var (wb, sheet, _) = CreateSheetWithTable();
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), Cell.FromFormula("SUM(Table1[Price])"));

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;
        var copiedFormulaCell = copy.GetCell(10, 1)!;

        copiedFormulaCell.FormulaText.Should().Be($"SUM({copiedTable.Name}[Price])");
        copiedFormulaCell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        copiedFormulaCell.LegacyArrayRows.Should().Be(0u);
        copiedFormulaCell.LegacyArrayCols.Should().Be(0u);
    }
}
