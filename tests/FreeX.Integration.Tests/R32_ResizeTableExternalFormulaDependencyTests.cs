using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R32-calc-dependency-volatile-deep-3: ResizeStructuredTableCommand grows/shrinks a structured
/// table's Range, but an external formula that references the table via a structured reference
/// (e.g. D1=SUM(Table1[Amount])) had its dependency-graph edges registered against the table's
/// PRE-resize extent. Growing the table alone never touches that formula cell, so unless it is
/// surfaced in CommandOutcome.AffectedCells, the standard post-command pipeline
/// (WorkbookCellEditService.UpdateFormulaDependencies, driven off AffectedCells) never
/// re-registers it -- leaving it wired to the stale range, so editing a newly-added row would
/// never dirty/recalculate it.
/// </summary>
public sealed class R32_ResizeTableExternalFormulaDependencyTests
{
    // Table1 spans A1:A3 (A1 header "Amount"; data rows A2=10, A3=20). D1 holds the external
    // formula "=SUM(Table1[Amount])".
    private static (Workbook Workbook, Sheet Sheet, StructuredTableModel Table, CellAddress SumAddress) BuildWorkbook()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Columns = { new StructuredTableColumnModel(1, "Amount") }
        };
        sheet.StructuredTables.Add(table);

        var sumAddress = new CellAddress(sheet.Id, 1, 4);
        sheet.SetFormula(sumAddress, "SUM(Table1[Amount])");

        return (workbook, sheet, table, sumAddress);
    }

    // Mirrors WorkbookCellEditService.UpdateFormulaDependencies, the standard post-command
    // pipeline step that drives RecalcEngine off CommandOutcome.AffectedCells.
    private static void UpdateFormulaDependencies(Workbook workbook, RecalcEngine engine, IReadOnlyList<CellAddress> affectedCells)
    {
        foreach (var affected in affectedCells)
        {
            var cell = workbook.GetSheet(affected.Sheet)?.GetCell(affected);
            if (cell?.FormulaText is null)
            {
                engine.ClearFormulaDependencies(affected);
                continue;
            }

            engine.RegisterFormulaDependencies(
                affected, FormulaEvaluator.ParseFormula(cell.FormulaText), affected.Sheet, workbook);
        }
    }

    [Fact]
    public void ResizeTableLarger_ExternalSumFormulaRecalculatesAfterEditingNewRow()
    {
        var (workbook, sheet, table, sumAddress) = BuildWorkbook();
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);

        engine.RegisterFormulaDependencies(
            sumAddress, FormulaEvaluator.ParseFormula("SUM(Table1[Amount])"), sheet.Id, workbook);
        engine.Recalculate(workbook, [sumAddress]);
        sheet.GetValue(sumAddress).Should().Be(new NumberValue(30));

        // Grow the table by one data row (A1:A3 -> A1:A4).
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);
        var outcome = command.Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeTrue();

        // The fix under test: the external SUM formula must be surfaced as affected so the
        // standard pipeline re-registers its dependencies against the grown table.
        outcome.AffectedCells.Should().Contain(sumAddress,
            "a formula that references the resized table by name must have its dependency " +
            "registration refreshed, or it will never see edits to the newly-added row");

        UpdateFormulaDependencies(workbook, engine, outcome.AffectedCells!);
        engine.Recalculate(workbook, outcome.AffectedCells!);

        // Editing the newly-added row's cell must now dirty/recalculate the SUM formula --
        // this only happens if RegisterFormulaDependencies above re-resolved Table1[Amount]
        // against the grown A2:A4 range instead of the stale pre-resize A2:A3 range.
        var newRowAddress = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(newRowAddress, new NumberValue(5));
        var report = engine.Recalculate(workbook, [newRowAddress]);

        report.RecalculatedCells.Should().Contain(sumAddress,
            "the SUM formula's dependency edges must include the table's newly-added row, " +
            "not just its pre-resize extent");
        sheet.GetValue(sumAddress).Should().Be(new NumberValue(35));
    }

    // Sibling/already-working case: a formula that does not reference the resized table at all
    // (nor any structured reference) must not be swept into AffectedCells by the fix.
    [Fact]
    public void ResizeTableLarger_DoesNotSurfaceUnrelatedFormulasAsAffected()
    {
        var (workbook, sheet, table, sumAddress) = BuildWorkbook();

        var plainFormulaAddress = new CellAddress(sheet.Id, 1, 5);
        sheet.SetFormula(plainFormulaAddress, "10+5");

        var otherTableRefAddress = new CellAddress(sheet.Id, 1, 6);
        sheet.SetFormula(otherTableRefAddress, "SUM(OtherTable[X])");

        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);
        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Contain(sumAddress);
        outcome.AffectedCells.Should().NotContain(plainFormulaAddress,
            "a formula with no structured reference at all must not be swept into AffectedCells");
        outcome.AffectedCells.Should().NotContain(otherTableRefAddress,
            "a structured reference naming a different table must not be mistaken for a reference " +
            "to the resized table");
    }
}
