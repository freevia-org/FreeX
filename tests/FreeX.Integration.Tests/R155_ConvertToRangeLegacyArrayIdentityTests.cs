using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R155 (legacy-CSE-array-identity class, continued from R153/K1 and R154/M1): the last remaining
/// <c>cell.FormulaText = ...</c> assignment onto an EXISTING sheet cell in FreeX.Core.Commands lived
/// in <see cref="ConvertToRangeStructuredReferenceLowering"/>.LowerAllFormulas -- "Convert to Range"
/// lowering every structured reference into the converted table down to an absolute A1 reference.
/// The <see cref="Cell.FormulaText"/> setter unconditionally resets
/// ArrayMode/LegacyArrayRows/LegacyArrayCols to the "freshly authored modern formula" defaults, so a
/// legacy CSE array cell that merely happened to reference the converted table silently lost its
/// fixed extent -- and with it the replication of its non-anchor members and the "you cannot change
/// part of an array" protection -- even though the user never touched that formula. Fixed by routing
/// through the same <see cref="RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity"/> helper
/// the sibling rewrite loops use (the undo path, RestoreFormulas, already did).
/// </summary>
public sealed class R155_ConvertToRangeLegacyArrayIdentityTests
{
    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    /// <summary>Table1 spans Sheet1!A1:B3 — header row 1 (Item, Values), data rows 2-3.</summary>
    private static (Workbook Workbook, Sheet Sheet, StructuredTableModel Table) CreateSheetWithTable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Item"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Values"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet.Id, 1, 1, 3, 2),
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Item"),
                new StructuredTableColumnModel(2, "Values")
            }
        };
        sheet.StructuredTables.Add(table);
        return (workbook, sheet, table);
    }

    [Fact]
    public void ConvertToRange_FormulaIsLegacyArray_LowersReferenceButKeepsArrayIdentityAndGuard()
    {
        var (workbook, sheet, table) = CreateSheetWithTable();
        var context = new TestCommandContext(workbook);

        // D1:D2 (2x1) CSE-entered as {=SUM(Table1[Values])} — outside the table, referencing it.
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var d2 = new CellAddress(sheet.Id, 2, 4);
        var legacyCell = Cell.FromFormula("SUM(Table1[Values])");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(d1, legacyCell);

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        engine.RebuildFormulaDependencies(workbook);
        engine.Recalculate(workbook, [d1]);
        sheet.GetValue(d1).Should().Be(new NumberValue(30), "sanity before the conversion");
        sheet.GetValue(d2).Should().Be(new NumberValue(30), "sanity: D2 is replicated before the conversion");

        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);
        command.Apply(context).Success.Should().BeTrue();

        var d1Cell = sheet.GetCell(d1)!;
        d1Cell.FormulaText.Should().Be("SUM($B$2:$B$3)",
            "the structured reference must be lowered before the table model disappears");

        d1Cell.LegacyArrayRows.Should().Be(2u,
            "lowering a structured reference is not a fresh user edit of this cell and must not " +
            "strip its legacy fixed-extent array identity");
        d1Cell.LegacyArrayCols.Should().Be(1u);

        engine.RebuildFormulaDependencies(workbook);
        engine.Recalculate(workbook, [d1]);
        sheet.GetValue(d1).Should().Be(new NumberValue(30), "D1 still computes correctly");
        sheet.GetValue(d2).Should().Be(new NumberValue(30),
            "D2 must still be replicated instead of silently going blank now that LegacyArrayRows " +
            "survived Convert to Range");

        sheet.TryGetArrayExtent(d2, out var anchor, out var rows, out var cols).Should().BeTrue(
            "D2 must still be recognized as a declared array member after the conversion");
        anchor.Should().Be(d1);
        rows.Should().Be(2u);
        cols.Should().Be(1u);

        CommandGuards.RejectIfSplitsArray(sheet, [d2]).Should().NotBeNull(
            "'You cannot change part of an array' must still be enforced for the surviving " +
            "non-anchor array member after Convert to Range");

        // Undo (already routed through the preserving RestoreFormulas) puts the text back and keeps
        // the identity that Apply must no longer have destroyed.
        command.Revert(context);
        var revertedCell = sheet.GetCell(d1)!;
        revertedCell.FormulaText.Should().Be("SUM(Table1[Values])");
        revertedCell.LegacyArrayRows.Should().Be(2u);
        revertedCell.LegacyArrayCols.Should().Be(1u);
    }

    /// <summary>
    /// No-regression sibling: an ordinary (non-array) formula referencing the table is lowered
    /// exactly as R141/backlog already pin, and stays a plain modern Dynamic formula.
    /// </summary>
    [Fact]
    public void ConvertToRange_OrdinaryFormula_StillLowersAndStaysDynamic()
    {
        var (workbook, sheet, table) = CreateSheetWithTable();
        var context = new TestCommandContext(workbook);

        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetFormula(d1, "SUM(Table1[Values])");

        var command = new ConvertStructuredTableToRangeCommand(sheet.Id, table.Id);
        command.Apply(context).Success.Should().BeTrue();

        var d1Cell = sheet.GetCell(d1)!;
        d1Cell.FormulaText.Should().Be("SUM($B$2:$B$3)");
        d1Cell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        d1Cell.LegacyArrayRows.Should().Be(0u);
        d1Cell.LegacyArrayCols.Should().Be(0u);
    }
}
